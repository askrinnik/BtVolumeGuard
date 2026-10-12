<#
.SYNOPSIS
    Reverts sudden jumps to maximum volume on Bluetooth headset endpoints.

.DESCRIPTION
    The LK12 headset reports maximum volume when the Hands-Free (HFP) profile
    starts (e.g. when a Teams meeting opens the microphone), and Windows sets
    the endpoint volume to 100%. This script polls the volume of matching
    playback endpoints and, when it jumps to the maximum in one step, restores
    the previous value.

    Works in Windows PowerShell 5.1 and PowerShell 7. No admin rights needed.

.PARAMETER DeviceName
    Wildcard pattern for playback endpoint names to guard.

.PARAMETER DefaultVolume
    Volume (%) to set when a guarded endpoint appears already at maximum and
    no earlier volume is known for it (first call after the script started).
    On later calls the volume remembered from the previous call is used.

.PARAMETER MaxThreshold
    Volume (%) at or above which a value counts as "maximum".

.PARAMETER MinJump
    Minimum single-step increase (%) treated as a jump rather than a manual change.

.PARAMETER PollMs
    Polling interval in milliseconds.

.PARAMETER LogFile
    Log file path. Empty string disables logging to file.

.PARAMETER ListDevices
    Print active playback endpoints with their volume and exit.

.EXAMPLE
    .\LK12-VolumeGuard.ps1 -ListDevices

.EXAMPLE
    .\LK12-VolumeGuard.ps1 -DefaultVolume 20
#>
[CmdletBinding()]
param(
    [string]$DeviceName = '*LK12*',
    [ValidateRange(1, 100)][int]$DefaultVolume = 20,
    [ValidateRange(50, 100)][int]$MaxThreshold = 95,
    [ValidateRange(1, 100)][int]$MinJump = 20,
    [ValidateRange(20, 5000)][int]$PollMs = 100,
    [string]$LogFile = (Join-Path $env:LOCALAPPDATA 'LK12-VolumeGuard\VolumeGuard.log'),
    [switch]$ListDevices
)

$ErrorActionPreference = 'Stop'

