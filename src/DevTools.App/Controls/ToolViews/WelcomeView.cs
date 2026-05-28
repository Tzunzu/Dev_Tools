using DevTools.App.Infrastructure.UI;
using System.Drawing;
using System.Windows.Forms;

namespace DevTools.App.Controls.ToolViews;

internal sealed class WelcomeView : UserControl
{
    public WelcomeView()
    {
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = AppTheme.PagePadding,
            RowCount = 2
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8),
            Text = "DevTools"
        };

        var card = new GroupBox
        {
            Dock = DockStyle.Top,
            Height = 132,
            Padding = AppTheme.WorkspaceGroupPadding,
            Margin = new Padding(0),
            Text = ""
        };

        var cardTitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Location = new Point(0, 0),
            Text = "Quick Start"
        };

        var descriptionLabel = new Label
        {
            AutoSize = true,
            Location = new Point(0, 32),
            MaximumSize = new Size(760, 0),
            Text = "Choose a tool in the navigation tree to open its workspace.\nUse Settings to tune appearance, density, and theme mode."
        };

        card.Controls.Add(cardTitle);
        card.Controls.Add(descriptionLabel);
        root.Controls.Add(titleLabel, 0, 0);
        root.Controls.Add(card, 0, 1);
        Controls.Add(root);
    }
}
