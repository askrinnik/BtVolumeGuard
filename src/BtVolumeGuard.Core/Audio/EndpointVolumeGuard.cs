using System.Collections.Concurrent;
using System.Globalization;

using BtVolumeGuard.Core.Logging;
using BtVolumeGuard.Core.Settings;

using NAudio.CoreAudioApi;

namespace BtVolumeGuard.Core.Audio;

/// <summary>
/// Watches the selected playback endpoints (by default every Bluetooth one, see
/// <see cref="DeviceSelection"/>) and reverts headset-induced jumps to maximum volume (decisions by <see cref="EndpointGuard"/>). Logs endpoint arrival, removal,
/// state changes and reverted jumps; at verbose level also every volume change with its event context.
/// </summary>
/// <remarks>
/// All Core Audio objects are created and used on one dedicated worker thread. Notification
/// callbacks arrive on Windows audio threads; they only capture their data and enqueue work.
/// The worker also re-enumerates endpoints and re-reads volumes periodically, in case a
/// notification is missed. The last volume below maximum per endpoint is kept in a
/// <see cref="VolumeMemory"/>, which survives restarts.
/// </remarks>
public sealed class EndpointVolumeGuard : IDisposable
{
    private static readonly TimeSpan s_safetyPollInterval = TimeSpan.FromSeconds(2);

    private readonly VolumeJumpPolicy _policy;
    private readonly VolumeChangeContexts _contexts;
    private readonly ILog _log;
    private readonly TimeProvider _time;
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Dictionary<string, WatchedEndpoint> _watched = [];
    private readonly VolumeMemory _memory;
    private readonly Dictionary<string, bool> _isBluetooth = [];
    private readonly Thread _worker;

    private volatile DeviceSelection _selection;
    private volatile KnownDevice[] _devices = [];

    private volatile bool _enabled = true;
    private volatile EndpointStatus[] _status = [];

    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _deviceNotifications;

    public EndpointVolumeGuard(
        DeviceSelection selection,
        VolumeJumpPolicy policy,
        VolumeChangeContexts contexts,
        VolumeMemory memory,
        ILog log,
        TimeProvider? time = null)
    {
        _selection = selection;
        _policy = policy;
        _contexts = contexts;
        _memory = memory;
        _log = log;
        _time = time ?? TimeProvider.System;
        _worker = new Thread(Run) { IsBackground = true, Name = "BtVolumeGuard audio" };
    }

    /// <summary>Raised on the worker thread after the guard reverted a jump.</summary>
    public event Action<RevertInfo>? Reverted;

    /// <summary>Raised on the worker thread when <see cref="Status"/> or <see cref="Devices"/> changes.</summary>
    public event Action? StatusChanged;

