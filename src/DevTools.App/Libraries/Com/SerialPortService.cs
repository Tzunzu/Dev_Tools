using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace DevTools.App.Libraries.Com;

internal sealed class SerialPortService : ISerialPortService, IDisposable
{
    private readonly SerialPort serialPort = new();

    public bool IsOpen => serialPort.IsOpen;

    public SerialPortSettings CurrentSettings { get; private set; } = new();

    public void Configure(SerialPortSettings settings)
    {
        if (IsOpen)
        {
            throw new InvalidOperationException("Cannot change serial settings while port is open.");
        }

        CurrentSettings = settings;
        serialPort.PortName = settings.PortName;
        serialPort.BaudRate = settings.BaudRate;
        serialPort.Parity = settings.Parity;
        serialPort.DataBits = settings.DataBits;
        serialPort.StopBits = settings.StopBits;
        serialPort.RtsEnable = settings.RtsEnable;
        serialPort.DtrEnable = settings.DtrEnable;
    }

    public void Open()
    {
        if (!IsOpen)
        {
            serialPort.Open();
        }
    }

    public void Close()
    {
        if (IsOpen)
        {
            serialPort.Close();
        }
    }

    public void DiscardInBuffer()
    {
        if (IsOpen)
        {
            serialPort.DiscardInBuffer();
        }
    }

    public async Task WriteAsync(byte[] frame, CancellationToken cancellationToken)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("Serial port is not open.");
        }

        await serialPort.BaseStream.WriteAsync(frame.AsMemory(0, frame.Length), cancellationToken);
        await serialPort.BaseStream.FlushAsync(cancellationToken);
    }

    public async Task<byte[]> ReadAsync(int bytesToRead, int timeoutMs, CancellationToken cancellationToken)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("Serial port is not open.");
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var buffer = new byte[bytesToRead];
            var offset = 0;
            var startedAt = DateTime.UtcNow;
            var originalReadTimeout = serialPort.ReadTimeout;

            try
            {
                while (offset < bytesToRead)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                    var remainingMs = timeoutMs - elapsedMs;
                    if (remainingMs <= 0)
                    {
                        throw new TimeoutException("The read operation timed out while waiting for Modbus response bytes.");
                    }

                    serialPort.ReadTimeout = Math.Min(remainingMs, 200);

                    try
                    {
                        var read = serialPort.Read(buffer, offset, bytesToRead - offset);
                        if (read == 0)
                        {
                            throw new EndOfStreamException("No more data from serial stream while waiting for frame.");
                        }

                        offset += read;
                    }
                    catch (TimeoutException) when (offset < bytesToRead)
                    {
                        // Continue until the overall timeout expires.
                    }
                }

                return buffer;
            }
            finally
            {
                serialPort.ReadTimeout = originalReadTimeout;
            }
        });
    }

    public void Dispose()
    {
        if (IsOpen)
        {
            serialPort.Close();
        }

        serialPort.Dispose();
    }
}
