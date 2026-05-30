using DevTools.Wpf.Infrastructure;
using DevTools.Wpf.Infrastructure.Logging;
using DevTools.Wpf.Infrastructure.Modbus;
using DevTools.Wpf.Infrastructure.Presets;
using DevTools.Wpf.Infrastructure.Dialogs;
using DevTools.Wpf.Libraries.Modbus;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DevTools.Wpf.Views;

public partial class ModbusTcpServerView : UserControl
{
    private static readonly Brush RunningBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF78C9B0"));
    private static readonly Brush StoppedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB5B5B5"));
    private static readonly Brush ErrorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE06C75"));
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Random random = new();
    private readonly List<TcpServerPresetModel> presets = new();
    private readonly PresetStore<TcpServerPresetModel> presetStore;
    private readonly DispatcherTimer storeSyncTimer;

    private bool isRunning;
    private bool suppressPresetSelectionChange;
    private bool syncFromStoreInProgress;

    private ModbusServerDataStore DataStore => SharedRuntimes.Instance.TcpServerDataStore;
    private ModbusTcpServerRuntime Server => SharedRuntimes.Instance.TcpServer;

    public ObservableCollection<TcpServerMapCardViewModel> MapCards { get; } = new();
    public ObservableCollection<string> PresetNames { get; } = new();

    private string PresetsFilePath => Path.Combine(
        AppContext.BaseDirectory,
        "modbus-tcp-server-presets.json");

    public ModbusTcpServerView()
    {
        InitializeComponent();
        DataContext = this;
        presetStore = new PresetStore<TcpServerPresetModel>(PresetsFilePath, JsonOptions);

        Server.Log += msg => OutputLogger.Instance.Log($"[TCP-Server] {msg}");
        DataStore.DataChanged += OnDataStoreChanged;
        
        storeSyncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        storeSyncTimer.Tick += (_, _) => SynchronizeCardsFromStore();

        Unloaded += OnUnloaded;

        InitializeMapCards();
        LoadPresetsFromDisk();
        RefreshPresetNames();

        if (presets.Count > 0)
        {
            suppressPresetSelectionChange = true;
            var loopbackPreset = presets.FirstOrDefault(p => string.Equals(p.Name, "Loopback", StringComparison.OrdinalIgnoreCase)) ?? presets[0];
            PresetComboBox.SelectedItem = loopbackPreset.Name;
            suppressPresetSelectionChange = false;
            ApplyPreset(loopbackPreset);
            SetStatus($"Loaded preset: {loopbackPreset.Name} ({loopbackPreset.BindAddress}:{loopbackPreset.Port})", isError: false);
        }

        isRunning = Server.IsRunning;
        if (isRunning)
        {
            SynchronizeCardsFromStore();
            SetStatus("Running.", isError: false);
        }
        else
        {
            UpdateStoreFromCards();
            SetStatus("Stopped", isError: false);
        }

        SetServerUiState(isRunning);
        storeSyncTimer.Start();
    }

    private void InitializeMapCards()
    {
        MapCards.Clear();
        AddMapCard(new TcpServerMapCardViewModel("Holding Registers", 0, 16, isBooleanMap: false));
        AddMapCard(new TcpServerMapCardViewModel("Input Registers", 0, 16, isBooleanMap: false));
        AddMapCard(new TcpServerMapCardViewModel("Discrete Inputs", 0, 16, isBooleanMap: true));
        AddMapCard(new TcpServerMapCardViewModel("Coils", 0, 16, isBooleanMap: true));
    }

    private void AddMapCard(TcpServerMapCardViewModel card)
    {
        card.MapDataChanged += OnMapDataChanged;
        MapCards.Add(card);
    }

    private void OnMapDataChanged()
    {
        if (syncFromStoreInProgress)
        {
            return;
        }

        UpdateStoreFromCards();
    }

