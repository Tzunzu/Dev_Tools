using DevTools.App.Libraries.Com;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DevTools.App.Libraries.Modbus;

internal sealed class ModbusRtuServer : IDisposable
{
    private readonly ModbusRtuServerDataStore dataStore;
    private readonly SerialPort serialPort = new();

    private CancellationTokenSource? serverCancellation;
    private Task? serverTask;
    private byte unitId;

    public ModbusRtuServer(ModbusRtuServerDataStore dataStore)
    {
        this.dataStore = dataStore;
    }

    public bool IsRunning => serialPort.IsOpen;

    public event Action<string>? Log;

    public void Start(SerialPortSettings settings, byte unitId)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Server is already running.");
        }

        this.unitId = unitId;
        serialPort.PortName = settings.PortName;
        serialPort.BaudRate = settings.BaudRate;
        serialPort.Parity = settings.Parity;
        serialPort.DataBits = settings.DataBits;
        serialPort.StopBits = settings.StopBits;
        serialPort.RtsEnable = settings.RtsEnable;
        serialPort.DtrEnable = settings.DtrEnable;
        serialPort.ReadTimeout = 50;
        serialPort.WriteTimeout = 500;

        serialPort.Open();

        serverCancellation = new CancellationTokenSource();
        serverTask = Task.Run(() => RunServerLoopAsync(serverCancellation.Token));

        Log?.Invoke("[rtu-server] started on " + settings.PortName + ", unit " + unitId.ToString(CultureInfo.InvariantCulture));
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        serverCancellation?.Cancel();

        try
        {
            serialPort.Close();
        }
        catch
        {
            // Ignore close errors while stopping.
        }

        try
        {
            serverTask?.Wait(500);
        }
        catch
        {
            // Ignore cancellation/IO completion races while stopping.
        }

        serverCancellation?.Dispose();
        serverCancellation = null;
        serverTask = null;

        Log?.Invoke("[rtu-server] stopped");
    }

    public void Dispose()
    {
        Stop();
        serialPort.Dispose();
    }

    private async Task RunServerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[] frame;
            try
            {
                frame = await ReadFrameAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log?.Invoke("[rtu-server] read error: " + ex.Message);
                await Task.Delay(25, cancellationToken);
                continue;
            }

            if (frame.Length == 0)
            {
                continue;
            }

            try
            {
                ProcessFrame(frame);
            }
            catch (Exception ex)
            {
                Log?.Invoke("[rtu-server] frame error: " + ex.Message);
            }
        }
    }

    private async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken)
    {
        var frameBytes = new List<byte>(256);

        while (!cancellationToken.IsCancellationRequested)
        {
            var available = serialPort.BytesToRead;
            if (available > 0)
            {
                ReadInto(frameBytes, available);
                break;
            }

            await Task.Delay(5, cancellationToken);
        }

        var lastByteAt = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            var available = serialPort.BytesToRead;
            if (available > 0)
            {
                ReadInto(frameBytes, available);
                lastByteAt = DateTime.UtcNow;
            }
            else if ((DateTime.UtcNow - lastByteAt).TotalMilliseconds >= 20)
            {
                break;
            }

            await Task.Delay(2, cancellationToken);
        }

        return frameBytes.ToArray();
    }

    private void ReadInto(List<byte> buffer, int available)
    {
        if (available <= 0)
        {
            return;
        }

        var chunk = new byte[available];
        var read = serialPort.Read(chunk, 0, available);
        if (read <= 0)
        {
            return;
        }

        buffer.AddRange(chunk.Take(read));
    }

    private void ProcessFrame(byte[] frame)
    {
        if (frame.Length < 4)
        {
            return;
        }

        if (!IsCrcValid(frame))
        {
            Log?.Invoke("[rtu-server] crc failed: " + FormatFrame(frame));
            return;
        }

        var requestUnitId = frame[0];
        var functionCode = frame[1];
        var broadcast = requestUnitId == 0;

        if (!broadcast && requestUnitId != unitId)
        {
            return;
        }

        Log?.Invoke("[rtu-server] rx " + FormatFrame(frame));

        try
        {
            var response = HandleRequest(frame);
            if (broadcast || response.Length == 0)
            {
                return;
            }

            serialPort.Write(response, 0, response.Length);
            Log?.Invoke("[rtu-server] tx " + FormatFrame(response));
        }
        catch (ModbusServerException ex)
        {
            if (broadcast)
            {
                return;
            }

            var exceptionFrame = BuildExceptionResponse(requestUnitId, functionCode, ex.ExceptionCode);
            serialPort.Write(exceptionFrame, 0, exceptionFrame.Length);
            Log?.Invoke("[rtu-server] tx " + FormatFrame(exceptionFrame));
        }
        catch
        {
            if (broadcast)
            {
                return;
            }

            var exceptionFrame = BuildExceptionResponse(requestUnitId, functionCode, 0x04);
            serialPort.Write(exceptionFrame, 0, exceptionFrame.Length);
            Log?.Invoke("[rtu-server] tx " + FormatFrame(exceptionFrame));
        }
    }

    private byte[] HandleRequest(byte[] request)
    {
        var functionCode = request[1];

        return functionCode switch
        {
            0x01 => HandleReadBits(request, ModbusDataArea.Coils),
            0x02 => HandleReadBits(request, ModbusDataArea.DiscreteInputs),
            0x03 => HandleReadRegisters(request, ModbusDataArea.HoldingRegisters),
            0x04 => HandleReadRegisters(request, ModbusDataArea.InputRegisters),
            0x05 => HandleWriteSingleCoil(request),
            0x06 => HandleWriteSingleRegister(request),
            0x0F => HandleWriteMultipleCoils(request),
            0x10 => HandleWriteMultipleRegisters(request),
            _ => throw new ModbusServerException(0x01, "Unsupported function code.")
        };
    }

    private byte[] HandleReadBits(byte[] request, ModbusDataArea area)
    {
        EnsureFrameLength(request, 8);

        var startAddress = ReadUShort(request, 2);
        var quantity = ReadUShort(request, 4);
        if (quantity == 0 || quantity > 2000)
        {
            throw new ModbusServerException(0x03, "Invalid quantity for bit read.");
        }

        var values = dataStore.ReadBooleans(area, startAddress, quantity);
        var byteCount = (values.Length + 7) / 8;
        var response = new byte[3 + byteCount + 2];
        response[0] = request[0];
        response[1] = request[1];
        response[2] = (byte)byteCount;

        for (var i = 0; i < values.Length; i++)
        {
            if (values[i])
            {
                response[3 + (i / 8)] |= (byte)(1 << (i % 8));
            }
        }

        AppendCrc(response, response.Length - 2);
        return response;
    }

    private byte[] HandleReadRegisters(byte[] request, ModbusDataArea area)
    {
        EnsureFrameLength(request, 8);

        var startAddress = ReadUShort(request, 2);
        var quantity = ReadUShort(request, 4);
        if (quantity == 0 || quantity > 125)
        {
            throw new ModbusServerException(0x03, "Invalid quantity for register read.");
        }

        var values = dataStore.ReadRegisters(area, startAddress, quantity);
        var byteCount = values.Length * 2;
        var response = new byte[3 + byteCount + 2];
        response[0] = request[0];
        response[1] = request[1];
        response[2] = (byte)byteCount;

        for (var i = 0; i < values.Length; i++)
        {
            response[3 + (i * 2)] = (byte)(values[i] >> 8);
            response[4 + (i * 2)] = (byte)(values[i] & 0xFF);
        }

        AppendCrc(response, response.Length - 2);
        return response;
    }

    private byte[] HandleWriteSingleCoil(byte[] request)
    {
        EnsureFrameLength(request, 8);

        var address = ReadUShort(request, 2);
        var encodedValue = ReadUShort(request, 4);
        var value = encodedValue switch
        {
            0xFF00 => true,
            0x0000 => false,
            _ => throw new ModbusServerException(0x03, "Invalid single-coil value.")
        };

        dataStore.WriteSingleCoil(address, value);
        return request.ToArray();
    }

    private byte[] HandleWriteSingleRegister(byte[] request)
    {
        EnsureFrameLength(request, 8);

        var address = ReadUShort(request, 2);
        var value = ReadUShort(request, 4);

        dataStore.WriteSingleRegister(address, value);
        return request.ToArray();
    }

    private byte[] HandleWriteMultipleCoils(byte[] request)
    {
        if (request.Length < 9)
        {
            throw new ModbusServerException(0x03, "Frame is too short for write-multiple-coils.");
        }

        var startAddress = ReadUShort(request, 2);
        var quantity = ReadUShort(request, 4);
        var byteCount = request[6];

        if (quantity == 0 || quantity > 1968)
        {
            throw new ModbusServerException(0x03, "Invalid quantity for write-multiple-coils.");
        }

        var expectedByteCount = (quantity + 7) / 8;
        if (byteCount != expectedByteCount)
        {
            throw new ModbusServerException(0x03, "Coil byte count does not match quantity.");
        }

        var expectedFrameLength = 9 + byteCount;
        EnsureFrameLength(request, expectedFrameLength);

        var values = new bool[quantity];
        for (var i = 0; i < quantity; i++)
        {
            var packed = request[7 + (i / 8)];
            values[i] = ((packed >> (i % 8)) & 0x01) == 0x01;
        }

        dataStore.WriteMultipleCoils(startAddress, values);

        var response = new byte[8];
        response[0] = request[0];
        response[1] = request[1];
        response[2] = request[2];
        response[3] = request[3];
        response[4] = request[4];
        response[5] = request[5];
        AppendCrc(response, 6);
        return response;
    }

    private byte[] HandleWriteMultipleRegisters(byte[] request)
    {
        if (request.Length < 9)
        {
            throw new ModbusServerException(0x03, "Frame is too short for write-multiple-registers.");
        }

        var startAddress = ReadUShort(request, 2);
        var quantity = ReadUShort(request, 4);
        var byteCount = request[6];

        if (quantity == 0 || quantity > 123)
        {
            throw new ModbusServerException(0x03, "Invalid quantity for write-multiple-registers.");
        }

        if (byteCount != quantity * 2)
        {
            throw new ModbusServerException(0x03, "Register byte count does not match quantity.");
        }

        var expectedFrameLength = 9 + byteCount;
        EnsureFrameLength(request, expectedFrameLength);

        var values = new ushort[quantity];
        for (var i = 0; i < quantity; i++)
        {
            values[i] = ReadUShort(request, 7 + (i * 2));
        }

        dataStore.WriteMultipleRegisters(startAddress, values);

        var response = new byte[8];
        response[0] = request[0];
        response[1] = request[1];
        response[2] = request[2];
        response[3] = request[3];
        response[4] = request[4];
        response[5] = request[5];
        AppendCrc(response, 6);
        return response;
    }

    private static byte[] BuildExceptionResponse(byte slaveId, byte functionCode, byte exceptionCode)
    {
        var response = new byte[5];
        response[0] = slaveId;
        response[1] = (byte)(functionCode | 0x80);
        response[2] = exceptionCode;
        AppendCrc(response, 3);
        return response;
    }

    private static bool IsCrcValid(byte[] frame)
    {
        var crcFromFrame = (ushort)((frame[^1] << 8) | frame[^2]);
        var crcComputed = ModbusCrc16.Compute(frame, frame.Length - 2);
        return crcFromFrame == crcComputed;
    }

    private static ushort ReadUShort(byte[] frame, int offset)
    {
        return (ushort)((frame[offset] << 8) | frame[offset + 1]);
    }

    private static void EnsureFrameLength(byte[] frame, int expectedLength)
    {
        if (frame.Length != expectedLength)
        {
            throw new ModbusServerException(0x03, "Unexpected frame length.");
        }
    }

    private static void AppendCrc(byte[] frame, int lengthWithoutCrc)
    {
        var crc = ModbusCrc16.Compute(frame, lengthWithoutCrc);
        frame[lengthWithoutCrc] = (byte)(crc & 0xFF);
        frame[lengthWithoutCrc + 1] = (byte)(crc >> 8);
    }

    private static string FormatFrame(byte[] data)
    {
        if (data.Length == 0)
        {
            return "<empty>";
        }

        var builder = new StringBuilder(data.Length * 3);
        for (var i = 0; i < data.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
