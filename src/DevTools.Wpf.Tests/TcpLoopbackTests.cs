using DevTools.Wpf.Libraries.Modbus;
using DevTools.Wpf.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Sockets;

namespace DevTools.Wpf.Tests;

[TestClass]
public sealed class TcpLoopbackTests
{
    [TestMethod]
    public async Task TcpClient_ReadsHoldingRegisters_FromLoopbackServer()
    {
        var dataStore = new ModbusServerDataStore();
        dataStore.ReplaceRegisterArea(ModbusDataArea.HoldingRegisters, 0, new ushort[] { 123, 456, 789 });

        using var server = new ModbusTcpServerRuntime(dataStore);
        using var client = new ModbusTcpTransport();

        var port = GetFreeTcpPort();
        server.Start(IPAddress.Loopback, port, unitId: 1);

        await ConnectWithRetryAsync(client, port);
        var values = await client.ReadAsync(unitId: 1, functionCode: 0x03, startAddress: 0, count: 3, CancellationToken.None);

        CollectionAssert.AreEqual(new ushort[] { 123, 456, 789 }, values);
    }

    private static async Task ConnectWithRetryAsync(ModbusTcpTransport client, int port)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await client.ConnectAsync("127.0.0.1", port, CancellationToken.None);
                return;
            }
            catch (SocketException ex)
            {
                lastError = ex;
                await Task.Delay(50);
            }
        }

        if (lastError is not null)
        {
            throw new AssertFailedException("Failed to connect TCP client to loopback server.", lastError);
        }

        throw new AssertFailedException("Failed to connect TCP client to loopback server.");
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}