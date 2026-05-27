using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DevTools.App.Infrastructure.UI;

internal sealed class ThemedHorizontalScrollFlowPanel : FlowLayoutPanel
{
    private const int SB_BOTH = 3;
    private const int ScrollBarHeight = 12;
    private const int ScrollBarPadding = 2;
    private const int MinThumbWidth = 28;

    private bool dragActive;
    private int dragStartX;
    private int dragStartValue;

    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

    public ThemedHorizontalScrollFlowPanel()
    {
        AutoScroll = true;
        WrapContents = false;
        FlowDirection = FlowDirection.LeftToRight;
        Margin = new Padding(0);
        Padding = new Padding(0, 0, 0, ScrollBarHeight + (ScrollBarPadding * 2));

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        UpdateStyles();
    }

    private Rectangle TrackRectangle => new(
        ScrollBarPadding,
        Height - ScrollBarHeight - ScrollBarPadding,
        Math.Max(0, Width - (ScrollBarPadding * 2)),
        ScrollBarHeight);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        HideNativeScrollBars();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        HideNativeScrollBars();
        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        HideNativeScrollBars();
        Invalidate();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        HideNativeScrollBars();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!ShowCustomScrollBar())
        {
            return;
        }

        var delta = e.Delta > 0 ? -48 : 48;
        SetHorizontalOffset(GetHorizontalOffset() + delta);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || !ShowCustomScrollBar())
        {
            return;
        }

        var track = TrackRectangle;
        if (!track.Contains(e.Location))
        {
            return;
        }

        var thumb = GetThumbRectangle();
        if (thumb.Contains(e.Location))
        {
            dragActive = true;
            dragStartX = e.X;
            dragStartValue = GetHorizontalOffset();
            Capture = true;
            return;
        }

        var pageStep = Math.Max(40, HorizontalScroll.LargeChange - 12);
        var next = e.X < thumb.Left ? GetHorizontalOffset() - pageStep : GetHorizontalOffset() + pageStep;
        SetHorizontalOffset(next);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!dragActive || !ShowCustomScrollBar())
        {
            return;
        }

        var track = TrackRectangle;
        var thumb = GetThumbRectangle();
        var travel = Math.Max(1, track.Width - thumb.Width);
        var maxOffset = GetMaxHorizontalOffset();
        if (maxOffset <= 0)
        {
            return;
        }

        var deltaPixels = e.X - dragStartX;
        var deltaValue = (int)Math.Round((double)deltaPixels * maxOffset / travel);
        SetHorizontalOffset(dragStartValue + deltaValue);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButtons.Left)
        {
            dragActive = false;
            Capture = false;
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        // Keep native scrollbars hidden while layout/scroll messages flow.
        const int WM_SIZE = 0x0005;
        const int WM_HSCROLL = 0x0114;
        const int WM_VSCROLL = 0x0115;

        if (m.Msg is WM_SIZE or WM_HSCROLL or WM_VSCROLL)
        {
            HideNativeScrollBars();
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (!ShowCustomScrollBar())
        {
            return;
        }

        var track = TrackRectangle;
        using var trackBrush = new SolidBrush(GetTrackColor());
        using var borderPen = new Pen(GetBorderColor());
        e.Graphics.FillRectangle(trackBrush, track);
        e.Graphics.DrawRectangle(borderPen, track.X, track.Y, track.Width - 1, track.Height - 1);

        var thumb = GetThumbRectangle();
        using var thumbBrush = new SolidBrush(GetThumbColor());
        e.Graphics.FillRectangle(thumbBrush, thumb);
    }

    private bool ShowCustomScrollBar()
    {
        return GetMaxHorizontalOffset() > 0 && TrackRectangle.Width > MinThumbWidth;
    }

    private Rectangle GetThumbRectangle()
    {
        var track = TrackRectangle;
        var maxOffset = GetMaxHorizontalOffset();
        if (maxOffset <= 0 || track.Width <= 0)
        {
            return new Rectangle(track.X, track.Y, Math.Max(MinThumbWidth, track.Width), track.Height);
        }

        var visible = Math.Max(1, HorizontalScroll.LargeChange);
        var maximum = Math.Max(visible, HorizontalScroll.Maximum + 1);
        var thumbWidth = (int)Math.Round((double)track.Width * visible / maximum);
        thumbWidth = Math.Max(MinThumbWidth, Math.Min(track.Width, thumbWidth));

        var travel = track.Width - thumbWidth;
        var offset = GetHorizontalOffset();
        var thumbX = track.X + (int)Math.Round((double)offset * travel / maxOffset);

        return new Rectangle(thumbX, track.Y + 1, thumbWidth, track.Height - 2);
    }

    private void SetHorizontalOffset(int value)
    {
        var clamped = Math.Max(0, Math.Min(GetMaxHorizontalOffset(), value));
        AutoScrollPosition = new Point(clamped, 0);
        Invalidate();
    }

    private int GetHorizontalOffset()
    {
        return Math.Max(0, -AutoScrollPosition.X);
    }

    private int GetMaxHorizontalOffset()
    {
        return Math.Max(0, HorizontalScroll.Maximum - HorizontalScroll.LargeChange + 1);
    }

    private void HideNativeScrollBars()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        ShowScrollBar(Handle, SB_BOTH, false);
    }

    private static Color GetTrackColor()
    {
        return AppTheme.CurrentMode == ThemeMode.Dark
            ? Color.FromArgb(43, 43, 46)
            : Color.FromArgb(229, 231, 235);
    }

    private static Color GetThumbColor()
    {
        return AppTheme.CurrentMode == ThemeMode.Dark
            ? Color.FromArgb(87, 87, 94)
            : Color.FromArgb(156, 163, 175);
    }

    private static Color GetBorderColor()
    {
        return AppTheme.CurrentMode == ThemeMode.Dark
            ? Color.FromArgb(63, 63, 70)
            : Color.FromArgb(203, 213, 225);
    }
}
