using DevTools.App.Libraries.Modbus;
using DevTools.App.Infrastructure.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DevTools.App.Controls.ToolViews;

internal sealed class ModbusTcpClientView : UserControl
{
    private readonly TableLayoutPanel rootLayout;
    private readonly GroupBox connectionGroup;
    private readonly TableLayoutPanel connectionLayout;
    private readonly TextBox hostTextBox;
    private readonly TextBox portTextBox;
    private readonly TextBox pollIntervalTextBox;
    private readonly Button connectButton;
    private readonly Label connectionStatusLabel;

    private readonly GroupBox requestGroup;
    private readonly TableLayoutPanel requestLayout;
    private readonly FlowLayoutPanel requestToolbar;
    private readonly Button addSlaveButton;
    private readonly Button pollButton;
    private readonly Button saveConfigButton;
    private readonly Button updateConfigButton;
    private readonly ComboBox configPresetComboBox;
    private readonly Button renameConfigButton;
    private readonly Button deleteConfigButton;
    private readonly ThemedHorizontalScrollFlowPanel requestCardsHostPanel;

    private readonly ModbusTcpClient tcpClient = new();
    private readonly System.Windows.Forms.Timer pollTimer;
    private readonly List<TcpRequestCard> requestCards = new();
    private readonly Dictionary<string, TcpClientSavedConfigEntry> savedConfigurations = new(StringComparer.OrdinalIgnoreCase);
    private readonly string savedConfigFilePath;

    private bool pollingInProgress;
    private bool pollingEnabled;
    private bool applyingConfigurationPreset;
    private int nextSlaveId;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ModbusTcpClientView()
    {
        Dock = DockStyle.Fill;
        savedConfigFilePath = Path.Combine(Environment.CurrentDirectory, "modbus-tcp-client-configs.json");
        pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        pollTimer.Tick += async (_, _) => await PollOnceAsync();

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
            Text = "TCP Client"
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
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        hostTextBox = new TextBox { Text = "127.0.0.1" };
        portTextBox = new TextBox { Text = "502" };
        pollIntervalTextBox = new TextBox { Text = "1000" };

        var hostField = BuildLabeledField("Host", hostTextBox, 176);
        var portField = BuildLabeledField("Port", portTextBox, 72);
        var pollField = BuildLabeledField("Poll", pollIntervalTextBox, 72);

        connectButton = new Button { Dock = DockStyle.Fill, Text = "Connect" };
        connectButton.Click += connectButton_Click;

        connectionStatusLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            ForeColor = AppTheme.GetStatusColor(StatusTone.Error),
            Text = "Disconnected"
        };

        connectionLayout.Controls.Add(hostField, 0, 0);
        connectionLayout.Controls.Add(portField, 1, 0);
        connectionLayout.Controls.Add(pollField, 2, 0);
        connectionLayout.Controls.Add(connectButton, 3, 0);
        connectionLayout.Controls.Add(connectionStatusLabel, 4, 0);
        connectionGroup.Controls.Add(connectionLayout);

        requestGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 8, 10, 8),
            Text = "Read Data"
        };

        requestLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        requestLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        requestLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        requestLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        requestToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            WrapContents = false
        };

        addSlaveButton = new Button { Margin = new Padding(0, 3, 8, 3), Size = new Size(92, 27), Text = "Add Slave" };
        addSlaveButton.Click += addSlaveButton_Click;

        pollButton = new Button { Margin = new Padding(0, 3, 8, 3), Size = new Size(92, 27), Text = "Start Poll" };
        pollButton.Click += pollButton_Click;

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

        requestToolbar.Controls.Add(addSlaveButton);
        requestToolbar.Controls.Add(pollButton);
        requestToolbar.Controls.Add(saveConfigButton);
        requestToolbar.Controls.Add(updateConfigButton);
        requestToolbar.Controls.Add(configPresetComboBox);
        requestToolbar.Controls.Add(renameConfigButton);
        requestToolbar.Controls.Add(deleteConfigButton);

        requestCardsHostPanel = new ThemedHorizontalScrollFlowPanel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            WrapContents = false
        };
        requestCardsHostPanel.SizeChanged += (_, _) => ResizeRequestCards();

        requestLayout.Controls.Add(requestToolbar, 0, 0);
        requestLayout.Controls.Add(requestCardsHostPanel, 0, 1);
        requestGroup.Controls.Add(requestLayout);

        rootLayout.Controls.Add(connectionGroup, 0, 0);
        rootLayout.Controls.Add(requestGroup, 0, 1);
        Controls.Add(rootLayout);

        InitializeRequestCards();
        LoadConfigurationsFromDisk();
        UpdatePresetActionButtons();
        SetClientUiState(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pollTimer.Stop();
            pollTimer.Dispose();
            tcpClient.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeRequestCards()
    {
        requestCards.Clear();
        requestCardsHostPanel.Controls.Clear();

        nextSlaveId = 0;
        AddSlaveCard(CreateDefaultCard());

        ResizeRequestCards();
    }

    private TcpRequestCard CreateDefaultCard()
    {
        return CreateCard(1, 0x03, 0, 10);
    }

    private void addSlaveButton_Click(object? sender, EventArgs e)
    {
        var nextId = Math.Min(247, Math.Max(nextSlaveId + 1, 1));
        AddSlaveCard(CreateCard((byte)nextId, 0x03, 0, 10));
        ResizeRequestCards();
    }

    private void AddSlaveCard(TcpRequestCard card)
    {
        requestCards.Add(card);
        requestCardsHostPanel.Controls.Add(card.Group);
        nextSlaveId = Math.Max(nextSlaveId, card.GetUnitId());
        RebuildCardRows(card);

        UpdateCardTitle(card);
    }

    private TcpRequestCard CreateCard(byte unitId, byte functionCode, int startAddress, int pointCount)
    {
        var cardShell = ModbusClientSlaveCardFactory.Create(functionCode, unitId, startAddress, pointCount, new Padding(0));
        var group = cardShell.Group;
        var rootPanel = cardShell.RootPanel;
        var headerHost = cardShell.HeaderHost;
        var headerPanel = cardShell.HeaderPanel;
        var statusLabel = cardShell.StatusLabel;
        var functionInput = cardShell.FunctionComboBox;
        var unitIdTextBox = cardShell.UnitIdTextBox;
        var startTextBox = cardShell.StartTextBox;
        var countTextBox = cardShell.CountTextBox;

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
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", Name = "Value", ReadOnly = true, Width = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Description",
            Name = "Description",
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 120,
            Visible = false
        });

        cardShell.ContentHost.Controls.Add(grid);

        var card = new TcpRequestCard(group, grid, functionInput, unitIdTextBox, startTextBox, countTextBox, statusLabel);

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
        menu.Items.Add(new ToolStripSeparator());
        var closeMenu = new ToolStripMenuItem("Close");
        closeMenu.Click += (_, _) => RemoveCard(card);
        menu.Items.Add(closeMenu);

        group.ContextMenuStrip = menu;
        rootPanel.ContextMenuStrip = menu;
        headerHost.ContextMenuStrip = menu;
        headerPanel.ContextMenuStrip = menu;
        cardShell.ContentHost.ContextMenuStrip = menu;
        grid.ContextMenuStrip = menu;

        unitIdTextBox.TextChanged += (_, _) => UpdateCardTitle(card);
        startTextBox.Leave += (_, _) => RebuildCardRows(card);
        countTextBox.Leave += (_, _) => RebuildCardRows(card);
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
        grid.DataError += (_, _) => { };

        AppTheme.Apply(group);

        return card;
    }

    private void RebuildCardRows(TcpRequestCard card)
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
            card.Grid.Rows[rowIndex].Cells["Value"].Value = "-";
            card.Grid.Rows[rowIndex].Cells["Description"].Value = card.Descriptions.TryGetValue(address, out var text) ? text : string.Empty;
        }

        ApplyDescriptionColumnVisibility(card);
        card.Grid.ClearSelection();
    }

    private async void pollButton_Click(object? sender, EventArgs e)
    {
        if (!tcpClient.IsConnected)
        {
            MessageBox.Show(this, "Connect to a TCP endpoint first.", "TCP Client", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (pollingEnabled)
        {
            pollingEnabled = false;
            pollTimer.Stop();
            pollButton.Text = "Start Poll";
            return;
        }

        if (!int.TryParse(pollIntervalTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pollMs) || pollMs < 50)
        {
            MessageBox.Show(this, "Poll interval must be at least 50 ms.", "TCP Client", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        pollTimer.Interval = Math.Max(50, pollMs);
        pollingEnabled = true;
        pollButton.Text = "Stop Poll";
        pollTimer.Start();
        await PollOnceAsync();
    }

    private async void connectButton_Click(object? sender, EventArgs e)
    {
        if (tcpClient.IsConnected)
        {
            pollTimer.Stop();
            pollingEnabled = false;
            tcpClient.Disconnect();
            SetClientUiState(false);
            return;
        }

        var host = hostTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            MessageBox.Show(this, "Host is required.", "TCP Client", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!int.TryParse(portTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show(this, "Port must be between 1 and 65535.", "TCP Client", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!int.TryParse(pollIntervalTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pollMs) || pollMs < 50)
        {
            MessageBox.Show(this, "Poll interval must be at least 50 ms.", "TCP Client", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        connectButton.Enabled = false;
        try
        {
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await tcpClient.ConnectAsync(host, port, connectCts.Token);
            SetClientUiState(true);
            pollTimer.Interval = Math.Max(50, pollMs);
        }
        catch (Exception ex)
        {
            tcpClient.Disconnect();
            SetClientUiState(false);
            MessageBox.Show(this, "Unable to connect: " + ex.Message, "TCP Client", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            connectButton.Enabled = true;
        }
    }

    private void SetClientUiState(bool isConnected)
    {
        connectButton.Text = isConnected ? "Disconnect" : "Connect";
        connectionStatusLabel.Text = isConnected ? "Connected" : "Disconnected";
        connectionStatusLabel.ForeColor = isConnected
            ? AppTheme.GetStatusColor(StatusTone.Success)
            : AppTheme.GetStatusColor(StatusTone.Error);

        hostTextBox.Enabled = !isConnected;
        portTextBox.Enabled = !isConnected;
        pollIntervalTextBox.Enabled = !isConnected;
        pollButton.Enabled = isConnected;
        pollButton.Text = pollingEnabled ? "Stop Poll" : "Start Poll";
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

    private static bool IsBooleanFunction(byte functionCode)
    {
        return functionCode is 0x01 or 0x02;
    }

    private void RemoveCard(TcpRequestCard card)
    {
        if (requestCards.Count <= 1)
        {
            MessageBox.Show(this, "At least one slave card must remain.", "TCP Client", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        requestCards.Remove(card);
        requestCardsHostPanel.Controls.Remove(card.Group);
        card.Group.Dispose();
        ResizeRequestCards();
    }

    private void UpdateCardTitle(TcpRequestCard card)
    {
        card.Group.Text = "Slave " + card.GetUnitId().ToString(CultureInfo.InvariantCulture);
    }

    private void LoadRequestCards(IEnumerable<TcpClientSavedCardConfig> savedCards)
    {
        requestCards.Clear();
        requestCardsHostPanel.Controls.Clear();
        nextSlaveId = 0;

        foreach (var savedCard in savedCards)
        {
            var functionCode = savedCard.FunctionCode != 0
                ? savedCard.FunctionCode
                : GetLegacyFunctionCode(savedCard.AreaKey);
            var slaveId = savedCard.SlaveId is >= 1 and <= 247 ? savedCard.SlaveId : (byte)1;
            var count = Math.Max(1, (int)savedCard.Count);

            var card = CreateCard(slaveId, functionCode, savedCard.StartAddress, count);
            card.ShowDescriptionColumn = savedCard.ShowDescriptionColumn;
            foreach (var pair in savedCard.Descriptions)
            {
                card.Descriptions[pair.Key] = pair.Value;
            }

            AddSlaveCard(card);
            ApplyDescriptionColumnVisibility(card);
        }

        if (requestCards.Count == 0)
        {
            AddSlaveCard(CreateDefaultCard());
        }

        ResizeRequestCards();
    }

    private static byte GetLegacyFunctionCode(string areaKey)
    {
        return areaKey switch
        {
            "InputRegisters" => 0x04,
            "DiscreteInputs" => 0x02,
            "Coils" => 0x01,
            _ => 0x03
        };
    }

    private async Task PollOnceAsync()
    {
        if (!tcpClient.IsConnected || pollingInProgress)
        {
            return;
        }

        pollingInProgress = true;
        try
        {
            foreach (var card in requestCards)
            {
                if (!TryGetCardReadRequest(card, out var unitId, out var functionCode, out var startAddress, out var count) || count == 0)
                {
                    card.ErrorCount++;
                    card.StatusLabel.Text = "Reads: " + card.ReadCount.ToString(CultureInfo.InvariantCulture) + "  Err: " + card.ErrorCount.ToString(CultureInfo.InvariantCulture);
                    continue;
                }

                var values = await tcpClient.ReadAsync(unitId, functionCode, startAddress, count, CancellationToken.None);
                ApplyValuesToCard(card, values);
                card.ReadCount++;
                card.StatusLabel.Text = "Reads: " + card.ReadCount.ToString(CultureInfo.InvariantCulture) + "  Err: " + card.ErrorCount.ToString(CultureInfo.InvariantCulture);
            }

            connectionStatusLabel.Text = "Polled " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            connectionStatusLabel.ForeColor = AppTheme.GetStatusColor(StatusTone.Success);
        }
        catch (Exception ex)
        {
            connectionStatusLabel.Text = "Poll failed";
            connectionStatusLabel.ForeColor = AppTheme.GetStatusColor(StatusTone.Error);
            Console.WriteLine("[raw] [tcp-client-poll-error] " + ex.Message);
        }
        finally
        {
            pollingInProgress = false;
        }
    }

    private static bool TryGetCardReadRequest(TcpRequestCard card, out byte unitId, out byte functionCode, out ushort startAddress, out ushort count)
    {
        unitId = 0;
        functionCode = 0;
        startAddress = 0;
        count = 0;

        return byte.TryParse(card.UnitIdTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out unitId)
            && unitId is >= 1 and <= 247
            && ushort.TryParse(card.StartAddressTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out startAddress)
            && ushort.TryParse(card.CountTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out count)
            && count > 0
            && ModbusClientCardHeaderFactory.TryGetSelectedFunctionCode(card.FunctionComboBox, out functionCode);
    }

    private static void ApplyValuesToCard(TcpRequestCard card, IReadOnlyList<ushort> values)
    {
        for (var rowIndex = 0; rowIndex < card.Grid.Rows.Count && rowIndex < values.Count; rowIndex++)
        {
            card.Grid.Rows[rowIndex].Cells["Value"].Value = IsBooleanFunction(card.GetFunctionCode())
                ? (values[rowIndex] != 0 ? "1" : "0")
                : values[rowIndex].ToString(CultureInfo.InvariantCulture);
        }
    }

    private void ResizeRequestCards()
    {
        var width = 220;
        var availableHeight = requestCardsHostPanel.DisplayRectangle.Height - 2;
        var height = Math.Max(120, availableHeight);

        for (var index = 0; index < requestCards.Count; index++)
        {
            var card = requestCards[index];
            card.Group.Width = width;
            card.Group.Height = height;
            card.Group.Margin = index == requestCards.Count - 1
                ? new Padding(0)
                : new Padding(0, 0, 6, 0);
        }
    }

    private void EditSelectedRowDescription(TcpRequestCard card)
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

    private void ClearSelectedRowDescription(TcpRequestCard card)
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

    private static int GetRowAddress(TcpRequestCard card, int rowIndex)
    {
        var text = card.Grid.Rows[rowIndex].Cells["Address"].Value?.ToString();
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var address) ? address : -1;
    }

    private void ApplyDescriptionColumnVisibility(TcpRequestCard card)
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

    private TcpClientSavedConfigEntry CaptureCurrentConfiguration(string name)
    {
        return new TcpClientSavedConfigEntry
        {
            Name = name,
            Host = hostTextBox.Text.Trim(),
            Port = int.TryParse(portTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? port : 502,
            UnitId = requestCards.FirstOrDefault()?.GetUnitId() ?? (byte)1,
            PollIntervalMs = int.TryParse(pollIntervalTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pollMs) ? pollMs : 1000,
            Cards = requestCards.Select(CreateSavedCardConfig).ToList()
        };
    }

    private static TcpClientSavedCardConfig CreateSavedCardConfig(TcpRequestCard card)
    {
        var start = ushort.TryParse(card.StartAddressTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startAddress) ? startAddress : (ushort)0;
        var count = ushort.TryParse(card.CountTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pointCount) ? pointCount : (ushort)0;

        var savedCard = new TcpClientSavedCardConfig
        {
            SlaveId = card.GetUnitId(),
            FunctionCode = card.GetFunctionCode(),
            StartAddress = start,
            Count = count,
            ShowDescriptionColumn = card.ShowDescriptionColumn,
            Descriptions = new Dictionary<int, string>(card.Descriptions)
        };

        return savedCard;
    }

    private void ApplyConfiguration(TcpClientSavedConfigEntry config)
    {
        hostTextBox.Text = string.IsNullOrWhiteSpace(config.Host) ? "127.0.0.1" : config.Host;
        portTextBox.Text = config.Port.ToString(CultureInfo.InvariantCulture);
        pollIntervalTextBox.Text = Math.Max(50, config.PollIntervalMs).ToString(CultureInfo.InvariantCulture);

        LoadRequestCards(config.Cards);
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
            var items = JsonSerializer.Deserialize<List<TcpClientSavedConfigEntry>>(json, JsonOptions) ?? new List<TcpClientSavedConfigEntry>();

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
            Console.WriteLine("[raw] [tcp-client-config-load-error] " + ex.Message);
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
            Console.WriteLine("[raw] [tcp-client-config-save-error] " + ex.Message);
        }
    }

    private sealed class TcpRequestCard
    {
        public TcpRequestCard(GroupBox group, DataGridView grid, ComboBox functionComboBox, TextBox unitIdTextBox, TextBox startAddressTextBox, TextBox countTextBox, Label statusLabel)
        {
            Group = group;
            Grid = grid;
            FunctionComboBox = functionComboBox;
            UnitIdTextBox = unitIdTextBox;
            StartAddressTextBox = startAddressTextBox;
            CountTextBox = countTextBox;
            StatusLabel = statusLabel;
        }

        public GroupBox Group { get; }

        public DataGridView Grid { get; }

        public ComboBox FunctionComboBox { get; }

        public TextBox UnitIdTextBox { get; }

        public TextBox StartAddressTextBox { get; }

        public TextBox CountTextBox { get; }

        public Label StatusLabel { get; }

        public int ReadCount { get; set; }

        public int ErrorCount { get; set; }

        public bool ShowDescriptionColumn { get; set; }

        public Dictionary<int, string> Descriptions { get; } = new();

        public byte GetUnitId()
        {
            return byte.TryParse(UnitIdTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitId) && unitId is >= 1 and <= 247
                ? unitId
                : (byte)1;
        }

        public byte GetFunctionCode()
        {
            return ModbusClientCardHeaderFactory.TryGetSelectedFunctionCode(FunctionComboBox, out var functionCode)
                ? functionCode
                : (byte)0x03;
        }
    }

    private sealed class TcpClientSavedConfigEntry
    {
        public string Name { get; set; } = string.Empty;

        public string Host { get; set; } = "127.0.0.1";

        public int Port { get; set; } = 502;

        public byte UnitId { get; set; } = 1;

        public int PollIntervalMs { get; set; } = 1000;

        public List<TcpClientSavedCardConfig> Cards { get; set; } = new();
    }

    private sealed class TcpClientSavedCardConfig
    {
        public string AreaKey { get; set; } = string.Empty;

        public byte SlaveId { get; set; } = 1;

        public byte FunctionCode { get; set; } = 0x03;

        public ushort StartAddress { get; set; }

        public ushort Count { get; set; }

        public bool ShowDescriptionColumn { get; set; }

        public Dictionary<int, string> Descriptions { get; set; } = new();
    }
}
