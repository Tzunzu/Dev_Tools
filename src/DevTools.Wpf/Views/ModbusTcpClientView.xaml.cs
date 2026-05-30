using DevTools.Wpf.Infrastructure.Logging;
using DevTools.Wpf.Infrastructure.Presets;
using DevTools.Wpf.Infrastructure.Dialogs;
using DevTools.Wpf.Infrastructure.Modbus;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;

namespace DevTools.Wpf.Views;

public partial class ModbusTcpClientView : UserControl
{
    private static readonly Brush ConnectedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF78C9B0"));
    private static readonly Brush ErrorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE06C75"));
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ObservableCollection<TcpRequestCardViewModel> Cards { get; } = new();
    public ObservableCollection<string> PresetNames { get; } = new();

    private readonly ModbusTcpTransport transport = new();
    private readonly List<TcpClientPresetModel> presets = new();
    private readonly PresetStore<TcpClientPresetModel> presetStore;

    private CancellationTokenSource? pollCancellation;
    private bool isPolling;
    private bool suppressPresetSelectionChange;

    private string PresetsFilePath => Path.Combine(
        AppContext.BaseDirectory,
        "modbus-tcp-client-presets.json");

    public ModbusTcpClientView()
    {
        InitializeComponent();
        DataContext = this;
        Unloaded += OnUnloaded;
        presetStore = new PresetStore<TcpClientPresetModel>(PresetsFilePath, JsonOptions);

        LoadPresetsFromDisk();
        RefreshPresetNames();

        Cards.Add(new TcpRequestCardViewModel
        {
            UnitId = 1,
            Start = 0,
            Length = 10,
            FunctionIndex = 2
        });

        if (presets.Count > 0)
        {
            suppressPresetSelectionChange = true;
            var loopbackPreset = presets.FirstOrDefault(p => string.Equals(p.Name, "Loopback", StringComparison.OrdinalIgnoreCase)) ?? presets[0];
            PresetComboBox.SelectedItem = loopbackPreset.Name;
            suppressPresetSelectionChange = false;
            ApplyPreset(loopbackPreset);
            SetConnectionStatus($"Loaded preset: {loopbackPreset.Name} ({loopbackPreset.Host}:{loopbackPreset.Port})", isError: false);
        }
        else
        {
            SetConnectionStatus("Disconnected", isError: false);
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (transport.IsConnected)
        {
            StopPollingInternal();
            transport.Disconnect();
            SetConnectionState(connected: false);
            SetConnectionStatus("Disconnected", isError: false);
            OutputLogger.Instance.Log("[TCP-Client] Disconnected");
            return;
        }

        if (string.IsNullOrWhiteSpace(HostTextBox.Text))
        {
            SetConnectionStatus("Host is required.", isError: true);
            return;
        }

        if (!int.TryParse(PortTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            SetConnectionStatus("Invalid port.", isError: true);
            return;
        }

        ConnectButton.IsEnabled = false;
        try
        {
            var host = HostTextBox.Text.Trim();
            OutputLogger.Instance.Log($"[TCP-Client] Attempting to connect to {host}:{port}...");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await transport.ConnectAsync(host, port, timeout.Token);
            SetConnectionState(connected: true);
            SetConnectionStatus($"Connected {host}:{port}", isError: false);
            OutputLogger.Instance.Log($"[TCP-Client] Connected successfully to {host}:{port}");
        }
        catch (Exception ex)
        {
            SetConnectionState(connected: false);
            SetConnectionStatus(ex.Message, isError: true);
            OutputLogger.Instance.Log($"[TCP-Client] Connection failed: {ex.Message}");
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        SetConnectionStatus(transport.IsConnected ? "Ready" : "Disconnected", isError: false);
    }

    private void AddSlaveButton_Click(object sender, RoutedEventArgs e)
    {
        var nextUnitId = Math.Max(1, Math.Min(247, Cards.Count + 1));
        Cards.Add(new TcpRequestCardViewModel
        {
            UnitId = nextUnitId,
            Start = 0,
            Length = 10,
            FunctionIndex = 2
        });
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

    private void StartPollButton_Click(object sender, RoutedEventArgs e)
    {
        if (!transport.IsConnected)
        {
            SetConnectionStatus("Connect first.", isError: true);
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
                var cardsSnapshot = await Dispatcher.InvokeAsync(() => Cards.ToArray(), System.Windows.Threading.DispatcherPriority.Background, cancellationToken);

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
            // Expected when polling stops.
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

    private async Task PollCardAsync(TcpRequestCardViewModel card, CancellationToken cancellationToken)
    {
        try
        {
            var unitId = (byte)Math.Clamp(card.UnitId, 1, 247);
            var functionCode = MapFunctionCode(card.FunctionIndex);
            var startAddress = (ushort)Math.Clamp(card.Start, 0, ushort.MaxValue);
            var count = (ushort)Math.Clamp(card.Length, 1, 125);

            var values = await transport.ReadAsync(unitId, functionCode, startAddress, count, cancellationToken);
            await Dispatcher.InvokeAsync(() =>
            {
                card.ApplyRead(values);
                SetConnectionStatus("Polling", isError: false);
            }, System.Windows.Threading.DispatcherPriority.Background, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                card.RecordError(ModbusStatusText.DescribePollFailure(ex));
                SetConnectionStatus(ex.Message, isError: true);
            }, System.Windows.Threading.DispatcherPriority.Background, cancellationToken);
        }
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

    private TcpClientPresetModel CaptureCurrentPreset(string name)
    {
        return new TcpClientPresetModel
        {
            Name = name,
            Host = HostTextBox.Text?.Trim() ?? "127.0.0.1",
            Port = int.TryParse(PortTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? port : 502,
            PollIntervalMs = ParsePollInterval(),
            Cards = Cards.Select(card => new TcpClientPresetCardModel
            {
                UnitId = card.UnitId,
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

    private void ApplyPreset(TcpClientPresetModel preset)
    {
        if (!string.IsNullOrWhiteSpace(preset.Host))
        {
            HostTextBox.Text = preset.Host;
        }

        PortTextBox.Text = Math.Clamp(preset.Port, 1, 65535).ToString(CultureInfo.InvariantCulture);
        PollIntervalTextBox.Text = Math.Clamp(preset.PollIntervalMs, 100, 60_000).ToString(CultureInfo.InvariantCulture);

        Cards.Clear();
        var savedCards = preset.Cards.Count > 0
            ? preset.Cards
            : new List<TcpClientPresetCardModel> { new() { UnitId = 1, Start = 0, Length = 10, FunctionIndex = 2 } };

        foreach (var saved in savedCards)
        {
            var card = new TcpRequestCardViewModel
            {
                UnitId = Math.Clamp(saved.UnitId, 1, 247),
                Start = Math.Max(0, saved.Start),
                Length = Math.Clamp(saved.Length, 1, 125),
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
            var show = (grid.DataContext as TcpRequestCardViewModel)?.ShowDescriptionColumn == true;
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

    private TcpClientPresetModel? FindPreset(string name)
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

    private static void ApplyModel(TcpClientPresetModel target, TcpClientPresetModel source)
    {
        target.Host = source.Host;
        target.Port = source.Port;
        target.PollIntervalMs = source.PollIntervalMs;
        target.Cards = source.Cards;
    }

    private void StopPollingInternal()
    {
        pollCancellation?.Cancel();
        pollCancellation?.Dispose();
        pollCancellation = null;
        isPolling = false;
        StartPollButton.Content = "Start Poll";
    }

    private void SetConnectionState(bool connected)
    {
        ConnectButton.Content = connected ? "Disconnect" : "Connect";
        HostTextBox.IsEnabled = !connected;
        PortTextBox.IsEnabled = !connected;
    }

    private void SetConnectionStatus(string message, bool isError)
    {
        ConnectionStatusText.Text = message;
        ConnectionStatusText.Foreground = isError ? ErrorBrush : ConnectedBrush;
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

        var show = (grid.DataContext as TcpRequestCardViewModel)?.ShowDescriptionColumn == true;
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

        if (sender is not DataGrid grid
            || grid.DataContext is not TcpRequestCardViewModel card
            || e.Row.Item is not TcpRegisterRowViewModel row)
        {
            return;
        }

        var pendingValue = (e.EditingElement as TextBox)?.Text ?? row.Value;

        if (!transport.IsConnected)
        {
            SetConnectionStatus("Connect first.", isError: true);
            return;
        }

        if (!card.TryGetRowAddress(row, out var address))
        {
            SetConnectionStatus("Invalid row address.", isError: true);
            return;
        }

        var unitId = (byte)Math.Clamp(card.UnitId, 1, 247);
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

                    await transport.WriteSingleCoilAsync(unitId, (ushort)address, coil, CancellationToken.None);
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
                        await transport.WriteSingleRegisterAsync(unitId, (ushort)writeAddress, registers[0], CancellationToken.None);
                    }
                    else
                    {
                        await transport.WriteMultipleRegistersAsync(unitId, (ushort)writeAddress, registers, CancellationToken.None);
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
        if (sender is not ContextMenu contextMenu || contextMenu.DataContext is not TcpRequestCardViewModel card)
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

    private bool TryGetContextSelection(object sender, out TcpRequestCardViewModel card, out TcpRegisterRowViewModel row, out int address)
    {
        card = null!;
        row = null!;
        address = -1;

        if (sender is not MenuItem menuItem)
        {
            return false;
        }

        if (menuItem.DataContext is not TcpRequestCardViewModel cardViewModel)
        {
            return false;
        }

        if (!TryResolveContextMenu(menuItem, out var contextMenu))
        {
            return false;
        }

        var grid = contextMenu?.PlacementTarget as DataGrid;
        if (grid?.SelectedItem is not TcpRegisterRowViewModel selectedRow)
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

    private static bool TryGetContextCard(object sender, out TcpRequestCardViewModel card, out ContextMenu contextMenu)
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

        if (contextMenu?.DataContext is not TcpRequestCardViewModel cardViewModel)
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

    private static void SyncFormatChecks(ContextMenu contextMenu, TcpRequestCardViewModel card)
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
}

public sealed class TcpRequestCardViewModel : INotifyPropertyChanged
{
    private int unitId;
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

    public ObservableCollection<TcpRegisterRowViewModel> Rows { get; } = new();
    public Dictionary<int, string> Descriptions { get; } = new();

    public int UnitId
    {
        get => unitId;
        set => SetField(ref unitId, value);
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

    public bool ShowDescriptionColumn
    {
        get => showDescriptionColumn;
        set => SetField(ref showDescriptionColumn, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TcpRequestCardViewModel()
    {
        registerNumberFormat = RegisterNumberFormat.Decimal;
        registerValueDataType = RegisterValueDataType.UInt;
        RebuildRows();
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

    public void SetRawValueAtRow(TcpRegisterRowViewModel row, ushort rawValue)
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

    public bool TryBuildRegisterWrite(TcpRegisterRowViewModel row, string? text, out int writeAddress, out ushort[] values, out string error)
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

    private void RebuildRows()
    {
        Rows.Clear();

        var safeStart = Math.Max(0, Start);
        var safeLength = Math.Max(1, Math.Min(125, Length));

        rawValues.Clear();

        for (var i = 0; i < safeLength; i++)
        {
            var address = safeStart + i;
            Rows.Add(new TcpRegisterRowViewModel
            {
                Address = RegisterDisplayFormatter.FormatAddress(address, RegisterNumberFormat),
                Value = "-",
                Description = Descriptions.TryGetValue(address, out var description) ? description : string.Empty
            });
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

    public bool TryGetRowAddress(TcpRegisterRowViewModel row, out int address)
    {
        return RegisterDisplayFormatter.TryParseAddress(row.Address, out address);
    }

    public void SetRowDescription(TcpRegisterRowViewModel row, string description)
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

    public void ClearRowDescription(TcpRegisterRowViewModel row)
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

public sealed class TcpRegisterRowViewModel : INotifyPropertyChanged
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

internal sealed class TcpClientPresetModel : IPresetNamed
{
    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 502;

    public int PollIntervalMs { get; set; } = 1000;

    public List<TcpClientPresetCardModel> Cards { get; set; } = new();
}

internal sealed class TcpClientPresetCardModel
{
    public int UnitId { get; set; } = 1;

    public int Start { get; set; }

    public int Length { get; set; } = 10;

    public int FunctionIndex { get; set; } = 2;

    public RegisterNumberFormat RegisterNumberFormat { get; set; } = RegisterNumberFormat.Decimal;

    public RegisterValueDataType RegisterValueDataType { get; set; } = RegisterValueDataType.UInt;

    public Dictionary<int, string> Descriptions { get; set; } = new();

    public bool ShowDescriptionColumn { get; set; }
}

internal sealed class ModbusTcpTransport : IDisposable
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

    public async Task WriteSingleCoilAsync(byte unitId, ushort address, bool value, CancellationToken cancellationToken)
    {
        var requestPdu = new byte[5];
        requestPdu[0] = 0x05;
        requestPdu[1] = (byte)(address >> 8);
        requestPdu[2] = (byte)(address & 0xFF);
        requestPdu[3] = value ? (byte)0xFF : (byte)0x00;
        requestPdu[4] = 0x00;

        var responsePdu = await ExecuteRequestAsync(unitId, requestPdu, cancellationToken);
        ValidateWriteEcho(responsePdu, requestPdu);
    }

    public async Task WriteSingleRegisterAsync(byte unitId, ushort address, ushort registerValue, CancellationToken cancellationToken)
    {
        var requestPdu = new byte[5];
        requestPdu[0] = 0x06;
        requestPdu[1] = (byte)(address >> 8);
        requestPdu[2] = (byte)(address & 0xFF);
        requestPdu[3] = (byte)(registerValue >> 8);
        requestPdu[4] = (byte)(registerValue & 0xFF);

        var responsePdu = await ExecuteRequestAsync(unitId, requestPdu, cancellationToken);
        ValidateWriteEcho(responsePdu, requestPdu);
    }

    public async Task WriteMultipleRegistersAsync(byte unitId, ushort startAddress, IReadOnlyList<ushort> values, CancellationToken cancellationToken)
    {
        if (values.Count == 0 || values.Count > 123)
        {
            throw new ArgumentOutOfRangeException(nameof(values), "Write register count must be between 1 and 123.");
        }

        var requestPdu = new byte[6 + (values.Count * 2)];
        requestPdu[0] = 0x10;
        requestPdu[1] = (byte)(startAddress >> 8);
        requestPdu[2] = (byte)(startAddress & 0xFF);
        requestPdu[3] = (byte)(values.Count >> 8);
        requestPdu[4] = (byte)(values.Count & 0xFF);
        requestPdu[5] = (byte)(values.Count * 2);

        for (var i = 0; i < values.Count; i++)
        {
            requestPdu[6 + (i * 2)] = (byte)(values[i] >> 8);
            requestPdu[7 + (i * 2)] = (byte)(values[i] & 0xFF);
        }

        var responsePdu = await ExecuteRequestAsync(unitId, requestPdu, cancellationToken);
        ValidateWriteMultipleResponse(responsePdu, startAddress, (ushort)values.Count);
    }

    public void Dispose()
    {
        Disconnect();
        ioLock.Dispose();
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
                var exceptionMessage = ModbusStatusText.DescribeExceptionCode(exceptionCode);
                var message = $"Modbus exception response: 0x{exceptionCode:X2} ({exceptionMessage})";
                OutputLogger.Instance.Log($"[TCP-Client] {message}");
                throw new InvalidOperationException(message);
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

    private static string GetModbusExceptionMessage(byte exceptionCode)
    {
        return ModbusStatusText.DescribeExceptionCode(exceptionCode);
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

    private static void ValidateWriteEcho(byte[] responsePdu, byte[] requestPdu)
    {
        if (responsePdu.Length != requestPdu.Length)
        {
            throw new InvalidOperationException("Invalid write response length.");
        }

        for (var i = 0; i < requestPdu.Length; i++)
        {
            if (responsePdu[i] != requestPdu[i])
            {
                throw new InvalidOperationException("Write response echo mismatch.");
            }
        }
    }

    private static void ValidateWriteMultipleResponse(byte[] responsePdu, ushort startAddress, ushort count)
    {
        if (responsePdu.Length != 5)
        {
            throw new InvalidOperationException("Invalid write-multiple response length.");
        }

        if (responsePdu[0] != 0x10)
        {
            throw new InvalidOperationException("Invalid write-multiple function code in response.");
        }

        var echoedAddress = (ushort)((responsePdu[1] << 8) | responsePdu[2]);
        var echoedCount = (ushort)((responsePdu[3] << 8) | responsePdu[4]);
        if (echoedAddress != startAddress || echoedCount != count)
        {
            throw new InvalidOperationException("Write-multiple response echo mismatch.");
        }
    }
}
