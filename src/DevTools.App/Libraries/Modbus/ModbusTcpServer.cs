using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DevTools.App.Libraries.Modbus;

internal sealed class ModbusTcpServer : IDisposable
{
    private readonly ModbusRtuServerDataStore dataStore;
    private readonly List<Task> clientTasks = new();

    private TcpListener? listener;
    private CancellationTokenSource? cancellation;
    private Task? acceptTask;
    private byte unitId;

    public ModbusTcpServer(ModbusRtuServerDataStore dataStore)
    {
        this.dataStore = dataStore;
    }

    public bool IsRunning => listener is not null;

    public event Action<string>? Log;

    public void Start(IPAddress bindAddress, int port, byte unitId)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("TCP server is already running.");
        }

        this.unitId = unitId;
        listener = new TcpListener(bindAddress, port);
        listener.Start();

        cancellation = new CancellationTokenSource();
        acceptTask = Task.Run(() => AcceptLoopAsync(cancellation.Token));

        Log?.Invoke("[tcp-server] started on " + bindAddress + ":" + port + ", unit " + unitId);
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        cancellation?.Cancel();

        try
        {
            listener?.Stop();
        }
        catch
        {
            // Ignore close failures.
        }

        try
        {
            acceptTask?.Wait(500);
        }
        catch
        {
            // Ignore shutdown races.
        }

        Task[] tasks;
        lock (clientTasks)
        {
            tasks = clientTasks.ToArray();
            clientTasks.Clear();
        }

        try
        {
            Task.WaitAll(tasks, 500);
        }
        catch
        {
            // Ignore shutdown races.
        }

        acceptTask = null;
        listener = null;
        cancellation?.Dispose();
        cancellation = null;

        Log?.Invoke("[tcp-server] stopped");
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                if (listener is null)
                {
                    return;
                }

                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log?.Invoke("[tcp-server] accept error: " + ex.Message);
                await Task.Delay(50, cancellationToken);
                continue;
            }

            var task = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            lock (clientTasks)
            {
                clientTasks.Add(task);
            }

            _ = task.ContinueWith(_ =>
            {
                lock (clientTasks)
                {
                    clientTasks.Remove(task);
                }
            }, TaskScheduler.Default);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            using var stream = client.GetStream();

            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] header;
                try
                {
                    header = await ReadExactAsync(stream, 7, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    return;
                }

                var transactionId = (ushort)((header[0] << 8) | header[1]);
                var protocolId = (ushort)((header[2] << 8) | header[3]);
                var length = (ushort)((header[4] << 8) | header[5]);
                var requestUnitId = header[6];

                if (protocolId != 0 || length < 2)
                {
                    return;
                }

                var pduLength = length - 1;
                byte[] requestPdu;
                try
                {
                    requestPdu = await ReadExactAsync(stream, pduLength, cancellationToken);
                }
                catch (IOException)
                {
                    return;
                }

                if (requestPdu.Length == 0)
                {
                    continue;
                }

                if (requestUnitId != unitId)
                {
                    var mismatch = BuildExceptionPdu(requestPdu[0], 0x0B);
                    await WriteResponseAsync(stream, transactionId, requestUnitId, mismatch, cancellationToken);
                    continue;
                }

                try
                {
                    var responsePdu = HandleRequest(requestPdu);
                    await WriteResponseAsync(stream, transactionId, requestUnitId, responsePdu, cancellationToken);
                }
                catch (ModbusServerException ex)
                {
                    var exceptionPdu = BuildExceptionPdu(requestPdu[0], ex.ExceptionCode);
                    await WriteResponseAsync(stream, transactionId, requestUnitId, exceptionPdu, cancellationToken);
                }
                catch (Exception ex)
                {
                    Log?.Invoke("[tcp-server] request error: " + ex.Message);
                    var exceptionPdu = BuildExceptionPdu(requestPdu[0], 0x04);
                    await WriteResponseAsync(stream, transactionId, requestUnitId, exceptionPdu, cancellationToken);
                }
            }
        }
    }

    private byte[] HandleRequest(byte[] requestPdu)
    {
        var functionCode = requestPdu[0];

        return functionCode switch
        {
            0x01 => HandleReadBits(requestPdu, ModbusDataArea.Coils),
            0x02 => HandleReadBits(requestPdu, ModbusDataArea.DiscreteInputs),
            0x03 => HandleReadRegisters(requestPdu, ModbusDataArea.HoldingRegisters),
            0x04 => HandleReadRegisters(requestPdu, ModbusDataArea.InputRegisters),
            0x05 => HandleWriteSingleCoil(requestPdu),
            0x06 => HandleWriteSingleRegister(requestPdu),
            0x0F => HandleWriteMultipleCoils(requestPdu),
            0x10 => HandleWriteMultipleRegisters(requestPdu),
            _ => throw new ModbusServerException(0x01, "Unsupported function code.")
        };
    }

    private byte[] HandleReadBits(byte[] requestPdu, ModbusDataArea area)
    {
        EnsurePduLength(requestPdu, 5);

        var startAddress = ReadUShort(requestPdu, 1);
        var quantity = ReadUShort(requestPdu, 3);
        if (quantity == 0 || quantity > 2000)
        {
            throw new ModbusServerException(0x03, "Invalid quantity for bit read.");
        }

        var values = dataStore.ReadBooleans(area, startAddress, quantity);
        var byteCount = (values.Length + 7) / 8;
        var response = new byte[2 + byteCount];
        response[0] = requestPdu[0];
        response[1] = (byte)byteCount;

        for (var i = 0; i < values.Length; i++)
        {
            if (values[i])
            {
                response[2 + (i / 8)] |= (byte)(1 << (i % 8));
            }
        }

        return response;
    }

    private byte[] HandleReadRegisters(byte[] requestPdu, ModbusDataArea area)
    {
        EnsurePduLength(requestPdu, 5);

        var startAddress = ReadUShort(requestPdu, 1);
        var quantity = ReadUShort(requestPdu, 3);
        if (quantity == 0 || quantity > 125)
        {
            throw new ModbusServerException(0x03, "Invalid quantity for register read.");
        }

        var values = dataStore.ReadRegisters(area, startAddress, quantity);
        var response = new byte[2 + (values.Length * 2)];
        response[0] = requestPdu[0];
        response[1] = (byte)(values.Length * 2);

        for (var i = 0; i < values.Length; i++)
        {
            response[2 + (i * 2)] = (byte)(values[i] >> 8);
            response[3 + (i * 2)] = (byte)(values[i] & 0xFF);
        }

        return response;
    }

    private byte[] HandleWriteSingleCoil(byte[] requestPdu)
    {
        EnsurePduLength(requestPdu, 5);

        var address = ReadUShort(requestPdu, 1);
        var encoded = ReadUShort(requestPdu, 3);
        var value = encoded switch
        {
            0xFF00 => true,
            0x0000 => false,
            _ => throw new ModbusServerException(0x03, "Invalid single-coil value.")
        };

        dataStore.WriteSingleCoil(address, value);

        var response = new byte[5];
        Buffer.BlockCopy(requestPdu, 0, response, 0, 5);
        return response;
    }

    private byte[] HandleWriteSingleRegister(byte[] requestPdu)
    {
        EnsurePduLength(requestPdu, 5);

        var address = ReadUShort(requestPdu, 1);
        var value = ReadUShort(requestPdu, 3);

        dataStore.WriteSingleRegister(address, value);

        var response = new byte[5];
        Buffer.BlockCopy(requestPdu, 0, response, 0, 5);
        return response;
    }

    private byte[] HandleWriteMultipleCoils(byte[] requestPdu)
    {
        if (requestPdu.Length < 6)
        {
            throw new ModbusServerException(0x03, "Frame is too short for write-multiple-coils.");
        }

        var startAddress = ReadUShort(requestPdu, 1);
        var quantity = ReadUShort(requestPdu, 3);
        var byteCount = requestPdu[5];

        if (quantity == 0 || quantity > 1968)
        {
            throw new ModbusServerException(0x03, "Invalid quantity for write-multiple-coils.");
        }

        var expectedByteCount = (quantity + 7) / 8;
        if (byteCount != expectedByteCount)
        {
            throw new ModbusServerException(0x03, "Coil byte count does not match quantity.");
        }

        if (requestPdu.Length != 6 + byteCount)
        {
            throw new ModbusServerException(0x03, "Unexpected write-multiple-coils frame length.");
        }

        var values = new bool[quantity];
        for (var i = 0; i < quantity; i++)
        {
            var packed = requestPdu[6 + (i / 8)];
            values[i] = ((packed >> (i % 8)) & 0x01) == 0x01;
        }

        dataStore.WriteMultipleCoils(startAddress, values);

        return
        [
            requestPdu[0],
            requestPdu[1],
            requestPdu[2],
            requestPdu[3],
            requestPdu[4]
        ];
    }

    private byte[] HandleWriteMultipleRegisters(byte[] requestPdu)
    {
        if (requestPdu.Length < 6)
        {
            throw new ModbusServerException(0x03, "Frame is too short for write-multiple-registers.");
        }

        var startAddress = ReadUShort(requestPdu, 1);
        var quantity = ReadUShort(requestPdu, 3);
        var byteCount = requestPdu[5];

        if (quantity == 0 || quantity > 123)
        {
            throw new ModbusServerException(0x03, "Invalid quantity for write-multiple-registers.");
        }

        if (byteCount != quantity * 2)
        {
            throw new ModbusServerException(0x03, "Register byte count does not match quantity.");
        }

        if (requestPdu.Length != 6 + byteCount)
        {
            throw new ModbusServerException(0x03, "Unexpected write-multiple-registers frame length.");
        }

        var values = new ushort[quantity];
        for (var i = 0; i < quantity; i++)
        {
            values[i] = ReadUShort(requestPdu, 6 + (i * 2));
        }

        dataStore.WriteMultipleRegisters(startAddress, values);

        return
        [
            requestPdu[0],
            requestPdu[1],
            requestPdu[2],
            requestPdu[3],
            requestPdu[4]
        ];
    }

    private static async Task WriteResponseAsync(NetworkStream stream, ushort transactionId, byte unitId, byte[] pdu, CancellationToken cancellationToken)
    {
        var frame = BuildFrame(transactionId, unitId, pdu);
        await stream.WriteAsync(frame.AsMemory(0, frame.Length), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static byte[] BuildExceptionPdu(byte functionCode, byte exceptionCode)
    {
        return [(byte)(functionCode | 0x80), exceptionCode];
    }

    private static byte[] BuildFrame(ushort transactionId, byte unitId, byte[] pdu)
    {
        var frame = new byte[7 + pdu.Length];
        frame[0] = (byte)(transactionId >> 8);
        frame[1] = (byte)(transactionId & 0xFF);
        frame[2] = 0;
        frame[3] = 0;
        var length = (ushort)(pdu.Length + 1);
        frame[4] = (byte)(length >> 8);
        frame[5] = (byte)(length & 0xFF);
        frame[6] = unitId;
        Buffer.BlockCopy(pdu, 0, frame, 7, pdu.Length);
        return frame;
    }

    private static ushort ReadUShort(byte[] data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static void EnsurePduLength(byte[] pdu, int expectedLength)
    {
        if (pdu.Length != expectedLength)
        {
            throw new ModbusServerException(0x03, "Unexpected frame length.");
        }
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
            {
                throw new IOException("Connection closed while reading request.");
            }

            offset += read;
        }

        return buffer;
    }
}
