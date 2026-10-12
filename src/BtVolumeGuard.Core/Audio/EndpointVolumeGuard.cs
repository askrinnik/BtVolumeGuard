using System.Collections.Concurrent;
using System.Globalization;

using BtVolumeGuard.Core.Logging;

using NAudio.CoreAudioApi;

namespace BtVolumeGuard.Core.Audio;

/// <summary>
/// Watches playback endpoints whose name matches a mask and reverts headset-induced jumps to
/// maximum volume (decisions by <see cref="EndpointGuard"/>). Logs endpoint arrival, removal,
/// state changes and every volume change with its event context and the decision taken.
/// </summary>
/// <remarks>
/// All Core Audio objects are created and used on one dedicated worker thread. Notification
/// callbacks arrive on Windows audio threads; they only capture their data and enqueue work.
/// The worker also re-enumerates endpoints and re-reads volumes periodically, in case a
/// notification is missed. Volumes remembered for endpoints that went away are kept in memory
/// only (until roadmap step 4).
/// </remarks>
public sealed class EndpointVolumeGuard : IDisposable
{
    private static readonly TimeSpan s_safetyPollInterval = TimeSpan.FromSeconds(2);

    private readonly DeviceNameMask _mask;
    private readonly VolumeJumpPolicy _policy;
    private readonly ILog _log;
    private readonly TimeProvider _time;
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Dictionary<string, WatchedEndpoint> _watched = [];
    private readonly Dictionary<string, int> _remembered = [];
    private readonly Thread _worker;

    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _deviceNotifications;

    public EndpointVolumeGuard(DeviceNameMask mask, VolumeJumpPolicy policy, ILog log, TimeProvider? time = null)
    {
        _mask = mask;
        _policy = policy;
        _log = log;
        _time = time ?? TimeProvider.System;
        _worker = new Thread(Run) { IsBackground = true, Name = "BtVolumeGuard audio" };
    }

    public void Start() => _worker.Start();

