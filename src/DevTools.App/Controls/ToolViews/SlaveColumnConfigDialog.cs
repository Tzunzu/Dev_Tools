using System;
using System.Drawing;
using System.Windows.Forms;

namespace DevTools.App.Controls.ToolViews;

internal sealed class SlaveColumnConfigDialog : Form
{
    private readonly NumericUpDown slaveIdNumericUpDown;
    private readonly NumericUpDown startRegisterNumericUpDown;
    private readonly NumericUpDown registerCountNumericUpDown;

    public SlaveColumnConfig Result { get; private set; }

    public SlaveColumnConfigDialog(SlaveColumnConfig config)
    {
        Result = new SlaveColumnConfig
        {
            Key = config.Key,
            SlaveId = config.SlaveId,
            StartRegister = config.StartRegister,
            RegisterCount = config.RegisterCount
        };

        Text = "Configure Slave Column";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 190);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var slaveIdLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Text = "Slave ID"
        };

        slaveIdNumericUpDown = new NumericUpDown
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Minimum = 1,
            Maximum = 247,
            Value = config.SlaveId
        };

        var startRegisterLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Text = "Start Register"
        };

        startRegisterNumericUpDown = new NumericUpDown
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Minimum = 0,
            Maximum = 65535,
            Value = config.StartRegister
        };

        var registerCountLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Text = "Registers Read"
        };

        registerCountNumericUpDown = new NumericUpDown
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Minimum = 1,
            Maximum = 500,
            Value = config.RegisterCount
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Width = 80
        };
        okButton.Click += okButton_Click;

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 80
        };

        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        layout.Controls.Add(slaveIdLabel, 0, 0);
        layout.Controls.Add(slaveIdNumericUpDown, 1, 0);
        layout.Controls.Add(startRegisterLabel, 0, 1);
        layout.Controls.Add(startRegisterNumericUpDown, 1, 1);
        layout.Controls.Add(registerCountLabel, 0, 2);
        layout.Controls.Add(registerCountNumericUpDown, 1, 2);
        layout.Controls.Add(buttonPanel, 0, 3);
        layout.SetColumnSpan(buttonPanel, 2);

        Controls.Add(layout);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private void okButton_Click(object? sender, EventArgs e)
    {
        Result = new SlaveColumnConfig
        {
            Key = Result.Key,
            SlaveId = (int)slaveIdNumericUpDown.Value,
            StartRegister = (int)startRegisterNumericUpDown.Value,
            RegisterCount = (int)registerCountNumericUpDown.Value
        };
    }
}
