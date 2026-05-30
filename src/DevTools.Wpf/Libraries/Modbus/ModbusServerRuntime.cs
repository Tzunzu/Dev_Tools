using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO.Ports;
using System.IO;
using DevTools.Wpf.Infrastructure.Logging;

namespace DevTools.Wpf.Libraries.Modbus;

internal enum ModbusDataArea
{
    Coils,
    DiscreteInputs,
    HoldingRegisters,
    InputRegisters
}

internal sealed class ModbusServerException : Exception
{
    public ModbusServerException(byte exceptionCode, string message)
        : base(message)
    {
        ExceptionCode = exceptionCode;
    }

    public byte ExceptionCode { get; }
}

internal static class ModbusCrc16
{
    public static ushort Compute(byte[] data, int length)
    {
        ushort crc = 0xFFFF;

        for (var i = 0; i < length; i++)
        {
            crc ^= data[i];
            for (var bit = 0; bit < 8; bit++)
            {
                var lsbSet = (crc & 0x0001) != 0;
                crc >>= 1;
                if (lsbSet)
                {
                    crc ^= 0xA001;
                }
            }
        }

        return crc;
    }
}

internal sealed class ModbusServerDataStore
{
    private readonly object sync = new();
    private readonly Dictionary<ushort, bool> coils = new();
    private readonly Dictionary<ushort, bool> discreteInputs = new();
    private readonly Dictionary<ushort, ushort> holdingRegisters = new();
    private readonly Dictionary<ushort, ushort> inputRegisters = new();

    public event Action? DataChanged;

    public void ReplaceBooleanArea(ModbusDataArea area, ushort startAddress, IReadOnlyList<bool> values)
    {
        if (area is not ModbusDataArea.Coils and not ModbusDataArea.DiscreteInputs)
        {
            throw new ArgumentOutOfRangeException(nameof(area), "Area must be Coils or DiscreteInputs.");
        }

        lock (sync)
        {
            ValidateAddressRange(startAddress, values.Count);
            var target = GetBooleanArea(area);
            target.Clear();

            for (var i = 0; i < values.Count; i++)
            {
                target[(ushort)(startAddress + i)] = values[i];
            }
        }

        DataChanged?.Invoke();
    }

    public void ReplaceRegisterArea(ModbusDataArea area, ushort startAddress, IReadOnlyList<ushort> values)
    {
        if (area is not ModbusDataArea.HoldingRegisters and not ModbusDataArea.InputRegisters)
        {
            throw new ArgumentOutOfRangeException(nameof(area), "Area must be HoldingRegisters or InputRegisters.");
        }

        lock (sync)
        {
            ValidateAddressRange(startAddress, values.Count);
            var target = GetRegisterArea(area);
            target.Clear();

            for (var i = 0; i < values.Count; i++)
            {
                target[(ushort)(startAddress + i)] = values[i];
            }
        }

        DataChanged?.Invoke();
    }

    public bool[] ReadBooleans(ModbusDataArea area, ushort startAddress, ushort count)
    {
        if (count == 0)
        {
            throw new ModbusServerException(0x03, "Quantity must be greater than zero.");
        }

        lock (sync)
        {
            ValidateAddressRange(startAddress, count);
            var source = GetBooleanArea(area);
            var result = new bool[count];
            for (var i = 0; i < count; i++)
            {
                var address = (ushort)(startAddress + i);
                if (!source.TryGetValue(address, out var value))
                {
                    throw new ModbusServerException(0x02, "Requested address is not mapped.");
                }

                result[i] = value;
            }

            return result;
        }
    }

    public ushort[] ReadRegisters(ModbusDataArea area, ushort startAddress, ushort count)
    {
        if (count == 0)
        {
            throw new ModbusServerException(0x03, "Quantity must be greater than zero.");
        }

        lock (sync)
        {
            ValidateAddressRange(startAddress, count);
            var source = GetRegisterArea(area);
            var result = new ushort[count];
            for (var i = 0; i < count; i++)
            {
                var address = (ushort)(startAddress + i);
                if (!source.TryGetValue(address, out var value))
                {
                    throw new ModbusServerException(0x02, "Requested address is not mapped.");
                }

                result[i] = value;
            }

            return result;
        }
    }

    public IReadOnlyList<KeyValuePair<ushort, bool>> GetMappedBooleans(ModbusDataArea area)
    {
        lock (sync)
        {
            var source = GetBooleanArea(area);
            return source
                .OrderBy(pair => pair.Key)
                .ToArray();
        }
    }

    public IReadOnlyList<KeyValuePair<ushort, ushort>> GetMappedRegisters(ModbusDataArea area)
    {
        lock (sync)
        {
            var source = GetRegisterArea(area);
            return source
                .OrderBy(pair => pair.Key)
                .ToArray();
        }
    }

    public void WriteSingleCoil(ushort address, bool value)
    {
        lock (sync)
        {
            if (!coils.ContainsKey(address))
            {
                throw new ModbusServerException(0x02, "Requested coil address is not mapped.");
            }

            coils[address] = value;
        }

        DataChanged?.Invoke();
    }

    public void WriteSingleRegister(ushort address, ushort value)
    {
        lock (sync)
        {
            if (!holdingRegisters.ContainsKey(address))
            {
                throw new ModbusServerException(0x02, "Requested register address is not mapped.");
            }

            holdingRegisters[address] = value;
        }

        DataChanged?.Invoke();
    }

