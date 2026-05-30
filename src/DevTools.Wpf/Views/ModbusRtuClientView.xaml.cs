using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DevTools.Wpf.Infrastructure.Modbus;
using DevTools.Wpf.Infrastructure.Dialogs;
using DevTools.Wpf.Infrastructure.Presets;

namespace DevTools.Wpf.Views;

public partial class ModbusRtuClientView : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ObservableCollection<SlaveCardViewModel> Cards { get; } = new();
    public ObservableCollection<string> PresetNames { get; } = new();

    private readonly SemaphoreSlim modbusOperationLock = new(1, 1);
    private readonly List<RtuClientPresetModel> presets = new();
    private readonly PresetStore<RtuClientPresetModel> presetStore;

    private SerialPort? serialPort;
    private CancellationTokenSource? pollCancellation;
    private bool isPolling;
    private bool suppressPresetSelectionChange;

    private string PresetsFilePath => Path.Combine(
        AppContext.BaseDirectory,
        "modbus-rtu-client-presets.json");

    public ModbusRtuClientView()
    {
        InitializeComponent();
        DataContext = this;
        Unloaded += OnUnloaded;
        presetStore = new PresetStore<RtuClientPresetModel>(PresetsFilePath, JsonOptions);

        PopulateComPorts();
        LoadPresetsFromDisk();
        RefreshPresetNames();

        // Starter parity with WinForms: one default slave card.
        Cards.Add(new SlaveCardViewModel
        {
            SlaveId = 1,
            Start = 0,
            Length = 10,
            FunctionIndex = 2
        });

        if (presets.Count > 0)
        {
            suppressPresetSelectionChange = true;
            PresetComboBox.SelectedIndex = 0;
            suppressPresetSelectionChange = false;
            ApplyPreset(presets[0]);
        }
    }

    private void AddSlaveButton_Click(object sender, RoutedEventArgs e)
    {
        var nextSlaveId = Cards.Count + 1;
        Cards.Add(new SlaveCardViewModel
        {
            SlaveId = nextSlaveId,
            Start = 0,
            Length = 10,
            FunctionIndex = 2
        });
    }

    private void RefreshPortsButton_Click(object sender, RoutedEventArgs e)
    {
        PopulateComPorts();
    }

    private void SaveConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var suggestedName = string.IsNullOrWhiteSpace(PresetComboBox.Text)
            ? $"Preset {presets.Count + 1}"
            : PresetComboBox.Text.Trim();

        var targetName = PromptForPresetName(suggestedName);
        if (targetName is null)
        {
            return;
        }

        var existing = FindPreset(targetName);
        if (existing is null)
        {
            presets.Add(CaptureCurrentPreset(targetName));
        }
        else
        {
            ApplyModel(existing, CaptureCurrentPreset(targetName));
        }

        PersistPresets();
        RefreshPresetNames();
        SelectPresetName(targetName);
        SetConnectionStatus($"Saved config '{targetName}'.", isError: false);
    }

    private string? PromptForPresetName(string initialValue)
    {
        return PresetNamePrompt.Show(Window.GetWindow(this), initialValue, "Save Preset", message => SetConnectionStatus(message, isError: true));
    }

    private void UpdateConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedName = GetSelectedPresetName();
        if (string.IsNullOrWhiteSpace(selectedName))
        {
            SetConnectionStatus("Select a preset to update.", isError: true);
            return;
        }

        var existing = FindPreset(selectedName);
        if (existing is null)
        {
            SetConnectionStatus("Selected preset does not exist.", isError: true);
            return;
        }

        ApplyModel(existing, CaptureCurrentPreset(selectedName));
        PersistPresets();
        RefreshPresetNames();
        SelectPresetName(selectedName);
        SetConnectionStatus($"Updated config '{selectedName}'.", isError: false);
    }

    private void RenamePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var sourceName = GetSelectedPresetName();
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            SetConnectionStatus("Select a preset to rename.", isError: true);
            return;
        }

        var targetName = PresetComboBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetName))
        {
            SetConnectionStatus("Type a new preset name in the preset field.", isError: true);
            return;
        }

        if (string.Equals(sourceName, targetName, StringComparison.OrdinalIgnoreCase))
        {
            SetConnectionStatus("Preset name is unchanged.", isError: true);
            return;
        }

        if (FindPreset(targetName) is not null)
        {
            SetConnectionStatus("A preset with that name already exists.", isError: true);
            return;
        }

        var preset = FindPreset(sourceName);
        if (preset is null)
        {
            SetConnectionStatus("Selected preset does not exist.", isError: true);
            return;
        }

        preset.Name = targetName;
        PersistPresets();
        RefreshPresetNames();
        SelectPresetName(targetName);
        SetConnectionStatus($"Renamed preset to '{targetName}'.", isError: false);
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedName = GetSelectedPresetName();
        if (string.IsNullOrWhiteSpace(selectedName))
        {
            SetConnectionStatus("Select a preset to delete.", isError: true);
            return;
        }

        var removed = presets.RemoveAll(p => string.Equals(p.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            SetConnectionStatus("Selected preset does not exist.", isError: true);
            return;
        }

        PersistPresets();
        RefreshPresetNames();
        PresetComboBox.Text = string.Empty;
        SetConnectionStatus($"Deleted preset '{selectedName}'.", isError: false);
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressPresetSelectionChange)
        {
            return;
        }

        var selectedName = PresetComboBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selectedName))
        {
            return;
        }

        var preset = FindPreset(selectedName);
        if (preset is null)
        {
            return;
        }

        ApplyPreset(preset);
        SetConnectionStatus($"Loaded config '{selectedName}'.", isError: false);
    }

    private async void OpenPortButton_Click(object sender, RoutedEventArgs e)
    {
        if (serialPort?.IsOpen == true)
        {
            StopPollingInternal();
            serialPort.Close();
            SetConnectionState(connected: false, "Closed");
            return;
        }

        try
        {
            var portName = (PortComboBox.SelectedItem as DevTools.Wpf.Libraries.Com.ComPortDeviceInfo)?.PortName?.Trim();
            if (string.IsNullOrWhiteSpace(portName))
            {
                SetConnectionStatus("Select a COM port.", isError: true);
                return;
            }

            if (!int.TryParse(BaudRateComboBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var baudRate))
            {
                SetConnectionStatus("Invalid baud rate.", isError: true);
                return;
            }

            if (!TryParseFrame(FrameComboBox.Text, out var dataBits, out var parity, out var stopBits))
            {
                SetConnectionStatus("Invalid frame. Use 8N1, 8E1, 8O1, or 8N2.", isError: true);
                return;
            }

            serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                ReadTimeout = 250,
                WriteTimeout = 2000,
                RtsEnable = RtsCheckBox.IsChecked == true,
                DtrEnable = DtrCheckBox.IsChecked == true
            };

            await Task.Run(serialPort.Open);
            SetConnectionState(connected: true, $"Connected to {portName}");
        }
        catch (Exception ex)
        {
            serialPort?.Dispose();
            serialPort = null;
            SetConnectionState(connected: false, "Connection failed");
            SetConnectionStatus(ex.Message, isError: true);
        }
    }

    private void StartPollButton_Click(object sender, RoutedEventArgs e)
    {
        if (serialPort?.IsOpen != true)
        {
            SetConnectionStatus("Open a port before polling.", isError: true);
            return;
        }

        if (isPolling)
        {
            StopPollingInternal();
            return;
        }

        isPolling = true;
        StartPollButton.Content = "Stop Poll";
        pollCancellation = new CancellationTokenSource();
        _ = PollLoopAsync(pollCancellation.Token);
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var intervalMs = ParsePollInterval();
                var cardsSnapshot = await Dispatcher.InvokeAsync(() => Cards.ToList(), System.Windows.Threading.DispatcherPriority.Background, cancellationToken);

                foreach (var card in cardsSnapshot)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await PollCardAsync(card, cancellationToken);
                }

                await Task.Delay(intervalMs, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path when polling is stopped.
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                isPolling = false;
                StartPollButton.Content = "Start Poll";
            });
        }
    }

    private async Task PollCardAsync(SlaveCardViewModel card, CancellationToken cancellationToken)
    {
        var request = await Dispatcher.InvokeAsync(() => new PollRequest
        {
            Card = card,
            SlaveId = (byte)Math.Clamp(card.SlaveId, 1, 247),
            StartAddress = (ushort)Math.Clamp(card.Start, 0, ushort.MaxValue),
            RegisterCount = (ushort)Math.Clamp(card.Length, 1, 125),
            FunctionCode = MapFunctionCode(card.FunctionIndex)
        }, System.Windows.Threading.DispatcherPriority.Background, cancellationToken);

        try
        {
            var values = await ReadRegistersAsync(request.SlaveId, request.FunctionCode, request.StartAddress, request.RegisterCount, cancellationToken);

            await Dispatcher.InvokeAsync(() =>
            {
                request.Card.ApplyRead(values);
                SetConnectionStatus("Polling", isError: false);
            }, System.Windows.Threading.DispatcherPriority.Background, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                request.Card.RecordError(ModbusStatusText.DescribePollFailure(ex));
                SetConnectionStatus(ex.Message, isError: true);
            }, System.Windows.Threading.DispatcherPriority.Background, cancellationToken);
        }
    }

    private async Task<ushort[]> ReadRegistersAsync(byte slaveId, byte functionCode, ushort startAddress, ushort registerCount, CancellationToken cancellationToken)
    {
        if (serialPort is null || !serialPort.IsOpen)
        {
            throw new InvalidOperationException("Serial port is not open.");
        }

        var frame = BuildReadFrame(slaveId, functionCode, startAddress, registerCount);
        var expectedDataBytes = functionCode is 0x01 or 0x02
            ? (registerCount + 7) / 8
            : registerCount * 2;
        var expectedFrameLength = 5 + expectedDataBytes;

        await modbusOperationLock.WaitAsync(cancellationToken);
        try
        {
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            await serialPort.BaseStream.WriteAsync(frame, cancellationToken);
            await serialPort.BaseStream.FlushAsync(cancellationToken);

            var response = await ReadExactAsync(serialPort, 5, timeoutMs: 2000, cancellationToken);
            if ((response[1] & 0x80) == 0x80)
            {
                ValidateCrc(response);
                var exceptionCode = response[2];
                var exceptionMessage = ModbusStatusText.DescribeExceptionCode(exceptionCode);
                throw new InvalidOperationException($"Modbus exception response: 0x{exceptionCode:X2} ({exceptionMessage})");
            }

            if (expectedFrameLength > 5)
            {
                var remainder = await ReadExactAsync(serialPort, expectedFrameLength - 5, timeoutMs: 2000, cancellationToken);
                var fullResponse = new byte[expectedFrameLength];
                Buffer.BlockCopy(response, 0, fullResponse, 0, response.Length);
                Buffer.BlockCopy(remainder, 0, fullResponse, response.Length, remainder.Length);
                response = fullResponse;
            }

            ValidateReadResponse(slaveId, functionCode, expectedDataBytes, response);
            return DecodeValues(functionCode, registerCount, response);
        }
        finally
        {
            modbusOperationLock.Release();
        }
    }

    private static string GetModbusExceptionMessage(byte exceptionCode)
    {
        return ModbusStatusText.DescribeExceptionCode(exceptionCode);
    }

    private static async Task<byte[]> ReadExactAsync(SerialPort port, int count, int timeoutMs, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var buffer = new byte[count];
            var offset = 0;
            var startedAt = DateTime.UtcNow;

            while (offset < count)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                var remainingMs = timeoutMs - elapsedMs;
                if (remainingMs <= 0)
                {
                    throw new TimeoutException("Timed out waiting for Modbus response.");
                }

                port.ReadTimeout = Math.Min(remainingMs, 250);

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
                    // Keep reading until the overall timeout is reached.
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

    private static byte[] BuildWriteSingleFrame(byte slaveId, byte functionCode, ushort address, ushort value)
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

    private static byte[] BuildWriteMultipleFrame(byte slaveId, ushort startAddress, IReadOnlyList<ushort> values)
    {
        var frame = new byte[9 + (values.Count * 2)];
        frame[0] = slaveId;
        frame[1] = 0x10;
        frame[2] = (byte)(startAddress >> 8);
        frame[3] = (byte)(startAddress & 0xFF);
        frame[4] = (byte)(values.Count >> 8);
        frame[5] = (byte)(values.Count & 0xFF);
        frame[6] = (byte)(values.Count * 2);

        for (var i = 0; i < values.Count; i++)
        {
            frame[7 + (i * 2)] = (byte)(values[i] >> 8);
            frame[8 + (i * 2)] = (byte)(values[i] & 0xFF);
        }

        AppendCrc(frame, frame.Length - 2);
        return frame;
    }

    private async Task WriteSingleCoilAsync(byte slaveId, ushort address, bool coilValue, CancellationToken cancellationToken)
    {
        await WriteSingleValueAsync(slaveId, 0x05, address, coilValue ? (ushort)0xFF00 : (ushort)0x0000, cancellationToken);
    }

    private async Task WriteSingleRegisterAsync(byte slaveId, ushort address, ushort registerValue, CancellationToken cancellationToken)
    {
        await WriteSingleValueAsync(slaveId, 0x06, address, registerValue, cancellationToken);
    }

    private async Task WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, IReadOnlyList<ushort> values, CancellationToken cancellationToken)
    {
        if (values.Count == 0 || values.Count > 123)
        {
            throw new ArgumentOutOfRangeException(nameof(values), "Write register count must be between 1 and 123.");
        }

        if (serialPort is null || !serialPort.IsOpen)
        {
            throw new InvalidOperationException("Serial port is not open.");
        }

        var request = BuildWriteMultipleFrame(slaveId, startAddress, values);
        await modbusOperationLock.WaitAsync(cancellationToken);
        try
        {
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            await serialPort.BaseStream.WriteAsync(request, cancellationToken);
            await serialPort.BaseStream.FlushAsync(cancellationToken);

            var response = await ReadExactAsync(serialPort, 8, timeoutMs: 2000, cancellationToken);
            ValidateCrc(response);
            if (response[0] != slaveId || response[1] != 0x10)
            {
                throw new InvalidOperationException("Invalid write-multiple response header.");
            }

            var echoedAddress = (ushort)((response[2] << 8) | response[3]);
            var echoedCount = (ushort)((response[4] << 8) | response[5]);
            if (echoedAddress != startAddress || echoedCount != values.Count)
            {
                throw new InvalidOperationException("Write-multiple response echo mismatch.");
            }
        }
        finally
        {
            modbusOperationLock.Release();
        }
    }

    private async Task WriteSingleValueAsync(byte slaveId, byte functionCode, ushort address, ushort value, CancellationToken cancellationToken)
    {
        if (serialPort is null || !serialPort.IsOpen)
        {
            throw new InvalidOperationException("Serial port is not open.");
        }

        var request = BuildWriteSingleFrame(slaveId, functionCode, address, value);
        await modbusOperationLock.WaitAsync(cancellationToken);
        try
        {
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            await serialPort.BaseStream.WriteAsync(request, cancellationToken);
            await serialPort.BaseStream.FlushAsync(cancellationToken);

            var response = await ReadExactAsync(serialPort, 8, timeoutMs: 2000, cancellationToken);
            ValidateCrc(response);

            for (var i = 0; i < 6; i++)
            {
                if (response[i] != request[i])
                {
                    throw new InvalidOperationException("Write response echo mismatch.");
                }
            }
        }
        finally
        {
            modbusOperationLock.Release();
        }
    }

    private static void ValidateReadResponse(byte slaveId, byte functionCode, int expectedDataBytes, byte[] response)
    {
        if (response.Length < 5)
        {
            throw new InvalidOperationException("Modbus response too short.");
        }

        if (response[0] != slaveId)
        {
            throw new InvalidOperationException("Slave ID mismatch in response.");
        }

        if (response[1] != functionCode)
        {
            throw new InvalidOperationException("Function code mismatch in response.");
        }

        if (response[2] != expectedDataBytes)
        {
            throw new InvalidOperationException("Unexpected Modbus byte count.");
        }

        ValidateCrc(response);
    }

    private static ushort[] DecodeValues(byte functionCode, ushort registerCount, byte[] response)
    {
        if (functionCode is 0x01 or 0x02)
        {
            var bitValues = new ushort[registerCount];
            for (var index = 0; index < registerCount; index++)
            {
                var dataByte = response[3 + (index / 8)];
                bitValues[index] = (ushort)((dataByte >> (index % 8)) & 0x01);
            }

            return bitValues;
        }

        var values = new ushort[registerCount];
        for (var index = 0; index < registerCount; index++)
        {
            var high = response[3 + (index * 2)];
            var low = response[4 + (index * 2)];
            values[index] = (ushort)((high << 8) | low);
        }

        return values;
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

    private static byte MapFunctionCode(int functionIndex)
    {
        return functionIndex switch
        {
            0 => 0x01,
            1 => 0x02,
            2 => 0x03,
            3 => 0x04,
            _ => 0x03
        };
    }

    private static bool TryParseFrame(string? frameText, out int dataBits, out Parity parity, out StopBits stopBits)
    {
        dataBits = 8;
        parity = Parity.None;
        stopBits = StopBits.One;

        if (string.IsNullOrWhiteSpace(frameText))
        {
            return false;
        }

        var frame = frameText.Trim().ToUpperInvariant();
        if (frame.Length != 3 || !char.IsDigit(frame[0]) || !char.IsDigit(frame[2]))
        {
            return false;
        }

        dataBits = frame[0] - '0';
        parity = frame[1] switch
        {
            'N' => Parity.None,
            'E' => Parity.Even,
            'O' => Parity.Odd,
            _ => Parity.None
        };

        stopBits = frame[2] switch
        {
            '1' => StopBits.One,
            '2' => StopBits.Two,
            _ => StopBits.None
        };

        return stopBits != StopBits.None;
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

        if (ports.Count == 0)
        {
            SetConnectionStatus("No COM ports found.", isError: true);
        }
    }

    private int ParsePollInterval()
    {
        return int.TryParse(PollIntervalTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intervalMs)
            ? Math.Clamp(intervalMs, 100, 60_000)
            : 1000;
    }

    private string? GetSelectedPresetName()
    {
        var selected = PresetComboBox.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            return selected;
        }

        var typed = PresetComboBox.Text?.Trim();
        return string.IsNullOrWhiteSpace(typed) ? null : typed;
    }

    private RtuClientPresetModel CaptureCurrentPreset(string name)
    {
        return new RtuClientPresetModel
        {
            Name = name,
            Port = (PortComboBox.SelectedItem as DevTools.Wpf.Libraries.Com.ComPortDeviceInfo)?.PortName?.Trim() ?? string.Empty,
            BaudRate = BaudRateComboBox.Text?.Trim() ?? string.Empty,
            Frame = FrameComboBox.Text?.Trim() ?? "8N1",
            PollIntervalMs = ParsePollInterval(),
            Rts = RtsCheckBox.IsChecked == true,
            Dtr = DtrCheckBox.IsChecked == true,
            Cards = Cards.Select(card => new RtuClientPresetCardModel
            {
                SlaveId = card.SlaveId,
                Start = card.Start,
                Length = card.Length,
                FunctionIndex = card.FunctionIndex,
                RegisterNumberFormat = card.RegisterNumberFormat,
                RegisterValueDataType = card.RegisterValueDataType,
                Descriptions = card.Descriptions.ToDictionary(pair => pair.Key, pair => pair.Value),
                ShowDescriptionColumn = card.ShowDescriptionColumn
            }).ToList()
        };
    }

    private void ApplyPreset(RtuClientPresetModel preset)
    {
        if (!string.IsNullOrWhiteSpace(preset.Port))
        {
            var portMatch = (PortComboBox.ItemsSource as IEnumerable<DevTools.Wpf.Libraries.Com.ComPortDeviceInfo>)
                ?.FirstOrDefault(p => p.PortName.Equals(preset.Port, StringComparison.OrdinalIgnoreCase));
            if (portMatch is not null)
            {
                PortComboBox.SelectedItem = portMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(preset.BaudRate))
        {
            BaudRateComboBox.Text = preset.BaudRate;
        }

        if (!string.IsNullOrWhiteSpace(preset.Frame))
        {
            FrameComboBox.Text = preset.Frame;
        }

        PollIntervalTextBox.Text = Math.Clamp(preset.PollIntervalMs, 100, 60_000).ToString(CultureInfo.InvariantCulture);
        RtsCheckBox.IsChecked = preset.Rts;
        DtrCheckBox.IsChecked = preset.Dtr;

        Cards.Clear();
        var savedCards = preset.Cards.Count > 0
            ? preset.Cards
            : new List<RtuClientPresetCardModel> { new() { SlaveId = 1, Start = 0, Length = 10, FunctionIndex = 2 } };

        foreach (var saved in savedCards)
        {
            var card = new SlaveCardViewModel
            {
                SlaveId = Math.Clamp(saved.SlaveId, 1, 247),
                Start = Math.Max(0, saved.Start),
                Length = Math.Clamp(saved.Length, 1, 250),
                FunctionIndex = Math.Clamp(saved.FunctionIndex, 0, 3),
                RegisterNumberFormat = saved.RegisterNumberFormat,
                RegisterValueDataType = saved.RegisterValueDataType,
                ShowDescriptionColumn = saved.ShowDescriptionColumn
            };

            card.SetDescriptions(saved.Descriptions);
            Cards.Add(card);
        }

        SyncAllDescriptionColumnVisibility();
    }

    private void SyncAllDescriptionColumnVisibility()
    {
        foreach (var grid in FindVisualChildren<DataGrid>(this))
        {
            var show = (grid.DataContext as SlaveCardViewModel)?.ShowDescriptionColumn == true;
            ApplyDescriptionColumnVisibility(grid, show);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    private void RefreshPresetNames()
    {
        presetStore.RefreshPresetNames(presets, PresetNames);
    }

    private void SelectPresetName(string name)
    {
        suppressPresetSelectionChange = true;
        PresetComboBox.SelectedItem = PresetNames.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        PresetComboBox.Text = name;
        suppressPresetSelectionChange = false;
    }

    private RtuClientPresetModel? FindPreset(string name)
    {
        return presetStore.FindPreset(presets, name);
    }

    private void LoadPresetsFromDisk()
    {
        presets.Clear();
        presets.AddRange(presetStore.LoadPresets());
    }

    private void PersistPresets()
    {
        presetStore.SavePresets(presets);
    }

    private static void ApplyModel(RtuClientPresetModel target, RtuClientPresetModel source)
    {
        target.Port = source.Port;
        target.BaudRate = source.BaudRate;
        target.Frame = source.Frame;
        target.PollIntervalMs = source.PollIntervalMs;
        target.Rts = source.Rts;
        target.Dtr = source.Dtr;
        target.Cards = source.Cards;
    }

    private void SetConnectionState(bool connected, string message)
    {
        OpenPortButton.Content = connected ? "Close Port" : "Open Port";
        PortComboBox.IsEnabled = !connected;
        BaudRateComboBox.IsEnabled = !connected;
        FrameComboBox.IsEnabled = !connected;
        RtsCheckBox.IsEnabled = !connected;
        DtrCheckBox.IsEnabled = !connected;
        SetConnectionStatus(message, isError: false);
    }

    private void SetConnectionStatus(string message, bool isError)
    {
        ConnectionStatusText.Text = message;
        ConnectionStatusText.Foreground = (System.Windows.Media.Brush)FindResource(isError ? "ErrorBrush" : "SuccessBrush");
    }

    private void StopPollingInternal()
    {
        pollCancellation?.Cancel();
        pollCancellation?.Dispose();
        pollCancellation = null;
        isPolling = false;
        StartPollButton.Content = "Start Poll";
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Keep the client alive when this view is removed from the visual tree.
    }

    private void RequestGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        while (source is not null && source is not DataGridRow)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        if (source is not DataGridRow row)
        {
            return;
        }

        row.IsSelected = true;
        grid.SelectedItem = row.Item;
        if (grid.Columns.Count > 0)
        {
            grid.CurrentCell = new DataGridCellInfo(row.Item, grid.Columns[0]);
        }
    }

    private void RequestGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        var show = (grid.DataContext as SlaveCardViewModel)?.ShowDescriptionColumn == true;
        ApplyDescriptionColumnVisibility(grid, show);
    }

    private async void RequestGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
        {
            return;
        }

        if (!string.Equals(e.Column.Header?.ToString(), "Value", StringComparison.Ordinal))
        {
            return;
        }

        if (serialPort?.IsOpen != true)
        {
            SetConnectionStatus("Open a port before writing.", isError: true);
            return;
        }

        if (sender is not DataGrid grid
            || grid.DataContext is not SlaveCardViewModel card
            || e.Row.Item is not RegisterRowViewModel row)
        {
            return;
        }

        var pendingValue = (e.EditingElement as TextBox)?.Text ?? row.Value;

        if (!card.TryGetRowAddress(row, out var address))
        {
            SetConnectionStatus("Invalid row address.", isError: true);
            return;
        }

        var slaveId = (byte)Math.Clamp(card.SlaveId, 1, 247);
        try
        {
            switch (MapFunctionCode(card.FunctionIndex))
            {
                case 0x01:
                {
                    if (!TryParseBooleanValue(pendingValue, out var coil))
                    {
                        SetConnectionStatus("Invalid coil value. Use 0/1 or true/false.", isError: true);
                        return;
                    }

                    await WriteSingleCoilAsync(slaveId, (ushort)address, coil, CancellationToken.None);
                    card.SetRawValueAtRow(row, coil ? (ushort)1 : (ushort)0);
                    SetConnectionStatus($"Wrote coil {address}.", isError: false);
                    break;
                }
                case 0x03:
                {
                    if (!card.TryBuildRegisterWrite(row, pendingValue, out var writeAddress, out var registers, out var error))
                    {
                        SetConnectionStatus(error, isError: true);
                        return;
                    }

                    if (registers.Length == 1)
                    {
                        await WriteSingleRegisterAsync(slaveId, (ushort)writeAddress, registers[0], CancellationToken.None);
                    }
                    else
                    {
                        await WriteMultipleRegistersAsync(slaveId, (ushort)writeAddress, registers, CancellationToken.None);
                    }

                    card.SetRawValues(writeAddress, registers);
                    SetConnectionStatus($"Wrote register {writeAddress}.", isError: false);
                    break;
                }
                default:
                    SetConnectionStatus("Selected function is read-only. Use Coil or Holding.", isError: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            SetConnectionStatus(ex.Message, isError: true);
        }
    }

    private static bool TryParseBooleanValue(string? value, out bool parsed)
    {
        parsed = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (bool.TryParse(text, out parsed))
        {
            return true;
        }

        if (ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            parsed = number != 0;
            return true;
        }

        return false;
    }

    private void AddEditDescriptionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetContextSelection(sender, out var card, out var row, out var address))
        {
            return;
        }

        var editedText = PromptForDescription(address, row.Description ?? string.Empty);
        if (editedText is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(editedText))
        {
            card.ClearRowDescription(row);
        }
        else
        {
            card.SetRowDescription(row, editedText.Trim());
        }
    }

    private void ClearDescriptionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetContextSelection(sender, out var card, out var row, out _))
        {
            return;
        }

        card.ClearRowDescription(row);
    }

    private void ShowDescriptionColumnMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
        {
            return;
        }

        var contextMenu = menuItem.Parent as ContextMenu;
        if (contextMenu?.PlacementTarget is not DataGrid grid)
        {
            return;
        }

        ApplyDescriptionColumnVisibility(grid, menuItem.IsChecked);
    }

    private void CardContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu contextMenu || contextMenu.DataContext is not SlaveCardViewModel card)
        {
            return;
        }

        SyncFormatChecks(contextMenu, card);
    }

    private void AddressFormatDecimalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetContextCard(sender, out var card, out var contextMenu))
        {
            return;
        }

        card.RegisterNumberFormat = RegisterNumberFormat.Decimal;
        SyncFormatChecks(contextMenu, card);
    }

    private void AddressFormatHexMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetContextCard(sender, out var card, out var contextMenu))
        {
            return;
        }

        card.RegisterNumberFormat = RegisterNumberFormat.Hex;
        SyncFormatChecks(contextMenu, card);
    }

    private void ValueDataTypeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string tag)
        {
            return;
        }

        if (!TryGetContextCard(menuItem, out var card, out var contextMenu))
        {
            return;
        }

        if (!tag.StartsWith("dtype-", StringComparison.Ordinal))
        {
            return;
        }

        if (!Enum.TryParse<RegisterValueDataType>(tag[6..], ignoreCase: true, out var dataType))
        {
            return;
        }

        card.RegisterValueDataType = dataType;
        SyncFormatChecks(contextMenu, card);
    }

    private bool TryGetContextSelection(object sender, out SlaveCardViewModel card, out RegisterRowViewModel row, out int address)
    {
        card = null!;
        row = null!;
        address = -1;

        if (sender is not MenuItem menuItem)
        {
            return false;
        }

        if (menuItem.DataContext is not SlaveCardViewModel cardViewModel)
        {
            return false;
        }

        if (!TryResolveContextMenu(menuItem, out var contextMenu))
        {
            return false;
        }

        var grid = contextMenu?.PlacementTarget as DataGrid;
        if (grid?.SelectedItem is not RegisterRowViewModel selectedRow)
        {
            return false;
        }

        if (!cardViewModel.TryGetRowAddress(selectedRow, out var selectedAddress))
        {
            return false;
        }

        card = cardViewModel;
        row = selectedRow;
        address = selectedAddress;
        return true;
    }

    private static bool TryGetContextCard(object sender, out SlaveCardViewModel card, out ContextMenu contextMenu)
    {
        card = null!;
        contextMenu = null!;

        if (sender is not MenuItem menuItem)
        {
            return false;
        }

        if (!TryResolveContextMenu(menuItem, out contextMenu))
        {
            return false;
        }

        if (contextMenu?.DataContext is not SlaveCardViewModel cardViewModel)
        {
            return false;
        }

        card = cardViewModel;
        return true;
    }

    private static bool TryResolveContextMenu(MenuItem menuItem, out ContextMenu contextMenu)
    {
        contextMenu = null!;
        ItemsControl? current = menuItem;
        while (current is not null)
        {
            if (current is ContextMenu found)
            {
                contextMenu = found;
                return true;
            }

            current = (current as MenuItem)?.Parent as ItemsControl;
        }

        return false;
    }

    private static void SyncFormatChecks(ContextMenu contextMenu, SlaveCardViewModel card)
    {
        SetTaggedMenuItemChecked(contextMenu, "addr-dec", card.RegisterNumberFormat == RegisterNumberFormat.Decimal);
        SetTaggedMenuItemChecked(contextMenu, "addr-hex", card.RegisterNumberFormat == RegisterNumberFormat.Hex);
        SetTaggedMenuItemChecked(contextMenu, "dtype-UInt", card.RegisterValueDataType == RegisterValueDataType.UInt);
        SetTaggedMenuItemChecked(contextMenu, "dtype-Int", card.RegisterValueDataType == RegisterValueDataType.Int);
        SetTaggedMenuItemChecked(contextMenu, "dtype-Hex", card.RegisterValueDataType == RegisterValueDataType.Hex);
        SetTaggedMenuItemChecked(contextMenu, "dtype-Bits", card.RegisterValueDataType == RegisterValueDataType.Bits);
        SetTaggedMenuItemChecked(contextMenu, "dtype-String", card.RegisterValueDataType == RegisterValueDataType.String);
        SetTaggedMenuItemChecked(contextMenu, "dtype-UDInt", card.RegisterValueDataType == RegisterValueDataType.UDInt);
        SetTaggedMenuItemChecked(contextMenu, "dtype-DInt", card.RegisterValueDataType == RegisterValueDataType.DInt);
        SetTaggedMenuItemChecked(contextMenu, "dtype-Float", card.RegisterValueDataType == RegisterValueDataType.Float);
    }

    private static void SetTaggedMenuItemChecked(ItemsControl parent, string tag, bool isChecked)
    {
        foreach (var item in parent.Items)
        {
            if (item is not MenuItem menuItem)
            {
                continue;
            }

            if (menuItem.Tag is string currentTag && string.Equals(currentTag, tag, StringComparison.Ordinal))
            {
                menuItem.IsChecked = isChecked;
            }

            if (menuItem.HasItems)
            {
                SetTaggedMenuItemChecked(menuItem, tag, isChecked);
            }
        }
    }

    private static void ApplyDescriptionColumnVisibility(DataGrid grid, bool show)
    {
        var descriptionColumn = grid.Columns.FirstOrDefault(column => string.Equals(column.Header?.ToString(), "Description", StringComparison.Ordinal));
        if (descriptionColumn is null)
        {
            return;
        }

        descriptionColumn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private string? PromptForDescription(int address, string initialValue)
    {
        var dialog = new Window
        {
            Title = "Register Description",
            Width = 420,
            Height = 170,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ShowInTaskbar = false
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Address " + address.ToString(CultureInfo.InvariantCulture) + " description"
        };

        var textBox = new TextBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            Text = initialValue
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var saveButton = new Button { Content = "Save", Width = 74, IsDefault = true };
        var cancelButton = new Button { Content = "Cancel", Width = 74, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };

        saveButton.Click += (_, _) => dialog.DialogResult = true;
        cancelButton.Click += (_, _) => dialog.DialogResult = false;

        actions.Children.Add(saveButton);
        actions.Children.Add(cancelButton);

        Grid.SetRow(label, 0);
        Grid.SetRow(textBox, 1);
        Grid.SetRow(actions, 2);

        root.Children.Add(label);
        root.Children.Add(textBox);
        root.Children.Add(actions);
        dialog.Content = root;

        return dialog.ShowDialog() == true ? textBox.Text : null;
    }

    private sealed class PollRequest
    {
        public required SlaveCardViewModel Card { get; init; }

        public required byte SlaveId { get; init; }

        public required ushort StartAddress { get; init; }

        public required ushort RegisterCount { get; init; }

        public required byte FunctionCode { get; init; }
    }
}