    public void Dispose()
    {
        if (!_worker.IsAlive)
        {
            return;
        }

        _queue.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(5));
    }

    private void Run()
    {
        try
        {
            Initialize();
            while (!_queue.IsCompleted)
            {
                if (_queue.TryTake(out Action? work, s_safetyPollInterval))
                {
                    Execute(work);
                }
                else if (!_queue.IsAddingCompleted)
                {
                    Execute(() => Synchronize(isStartup: false, fromSafetyPoll: true));
                }
            }
        }
        catch (InvalidOperationException) when (_queue.IsAddingCompleted)
        {
            // Stopped while waiting.
        }
        catch (Exception ex)
        {
            _log.Write($"Guard failed: {ex}");
        }
        finally
        {
            Shutdown();
        }
    }

    private void Execute(Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex)
        {
            _log.Write($"Error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Initialize()
    {
        _log.Write(
            $"Guard started. Mask '{_mask}', maximum >= {_policy.MaxThreshold}%, jump >= {_policy.MinJump}%, " +
            $"default {_policy.DefaultVolume}%, profile switch window {_policy.ProfileSwitchWindow.TotalSeconds:0.#} s.");

        _enumerator = new MMDeviceEnumerator();
        _deviceNotifications = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
        _deviceNotifications.DeviceAdded += (_, e) => Enqueue(() => OnDeviceEvent("added", e.DeviceId));
        _deviceNotifications.DeviceRemoved += (_, e) => Enqueue(() => OnDeviceEvent("removed", e.DeviceId));
        _deviceNotifications.DeviceStateChanged += (_, e) =>
            Enqueue(() => OnDeviceEvent($"state -> {e.NewState}", e.DeviceId));
        _deviceNotifications.DefaultDeviceChanged += (_, e) =>
        {
            if (e.Flow == DataFlow.Render)
            {
                Enqueue(() => OnDeviceEvent($"default {e.Role}", e.DeviceId));
            }
        };

        LogAllMatchingEndpoints();
        Synchronize(isStartup: true, fromSafetyPoll: false);
    }

    private void Shutdown()
    {
        foreach (WatchedEndpoint endpoint in _watched.Values)
        {
            endpoint.Dispose();
        }

        _watched.Clear();
        _deviceNotifications?.Dispose();
        _enumerator?.Dispose();
        _log.Write("Guard stopped.");
    }

    private void Enqueue(Action work)
    {
        try
        {
            _queue.Add(work);
        }
        catch (InvalidOperationException)
        {
            // Stopping; late notifications are dropped.
        }
    }

    /// <summary>Startup snapshot of every matching render endpoint in any state.</summary>
    private void LogAllMatchingEndpoints()
    {
        foreach (MMDevice device in _enumerator!.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All))
        {
            using (device)
            {
                string name = TryGetName(device);
                if (_mask.IsMatch(name))
                {
                    _log.Write($"Endpoint '{name}' state {device.State}, id {device.ID}");
                }
            }
        }
    }

    private void OnDeviceEvent(string what, string? deviceId)
    {
        if (deviceId is null)
        {
            _log.Write($"Render device event: {what}, no device");
            return;
        }

        string? name = _watched.TryGetValue(deviceId, out WatchedEndpoint? watched)
            ? watched.Name
            : TryGetNameById(deviceId);

        if (name is not null && _mask.IsMatch(name))
        {
            _log.Write($"[{name}] device {what}");
        }

        Synchronize(isStartup: false, fromSafetyPoll: false);
    }

    /// <summary>
    /// Starts watching matching endpoints that became active, stops watching those that are gone,
    /// and re-reads the volume of watched endpoints to catch missed notifications.
    /// </summary>
    private void Synchronize(bool isStartup, bool fromSafetyPoll)
    {
        var active = new HashSet<string>();

        foreach (MMDevice device in _enumerator!.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            string id = device.ID;
            string name = TryGetName(device);
            if (!_mask.IsMatch(name) || !active.Add(id))
            {
                device.Dispose();
                continue;
            }

            if (_watched.TryGetValue(id, out WatchedEndpoint? existing))
            {
                device.Dispose();
                existing.CheckForMissedChange(fromSafetyPoll);
                continue;
            }

            var endpoint = new WatchedEndpoint(id, name, device, this);
            _watched.Add(id, endpoint);
            _log.Write($"[{name}] watching, volume {endpoint.DescribeCurrent()}");

            if (!isStartup)
            {
                endpoint.OnAppeared(_remembered.TryGetValue(id, out int remembered) ? remembered : null);
            }
        }

        foreach (string id in _watched.Keys.Where(id => !active.Contains(id)).ToList())
        {
            WatchedEndpoint gone = _watched[id];
            _watched.Remove(id);
            gone.Dispose();
            _log.Write($"[{gone.Name}] no longer active, stopped watching");
        }
    }

    private string? TryGetNameById(string deviceId)
    {
        try
        {
            using MMDevice device = _enumerator!.GetDevice(deviceId);
            return TryGetName(device);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string TryGetName(MMDevice device)
    {
        try
        {
            return device.FriendlyName;
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static string Describe(float scalar, bool muted) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{VolumeLevel.ToPercent(scalar)}% ({scalar:0.0000}){(muted ? " muted" : "")}");

    /// <summary>An active endpoint subscribed to volume notifications. Used on the worker thread only.</summary>
    private sealed class WatchedEndpoint : IDisposable
    {
        private readonly MMDevice _device;
        private readonly AudioEndpointVolume _volume;
        private readonly EndpointVolumeGuard _owner;
        private readonly EndpointGuard _guard;

        private float _lastScalar;
        private bool _lastMuted;
        private (int Percent, bool Muted)? _pendingCheck;

        public WatchedEndpoint(string id, string name, MMDevice device, EndpointVolumeGuard owner)
        {
            Id = id;
            Name = name;
            _device = device;
            _owner = owner;
            _volume = device.AudioEndpointVolume;
            _volume.NotificationGuid = VolumeChangeContexts.Own;
            _lastScalar = _volume.MasterVolumeLevelScalar;
            _lastMuted = _volume.Mute;
            _guard = new EndpointGuard(owner._policy, VolumeLevel.ToPercent(_lastScalar));
            Remember();
            _volume.OnVolumeNotification += OnVolumeNotification;
        }

        public string Id { get; }

        public string Name { get; }

        public string DescribeCurrent() => Describe(_lastScalar, _lastMuted);

        public void OnAppeared(int? remembered)
        {
            if (_guard.OnAppeared(remembered) is { } target)
            {
                SetVolume(target, $"appeared at {DescribeCurrent()}");
            }
        }

        /// <summary>
        /// Reports a volume that differs from the last notified one only if it is seen on two
        /// checks in a row: the notification may simply be late, and it carries the context the
        /// guard needs to recognise a headset jump.
        /// </summary>
        public void CheckForMissedChange(bool fromSafetyPoll)
        {
            float scalar = _volume.MasterVolumeLevelScalar;
            bool muted = _volume.Mute;
            if (VolumeLevel.ToPercent(scalar) == VolumeLevel.ToPercent(_lastScalar) && muted == _lastMuted)
            {
                _pendingCheck = null;
                return;
            }

            if (_pendingCheck != (VolumeLevel.ToPercent(scalar), muted))
            {
                _pendingCheck = (VolumeLevel.ToPercent(scalar), muted);
                return;
            }

            string source = fromSafetyPoll ? "safety poll" : "re-read";
            Handle(VolumeChangeSource.Unknown, scalar, muted, _owner._time.GetUtcNow(), $"found by {source}, no notification");
        }

        public void Dispose()
        {
            _volume.OnVolumeNotification -= OnVolumeNotification;
            _device.Dispose();
        }

        // Windows audio thread: capture the data and hand off to the worker.
        private void OnVolumeNotification(AudioVolumeNotificationData data)
        {
            DateTimeOffset at = _owner._time.GetUtcNow();
            Guid context = data.EventContext;
            float scalar = data.MasterVolume;
            bool muted = data.Muted;
            _owner.Enqueue(() => OnVolumeChanged(context, scalar, muted, at));
        }

        private void OnVolumeChanged(Guid context, float scalar, bool muted, DateTimeOffset at)
        {
            if (!_owner._watched.TryGetValue(Id, out WatchedEndpoint? current) || current != this)
            {
                return;
            }

            VolumeChangeSource source = VolumeChangeContexts.Classify(context);
            Handle(source, scalar, muted, at, $"context {context} ({source})");
        }

        private void Handle(VolumeChangeSource source, float scalar, bool muted, DateTimeOffset at, string origin)
        {
            string change = $"[{Name}] volume {Describe(_lastScalar, _lastMuted)} -> {Describe(scalar, muted)}, {origin}";
            bool muteChanged = muted != _lastMuted;
            _lastScalar = scalar;
            _lastMuted = muted;
            _pendingCheck = null;

            GuardDecision decision = _guard.OnChange(source, VolumeLevel.ToPercent(scalar), at);
            switch (decision.Action)
            {
                case GuardAction.Revert:
                    _owner._log.Write($"{change}: {decision.Reason}");
                    SetVolume(decision.Target!.Value, "reverted");
                    break;

                case GuardAction.Accept:
                    _owner._log.Write(change);
                    Remember();
                    break;

                case GuardAction.Ignore when source == VolumeChangeSource.Own || muteChanged:
                    _owner._log.Write($"{change}: {decision.Reason}");
                    break;

                default:
                    // Same percent (driver echoes, duplicate notifications): not worth a log line.
                    break;
            }
        }

        private void SetVolume(int percent, string why)
        {
            _volume.MasterVolumeLevelScalar = percent / 100f;
            _lastScalar = percent / 100f;
            _owner._log.Write($"[{Name}] {why}: set volume {percent}%");
        }

        private void Remember()
        {
            if (_guard.Remembered is { } remembered)
            {
                _owner._remembered[Id] = remembered;
            }
        }
    }
}
