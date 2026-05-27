using System.Drawing;
using System.Windows.Forms;

namespace DevTools.App.Controls.ToolViews;

internal sealed class ModbusTcpClientView : UserControl
{
    public ModbusTcpClientView()
    {
        Dock = DockStyle.Fill;

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            Location = new Point(16, 16),
            Text = "Modbus TCP Client"
        };

        var descriptionLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 56),
            MaximumSize = new Size(640, 0),
            Text = "Version 1 placeholder. This screen will host connection settings, request builders, and live response data for the TCP client."
        };

        Controls.Add(descriptionLabel);
        Controls.Add(titleLabel);
    }
}
