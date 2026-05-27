namespace DevTools.App.Libraries.Com;

internal sealed class ComPortDeviceInfo
{
    public required string PortName { get; init; }

    public string FriendlyName { get; init; } = string.Empty;

    public bool IsSerialDevice { get; init; }

    public override string ToString()
    {
        var type = IsSerialDevice ? "Serial" : "Other";
        if (string.IsNullOrWhiteSpace(FriendlyName))
        {
            return PortName + " [" + type + "]";
        }

        return PortName + " [" + type + "] - " + FriendlyName;
    }
}
