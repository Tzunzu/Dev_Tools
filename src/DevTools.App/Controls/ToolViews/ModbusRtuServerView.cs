using DevTools.App.Libraries.Com;
using DevTools.App.Libraries.Modbus;
using DevTools.App.Infrastructure.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace DevTools.App.Controls.ToolViews;

internal sealed class ModbusRtuServerView : UserControl
{
    private readonly TableLayoutPanel rootLayout;
    private readonly GroupBox connectionGroup;
    private readonly TableLayoutPanel connectionLayout;
    private readonly ComboBox portComboBox;
    private readonly ComboBox baudRateComboBox;
    private readonly ComboBox frameComboBox;
    private readonly TextBox unitIdTextBox;
    private readonly CheckBox rtsCheckBox;
    private readonly CheckBox dtrCheckBox;
    private readonly Button startServerButton;
    private readonly Button refreshPortsButton;
    private readonly Label connectionStatusLabel;

    private readonly GroupBox mapsGroup;
    private readonly TableLayoutPanel mapsLayout;
    private readonly FlowLayoutPanel mapsToolbar;
    private readonly Button seedDataButton;
    private readonly Button clearDataButton;
    private readonly Button saveConfigButton;
    private readonly Button updateConfigButton;
    private readonly ComboBox configPresetComboBox;
    private readonly Button renameConfigButton;
    private readonly Button deleteConfigButton;
    private readonly ThemedHorizontalScrollFlowPanel mapCardsHostPanel;

    private readonly ModbusRtuServerDataStore dataStore = new();
    private readonly ModbusRtuServer server;
    private readonly System.Windows.Forms.Timer storeSyncTimer;
    private readonly List<ServerMapCard> mapCards = new();
    private readonly Dictionary<string, ServerSavedConfigEntry> savedConfigurations = new(StringComparer.OrdinalIgnoreCase);
    private readonly string savedConfigFilePath;

    private bool syncFromStoreInProgress;
    private bool applyingConfigurationPreset;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ModbusRtuServerView()
    {
        Dock = DockStyle.Fill;

        savedConfigFilePath = BuildSavedConfigFilePath();
        server = new ModbusRtuServer(dataStore);
        server.Log += static message => Console.WriteLine(message);

        storeSyncTimer = new System.Windows.Forms.Timer { Interval = 300 };
        storeSyncTimer.Tick += (_, _) => SynchronizeCardsFromStore();

        rootLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            RowCount = 2
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        connectionGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(10, 8, 10, 8),
            Text = "RTU Server"
        };

