using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DevTools.App.Libraries.Modbus;

internal sealed class ModbusTcpClient : IDisposable
{
    private readonly SemaphoreSlim ioLock = new(1, 1);
    private TcpClient? tcpClient;
    private NetworkStream? networkStream;
    private ushort nextTransactionId;

    public bool IsConnected => tcpClient?.Connected == true;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        Disconnect();

        var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken);

        tcpClient = client;
        networkStream = client.GetStream();
    }

    public void Disconnect()
    {
        try
        {
            networkStream?.Dispose();
        }
        catch
        {
            // Ignore close failures.
        }

        try
        {
            tcpClient?.Dispose();
        }
        catch
        {
            // Ignore close failures.
        }

        networkStream = null;
        tcpClient = null;
    }

    public void Dispose()
    {
        Disconnect();
        ioLock.Dispose();
    }

    public async Task<ushort[]> ReadAsync(byte unitId, byte functionCode, ushort startAddress, ushort count, CancellationToken cancellationToken)
    {
        if (functionCode is not 0x01 and not 0x02 and not 0x03 and not 0x04)
        {
            throw new NotSupportedException("Only read function codes 0x01, 0x02, 0x03, and 0x04 are supported.");
        }

        var requestPdu = new byte[5];
        requestPdu[0] = functionCode;
        requestPdu[1] = (byte)(startAddress >> 8);
        requestPdu[2] = (byte)(startAddress & 0xFF);
        requestPdu[3] = (byte)(count >> 8);
        requestPdu[4] = (byte)(count & 0xFF);

        var responsePdu = await ExecuteRequestAsync(unitId, requestPdu, cancellationToken);
        if (responsePdu.Length < 2)
        {
            throw new InvalidOperationException("Modbus TCP response is too short.");
        }

        if (responsePdu[0] != functionCode)
        {
            throw new InvalidOperationException("Unexpected function code in Modbus TCP response.");
        }

        var byteCount = responsePdu[1];
        if (responsePdu.Length != byteCount + 2)
        {
            throw new InvalidOperationException("Modbus TCP read response byte count mismatch.");
        }

        if (functionCode is 0x01 or 0x02)
        {
            var values = new ushort[count];
            for (var i = 0; i < count; i++)
            {
                var packed = responsePdu[2 + (i / 8)];
                values[i] = (ushort)((packed >> (i % 8)) & 0x01);
            }

            return values;
        }

        if (byteCount != count * 2)
        {
            throw new InvalidOperationException("Modbus TCP register response byte count mismatch.");
        }

        var registers = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            registers[i] = (ushort)((responsePdu[2 + (i * 2)] << 8) | responsePdu[3 + (i * 2)]);
        }

        return registers;
    }

    private async Task<byte[]> ExecuteRequestAsync(byte unitId, byte[] requestPdu, CancellationToken cancellationToken)
    {
        if (networkStream is null || tcpClient is null || !tcpClient.Connected)
        {
            throw new InvalidOperationException("TCP client is not connected.");
        }

        await ioLock.WaitAsync(cancellationToken);
        try
        {
            var transactionId = unchecked(++nextTransactionId);
            var frame = BuildFrame(transactionId, unitId, requestPdu);
            await networkStream.WriteAsync(frame.AsMemory(0, frame.Length), cancellationToken);
            await networkStream.FlushAsync(cancellationToken);

            var header = await ReadExactAsync(networkStream, 7, cancellationToken);
            var responseTransactionId = (ushort)((header[0] << 8) | header[1]);
            var protocolId = (ushort)((header[2] << 8) | header[3]);
            var length = (ushort)((header[4] << 8) | header[5]);
            if (responseTransactionId != transactionId)
            {
                throw new InvalidOperationException("Transaction ID mismatch in Modbus TCP response.");
            }

            if (protocolId != 0)
            {
                throw new InvalidOperationException("Invalid protocol ID in Modbus TCP response.");
            }

            if (length < 2)
            {
                throw new InvalidOperationException("Invalid Modbus TCP response length.");
            }

            var pduLength = length - 1;
            var responsePdu = await ReadExactAsync(networkStream, pduLength, cancellationToken);

            if ((responsePdu[0] & 0x80) == 0x80)
            {
                var exceptionCode = responsePdu.Length > 1 ? responsePdu[1] : (byte)0;
                throw new InvalidOperationException("Modbus exception response: 0x" + exceptionCode.ToString("X2"));
            }

            return responsePdu;
        }
        finally
        {
            ioLock.Release();
        }
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

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Connection closed while reading Modbus TCP response.");
            }

            offset += read;
        }

        return buffer;
    }
}
