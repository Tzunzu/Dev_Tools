using System.IO.Ports;

namespace DevTools.App.Libraries.Com;

internal sealed class SerialPortSettings
{
    public string PortName { get; set; } = "COM1";

    public int BaudRate { get; set; } = 19200;

    public Parity Parity { get; set; } = Parity.None;

    public int DataBits { get; set; } = 8;

    public StopBits StopBits { get; set; } = StopBits.One;

    public bool RtsEnable { get; set; }

    public bool DtrEnable { get; set; }
}