    public void WriteMultipleCoils(ushort startAddress, IReadOnlyList<bool> values)
    {
        lock (sync)
        {
            ValidateAddressRange(startAddress, values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                var address = (ushort)(startAddress + i);
                if (!coils.ContainsKey(address))
                {
                    throw new ModbusServerException(0x02, "Requested coil address is not mapped.");
                }
            }

            for (var i = 0; i < values.Count; i++)
            {
                coils[(ushort)(startAddress + i)] = values[i];
            }
        }

        DataChanged?.Invoke();
    }

    public void WriteMultipleRegisters(ushort startAddress, IReadOnlyList<ushort> values)
    {
        lock (sync)
        {
            ValidateAddressRange(startAddress, values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                var address = (ushort)(startAddress + i);
                if (!holdingRegisters.ContainsKey(address))
                {
                    throw new ModbusServerException(0x02, "Requested register address is not mapped.");
                }
            }

            for (var i = 0; i < values.Count; i++)
            {
                holdingRegisters[(ushort)(startAddress + i)] = values[i];
            }
        }

        DataChanged?.Invoke();
    }

    private Dictionary<ushort, bool> GetBooleanArea(ModbusDataArea area)
    {
        return area switch
        {
            ModbusDataArea.Coils => coils,
            ModbusDataArea.DiscreteInputs => discreteInputs,
            _ => throw new ArgumentOutOfRangeException(nameof(area), "Area must be Coils or DiscreteInputs.")
        };
    }

    private Dictionary<ushort, ushort> GetRegisterArea(ModbusDataArea area)
    {
        return area switch
        {
            ModbusDataArea.HoldingRegisters => holdingRegisters,
            ModbusDataArea.InputRegisters => inputRegisters,
            _ => throw new ArgumentOutOfRangeException(nameof(area), "Area must be HoldingRegisters or InputRegisters.")
        };
    }

    private static void ValidateAddressRange(ushort startAddress, int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return;
        }

        var endAddress = startAddress + count - 1;
        if (endAddress > ushort.MaxValue)
        {
            throw new ModbusServerException(0x02, "Requested address range is out of bounds.");
        }
    }
}

internal sealed class ModbusRtuServerRuntime : IDisposable
{
    private readonly ModbusServerDataStore dataStore;
    private readonly SerialPort serialPort = new();

    private CancellationTokenSource? serverCancellation;
    private Task? serverTask;
    private byte unitId;

    public ModbusRtuServerRuntime(ModbusServerDataStore dataStore)
    {
        this.dataStore = dataStore;
    }

    public bool IsRunning => serialPort.IsOpen;

    public event Action<string>? Log;

    public void Start(string portName, int baudRate, Parity parity, int dataBits, StopBits stopBits, bool rtsEnable, bool dtrEnable, byte unitId)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Server is already running.");
        }

        this.unitId = unitId;
        serialPort.PortName = portName;
        serialPort.BaudRate = baudRate;
        serialPort.Parity = parity;
        serialPort.DataBits = dataBits;
        serialPort.StopBits = stopBits;
        serialPort.RtsEnable = rtsEnable;
        serialPort.DtrEnable = dtrEnable;
        serialPort.ReadTimeout = 50;
        serialPort.WriteTimeout = 500;

        serialPort.Open();

        serverCancellation = new CancellationTokenSource();
        serverTask = Task.Run(() => RunServerLoopAsync(serverCancellation.Token));

        Log?.Invoke("[rtu-server] started on " + portName + ", unit " + unitId);
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

        if (DiagnosticsSettings.IsDebugEnabled)
        {
            Log?.Invoke("[rtu-server][rx] " + FormatFrame(frame));
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

        try
        {
            var response = HandleRequest(frame);
            if (broadcast || response.Length == 0)
            {
                return;
            }

            serialPort.Write(response, 0, response.Length);
        }
        catch (ModbusServerException ex)
        {
            if (broadcast)
            {
                return;
            }

            var exceptionFrame = BuildExceptionResponse(requestUnitId, functionCode, ex.ExceptionCode);
            serialPort.Write(exceptionFrame, 0, exceptionFrame.Length);
        }
        catch
        {
            if (broadcast)
            {
                return;
            }

            var exceptionFrame = BuildExceptionResponse(requestUnitId, functionCode, 0x04);
            serialPort.Write(exceptionFrame, 0, exceptionFrame.Length);
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

            builder.Append(data[i].ToString("X2"));
        }

        return builder.ToString();
    }
}

internal sealed class ModbusTcpServerRuntime : IDisposable
{
    private readonly ModbusServerDataStore dataStore;
    private readonly List<Task> clientTasks = new();

    private TcpListener? listener;
    private CancellationTokenSource? cancellation;
    private Task? acceptTask;
    private byte unitId;

    public ModbusTcpServerRuntime(ModbusServerDataStore dataStore)
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

                if (DiagnosticsSettings.IsDebugEnabled)
                {
                    Log?.Invoke($"[tcp-server][rx] unit={requestUnitId} tx={transactionId} pdu={FormatBytes(requestPdu)}");
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

    private static string FormatBytes(byte[] data)
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

            builder.Append(data[i].ToString("X2"));
        }

        return builder.ToString();
    }
}
