using DevTools.App.Infrastructure.UI;
using System.Windows.Forms;

namespace DevTools.App.Controls.ToolViews;

internal enum ToolbarButtonVariant
{
    Standard,
    Wide,
    Trailing,
    Last
}

internal static class ModbusViewControlFactory
{
    public static Control CreateLabeledField(string labelText, Control input, int inputWidth)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0),
            WrapContents = false
        };

        var label = new Label
        {
            AutoSize = true,
            Margin = AppTheme.InlineFieldLabelMargin,
            Text = labelText
        };

        input.Margin = AppTheme.InlineFieldInputMargin;
        input.Width = inputWidth;

        panel.Controls.Add(label);
        panel.Controls.Add(input);
        return panel;
    }

    public static FlowLayoutPanel CreateToolbarPanel()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            WrapContents = false
        };
    }

    public static Button CreateToolbarButton(string text, ToolbarButtonVariant variant = ToolbarButtonVariant.Standard, bool enabled = true)
    {
        return new Button
        {
            Enabled = enabled,
            Margin = GetToolbarButtonMargin(variant),
            Size = GetToolbarButtonSize(variant),
            Text = text
        };
    }

    public static ComboBox CreatePresetComboBox()
    {
        return new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            Margin = AppTheme.ToolbarComboMargin,
            Size = AppTheme.ToolbarPresetSize
        };
    }

    public static Button CreateCardApplyButton()
    {
        return new Button
        {
            Margin = AppTheme.CardApplyButtonMargin,
            Size = AppTheme.CardApplyButtonSize,
            Text = "Apply"
        };
    }

    private static Padding GetToolbarButtonMargin(ToolbarButtonVariant variant)
    {
        return variant switch
        {
            ToolbarButtonVariant.Trailing => AppTheme.ToolbarTrailingButtonMargin,
            ToolbarButtonVariant.Last => AppTheme.ToolbarLastButtonMargin,
            _ => AppTheme.ToolbarButtonMargin
        };
    }

    private static System.Drawing.Size GetToolbarButtonSize(ToolbarButtonVariant variant)
    {
        return variant switch
        {
            ToolbarButtonVariant.Wide => AppTheme.ToolbarWideButtonSize,
            ToolbarButtonVariant.Trailing => AppTheme.ToolbarRenameButtonSize,
            ToolbarButtonVariant.Last => AppTheme.ToolbarDeleteButtonSize,
            _ => AppTheme.ToolbarButtonSize
        };
    }
}