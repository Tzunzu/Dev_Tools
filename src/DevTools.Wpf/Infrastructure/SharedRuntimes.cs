using DevTools.Wpf.Libraries.Modbus;

namespace DevTools.Wpf.Infrastructure;

/// <summary>
/// Holds shared server/client instances that should persist across view switches.
/// </summary>
internal sealed class SharedRuntimes
{
    private static readonly Lazy<SharedRuntimes> instance = new(() => new SharedRuntimes());

    internal static SharedRuntimes Instance => instance.Value;

    private readonly ModbusServerDataStore tcpServerDataStore = new();
    private readonly ModbusTcpServerRuntime tcpServer;
    private readonly ModbusServerDataStore rtuServerDataStore = new();
    private readonly ModbusRtuServerRuntime rtuServer;

    private SharedRuntimes()
    {
        tcpServer = new ModbusTcpServerRuntime(tcpServerDataStore);
        rtuServer = new ModbusRtuServerRuntime(rtuServerDataStore);
    }

    internal ModbusServerDataStore TcpServerDataStore => tcpServerDataStore;
    internal ModbusTcpServerRuntime TcpServer => tcpServer;
    internal ModbusServerDataStore RtuServerDataStore => rtuServerDataStore;
    internal ModbusRtuServerRuntime RtuServer => rtuServer;

    public void Cleanup()
    {
        if (tcpServer.IsRunning)
        {
            tcpServer.Stop();
        }

        if (rtuServer.IsRunning)
        {
            rtuServer.Stop();
        }

        tcpServer.Dispose();
        rtuServer.Dispose();
    }
}