public sealed class SlaveCardViewModel : INotifyPropertyChanged
{
    private int slaveId;
    private int start;
    private int length;
    private int functionIndex;
    private RegisterNumberFormat registerNumberFormat;
    private RegisterValueDataType registerValueDataType;
    private int readCount;
    private int errorCount;
    private string lastStatusText = "Not connected";
    private bool showDescriptionColumn;
    private readonly List<ushort> rawValues = new();

    public ObservableCollection<RegisterRowViewModel> Rows { get; } = new();
    public Dictionary<int, string> Descriptions { get; } = new();

    public int SlaveId
    {
        get => slaveId;
        set => SetField(ref slaveId, value);
    }

    public int Start
    {
        get => start;
        set
        {
            if (!SetField(ref start, value))
            {
                return;
            }

            RebuildRows();
        }
    }

    public int Length
    {
        get => length;
        set
        {
            if (!SetField(ref length, value))
            {
                return;
            }

            RebuildRows();
        }
    }

    public int FunctionIndex
    {
        get => functionIndex;
        set => SetField(ref functionIndex, value);
    }

    public RegisterNumberFormat RegisterNumberFormat
    {
        get => registerNumberFormat;
        set
        {
            if (!SetField(ref registerNumberFormat, value))
            {
                return;
            }

            RenderRows();
        }
    }

