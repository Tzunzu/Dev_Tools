using System.Drawing;
using System.Windows.Forms;

namespace DevTools.App.Infrastructure.UI;

internal enum ThemeMode
{
    Light,
    Dark
}

internal enum StatusTone
{
    Info,
    Success,
    Warning,
    Error
}

internal sealed record ThemeOptions(ThemeMode Mode, bool CompactDataRows);

internal static class AppTheme
{
    private static readonly Font UiFont = new("Segoe UI", 9F);
    private static readonly Font UiFontBold = new("Segoe UI Semibold", 9F, FontStyle.Bold);
    private static readonly Font UiSectionFont = new("Segoe UI Semibold", 9.5F, FontStyle.Bold);
    public static Padding WorkspaceGroupPadding => new(12, 10, 12, 12);
    public static Padding WorkspaceGroupMargin => new(0, 0, 0, 10);
    public static Padding PagePadding => new(12);
    public static Padding InlineFieldLabelMargin => new(0, 5, 6, 0);
    public static Padding InlineFieldInputMargin => new(0, 1, 0, 0);
    public static Padding ToolbarButtonMargin => new(0, 4, 8, 4);
    public static Padding ToolbarTrailingButtonMargin => new(8, 4, 6, 4);
    public static Padding ToolbarLastButtonMargin => new(0, 4, 0, 4);
    public static Padding ToolbarComboMargin => new(0, 5, 0, 4);
    public static Padding CardGroupMargin => new(0, 0, 6, 0);
    public static Padding CardGroupPadding => new(6);
    public static Padding CardApplyButtonMargin => new(8, 2, 0, 0);
    public static Size ToolbarButtonSize => new(92, 27);
    public static Size ToolbarWideButtonSize => new(100, 27);
    public static Size ToolbarRenameButtonSize => new(66, 27);
    public static Size ToolbarDeleteButtonSize => new(60, 27);
    public static Size ToolbarPresetSize => new(130, 23);
    public static Size CardApplyButtonSize => new(64, 27);
    public static Size ConnectionPrimaryButtonSize => new(96, 28);
    public static Size ConnectionSecondaryButtonSize => new(84, 28);
    public static int ToolbarRowHeight => 36;
    public static int SectionHeaderHeight => 76;

    private static ThemeOptions options = new(ThemeMode.Dark, true);

    public static event Action<ThemeOptions>? ThemeChanged;

    public static ThemeMode CurrentMode => options.Mode;

    public static ThemeOptions Options => options;

    public static void SetMode(ThemeMode mode)
    {
        UpdateOptions(options with { Mode = mode });
    }

    public static void SetCompactDataRows(bool compact)
    {
        UpdateOptions(options with { CompactDataRows = compact });
    }

    public static void UpdateOptions(ThemeOptions nextOptions)
    {
        if (options == nextOptions)
        {
            return;
        }

        options = nextOptions;
        ThemeChanged?.Invoke(options);
    }

    public static void Apply(Control root)
    {
        ApplyToControl(root);

        if (root.ContextMenuStrip is not null)
        {
            Apply(root.ContextMenuStrip);
        }

        foreach (Control child in root.Controls)
        {
            Apply(child);
        }
    }

    public static void Apply(ContextMenuStrip menu)
    {
        menu.BackColor = SurfaceBackColor;
        menu.ForeColor = TextPrimary;
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable());

