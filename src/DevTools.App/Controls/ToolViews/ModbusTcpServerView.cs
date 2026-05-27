using DevTools.App.Libraries.Modbus;
using DevTools.App.Infrastructure.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Windows.Forms;

namespace DevTools.App.Controls.ToolViews;

internal sealed class ModbusTcpServerView : UserControl
{
    private readonly TableLayoutPanel rootLayout;
    private readonly GroupBox connectionGroup;
    private readonly TableLayoutPanel connectionLayout;
    private readonly TextBox bindAddressTextBox;
    private readonly TextBox portTextBox;
    private readonly TextBox unitIdTextBox;
    private readonly Button startServerButton;
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
    private readonly ModbusTcpServer server;
    private readonly System.Windows.Forms.Timer storeSyncTimer;
    private readonly List<TcpMapCard> mapCards = new();
    private readonly Dictionary<string, TcpSavedConfigEntry> savedConfigurations = new(StringComparer.OrdinalIgnoreCase);
    private readonly string savedConfigFilePath;

    private bool syncFromStoreInProgress;
    private bool applyingConfigurationPreset;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ModbusTcpServerView()
    {
        Dock = DockStyle.Fill;
        savedConfigFilePath = Path.Combine(Environment.CurrentDirectory, "modbus-tcp-server-configs.json");
        server = new ModbusTcpServer(dataStore);
        server.Log += static message => Console.WriteLine("[raw] " + message);

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
            Text = "TCP Server"
        };

        connectionLayout = new TableLayoutPanel
        {
            ColumnCount = 5,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            RowCount = 1
        };
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        bindAddressTextBox = new TextBox { Text = "0.0.0.0" };
        portTextBox = new TextBox { Text = "502" };
        unitIdTextBox = new TextBox { Text = "1" };

        var bindField = BuildLabeledField("Bind", bindAddressTextBox, 176);
        var portField = BuildLabeledField("Port", portTextBox, 72);
        var unitIdField = BuildLabeledField("ID", unitIdTextBox, 56);

        startServerButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Start"
        };
        startServerButton.Click += startServerButton_Click;

        connectionStatusLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            ForeColor = AppTheme.GetStatusColor(StatusTone.Error),
            Text = "Stopped"
        };

        connectionLayout.Controls.Add(bindField, 0, 0);
        connectionLayout.Controls.Add(portField, 1, 0);
        connectionLayout.Controls.Add(unitIdField, 2, 0);
        connectionLayout.Controls.Add(startServerButton, 3, 0);
        connectionLayout.Controls.Add(connectionStatusLabel, 4, 0);
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

        seedDataButton = new Button { Margin = new Padding(0, 3, 8, 3), Size = new Size(92, 27), Text = "Seed Data" };
        seedDataButton.Click += seedDataButton_Click;

        clearDataButton = new Button { Margin = new Padding(0, 3, 8, 3), Size = new Size(92, 27), Text = "Clear Data" };
        clearDataButton.Click += clearDataButton_Click;

        saveConfigButton = new Button { Margin = new Padding(0, 3, 8, 3), Size = new Size(92, 27), Text = "Save Config" };
        saveConfigButton.Click += saveConfigButton_Click;

        updateConfigButton = new Button { Enabled = false, Margin = new Padding(0, 3, 8, 3), Size = new Size(100, 27), Text = "Update Config" };
        updateConfigButton.Click += updateConfigButton_Click;

        configPresetComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            Margin = new Padding(0, 4, 0, 3),
            Size = new Size(130, 23)
        };
        configPresetComboBox.SelectedIndexChanged += configPresetComboBox_SelectedIndexChanged;

        renameConfigButton = new Button { Enabled = false, Margin = new Padding(8, 3, 6, 3), Size = new Size(66, 27), Text = "Rename" };
        renameConfigButton.Click += renameConfigButton_Click;

        deleteConfigButton = new Button { Enabled = false, Margin = new Padding(0, 3, 0, 3), Size = new Size(60, 27), Text = "Delete" };
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
        LoadConfigurationsFromDisk();
        UpdatePresetActionButtons();
        SetServerUiState(false);
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

        mapCards.Add(CreateCard("Holding Registers", "HoldingRegisters", MapCardKind.UInt16, 0, 16));
        mapCards.Add(CreateCard("Input Registers", "InputRegisters", MapCardKind.UInt16, 0, 16));
        mapCards.Add(CreateCard("Discrete Inputs", "DiscreteInputs", MapCardKind.Boolean, 0, 16));
        mapCards.Add(CreateCard("Coils", "Coils", MapCardKind.Boolean, 0, 16));

        foreach (var card in mapCards)
        {
            mapCardsHostPanel.Controls.Add(card.Group);
            RebuildCardRows(card);
        }

        ResizeMapCards();
    }

    private TcpMapCard CreateCard(string title, string areaKey, MapCardKind kind, int startAddress, int pointCount)
    {
        var group = new GroupBox { ForeColor = Color.FromArgb(51, 65, 85), Margin = new Padding(0, 0, 6, 0), Padding = new Padding(6), Text = title };
        var rootPanel = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, Margin = new Padding(0), RowCount = 2 };
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

        var startTextBox = new TextBox { Text = startAddress.ToString(CultureInfo.InvariantCulture) };
        var countTextBox = new TextBox { Text = pointCount.ToString(CultureInfo.InvariantCulture) };
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

        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Address", Name = "Address", ReadOnly = true, Width = 82 });
        if (kind == MapCardKind.Boolean)
        {
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Value", Name = "Value", Width = 68 });
        }
        else
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", Name = "Value", Width = 80 });
        }
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Description",
            Name = "Description",
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 120,
            Visible = false
        });

        rootPanel.Controls.Add(headerPanel, 0, 0);
        rootPanel.Controls.Add(grid, 0, 1);
        group.Controls.Add(rootPanel);

        var card = new TcpMapCard(areaKey, kind, group, grid, startTextBox, countTextBox);

        var editDescription = new ToolStripMenuItem("Add/Edit Description");
        editDescription.Click += (_, _) => EditSelectedRowDescription(card);
        var clearDescription = new ToolStripMenuItem("Clear Description");
        clearDescription.Click += (_, _) => ClearSelectedRowDescription(card);
        var showDescription = new ToolStripMenuItem("Show Description Column") { CheckOnClick = true, Checked = false };
        showDescription.CheckedChanged += (_, _) =>
        {
            card.ShowDescriptionColumn = showDescription.Checked;
            ApplyDescriptionColumnVisibility(card);
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(editDescription);
        menu.Items.Add(clearDescription);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(showDescription);

        group.ContextMenuStrip = menu;
        rootPanel.ContextMenuStrip = menu;
        headerPanel.ContextMenuStrip = menu;
        grid.ContextMenuStrip = menu;

        applyButton.Click += (_, _) => RebuildCardRows(card);
        grid.CellMouseDown += (_, args) =>
        {
            if (args.Button == MouseButtons.Right && args.RowIndex >= 0)
            {
                grid.ClearSelection();
                grid.Rows[args.RowIndex].Selected = true;
                if (args.ColumnIndex >= 0)
                {
                    grid.CurrentCell = grid.Rows[args.RowIndex].Cells[args.ColumnIndex];
                }
            }
        };
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        grid.CellValueChanged += (_, args) =>
        {
            if (args.RowIndex >= 0 && args.ColumnIndex >= 0 &&
                string.Equals(grid.Columns[args.ColumnIndex].Name, "Value", StringComparison.Ordinal))
            {
                TryUpdateStoreFromCard(card);
            }
        };
        grid.DataError += (_, _) => { };

        return card;
    }

    private void RebuildCardRows(TcpMapCard card)
    {
        if (!int.TryParse(card.StartAddressTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startAddress) || startAddress < 0)
        {
            startAddress = 0;
            card.StartAddressTextBox.Text = "0";
        }

        if (!int.TryParse(card.CountTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 1)
        {
            count = 1;
            card.CountTextBox.Text = "1";
        }

        startAddress = Math.Min(ushort.MaxValue, startAddress);
        count = Math.Min(250, count);
        count = Math.Min(count, (ushort.MaxValue - startAddress) + 1);
        card.StartAddressTextBox.Text = startAddress.ToString(CultureInfo.InvariantCulture);
        card.CountTextBox.Text = count.ToString(CultureInfo.InvariantCulture);

        card.Grid.Rows.Clear();
        for (var i = 0; i < count; i++)
        {
            var address = startAddress + i;
            var rowIndex = card.Grid.Rows.Add();
            card.Grid.Rows[rowIndex].Cells["Address"].Value = address.ToString(CultureInfo.InvariantCulture);
            card.Grid.Rows[rowIndex].Cells["Value"].Value = card.Kind == MapCardKind.Boolean ? false : "0";
            card.Grid.Rows[rowIndex].Cells["Description"].Value = card.Descriptions.TryGetValue(address, out var text) ? text : string.Empty;
        }

        ApplyDescriptionColumnVisibility(card);
        card.Grid.ClearSelection();
        TryUpdateStoreFromCard(card);
    }

    private void seedDataButton_Click(object? sender, EventArgs e)
    {
        foreach (var card in mapCards)
        {
            for (var i = 0; i < card.Grid.Rows.Count; i++)
            {
                card.Grid.Rows[i].Cells["Value"].Value = card.Kind == MapCardKind.Boolean
                    ? i % 2 == 0
                    : ((i + 1) * 10).ToString(CultureInfo.InvariantCulture);
            }

            TryUpdateStoreFromCard(card);
        }
    }

    private void clearDataButton_Click(object? sender, EventArgs e)
    {
        foreach (var card in mapCards)
        {
            for (var i = 0; i < card.Grid.Rows.Count; i++)
            {
                card.Grid.Rows[i].Cells["Value"].Value = card.Kind == MapCardKind.Boolean ? false : "0";
            }

            TryUpdateStoreFromCard(card);
        }
    }

    private void startServerButton_Click(object? sender, EventArgs e)
    {
        if (server.IsRunning)
        {
            server.Stop();
            SetServerUiState(false);
            return;
        }

        var bindAddressText = bindAddressTextBox.Text.Trim();
        if (!TryParseBindAddress(bindAddressText, out var bindAddress))
        {
            MessageBox.Show(this, "Bind address must be a valid IPv4/IPv6 address.", "TCP Server", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!int.TryParse(portTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show(this, "Port must be between 1 and 65535.", "TCP Server", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!byte.TryParse(unitIdTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitId) || unitId is < 1 or > 247)
        {
            MessageBox.Show(this, "Unit ID must be between 1 and 247.", "TCP Server", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            server.Start(bindAddress, port, unitId);
            SetServerUiState(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Unable to start TCP server: " + ex.Message, "TCP Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetServerUiState(false);
        }
    }

    private void SetServerUiState(bool running)
    {
        startServerButton.Text = running ? "Stop" : "Start";
        connectionStatusLabel.Text = running ? "Running" : "Stopped";
        connectionStatusLabel.ForeColor = running
            ? AppTheme.GetStatusColor(StatusTone.Success)
            : AppTheme.GetStatusColor(StatusTone.Error);
        bindAddressTextBox.Enabled = !running;
        portTextBox.Enabled = !running;
        unitIdTextBox.Enabled = !running;
    }

    private static Control BuildLabeledField(string labelText, TextBox textBox, int textWidth)
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

        textBox.Margin = new Padding(0, 2, 0, 0);
        textBox.Width = textWidth;

        panel.Controls.Add(label);
        panel.Controls.Add(textBox);
        return panel;
    }

    private static bool TryParseBindAddress(string input, out IPAddress address)
    {
        if (string.IsNullOrWhiteSpace(input) || input == "*")
        {
            address = IPAddress.Any;
            return true;
        }

        return IPAddress.TryParse(input, out address!);
    }

    private void TryUpdateStoreFromCard(TcpMapCard card)
    {
        if (syncFromStoreInProgress)
        {
            return;
        }

        if (!TryGetCardAddressing(card, out var area, out var startAddress, out _))
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

                dataStore.ReplaceBooleanArea(area, startAddress, values);
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

            dataStore.ReplaceRegisterArea(area, startAddress, registerValues);
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
                if (!TryGetCardAddressing(card, out var area, out var startAddress, out var count) || count == 0)
                {
                    continue;
                }

                if (card.Kind == MapCardKind.Boolean)
                {
                    var values = dataStore.ReadBooleans(area, startAddress, count);
                    for (var rowIndex = 0; rowIndex < values.Length && rowIndex < card.Grid.Rows.Count; rowIndex++)
                    {
                        card.Grid.Rows[rowIndex].Cells["Value"].Value = values[rowIndex];
                    }
                }
                else
                {
                    var values = dataStore.ReadRegisters(area, startAddress, count);
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

    private static bool TryParseBooleanCell(object? value)
    {
        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt) => parsedInt != 0,
            int i => i != 0,
            _ => false
        };
    }

    private static bool TryGetCardAddressing(TcpMapCard card, out ModbusDataArea area, out ushort startAddress, out ushort count)
    {
        startAddress = 0;
        count = 0;
        area = GetAreaFromKey(card.AreaKey);
        return ushort.TryParse(card.StartAddressTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out startAddress) &&
               ushort.TryParse(card.CountTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
    }

    private static ModbusDataArea GetAreaFromKey(string areaKey)
    {
        return areaKey switch
        {
            "HoldingRegisters" => ModbusDataArea.HoldingRegisters,
            "InputRegisters" => ModbusDataArea.InputRegisters,
            "DiscreteInputs" => ModbusDataArea.DiscreteInputs,
            "Coils" => ModbusDataArea.Coils,
            _ => throw new InvalidOperationException("Unknown map area key: " + areaKey)
        };
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

    private void EditSelectedRowDescription(TcpMapCard card)
    {
        var rowIndex = card.Grid.CurrentCell?.RowIndex ?? -1;
        if (rowIndex < 0)
        {
            return;
        }

        var address = GetRowAddress(card, rowIndex);
        if (address < 0)
        {
            return;
        }

        card.Descriptions.TryGetValue(address, out var existing);
        var text = PromptForDescription(address, existing ?? string.Empty);
        if (text is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            card.Descriptions.Remove(address);
            card.Grid.Rows[rowIndex].Cells["Description"].Value = string.Empty;
        }
        else
        {
            var normalized = text.Trim();
            card.Descriptions[address] = normalized;
            card.Grid.Rows[rowIndex].Cells["Description"].Value = normalized;
        }
    }

    private void ClearSelectedRowDescription(TcpMapCard card)
    {
        var rowIndex = card.Grid.CurrentCell?.RowIndex ?? -1;
        if (rowIndex < 0)
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

    private static int GetRowAddress(TcpMapCard card, int rowIndex)
    {
        var text = card.Grid.Rows[rowIndex].Cells["Address"].Value?.ToString();
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var address) ? address : -1;
    }

    private void ApplyDescriptionColumnVisibility(TcpMapCard card)
    {
        card.Grid.Columns["Description"].Visible = card.ShowDescriptionColumn;
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

        var textBox = new TextBox { Left = 12, Top = 34, Width = 380, Text = initialValue };
        var saveButton = new Button { Text = "Save", Left = 236, Width = 74, Top = 72, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", Left = 318, Width = 74, Top = 72, DialogResult = DialogResult.Cancel };

        prompt.Controls.Add(label);
        prompt.Controls.Add(textBox);
        prompt.Controls.Add(saveButton);
        prompt.Controls.Add(cancelButton);
        prompt.AcceptButton = saveButton;
        prompt.CancelButton = cancelButton;

        return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : null;
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
            var overwrite = MessageBox.Show(this, "A configuration with that name already exists. Overwrite it?", "Save Configuration", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (overwrite != DialogResult.Yes)
            {
                return;
            }
        }

        savedConfigurations[name] = CaptureCurrentConfiguration(name);
        RefreshConfigurationDropdown(name);
        SaveConfigurationsToDisk();
    }

    private void updateConfigButton_Click(object? sender, EventArgs e)
    {
        var selectedName = GetSelectedPresetName();
        if (string.IsNullOrWhiteSpace(selectedName) || !savedConfigurations.ContainsKey(selectedName))
        {
            MessageBox.Show(this, "Select a configuration preset first.", "Update Configuration", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            var overwrite = MessageBox.Show(this, "A configuration with that name already exists. Overwrite it?", "Rename Configuration", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
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

        var confirm = MessageBox.Show(this, "Delete configuration '" + currentName + "'?", "Delete Configuration", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
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

    private TcpSavedConfigEntry CaptureCurrentConfiguration(string name)
    {
        return new TcpSavedConfigEntry
        {
            Name = name,
            BindAddress = bindAddressTextBox.Text.Trim(),
            Port = int.TryParse(portTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? port : 502,
            UnitId = byte.TryParse(unitIdTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitId) ? unitId : (byte)1,
            Cards = mapCards.Select(CreateSavedCardConfig).ToList()
        };
    }

    private static TcpSavedCardConfig CreateSavedCardConfig(TcpMapCard card)
    {
        var start = ushort.TryParse(card.StartAddressTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startAddress) ? startAddress : (ushort)0;
        var count = ushort.TryParse(card.CountTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pointCount) ? pointCount : (ushort)0;

        var savedCard = new TcpSavedCardConfig
        {
            AreaKey = card.AreaKey,
            StartAddress = start,
            Count = count,
            ShowDescriptionColumn = card.ShowDescriptionColumn,
            Descriptions = new Dictionary<int, string>(card.Descriptions)
        };

        if (card.Kind == MapCardKind.Boolean)
        {
            savedCard.BooleanValues = Enumerable.Range(0, card.Grid.Rows.Count)
                .Select(rowIndex => card.Grid.Rows[rowIndex].Cells["Value"].Value is true)
                .ToList();
        }
        else
        {
            savedCard.RegisterValues = Enumerable.Range(0, card.Grid.Rows.Count)
                .Select(rowIndex =>
                {
                    var text = card.Grid.Rows[rowIndex].Cells["Value"].Value?.ToString() ?? "0";
                    return ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : (ushort)0;
                })
                .ToList();
        }

        return savedCard;
    }

    private void ApplyConfiguration(TcpSavedConfigEntry config)
    {
        bindAddressTextBox.Text = string.IsNullOrWhiteSpace(config.BindAddress) ? "0.0.0.0" : config.BindAddress;
        portTextBox.Text = config.Port.ToString(CultureInfo.InvariantCulture);
        unitIdTextBox.Text = config.UnitId.ToString(CultureInfo.InvariantCulture);

        foreach (var savedCard in config.Cards)
        {
            var card = mapCards.FirstOrDefault(item => string.Equals(item.AreaKey, savedCard.AreaKey, StringComparison.OrdinalIgnoreCase));
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
            if (card.Kind == MapCardKind.Boolean)
            {
                for (var i = 0; i < card.Grid.Rows.Count && i < savedCard.BooleanValues.Count; i++)
                {
                    card.Grid.Rows[i].Cells["Value"].Value = savedCard.BooleanValues[i];
                }
            }
            else
            {
                for (var i = 0; i < card.Grid.Rows.Count && i < savedCard.RegisterValues.Count; i++)
                {
                    card.Grid.Rows[i].Cells["Value"].Value = savedCard.RegisterValues[i].ToString(CultureInfo.InvariantCulture);
                }
            }

            ApplyDescriptionColumnVisibility(card);
            TryUpdateStoreFromCard(card);
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

        var nameLabel = new Label { Left = 12, Top = 12, Width = 320, Text = "Configuration name" };
        var nameTextBox = new TextBox { Left = 12, Top = 34, Width = 320, Text = initialName ?? string.Empty };
        var saveButton = new Button { Text = "Save", Left = 176, Width = 74, Top = 68, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", Left = 258, Width = 74, Top = 68, DialogResult = DialogResult.Cancel };

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
            var items = JsonSerializer.Deserialize<List<TcpSavedConfigEntry>>(json, JsonOptions) ?? new List<TcpSavedConfigEntry>();

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
            Console.WriteLine("[raw] [tcp-server-config-load-error] " + ex.Message);
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
            Console.WriteLine("[raw] [tcp-server-config-save-error] " + ex.Message);
        }
    }

    private enum MapCardKind
    {
        UInt16,
        Boolean
    }

    private sealed class TcpMapCard
    {
        public TcpMapCard(string areaKey, MapCardKind kind, GroupBox group, DataGridView grid, TextBox startAddressTextBox, TextBox countTextBox)
        {
            AreaKey = areaKey;
            Kind = kind;
            Group = group;
            Grid = grid;
            StartAddressTextBox = startAddressTextBox;
            CountTextBox = countTextBox;
        }

        public string AreaKey { get; }

        public MapCardKind Kind { get; }

        public GroupBox Group { get; }

        public DataGridView Grid { get; }

        public TextBox StartAddressTextBox { get; }

        public TextBox CountTextBox { get; }

        public bool ShowDescriptionColumn { get; set; }

        public Dictionary<int, string> Descriptions { get; } = new();
    }

    private sealed class TcpSavedConfigEntry
    {
        public string Name { get; set; } = string.Empty;

        public string BindAddress { get; set; } = "0.0.0.0";

        public int Port { get; set; } = 502;

        public byte UnitId { get; set; } = 1;

        public List<TcpSavedCardConfig> Cards { get; set; } = new();
    }

    private sealed class TcpSavedCardConfig
    {
        public string AreaKey { get; set; } = string.Empty;

        public ushort StartAddress { get; set; }

        public ushort Count { get; set; }

        public bool ShowDescriptionColumn { get; set; }

        public Dictionary<int, string> Descriptions { get; set; } = new();

        public List<ushort> RegisterValues { get; set; } = new();

        public List<bool> BooleanValues { get; set; } = new();
    }
}
