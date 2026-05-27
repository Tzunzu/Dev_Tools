using System.Drawing;
using System.Windows.Forms;

namespace DevTools.App.Controls.ToolViews;

internal static class ModbusClientSlaveCardFactory
{
    public static CardShell Create(byte functionCode, int slaveId, int startRegister, int registerCount, Padding contentPadding)
    {
        var group = new GroupBox
        {
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(6),
            ForeColor = Color.FromArgb(51, 65, 85)
        };

        var rootPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0)
        };
        rootPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var headerHost = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Margin = new Padding(0),
            ColumnCount = 1,
            RowCount = 1
        };
        headerHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var headerControls = ModbusClientCardHeaderFactory.Create(functionCode, slaveId, startRegister, registerCount);
        headerHost.Controls.Add(headerControls.Panel, 0, 0);

        var statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(2, 0, 0, 0),
            Text = "Reads: 0  Err: 0",
            Font = new Font("Segoe UI", 7.5F),
            ForeColor = Color.FromArgb(71, 85, 105),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = contentPadding
        };

        rootPanel.Controls.Add(headerHost, 0, 0);
        rootPanel.Controls.Add(statusLabel, 0, 1);
        rootPanel.Controls.Add(contentHost, 0, 2);
        group.Controls.Add(rootPanel);

        return new CardShell(
            group,
            rootPanel,
            headerHost,
            headerControls.Panel,
            contentHost,
            statusLabel,
            headerControls.FunctionComboBox,
            headerControls.UnitIdTextBox,
            headerControls.StartTextBox,
            headerControls.CountTextBox);
    }

    internal sealed record CardShell(
        GroupBox Group,
        TableLayoutPanel RootPanel,
        TableLayoutPanel HeaderHost,
        TableLayoutPanel HeaderPanel,
        Panel ContentHost,
        Label StatusLabel,
        ComboBox FunctionComboBox,
        TextBox UnitIdTextBox,
        TextBox StartTextBox,
        TextBox CountTextBox);
}