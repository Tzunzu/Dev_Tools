namespace DevTools.App.Controls.ToolViews;

using System.Collections.Generic;

internal sealed class SlaveColumnConfig
{
    public string Key { get; init; } = string.Empty;

    public int SlaveId { get; set; }

    public int StartRegister { get; set; }

    public int RegisterCount { get; set; }

    public int PollRateMs { get; set; } = 1000;

    public byte FunctionCode { get; set; } = 0x03;

    public string RegisterNumberFormat { get; set; } = "Decimal";

    public string RegisterValueDataType { get; set; } = "UInt";

    public bool ShowDescriptionColumn { get; set; }

    public Dictionary<int, string> Descriptions { get; set; } = new();
}
