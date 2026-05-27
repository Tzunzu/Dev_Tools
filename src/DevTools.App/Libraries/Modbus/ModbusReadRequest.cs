namespace DevTools.App.Libraries.Modbus;

internal sealed class ModbusReadRequest
{
    public byte SlaveId { get; set; }

    public ushort StartAddress { get; set; }

    public ushort RegisterCount { get; set; }

    public byte FunctionCode { get; set; } = 0x03;
}
