using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace DevTools.Wpf.Libraries.Com;

internal static class ComPortDiscovery
{
    private static readonly Regex ComNameRegex = new(@"\((COM\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<ComPortDeviceInfo> Discover()
    {
        var ports = SerialPort.GetPortNames()
            .OrderBy(port => port, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var metadataByPort = GetDeviceMetadataByPort();

        var results = new List<ComPortDeviceInfo>(ports.Length);
        foreach (var port in ports)
        {
            metadataByPort.TryGetValue(port, out var metadata);

            results.Add(new ComPortDeviceInfo
            {
                PortName = port,
                FriendlyName = metadata.FriendlyName,
                IsSerialDevice = metadata.IsSerialDevice
            });
        }

        return results;
    }

    private static Dictionary<string, (string FriendlyName, bool IsSerialDevice)> GetDeviceMetadataByPort()
    {
        var result = new Dictionary<string, (string FriendlyName, bool IsSerialDevice)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, PNPClass FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (var entry in searcher.Get())
            {
                using var obj = (ManagementObject)entry;

                var name = obj["Name"]?.ToString() ?? string.Empty;
                var pnpClass = obj["PNPClass"]?.ToString() ?? string.Empty;
                var match = ComNameRegex.Match(name);
                if (!match.Success)
                {
                    continue;
                }

                var portName = match.Groups[1].Value.ToUpperInvariant();
                var isSerial = pnpClass.Equals("Ports", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("serial", StringComparison.OrdinalIgnoreCase);

                result[portName] = (FriendlyName: name, IsSerialDevice: isSerial);
            }
        }
        catch
        {
            // Fallback to unknown metadata when WMI is unavailable.
        }

        return result;
    }
}
