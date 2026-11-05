namespace SnoozeIt.Core.Native;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
/// Detects processes currently outputting audio via Windows Core Audio (WASAPI).
/// Used by HeuristicEngine to apply the P_audio (80 pts) penalty so music/calls are not muted.
/// </summary>
public static class AudioDetector
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    private const int eRender = 0;
    private const int eMultimedia = 1;
    private const int AudioSessionStateActive = 1;

    /// <summary>
    /// Returns the set of process IDs that currently have active audio playback streams.
    /// </summary>
    public static HashSet<int> GetActiveAudioProcessIds()
    {
        var activePids = new HashSet<int>();

        try
        {
            Type? enumeratorType = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
            if (enumeratorType == null) return activePids;

            var deviceEnumerator = (IMMDeviceEnumerator?)Activator.CreateInstance(enumeratorType);
            if (deviceEnumerator == null) return activePids;

            int hr = deviceEnumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out IMMDevice defaultDevice);
            if (hr != 0 || defaultDevice == null) return activePids;

            var sessionManagerGuid = IID_IAudioSessionManager2;
            hr = defaultDevice.Activate(ref sessionManagerGuid, 0, IntPtr.Zero, out object sessionManagerObj);
            if (hr != 0 || sessionManagerObj is not IAudioSessionManager2 sessionManager) return activePids;

            hr = sessionManager.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
            if (hr != 0 || sessionEnumerator == null) return activePids;

            hr = sessionEnumerator.GetCount(out int sessionCount);
            if (hr != 0) return activePids;

            for (int i = 0; i < sessionCount; i++)
            {
                hr = sessionEnumerator.GetSession(i, out IAudioSessionControl sessionControl);
                if (hr != 0 || sessionControl == null) continue;

                if (sessionControl is IAudioSessionControl2 sessionControl2)
                {
                    hr = sessionControl2.GetState(out int state);
                    if (hr == 0 && state == AudioSessionStateActive)
                    {
                        hr = sessionControl2.GetProcessId(out uint pid);
                        if (hr == 0 && pid > 0)
                        {
                            activePids.Add((int)pid);
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback: if audio service is disabled or in headless VM, fail-safe to empty set
        }

        return activePids;
    }

    // ── COM Interface Declarations ───────────────────────────────────────────

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        int GetDevice(string id, out IntPtr device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(int stgmAccess, out IntPtr properties);
        int GetId(out IntPtr strId);
        int GetState(out int state);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int GetAudioSessionControl(IntPtr audioSessionGuid, int streamFlags, out IntPtr sessionControl);
        int GetSimpleAudioVolume(IntPtr audioSessionGuid, int streamFlags, out IntPtr audioVolume);
        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionList);
        int RegisterSessionNotification(IntPtr newSession);
        int UnregisterSessionNotification(IntPtr newSession);
        int RegisterDuckNotification(string sessionID, IntPtr duckNotification);
        int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport]
    [Guid("E2F5EE11-070C-401E-A504-9E56DB600579")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int sessionCount);
        [PreserveSig]
        int GetSession(int sessionIndex, out IAudioSessionControl session);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig]
        int GetState(out int state);
        int GetDisplayName(out IntPtr displayName);
        int SetDisplayName(string displayName, IntPtr eventContext);
        int GetIconPath(out IntPtr iconPath);
        int SetIconPath(string iconPath, IntPtr eventContext);
        int GetGroupingParam(out Guid groupingParam);
        int SetGroupingParam(ref Guid groupingParam, IntPtr eventContext);
        int RegisterAudioSessionNotification(IntPtr client);
        int UnregisterAudioSessionNotification(IntPtr client);
    }

    [ComImport]
    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2 : IAudioSessionControl
    {
        [PreserveSig]
        new int GetState(out int state);
        new int GetDisplayName(out IntPtr displayName);
        new int SetDisplayName(string displayName, IntPtr eventContext);
        new int GetIconPath(out IntPtr iconPath);
        new int SetIconPath(string iconPath, IntPtr eventContext);
        new int GetGroupingParam(out Guid groupingParam);
        new int SetGroupingParam(ref Guid groupingParam, IntPtr eventContext);
        new int RegisterAudioSessionNotification(IntPtr client);
        new int UnregisterAudioSessionNotification(IntPtr client);

        [PreserveSig]
        int GetSessionIdentifier(out IntPtr retVal);
        [PreserveSig]
        int GetSessionInstanceIdentifier(out IntPtr retVal);
        [PreserveSig]
        int GetProcessId(out uint retVal);
        [PreserveSig]
        int IsSystemSoundsSession();
        [PreserveSig]
        int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }
}