        connectionLayout = new TableLayoutPanel
        {
            ColumnCount = 9,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            RowCount = 1
        };
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        portComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            Name = "portComboBox"
        };

        baudRateComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true
        };
        baudRateComboBox.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });

        frameComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true
        };
        frameComboBox.Items.AddRange(new object[] { "8N1", "8E1", "8O1", "8N2", "7E1" });

        unitIdTextBox = new TextBox
        {
            Text = "1"
        };

        var portField = BuildLabeledField("Port", portComboBox, 132);
        var baudField = BuildLabeledField("Baud", baudRateComboBox, 72);
        var frameField = BuildLabeledField("Frame", frameComboBox, 74);
        var unitIdField = BuildLabeledField("ID", unitIdTextBox, 56);

        rtsCheckBox = new CheckBox { Anchor = AnchorStyles.Left, AutoSize = true, Text = "RTS" };
        dtrCheckBox = new CheckBox { Anchor = AnchorStyles.Left, AutoSize = true, Text = "DTR" };

        startServerButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Start"
        };
        startServerButton.Click += startServerButton_Click;

        refreshPortsButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Refresh"
        };
        refreshPortsButton.Click += refreshPortsButton_Click;

        connectionStatusLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            ForeColor = AppTheme.GetStatusColor(StatusTone.Error),
            Text = "Stopped"
        };

        connectionLayout.Controls.Add(portField, 0, 0);
        connectionLayout.Controls.Add(baudField, 1, 0);
        connectionLayout.Controls.Add(frameField, 2, 0);
        connectionLayout.Controls.Add(unitIdField, 3, 0);
        connectionLayout.Controls.Add(rtsCheckBox, 4, 0);
        connectionLayout.Controls.Add(dtrCheckBox, 5, 0);
        connectionLayout.Controls.Add(startServerButton, 6, 0);
        connectionLayout.Controls.Add(refreshPortsButton, 7, 0);
        connectionLayout.Controls.Add(connectionStatusLabel, 8, 0);
        connectionGroup.Controls.Add(connectionLayout);

        mapsGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 8, 10, 8),
            Text = "Register Maps"
        };

        mapsLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        mapsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mapsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        mapsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        mapsToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            WrapContents = false
        };

        seedDataButton = new Button
        {
            Location = new Point(0, 3),
            Margin = new Padding(0, 3, 8, 3),
            Size = new Size(92, 27),
            Text = "Seed Data"
        };
        seedDataButton.Click += seedDataButton_Click;

        clearDataButton = new Button
        {
            Margin = new Padding(0, 3, 8, 3),
            Size = new Size(92, 27),
            Text = "Clear Data"
        };
        clearDataButton.Click += clearDataButton_Click;

        saveConfigButton = new Button
        {
            Margin = new Padding(0, 3, 8, 3),
            Size = new Size(92, 27),
            Text = "Save Config"
        };
        saveConfigButton.Click += saveConfigButton_Click;

        updateConfigButton = new Button
        {
            Enabled = false,
            Margin = new Padding(0, 3, 8, 3),
            Size = new Size(100, 27),
            Text = "Update Config"
        };
        updateConfigButton.Click += updateConfigButton_Click;

        configPresetComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            Margin = new Padding(0, 4, 0, 3),
            Size = new Size(130, 23)
        };
        configPresetComboBox.SelectedIndexChanged += configPresetComboBox_SelectedIndexChanged;

        renameConfigButton = new Button
        {
            Enabled = false,
            Margin = new Padding(8, 3, 6, 3),
            Size = new Size(66, 27),
            Text = "Rename"
        };
        renameConfigButton.Click += renameConfigButton_Click;

        deleteConfigButton = new Button
        {
            Enabled = false,
            Margin = new Padding(0, 3, 0, 3),
            Size = new Size(60, 27),
            Text = "Delete"
        };
        deleteConfigButton.Click += deleteConfigButton_Click;

        mapsToolbar.Controls.Add(seedDataButton);
        mapsToolbar.Controls.Add(clearDataButton);
        mapsToolbar.Controls.Add(saveConfigButton);
        mapsToolbar.Controls.Add(updateConfigButton);
        mapsToolbar.Controls.Add(configPresetComboBox);
        mapsToolbar.Controls.Add(renameConfigButton);
        mapsToolbar.Controls.Add(deleteConfigButton);

        mapCardsHostPanel = new ThemedHorizontalScrollFlowPanel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            WrapContents = false
        };
        mapCardsHostPanel.SizeChanged += (_, _) => ResizeMapCards();

        mapsLayout.Controls.Add(mapsToolbar, 0, 0);
        mapsLayout.Controls.Add(mapCardsHostPanel, 0, 1);
        mapsGroup.Controls.Add(mapsLayout);

        rootLayout.Controls.Add(connectionGroup, 0, 0);
        rootLayout.Controls.Add(mapsGroup, 0, 1);
        Controls.Add(rootLayout);

        InitializeMapCards();
        PopulateComPorts();

        baudRateComboBox.SelectedIndex = 1;
        frameComboBox.SelectedIndex = 0;

        LoadConfigurationsFromDisk();
        UpdatePresetActionButtons();
        SetServerUiState(running: false);
        storeSyncTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            storeSyncTimer.Stop();
            storeSyncTimer.Dispose();
            server.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeMapCards()
    {
        mapCards.Clear();
        mapCardsHostPanel.Controls.Clear();

        mapCards.Add(CreateCard("Holding Registers", ModbusDataArea.HoldingRegisters, MapCardKind.UInt16, 0, 16));
        mapCards.Add(CreateCard("Input Registers", ModbusDataArea.InputRegisters, MapCardKind.UInt16, 0, 16));
        mapCards.Add(CreateCard("Discrete Inputs", ModbusDataArea.DiscreteInputs, MapCardKind.Boolean, 0, 16));
        mapCards.Add(CreateCard("Coils", ModbusDataArea.Coils, MapCardKind.Boolean, 0, 16));

        foreach (var card in mapCards)
        {
            mapCardsHostPanel.Controls.Add(card.Group);
            RebuildCardRows(card);
        }

        ResizeMapCards();
    }

    private ServerMapCard CreateCard(string title, ModbusDataArea area, MapCardKind kind, int startAddress, int pointCount)
    {
        var group = new GroupBox
        {
            ForeColor = Color.FromArgb(51, 65, 85),
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(6),
            Text = title
        };

        var rootPanel = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            RowCount = 2
        };
        rootPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var headerPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Left,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            WrapContents = false
        };

        var startTextBox = new TextBox
        {
            Text = startAddress.ToString(CultureInfo.InvariantCulture)
        };

        var countTextBox = new TextBox
        {
            Text = pointCount.ToString(CultureInfo.InvariantCulture)
        };

        var startField = BuildLabeledField("Start", startTextBox, 62);
        var countField = BuildLabeledField("Count", countTextBox, 62);
        var applyButton = new Button
        {
            Margin = new Padding(8, 2, 0, 0),
            Size = new Size(64, 27),
            Text = "Apply"
        };

        headerPanel.Controls.Add(startField);
        headerPanel.Controls.Add(countField);
        headerPanel.Controls.Add(applyButton);

        var grid = new ThemedDataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            EditMode = DataGridViewEditMode.EditOnEnter,
            MultiSelect = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect
        };

        var addressColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Address",
            Name = "Address",
            ReadOnly = true,
            Width = 82
        };
        grid.Columns.Add(addressColumn);

        if (kind == MapCardKind.Boolean)
        {
            var valueColumn = new DataGridViewCheckBoxColumn
            {
                HeaderText = "Value",
                Name = "Value",
                Width = 68
            };
            grid.Columns.Add(valueColumn);
        }
        else
        {
            var valueColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "Value",
                Name = "Value",
                Width = 80
            };
            grid.Columns.Add(valueColumn);
        }

        var descriptionColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Description",
            Name = "Description",
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 120,
            Visible = false
        };
        grid.Columns.Add(descriptionColumn);

        rootPanel.Controls.Add(headerPanel, 0, 0);
        rootPanel.Controls.Add(grid, 0, 1);
        group.Controls.Add(rootPanel);

        var card = new ServerMapCard(title, area, kind, group, grid, startTextBox, countTextBox);

        var editDescriptionMenuItem = new ToolStripMenuItem("Add/Edit Description");
        editDescriptionMenuItem.Click += (_, _) => EditSelectedRowDescription(card);

        var clearDescriptionMenuItem = new ToolStripMenuItem("Clear Description");
        clearDescriptionMenuItem.Click += (_, _) => ClearSelectedRowDescription(card);

        var showDescriptionColumnMenuItem = new ToolStripMenuItem("Show Description Column")
        {
            CheckOnClick = true,
            Checked = card.ShowDescriptionColumn
        };
        showDescriptionColumnMenuItem.CheckedChanged += (_, _) =>
        {
            card.ShowDescriptionColumn = showDescriptionColumnMenuItem.Checked;
            ApplyDescriptionColumnVisibility(card);
        };

        var cardContextMenu = new ContextMenuStrip();
        cardContextMenu.Items.Add(editDescriptionMenuItem);
        cardContextMenu.Items.Add(clearDescriptionMenuItem);
        cardContextMenu.Items.Add(new ToolStripSeparator());
        cardContextMenu.Items.Add(showDescriptionColumnMenuItem);

        group.ContextMenuStrip = cardContextMenu;
        rootPanel.ContextMenuStrip = cardContextMenu;
        headerPanel.ContextMenuStrip = cardContextMenu;
        grid.ContextMenuStrip = cardContextMenu;

        applyButton.Click += (_, _) => RebuildCardRows(card);
        grid.CellValueChanged += (_, args) => OnGridCellValueChanged(card, args);
        grid.CellMouseDown += (_, args) =>
        {
            if (args.Button != MouseButtons.Right || args.RowIndex < 0)
            {
                return;
            }

            grid.ClearSelection();
            grid.Rows[args.RowIndex].Selected = true;
            if (args.ColumnIndex >= 0)
            {
                grid.CurrentCell = grid.Rows[args.RowIndex].Cells[args.ColumnIndex];
            }
        };
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        grid.DataError += (_, _) => { };

        return card;
    }

    private void OnGridCellValueChanged(ServerMapCard card, DataGridViewCellEventArgs args)
    {
        if (args.RowIndex < 0 || args.ColumnIndex < 0)
        {
            return;
        }

        if (card.Grid.Columns[args.ColumnIndex].Name != "Value")
        {
            return;
        }

        TryUpdateStoreFromCard(card);
    }

    private void RebuildCardRows(ServerMapCard card)
    {
        if (!int.TryParse(card.StartAddressTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startAddress) || startAddress < 0)
        {
            startAddress = 0;
            card.StartAddressTextBox.Text = "0";
        }

        if (startAddress > ushort.MaxValue)
        {
            startAddress = ushort.MaxValue;
            card.StartAddressTextBox.Text = ushort.MaxValue.ToString(CultureInfo.InvariantCulture);
        }

        if (!int.TryParse(card.CountTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 1)
        {
            count = 1;
            card.CountTextBox.Text = "1";
        }

        count = Math.Min(250, count);
        count = Math.Min(count, (ushort.MaxValue - startAddress) + 1);
        card.CountTextBox.Text = count.ToString(CultureInfo.InvariantCulture);

        syncFromStoreInProgress = true;
        card.Grid.Rows.Clear();

        for (var index = 0; index < count; index++)
        {
            var address = startAddress + index;
            var rowIndex = card.Grid.Rows.Add();
            card.Grid.Rows[rowIndex].Cells["Address"].Value = address.ToString(CultureInfo.InvariantCulture);
            card.Grid.Rows[rowIndex].Cells["Value"].Value = card.Kind == MapCardKind.Boolean ? false : "0";

            if (card.Descriptions.TryGetValue(address, out var description))
            {
                card.Grid.Rows[rowIndex].Cells["Description"].Value = description;
            }
            else
            {
                card.Grid.Rows[rowIndex].Cells["Description"].Value = string.Empty;
            }
        }

        ApplyDescriptionColumnVisibility(card);
        card.Grid.ClearSelection();
        syncFromStoreInProgress = false;
        TryUpdateStoreFromCard(card);
    }

    private void ResizeMapCards()
    {
        if (mapCards.Count == 0)
        {
            return;
        }

        var totalSpacing = 6 * (mapCards.Count - 1);
        var availableWidth = mapCardsHostPanel.DisplayRectangle.Width - totalSpacing;
        if (availableWidth <= 0)
        {
            return;
        }

        var cardWidth = Math.Max(230, availableWidth / mapCards.Count);

        for (var index = 0; index < mapCards.Count; index++)
        {
            var card = mapCards[index];
            card.Group.Width = cardWidth;
            card.Group.Height = Math.Max(120, mapCardsHostPanel.DisplayRectangle.Height - 2);
            card.Group.Margin = index == mapCards.Count - 1
                ? new Padding(0)
                : new Padding(0, 0, 6, 0);
        }
    }

    private void seedDataButton_Click(object? sender, EventArgs e)
    {
        foreach (var card in mapCards)
        {
            for (var rowIndex = 0; rowIndex < card.Grid.Rows.Count; rowIndex++)
            {
                var row = card.Grid.Rows[rowIndex];
                row.Cells["Value"].Value = card.Kind == MapCardKind.Boolean
                    ? rowIndex % 2 == 0
                    : ((rowIndex + 1) * 10).ToString(CultureInfo.InvariantCulture);
            }

            card.Grid.ClearSelection();
            TryUpdateStoreFromCard(card);
        }
    }

    private void clearDataButton_Click(object? sender, EventArgs e)
    {
        foreach (var card in mapCards)
        {
            for (var rowIndex = 0; rowIndex < card.Grid.Rows.Count; rowIndex++)
            {
                var row = card.Grid.Rows[rowIndex];
                row.Cells["Value"].Value = card.Kind == MapCardKind.Boolean ? false : "0";
            }

            card.Grid.ClearSelection();
            TryUpdateStoreFromCard(card);
        }
    }

    private void startServerButton_Click(object? sender, EventArgs e)
    {
        if (!server.IsRunning)
        {
            if (portComboBox.SelectedItem is null)
            {
                MessageBox.Show(
                    this,
                    "Select a COM port before starting the server.",
                    "Modbus RTU Server",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!byte.TryParse(unitIdTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitId) || unitId is < 1 or > 247)
            {
                MessageBox.Show(
                    this,
                    "Unit ID must be between 1 and 247.",
                    "Modbus RTU Server",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var settings = BuildSerialSettings();
                server.Start(settings, unitId);
                SetServerUiState(running: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Unable to start Modbus RTU server",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            return;
        }

        server.Stop();
        SetServerUiState(running: false);
    }

    private void refreshPortsButton_Click(object? sender, EventArgs e)
    {
        PopulateComPorts();
    }

    private void saveConfigButton_Click(object? sender, EventArgs e)
    {
        var name = PromptForConfigurationName();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (savedConfigurations.ContainsKey(name))
        {
            var overwrite = MessageBox.Show(
                this,
                "A configuration with that name already exists. Overwrite it?",
                "Save Configuration",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (overwrite != DialogResult.Yes)
            {
                return;
            }
        }

        var snapshot = CaptureCurrentConfiguration(name);
        savedConfigurations[name] = snapshot;
        RefreshConfigurationDropdown(name);
        SaveConfigurationsToDisk();
    }

    private void updateConfigButton_Click(object? sender, EventArgs e)
    {
        var selectedName = GetSelectedPresetName();
        if (string.IsNullOrWhiteSpace(selectedName))
        {
            MessageBox.Show(
                this,
                "Select a configuration preset first.",
                "Update Configuration",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!savedConfigurations.ContainsKey(selectedName))
        {
            MessageBox.Show(
                this,
                "The selected preset is no longer available.",
                "Update Configuration",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        savedConfigurations[selectedName] = CaptureCurrentConfiguration(selectedName);
        RefreshConfigurationDropdown(selectedName);
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

        if (!savedConfigurations.TryGetValue(selectedName, out var config))
        {
            return;
        }

        ApplyConfiguration(config);
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
                this,
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
        config.Name = newName;
        savedConfigurations[newName] = config;
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
            this,
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

    private void PopulateComPorts()
    {
        var ports = SerialPort.GetPortNames()
            .OrderBy(static port => port, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var previousSelection = portComboBox.SelectedItem as string;

        portComboBox.BeginUpdate();
        try
        {
            portComboBox.Items.Clear();

            foreach (var port in ports)
            {
                portComboBox.Items.Add(port);
            }
        }
        finally
        {
            portComboBox.EndUpdate();
        }

        if (!string.IsNullOrWhiteSpace(previousSelection) && portComboBox.Items.Contains(previousSelection))
        {
            portComboBox.SelectedItem = previousSelection;
            return;
        }

        portComboBox.SelectedIndex = portComboBox.Items.Count > 0 ? 0 : -1;
    }

    private SerialPortSettings BuildSerialSettings()
    {
        var frame = frameComboBox.SelectedItem?.ToString() ?? "8N1";
        var dataBits = int.Parse(frame[..1], CultureInfo.InvariantCulture);
        var parity = frame[1] switch
        {
            'E' => Parity.Even,
            'O' => Parity.Odd,
            _ => Parity.None
        };
        var stopBits = frame[2] == '2' ? StopBits.Two : StopBits.One;

        return new SerialPortSettings
        {
            PortName = portComboBox.SelectedItem?.ToString() ?? "COM1",
            BaudRate = int.Parse(baudRateComboBox.SelectedItem?.ToString() ?? "19200", CultureInfo.InvariantCulture),
            DataBits = dataBits,
            Parity = parity,
            StopBits = stopBits,
            RtsEnable = rtsCheckBox.Checked,
            DtrEnable = dtrCheckBox.Checked
        };
    }

    private void SetServerUiState(bool running)
    {
        startServerButton.Text = running ? "Stop" : "Start";
        connectionStatusLabel.Text = running ? "Running" : "Stopped";
        connectionStatusLabel.ForeColor = running
            ? AppTheme.GetStatusColor(StatusTone.Success)
            : AppTheme.GetStatusColor(StatusTone.Error);

        portComboBox.Enabled = !running;
        baudRateComboBox.Enabled = !running;
        frameComboBox.Enabled = !running;
        unitIdTextBox.Enabled = !running;
        rtsCheckBox.Enabled = !running;
        dtrCheckBox.Enabled = !running;
        refreshPortsButton.Enabled = !running;
    }

    private static Control BuildLabeledField(string labelText, Control input, int inputWidth)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            WrapContents = false
        };

        var label = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 6, 6, 0),
            Text = labelText
        };

        input.Margin = new Padding(0, 2, 0, 0);
        input.Width = inputWidth;

        panel.Controls.Add(label);
        panel.Controls.Add(input);
        return panel;
    }

    private void TryUpdateStoreFromCard(ServerMapCard card)
    {
        if (syncFromStoreInProgress)
        {
            return;
        }

        if (!ushort.TryParse(card.StartAddressTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startAddress))
        {
            return;
        }

        try
        {
            if (card.Kind == MapCardKind.Boolean)
            {
                var values = new bool[card.Grid.Rows.Count];
                for (var rowIndex = 0; rowIndex < card.Grid.Rows.Count; rowIndex++)
                {
                    values[rowIndex] = TryParseBooleanCell(card.Grid.Rows[rowIndex].Cells["Value"].Value);
                }

                dataStore.ReplaceBooleanArea(card.Area, startAddress, values);
                return;
            }

            var registerValues = new ushort[card.Grid.Rows.Count];
            for (var rowIndex = 0; rowIndex < card.Grid.Rows.Count; rowIndex++)
            {
                var cellValue = card.Grid.Rows[rowIndex].Cells["Value"].Value?.ToString() ?? "0";
                if (!ushort.TryParse(cellValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out registerValues[rowIndex]))
                {
                    registerValues[rowIndex] = 0;
                    card.Grid.Rows[rowIndex].Cells["Value"].Value = "0";
                }
            }

            dataStore.ReplaceRegisterArea(card.Area, startAddress, registerValues);
        }
        catch (ModbusServerException)
        {
            // Ignore temporary invalid ranges while users edit card parameters.
        }
    }

    private void SynchronizeCardsFromStore()
    {
        syncFromStoreInProgress = true;
        try
        {
            foreach (var card in mapCards)
            {
                if (!ushort.TryParse(card.StartAddressTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startAddress))
                {
                    continue;
                }

                if (!ushort.TryParse(card.CountTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count == 0)
                {
                    continue;
                }

                if (card.Kind == MapCardKind.Boolean)
                {
                    var values = dataStore.ReadBooleans(card.Area, startAddress, count);
                    for (var rowIndex = 0; rowIndex < values.Length && rowIndex < card.Grid.Rows.Count; rowIndex++)
                    {
                        card.Grid.Rows[rowIndex].Cells["Value"].Value = values[rowIndex];
                    }
                }
                else
                {
                    var values = dataStore.ReadRegisters(card.Area, startAddress, count);
                    for (var rowIndex = 0; rowIndex < values.Length && rowIndex < card.Grid.Rows.Count; rowIndex++)
                    {
                        card.Grid.Rows[rowIndex].Cells["Value"].Value = values[rowIndex].ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
        }
        catch (ModbusServerException)
        {
            // Ignore temporary map-range mismatch while a card is being edited.
        }
        finally
        {
            syncFromStoreInProgress = false;
        }
    }

    private void EditSelectedRowDescription(ServerMapCard card)
    {
        var rowIndex = card.Grid.CurrentCell?.RowIndex ?? -1;
        if (rowIndex < 0 || rowIndex >= card.Grid.Rows.Count)
        {
            return;
        }

        var address = GetRowAddress(card, rowIndex);
        if (address < 0)
        {
            return;
        }

        card.Descriptions.TryGetValue(address, out var currentText);
        var editedText = PromptForDescription(address, currentText ?? string.Empty);
        if (editedText is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(editedText))
        {
            card.Descriptions.Remove(address);
            card.Grid.Rows[rowIndex].Cells["Description"].Value = string.Empty;
        }
        else
        {
            card.Descriptions[address] = editedText.Trim();
            card.Grid.Rows[rowIndex].Cells["Description"].Value = editedText.Trim();
        }
    }

    private void ClearSelectedRowDescription(ServerMapCard card)
    {
        var rowIndex = card.Grid.CurrentCell?.RowIndex ?? -1;
        if (rowIndex < 0 || rowIndex >= card.Grid.Rows.Count)
        {
            return;
        }

        var address = GetRowAddress(card, rowIndex);
        if (address < 0)
        {
            return;
        }

        card.Descriptions.Remove(address);
        card.Grid.Rows[rowIndex].Cells["Description"].Value = string.Empty;
    }

    private int GetRowAddress(ServerMapCard card, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= card.Grid.Rows.Count)
        {
            return -1;
        }

        var text = card.Grid.Rows[rowIndex].Cells["Address"].Value?.ToString();
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var address)
            ? address
            : -1;
    }

    private static string? PromptForDescription(int address, string initialValue)
    {
        using var prompt = new Form
        {
            Text = "Register Description",
            Width = 420,
            Height = 170,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        };

        var label = new Label
        {
            Left = 12,
            Top = 12,
            Width = 380,
            Text = "Address " + address.ToString(CultureInfo.InvariantCulture) + " description"
        };

        var textBox = new TextBox
        {
            Left = 12,
            Top = 34,
            Width = 380,
            Text = initialValue
        };

        var saveButton = new Button
        {
            Text = "Save",
            Left = 236,
            Width = 74,
            Top = 72,
            DialogResult = DialogResult.OK
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 318,
            Width = 74,
            Top = 72,
            DialogResult = DialogResult.Cancel
        };

        prompt.Controls.Add(label);
        prompt.Controls.Add(textBox);
        prompt.Controls.Add(saveButton);
        prompt.Controls.Add(cancelButton);
        prompt.AcceptButton = saveButton;
        prompt.CancelButton = cancelButton;

        if (prompt.ShowDialog() != DialogResult.OK)
        {
            return null;
        }

        return textBox.Text;
    }

    private void ApplyDescriptionColumnVisibility(ServerMapCard card)
    {
        if (!card.Grid.Columns.Contains("Description"))
        {
            return;
        }

        card.Grid.Columns["Description"].Visible = card.ShowDescriptionColumn;
    }

    private ServerSavedConfigEntry CaptureCurrentConfiguration(string name)
    {
        var config = new ServerSavedConfigEntry
        {
            Name = name,
            PortName = portComboBox.SelectedItem?.ToString() ?? string.Empty,
            BaudRate = int.TryParse(baudRateComboBox.SelectedItem?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var baudRate)
                ? baudRate
                : 19200,
            Frame = frameComboBox.SelectedItem?.ToString() ?? "8N1",
            UnitId = byte.TryParse(unitIdTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitId)
                ? unitId
                : (byte)1,
            RtsEnable = rtsCheckBox.Checked,
            DtrEnable = dtrCheckBox.Checked,
            Cards = mapCards.Select(CreateSavedCardConfig).ToList()
        };

        return config;
    }

    private ServerSavedCardConfig CreateSavedCardConfig(ServerMapCard card)
    {
        var start = ushort.TryParse(card.StartAddressTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startAddress)
            ? startAddress
            : (ushort)0;
        var count = ushort.TryParse(card.CountTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pointCount)
            ? pointCount
            : (ushort)0;

        var savedCard = new ServerSavedCardConfig
        {
            Area = card.Area.ToString(),
            StartAddress = start,
            Count = count,
            ShowDescriptionColumn = card.ShowDescriptionColumn,
            Descriptions = new Dictionary<int, string>(card.Descriptions)
        };

        if (card.Kind == MapCardKind.Boolean)
        {
            savedCard.BooleanValues = Enumerable.Range(0, card.Grid.Rows.Count)
                .Select(rowIndex => TryParseBooleanCell(card.Grid.Rows[rowIndex].Cells["Value"].Value))
                .ToList();
        }
        else
        {
            savedCard.RegisterValues = Enumerable.Range(0, card.Grid.Rows.Count)
                .Select(rowIndex =>
                {
                    var text = card.Grid.Rows[rowIndex].Cells["Value"].Value?.ToString() ?? "0";
                    return ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : (ushort)0;
                })
                .ToList();
        }

        return savedCard;
    }

    private void ApplyConfiguration(ServerSavedConfigEntry config)
    {
        if (server.IsRunning)
        {
            server.Stop();
            SetServerUiState(running: false);
        }

        if (!string.IsNullOrWhiteSpace(config.PortName) && portComboBox.Items.Contains(config.PortName))
        {
            portComboBox.SelectedItem = config.PortName;
        }

        var baudRateText = config.BaudRate.ToString(CultureInfo.InvariantCulture);
        if (baudRateComboBox.Items.Contains(baudRateText))
        {
            baudRateComboBox.SelectedItem = baudRateText;
        }

        if (!string.IsNullOrWhiteSpace(config.Frame) && frameComboBox.Items.Contains(config.Frame))
        {
            frameComboBox.SelectedItem = config.Frame;
        }

        unitIdTextBox.Text = config.UnitId.ToString(CultureInfo.InvariantCulture);
        rtsCheckBox.Checked = config.RtsEnable;
        dtrCheckBox.Checked = config.DtrEnable;

        foreach (var savedCard in config.Cards)
        {
            if (!Enum.TryParse<ModbusDataArea>(savedCard.Area, true, out var area))
            {
                continue;
            }

            var card = mapCards.FirstOrDefault(item => item.Area == area);
            if (card is null)
            {
                continue;
            }

            card.StartAddressTextBox.Text = savedCard.StartAddress.ToString(CultureInfo.InvariantCulture);
            card.CountTextBox.Text = Math.Max(1, (int)savedCard.Count).ToString(CultureInfo.InvariantCulture);
            card.ShowDescriptionColumn = savedCard.ShowDescriptionColumn;
            card.Descriptions.Clear();
            foreach (var pair in savedCard.Descriptions)
            {
                card.Descriptions[pair.Key] = pair.Value;
            }

            RebuildCardRows(card);
            ApplySavedCardValues(card, savedCard);
            ApplyDescriptionColumnVisibility(card);
            TryUpdateStoreFromCard(card);
        }
    }

    private static void ApplySavedCardValues(ServerMapCard card, ServerSavedCardConfig savedCard)
    {
        if (card.Kind == MapCardKind.Boolean)
        {
            for (var rowIndex = 0; rowIndex < card.Grid.Rows.Count && rowIndex < savedCard.BooleanValues.Count; rowIndex++)
            {
                card.Grid.Rows[rowIndex].Cells["Value"].Value = savedCard.BooleanValues[rowIndex];
            }

            return;
        }

        for (var rowIndex = 0; rowIndex < card.Grid.Rows.Count && rowIndex < savedCard.RegisterValues.Count; rowIndex++)
        {
            card.Grid.Rows[rowIndex].Cells["Value"].Value = savedCard.RegisterValues[rowIndex].ToString(CultureInfo.InvariantCulture);
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
        updateConfigButton.Enabled = hasSelection;
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
            var items = JsonSerializer.Deserialize<List<ServerSavedConfigEntry>>(json, JsonOptions) ?? new List<ServerSavedConfigEntry>();

            savedConfigurations.Clear();
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Name) || item.Cards.Count == 0)
                {
                    continue;
                }

                savedConfigurations[item.Name] = item;
            }

            if (savedConfigurations.Count > 0)
            {
                var first = savedConfigurations.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).First();
                RefreshConfigurationDropdown(first);
                if (savedConfigurations.TryGetValue(first, out var config))
                {
                    ApplyConfiguration(config);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[raw] [server-config-load-error] " + ex.Message);
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
                .Select(pair => pair.Value)
                .ToList();

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(savedConfigFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[raw] [server-config-save-error] " + ex.Message);
        }
    }

    private static string BuildSavedConfigFilePath()
    {
        return Path.Combine(Environment.CurrentDirectory, "modbus-rtu-server-configs.json");
    }

    private static bool TryParseBooleanCell(object? value)
    {
        return value switch
        {
            bool booleanValue => booleanValue,
            string stringValue when bool.TryParse(stringValue, out var parsed) => parsed,
            _ => false
        };
    }

    private enum MapCardKind
    {
        UInt16,
        Boolean
    }

    private sealed class ServerMapCard
    {
        public ServerMapCard(
            string name,
            ModbusDataArea area,
            MapCardKind kind,
            GroupBox group,
            DataGridView grid,
            TextBox startAddressTextBox,
            TextBox countTextBox)
        {
            Name = name;
            Area = area;
            Kind = kind;
            Group = group;
            Grid = grid;
            StartAddressTextBox = startAddressTextBox;
            CountTextBox = countTextBox;
        }

        public string Name { get; }

        public ModbusDataArea Area { get; }

        public MapCardKind Kind { get; }

        public GroupBox Group { get; }

        public DataGridView Grid { get; }

        public TextBox StartAddressTextBox { get; }

        public TextBox CountTextBox { get; }

        public bool ShowDescriptionColumn { get; set; }

        public Dictionary<int, string> Descriptions { get; } = new();
    }

    private sealed class ServerSavedConfigEntry
    {
        public string Name { get; set; } = string.Empty;

        public string PortName { get; set; } = string.Empty;

        public int BaudRate { get; set; } = 19200;

        public string Frame { get; set; } = "8N1";

        public byte UnitId { get; set; } = 1;

        public bool RtsEnable { get; set; }

        public bool DtrEnable { get; set; }

        public List<ServerSavedCardConfig> Cards { get; set; } = new();
    }

    private sealed class ServerSavedCardConfig
    {
        public string Area { get; set; } = string.Empty;

        public ushort StartAddress { get; set; }

        public ushort Count { get; set; }

        public bool ShowDescriptionColumn { get; set; }

        public Dictionary<int, string> Descriptions { get; set; } = new();

        public List<ushort> RegisterValues { get; set; } = new();

        public List<bool> BooleanValues { get; set; } = new();
    }
}
