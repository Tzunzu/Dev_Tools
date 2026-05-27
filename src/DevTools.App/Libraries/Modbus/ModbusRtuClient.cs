using DevTools.App.Libraries.Com;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DevTools.App.Libraries.Modbus;

internal sealed class ModbusRtuClient : IModbusRtuClient
{
    private readonly ISerialPortService serialPortService;

    public ModbusRtuClient(ISerialPortService serialPortService)
    {
        this.serialPortService = serialPortService;
    }

    public async Task<ModbusReadResult> ReadAsync(ModbusReadRequest request, CancellationToken cancellationToken)
    {
        if (request.FunctionCode is not 0x01 and not 0x02 and not 0x03 and not 0x04)
        {
            throw new NotSupportedException("Only function codes 0x01, 0x02, 0x03, and 0x04 are implemented in this starter layer.");
        }

        try
        {
            var frame = BuildReadFrame(request);
            Console.WriteLine("[raw] [request] " + FormatFrame(frame));
            await serialPortService.WriteAsync(frame, cancellationToken);

            var dataByteCount = GetExpectedDataByteCount(request);
            var expectedByteCount = 5 + dataByteCount;
            var response = await serialPortService.ReadAsync(expectedByteCount, timeoutMs: 2000, cancellationToken);
            Console.WriteLine("[raw] [response] " + FormatFrame(response));

            ValidateReadResponse(request, response);

            return new ModbusReadResult
            {
                Request = request,
                Values = DecodeValues(request, response)
            };
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            serialPortService.DiscardInBuffer();
            throw;
        }
    }

    public async Task WriteSingleCoilAsync(byte slaveId, ushort address, bool value, CancellationToken cancellationToken)
    {
        try
        {
            var encodedValue = value ? (ushort)0xFF00 : (ushort)0x0000;
            var frame = BuildSingleWriteFrame(slaveId, 0x05, address, encodedValue);
            Console.WriteLine("[raw] [request] " + FormatFrame(frame));
            await serialPortService.WriteAsync(frame, cancellationToken);

            var response = await serialPortService.ReadAsync(8, timeoutMs: 2000, cancellationToken);
            Console.WriteLine("[raw] [response] " + FormatFrame(response));

            ValidateWriteEchoResponse(frame, response, slaveId, 0x05, address, encodedValue);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            serialPortService.DiscardInBuffer();
            throw;
        }
    }

    public async Task WriteSingleRegisterAsync(byte slaveId, ushort address, ushort value, CancellationToken cancellationToken)
    {
        try
        {
            var frame = BuildSingleWriteFrame(slaveId, 0x06, address, value);
            Console.WriteLine("[raw] [request] " + FormatFrame(frame));
            await serialPortService.WriteAsync(frame, cancellationToken);

            var response = await serialPortService.ReadAsync(8, timeoutMs: 2000, cancellationToken);
            Console.WriteLine("[raw] [response] " + FormatFrame(response));

            ValidateWriteEchoResponse(frame, response, slaveId, 0x06, address, value);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            serialPortService.DiscardInBuffer();
            throw;
        }
    }