    /// <summary>
    /// When off, changes are only remembered (and logged at verbose level), never reverted. Can be set from any thread.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            if (_worker.IsAlive)
            {
                _log.Write(value ? "Guard enabled." : "Guard disabled: jumps are not reverted.");
            }
        }
    }

    /// <summary>Watched endpoints and their volume; safe to read from any thread.</summary>
    public IReadOnlyList<EndpointStatus> Status => _status;

    /// <summary>Devices offered in the tray menu, connected or not; safe to read from any thread.</summary>
    public IReadOnlyList<KnownDevice> Devices => _devices;

    public DeviceSelection Selection => _selection;

    /// <summary>Changes which devices are guarded; applied on the worker thread. Can be called from any thread.</summary>
    public void SetSelection(DeviceSelection selection) =>
        Enqueue(() =>
        {
            _selection = selection;
            _log.Write($"Devices: {selection}.");
            RefreshDevices(logEndpoints: false);
            Synchronize(isStartup: true, fromSafetyPoll: false);
        });

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
            $"Guard started. Devices: {_selection}. Maximum >= {_policy.MaxThreshold}%, jump >= {_policy.MinJump}%, " +
            $"default {_policy.DefaultVolume}%, profile switch window {_policy.ProfileSwitchWindow.TotalSeconds:0.#} s." +
            (_enabled ? "" : " Guard is OFF: jumps are not reverted."));

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

        RefreshDevices(logEndpoints: true);
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

    /// <summary>
    /// Rebuilds <see cref="Devices"/>: every candidate render endpoint in any state (so that a
    /// switched-off headset stays in the menu), one entry per friendly name.
    /// </summary>
    private void RefreshDevices(bool logEndpoints)
    {
        DeviceSelection selection = _selection;
        var byName = new Dictionary<string, (bool Connected, bool IsBluetooth)>(StringComparer.OrdinalIgnoreCase);

        foreach (MMDevice device in _enumerator!.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All))
        {
            using (device)
            {
                string name = TryGetName(device);
                bool isBluetooth = IsBluetooth(device);
                if (name.Length == 0 || !selection.IsCandidate(name, isBluetooth))
                {
                    continue;
                }

                DeviceState state = TryGetState(device);
                if (logEndpoints)
                {
                    _log.Write($"Endpoint '{name}' {ShortId(device.ID)}: {state}{(isBluetooth ? ", Bluetooth" : "")}");
                }

                bool connected = state == DeviceState.Active;
                byName[name] = byName.TryGetValue(name, out var known)
                    ? (known.Connected || connected, isBluetooth)
                    : (connected, isBluetooth);
            }
        }

        KnownDevice[] devices =
        [
            .. byName
                .Select(d => new KnownDevice(d.Key, d.Value.Connected, selection.IsGuarded(d.Key, d.Value.IsBluetooth)))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase),
        ];

        if (!devices.SequenceEqual(_devices))
        {
            _devices = devices;
            StatusChanged?.Invoke();
        }
    }

    /// <summary>Whether the endpoint belongs to a Bluetooth device. Cached per endpoint ID: it never changes.</summary>
    private bool IsBluetooth(MMDevice device)
    {
        string id = device.ID;
        if (_isBluetooth.TryGetValue(id, out bool cached))
        {
            return cached;
        }

        bool isBluetooth = BluetoothEndpoint.IsBluetooth(
            TryGetString(device, s_enumeratorNameKey), TryGetString(device, s_associationKey));
        _isBluetooth[id] = isBluetooth;
        return isBluetooth;
    }

    // PKEY_Device_EnumeratorName: BTHENUM / BTHHFENUM for classic Bluetooth endpoints.
    private static readonly PropertyKey s_enumeratorNameKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 24);

    // Undocumented endpoint property holding the path of the associated device node; for Bluetooth
    // audio routed through Intel Smart Sound Technology it is BTHENUM\{0000110B-...}\... (observed).
    private static readonly PropertyKey s_associationKey = new(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 39);

    private static string? TryGetString(MMDevice device, PropertyKey key)
    {
        try
        {
            PropertyStore properties = device.Properties;
            return properties.Contains(key) ? properties[key].Value as string : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DeviceState TryGetState(MMDevice device)
    {
        try
        {
            return device.State;
        }
        catch (Exception)
        {
            return DeviceState.NotPresent;
        }
    }

    private void OnDeviceEvent(string what, string? deviceId)
    {
        if (deviceId is null)
        {
            _log.Verbose($"Render device event: {what}, no device");
            return;
        }

        // Raw Windows events are noisy: a Bluetooth disconnect walks every endpoint of the headset
        // (playback, microphone, stale entries from earlier pairings) through several states.
        // Normal level only gets the outcome from Synchronize: "watching" / "stopped watching".
        if (TryDescribe(deviceId) is { } d && _selection.IsCandidate(d.Name, d.IsBluetooth))
        {
            _log.Verbose($"[{d.Name}] {d.Flow.ToString().ToLowerInvariant()} {ShortId(deviceId)}: device {what}");
        }

        RefreshDevices(logEndpoints: false);
        Synchronize(isStartup: false, fromSafetyPoll: false);
    }

    /// <summary>
    /// Starts watching guarded endpoints that became active, stops watching those that are gone or
    /// no longer guarded, and re-reads the volume of watched endpoints to catch missed notifications.
    /// </summary>
    /// <param name="isStartup">
    /// True at startup and when the selection changes: endpoints found then are not "appearing",
    /// so their volume is not corrected.
    /// </param>
    private void Synchronize(bool isStartup, bool fromSafetyPoll)
    {
        DeviceSelection selection = _selection;
        var active = new HashSet<string>();
        var guarded = new HashSet<string>();

        foreach (MMDevice device in _enumerator!.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            string id = device.ID;
            string name = TryGetName(device);
            active.Add(id);
            if (!selection.IsGuarded(name, IsBluetooth(device)) || !guarded.Add(id))
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
            _log.Write($"[{name}] {ShortId(id)}: connected, watching, volume {endpoint.DescribeCurrent()}");

            if (!isStartup)
            {
                endpoint.OnAppeared(_memory.Get(id));
            }
        }

        foreach (string id in _watched.Keys.Where(id => !guarded.Contains(id)).ToList())
        {
            WatchedEndpoint gone = _watched[id];
            _watched.Remove(id);
            gone.Dispose();
            string reason = active.Contains(id) ? "no longer guarded" : "disconnected";
            _log.Write($"[{gone.Name}] {ShortId(id)}: {reason}, stopped watching");
        }

        PublishStatus();
    }

    private void PublishStatus()
    {
        EndpointStatus[] status = [.. _watched.Values.Select(e => e.ToStatus()).OrderBy(s => s.Name)];
        if (status.SequenceEqual(_status))
        {
            return;
        }

        _status = status;
        StatusChanged?.Invoke();
    }

    private (string Name, DataFlow Flow, bool IsBluetooth)? TryDescribe(string deviceId)
    {
        try
        {
            using MMDevice device = _enumerator!.GetDevice(deviceId);
            return (TryGetName(device), device.DataFlow, IsBluetooth(device));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>First block of the endpoint GUID, e.g. <c>aa9b8faf</c>: enough to tell endpoints apart in the log.</summary>
    private static string ShortId(string deviceId)
    {
        string guid = deviceId[(deviceId.LastIndexOf('{') + 1)..];
        int dash = guid.IndexOf('-');
        return dash > 0 ? guid[..dash] : guid.TrimEnd('}');
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

            VolumeChangeSource source = _owner._contexts.Classify(context);
            Handle(source, scalar, muted, at, $"context {context} ({source})");
        }

        private void Handle(VolumeChangeSource source, float scalar, bool muted, DateTimeOffset at, string origin)
        {
            string change = $"[{Name}] volume {Describe(_lastScalar, _lastMuted)} -> {Describe(scalar, muted)}, {origin}";
            bool muteChanged = muted != _lastMuted;
            _lastScalar = scalar;
            _lastMuted = muted;
            _pendingCheck = null;

            // While the guard is off every change is trusted, so the state stays in sync for when it is back on.
            if (!_owner._enabled && source != VolumeChangeSource.Own)
            {
                source = VolumeChangeSource.User;
                change += " [guard off]";
            }

            GuardDecision decision = _guard.OnChange(source, VolumeLevel.ToPercent(scalar), at);
            switch (decision.Action)
            {
                case GuardAction.Revert:
                    _owner._log.Write($"{change}: {decision.Reason}");
                    SetVolume(decision.Target!.Value, "reverted");
                    _owner.Reverted?.Invoke(new RevertInfo(Name, VolumeLevel.ToPercent(scalar), decision.Target.Value));
                    break;

                case GuardAction.Accept:
                    _owner._log.Verbose(change);
                    Remember();
                    break;

                case GuardAction.Ignore when source == VolumeChangeSource.Own || muteChanged:
                    _owner._log.Verbose($"{change}: {decision.Reason}");
                    break;

                default:
                    // Same percent (driver echoes, duplicate notifications): not worth a log line.
                    break;
            }

            _owner.PublishStatus();
        }

        public EndpointStatus ToStatus() => new(Name, _guard.Volume, _lastMuted);

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
                if (_owner._memory.Set(Id, remembered) is { } problem)
                {
                    _owner._log.Write(problem);
                }
            }
        }
    }
}
