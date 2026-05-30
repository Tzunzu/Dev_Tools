using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DevTools.Wpf.Infrastructure.Modbus;

namespace DevTools.Wpf.Views;

public partial class ModbusRtuSerialScannerView : UserControl
{
    private CancellationTokenSource? scanCancellation;
    private bool isScanning;
    private readonly object scanPortSync = new();
    private SerialPort? activeScanPort;

    public ObservableCollection<RtuScanAttemptResult> Results { get; } = new();

    public ModbusRtuSerialScannerView()
    {
        InitializeComponent();
        DataContext = this;
        Unloaded += OnUnloaded;
        PopulateComPorts();
    }

    private void RefreshPortsButton_Click(object sender, RoutedEventArgs e)
    {
        PopulateComPorts();
    }

    private async void StartScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (isScanning)
        {
            StopScan();
            return;
        }

        if (!TryBuildScanPlan(out var plan, out var validationError))
        {
            SetStatus(validationError, isError: true);
            return;
        }

        isScanning = true;
        StartScanButton.Content = "Stop Scan";
        SetStatus("Scanning...", isError: false);
        scanCancellation = new CancellationTokenSource();

        try
        {
            await RunScanAsync(plan, scanCancellation.Token);
            SetStatus("Scan complete.", isError: false);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Scan canceled.", isError: true);
        }
        finally
        {
            isScanning = false;
            StartScanButton.Content = "Start Scan";
            scanCancellation?.Dispose();
            scanCancellation = null;
        }
    }

    private void ClearResultsButton_Click(object sender, RoutedEventArgs e)
    {
        Results.Clear();
    }

    private async Task RunScanAsync(RtuScanPlan plan, CancellationToken cancellationToken)
    {
        foreach (var baudRate in plan.BaudRates)
        {
            foreach (var frame in plan.Frames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var port = new SerialPort(plan.PortName, baudRate, frame.Parity, frame.DataBits, frame.StopBits)
                {
                    ReadTimeout = Math.Min(plan.TimeoutMs, 100),
                    WriteTimeout = plan.TimeoutMs,
                    RtsEnable = false,
                    DtrEnable = false
                };

                try
                {
                    lock (scanPortSync)
                    {
                        activeScanPort = port;
                    }

                    await Task.Run(port.Open, cancellationToken);
                }
                catch (Exception ex)
                {
                    AppendResult(new RtuScanAttemptResult
                    {
                        Timestamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                        SlaveId = "-",
                        BaudRate = baudRate,
                        Frame = frame.Text,
                        ReadType = plan.ReadTypeLabel,
                        Address = plan.Address,
                        Status = "PortError",
                        Value = "-",
                        Message = ex.Message,
                        DurationMs = 0
                    });
                    continue;
                }

                try
                {
                    for (var slave = plan.SlaveFrom; slave <= plan.SlaveTo; slave++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var started = DateTime.UtcNow;
                        string status;
                        string value;
                        string message;

                        try
                        {
                            var readValue = await ReadSingleValueAsync(port, (byte)slave, plan.FunctionCode, plan.Address, plan.TimeoutMs, cancellationToken);
                            status = "OK";
                            value = readValue;
                            message = string.Empty;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (TimeoutException)
                        {
                            status = "Timeout";
                            value = "-";
                            message = "Timed out waiting for response.";
                        }
                        catch (Exception) when (cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            status = ModbusStatusText.DescribePollFailure(ex);
                            value = "-";
                            message = ex.Message;
                        }

                        var durationMs = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                        AppendResult(new RtuScanAttemptResult
                        {
                            Timestamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                            SlaveId = slave.ToString(CultureInfo.InvariantCulture),
                            BaudRate = baudRate,
                            Frame = frame.Text,
                            ReadType = plan.ReadTypeLabel,
                            Address = plan.Address,
                            Status = status,
                            Value = value,
                            Message = message,
                            DurationMs = durationMs
                        });
                    }
                }
                finally
                {
                    lock (scanPortSync)
                    {
                        if (ReferenceEquals(activeScanPort, port))
                        {
                            activeScanPort = null;
                        }
                    }

                    try
                    {
                        if (port.IsOpen)
                        {
                            port.Close();
                        }
                    }
                    catch
                    {
                        // Ignore close failures during shutdown/cancel.
                    }

                    port.Dispose();
                }
            }
        }
    }

    private async Task<string> ReadSingleValueAsync(SerialPort port, byte slaveId, byte functionCode, ushort address, int timeoutMs, CancellationToken cancellationToken)
    {
        using var perAttemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        perAttemptCancellation.CancelAfter(timeoutMs);
        var attemptToken = perAttemptCancellation.Token;
        var deadlineUtc = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        var frame = BuildReadFrame(slaveId, functionCode, address, 1);

        port.DiscardInBuffer();
        port.DiscardOutBuffer();

        await port.BaseStream.WriteAsync(frame, attemptToken);
        await port.BaseStream.FlushAsync(attemptToken);

        var header = await ReadExactAsync(port, 3, deadlineUtc, attemptToken);
        if (header[0] != slaveId)
        {
            throw new InvalidOperationException("Slave ID mismatch in response.");
        }

        var responseFunction = header[1];
        var byteCountOrException = header[2];

        if ((responseFunction & 0x80) == 0x80)
        {
            var remainder = await ReadExactAsync(port, 2, deadlineUtc, attemptToken);
            var exceptionFrame = new byte[5];
            Buffer.BlockCopy(header, 0, exceptionFrame, 0, 3);
            Buffer.BlockCopy(remainder, 0, exceptionFrame, 3, 2);
            ValidateCrc(exceptionFrame);

            var exceptionCode = byteCountOrException;
            var exceptionText = ModbusStatusText.DescribeExceptionCode(exceptionCode);
            throw new InvalidOperationException($"Modbus exception 0x{exceptionCode:X2} ({exceptionText}).");
        }

        if (responseFunction != functionCode)
        {
            throw new InvalidOperationException("Function code mismatch in response.");
        }

        var dataLength = byteCountOrException;
        var remainderData = await ReadExactAsync(port, dataLength + 2, deadlineUtc, attemptToken);
        var fullResponse = new byte[3 + dataLength + 2];
        Buffer.BlockCopy(header, 0, fullResponse, 0, 3);
        Buffer.BlockCopy(remainderData, 0, fullResponse, 3, remainderData.Length);

        ValidateCrc(fullResponse);

        return functionCode switch
        {
            0x01 or 0x02 => ((fullResponse[3] & 0x01) == 1 ? "1" : "0"),
            0x03 or 0x04 => ((ushort)((fullResponse[3] << 8) | fullResponse[4])).ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("Unsupported function code.")
        };
    }

    private static async Task<byte[]> ReadExactAsync(SerialPort port, int count, DateTime deadlineUtc, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var buffer = new byte[count];
            var offset = 0;

            while (offset < count)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remainingMs = (int)(deadlineUtc - DateTime.UtcNow).TotalMilliseconds;
                if (remainingMs <= 0)
                {
                    throw new TimeoutException();
                }

                port.ReadTimeout = Math.Max(1, Math.Min(remainingMs, 50));

                try
                {
                    var read = port.Read(buffer, offset, count - offset);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("Serial stream closed before frame was complete.");
                    }

                    offset += read;
                }
                catch (TimeoutException) when (offset < count)
                {
                    // Keep reading until overall timeout is reached.
                }
            }

            return buffer;
        }, cancellationToken);
    }

    private static byte[] BuildReadFrame(byte slaveId, byte functionCode, ushort startAddress, ushort registerCount)
    {
        var frame = new byte[8];
        frame[0] = slaveId;
        frame[1] = functionCode;
        frame[2] = (byte)(startAddress >> 8);
        frame[3] = (byte)(startAddress & 0xFF);
        frame[4] = (byte)(registerCount >> 8);
        frame[5] = (byte)(registerCount & 0xFF);
        AppendCrc(frame, 6);
        return frame;
    }

    private static void ValidateCrc(byte[] frame)
    {
        var crcFromFrame = (ushort)((frame[^1] << 8) | frame[^2]);
        var crcComputed = ComputeCrc(frame, frame.Length - 2);
        if (crcFromFrame != crcComputed)
        {
            throw new InvalidOperationException("CRC validation failed.");
        }
    }

    private static void AppendCrc(byte[] frame, int lengthWithoutCrc)
    {
        var crc = ComputeCrc(frame, lengthWithoutCrc);
        frame[lengthWithoutCrc] = (byte)(crc & 0xFF);
        frame[lengthWithoutCrc + 1] = (byte)(crc >> 8);
    }

    private static ushort ComputeCrc(byte[] buffer, int length)
    {
        ushort crc = 0xFFFF;

        for (var i = 0; i < length; i++)
        {
            crc ^= buffer[i];

            for (var bit = 0; bit < 8; bit++)
            {
                var lsbSet = (crc & 0x0001) == 0x0001;
                crc >>= 1;
                if (lsbSet)
                {
                    crc ^= 0xA001;
                }
            }
        }

        return crc;
    }

    private bool TryBuildScanPlan(out RtuScanPlan plan, out string validationError)
    {
        plan = null!;
        validationError = string.Empty;

        var portName = (PortComboBox.SelectedItem as DevTools.Wpf.Libraries.Com.ComPortDeviceInfo)?.PortName?.Trim();
        if (string.IsNullOrWhiteSpace(portName))
        {
            validationError = "Select a COM port.";
            return false;
        }

        if (!byte.TryParse(SlaveFromTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slaveFrom))
        {
            validationError = "Invalid slave start.";
            return false;
        }

        if (!byte.TryParse(SlaveToTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slaveTo))
        {
            validationError = "Invalid slave end.";
            return false;
        }

        if (slaveTo < slaveFrom)
        {
            validationError = "Slave end must be >= start.";
            return false;
        }

        if (!ushort.TryParse(AddressTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var address))
        {
            validationError = "Invalid register address.";
            return false;
        }

        if (!int.TryParse(TimeoutTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMs)
            || timeoutMs < 50
            || timeoutMs > 10000)
        {
            validationError = "Timeout must be 50..10000 ms.";
            return false;
        }

        var baudRates = GetSelectedBaudRates();
        if (baudRates.Count == 0)
        {
            validationError = "Select at least one baud rate.";
            return false;
        }

        var frames = GetSelectedFrames();
        if (frames.Count == 0)
        {
            validationError = "Select at least one frame.";
            return false;
        }

        var readTypeIndex = ReadTypeComboBox.SelectedIndex < 0 ? 2 : ReadTypeComboBox.SelectedIndex;
        var functionCode = readTypeIndex switch
        {
            0 => (byte)0x01,
            1 => (byte)0x02,
            2 => (byte)0x03,
            3 => (byte)0x04,
            _ => (byte)0x03
        };

        var readTypeLabel = readTypeIndex switch
        {
            0 => "Coil",
            1 => "Discrete",
            2 => "Holding",
            3 => "Input",
            _ => "Holding"
        };

        plan = new RtuScanPlan(
            portName,
            slaveFrom,
            slaveTo,
            address,
            timeoutMs,
            functionCode,
            readTypeLabel,
            baudRates,
            frames);

        return true;
    }

    private List<int> GetSelectedBaudRates()
    {
        var result = new List<int>();
        if (Baud9600CheckBox.IsChecked == true) result.Add(9600);
        if (Baud19200CheckBox.IsChecked == true) result.Add(19200);
        if (Baud38400CheckBox.IsChecked == true) result.Add(38400);
        if (Baud57600CheckBox.IsChecked == true) result.Add(57600);
        if (Baud115200CheckBox.IsChecked == true) result.Add(115200);
        return result;
    }

    private List<SerialFrameOption> GetSelectedFrames()
    {
        var result = new List<SerialFrameOption>();

        if (Frame8N1CheckBox.IsChecked == true)
        {
            result.Add(new SerialFrameOption("8N1", 8, Parity.None, StopBits.One));
        }

        if (Frame8E1CheckBox.IsChecked == true)
        {
            result.Add(new SerialFrameOption("8E1", 8, Parity.Even, StopBits.One));
        }

        if (Frame8O1CheckBox.IsChecked == true)
        {
            result.Add(new SerialFrameOption("8O1", 8, Parity.Odd, StopBits.One));
        }

        if (Frame8N2CheckBox.IsChecked == true)
        {
            result.Add(new SerialFrameOption("8N2", 8, Parity.None, StopBits.Two));
        }

        return result;
    }

    private void PopulateComPorts()
    {
        var previousPort = (PortComboBox.SelectedItem as DevTools.Wpf.Libraries.Com.ComPortDeviceInfo)?.PortName
            ?? PortComboBox.Text;

        var ports = DevTools.Wpf.Libraries.Com.ComPortDiscovery.Discover();
        PortComboBox.ItemsSource = ports;

        if (!string.IsNullOrWhiteSpace(previousPort))
        {
            var match = ports.FirstOrDefault(p => p.PortName.Equals(previousPort, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                PortComboBox.SelectedItem = match;
            }
        }

        if (PortComboBox.SelectedItem is null && ports.Count > 0)
        {
            PortComboBox.SelectedIndex = 0;
        }
    }

    private void AppendResult(RtuScanAttemptResult result)
    {
        Dispatcher.Invoke(() =>
        {
            Results.Add(result);
            while (Results.Count > 5000)
            {
                Results.RemoveAt(0);
            }
        });
    }

    private void StopScan()
    {
        scanCancellation?.Cancel();

        lock (scanPortSync)
        {
            if (activeScanPort is null)
            {
                return;
            }

            try
            {
                activeScanPort.Close();
            }
            catch
            {
                // Ignore close errors while force-stopping.
            }

            try
            {
                activeScanPort.Dispose();
            }
            catch
            {
                // Ignore dispose errors while force-stopping.
            }

            activeScanPort = null;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopScan();
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFE06C75"))
            : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF78C9B0"));
    }
}

internal sealed record RtuScanPlan(
    string PortName,
    int SlaveFrom,
    int SlaveTo,
    ushort Address,
    int TimeoutMs,
    byte FunctionCode,
    string ReadTypeLabel,
    IReadOnlyList<int> BaudRates,
    IReadOnlyList<SerialFrameOption> Frames);

internal sealed record SerialFrameOption(string Text, int DataBits, Parity Parity, StopBits StopBits);

public sealed class RtuScanAttemptResult
{
    public string Timestamp { get; init; } = string.Empty;
    public string SlaveId { get; init; } = string.Empty;
    public int BaudRate { get; init; }
    public string Frame { get; init; } = string.Empty;
    public string ReadType { get; init; } = string.Empty;
    public int Address { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int DurationMs { get; init; }
}