    public RegisterValueDataType RegisterValueDataType
    {
        get => registerValueDataType;
        set
        {
            if (!SetField(ref registerValueDataType, value))
            {
                return;
            }

            RenderRows();
        }
    }

    public bool ShowDescriptionColumn
    {
        get => showDescriptionColumn;
        set => SetField(ref showDescriptionColumn, value);
    }

    public int ReadCount
    {
        get => readCount;
        private set
        {
            if (SetField(ref readCount, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PollStatus)));
            }
        }
    }

    public int ErrorCount
    {
        get => errorCount;
        private set
        {
            if (SetField(ref errorCount, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PollStatus)));
            }
        }
    }

    public string LastStatusText
    {
        get => lastStatusText;
        private set
        {
            if (SetField(ref lastStatusText, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PollStatus)));
            }
        }
    }

    public string PollStatus => $"Reads: {ReadCount}  Err: {ErrorCount}  {LastStatusText}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public SlaveCardViewModel()
    {
        registerNumberFormat = RegisterNumberFormat.Decimal;
        registerValueDataType = RegisterValueDataType.UInt;
        RebuildRows();
    }

    private void RebuildRows()
    {
        Rows.Clear();

        var safeLength = Math.Max(1, Math.Min(250, Length));
        var safeStart = Math.Max(0, Start);

        rawValues.Clear();

        for (var i = 0; i < safeLength; i++)
        {
            var address = safeStart + i;
            Rows.Add(new RegisterRowViewModel
            {
                Address = RegisterDisplayFormatter.FormatAddress(address, RegisterNumberFormat),
                Value = "-",
                Description = Descriptions.TryGetValue(address, out var description) ? description : string.Empty
            });
        }
    }

    private void RenderRows()
    {
        var safeStart = Math.Max(0, Start);
        for (var i = 0; i < Rows.Count; i++)
        {
            var address = safeStart + i;
            Rows[i].Address = RegisterDisplayFormatter.FormatAddress(address, RegisterNumberFormat);
            Rows[i].Value = i < rawValues.Count
                ? RegisterDisplayFormatter.FormatValue(rawValues, i, RegisterValueDataType)
                : "-";
            Rows[i].Description = Descriptions.TryGetValue(address, out var description) ? description : string.Empty;
        }
    }

    public bool TryGetRowAddress(RegisterRowViewModel row, out int address)
    {
        return RegisterDisplayFormatter.TryParseAddress(row.Address, out address);
    }

    public void SetRowDescription(RegisterRowViewModel row, string description)
    {
        if (!TryGetRowAddress(row, out var address))
        {
            return;
        }

        var normalized = description.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            Descriptions.Remove(address);
            row.Description = string.Empty;
            return;
        }

        Descriptions[address] = normalized;
        row.Description = normalized;
    }

    public void ClearRowDescription(RegisterRowViewModel row)
    {
        if (!TryGetRowAddress(row, out var address))
        {
            return;
        }

        Descriptions.Remove(address);
        row.Description = string.Empty;
    }

    public void SetDescriptions(Dictionary<int, string>? descriptions)
    {
        Descriptions.Clear();
        if (descriptions is not null)
        {
            foreach (var pair in descriptions)
            {
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                Descriptions[pair.Key] = pair.Value.Trim();
            }
        }

        RenderRows();
    }

    public void ApplyRead(IReadOnlyList<ushort> values)
    {
        if (Rows.Count != values.Count)
        {
            RebuildRows();
        }

        rawValues.Clear();
        rawValues.AddRange(values);
        RenderRows();

        ReadCount++;
        LastStatusText = "OK";
    }

    public void RecordError(string statusText)
    {
        ErrorCount++;
        LastStatusText = statusText;
    }

    public void SetRawValueAtRow(RegisterRowViewModel row, ushort rawValue)
    {
        var index = Rows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        EnsureRawValueCapacity();
        rawValues[index] = rawValue;
        RenderRows();
    }

    public void SetRawValues(int startAddress, IReadOnlyList<ushort> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        EnsureRawValueCapacity();
        var safeStart = Math.Max(0, Start);
        var startIndex = Math.Max(0, startAddress - safeStart);
        for (var i = 0; i < values.Count; i++)
        {
            var index = startIndex + i;
            if (index >= rawValues.Count)
            {
                break;
            }

            rawValues[index] = values[i];
        }

        RenderRows();
    }

    public bool TryBuildRegisterWrite(RegisterRowViewModel row, string? text, out int writeAddress, out ushort[] values, out string error)
    {
        writeAddress = Math.Max(0, Start);
        values = Array.Empty<ushort>();
        error = "Invalid register value for selected data type.";

        var rowIndex = Rows.IndexOf(row);
        if (rowIndex < 0)
        {
            error = "Unable to identify edited row.";
            return false;
        }

        var safeStart = Math.Max(0, Start);
        writeAddress = safeStart + rowIndex;

        switch (RegisterValueDataType)
        {
            case RegisterValueDataType.UInt:
                if (ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u))
                {
                    values = [u];
                    return true;
                }

                error = "Enter UINT in range 0..65535.";
                return false;

            case RegisterValueDataType.Int:
                if (short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                {
                    values = [unchecked((ushort)s)];
                    return true;
                }

                error = "Enter INT in range -32768..32767.";
                return false;

            case RegisterValueDataType.Hex:
                if (TryParseHexOrUInt16(text, out var hex))
                {
                    values = [hex];
                    return true;
                }

                error = "Enter HEX like 0x1234 or decimal 0..65535.";
                return false;

            case RegisterValueDataType.Bits:
            {
                var normalized = (text ?? string.Empty).Trim().Replace(" ", string.Empty).Replace("_", string.Empty);
                if (normalized.Length > 0
                    && normalized.Length <= 16
                    && normalized.All(ch => ch is '0' or '1'))
                {
                    values = [Convert.ToUInt16(normalized, 2)];
                    return true;
                }

                error = "Enter binary bits using 0 and 1 only.";
                return false;
            }

            case RegisterValueDataType.String:
            {
                var str = text ?? string.Empty;
                var high = str.Length > 0 ? str[0] : '\0';
                var low = str.Length > 1 ? str[1] : '\0';
                values = [(ushort)((high << 8) | low)];
                return true;
            }

            case RegisterValueDataType.DInt:
                if (!TryResolveDoubleWordWrite(rowIndex, safeStart, out writeAddress))
                {
                    error = "Need two registers for DINT write.";
                    return false;
                }

                if (TryParseInt32Flexible(text, out var dintValue))
                {
                    var combined = unchecked((uint)dintValue);
                    values = [(ushort)(combined >> 16), (ushort)(combined & 0xFFFF)];
                    return true;
                }

                error = "Enter DINT as decimal or hex (0x...).";
                return false;

            case RegisterValueDataType.UDInt:
                if (!TryResolveDoubleWordWrite(rowIndex, safeStart, out writeAddress))
                {
                    error = "Need two registers for UDINT write.";
                    return false;
                }

                if (TryParseUInt32Flexible(text, out var udintValue))
                {
                    values = [(ushort)(udintValue >> 16), (ushort)(udintValue & 0xFFFF)];
                    return true;
                }

                error = "Enter UDINT as decimal or hex (0x...).";
                return false;

            case RegisterValueDataType.Float:
                if (!TryResolveDoubleWordWrite(rowIndex, safeStart, out writeAddress))
                {
                    error = "Need two registers for FLOAT write.";
                    return false;
                }

                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                {
                    var bits = unchecked((uint)BitConverter.SingleToInt32Bits(floatValue));
                    values = [(ushort)(bits >> 16), (ushort)(bits & 0xFFFF)];
                    return true;
                }

                error = "Enter FLOAT using invariant format (example: 12.34).";
                return false;

            default:
                return false;
        }
    }

    private void EnsureRawValueCapacity()
    {
        while (rawValues.Count < Rows.Count)
        {
            rawValues.Add(0);
        }

        if (rawValues.Count > Rows.Count)
        {
            rawValues.RemoveRange(Rows.Count, rawValues.Count - Rows.Count);
        }
    }

    private bool TryResolveDoubleWordWrite(int rowIndex, int safeStart, out int writeAddress)
    {
        var baseIndex = rowIndex % 2 == 0 ? rowIndex : rowIndex - 1;
        if (baseIndex < 0 || baseIndex + 1 >= Rows.Count)
        {
            writeAddress = safeStart + Math.Max(0, rowIndex);
            return false;
        }

        writeAddress = safeStart + baseIndex;
        return true;
    }

    private static bool TryParseHexOrUInt16(string? text, out ushort value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ushort.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return ushort.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseInt32Flexible(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
        {
            value = unchecked((int)hex);
            return true;
        }

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseUInt32Flexible(string? text, out uint value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private bool SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value))
        {
            return false;
        }

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class RegisterRowViewModel : INotifyPropertyChanged
{
    private string address = string.Empty;

    private string value = string.Empty;
    private string description = string.Empty;

    public string Address
    {
        get => address;
        set
        {
            if (address == value)
            {
                return;
            }

            address = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Address)));
        }
    }

    public string Value
    {
        get => value;
        set
        {
            if (this.value == value)
            {
                return;
            }

            this.value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public string Description
    {
        get => description;
        set
        {
            if (description == value)
            {
                return;
            }

            description = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class RtuClientPresetModel : IPresetNamed
{
    public string Name { get; set; } = string.Empty;

    public string Port { get; set; } = string.Empty;

    public string BaudRate { get; set; } = string.Empty;

    public string Frame { get; set; } = "8N1";

    public int PollIntervalMs { get; set; } = 1000;

    public bool Rts { get; set; }

    public bool Dtr { get; set; }

    public List<RtuClientPresetCardModel> Cards { get; set; } = new();
}

internal sealed class RtuClientPresetCardModel
{
    public int SlaveId { get; set; } = 1;

    public int Start { get; set; }

    public int Length { get; set; } = 10;

    public int FunctionIndex { get; set; } = 2;

    public RegisterNumberFormat RegisterNumberFormat { get; set; } = RegisterNumberFormat.Decimal;

    public RegisterValueDataType RegisterValueDataType { get; set; } = RegisterValueDataType.UInt;

    public Dictionary<int, string> Descriptions { get; set; } = new();

    public bool ShowDescriptionColumn { get; set; }
}
