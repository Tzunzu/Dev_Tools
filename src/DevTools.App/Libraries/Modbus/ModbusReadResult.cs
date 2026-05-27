namespace DevTools.App.Libraries.Modbus;

internal sealed class ModbusReadResult
{
    public required ModbusReadRequest Request { get; init; }

    public required ushort[] Values { get; init; }
}
