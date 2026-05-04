using System.Runtime.Versioning;
using System.Windows.Forms;

namespace WorkTimeTracker.Agent.Native;

[SupportedOSPlatform("windows")]
internal sealed class WtsMessageWindow : NativeWindow, IDisposable
{
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    public event EventHandler<int>? SessionChange;

    public WtsMessageWindow()
    {
        var cp = new CreateParams
        {
            Caption = "WorkTimeTrackerWtsListener",
            Parent = HWND_MESSAGE
        };
        CreateHandle(cp);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_WTSSESSION_CHANGE)
        {
            SessionChange?.Invoke(this, m.WParam.ToInt32());
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }
}
