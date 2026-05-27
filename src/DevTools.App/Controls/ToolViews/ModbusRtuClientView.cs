using DevTools.App.Controllers.Modbus;
using DevTools.App.Libraries.Com;
using DevTools.App.Libraries.Modbus;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DevTools.App.Controls.ToolViews;

internal sealed partial class ModbusRtuClientView : UserControl
{
    private readonly IComPortDiscoveryService comPortDiscoveryService;
    private readonly ISerialPortService serialPortService;
    private readonly ModbusRtuReadController readController;

    private readonly List<SlaveColumnConfig> slaveConfigs = new();
    private readonly Dictionary<string, DataGridView> slaveGrids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GroupBox> slaveGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComboBox> slaveFunctionInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> slaveIdInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> startRegisterInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> registerCountInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> nextPollDueTimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RegisterNumberFormat> slaveRegisterNumberFormats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RegisterValueDataType> slaveRegisterValueDataTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> slaveDescriptionColumnVisible = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, string>> slaveDescriptions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ushort>> slaveLastValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> slaveReadCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> slaveErrorCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label> slaveStatusLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SlaveColumnConfig>> savedConfigurations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> savedConfigurationPollRates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string savedConfigFilePath;
    private readonly SemaphoreSlim modbusOperationLock = new(1, 1);

    private int nextSlaveId;
    private int nextColumnKey;
    private CancellationTokenSource? pollingCancellationTokenSource;
    private bool pollingEnabled;
    private bool applyingConfigurationPreset;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ModbusRtuClientView()
    {
        InitializeComponent();

        savedConfigFilePath = BuildSavedConfigFilePath();

        comPortDiscoveryService = new ComPortDiscoveryService();
        serialPortService = new SerialPortService();
        var modbusClient = new ModbusRtuClient(serialPortService);
        readController = new ModbusRtuReadController(modbusClient);

        slaveTablesHostPanel.SizeChanged += (_, _) => ResizeSlaveTables();

        PopulateComPorts();
        InitializeSlaveTables();
        LoadConfigurationsFromDisk();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopPolling();
            serialPortService.Close();

            if (serialPortService is IDisposable disposable)
            {
                disposable.Dispose();
            }

            modbusOperationLock.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeSlaveTables()
    {
        var defaultConfig = new SlaveColumnConfig
        {
            Key = BuildNextColumnKey(),
            SlaveId = 1,
            FunctionCode = 0x03,
            StartRegister = 0,
            RegisterCount = 10
        };

        slaveConfigs.Add(defaultConfig);
        nextSlaveId = 1;
        AddSlaveTable(defaultConfig);
    }

    private void addSlaveButton_Click(object? sender, EventArgs e)
    {
        var config = new SlaveColumnConfig
        {
            Key = BuildNextColumnKey(),
            SlaveId = Math.Min(247, nextSlaveId + 1),
            FunctionCode = 0x03,
            StartRegister = 0,
            RegisterCount = 10
        };

        slaveConfigs.Add(config);
        nextSlaveId = Math.Max(nextSlaveId, config.SlaveId);
        AddSlaveTable(config);
    }

    private void saveConfigButton_Click(object? sender, EventArgs e)
    {
        var name = PromptForConfigurationName();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var snapshot = slaveConfigs
            .Select(config => new SlaveColumnConfig
            {
                Key = config.Key,
                SlaveId = config.SlaveId,
                FunctionCode = config.FunctionCode,
                StartRegister = config.StartRegister,
                RegisterCount = config.RegisterCount,
                RegisterNumberFormat = GetRegisterNumberFormat(config.Key).ToString(),
                RegisterValueDataType = GetRegisterValueDataType(config.Key).ToString(),
                ShowDescriptionColumn = slaveDescriptionColumnVisible.TryGetValue(config.Key, out var showDescription) && showDescription,
                Descriptions = slaveDescriptions.TryGetValue(config.Key, out var descriptions)
                    ? new Dictionary<int, string>(descriptions)
                    : new Dictionary<int, string>()
            })
            .ToList();

        savedConfigurations[name] = snapshot;
        savedConfigurationPollRates[name] = GetGlobalPollRateMs();
        RefreshConfigurationDropdown(name);
        SaveConfigurationsToDisk();
    }

    private void configPresetComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        UpdatePresetActionButtons();

        if (applyingConfigurationPreset)
        {
            return;
        }

        if (configPresetComboBox.SelectedItem is not string selectedName)
        {
            return;
        }

        if (!savedConfigurations.TryGetValue(selectedName, out var savedConfig) || savedConfig.Count == 0)
        {
            return;
        }

        if (savedConfigurationPollRates.TryGetValue(selectedName, out var pollRateMs))
        {
            pollIntervalTextBox.Text = pollRateMs.ToString(CultureInfo.InvariantCulture);
        }

        LoadConfiguration(savedConfig);
    }