    private void SeedDataButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var card in MapCards)
        {
            card.SeedData(random);
        }

        UpdateStoreFromCards();
    }

    private void ClearDataButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var card in MapCards)
        {
            card.ClearData();
        }

        UpdateStoreFromCards();
    }

    private void ServerMapGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
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

    private void ServerMapGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        var show = (grid.DataContext as TcpServerMapCardViewModel)?.ShowDescriptionColumn == true;
        ApplyDescriptionColumnVisibility(grid, show);
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
        if (sender is not ContextMenu contextMenu || contextMenu.DataContext is not TcpServerMapCardViewModel card)
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

    private bool TryGetContextSelection(object sender, out TcpServerMapCardViewModel card, out TcpServerMapRowViewModel row, out int address)
    {
        card = null!;
        row = null!;
        address = -1;

        if (sender is not MenuItem menuItem)
        {
            return false;
        }

        if (menuItem.DataContext is not TcpServerMapCardViewModel cardViewModel)
        {
            return false;
        }

        if (!TryResolveContextMenu(menuItem, out var contextMenu))
        {
            return false;
        }

        var grid = contextMenu?.PlacementTarget as DataGrid;
        if (grid?.SelectedItem is not TcpServerMapRowViewModel selectedRow)
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

    private static bool TryGetContextCard(object sender, out TcpServerMapCardViewModel card, out ContextMenu contextMenu)
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

        if (contextMenu?.DataContext is not TcpServerMapCardViewModel cardViewModel)
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

    private static void SyncFormatChecks(ContextMenu contextMenu, TcpServerMapCardViewModel card)
    {
        SetTaggedMenuItemEnabled(contextMenu, "dtype-group", !card.IsBooleanMap);
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

    private static void SetTaggedMenuItemEnabled(ItemsControl parent, string tag, bool isEnabled)
    {
        foreach (var item in parent.Items)
        {
            if (item is not MenuItem menuItem)
            {
                continue;
            }

            if (menuItem.Tag is string currentTag && string.Equals(currentTag, tag, StringComparison.Ordinal))
            {
                menuItem.IsEnabled = isEnabled;
            }

            if (menuItem.HasItems)
            {
                SetTaggedMenuItemEnabled(menuItem, tag, isEnabled);
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
        SetStatus($"Saved config '{targetName}'.", isError: false);
    }

    private string? PromptForPresetName(string initialValue)
    {
        return PresetNamePrompt.Show(Window.GetWindow(this), initialValue, "Save Preset", message => SetStatus(message, isError: true));
    }

    private void UpdateConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedName = GetSelectedPresetName();
        if (string.IsNullOrWhiteSpace(selectedName))
        {
            SetStatus("Select a preset to update.", isError: true);
            return;
        }

        var existing = FindPreset(selectedName);
        if (existing is null)
        {
            SetStatus("Selected preset does not exist.", isError: true);
            return;
        }

        ApplyModel(existing, CaptureCurrentPreset(selectedName));
        PersistPresets();
        RefreshPresetNames();
        SelectPresetName(selectedName);
        SetStatus($"Updated config '{selectedName}'.", isError: false);
    }

    private void RenamePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var sourceName = GetSelectedPresetName();
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            SetStatus("Select a preset to rename.", isError: true);
            return;
        }

        var targetName = PresetComboBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetName))
        {
            SetStatus("Type a new preset name in the preset field.", isError: true);
            return;
        }

        if (string.Equals(sourceName, targetName, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Preset name is unchanged.", isError: true);
            return;
        }

        if (FindPreset(targetName) is not null)
        {
            SetStatus("A preset with that name already exists.", isError: true);
            return;
        }

        var preset = FindPreset(sourceName);
        if (preset is null)
        {
            SetStatus("Selected preset does not exist.", isError: true);
            return;
        }

        preset.Name = targetName;
        PersistPresets();
        RefreshPresetNames();
        SelectPresetName(targetName);
        SetStatus($"Renamed preset to '{targetName}'.", isError: false);
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedName = GetSelectedPresetName();
        if (string.IsNullOrWhiteSpace(selectedName))
        {
            SetStatus("Select a preset to delete.", isError: true);
            return;
        }

        var removed = presets.RemoveAll(p => string.Equals(p.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            SetStatus("Selected preset does not exist.", isError: true);
            return;
        }

        PersistPresets();
        RefreshPresetNames();
        PresetComboBox.Text = string.Empty;
        SetStatus($"Deleted preset '{selectedName}'.", isError: false);
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
        UpdateStoreFromCards();
        SetStatus($"Loaded config '{selectedName}'.", isError: false);
    }

    private void StartServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (Server.IsRunning)
        {
            Server.Stop();
            isRunning = Server.IsRunning;
            SetServerUiState(false);
            SetStatus("Stopped", isError: false);
            return;
        }

        if (!IPAddress.TryParse(BindTextBox.Text.Trim(), out var bindAddress))
        {
            SetStatus("Invalid bind address.", isError: true);
            return;
        }

        if (!int.TryParse(PortTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            SetStatus("Invalid port.", isError: true);
            return;
        }

        if (!byte.TryParse(UnitIdTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitId) || unitId is < 1 or > 247)
        {
            SetStatus("Invalid Unit ID.", isError: true);
            return;
        }

        try
        {
            UpdateStoreFromCards();
            OutputLogger.Instance.Log($"[TCP-Server] Starting server on {bindAddress}:{port} (Unit ID: {unitId})");
            Server.Start(bindAddress, port, unitId);
            isRunning = Server.IsRunning;
            SetServerUiState(true);
            SetStatus($"Running on {bindAddress}:{port}.", isError: false);
        }
        catch (Exception ex)
        {
            isRunning = Server.IsRunning;
            SetServerUiState(false);
            OutputLogger.Instance.Log($"[TCP-Server] Start failed: {ex.Message}");
            SetStatus("Start failed: " + ex.Message, isError: true);
        }
    }

    private void SetServerUiState(bool running)
    {
        StartServerButton.Content = running ? "Stop" : "Start";
        BindTextBox.IsEnabled = !running;
        PortTextBox.IsEnabled = !running;
        UnitIdTextBox.IsEnabled = !running;
        ConnectionStatusText.Foreground = running ? RunningBrush : StoppedBrush;
    }

    private void UpdateStoreFromCards()
    {
        try
        {
            foreach (var card in MapCards)
            {
                var area = GetArea(card.Title);
                var startAddress = (ushort)Math.Clamp(card.Start, 0, ushort.MaxValue);

                if (area is ModbusDataArea.Coils or ModbusDataArea.DiscreteInputs)
                {
                    var values = card.Rows.Select(row => ParseBoolean(row.Value)).ToArray();
                    DataStore.ReplaceBooleanArea(area, startAddress, values);
                }
                else
                {
                    var values = card.GetRegisterValuesSnapshot();
                    DataStore.ReplaceRegisterArea(area, startAddress, values);
                }
            }
        }
        catch
        {
            // Ignore temporary invalid ranges while users edit card parameters.
        }
    }

    private void SynchronizeCardsFromStore()
    {
        isRunning = Server.IsRunning;
        if (!isRunning)
        {
            return;
        }

        syncFromStoreInProgress = true;
        try
        {
            foreach (var card in MapCards)
            {
                try
                {
                    var area = GetArea(card.Title);
                    var startAddress = (ushort)Math.Clamp(card.Start, 0, ushort.MaxValue);
                    var count = (ushort)Math.Clamp(card.Rows.Count, 1, 256);

                    if (area is ModbusDataArea.Coils or ModbusDataArea.DiscreteInputs)
                    {
                        var values = DataStore.ReadBooleans(area, startAddress, count);
                        card.ApplyBooleanValues(values);
                    }
                    else
                    {
                        var values = DataStore.ReadRegisters(area, startAddress, count);
                        card.ApplyRegisterValues(values);
                    }
                }
                catch (ModbusServerException)
                {
                    // Ignore transient or out-of-map card ranges while continuing to sync other cards.
                }
            }
        }
        finally
        {
            syncFromStoreInProgress = false;
        }
    }

    private static ModbusDataArea GetArea(string title)
    {
        return title switch
        {
            "Holding Registers" => ModbusDataArea.HoldingRegisters,
            "Input Registers" => ModbusDataArea.InputRegisters,
            "Discrete Inputs" => ModbusDataArea.DiscreteInputs,
            "Coils" => ModbusDataArea.Coils,
            _ => ModbusDataArea.HoldingRegisters
        };
    }

    private static bool ParseBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (bool.TryParse(text, out var parsedBool))
        {
            return parsedBool;
        }

        if (ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNumber))
        {
            return parsedNumber != 0;
        }

        return false;
    }

    private static ushort ParseRegister(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ushort.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                ? hex
                : (ushort)0;
        }

        return ushort.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (ushort)0;
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

    private TcpServerPresetModel CaptureCurrentPreset(string name)
    {
        return new TcpServerPresetModel
        {
            Name = name,
            BindAddress = BindTextBox.Text?.Trim() ?? "0.0.0.0",
            Port = int.TryParse(PortTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? port : 502,
            UnitId = int.TryParse(UnitIdTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitId) ? unitId : 1,
            Cards = MapCards.Select(card => new TcpServerPresetCardModel
            {
                Title = card.Title,
                Start = card.Start,
                Length = card.Length,
                Values = card.IsBooleanMap
                    ? card.Rows.Select(row => row.Value).ToList()
                    : card.GetRegisterValuesSnapshot().Select(v => v.ToString(CultureInfo.InvariantCulture)).ToList(),
                RegisterNumberFormat = card.RegisterNumberFormat,
                RegisterValueDataType = card.RegisterValueDataType,
                Descriptions = card.Descriptions.ToDictionary(pair => pair.Key, pair => pair.Value),
                ShowDescriptionColumn = card.ShowDescriptionColumn
            }).ToList()
        };
    }

    private void ApplyPreset(TcpServerPresetModel preset)
    {
        if (!string.IsNullOrWhiteSpace(preset.BindAddress))
        {
            BindTextBox.Text = preset.BindAddress;
        }

        PortTextBox.Text = Math.Clamp(preset.Port, 1, 65535).ToString(CultureInfo.InvariantCulture);
        UnitIdTextBox.Text = Math.Clamp(preset.UnitId, 1, 247).ToString(CultureInfo.InvariantCulture);

        var cardLookup = MapCards.ToDictionary(card => card.Title, StringComparer.OrdinalIgnoreCase);
        foreach (var savedCard in preset.Cards)
        {
            if (!cardLookup.TryGetValue(savedCard.Title, out var card))
            {
                continue;
            }

            card.Start = Math.Max(0, savedCard.Start);
            card.Length = Math.Clamp(savedCard.Length, 1, 256);
            card.RegisterNumberFormat = savedCard.RegisterNumberFormat;
            card.RegisterValueDataType = savedCard.RegisterValueDataType;
            card.ShowDescriptionColumn = savedCard.ShowDescriptionColumn;
            card.SetDescriptions(savedCard.Descriptions);

            if (card.IsBooleanMap)
            {
                for (var i = 0; i < card.Rows.Count; i++)
                {
                    card.Rows[i].Value = i < savedCard.Values.Count ? savedCard.Values[i] : "0";
                }
            }
            else
            {
                card.SetRegisterValuesFromText(savedCard.Values);
            }
        }

        SyncAllDescriptionColumnVisibility();
    }

    private void SyncAllDescriptionColumnVisibility()
    {
        foreach (var grid in FindVisualChildren<DataGrid>(this))
        {
            var show = (grid.DataContext as TcpServerMapCardViewModel)?.ShowDescriptionColumn == true;
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

    private TcpServerPresetModel? FindPreset(string name)
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

    private static void ApplyModel(TcpServerPresetModel target, TcpServerPresetModel source)
    {
        target.BindAddress = source.BindAddress;
        target.Port = source.Port;
        target.UnitId = source.UnitId;
        target.Cards = source.Cards;
    }

    private void SetStatus(string message, bool isError)
    {
        ConnectionStatusText.Text = message;
        ConnectionStatusText.Foreground = isError ? ErrorBrush : (isRunning ? RunningBrush : StoppedBrush);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Don't stop the server when unloading the view - it should persist across view switches
    }

    private void OnDataStoreChanged()
    {
        if (!Server.IsRunning)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(SynchronizeCardsFromStore), DispatcherPriority.Background);
            return;
        }

        SynchronizeCardsFromStore();
    }

    public void RequestSyncNow()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(SynchronizeCardsFromStore), DispatcherPriority.Background);
            return;
        }

        SynchronizeCardsFromStore();
    }
}

