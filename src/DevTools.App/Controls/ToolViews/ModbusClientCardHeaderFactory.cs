using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace DevTools.App.Controls.ToolViews;

internal static class ModbusClientCardHeaderFactory
{
    public static CardHeaderControls Create(byte functionCode, int slaveId, int startRegister, int registerCount)
    {
        var headerPanel = new TableLayoutPanel
        {
            ColumnCount = 4,
            Dock = DockStyle.Top,
            Height = 50,
            Margin = new Padding(0, 2, 0, 0),
            RowCount = 2
        };

        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34F));
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30F));
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56F));
        headerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
        headerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));

        var functionInput = CreateFunctionSelector(functionCode);
        var unitIdTextBox = CreateCardTextBox(slaveId.ToString(CultureInfo.InvariantCulture), 3);
        var startTextBox = CreateCardTextBox(startRegister.ToString(CultureInfo.InvariantCulture), 6);
        var countTextBox = CreateCardTextBox(registerCount.ToString(CultureInfo.InvariantCulture), 3);

        headerPanel.Controls.Add(CreateCardHeaderLabel("Func", new Padding(0, 0, 3, 0)), 0, 0);
        headerPanel.Controls.Add(functionInput, 1, 0);
        headerPanel.Controls.Add(CreateCardHeaderLabel("ID", new Padding(1, 0, 3, 0)), 2, 0);
        headerPanel.Controls.Add(unitIdTextBox, 3, 0);
        headerPanel.Controls.Add(CreateCardHeaderLabel("Start", new Padding(0, 0, 3, 0)), 0, 1);
        headerPanel.Controls.Add(startTextBox, 1, 1);
        headerPanel.Controls.Add(CreateCardHeaderLabel("Len", new Padding(1, 0, 3, 0)), 2, 1);
        headerPanel.Controls.Add(countTextBox, 3, 1);

        return new CardHeaderControls(headerPanel, functionInput, unitIdTextBox, startTextBox, countTextBox);
    }

    public static bool TryGetSelectedFunctionCode(ComboBox comboBox, out byte functionCode)
    {
        if (comboBox.SelectedItem is ModbusFunctionOption option)
        {
            functionCode = option.FunctionCode;
            return true;
        }

        functionCode = 0;
        return false;
    }

    public static string GetFunctionLabel(byte functionCode)
    {
        return ModbusFunctionOptions.FirstOrDefault(option => option.FunctionCode == functionCode)?.Label ?? "Value";
    }

    private static ComboBox CreateFunctionSelector(byte selectedFunctionCode)
    {
        var comboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 82,
            Margin = new Padding(0, 1, 0, 0),
            Anchor = AnchorStyles.Left,
            DisplayMember = nameof(ModbusFunctionOption.Label),
            ValueMember = nameof(ModbusFunctionOption.FunctionCode)
        };

        comboBox.Items.AddRange(ModbusFunctionOptions.Cast<object>().ToArray());
        comboBox.SelectedItem = ModbusFunctionOptions.FirstOrDefault(option => option.FunctionCode == selectedFunctionCode) ?? ModbusFunctionOptions[0];
        return comboBox;
    }

    private static TextBox CreateCardTextBox(string text, int maxLength)
    {
        return new TextBox
        {
            AutoSize = false,
            Height = 23,
            Text = text,
            MaxLength = maxLength,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 1, 0, 0),
            Width = maxLength <= 3 ? 52 : 82
        };
    }

    private static Label CreateCardHeaderLabel(string text, Padding margin)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Margin = margin,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    internal sealed record CardHeaderControls(
        TableLayoutPanel Panel,
        ComboBox FunctionComboBox,
        TextBox UnitIdTextBox,
        TextBox StartTextBox,
        TextBox CountTextBox);

    private sealed record ModbusFunctionOption(string Label, byte FunctionCode);

    private static readonly ModbusFunctionOption[] ModbusFunctionOptions =
    {
        new("Coil", 0x01),
        new("Discrete", 0x02),
        new("Holding", 0x03),
        new("Input", 0x04)
    };
}