namespace DevTools.App.Controllers.Modbus;

internal sealed class SlaveReadProfile
{
    public string Key { get; init; } = string.Empty;

    public byte SlaveId { get; init; }

    public byte FunctionCode { get; init; } = 0x03;

    public ushort StartRegister { get; init; }

    public ushort RegisterCount { get; init; }
}
