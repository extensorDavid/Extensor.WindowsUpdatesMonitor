using System.Runtime.InteropServices;

namespace Extensor.WindowsUpdatesMonitor;

public sealed record UserSession(int SessionId, string UserName);

/// <summary>Finds logged-in user sessions and pops message boxes in them via the WTS API.</summary>
public sealed class SessionMessenger
{
    private readonly ILogger<SessionMessenger> _logger;

    public SessionMessenger(ILogger<SessionMessenger> logger) => _logger = logger;

    /// <summary>Sessions in the WTSActive state that have a user logged in.</summary>
    public List<UserSession> GetActiveSessions()
    {
        var sessions = new List<UserSession>();
        if (!WTSEnumerateSessions(WTS_CURRENT_SERVER_HANDLE, 0, 1, out var buffer, out int count))
        {
            _logger.LogError("WTSEnumerateSessions failed (error {Error})", Marshal.GetLastWin32Error());
            return sessions;
        }

        try
        {
            int size = Marshal.SizeOf<WTS_SESSION_INFO>();
            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<WTS_SESSION_INFO>(buffer + i * size);
                if (info.State != WTS_CONNECTSTATE_CLASS.WTSActive)
                    continue;

                string user = QueryString(info.SessionId, WTS_INFO_CLASS.WTSUserName);
                if (string.IsNullOrEmpty(user))
                    continue;

                string domain = QueryString(info.SessionId, WTS_INFO_CLASS.WTSDomainName);
                sessions.Add(new UserSession(info.SessionId, string.IsNullOrEmpty(domain) ? user : $@"{domain}\{user}"));
            }
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
        return sessions;
    }

    /// <summary>Shows an OK-only message box in the session without waiting for the user to respond.</summary>
    public bool Send(int sessionId, string title, string body, TimeSpan timeout)
    {
        bool ok = WTSSendMessage(WTS_CURRENT_SERVER_HANDLE, sessionId,
            title, title.Length * 2, body, body.Length * 2,
            MB_OK | MB_ICONWARNING | MB_SETFOREGROUND | MB_TOPMOST,
            (int)timeout.TotalSeconds, out _, bWait: false);

        if (!ok)
            _logger.LogError("WTSSendMessage to session {SessionId} failed (error {Error})", sessionId, Marshal.GetLastWin32Error());
        return ok;
    }

    private static string QueryString(int sessionId, WTS_INFO_CLASS infoClass)
    {
        if (!WTSQuerySessionInformation(WTS_CURRENT_SERVER_HANDLE, sessionId, infoClass, out var buffer, out _))
            return "";
        try { return Marshal.PtrToStringUni(buffer) ?? ""; }
        finally { WTSFreeMemory(buffer); }
    }

    // ---- P/Invoke ----

    private static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;

    private const int MB_OK = 0x0;
    private const int MB_ICONWARNING = 0x30;
    private const int MB_SETFOREGROUND = 0x10000;
    private const int MB_TOPMOST = 0x40000;

    private enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive, WTSConnected, WTSConnectQuery, WTSShadow, WTSDisconnected,
        WTSIdle, WTSListen, WTSReset, WTSDown, WTSInit,
    }

    private enum WTS_INFO_CLASS
    {
        WTSUserName = 5,
        WTSDomainName = 7,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WTS_SESSION_INFO
    {
        public int SessionId;
        public IntPtr pWinStationName;
        public WTS_CONNECTSTATE_CLASS State;
    }

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WTSEnumerateSessionsW")]
    private static extern bool WTSEnumerateSessions(IntPtr hServer, int reserved, int version,
        out IntPtr ppSessionInfo, out int pCount);

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WTSQuerySessionInformationW")]
    private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, WTS_INFO_CLASS infoClass,
        out IntPtr ppBuffer, out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WTSSendMessageW")]
    private static extern bool WTSSendMessage(IntPtr hServer, int sessionId,
        string pTitle, int titleLength, string pMessage, int messageLength,
        int style, int timeout, out int pResponse, bool bWait);
}
