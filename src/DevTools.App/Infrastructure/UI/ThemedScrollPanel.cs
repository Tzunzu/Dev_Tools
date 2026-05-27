using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DevTools.App.Infrastructure.UI;

internal sealed class ThemedScrollPanel : Panel
{
    private const int SB_BOTH = 3;

    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

    public ThemedScrollPanel()
    {
        AutoScroll = true;
        DoubleBuffered = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        HideNativeScrollBars();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        HideNativeScrollBars();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        HideNativeScrollBars();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        const int WM_SIZE = 0x0005;
        const int WM_HSCROLL = 0x0114;
        const int WM_VSCROLL = 0x0115;

        if (m.Msg is WM_SIZE or WM_HSCROLL or WM_VSCROLL)
        {
            HideNativeScrollBars();
        }
    }

    private void HideNativeScrollBars()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        ShowScrollBar(Handle, SB_BOTH, false);
    }
}