# C# 5 syntax only: Windows PowerShell 5.1 compiles Add-Type code with the legacy compiler.
$source = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LK12VolumeGuard
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointer;
    }

    public class Endpoint
    {
        private readonly IAudioEndpointVolume volume;
        public string Id { get; private set; }
        public string Name { get; private set; }

        internal Endpoint(string id, string name, IAudioEndpointVolume volume)
        {
            Id = id;
            Name = name;
            this.volume = volume;
        }

        // Volume in percent (0..100).
        public int GetVolume()
        {
            float level;
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out level));
            return (int)Math.Round(level * 100);
        }

        public void SetVolume(int percent)
        {
            Guid context = Guid.Empty;
            Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(percent / 100f, ref context));
        }
    }

    public static class Audio
    {
        const int eRender = 0;
        const int DEVICE_STATE_ACTIVE = 1;
        const int CLSCTX_ALL = 23;
        const int STGM_READ = 0;
        const ushort VT_LPWSTR = 31;

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PropVariant value);

        public static Endpoint[] GetActiveRenderEndpoints()
        {
            var result = new List<Endpoint>();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDeviceCollection devices;
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out devices));
            uint count;
            Marshal.ThrowExceptionForHR(devices.GetCount(out count));
            for (uint i = 0; i < count; i++)
            {
                IMMDevice device;
                if (devices.Item(i, out device) != 0) continue;
                string id;
                if (device.GetId(out id) != 0) continue;

                Guid iid = typeof(IAudioEndpointVolume).GUID;
                object iface;
                if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out iface) != 0) continue;

                result.Add(new Endpoint(id, GetFriendlyName(device), (IAudioEndpointVolume)iface));
            }
            return result.ToArray();
        }

        static string GetFriendlyName(IMMDevice device)
        {
            IPropertyStore store;
            if (device.OpenPropertyStore(STGM_READ, out store) != 0) return "";
            // PKEY_Device_FriendlyName
            var key = new PropertyKey { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
            PropVariant value;
            if (store.GetValue(ref key, out value) != 0) return "";
            try
            {
                return value.vt == VT_LPWSTR ? Marshal.PtrToStringUni(value.pointer) : "";
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
    }
}
'@

if (-not ('LK12VolumeGuard.Audio' -as [type])) {
    Add-Type -TypeDefinition $source -Language CSharp
}

function Write-Log([string]$message) {
    $line = '{0:yyyy-MM-dd HH:mm:ss.fff}  {1}' -f (Get-Date), $message
    Write-Host $line
    if ($LogFile) {
        try { Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8 } catch { }
    }
}

if ($ListDevices) {
    [LK12VolumeGuard.Audio]::GetActiveRenderEndpoints() |
        ForEach-Object {
            [pscustomobject]@{
                Name    = $_.Name
                Volume  = $_.GetVolume()
                Guarded = $_.Name -like $DeviceName
            }
        } | Format-Table -AutoSize
    return
}

# Single instance per user session.
$mutex = New-Object System.Threading.Mutex($false, 'Local\LK12-VolumeGuard')
if (-not $mutex.WaitOne(0)) {
    Write-Host 'LK12-VolumeGuard is already running.'
    return
}

if ($LogFile) {
    New-Item -ItemType Directory -Force -Path (Split-Path $LogFile) | Out-Null
}

Write-Log ("Started. DeviceName='{0}' DefaultVolume={1} MaxThreshold={2} MinJump={3} PollMs={4}" -f `
    $DeviceName, $DefaultVolume, $MaxThreshold, $MinJump, $PollMs)

$lastVolume = @{}          # endpoint id -> last accepted volume (%), for currently active endpoints
$remembered = @{}          # endpoint id -> last accepted volume (%), kept after the endpoint goes away
$endpoints = @()
$nextRefresh = [DateTime]::MinValue
# The Hands-Free endpoint becomes active only during a call, so look for new endpoints often.
$refreshInterval = [TimeSpan]::FromMilliseconds(300)

try {
    while ($true) {
        # Endpoints come and go when the headset connects/disconnects, so re-enumerate periodically.
        if ([DateTime]::UtcNow -ge $nextRefresh) {
            try {
                $endpoints = @([LK12VolumeGuard.Audio]::GetActiveRenderEndpoints() | Where-Object { $_.Name -like $DeviceName })
            } catch {
                $endpoints = @()
            }
            $activeIds = @($endpoints | ForEach-Object { $_.Id })
            foreach ($id in @($lastVolume.Keys)) {
                if ($activeIds -notcontains $id) { $lastVolume.Remove($id) }
            }
            $nextRefresh = [DateTime]::UtcNow + $refreshInterval
        }

        foreach ($ep in $endpoints) {
            try {
                $current = $ep.GetVolume()

                if (-not $lastVolume.ContainsKey($ep.Id)) {
                    if ($current -ge $MaxThreshold) {
                        $target = if ($remembered.ContainsKey($ep.Id)) { $remembered[$ep.Id] } else { $DefaultVolume }
                        $ep.SetVolume($target)
                        Write-Log ("[{0}] appeared at {1}% -> set {2}%" -f $ep.Name, $current, $target)
                        $current = $target
                    } else {
                        Write-Log ("[{0}] watching, volume {1}%" -f $ep.Name, $current)
                    }
                    $lastVolume[$ep.Id] = $current
                    $remembered[$ep.Id] = $current
                    continue
                }

                $previous = $lastVolume[$ep.Id]
                if ($current -ge $MaxThreshold -and $previous -lt $MaxThreshold -and ($current - $previous) -ge $MinJump) {
                    $ep.SetVolume($previous)
                    Write-Log ("[{0}] jump {1}% -> {2}%, restored {1}%" -f $ep.Name, $previous, $current)
                    continue
                }

                $lastVolume[$ep.Id] = $current
                if ($current -lt $MaxThreshold) { $remembered[$ep.Id] = $current }
            } catch {
                # Endpoint vanished (headset disconnected); re-enumerate on next tick.
                $nextRefresh = [DateTime]::MinValue
            }
        }

        Start-Sleep -Milliseconds $PollMs
    }
} finally {
    Write-Log 'Stopped.'
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