public sealed class TcpServerMapCardViewModel : INotifyPropertyChanged
{
    private int start;
    private int length;
    private int seedCount;
    private RegisterNumberFormat registerNumberFormat;
    private RegisterValueDataType registerValueDataType;
    private bool showDescriptionColumn;
    private readonly List<ushort> rawRegisterValues = new();
    private bool suppressMapDataChanged;

    public string Title { get; }
    public bool IsBooleanMap { get; }

    public ObservableCollection<TcpServerMapRowViewModel> Rows { get; } = new();
    public Dictionary<int, string> Descriptions { get; } = new();

    public event Action? MapDataChanged;

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
            MapDataChanged?.Invoke();
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
            MapDataChanged?.Invoke();
        }
    }

    public string StatusText => seedCount > 0
        ? $"Count: {Rows.Count}  Seeded: {seedCount}"
        : $"Count: {Rows.Count}";

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

    public event PropertyChangedEventHandler? PropertyChanged;

    public TcpServerMapCardViewModel(string title, int start, int length, bool isBooleanMap)
    {
        Title = title;
        IsBooleanMap = isBooleanMap;
        this.start = start;
        this.length = length;
        registerNumberFormat = RegisterNumberFormat.Decimal;
        registerValueDataType = RegisterValueDataType.UInt;
        RebuildRows();
    }

    public void SeedData(Random random)
    {
        if (IsBooleanMap)
        {
            for (var i = 0; i < Rows.Count; i++)
            {
                Rows[i].Value = random.Next(0, 2).ToString(CultureInfo.InvariantCulture);
            }
        }
        else
        {
            rawRegisterValues.Clear();
            for (var i = 0; i < Rows.Count; i++)
            {
                rawRegisterValues.Add((ushort)random.Next(0, 65536));
            }

            RenderRows();
        }

        seedCount++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        MapDataChanged?.Invoke();
    }

    public void ClearData()
    {
        if (IsBooleanMap)
        {
            for (var i = 0; i < Rows.Count; i++)
            {
                Rows[i].Value = "0";
            }
        }
        else
        {
            rawRegisterValues.Clear();
            for (var i = 0; i < Rows.Count; i++)
            {
                rawRegisterValues.Add(0);
            }

            RenderRows();
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        MapDataChanged?.Invoke();
    }

    public bool TryGetRowAddress(TcpServerMapRowViewModel row, out int address)
    {
        return RegisterDisplayFormatter.TryParseAddress(row.Address, out address);
    }

    public void ApplyBooleanValues(IReadOnlyList<bool> values)
    {
        suppressMapDataChanged = true;
        try
        {
            for (var i = 0; i < Math.Min(Rows.Count, values.Count); i++)
            {
                Rows[i].Value = values[i] ? "1" : "0";
            }
        }
        finally
        {
            suppressMapDataChanged = false;
        }
    }

    public void ApplyRegisterValues(IReadOnlyList<ushort> values)
    {
        rawRegisterValues.Clear();
        rawRegisterValues.AddRange(values);
        RenderRows();
    }

    public ushort[] GetRegisterValuesSnapshot()
    {
        if (IsBooleanMap)
        {
            return Array.Empty<ushort>();
        }

        EnsureRawRegisterCapacity();
        return rawRegisterValues.Take(Rows.Count).ToArray();
    }

    public void SetRegisterValuesFromText(IReadOnlyList<string> values)
    {
        if (IsBooleanMap)
        {
            return;
        }

        rawRegisterValues.Clear();
        for (var i = 0; i < Rows.Count; i++)
        {
            rawRegisterValues.Add(i < values.Count ? ParseRegisterText(values[i]) : (ushort)0);
        }

        RenderRows();
    }

    public void SetRowDescription(TcpServerMapRowViewModel row, string description)
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

    public void ClearRowDescription(TcpServerMapRowViewModel row)
    {
        if (!TryGetRowAddress(row, out var address))
        {
            return;
        }

        Descriptions.Remove(address);
        row.Description = string.Empty;
    }

    public void SetDescriptions(IDictionary<int, string>? descriptions)
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

    private void RebuildRows()
    {
        Rows.Clear();
        var safeStart = Math.Max(0, Start);
        var safeLength = Math.Clamp(Length, 1, 256);
        rawRegisterValues.Clear();

        for (var i = 0; i < safeLength; i++)
        {
            var address = safeStart + i;
            var row = new TcpServerMapRowViewModel
            {
                Address = RegisterDisplayFormatter.FormatAddress(address, RegisterNumberFormat),
                Value = "0",
                Description = Descriptions.TryGetValue(address, out var description) ? description : string.Empty
            };

            row.PropertyChanged += OnRowPropertyChanged;
            Rows.Add(row);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
    }

    private void RenderRows()
    {
        suppressMapDataChanged = true;
        try
        {
        EnsureRawRegisterCapacity();
        var safeStart = Math.Max(0, Start);
        for (var i = 0; i < Rows.Count; i++)
        {
            var address = safeStart + i;
            Rows[i].Address = RegisterDisplayFormatter.FormatAddress(address, RegisterNumberFormat);
            if (!IsBooleanMap)
            {
                Rows[i].Value = i < rawRegisterValues.Count
                    ? RegisterDisplayFormatter.FormatValue(rawRegisterValues, i, RegisterValueDataType)
                    : "0";
            }

            Rows[i].Description = Descriptions.TryGetValue(address, out var description) ? description : string.Empty;
        }
        }
        finally
        {
            suppressMapDataChanged = false;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (suppressMapDataChanged)
        {
            return;
        }

        if (e.PropertyName == nameof(TcpServerMapRowViewModel.Value))
        {
            if (!IsBooleanMap && sender is TcpServerMapRowViewModel row)
            {
                UpdateRawValueFromRow(row);
            }

            MapDataChanged?.Invoke();
        }
    }

    private void UpdateRawValueFromRow(TcpServerMapRowViewModel row)
    {
        var index = Rows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        EnsureRawRegisterCapacity();
        if (TryApplyEditedRegisterValue(index, row.Value))
        {
            RenderRows();
        }
    }

    private bool TryApplyEditedRegisterValue(int rowIndex, string? text)
    {
        switch (RegisterValueDataType)
        {
            case RegisterValueDataType.UInt:
                if (ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u))
                {
                    rawRegisterValues[rowIndex] = u;
                    return true;
                }

                break;

            case RegisterValueDataType.Int:
                if (short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                {
                    rawRegisterValues[rowIndex] = unchecked((ushort)s);
                    return true;
                }

                break;

            case RegisterValueDataType.Hex:
                rawRegisterValues[rowIndex] = ParseRegisterText(text);
                return true;

            case RegisterValueDataType.Bits:
                var normalized = (text ?? string.Empty).Trim().Replace(" ", string.Empty).Replace("_", string.Empty);
                if (normalized.Length > 0
                    && normalized.Length <= 16
                    && normalized.All(ch => ch is '0' or '1'))
                {
                    rawRegisterValues[rowIndex] = Convert.ToUInt16(normalized, 2);
                    return true;
                }

                break;

            case RegisterValueDataType.String:
                rawRegisterValues[rowIndex] = ParseStringWord(text);
                return true;

            case RegisterValueDataType.DInt:
                if (TryParseInt32Flexible(text, out var signed))
                {
                    SetWordPair(rowIndex, unchecked((uint)signed));
                    return true;
                }

                break;

            case RegisterValueDataType.UDInt:
                if (TryParseUInt32Flexible(text, out var unsigned))
                {
                    SetWordPair(rowIndex, unsigned);
                    return true;
                }

                break;

            case RegisterValueDataType.Float:
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                {
                    var bits = unchecked((uint)BitConverter.SingleToInt32Bits(floatValue));
                    SetWordPair(rowIndex, bits);
                    return true;
                }

                break;
        }

        return false;
    }

    private void SetWordPair(int rowIndex, uint combined)
    {
        var baseIndex = rowIndex % 2 == 0 ? rowIndex : rowIndex - 1;
        if (baseIndex < 0 || baseIndex + 1 >= rawRegisterValues.Count)
        {
            return;
        }

        rawRegisterValues[baseIndex] = (ushort)(combined >> 16);
        rawRegisterValues[baseIndex + 1] = (ushort)(combined & 0xFFFF);
    }

    private static ushort ParseStringWord(string? text)
    {
        var value = text ?? string.Empty;
        if (value.Length == 0)
        {
            return 0;
        }

        var high = value[0];
        var low = value.Length > 1 ? value[1] : '\0';
        return (ushort)((high << 8) | low);
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

    private void EnsureRawRegisterCapacity()
    {
        if (IsBooleanMap)
        {
            return;
        }

        while (rawRegisterValues.Count < Rows.Count)
        {
            rawRegisterValues.Add(0);
        }

        if (rawRegisterValues.Count > Rows.Count)
        {
            rawRegisterValues.RemoveRange(Rows.Count, rawRegisterValues.Count - Rows.Count);
        }
    }

    private static ushort ParseRegisterText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ushort.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                ? hex
                : (ushort)0;
        }

        return ushort.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (ushort)0;
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

public sealed class TcpServerMapRowViewModel : INotifyPropertyChanged
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BoolValue)));
        }
    }

    public bool BoolValue
    {
        get => value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "1" : "0";
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

internal sealed class TcpServerPresetModel : IPresetNamed
{
    public string Name { get; set; } = string.Empty;

    public string BindAddress { get; set; } = "0.0.0.0";

    public int Port { get; set; } = 502;

    public int UnitId { get; set; } = 1;

    public List<TcpServerPresetCardModel> Cards { get; set; } = new();
}

internal sealed class TcpServerPresetCardModel
{
    public string Title { get; set; } = string.Empty;

    public int Start { get; set; }

    public int Length { get; set; } = 16;

    public List<string> Values { get; set; } = new();

    public RegisterNumberFormat RegisterNumberFormat { get; set; } = RegisterNumberFormat.Decimal;

    public RegisterValueDataType RegisterValueDataType { get; set; } = RegisterValueDataType.UInt;

    public Dictionary<int, string> Descriptions { get; set; } = new();

    public bool ShowDescriptionColumn { get; set; }
}
