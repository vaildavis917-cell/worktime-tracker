using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WorkTimeTracker.Agent.Native;

[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    public const int WM_WTSSESSION_CHANGE = 0x02B1;
    public const int WM_QUIT = 0x0012;

    public const int NOTIFY_FOR_THIS_SESSION = 0;
    public const int NOTIFY_FOR_ALL_SESSIONS = 1;

    // wParam values for WM_WTSSESSION_CHANGE
    public const int WTS_CONSOLE_CONNECT = 0x1;
    public const int WTS_CONSOLE_DISCONNECT = 0x2;
    public const int WTS_REMOTE_CONNECT = 0x3;
    public const int WTS_REMOTE_DISCONNECT = 0x4;
    public const int WTS_SESSION_LOGON = 0x5;
    public const int WTS_SESSION_LOGOFF = 0x6;
    public const int WTS_SESSION_LOCK = 0x7;
    public const int WTS_SESSION_UNLOCK = 0x8;
    public const int WTS_SESSION_REMOTE_CONTROL = 0x9;
    public const int WTS_SESSION_CREATE = 0xA;
    public const int WTS_SESSION_TERMINATE = 0xB;

    // WTS_INFO_CLASS values we care about
    public const int WTSUserName = 5;
    public const int WTSDomainName = 7;
    public const int WTSClientProtocolType = 16;

    public static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        int sessionId,
        int wtsInfoClass,
        out IntPtr ppBuffer,
        out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    public static extern void WTSFreeMemory(IntPtr pMemory);

    [DllImport("kernel32.dll")]
    public static extern int WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public int time;
        public int x;
        public int y;
    }

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, int wMsgFilterMin, int wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint idThread, int msg, IntPtr wParam, IntPtr lParam);

    public static string? QuerySessionString(int sessionId, int infoClass)
    {
        if (!WTSQuerySessionInformation(WTS_CURRENT_SERVER_HANDLE, sessionId, infoClass, out var buf, out var len))
        {
            return null;
        }
        try
        {
            return len > 0 ? Marshal.PtrToStringUni(buf) : null;
        }
        finally
        {
            WTSFreeMemory(buf);
        }
    }

    /// <summary>
    /// 0 = console / direct, 2 = RDP. -1 if query failed.
    /// </summary>
    public static int QuerySessionProtocolType(int sessionId)
    {
        if (!WTSQuerySessionInformation(WTS_CURRENT_SERVER_HANDLE, sessionId, WTSClientProtocolType, out var buf, out var len))
        {
            return -1;
        }
        try
        {
            if (len < sizeof(short))
            {
                return -1;
            }
            return Marshal.ReadInt16(buf);
        }
        finally
        {
            WTSFreeMemory(buf);
        }
    }
}
