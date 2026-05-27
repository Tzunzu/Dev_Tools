using System;
using System.Drawing;
using System.Windows.Forms;

namespace DevTools.App.Infrastructure.UI;

internal sealed class ThemedDataGridView : DataGridView
{
    private const int ScrollBarThickness = 12;
    private const int ScrollPadding = 2;
    private const int MinThumbSize = 24;

    private bool draggingVertical;
    private bool draggingHorizontal;
    private int dragStart;
    private int dragStartValue;

    public ThemedDataGridView()
    {
        DoubleBuffered = true;
        ScrollBars = ScrollBars.None;
    }

    protected override void OnRowsAdded(DataGridViewRowsAddedEventArgs e)
    {
        base.OnRowsAdded(e);
        Invalidate();
    }

    protected override void OnRowsRemoved(DataGridViewRowsRemovedEventArgs e)
    {
        base.OnRowsRemoved(e);
        Invalidate();
    }

    protected override void OnColumnWidthChanged(DataGridViewColumnEventArgs e)
    {
        base.OnColumnWidthChanged(e);
        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Invalidate();
    }

    protected override void OnScroll(ScrollEventArgs e)
    {
        base.OnScroll(e);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (TryScrollRows(e.Delta > 0 ? -3 : 3))
        {
            return;
        }

        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        var vTrack = GetVerticalTrackBounds();
        if (vTrack.Contains(e.Location) && HasVerticalScroll())
        {
            var thumb = GetVerticalThumbBounds(vTrack);
            if (thumb.Contains(e.Location))
            {
                draggingVertical = true;
                dragStart = e.Y;
                dragStartValue = GetFirstVisibleRowIndex();
                Capture = true;
                return;
            }

            var page = Math.Max(1, GetDisplayedRowCount() - 1);
            SetFirstVisibleRowIndex(e.Y < thumb.Top ? dragStartValue - page : dragStartValue + page);
            return;
        }

        var hTrack = GetHorizontalTrackBounds();
        if (hTrack.Contains(e.Location) && HasHorizontalScroll())
        {
            var thumb = GetHorizontalThumbBounds(hTrack);
            if (thumb.Contains(e.Location))
            {
                draggingHorizontal = true;
                dragStart = e.X;
                dragStartValue = HorizontalScrollingOffset;
                Capture = true;
                return;
            }

            var page = Math.Max(32, hTrack.Width / 2);
            SetHorizontalOffset(e.X < thumb.Left ? dragStartValue - page : dragStartValue + page);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (draggingVertical && HasVerticalScroll())
        {
            var track = GetVerticalTrackBounds();
            var thumb = GetVerticalThumbBounds(track);
            var travel = Math.Max(1, track.Height - thumb.Height);
            var max = GetMaxVerticalValue();
            if (max > 0)
            {
                var deltaPixels = e.Y - dragStart;
                var deltaValue = (int)Math.Round((double)deltaPixels * max / travel);
                SetFirstVisibleRowIndex(dragStartValue + deltaValue);
            }
        }

        if (draggingHorizontal && HasHorizontalScroll())
        {
            var track = GetHorizontalTrackBounds();
            var thumb = GetHorizontalThumbBounds(track);
            var travel = Math.Max(1, track.Width - thumb.Width);
            var max = GetMaxHorizontalValue();
            if (max > 0)
            {
                var deltaPixels = e.X - dragStart;
                var deltaValue = (int)Math.Round((double)deltaPixels * max / travel);
                SetHorizontalOffset(dragStartValue + deltaValue);
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButtons.Left)
        {
            draggingVertical = false;
            draggingHorizontal = false;
            Capture = false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var vTrack = GetVerticalTrackBounds();
        if (HasVerticalScroll())
        {
            DrawTrack(e.Graphics, vTrack);
            DrawThumb(e.Graphics, GetVerticalThumbBounds(vTrack));
        }

        var hTrack = GetHorizontalTrackBounds();
        if (HasHorizontalScroll())
        {
            DrawTrack(e.Graphics, hTrack);
            DrawThumb(e.Graphics, GetHorizontalThumbBounds(hTrack));
        }

        if (HasHorizontalScroll() && HasVerticalScroll())
        {
            var corner = new Rectangle(vTrack.X, hTrack.Y, vTrack.Width, hTrack.Height);
            using var brush = new SolidBrush(GetTrackColor());
            e.Graphics.FillRectangle(brush, corner);
        }
    }

    private void DrawTrack(Graphics graphics, Rectangle bounds)
    {
        using var brush = new SolidBrush(GetTrackColor());
        using var pen = new Pen(GetBorderColor());
        graphics.FillRectangle(brush, bounds);
        graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    private void DrawThumb(Graphics graphics, Rectangle bounds)
    {
        using var brush = new SolidBrush(GetThumbColor());
        graphics.FillRectangle(brush, bounds);
    }

    private bool TryScrollRows(int delta)
    {
        if (!HasVerticalScroll())
        {
            return false;
        }

        SetFirstVisibleRowIndex(GetFirstVisibleRowIndex() + delta);
        return true;
    }

    private Rectangle GetVerticalTrackBounds()
    {
        var x = Math.Max(0, ClientSize.Width - ScrollBarThickness - ScrollPadding);
        var y = ColumnHeadersVisible ? ColumnHeadersHeight + 1 : 0;
        var hTrack = GetHorizontalTrackBounds();
        var bottom = HasHorizontalScroll() ? hTrack.Top - ScrollPadding : ClientSize.Height - ScrollPadding;
        return new Rectangle(x, y, ScrollBarThickness, Math.Max(0, bottom - y));
    }

    private Rectangle GetHorizontalTrackBounds()
    {
        var x = RowHeadersVisible ? RowHeadersWidth + 1 : 0;
        var y = Math.Max(0, ClientSize.Height - ScrollBarThickness - ScrollPadding);
        var right = HasVerticalScroll()
            ? ClientSize.Width - ScrollBarThickness - (ScrollPadding * 2)
            : ClientSize.Width - ScrollPadding;
        return new Rectangle(x, y, Math.Max(0, right - x), ScrollBarThickness);
    }

    private Rectangle GetVerticalThumbBounds(Rectangle track)
    {
        var max = GetMaxVerticalValue();
        if (max <= 0 || track.Height <= 0)
        {
            return new Rectangle(track.X + 1, track.Y + 1, track.Width - 2, Math.Max(0, track.Height - 2));
        }

        var displayed = Math.Max(1, GetDisplayedRowCount());
        var thumbHeight = Math.Max(MinThumbSize, (int)Math.Round((double)track.Height * displayed / Math.Max(displayed, RowCount)));
        thumbHeight = Math.Min(track.Height, thumbHeight);

        var travel = track.Height - thumbHeight;
        var value = GetFirstVisibleRowIndex();
        var y = track.Y + (int)Math.Round((double)value * travel / max);

        return new Rectangle(track.X + 1, y + 1, track.Width - 2, Math.Max(0, thumbHeight - 2));
    }

    private Rectangle GetHorizontalThumbBounds(Rectangle track)
    {
        var max = GetMaxHorizontalValue();
        if (max <= 0 || track.Width <= 0)
        {
            return new Rectangle(track.X + 1, track.Y + 1, Math.Max(0, track.Width - 2), track.Height - 2);
        }

        var totalWidth = GetTotalVisibleColumnWidth();
        var viewportWidth = Math.Max(1, track.Width);
        var thumbWidth = Math.Max(MinThumbSize, (int)Math.Round((double)track.Width * viewportWidth / Math.Max(totalWidth, viewportWidth)));
        thumbWidth = Math.Min(track.Width, thumbWidth);

        var travel = track.Width - thumbWidth;
        var value = HorizontalScrollingOffset;
        var x = track.X + (int)Math.Round((double)value * travel / max);

        return new Rectangle(x + 1, track.Y + 1, Math.Max(0, thumbWidth - 2), track.Height - 2);
    }

    private bool HasVerticalScroll()
    {
        return GetMaxVerticalValue() > 0;
    }

    private bool HasHorizontalScroll()
    {
        return GetMaxHorizontalValue() > 0;
    }

    private int GetMaxVerticalValue()
    {
        return Math.Max(0, RowCount - GetDisplayedRowCount());
    }

    private int GetDisplayedRowCount()
    {
        var count = DisplayedRowCount(false);
        return Math.Max(1, count);
    }

    private int GetFirstVisibleRowIndex()
    {
        return Math.Max(0, FirstDisplayedScrollingRowIndex);
    }

    private void SetFirstVisibleRowIndex(int value)
    {
        var clamped = Math.Max(0, Math.Min(GetMaxVerticalValue(), value));
        try
        {
            if (RowCount > 0)
            {
                FirstDisplayedScrollingRowIndex = clamped;
                Invalidate();
            }
        }
        catch
        {
            // Ignore transient invalid row display states during layout.
        }
    }

    private int GetMaxHorizontalValue()
    {
        var viewport = GetHorizontalTrackBounds().Width;
        return Math.Max(0, GetTotalVisibleColumnWidth() - Math.Max(1, viewport));
    }

    private int GetTotalVisibleColumnWidth()
    {
        var width = RowHeadersVisible ? RowHeadersWidth : 0;
        foreach (DataGridViewColumn column in Columns)
        {
            if (column.Visible)
            {
                width += column.Width;
            }
        }

        return width;
    }

    private void SetHorizontalOffset(int value)
    {
        var clamped = Math.Max(0, Math.Min(GetMaxHorizontalValue(), value));
        HorizontalScrollingOffset = clamped;
        Invalidate();
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
