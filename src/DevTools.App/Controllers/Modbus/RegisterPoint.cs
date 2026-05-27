namespace DevTools.App.Controllers.Modbus;

internal sealed class RegisterPoint
{
    public int RowIndex { get; init; }

    public ushort RegisterNumber { get; init; }

    public ushort Value { get; init; }
}
