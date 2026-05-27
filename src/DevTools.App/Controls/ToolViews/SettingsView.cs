using DevTools.App.Infrastructure.UI;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace DevTools.App.Controls.ToolViews;

internal sealed class SettingsView : UserControl
{
    private readonly ComboBox themeModeComboBox;
    private readonly CheckBox compactOutputRowsCheckBox;
    private bool applyingThemeOptions;

    public SettingsView()
    {
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            RowCount = 2
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var title = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            Margin = new Padding(8, 8, 8, 4),
            Text = "Appearance"
        };

        var group = new GroupBox
        {
            Dock = DockStyle.Top,
            Height = 130,
            Margin = new Padding(8, 4, 8, 8),
            Padding = new Padding(12, 10, 12, 12),
            Text = "Theme"
        };

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

        var modeLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Text = "Theme mode"
        };

        themeModeComboBox = new ComboBox
        {
            Anchor = AnchorStyles.Left,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 190
        };
        themeModeComboBox.Items.Add("Dark (VS Code)");
        themeModeComboBox.Items.Add("Light");
        themeModeComboBox.SelectedIndexChanged += themeModeComboBox_SelectedIndexChanged;

        compactOutputRowsCheckBox = new CheckBox
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Text = "Compact output event rows"
        };
        compactOutputRowsCheckBox.CheckedChanged += compactOutputRowsCheckBox_CheckedChanged;

        layout.Controls.Add(modeLabel, 0, 0);
        layout.Controls.Add(themeModeComboBox, 1, 0);
        layout.Controls.Add(compactOutputRowsCheckBox, 1, 1);

        group.Controls.Add(layout);
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(group, 0, 1);
        Controls.Add(root);

        AppTheme.ThemeChanged += AppTheme_ThemeChanged;
        ApplyThemeOptionsToControls(AppTheme.Options);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AppTheme.ThemeChanged -= AppTheme_ThemeChanged;
        }

        base.Dispose(disposing);
    }

    private void themeModeComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (applyingThemeOptions)
        {
            return;
        }

        var mode = themeModeComboBox.SelectedIndex == 0 ? ThemeMode.Dark : ThemeMode.Light;
        AppTheme.UpdateOptions(new ThemeOptions(mode, compactOutputRowsCheckBox.Checked));
    }

    private void compactOutputRowsCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        if (applyingThemeOptions)
        {
            return;
        }

        var mode = themeModeComboBox.SelectedIndex == 0 ? ThemeMode.Dark : ThemeMode.Light;
        AppTheme.UpdateOptions(new ThemeOptions(mode, compactOutputRowsCheckBox.Checked));
    }

    private void AppTheme_ThemeChanged(ThemeOptions options)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => ApplyThemeOptionsToControls(options));
            return;
        }

        ApplyThemeOptionsToControls(options);
    }

    private void ApplyThemeOptionsToControls(ThemeOptions options)
    {
        applyingThemeOptions = true;
        try
        {
            themeModeComboBox.SelectedIndex = options.Mode == ThemeMode.Dark ? 0 : 1;
            compactOutputRowsCheckBox.Checked = options.CompactDataRows;
        }
        finally
        {
            applyingThemeOptions = false;
        }
    }
}
