using System.Collections.Concurrent;
using System.Globalization;

using BtVolumeGuard.Core.Logging;

using NAudio.CoreAudioApi;

namespace BtVolumeGuard.Core.Audio;

/// <summary>
/// Watches playback endpoints whose name matches a mask and logs every volume change together
/// with its event-context GUID, plus endpoint arrival, removal and state changes.
/// Diagnostic only (roadmap step 1): it never changes the volume.
/// </summary>
/// <remarks>
/// All Core Audio objects are created and used on one dedicated worker thread. Notification
/// callbacks arrive on Windows audio threads; they only capture their data and enqueue work.
/// The worker also re-enumerates endpoints and re-reads volumes periodically, in case a
/// notification is missed.
/// </remarks>
public sealed class EndpointVolumeMonitor : IDisposable
{
    private static readonly TimeSpan s_safetyPollInterval = TimeSpan.FromSeconds(2);

    private readonly DeviceNameMask _mask;
    private readonly ILog _log;
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Dictionary<string, WatchedEndpoint> _watched = [];
    private readonly Thread _worker;

    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _deviceNotifications;

    public EndpointVolumeMonitor(DeviceNameMask mask, ILog log)
    {
        _mask = mask;
        _log = log;
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
                    Execute(() => Synchronize(fromSafetyPoll: true));
                }
            }
        }
        catch (InvalidOperationException) when (_queue.IsAddingCompleted)
        {
            // Stopped while waiting.
        }
        catch (Exception ex)
        {
            _log.Write($"Monitor failed: {ex}");
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
        _log.Write($"Monitor started. Mask '{_mask}'. Diagnostic mode: volume is logged, never changed.");

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
        Synchronize(fromSafetyPoll: false);
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
        _log.Write("Monitor stopped.");
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

        Synchronize(fromSafetyPoll: false);
    }

    /// <summary>
    /// Starts watching matching endpoints that became active, stops watching those that are gone,
    /// and re-reads the volume of watched endpoints to catch missed notifications.
    /// </summary>
    private void Synchronize(bool fromSafetyPoll)
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
        private readonly EndpointVolumeMonitor _owner;

        private float _lastScalar;
        private bool _lastMuted;

        public WatchedEndpoint(string id, string name, MMDevice device, EndpointVolumeMonitor owner)
        {
            Id = id;
            Name = name;
            _device = device;
            _owner = owner;
            _volume = device.AudioEndpointVolume;
            _lastScalar = _volume.MasterVolumeLevelScalar;
            _lastMuted = _volume.Mute;
            _volume.OnVolumeNotification += OnVolumeNotification;
        }

        public string Id { get; }

        public string Name { get; }

        public string DescribeCurrent() => Describe(_lastScalar, _lastMuted);

        public void CheckForMissedChange(bool fromSafetyPoll)
        {
            float scalar = _volume.MasterVolumeLevelScalar;
            bool muted = _volume.Mute;
            if (VolumeLevel.ToPercent(scalar) == VolumeLevel.ToPercent(_lastScalar) && muted == _lastMuted)
            {
                return;
            }

            string source = fromSafetyPoll ? "safety poll" : "re-read";
            _owner._log.Write(
                $"[{Name}] volume {Describe(_lastScalar, _lastMuted)} -> {Describe(scalar, muted)} " +
                $"found by {source}, no notification");
            _lastScalar = scalar;
            _lastMuted = muted;
        }

        public void Dispose()
        {
            _volume.OnVolumeNotification -= OnVolumeNotification;
            _device.Dispose();
        }

        // Windows audio thread: capture the data and hand off to the worker.
        private void OnVolumeNotification(AudioVolumeNotificationData data)
        {
            Guid context = data.EventContext;
            float scalar = data.MasterVolume;
            bool muted = data.Muted;
            _owner.Enqueue(() => OnVolumeChanged(context, scalar, muted));
        }

        private void OnVolumeChanged(Guid context, float scalar, bool muted)
        {
            if (!_owner._watched.TryGetValue(Id, out WatchedEndpoint? current) || current != this)
            {
                return;
            }

            _owner._log.Write(
                $"[{Name}] volume {Describe(_lastScalar, _lastMuted)} -> {Describe(scalar, muted)}, context {context}");
            _lastScalar = scalar;
            _lastMuted = muted;
        }
    }
}
