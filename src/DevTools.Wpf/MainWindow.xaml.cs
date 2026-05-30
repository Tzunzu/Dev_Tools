using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DevTools.Wpf.Infrastructure.Console;
using DevTools.Wpf.Infrastructure.Logging;
using DevTools.Wpf.Infrastructure;
using DevTools.Wpf.Libraries.Modbus;
using DevTools.Wpf.Views;

namespace DevTools.Wpf;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ConsoleCommandService consoleCommands;
    private readonly Dictionary<string, Func<UserControl>> toolViewFactories = new(StringComparer.Ordinal)
    {
        ["Modbus RTU client"] = static () => new ModbusRtuClientView(),
        ["Modbus RTU Serial Scanner"] = static () => new ModbusRtuSerialScannerView(),
        ["Modbus RTU Server"] = static () => new ModbusRtuServerView(),
        ["Modbus TCP client"] = static () => new ModbusTcpClientView(),
        ["Modbus TCP Server"] = static () => new ModbusTcpServerView(),
        ["Settings"] = static () => new SettingsView(),
        ["Help"] = static () => new HelpView()
    };

    private readonly Dictionary<string, UserControl> toolViewCache = new(StringComparer.Ordinal);

    public MainWindow()
    {
        InitializeComponent();
        TrySetWindowIcon();
        consoleCommands = new ConsoleCommandService(
            getActiveViewName: () => WorkAreaGroup?.Header?.ToString() ?? "Unknown",
            isConsoleExpanded: () => OutputConsoleExpander?.IsExpanded == true,
            getLogCount: () => OutputLogger.Instance.Logs.Count,
            availableViews: toolViewFactories.Keys,
            clearOutput: () => OutputLogger.Instance.Clear(),
            getDiagnosticsLevel: () => DiagnosticsSettings.Level.ToString().ToLowerInvariant(),
            setDiagnosticsLevel: SetDiagnosticsLevel,
            dumpRegisters: DumpRegisters,
            syncViews: SyncServerViewsNow);
        ShowToolView("Welcome", new WelcomeView());
        UpdateMaximizeRestoreGlyph();

        if (ToolNavigationTree.Items.Count > 0
            && ToolNavigationTree.Items[0] is TreeViewItem toolsNode
            && toolsNode.Items.Count > 0
            && toolsNode.Items[0] is TreeViewItem modbusNode
            && modbusNode.Items.Count > 0
            && modbusNode.Items[0] is TreeViewItem rtuClientNode)
        {
            rtuClientNode.IsSelected = true;
        }
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var iconImage = new BitmapImage(new Uri("pack://application:,,,/Assets/AppIcon.png", UriKind.Absolute));
            Icon = iconImage;
            if (WindowIconImage is not null)
            {
                WindowIconImage.Source = iconImage;
            }
        }
        catch
        {
            // Keep startup resilient if icon resource is missing in a transient build state.
        }
    }

    private void ToolNavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem selectedNode)
        {
            return;
        }

        var selectedText = selectedNode.Header?.ToString();
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return;
        }

        if (!toolViewFactories.TryGetValue(selectedText, out var factory))
        {
            ShowToolView(selectedText, new WelcomeView());
            return;
        }

        if (!toolViewCache.TryGetValue(selectedText, out var view))
        {
            view = factory();
            toolViewCache[selectedText] = view;
        }

        ShowToolView(selectedText, view);
    }

    private void ShowToolView(string title, UserControl view)
    {
        WorkAreaGroup.Header = title;
        WorkAreaContentHost.Content = view;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

        UpdateMaximizeRestoreGlyph();
    }

    private void UpdateMaximizeRestoreGlyph()
    {
        if (MaximizeRestoreButton is null)
        {
            return;
        }

        // Segoe MDL2: maximize = E922, restore = E923.
        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateMaximizeRestoreGlyph();
    }

    private void ClearOutputButton_Click(object sender, RoutedEventArgs e)
    {
        OutputLogger.Instance.Clear();
    }

    private void RunConsoleButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteConsoleInput();
    }

    private void ConsoleInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        ExecuteConsoleInput();
    }

    private void ExecuteConsoleInput()
    {
        if (ConsoleInputBox is null)
        {
            return;
        }

        var input = ConsoleInputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        OutputLogger.Instance.Log($"> {input}");

        if (!input.StartsWith('/'))
        {
            OutputLogger.Instance.Log("Console input is ready. Start commands with /help.");
            ConsoleInputBox.Clear();
            ConsoleInputBox.Focus();
            return;
        }

        if (!consoleCommands.TryExecute(input, out var response) || string.IsNullOrWhiteSpace(response))
        {
            OutputLogger.Instance.Log($"Unknown command: {input}. Type /help.");
        }
        else
        {
            LogConsoleResponse(response);
        }

        ConsoleInputBox.Clear();
        ConsoleInputBox.Focus();
    }

    private static void LogConsoleResponse(string response)
    {
        foreach (var line in response.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                OutputLogger.Instance.Log(line);
            }
        }
    }

    private static string SetDiagnosticsLevel(string[] args)
    {
        if (args.Length == 0)
        {
            return $"Diagnostics level: {DiagnosticsSettings.Level.ToString().ToLowerInvariant()}";
        }

        var levelText = args[0].Trim();
        if (string.Equals(levelText, "debug", StringComparison.OrdinalIgnoreCase))
        {
            DiagnosticsSettings.Level = DiagnosticsLogLevel.Debug;
            return "Diagnostics level set to debug. Packet RX logging enabled.";
        }

        if (string.Equals(levelText, "info", StringComparison.OrdinalIgnoreCase))
        {
            DiagnosticsSettings.Level = DiagnosticsLogLevel.Info;
            return "Diagnostics level set to info. Packet RX logging disabled.";
        }

        return "Invalid diagnostics level. Use /loglevel info or /loglevel debug.";
    }

    private static string DumpRegisters(string[] args)
    {
        if (args.Length < 1)
        {
            return "Usage: /regs [tcp|rtu] <hr|ir|co|di|all> <start> <count>";
        }

        var argOffset = 0;
        var target = "tcp";
        if (args.Length >= 1)
        {
            var possibleTarget = args[0].Trim().ToLowerInvariant();
            if (possibleTarget is "tcp" or "rtu")
            {
                target = possibleTarget;
                argOffset = 1;
            }
        }

        if (args.Length - argOffset < 1)
        {
            return "Usage: /regs [tcp|rtu] <hr|ir|co|di|all> <start> <count>";
        }

        var area = args[argOffset].Trim().ToLowerInvariant();

        ushort startAddress;
        ushort count;
        var hasRangeFilter = false;

        if (area == "all")
        {
            if (args.Length - argOffset == 1)
            {
                startAddress = 0;
                count = 0;
            }
            else
            {
                if (args.Length - argOffset < 3)
                {
                    return "Usage: /regs [tcp|rtu] all <start> <count>";
                }

                if (!ushort.TryParse(args[argOffset + 1], out startAddress))
                {
                    return "Invalid start address.";
                }

                if (!ushort.TryParse(args[argOffset + 2], out count) || count == 0)
                {
                    return "Invalid count. Use 1..65535.";
                }

                hasRangeFilter = true;
            }
        }
        else
        {
            if (args.Length - argOffset < 3)
            {
                return "Usage: /regs [tcp|rtu] <hr|ir|co|di> <start> <count>";
            }

            if (!ushort.TryParse(args[argOffset + 1], out startAddress))
            {
                return "Invalid start address.";
            }

            if (!ushort.TryParse(args[argOffset + 2], out count) || count == 0)
            {
                return "Invalid count. Use 1..65535.";
            }
        }

        var store = target == "rtu"
            ? SharedRuntimes.Instance.RtuServerDataStore
            : SharedRuntimes.Instance.TcpServerDataStore;

        var labelPrefix = target.ToUpperInvariant();

        if (area == "all")
        {
            var sections = new List<string>
            {
                DumpMappedArea(store, "hr", labelPrefix, hasRangeFilter, startAddress, count),
                DumpMappedArea(store, "ir", labelPrefix, hasRangeFilter, startAddress, count),
                DumpMappedArea(store, "co", labelPrefix, hasRangeFilter, startAddress, count),
                DumpMappedArea(store, "di", labelPrefix, hasRangeFilter, startAddress, count)
            };

            return string.Join(Environment.NewLine + Environment.NewLine, sections);
        }

        return DumpArea(store, area, labelPrefix, startAddress, count);
    }

    private static string DumpArea(ModbusServerDataStore store, string area, string labelPrefix, ushort startAddress, ushort count)
    {
        try
        {
            return area switch
            {
                "hr" or "holding" => FormatRegisterDump($"{labelPrefix}-HR", startAddress, store.ReadRegisters(ModbusDataArea.HoldingRegisters, startAddress, count)),
                "ir" or "input" => FormatRegisterDump($"{labelPrefix}-IR", startAddress, store.ReadRegisters(ModbusDataArea.InputRegisters, startAddress, count)),
                "co" or "coils" => FormatBooleanDump($"{labelPrefix}-CO", startAddress, store.ReadBooleans(ModbusDataArea.Coils, startAddress, count)),
                "di" or "discrete" => FormatBooleanDump($"{labelPrefix}-DI", startAddress, store.ReadBooleans(ModbusDataArea.DiscreteInputs, startAddress, count)),
                _ => "Invalid area. Use hr, ir, co, di, or all."
            };
        }
        catch (ModbusServerException ex)
        {
            return $"{labelPrefix}-{area.ToUpperInvariant()} read failed: {ex.Message}";
        }
    }

    private static string DumpMappedArea(ModbusServerDataStore store, string area, string labelPrefix, bool hasRangeFilter, ushort startAddress, ushort count)
    {
        var rangeSuffix = hasRangeFilter
            ? $" (filter {startAddress}..{(startAddress + count - 1)})"
            : string.Empty;

        return area switch
        {
            "hr" => FormatMappedRegisterDump($"{labelPrefix}-HR", store.GetMappedRegisters(ModbusDataArea.HoldingRegisters), hasRangeFilter, startAddress, count, rangeSuffix),
            "ir" => FormatMappedRegisterDump($"{labelPrefix}-IR", store.GetMappedRegisters(ModbusDataArea.InputRegisters), hasRangeFilter, startAddress, count, rangeSuffix),
            "co" => FormatMappedBooleanDump($"{labelPrefix}-CO", store.GetMappedBooleans(ModbusDataArea.Coils), hasRangeFilter, startAddress, count, rangeSuffix),
            "di" => FormatMappedBooleanDump($"{labelPrefix}-DI", store.GetMappedBooleans(ModbusDataArea.DiscreteInputs), hasRangeFilter, startAddress, count, rangeSuffix),
            _ => "Invalid area. Use hr, ir, co, di, or all."
        };
    }

    private static string FormatMappedRegisterDump(
        string prefix,
        IReadOnlyList<KeyValuePair<ushort, ushort>> values,
        bool hasRangeFilter,
        ushort startAddress,
        ushort count,
        string rangeSuffix)
    {
        var filtered = hasRangeFilter
            ? values.Where(pair => pair.Key >= startAddress && pair.Key < startAddress + count).ToArray()
            : values;

        var lines = new List<string> { $"{prefix} mapped count {filtered.Count}{rangeSuffix}" };
        foreach (var pair in filtered)
        {
            lines.Add($"{prefix}[{pair.Key}] = {pair.Value} (0x{pair.Value:X4})");
        }

        if (filtered.Count == 0)
        {
            lines.Add("<no mapped values>");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatMappedBooleanDump(
        string prefix,
        IReadOnlyList<KeyValuePair<ushort, bool>> values,
        bool hasRangeFilter,
        ushort startAddress,
        ushort count,
        string rangeSuffix)
    {
        var filtered = hasRangeFilter
            ? values.Where(pair => pair.Key >= startAddress && pair.Key < startAddress + count).ToArray()
            : values;

        var lines = new List<string> { $"{prefix} mapped count {filtered.Count}{rangeSuffix}" };
        foreach (var pair in filtered)
        {
            lines.Add($"{prefix}[{pair.Key}] = {(pair.Value ? 1 : 0)}");
        }

        if (filtered.Count == 0)
        {
            lines.Add("<no mapped values>");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatRegisterDump(string prefix, ushort startAddress, IReadOnlyList<ushort> values)
    {
        var lines = new List<string> { $"{prefix} dump @ {startAddress}, count {values.Count}" };
        for (var i = 0; i < values.Count; i++)
        {
            var address = (ushort)(startAddress + i);
            lines.Add($"{prefix}[{address}] = {values[i]} (0x{values[i]:X4})");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatBooleanDump(string prefix, ushort startAddress, IReadOnlyList<bool> values)
    {
        var lines = new List<string> { $"{prefix} dump @ {startAddress}, count {values.Count}" };
        for (var i = 0; i < values.Count; i++)
        {
            var address = (ushort)(startAddress + i);
            lines.Add($"{prefix}[{address}] = {(values[i] ? 1 : 0)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string SyncServerViewsNow(string[] args)
    {
        var target = args.Length > 0
            ? args[0].Trim().ToLowerInvariant()
            : "all";

        if (target is not "all" and not "tcp" and not "rtu")
        {
            return "Usage: /sync [tcp|rtu|all]";
        }

        var refreshed = new List<string>();

        if (target is "all" or "tcp")
        {
            if (TryGetServerView("Modbus TCP Server", out var tcpView) && tcpView is ModbusTcpServerView tcp)
            {
                tcp.RequestSyncNow();
                refreshed.Add("tcp");
            }
            else
            {
                refreshed.Add("tcp (view not created yet)");
            }
        }

        if (target is "all" or "rtu")
        {
            if (TryGetServerView("Modbus RTU Server", out var rtuView) && rtuView is ModbusRtuServerView rtu)
            {
                rtu.RequestSyncNow();
                refreshed.Add("rtu");
            }
            else
            {
                refreshed.Add("rtu (view not created yet)");
            }
        }

        return "Sync requested: " + string.Join(", ", refreshed);
    }

    private bool TryGetServerView(string viewName, out UserControl view)
    {
        if (toolViewCache.TryGetValue(viewName, out view!))
        {
            return true;
        }

        if (!toolViewFactories.TryGetValue(viewName, out var factory))
        {
            view = null!;
            return false;
        }

        view = factory();
        toolViewCache[viewName] = view;
        return true;
    }
}