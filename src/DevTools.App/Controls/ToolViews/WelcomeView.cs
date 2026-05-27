using System.Drawing;
using System.Windows.Forms;

namespace DevTools.App.Controls.ToolViews;

internal sealed class WelcomeView : UserControl
{
    public WelcomeView()
    {
        Dock = DockStyle.Fill;

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            Location = new Point(16, 16),
            Text = "DevTools"
        };

        var descriptionLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 56),
            MaximumSize = new Size(640, 0),
            Text = "Select a tool from the navigation tree to load its workspace. Each tool now has its own view file so the project can grow without oversized forms."
        };

        Controls.Add(descriptionLabel);
        Controls.Add(titleLabel);
    }
}
