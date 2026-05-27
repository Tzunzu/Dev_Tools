using System.Drawing;
using System.Windows.Forms;

namespace DevTools.App.Controls.ToolViews;

internal sealed class ModbusTcpServerView : UserControl
{
    public ModbusTcpServerView()
    {
        Dock = DockStyle.Fill;

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            Location = new Point(16, 16),
            Text = "Modbus TCP Server"
        };

        var descriptionLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 56),
            MaximumSize = new Size(640, 0),
            Text = "Version 1 placeholder. This screen will host port settings, register simulation, and active client session details for the TCP server."
        };

        Controls.Add(descriptionLabel);
        Controls.Add(titleLabel);
    }
}