    public async Task WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, IReadOnlyList<ushort> values, CancellationToken cancellationToken)
    {
        if (values.Count == 0)
        {
            throw new ArgumentException("At least one register value is required.", nameof(values));
        }

        if (values.Count > 123)
        {
            throw new ArgumentOutOfRangeException(nameof(values), "Modbus write multiple registers supports up to 123 registers per request.");
        }

        try
        {
            var frame = BuildWriteMultipleRegistersFrame(slaveId, startAddress, values);
            Console.WriteLine("[raw] [request] " + FormatFrame(frame));
            await serialPortService.WriteAsync(frame, cancellationToken);

            var response = await serialPortService.ReadAsync(8, timeoutMs: 2000, cancellationToken);
            Console.WriteLine("[raw] [response] " + FormatFrame(response));

            ValidateWriteMultipleRegistersResponse(response, slaveId, startAddress, (ushort)values.Count);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            serialPortService.DiscardInBuffer();
            throw;
        }
    }

    private static ushort[] DecodeValues(ModbusReadRequest request, byte[] response)
    {
        if (request.FunctionCode is 0x01 or 0x02)
        {
            var values = new ushort[request.RegisterCount];
            for (var i = 0; i < request.RegisterCount; i++)
            {
                var dataByte = response[3 + (i / 8)];
                values[i] = (ushort)((dataByte >> (i % 8)) & 0x01);
            }

            return values;
        }

        var registerValues = new ushort[request.RegisterCount];
        for (var i = 0; i < request.RegisterCount; i++)
        {
            var high = response[3 + (i * 2)];
            var low = response[4 + (i * 2)];
            registerValues[i] = (ushort)((high << 8) | low);
        }

        return registerValues;
    }

    private static byte[] BuildReadFrame(ModbusReadRequest request)
    {
        var frame = new byte[8];
        frame[0] = request.SlaveId;
        frame[1] = request.FunctionCode;
        frame[2] = (byte)(request.StartAddress >> 8);
        frame[3] = (byte)(request.StartAddress & 0xFF);
        frame[4] = (byte)(request.RegisterCount >> 8);
        frame[5] = (byte)(request.RegisterCount & 0xFF);

        AppendCrc(frame, 6);
        return frame;
    }

    private static byte[] BuildSingleWriteFrame(byte slaveId, byte functionCode, ushort address, ushort value)
    {
        var frame = new byte[8];
        frame[0] = slaveId;
        frame[1] = functionCode;
        frame[2] = (byte)(address >> 8);
        frame[3] = (byte)(address & 0xFF);
        frame[4] = (byte)(value >> 8);
        frame[5] = (byte)(value & 0xFF);

        AppendCrc(frame, 6);
        return frame;
    }

    private static byte[] BuildWriteMultipleRegistersFrame(byte slaveId, ushort startAddress, IReadOnlyList<ushort> values)
    {
        var byteCount = values.Count * 2;
        var frame = new byte[9 + byteCount];
        frame[0] = slaveId;
        frame[1] = 0x10;
        frame[2] = (byte)(startAddress >> 8);
        frame[3] = (byte)(startAddress & 0xFF);
        frame[4] = (byte)(values.Count >> 8);
        frame[5] = (byte)(values.Count & 0xFF);
        frame[6] = (byte)byteCount;

        for (var i = 0; i < values.Count; i++)
        {
            frame[7 + (i * 2)] = (byte)(values[i] >> 8);
            frame[8 + (i * 2)] = (byte)(values[i] & 0xFF);
        }

        AppendCrc(frame, frame.Length - 2);
        return frame;
    }

    private static void ValidateReadResponse(ModbusReadRequest request, byte[] response)
    {
        if (response.Length < 5)
        {
            throw new InvalidOperationException("Modbus response too short.");
        }

        if (response[0] != request.SlaveId)
        {
            throw new InvalidOperationException("Slave ID mismatch in Modbus response.");
        }

        if (response[1] != request.FunctionCode)
        {
            throw new InvalidOperationException("Function code mismatch in Modbus response.");
        }

        var dataByteCount = response[2];
        if (dataByteCount != GetExpectedDataByteCount(request))
        {
            throw new InvalidOperationException("Modbus response byte count does not match request.");
        }

        if (response.Length != dataByteCount + 5)
        {
            throw new InvalidOperationException("Modbus response length does not match the declared byte count.");
        }

        ValidateFrameCrc(response);
    }

    private static void ValidateWriteEchoResponse(byte[] requestFrame, byte[] response, byte slaveId, byte functionCode, ushort address, ushort value)
    {
        ValidateCommonWriteResponse(response, slaveId, functionCode);

        if (response.Length != 8)
        {
            throw new InvalidOperationException("Modbus write response length is invalid.");
        }

        var responseAddress = (ushort)((response[2] << 8) | response[3]);
        var responseValue = (ushort)((response[4] << 8) | response[5]);
        if (responseAddress != address || responseValue != value)
        {
            throw new InvalidOperationException("Modbus write response does not match the request.");
        }

        ValidateFrameCrc(response);

        for (var i = 0; i < response.Length; i++)
        {
            if (response[i] != requestFrame[i])
            {
                throw new InvalidOperationException("Modbus write echo response differs from the transmitted frame.");
            }
        }
    }

    private static void ValidateWriteMultipleRegistersResponse(byte[] response, byte slaveId, ushort startAddress, ushort registerCount)
    {
        ValidateCommonWriteResponse(response, slaveId, 0x10);

        if (response.Length != 8)
        {
            throw new InvalidOperationException("Modbus write multiple registers response length is invalid.");
        }

        var responseAddress = (ushort)((response[2] << 8) | response[3]);
        var responseCount = (ushort)((response[4] << 8) | response[5]);
        if (responseAddress != startAddress || responseCount != registerCount)
        {
            throw new InvalidOperationException("Modbus write multiple registers response does not match the request.");
        }

        ValidateFrameCrc(response);
    }

    private static void ValidateCommonWriteResponse(byte[] response, byte slaveId, byte functionCode)
    {
        if (response.Length < 5)
        {
            throw new InvalidOperationException("Modbus write response too short.");
        }

        if (response[0] != slaveId)
        {
            throw new InvalidOperationException("Slave ID mismatch in Modbus write response.");
        }

        if (response[1] != functionCode)
        {
            throw new InvalidOperationException("Function code mismatch in Modbus write response.");
        }
    }

    private static void ValidateFrameCrc(byte[] frame)
    {
        var crcFromFrame = (ushort)((frame[^1] << 8) | frame[^2]);
        var crcComputed = ModbusCrc16.Compute(frame, frame.Length - 2);
        if (crcFromFrame != crcComputed)
        {
            throw new InvalidOperationException("CRC validation failed for Modbus response.");
        }
    }

    private static void AppendCrc(byte[] frame, int lengthWithoutCrc)
    {
        var crc = ModbusCrc16.Compute(frame, lengthWithoutCrc);
        frame[lengthWithoutCrc] = (byte)(crc & 0xFF);
        frame[lengthWithoutCrc + 1] = (byte)(crc >> 8);
    }

    private static int GetExpectedDataByteCount(ModbusReadRequest request)
    {
        return request.FunctionCode is 0x01 or 0x02
            ? (request.RegisterCount + 7) / 8
            : request.RegisterCount * 2;
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