    private void renameConfigButton_Click(object? sender, EventArgs e)
    {
        var currentName = GetSelectedPresetName();
        if (string.IsNullOrWhiteSpace(currentName))
        {
            return;
        }

        var newName = PromptForConfigurationName("Rename Configuration", currentName);
        if (string.IsNullOrWhiteSpace(newName) || newName.Equals(currentName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (savedConfigurations.ContainsKey(newName))
        {
            var overwrite = MessageBox.Show(
                "A configuration with that name already exists. Overwrite it?",
                "Rename Configuration",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (overwrite != DialogResult.Yes)
            {
                return;
            }
        }

        if (!savedConfigurations.TryGetValue(currentName, out var config))
        {
            return;
        }

        savedConfigurations.Remove(currentName);
        savedConfigurations[newName] = config;
        if (savedConfigurationPollRates.Remove(currentName, out var pollRateMs))
        {
            savedConfigurationPollRates[newName] = pollRateMs;
        }
        RefreshConfigurationDropdown(newName);
        SaveConfigurationsToDisk();
    }

    private void deleteConfigButton_Click(object? sender, EventArgs e)
    {
        var currentName = GetSelectedPresetName();
        if (string.IsNullOrWhiteSpace(currentName))
        {
            return;
        }

        var confirm = MessageBox.Show(
            "Delete configuration '" + currentName + "'?",
            "Delete Configuration",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        if (!savedConfigurations.Remove(currentName))
        {
            return;
        }

        var nextSelection = savedConfigurations.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

        applyingConfigurationPreset = true;
        try
        {
            configPresetComboBox.BeginUpdate();
            configPresetComboBox.Items.Clear();

            foreach (var name in savedConfigurations.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                configPresetComboBox.Items.Add(name);
            }

            if (!string.IsNullOrWhiteSpace(nextSelection))
            {
                configPresetComboBox.SelectedItem = nextSelection;
            }
            else
            {
                configPresetComboBox.SelectedIndex = -1;
                configPresetComboBox.Text = string.Empty;
            }
        }
        finally
        {
            configPresetComboBox.EndUpdate();
            applyingConfigurationPreset = false;
        }

        UpdatePresetActionButtons();
        SaveConfigurationsToDisk();
    }

    private void AddSlaveTable(SlaveColumnConfig config)
    {
        var group = new GroupBox
        {
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(6),
            Text = BuildSlaveTitle(config),
            ForeColor = System.Drawing.Color.FromArgb(51, 65, 85)
        };

        var rootPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0)
        };
        rootPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var headerPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = 1,
            RowCount = 1
        };

        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var inputGrid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Left,
            Margin = new Padding(0),
            ColumnCount = 2,
            RowCount = 2
        };

        inputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138F));
        inputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74F));
        inputGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        inputGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));

        var registerNumberFormat = ParseRegisterNumberFormat(config.RegisterNumberFormat);
        var registerValueDataType = ParseRegisterValueDataType(config.RegisterValueDataType);
        var showDescriptionColumn = config.ShowDescriptionColumn;

        var closeMenuItem = new ToolStripMenuItem("Close");
        closeMenuItem.Click += (_, _) => RemoveSlave(config.Key);

        var registerFormatMenuItem = new ToolStripMenuItem("Register Number Format");
        var decimalRegistersMenuItem = new ToolStripMenuItem("Decimal") { Checked = registerNumberFormat == RegisterNumberFormat.Decimal };
        var hexRegistersMenuItem = new ToolStripMenuItem("Hex") { Checked = registerNumberFormat == RegisterNumberFormat.Hex };

        decimalRegistersMenuItem.Click += (_, _) =>
        {
            slaveRegisterNumberFormats[config.Key] = RegisterNumberFormat.Decimal;
            SyncDisplaySettingsToConfig(config);
            decimalRegistersMenuItem.Checked = true;
            hexRegistersMenuItem.Checked = false;
            RebuildSlaveTable(config);
            ApplyRenderedValues(config);
        };

        hexRegistersMenuItem.Click += (_, _) =>
        {
            slaveRegisterNumberFormats[config.Key] = RegisterNumberFormat.Hex;
            SyncDisplaySettingsToConfig(config);
            decimalRegistersMenuItem.Checked = false;
            hexRegistersMenuItem.Checked = true;
            RebuildSlaveTable(config);
            ApplyRenderedValues(config);
        };

        registerFormatMenuItem.DropDownItems.Add(decimalRegistersMenuItem);
        registerFormatMenuItem.DropDownItems.Add(hexRegistersMenuItem);

        var showDescriptionColumnMenuItem = new ToolStripMenuItem("Show Description Column")
        {
            CheckOnClick = true,
            Checked = showDescriptionColumn
        };

        showDescriptionColumnMenuItem.CheckedChanged += (_, _) =>
        {
            slaveDescriptionColumnVisible[config.Key] = showDescriptionColumnMenuItem.Checked;
            SyncDisplaySettingsToConfig(config);
            ApplyDescriptionColumnVisibility(config.Key);
        };

        var dataTypeMenuItem = new ToolStripMenuItem("Data Type");
        var dataTypeItems = new Dictionary<RegisterValueDataType, ToolStripMenuItem>();

        foreach (var item in RegisterValueDataTypeItems)
        {
            var menuItem = new ToolStripMenuItem(item.Label)
            {
                Checked = item.DataType == registerValueDataType
            };

            menuItem.Click += (_, _) =>
            {
                slaveRegisterValueDataTypes[config.Key] = item.DataType;
                SyncDisplaySettingsToConfig(config);
                foreach (var pair in dataTypeItems)
                {
                    pair.Value.Checked = pair.Key == item.DataType;
                }

                ApplyRenderedValues(config);
            };

            dataTypeItems[item.DataType] = menuItem;
            dataTypeMenuItem.DropDownItems.Add(menuItem);
        }

        var cardContextMenu = new ContextMenuStrip();
        cardContextMenu.Items.Add(registerFormatMenuItem);
        cardContextMenu.Items.Add(showDescriptionColumnMenuItem);
        cardContextMenu.Items.Add(dataTypeMenuItem);
        cardContextMenu.Items.Add(new ToolStripSeparator());
        cardContextMenu.Items.Add(closeMenuItem);

        var slaveIdLabel = CreateCardLabel("ID");
        var slaveIdInputHost = CreateCardInputHost(config.SlaveId.ToString(), 30, out var slaveIdInput);
        slaveIdInput.TextChanged += (_, _) => UpdateSlaveConfigFromUi(config.Key);

        var functionLabel = CreateCardLabel("Func");
        var functionInput = CreateFunctionSelector(config.FunctionCode);
        functionInput.SelectedIndexChanged += (_, _) => UpdateSlaveConfigFromUi(config.Key);

        var startRegisterLabel = CreateCardLabel("Start");
        var startRegisterInputHost = CreateCardInputHost(config.StartRegister.ToString(), 46, out var startRegisterInput);
        startRegisterInput.TextChanged += (_, _) => UpdateSlaveConfigFromUi(config.Key);

        var registerCountLabel = CreateCardLabel("Len");
        var registerCountInputHost = CreateCardInputHost(config.RegisterCount.ToString(), 30, out var registerCountInput);
        registerCountInput.TextChanged += (_, _) => UpdateSlaveConfigFromUi(config.Key);

        inputGrid.Controls.Add(CreateInlineCardField(functionLabel, functionInput, 30), 0, 0);
        inputGrid.Controls.Add(CreateInlineCardField(slaveIdLabel, slaveIdInputHost, 20), 1, 0);
        inputGrid.Controls.Add(CreateInlineCardField(startRegisterLabel, startRegisterInputHost, 30), 0, 1);
        inputGrid.Controls.Add(CreateInlineCardField(registerCountLabel, registerCountInputHost, 20), 1, 1);

        headerPanel.Controls.Add(inputGrid, 0, 0);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = System.Drawing.Color.FromArgb(249, 250, 251),
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = System.Drawing.Color.FromArgb(226, 232, 240),
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = false,
            EnableHeadersVisualStyles = false,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 22
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
        grid.ColumnHeadersDefaultCellStyle.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.DefaultCellStyle.BackColor = System.Drawing.Color.White;
        grid.DefaultCellStyle.ForeColor = System.Drawing.Color.FromArgb(30, 41, 59);
        grid.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(30, 136, 229);
        grid.DefaultCellStyle.SelectionForeColor = System.Drawing.Color.White;
        grid.DefaultCellStyle.Font = new System.Drawing.Font("Segoe UI", 8.5F);

        var registerColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Reg #",
            Name = "registerNumber",
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Width = 48
        };

        var valueColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Value",
            Name = "registerValue",
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 72
        };

        var descriptionColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Description",
            Name = "description",
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 120,
            Visible = false
        };

        grid.Columns.AddRange(registerColumn, valueColumn, descriptionColumn);
        grid.CellEndEdit += (_, args) => OnDescriptionEdited(config, args, grid);
    grid.CellDoubleClick += (_, args) => OnGridCellDoubleClick(config, args, grid);

        var gridHostPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(6, 2, 6, 6)
        };
        gridHostPanel.Controls.Add(grid);

        var statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(2, 0, 0, 0),
            Text = "Reads: 0  Err: 0",
            Font = new System.Drawing.Font("Segoe UI", 7.5F),
            ForeColor = System.Drawing.Color.FromArgb(71, 85, 105),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };

        rootPanel.Controls.Add(headerPanel, 0, 0);
        rootPanel.Controls.Add(statusLabel, 0, 1);
        rootPanel.Controls.Add(gridHostPanel, 0, 2);
        group.Controls.Add(rootPanel);
        group.ContextMenuStrip = cardContextMenu;
        rootPanel.ContextMenuStrip = cardContextMenu;
        headerPanel.ContextMenuStrip = cardContextMenu;
        inputGrid.ContextMenuStrip = cardContextMenu;
        gridHostPanel.ContextMenuStrip = cardContextMenu;
        grid.ContextMenuStrip = cardContextMenu;
        slaveTablesHostPanel.Controls.Add(group);

        slaveGrids[config.Key] = grid;
        slaveGroups[config.Key] = group;
        slaveFunctionInputs[config.Key] = functionInput;
        slaveIdInputs[config.Key] = slaveIdInput;
        startRegisterInputs[config.Key] = startRegisterInput;
        registerCountInputs[config.Key] = registerCountInput;
        nextPollDueTimes[config.Key] = DateTimeOffset.UtcNow;
        slaveRegisterNumberFormats[config.Key] = registerNumberFormat;
        slaveRegisterValueDataTypes[config.Key] = registerValueDataType;
        slaveDescriptionColumnVisible[config.Key] = showDescriptionColumn;
        slaveDescriptions[config.Key] = new Dictionary<int, string>(config.Descriptions);
        slaveLastValues[config.Key] = new List<ushort>();
        slaveReadCounts[config.Key] = 0;
        slaveErrorCounts[config.Key] = 0;
        slaveStatusLabels[config.Key] = statusLabel;

        config.RegisterNumberFormat = registerNumberFormat.ToString();
        config.RegisterValueDataType = registerValueDataType.ToString();
        config.ShowDescriptionColumn = showDescriptionColumn;
        config.Descriptions = new Dictionary<int, string>(slaveDescriptions[config.Key]);

        ResizeSlaveTables();
        RebuildSlaveTable(config);
    }

    private void RefreshSlaveTable(SlaveColumnConfig config)
    {
        if (!slaveGrids.TryGetValue(config.Key, out var grid))
        {
            return;
        }

        if (slaveGroups.TryGetValue(config.Key, out var group))
        {
            group.Text = BuildSlaveTitle(config);
        }

        RebuildSlaveTable(config);
        grid.ClearSelection();
    }

    private void RebuildSlaveTable(SlaveColumnConfig config)
    {
        if (!slaveGrids.TryGetValue(config.Key, out var grid))
        {
            return;
        }

        grid.Rows.Clear();

        for (var index = 0; index < config.RegisterCount; index++)
        {
            var rowIndex = grid.Rows.Add();
            var registerNumber = config.StartRegister + index;
            grid.Rows[rowIndex].Cells["registerNumber"].Value = FormatRegisterNumber(config.Key, registerNumber);
            grid.Rows[rowIndex].Cells["registerValue"].Value = "-";

            if (slaveDescriptions.TryGetValue(config.Key, out var descriptions)
                && descriptions.TryGetValue(registerNumber, out var description))
            {
                grid.Rows[rowIndex].Cells["description"].Value = description;
            }
            else
            {
                grid.Rows[rowIndex].Cells["description"].Value = string.Empty;
            }
        }

        ApplyDescriptionColumnVisibility(config.Key);
    }

    private void ResizeSlaveTables()
    {
        var width = 220;
        var availableHeight = slaveTablesHostPanel.ClientSize.Height - SystemInformation.HorizontalScrollBarHeight - 2;
        var height = Math.Max(120, availableHeight);

        foreach (Control control in slaveTablesHostPanel.Controls)
        {
            control.Width = width;
            control.Height = height;
        }
    }

    private void RemoveSlave(string key)
    {
        if (slaveConfigs.Count <= 1)
        {
            MessageBox.Show("At least one slave table must remain.", "Remove Slave", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var config = slaveConfigs.FirstOrDefault(item => item.Key == key);
        if (config is null)
        {
            return;
        }

        slaveConfigs.Remove(config);

        if (slaveGroups.TryGetValue(key, out var group))
        {
            slaveTablesHostPanel.Controls.Remove(group);
            group.Dispose();
            slaveGroups.Remove(key);
        }

        slaveGrids.Remove(key);
        slaveFunctionInputs.Remove(key);
        slaveIdInputs.Remove(key);
        startRegisterInputs.Remove(key);
        registerCountInputs.Remove(key);
        nextPollDueTimes.Remove(key);
        slaveRegisterNumberFormats.Remove(key);
        slaveRegisterValueDataTypes.Remove(key);
        slaveDescriptionColumnVisible.Remove(key);
        slaveDescriptions.Remove(key);
        slaveLastValues.Remove(key);
        slaveReadCounts.Remove(key);
        slaveErrorCounts.Remove(key);
        slaveStatusLabels.Remove(key);

        ResizeSlaveTables();
    }

    private void LoadConfiguration(IReadOnlyList<SlaveColumnConfig> preset)
    {
        StopPolling();
        pollButton.Text = "Start Poll";

        foreach (var group in slaveGroups.Values.ToArray())
        {
            slaveTablesHostPanel.Controls.Remove(group);
            group.Dispose();
        }

        slaveConfigs.Clear();
        slaveGrids.Clear();
        slaveGroups.Clear();
        slaveFunctionInputs.Clear();
        slaveIdInputs.Clear();
        startRegisterInputs.Clear();
        registerCountInputs.Clear();
        nextPollDueTimes.Clear();
        slaveRegisterNumberFormats.Clear();
        slaveRegisterValueDataTypes.Clear();
        slaveDescriptionColumnVisible.Clear();
        slaveDescriptions.Clear();
        slaveLastValues.Clear();
        slaveReadCounts.Clear();
        slaveErrorCounts.Clear();
        slaveStatusLabels.Clear();

        nextSlaveId = 0;

        foreach (var item in preset)
        {
            var config = new SlaveColumnConfig
            {
                Key = BuildNextColumnKey(),
                SlaveId = item.SlaveId,
                FunctionCode = item.FunctionCode,
                StartRegister = item.StartRegister,
                RegisterCount = item.RegisterCount,
                RegisterNumberFormat = item.RegisterNumberFormat,
                RegisterValueDataType = item.RegisterValueDataType,
                ShowDescriptionColumn = item.ShowDescriptionColumn,
                Descriptions = new Dictionary<int, string>(item.Descriptions)
            };

            slaveConfigs.Add(config);
            nextSlaveId = Math.Max(nextSlaveId, config.SlaveId);
            AddSlaveTable(config);
        }

        if (slaveConfigs.Count == 0)
        {
            InitializeSlaveTables();
        }
    }

    private void RefreshConfigurationDropdown(string selectedName)
    {
        applyingConfigurationPreset = true;
        try
        {
            configPresetComboBox.BeginUpdate();
            configPresetComboBox.Items.Clear();

            foreach (var name in savedConfigurations.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                configPresetComboBox.Items.Add(name);
            }

            configPresetComboBox.SelectedItem = selectedName;
            if (configPresetComboBox.SelectedItem is null && configPresetComboBox.Items.Count > 0)
            {
                configPresetComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            configPresetComboBox.EndUpdate();
            applyingConfigurationPreset = false;
        }

        UpdatePresetActionButtons();
    }

    private string? PromptForConfigurationName(string title = "Save Configuration", string? initialName = null)
    {
        using var prompt = new Form
        {
            Text = title,
            Width = 360,
            Height = 145,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        };

        var nameLabel = new Label
        {
            Left = 12,
            Top = 12,
            Width = 320,
            Text = "Configuration name"
        };

        var nameTextBox = new TextBox
        {
            Left = 12,
            Top = 34,
            Width = 320,
            Text = initialName ?? string.Empty
        };

        var saveButton = new Button
        {
            Text = "Save",
            Left = 176,
            Width = 74,
            Top = 68,
            DialogResult = DialogResult.OK
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 258,
            Width = 74,
            Top = 68,
            DialogResult = DialogResult.Cancel
        };

        prompt.Controls.Add(nameLabel);
        prompt.Controls.Add(nameTextBox);
        prompt.Controls.Add(saveButton);
        prompt.Controls.Add(cancelButton);
        prompt.AcceptButton = saveButton;
        prompt.CancelButton = cancelButton;

        if (prompt.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        var name = nameTextBox.Text.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private string? GetSelectedPresetName()
    {
        return configPresetComboBox.SelectedItem as string;
    }

    private void UpdatePresetActionButtons()
    {
        var hasSelection = configPresetComboBox.SelectedItem is string;
        renameConfigButton.Enabled = hasSelection;
        deleteConfigButton.Enabled = hasSelection;
    }

    private void LoadConfigurationsFromDisk()
    {
        if (!File.Exists(savedConfigFilePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(savedConfigFilePath);
            var items = JsonSerializer.Deserialize<List<SavedConfigEntry>>(json, JsonOptions) ?? new List<SavedConfigEntry>();

            savedConfigurations.Clear();
            savedConfigurationPollRates.Clear();

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Name) || item.Slaves.Count == 0)
                {
                    continue;
                }

                var configs = item.Slaves
                    .Select(slave => new SlaveColumnConfig
                    {
                        Key = string.Empty,
                        SlaveId = slave.SlaveId,
                        FunctionCode = slave.FunctionCode,
                        StartRegister = slave.StartRegister,
                        RegisterCount = slave.RegisterCount,
                        RegisterNumberFormat = slave.RegisterNumberFormat,
                        RegisterValueDataType = slave.RegisterValueDataType,
                        ShowDescriptionColumn = slave.ShowDescriptionColumn,
                        Descriptions = new Dictionary<int, string>(slave.Descriptions)
                    })
                    .ToList();

                savedConfigurations[item.Name] = configs;
                savedConfigurationPollRates[item.Name] = ResolveSavedPollRate(item);
            }

            if (savedConfigurations.Count > 0)
            {
                RefreshConfigurationDropdown(savedConfigurations.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).First());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[raw] [config-load-error] " + ex.Message);
        }
    }

    private void SaveConfigurationsToDisk()
    {
        try
        {
            var directory = Path.GetDirectoryName(savedConfigFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = savedConfigurations
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new SavedConfigEntry
                {
                    Name = pair.Key,
                    PollRateMs = savedConfigurationPollRates.TryGetValue(pair.Key, out var pollRateMs)
                        ? pollRateMs
                        : GetGlobalPollRateMs(),
                    Slaves = pair.Value.Select(config => new SavedSlaveConfig
                    {
                        SlaveId = config.SlaveId,
                        FunctionCode = config.FunctionCode,
                        StartRegister = config.StartRegister,
                        RegisterCount = config.RegisterCount,
                        RegisterNumberFormat = config.RegisterNumberFormat,
                        RegisterValueDataType = config.RegisterValueDataType,
                        ShowDescriptionColumn = config.ShowDescriptionColumn,
                        Descriptions = new Dictionary<int, string>(config.Descriptions)
                    }).ToList()
                })
                .ToList();

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(savedConfigFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[raw] [config-save-error] " + ex.Message);
        }
    }

    private static string BuildSavedConfigFilePath()
    {
        return Path.Combine(Environment.CurrentDirectory, "modbus-rtu-configs.json");
    }

    private sealed class SavedConfigEntry
    {
        public string Name { get; set; } = string.Empty;

        public int PollRateMs { get; set; } = 1000;

        public List<SavedSlaveConfig> Slaves { get; set; } = new();
    }

    private sealed class SavedSlaveConfig
    {
        public int SlaveId { get; set; }

        public byte FunctionCode { get; set; } = 0x03;

        public int StartRegister { get; set; }

        public int RegisterCount { get; set; }

        public int PollRateMs { get; set; } = 1000;

        public string RegisterNumberFormat { get; set; } = "Decimal";

        public string RegisterValueDataType { get; set; } = "UInt";

        public bool ShowDescriptionColumn { get; set; }

        public Dictionary<int, string> Descriptions { get; set; } = new();
    }

    private static string BuildSlaveTitle(SlaveColumnConfig config)
    {
        return "Slave " + config.SlaveId;
    }

    private void connectButton_Click(object? sender, EventArgs e)
    {
        try
        {
            if (serialPortService.IsOpen)
            {
                StopPolling();
                serialPortService.Close();
                connectionStatusLabel.Text = "Closed";
                connectButton.Text = "Open Port";
                pollButton.Enabled = false;
                return;
            }

            var settings = BuildSerialSettings();
            serialPortService.Configure(settings);
            serialPortService.Open();

            connectionStatusLabel.Text = "Open";
            connectButton.Text = "Close Port";
            pollButton.Enabled = true;
        }
        catch (Exception ex)
        {
            connectionStatusLabel.Text = "Error";
            Console.WriteLine("[raw] [connect-error] " + ex.Message);
        }
    }

    private void refreshPortsButton_Click(object? sender, EventArgs e)
    {
        PopulateComPorts();
    }

    private void PopulateComPorts()
    {
        var previousSelection = (portComboBox.SelectedItem as ComPortDeviceInfo)?.PortName;
        var ports = comPortDiscoveryService.Discover();

        portComboBox.BeginUpdate();
        portComboBox.Items.Clear();

        foreach (var port in ports)
        {
            portComboBox.Items.Add(port);
        }

        portComboBox.EndUpdate();

        if (portComboBox.Items.Count > 0)
        {
            var selectedIndex = 0;
            if (!string.IsNullOrWhiteSpace(previousSelection))
            {
                for (var i = 0; i < portComboBox.Items.Count; i++)
                {
                    if (portComboBox.Items[i] is ComPortDeviceInfo device && device.PortName.Equals(previousSelection, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            portComboBox.SelectedIndex = selectedIndex;
            connectButton.Enabled = true;
            if (!serialPortService.IsOpen)
            {
                connectionStatusLabel.Text = "Ready";
            }
        }
        else
        {
            connectButton.Enabled = false;
            pollButton.Enabled = false;
            connectionStatusLabel.Text = "No Ports";
        }
    }

    private async void pollButton_Click(object? sender, EventArgs e)
    {
        if (!serialPortService.IsOpen)
        {
            MessageBox.Show("Open the serial port first.", "Polling", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (pollingEnabled)
        {
            StopPolling();
            pollButton.Text = "Start Poll";
            return;
        }

        pollingEnabled = true;
        pollButton.Text = "Stop Poll";
        pollingCancellationTokenSource = new CancellationTokenSource();

        try
        {
            await PollLoopAsync(pollingCancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal stop path — cancellation is expected when the user stops polling.
        }
        finally
        {
            pollingEnabled = false;
            pollButton.Text = "Start Poll";
        }
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await PollDueSlavesAsync(cancellationToken);
            await Task.Delay(100, cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task PollDueSlavesAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var configs = slaveConfigs.ToArray();
        foreach (var config in configs)
        {
            if (!nextPollDueTimes.TryGetValue(config.Key, out var nextDue))
            {
                nextDue = now;
            }

            if (nextDue > now)
            {
                continue;
            }

            var profile = new SlaveReadProfile
            {
                Key = config.Key,
                SlaveId = (byte)config.SlaveId,
                FunctionCode = config.FunctionCode,
                StartRegister = (ushort)config.StartRegister,
                RegisterCount = (ushort)config.RegisterCount
            };

            try
            {
                await modbusOperationLock.WaitAsync(cancellationToken);
                var points = await readController.ReadAsync(profile, cancellationToken);
                UpdateGridValues(config, points);
            }
            catch (Exception ex)
            {
                SetTableError(config, ex.Message);
            }
            finally
            {
                if (modbusOperationLock.CurrentCount == 0)
                {
                    modbusOperationLock.Release();
                }
            }

            nextPollDueTimes[config.Key] = now.AddMilliseconds(GetGlobalPollRateMs());
        }
    }

    private void UpdateSlaveConfigFromUi(string key)
    {
        var config = slaveConfigs.FirstOrDefault(item => item.Key == key);
        if (config is null)
        {
            return;
        }

        if (slaveIdInputs.TryGetValue(key, out var slaveIdInput))
        {
            config.SlaveId = ParseCardInput(slaveIdInput.Text, config.SlaveId, 1, 247);
        }

        if (slaveFunctionInputs.TryGetValue(key, out var functionInput)
            && functionInput.SelectedItem is ModbusFunctionOption functionOption)
        {
            config.FunctionCode = functionOption.FunctionCode;
        }

        if (startRegisterInputs.TryGetValue(key, out var startRegisterInput))
        {
            config.StartRegister = ParseCardInput(startRegisterInput.Text, config.StartRegister, 0, 65535);
        }

        if (registerCountInputs.TryGetValue(key, out var registerCountInput))
        {
            config.RegisterCount = ParseCardInput(registerCountInput.Text, config.RegisterCount, 1, 500);
        }

        nextSlaveId = Math.Max(nextSlaveId, config.SlaveId);
        RefreshSlaveTable(config);
        nextPollDueTimes[key] = DateTimeOffset.UtcNow;
    }

    private int GetGlobalPollRateMs()
    {
        return ParseCardInput(pollIntervalTextBox.Text, 1000, 100, 100000);
    }

    private static int ResolveSavedPollRate(SavedConfigEntry entry)
    {
        if (entry.PollRateMs >= 100)
        {
            return entry.PollRateMs;
        }

        var legacyPollRate = entry.Slaves.FirstOrDefault()?.PollRateMs ?? 1000;
        return Math.Max(100, legacyPollRate);
    }

    private static Label CreateCardLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Dock = DockStyle.Left,
            Margin = new Padding(0, 0, 4, 0),
            Text = text,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = System.Drawing.Color.FromArgb(100, 116, 139),
            Font = new System.Drawing.Font("Segoe UI", 7.5F, System.Drawing.FontStyle.Bold)
        };
    }

    private static Panel CreateInlineCardField(Control label, Control input, int labelWidth)
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 1, 6, 1),
            Height = 24
        };

        input.Location = new System.Drawing.Point(labelWidth + 2, 0);
        input.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        label.Width = labelWidth;
        label.Location = new System.Drawing.Point(0, 3);
        host.Controls.Add(input);
        host.Controls.Add(label);
        return host;
    }

    private static Panel CreateCardInputHost(string text, int width, out TextBox input)
    {
        var host = new Panel
        {
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 6, 0),
            BackColor = System.Drawing.Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Height = 19,
            Width = width,
            Padding = new Padding(4, 2, 2, 2)
        };

        input = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BorderStyle = BorderStyle.None,
            Text = text,
            TextAlign = HorizontalAlignment.Left,
            BackColor = System.Drawing.Color.White,
            ForeColor = System.Drawing.Color.FromArgb(15, 23, 42),
            Font = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Regular),
            AutoSize = false,
            Height = 15,
            MaxLength = 6
        };

        host.Controls.Add(input);
        return host;
    }

    private static ComboBox CreateFunctionSelector(byte selectedFunctionCode)
    {
        var comboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 106,
            Margin = new Padding(0, 0, 6, 0),
            DisplayMember = nameof(ModbusFunctionOption.Label),
            ValueMember = nameof(ModbusFunctionOption.FunctionCode)
        };

        comboBox.Items.AddRange(ModbusFunctionOptions.Cast<object>().ToArray());

        var selected = ModbusFunctionOptions.FirstOrDefault(option => option.FunctionCode == selectedFunctionCode)
            ?? ModbusFunctionOptions[0];
        comboBox.SelectedItem = selected;
        return comboBox;
    }

    private static readonly ModbusFunctionOption[] ModbusFunctionOptions =
    {
        new("Coil", 0x01),
        new("Discrete", 0x02),
        new("Holding", 0x03),
        new("Input", 0x04)
    };

    private async void OnGridCellDoubleClick(SlaveColumnConfig config, DataGridViewCellEventArgs args, DataGridView grid)
    {
        if (args.RowIndex < 0 || args.ColumnIndex < 0)
        {
            return;
        }

        if (grid.Columns[args.ColumnIndex].Name != "registerValue")
        {
            return;
        }

        if (!serialPortService.IsOpen)
        {
            MessageBox.Show("Open the serial port first.", "Write Value", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (config.FunctionCode is not 0x01 and not 0x03)
        {
            MessageBox.Show("Only Coil and Holding support writing in this view.", "Write Value", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var address = config.StartRegister + args.RowIndex;
        var currentValue = grid.Rows[args.RowIndex].Cells["registerValue"].Value?.ToString() ?? string.Empty;
        var promptValue = PromptForRegisterWriteValue(config, args.RowIndex, address, currentValue);
        if (promptValue is null)
        {
            return;
        }

        if (!TryBuildWriteRequest(config, args.RowIndex, promptValue, out var startAddress, out var values, out var coilValue, out var errorMessage))
        {
            MessageBox.Show(errorMessage, "Write Value", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            await modbusOperationLock.WaitAsync();

            if (config.FunctionCode == 0x01)
            {
                await readController.WriteCoilAsync((byte)config.SlaveId, (ushort)startAddress, coilValue, CancellationToken.None);
            }
            else
            {
                await readController.WriteHoldingRegistersAsync((byte)config.SlaveId, (ushort)startAddress, values, CancellationToken.None);
            }

            ApplyWriteToCachedValues(config, startAddress, values);
            nextPollDueTimes[config.Key] = DateTimeOffset.UtcNow;
            ApplyRenderedValues(config);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[raw] [write-error] S" + config.SlaveId + ": " + ex.Message);
            MessageBox.Show(ex.Message, "Write Value", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (modbusOperationLock.CurrentCount == 0)
            {
                modbusOperationLock.Release();
            }
        }
    }

    private string? PromptForRegisterWriteValue(SlaveColumnConfig config, int rowIndex, int address, string currentValue)
    {
        using var prompt = new Form
        {
            Text = "Write " + GetFunctionLabel(config.FunctionCode),
            Width = 360,
            Height = 170,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        };

        var addressLabel = new Label
        {
            Left = 12,
            Top = 12,
            Width = 320,
            Text = "Address " + FormatRegisterNumber(config.Key, address)
        };

        var hintLabel = new Label
        {
            Left = 12,
            Top = 30,
            Width = 320,
            Text = BuildWriteHint(config, rowIndex),
            ForeColor = System.Drawing.Color.FromArgb(100, 116, 139)
        };

        var valueTextBox = new TextBox
        {
            Left = 12,
            Top = 54,
            Width = 320,
            Text = currentValue == "-" ? string.Empty : currentValue
        };

        var writeButton = new Button
        {
            Text = "Write",
            Left = 176,
            Width = 74,
            Top = 92,
            DialogResult = DialogResult.OK
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 258,
            Width = 74,
            Top = 92,
            DialogResult = DialogResult.Cancel
        };

        prompt.Controls.Add(addressLabel);
        prompt.Controls.Add(hintLabel);
        prompt.Controls.Add(valueTextBox);
        prompt.Controls.Add(writeButton);
        prompt.Controls.Add(cancelButton);
        prompt.AcceptButton = writeButton;
        prompt.CancelButton = cancelButton;

        if (prompt.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        return valueTextBox.Text.Trim();
    }

    private static string BuildWriteHint(SlaveColumnConfig config, int rowIndex)
    {
        return config.FunctionCode switch
        {
            0x01 => "Enter 0/1, true/false, or on/off.",
            0x03 => GetHoldingWriteHint(ParseRegisterValueDataType(config.RegisterValueDataType), rowIndex),
            _ => "Enter a value."
        };
    }

    private static string GetHoldingWriteHint(RegisterValueDataType dataType, int rowIndex)
    {
        return dataType switch
        {
            RegisterValueDataType.Hex => "Enter a 16-bit hex value, for example 0x1234.",
            RegisterValueDataType.Int => "Enter a signed 16-bit integer.",
            RegisterValueDataType.String => "Enter up to 2 characters.",
            RegisterValueDataType.DInt => rowIndex % 2 == 0
                ? "Enter a signed 32-bit integer. This writes the selected row and the next row."
                : "Select the first row of the 32-bit value.",
            RegisterValueDataType.UDInt => rowIndex % 2 == 0
                ? "Enter an unsigned 32-bit integer. This writes the selected row and the next row."
                : "Select the first row of the 32-bit value.",
            RegisterValueDataType.Float => rowIndex % 2 == 0
                ? "Enter a float. This writes the selected row and the next row."
                : "Select the first row of the 32-bit value.",
            _ => "Enter an unsigned 16-bit integer."
        };
    }

    private bool TryBuildWriteRequest(SlaveColumnConfig config, int rowIndex, string input, out int startAddress, out ushort[] values, out bool coilValue, out string errorMessage)
    {
        startAddress = config.StartRegister + rowIndex;
        values = Array.Empty<ushort>();
        coilValue = false;
        errorMessage = string.Empty;

        if (config.FunctionCode == 0x01)
        {
            if (!TryParseCoilValue(input, out coilValue))
            {
                errorMessage = "Coil values must be 0/1, true/false, or on/off.";
                return false;
            }

            values = new[] { (ushort)(coilValue ? 1 : 0) };
            return true;
        }

        var dataType = GetRegisterValueDataType(config.Key);
        switch (dataType)
        {
            case RegisterValueDataType.UInt:
                if (!ushort.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uintValue))
                {
                    errorMessage = "Enter a value between 0 and 65535.";
                    return false;
                }

                values = new[] { uintValue };
                return true;

            case RegisterValueDataType.Int:
                if (!short.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                {
                    errorMessage = "Enter a signed 16-bit value between -32768 and 32767.";
                    return false;
                }

                values = new[] { unchecked((ushort)intValue) };
                return true;

            case RegisterValueDataType.Hex:
                if (!TryParseHexUShort(input, out var hexValue))
                {
                    errorMessage = "Enter a 16-bit hex value like 0x1234 or 1234.";
                    return false;
                }

                values = new[] { hexValue };
                return true;

            case RegisterValueDataType.String:
                if (string.IsNullOrEmpty(input))
                {
                    errorMessage = "Enter at least 1 character.";
                    return false;
                }

                values = new[] { EncodeStringRegister(input) };
                return true;

            case RegisterValueDataType.DInt:
                if (rowIndex % 2 != 0)
                {
                    errorMessage = "Select the first row of the 32-bit value.";
                    return false;
                }

                if (rowIndex + 1 >= config.RegisterCount)
                {
                    errorMessage = "The selected 32-bit value needs one more register row.";
                    return false;
                }

                if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dintValue))
                {
                    errorMessage = "Enter a signed 32-bit integer.";
                    return false;
                }

                values = SplitUInt32ToRegisters(unchecked((uint)dintValue));
                return true;

            case RegisterValueDataType.UDInt:
                if (rowIndex % 2 != 0)
                {
                    errorMessage = "Select the first row of the 32-bit value.";
                    return false;
                }

                if (rowIndex + 1 >= config.RegisterCount)
                {
                    errorMessage = "The selected 32-bit value needs one more register row.";
                    return false;
                }

                if (!uint.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var udintValue))
                {
                    errorMessage = "Enter an unsigned 32-bit integer.";
                    return false;
                }

                values = SplitUInt32ToRegisters(udintValue);
                return true;

            case RegisterValueDataType.Float:
                if (rowIndex % 2 != 0)
                {
                    errorMessage = "Select the first row of the 32-bit value.";
                    return false;
                }

                if (rowIndex + 1 >= config.RegisterCount)
                {
                    errorMessage = "The selected 32-bit value needs one more register row.";
                    return false;
                }

                if (!float.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var floatValue))
                {
                    errorMessage = "Enter a floating-point value.";
                    return false;
                }

                values = SplitUInt32ToRegisters(BitConverter.SingleToUInt32Bits(floatValue));
                return true;

            default:
                errorMessage = "The selected data type is not writable.";
                return false;
        }
    }

    private void ApplyWriteToCachedValues(SlaveColumnConfig config, int startAddress, IReadOnlyList<ushort> values)
    {
        if (!slaveLastValues.TryGetValue(config.Key, out var rawValues))
        {
            rawValues = Enumerable.Repeat((ushort)0, config.RegisterCount).ToList();
            slaveLastValues[config.Key] = rawValues;
        }

        while (rawValues.Count < config.RegisterCount)
        {
            rawValues.Add(0);
        }

        var startIndex = startAddress - config.StartRegister;
        for (var i = 0; i < values.Count; i++)
        {
            var targetIndex = startIndex + i;
            if (targetIndex >= 0 && targetIndex < rawValues.Count)
            {
                rawValues[targetIndex] = values[i];
            }
        }
    }

    private static bool TryParseCoilValue(string input, out bool value)
    {
        switch (input.Trim().ToLowerInvariant())
        {
            case "1":
            case "true":
            case "on":
                value = true;
                return true;
            case "0":
            case "false":
            case "off":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static bool TryParseHexUShort(string input, out ushort value)
    {
        var normalized = input.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        return ushort.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static ushort EncodeStringRegister(string input)
    {
        var text = input.Length >= 2 ? input[..2] : input.PadRight(2, ' ');
        return (ushort)((text[0] << 8) | text[1]);
    }

    private static ushort[] SplitUInt32ToRegisters(uint value)
    {
        return
        [
            (ushort)(value >> 16),
            (ushort)(value & 0xFFFF)
        ];
    }

    private static string GetFunctionLabel(byte functionCode)
    {
        return ModbusFunctionOptions.FirstOrDefault(option => option.FunctionCode == functionCode)?.Label ?? "Value";
    }

    private static int ParseCardInput(string text, int fallbackValue, int minimum, int maximum)
    {
        if (!int.TryParse(text, out var value))
        {
            return fallbackValue;
        }

        return Math.Max(minimum, Math.Min(maximum, value));
    }

    private void UpdateGridValues(SlaveColumnConfig config, IReadOnlyList<RegisterPoint> points)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateGridValues(config, points));
            return;
        }

        if (!slaveGrids.TryGetValue(config.Key, out var grid))
        {
            return;
        }

        slaveLastValues[config.Key] = points.Select(point => point.Value).ToList();
        slaveReadCounts[config.Key] = slaveReadCounts.GetValueOrDefault(config.Key) + 1;
        UpdateStatusLabel(config.Key, null);
        ApplyRenderedValues(config);
    }

    private void ApplyRenderedValues(SlaveColumnConfig config)
    {
        if (!slaveGrids.TryGetValue(config.Key, out var grid))
        {
            return;
        }

        if (!slaveLastValues.TryGetValue(config.Key, out var rawValues))
        {
            rawValues = new List<ushort>();
        }

        for (var rowIndex = 0; rowIndex < grid.Rows.Count; rowIndex++)
        {
            var registerNumber = config.StartRegister + rowIndex;
            grid.Rows[rowIndex].Cells["registerNumber"].Value = FormatRegisterNumber(config.Key, registerNumber);

            if (rowIndex < rawValues.Count)
            {
                grid.Rows[rowIndex].Cells["registerValue"].Value = FormatRegisterValue(config.Key, rawValues, rowIndex);
            }
            else
            {
                grid.Rows[rowIndex].Cells["registerValue"].Value = "-";
            }

            if (slaveDescriptions.TryGetValue(config.Key, out var descriptions)
                && descriptions.TryGetValue(registerNumber, out var description))
            {
                grid.Rows[rowIndex].Cells["description"].Value = description;
            }
        }
    }

    private void OnDescriptionEdited(SlaveColumnConfig config, DataGridViewCellEventArgs args, DataGridView grid)
    {
        if (args.RowIndex < 0 || args.ColumnIndex < 0)
        {
            return;
        }

        if (grid.Columns[args.ColumnIndex].Name != "description")
        {
            return;
        }

        if (!slaveDescriptions.TryGetValue(config.Key, out var descriptions))
        {
            descriptions = new Dictionary<int, string>();
            slaveDescriptions[config.Key] = descriptions;
        }

        var registerNumber = config.StartRegister + args.RowIndex;
        var text = grid.Rows[args.RowIndex].Cells["description"].Value?.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            descriptions.Remove(registerNumber);
        }
        else
        {
            descriptions[registerNumber] = text;
        }

        SyncDisplaySettingsToConfig(config);
    }

    private void SyncDisplaySettingsToConfig(SlaveColumnConfig config)
    {
        config.RegisterNumberFormat = GetRegisterNumberFormat(config.Key).ToString();
        config.RegisterValueDataType = GetRegisterValueDataType(config.Key).ToString();
        config.ShowDescriptionColumn = slaveDescriptionColumnVisible.TryGetValue(config.Key, out var visible) && visible;
        config.Descriptions = slaveDescriptions.TryGetValue(config.Key, out var descriptions)
            ? new Dictionary<int, string>(descriptions)
            : new Dictionary<int, string>();
    }

    private RegisterNumberFormat GetRegisterNumberFormat(string key)
    {
        return slaveRegisterNumberFormats.TryGetValue(key, out var mode)
            ? mode
            : RegisterNumberFormat.Decimal;
    }

    private RegisterValueDataType GetRegisterValueDataType(string key)
    {
        return slaveRegisterValueDataTypes.TryGetValue(key, out var type)
            ? type
            : RegisterValueDataType.UInt;
    }

    private static RegisterNumberFormat ParseRegisterNumberFormat(string? value)
    {
        return Enum.TryParse<RegisterNumberFormat>(value, true, out var parsed)
            ? parsed
            : RegisterNumberFormat.Decimal;
    }

    private static RegisterValueDataType ParseRegisterValueDataType(string? value)
    {
        return Enum.TryParse<RegisterValueDataType>(value, true, out var parsed)
            ? parsed
            : RegisterValueDataType.UInt;
    }

    private void ApplyDescriptionColumnVisibility(string key)
    {
        if (!slaveGrids.TryGetValue(key, out var grid))
        {
            return;
        }

        if (!grid.Columns.Contains("description"))
        {
            return;
        }

        var visible = slaveDescriptionColumnVisible.TryGetValue(key, out var isVisible) && isVisible;
        grid.Columns["description"].Visible = visible;
    }

    private string FormatRegisterNumber(string key, int registerNumber)
    {
        if (slaveRegisterNumberFormats.TryGetValue(key, out var mode) && mode == RegisterNumberFormat.Hex)
        {
            return "0x" + registerNumber.ToString("X4");
        }

        return registerNumber.ToString();
    }

    private string FormatRegisterValue(string key, IReadOnlyList<ushort> values, int rowIndex)
    {
        if (!slaveRegisterValueDataTypes.TryGetValue(key, out var dataType))
        {
            dataType = RegisterValueDataType.UInt;
        }

        var current = values[rowIndex];
        return dataType switch
        {
            RegisterValueDataType.Hex => "0x" + current.ToString("X4"),
            RegisterValueDataType.Int => unchecked((short)current).ToString(),
            RegisterValueDataType.String => FormatStringValue(current),
            RegisterValueDataType.DInt => FormatDIntValue(values, rowIndex),
            RegisterValueDataType.UDInt => FormatUDIntValue(values, rowIndex),
            RegisterValueDataType.Float => FormatFloatValue(values, rowIndex),
            _ => current.ToString()
        };
    }

    private static string FormatStringValue(ushort value)
    {
        var high = (char)((value >> 8) & 0xFF);
        var low = (char)(value & 0xFF);
        return string.Concat(ToPrintable(high), ToPrintable(low));
    }

    private static char ToPrintable(char value)
    {
        return char.IsControl(value) ? '.' : value;
    }

    private static string FormatDIntValue(IReadOnlyList<ushort> values, int rowIndex)
    {
        if (!TryGetWordPair(values, rowIndex, out var combined, out var isLeadWord))
        {
            return "n/a";
        }

        if (!isLeadWord)
        {
            return string.Empty;
        }

        return unchecked((int)combined).ToString();
    }

    private static string FormatUDIntValue(IReadOnlyList<ushort> values, int rowIndex)
    {
        if (!TryGetWordPair(values, rowIndex, out var combined, out var isLeadWord))
        {
            return "n/a";
        }

        if (!isLeadWord)
        {
            return string.Empty;
        }

        return combined.ToString();
    }

    private static string FormatFloatValue(IReadOnlyList<ushort> values, int rowIndex)
    {
        if (!TryGetWordPair(values, rowIndex, out var combined, out var isLeadWord))
        {
            return "n/a";
        }

        if (!isLeadWord)
        {
            return string.Empty;
        }

        var bytes = BitConverter.GetBytes(combined);
        var value = BitConverter.ToSingle(bytes, 0);
        return value.ToString("G6");
    }

    private static bool TryGetWordPair(IReadOnlyList<ushort> values, int rowIndex, out uint combined, out bool isLeadWord)
    {
        combined = 0;
        isLeadWord = rowIndex % 2 == 0;

        var baseIndex = isLeadWord ? rowIndex : rowIndex - 1;
        if (baseIndex < 0 || baseIndex + 1 >= values.Count)
        {
            return false;
        }

        combined = ((uint)values[baseIndex] << 16) | values[baseIndex + 1];
        return true;
    }

    private void SetTableError(SlaveColumnConfig config, string error)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetTableError(config, error));
            return;
        }

        if (!slaveGrids.TryGetValue(config.Key, out var grid))
        {
            return;
        }

        for (var rowIndex = 0; rowIndex < grid.Rows.Count; rowIndex++)
        {
            grid.Rows[rowIndex].Cells["registerValue"].Value = rowIndex == 0 ? "ERR" : string.Empty;
        }

        slaveErrorCounts[config.Key] = slaveErrorCounts.GetValueOrDefault(config.Key) + 1;
        UpdateStatusLabel(config.Key, ClassifyError(error));
        Console.WriteLine("[raw] [poll-error] S" + config.SlaveId + ": " + error);
    }

    private void UpdateStatusLabel(string key, string? lastError)
    {
        if (!slaveStatusLabels.TryGetValue(key, out var label))
        {
            return;
        }

        var reads = slaveReadCounts.GetValueOrDefault(key);
        var errors = slaveErrorCounts.GetValueOrDefault(key);
        label.Text = $"Reads: {reads}  Err: {errors}" + (lastError is not null ? $"  [{lastError}]" : string.Empty);
        label.ForeColor = errors > 0
            ? System.Drawing.Color.FromArgb(220, 38, 38)
            : System.Drawing.Color.FromArgb(71, 85, 105);
    }

    private static string ClassifyError(string message) => message switch
    {
        _ when message.Contains("timeout", StringComparison.OrdinalIgnoreCase) => "Timeout",
        _ when message.Contains("cancel", StringComparison.OrdinalIgnoreCase) => "Timeout",
        _ when message.Contains("CRC", StringComparison.OrdinalIgnoreCase) => "CRC",
        _ when message.Contains("mismatch", StringComparison.OrdinalIgnoreCase) => "Frame mismatch",
        _ when message.Contains("too short", StringComparison.OrdinalIgnoreCase) => "Short frame",
        _ => message.Length > 25 ? message[..25] + "\u2026" : message
    };

    private void StopPolling()
    {
        pollingEnabled = false;

        if (pollingCancellationTokenSource is null)
        {
            return;
        }

        pollingCancellationTokenSource.Cancel();
        pollingCancellationTokenSource.Dispose();
        pollingCancellationTokenSource = null;
        pollButton.Text = "Start Poll";
    }

    private SerialPortSettings BuildSerialSettings()
    {
        var frame = frameComboBox.SelectedItem?.ToString() ?? "8N1";
        var dataBits = int.Parse(frame[..1]);
        var parity = frame[1] switch
        {
            'E' => Parity.Even,
            'O' => Parity.Odd,
            _ => Parity.None
        };
        var stopBits = frame[2] == '2' ? StopBits.Two : StopBits.One;

        var selectedPort = portComboBox.SelectedItem as ComPortDeviceInfo;

        return new SerialPortSettings
        {
            PortName = selectedPort?.PortName ?? "COM1",
            BaudRate = int.Parse(baudRateComboBox.SelectedItem?.ToString() ?? "19200"),
            DataBits = dataBits,
            Parity = parity,
            StopBits = stopBits,
            RtsEnable = rtsCheckBox.Checked,
            DtrEnable = dtrCheckBox.Checked
        };
    }

    private string BuildNextColumnKey()
    {
        nextColumnKey++;
        return "slave" + nextColumnKey;
    }

    private static readonly (string Label, RegisterValueDataType DataType)[] RegisterValueDataTypeItems =
    {
        ("UINT", RegisterValueDataType.UInt),
        ("INT", RegisterValueDataType.Int),
        ("Hex", RegisterValueDataType.Hex),
        ("String", RegisterValueDataType.String),
        ("UDINT", RegisterValueDataType.UDInt),
        ("DINT", RegisterValueDataType.DInt),
        ("FLOAT", RegisterValueDataType.Float)
    };

    private enum RegisterNumberFormat
    {
        Decimal,
        Hex
    }

    private enum RegisterValueDataType
    {
        UInt,
        Int,
        Hex,
        String,
        DInt,
        UDInt,
        Float
    }

    private sealed record ModbusFunctionOption(string Label, byte FunctionCode);
}
