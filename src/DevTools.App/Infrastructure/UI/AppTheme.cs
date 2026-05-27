using System.Drawing;
using System.Windows.Forms;

namespace DevTools.App.Infrastructure.UI;

internal static class AppTheme
{
    private static readonly Font UiFont = new("Segoe UI", 9F);
    private static readonly Font UiFontBold = new("Segoe UI", 9F, FontStyle.Bold);
    private static readonly Color PanelBackColor = Color.FromArgb(248, 250, 252);
    private static readonly Color SurfaceBackColor = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(226, 232, 240);
    private static readonly Color TextPrimary = Color.FromArgb(15, 23, 42);
    private static readonly Color TextSecondary = Color.FromArgb(71, 85, 105);
    private static readonly Color AccentColor = Color.FromArgb(30, 136, 229);

    public static void Apply(Control root)
    {
        ApplyToControl(root);

        foreach (Control child in root.Controls)
        {
            Apply(child);
        }
    }

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
                break;

            case TabPage tabPage:
                tabPage.BackColor = SurfaceBackColor;
                tabPage.ForeColor = TextPrimary;
                break;

            case Panel panel:
                panel.BackColor = SurfaceBackColor;
                break;

            case SplitContainer:
                break;

            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = BorderColor;
                button.FlatAppearance.BorderSize = 1;
                button.BackColor = Color.FromArgb(241, 245, 249);
                button.ForeColor = TextPrimary;
                break;

            case ComboBox comboBox:
                comboBox.BackColor = SurfaceBackColor;
                comboBox.ForeColor = TextPrimary;
                break;

            case Label label:
                label.ForeColor = TextSecondary;
                break;

            case TreeView treeView:
                treeView.BackColor = SurfaceBackColor;
                treeView.ForeColor = TextPrimary;
                treeView.BorderStyle = BorderStyle.FixedSingle;
                break;

            case TabControl tabControl:
                tabControl.Appearance = TabAppearance.Normal;
                tabControl.BackColor = SurfaceBackColor;
                break;

            case ListView listView:
                listView.BackColor = SurfaceBackColor;
                listView.ForeColor = TextPrimary;
                break;

            case RichTextBox richTextBox:
                richTextBox.BackColor = Color.FromArgb(249, 250, 251);
                richTextBox.ForeColor = TextPrimary;
                break;

            case TextBox textBox:
                textBox.BackColor = SurfaceBackColor;
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
        grid.EnableHeadersVisualStyles = false;
        grid.BackgroundColor = Color.FromArgb(249, 250, 251);
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = BorderColor;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextPrimary;
        grid.ColumnHeadersDefaultCellStyle.Font = UiFontBold;
        grid.DefaultCellStyle.BackColor = SurfaceBackColor;
        grid.DefaultCellStyle.ForeColor = TextPrimary;
        grid.DefaultCellStyle.SelectionBackColor = AccentColor;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.DefaultCellStyle.Font = UiFont;
    }

    private static void ApplyToMenuStrip(MenuStrip menuStrip)
    {
        menuStrip.BackColor = SurfaceBackColor;
        menuStrip.ForeColor = TextPrimary;
        menuStrip.RenderMode = ToolStripRenderMode.System;
    }

    private static void ApplyToStatusStrip(StatusStrip statusStrip)
    {
        statusStrip.BackColor = SurfaceBackColor;
        statusStrip.ForeColor = TextSecondary;
        statusStrip.SizingGrip = false;
    }
}