        foreach (ToolStripItem item in menu.Items)
        {
            ApplyToToolStripItem(item);
        }
    }

    public static Color GetStatusColor(StatusTone tone)
    {
        return tone switch
        {
            StatusTone.Success => IsDarkMode ? Color.FromArgb(78, 201, 176) : Color.FromArgb(0, 102, 68),
            StatusTone.Warning => IsDarkMode ? Color.FromArgb(220, 220, 170) : Color.FromArgb(146, 109, 0),
            StatusTone.Error => IsDarkMode ? Color.FromArgb(244, 135, 113) : Color.FromArgb(153, 27, 27),
            _ => TextSecondary
        };
    }

    private static Color PanelBackColor => IsDarkMode
        ? Color.FromArgb(30, 30, 30)
        : Color.FromArgb(248, 250, 252);

    private static Color SurfaceBackColor => IsDarkMode
        ? Color.FromArgb(37, 37, 38)
        : Color.White;

    private static Color SurfaceAltBackColor => IsDarkMode
        ? Color.FromArgb(45, 45, 48)
        : Color.FromArgb(249, 250, 251);

    private static Color HeaderBackColor => IsDarkMode
        ? Color.FromArgb(62, 62, 66)
        : Color.FromArgb(241, 245, 249);

    private static Color BorderColor => IsDarkMode
        ? Color.FromArgb(63, 63, 70)
        : Color.FromArgb(226, 232, 240);

    private static Color TextPrimary => IsDarkMode
        ? Color.FromArgb(212, 212, 212)
        : Color.FromArgb(15, 23, 42);

    private static Color TextSecondary => IsDarkMode
        ? Color.FromArgb(181, 181, 181)
        : Color.FromArgb(71, 85, 105);

    private static Color AccentColor => IsDarkMode
        ? Color.FromArgb(0, 122, 204)
        : Color.FromArgb(30, 136, 229);

    private static Color ButtonBackColor => IsDarkMode
        ? Color.FromArgb(14, 99, 156)
        : Color.FromArgb(241, 245, 249);

    private static Color ButtonHoverBackColor => IsDarkMode
        ? Color.FromArgb(17, 120, 188)
        : Color.FromArgb(226, 232, 240);

    private static Color ButtonPressedBackColor => IsDarkMode
        ? Color.FromArgb(9, 71, 113)
        : Color.FromArgb(203, 213, 225);

    private static Color InputBackColor => IsDarkMode
        ? Color.FromArgb(62, 62, 66)
        : SurfaceBackColor;

    private static Color SelectionBackColor => IsDarkMode
        ? Color.FromArgb(9, 71, 113)
        : AccentColor;

    private static Color TreeSelectionBackColor => IsDarkMode
        ? Color.FromArgb(55, 55, 61)
        : Color.FromArgb(219, 234, 254);

    private static bool IsDarkMode => options.Mode == ThemeMode.Dark;

    private static void ApplyToControl(Control control)
    {
        control.Font = UiFont;
        control.ForeColor = TextPrimary;

        switch (control)
        {
            case Form form:
                form.BackColor = PanelBackColor;
                break;

            case GroupBox groupBox:
                groupBox.ForeColor = TextSecondary;
                groupBox.BackColor = SurfaceBackColor;
                groupBox.Font = UiSectionFont;
                break;

            case TabPage tabPage:
                tabPage.BackColor = SurfaceBackColor;
                tabPage.ForeColor = TextPrimary;
                break;

            case Panel panel:
                panel.BackColor = SurfaceBackColor;
                break;

            case SplitContainer:
                control.BackColor = BorderColor;
                break;

            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = BorderColor;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.MouseOverBackColor = ButtonHoverBackColor;
                button.FlatAppearance.MouseDownBackColor = ButtonPressedBackColor;
                button.BackColor = ButtonBackColor;
                button.ForeColor = TextPrimary;
                break;

            case ComboBox comboBox:
                comboBox.BackColor = InputBackColor;
                comboBox.ForeColor = TextPrimary;
                comboBox.FlatStyle = FlatStyle.Flat;
                comboBox.DrawMode = DrawMode.OwnerDrawFixed;
                comboBox.ItemHeight = 20;
                comboBox.DrawItem -= comboBox_DrawItem;
                comboBox.DrawItem += comboBox_DrawItem;
                break;

            case Label label:
                label.ForeColor = TextSecondary;
                break;

            case TreeView treeView:
                treeView.BackColor = SurfaceAltBackColor;
                treeView.ForeColor = TextPrimary;
                treeView.BorderStyle = BorderStyle.None;
                treeView.FullRowSelect = true;
                treeView.HideSelection = false;
                treeView.ShowLines = false;
                treeView.ShowRootLines = false;
                treeView.ShowPlusMinus = false;
                treeView.ItemHeight = 22;
                treeView.DrawMode = TreeViewDrawMode.OwnerDrawText;
                treeView.DrawNode -= treeView_DrawNode;
                treeView.DrawNode += treeView_DrawNode;
                break;

            case TabControl tabControl:
                tabControl.Appearance = TabAppearance.Normal;
                tabControl.BackColor = SurfaceBackColor;
                tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabControl.DrawItem -= tabControl_DrawItem;
                tabControl.DrawItem += tabControl_DrawItem;
                break;

            case ListView listView:
                listView.BackColor = SurfaceBackColor;
                listView.ForeColor = TextPrimary;
                break;

            case RichTextBox richTextBox:
                richTextBox.BackColor = SurfaceAltBackColor;
                richTextBox.ForeColor = TextPrimary;
                break;

            case TextBox textBox:
                textBox.BackColor = InputBackColor;
                textBox.ForeColor = TextPrimary;
                break;

            case DataGridView grid:
                ApplyToGrid(grid);
                break;

            case MenuStrip menuStrip:
                ApplyToMenuStrip(menuStrip);
                break;

            case StatusStrip statusStrip:
                ApplyToStatusStrip(statusStrip);
                break;
        }
    }

    private static void ApplyToGrid(DataGridView grid)
    {
        var rowHeight = options.CompactDataRows ? 22 : 28;

        grid.EnableHeadersVisualStyles = false;
        grid.BackgroundColor = SurfaceAltBackColor;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = BorderColor;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderBackColor;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextPrimary;
        grid.ColumnHeadersDefaultCellStyle.Font = UiFontBold;
        grid.ColumnHeadersHeight = rowHeight + 6;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ScrollBars = grid is ThemedDataGridView ? ScrollBars.None : ScrollBars.Vertical;
        grid.DefaultCellStyle.BackColor = SurfaceBackColor;
        grid.DefaultCellStyle.ForeColor = TextPrimary;
        grid.DefaultCellStyle.SelectionBackColor = SelectionBackColor;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.DefaultCellStyle.Font = UiFont;
        grid.DefaultCellStyle.Padding = new Padding(0, 1, 0, 1);
        grid.AlternatingRowsDefaultCellStyle.BackColor = IsDarkMode
            ? Color.FromArgb(41, 41, 46)
            : Color.FromArgb(248, 250, 252);
        grid.RowHeadersDefaultCellStyle.BackColor = HeaderBackColor;
        grid.RowHeadersDefaultCellStyle.ForeColor = TextSecondary;
        grid.RowTemplate.Height = rowHeight;
    }

    private static void ApplyToMenuStrip(MenuStrip menuStrip)
    {
        menuStrip.BackColor = SurfaceBackColor;
        menuStrip.ForeColor = TextPrimary;
        menuStrip.RenderMode = ToolStripRenderMode.Professional;
        menuStrip.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable());
        menuStrip.Padding = new Padding(6, 2, 0, 2);
        foreach (ToolStripItem item in menuStrip.Items)
        {
            ApplyToToolStripItem(item);
        }
    }

    private static void ApplyToStatusStrip(StatusStrip statusStrip)
    {
        statusStrip.BackColor = SurfaceBackColor;
        statusStrip.ForeColor = TextSecondary;
        statusStrip.SizingGrip = false;
        statusStrip.RenderMode = ToolStripRenderMode.Professional;
        statusStrip.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable());
        statusStrip.Padding = new Padding(8, 2, 8, 2);
        foreach (ToolStripItem item in statusStrip.Items)
        {
            ApplyToToolStripItem(item);
        }
    }

    private static void ApplyToToolStripItem(ToolStripItem item)
    {
        item.ForeColor = TextPrimary;

        if (item is ToolStripDropDownItem dropDownItem)
        {
            dropDownItem.DropDown.BackColor = SurfaceBackColor;
            dropDownItem.DropDown.ForeColor = TextPrimary;

            foreach (ToolStripItem child in dropDownItem.DropDownItems)
            {
                ApplyToToolStripItem(child);
            }
        }
    }

    private static void tabControl_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabControl || e.Index < 0 || e.Index >= tabControl.TabPages.Count)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var tabBack = selected
            ? (IsDarkMode ? Color.FromArgb(45, 45, 48) : Color.White)
            : (IsDarkMode ? Color.FromArgb(37, 37, 38) : Color.FromArgb(241, 245, 249));
        var tabText = selected ? TextPrimary : TextSecondary;

        using var backBrush = new SolidBrush(tabBack);
        e.Graphics.FillRectangle(backBrush, e.Bounds);

        var textBounds = Rectangle.Inflate(e.Bounds, -8, -2);
        TextRenderer.DrawText(
            e.Graphics,
            tabControl.TabPages[e.Index].Text,
            UiFont,
            textBounds,
            tabText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        using var borderPen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(borderPen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);

        if (selected)
        {
            using var accentPen = new Pen(AccentColor, 2F);
            e.Graphics.DrawLine(accentPen, e.Bounds.Left + 2, e.Bounds.Bottom - 2, e.Bounds.Right - 3, e.Bounds.Bottom - 2);
        }
    }

    private static void treeView_DrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        if (sender is not TreeView treeView)
        {
            return;
        }

        var selected = (e.State & TreeNodeStates.Selected) == TreeNodeStates.Selected;
        var foreColor = selected ? TextPrimary : treeView.ForeColor;
        var backColor = selected ? TreeSelectionBackColor : treeView.BackColor;

        var fullRowBounds = new Rectangle(0, e.Bounds.Top, treeView.Width, e.Bounds.Height);
        using var backBrush = new SolidBrush(backColor);
        e.Graphics.FillRectangle(backBrush, fullRowBounds);

        TextRenderer.DrawText(
            e.Graphics,
            e.Node?.Text ?? string.Empty,
            treeView.Font ?? UiFont,
            e.Bounds,
            foreColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static void comboBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox comboBox || e.Bounds.Width <= 0 || e.Bounds.Height <= 0)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var backColor = selected ? SelectionBackColor : InputBackColor;
        var textColor = selected ? Color.White : TextPrimary;

        using var backBrush = new SolidBrush(backColor);
        e.Graphics.FillRectangle(backBrush, e.Bounds);

        string? text;
        if (e.Index >= 0 && e.Index < comboBox.Items.Count)
        {
            text = comboBox.GetItemText(comboBox.Items[e.Index]!);
        }
        else if (comboBox.SelectedItem is not null)
        {
            text = comboBox.GetItemText(comboBox.SelectedItem!);
        }
        else
        {
            text = comboBox.Text;
        }

        var textBounds = Rectangle.Inflate(e.Bounds, -4, 0);
        TextRenderer.DrawText(
            e.Graphics,
            text ?? string.Empty,
            comboBox.Font ?? UiFont,
            textBounds,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (selected)
        {
            e.DrawFocusRectangle();
        }
    }

    private sealed class ThemeColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => SurfaceBackColor;

        public override Color MenuStripGradientEnd => SurfaceBackColor;

        public override Color MenuBorder => BorderColor;

        public override Color MenuItemBorder => BorderColor;

        public override Color MenuItemSelected => HeaderBackColor;

        public override Color MenuItemSelectedGradientBegin => HeaderBackColor;

        public override Color MenuItemSelectedGradientEnd => HeaderBackColor;

        public override Color MenuItemPressedGradientBegin => SurfaceBackColor;

        public override Color MenuItemPressedGradientMiddle => SurfaceBackColor;

        public override Color MenuItemPressedGradientEnd => SurfaceBackColor;

        public override Color ToolStripDropDownBackground => SurfaceBackColor;

        public override Color ToolStripBorder => BorderColor;

        public override Color ToolStripGradientBegin => SurfaceBackColor;

        public override Color ToolStripGradientMiddle => SurfaceBackColor;

        public override Color ToolStripGradientEnd => SurfaceBackColor;

        public override Color ImageMarginGradientBegin => SurfaceBackColor;

        public override Color ImageMarginGradientMiddle => SurfaceBackColor;

        public override Color ImageMarginGradientEnd => SurfaceBackColor;

        public override Color StatusStripGradientBegin => SurfaceBackColor;

        public override Color StatusStripGradientEnd => SurfaceBackColor;
    }
}
