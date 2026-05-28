using System.Drawing;
using System.Windows.Forms;
using DevTools.App.Infrastructure.UI;

namespace DevTools.App.Controls.ToolViews;

internal sealed partial class ModbusRtuClientView
{
    private TableLayoutPanel rootLayout = null!;
    private GroupBox connectionGroup = null!;
    private TableLayoutPanel connectionLayout = null!;
    private Label portLabel = null!;
    private ComboBox portComboBox = null!;
    private Label baudRateLabel = null!;
    private ComboBox baudRateComboBox = null!;
    private Label frameLabel = null!;
    private ComboBox frameComboBox = null!;
    private Label pollIntervalLabel = null!;
    private TextBox pollIntervalTextBox = null!;
    private CheckBox rtsCheckBox = null!;
    private CheckBox dtrCheckBox = null!;
    private Button connectButton = null!;
    private Button refreshPortsButton = null!;
    private Label connectionStatusLabel = null!;
    private SplitContainer workspaceSplitContainer = null!;
    private GroupBox requestGroup = null!;
    private TableLayoutPanel requestRootLayout = null!;
    private FlowLayoutPanel requestToolbar = null!;
    private Button addSlaveButton = null!;
    private Button pollButton = null!;
    private Button saveConfigButton = null!;
    private Button updateConfigButton = null!;
    private ComboBox configPresetComboBox = null!;
    private Button renameConfigButton = null!;
    private Button deleteConfigButton = null!;
    private ThemedHorizontalScrollFlowPanel slaveTablesHostPanel = null!;

    private void InitializeComponent()
    {
        rootLayout = new TableLayoutPanel();
        connectionGroup = new GroupBox();
        connectionLayout = new TableLayoutPanel();
        portLabel = new Label();
        portComboBox = new ComboBox();
        baudRateLabel = new Label();
        baudRateComboBox = new ComboBox();
        frameLabel = new Label();
        frameComboBox = new ComboBox();
        pollIntervalLabel = new Label();
        pollIntervalTextBox = new TextBox();
        rtsCheckBox = new CheckBox();
        dtrCheckBox = new CheckBox();
        connectButton = new Button();
        refreshPortsButton = new Button();
        connectionStatusLabel = new Label();
        workspaceSplitContainer = new SplitContainer();
        requestGroup = new GroupBox();
        requestRootLayout = new TableLayoutPanel();
        requestToolbar = new FlowLayoutPanel();
        addSlaveButton = new Button();
        pollButton = new Button();
        saveConfigButton = new Button();
        updateConfigButton = new Button();
        configPresetComboBox = new ComboBox();
        renameConfigButton = new Button();
        deleteConfigButton = new Button();
        slaveTablesHostPanel = new ThemedHorizontalScrollFlowPanel();
        rootLayout.SuspendLayout();
        connectionGroup.SuspendLayout();
        connectionLayout.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)workspaceSplitContainer).BeginInit();
        workspaceSplitContainer.Panel1.SuspendLayout();
        workspaceSplitContainer.Panel2.SuspendLayout();
        workspaceSplitContainer.SuspendLayout();
        requestGroup.SuspendLayout();
        requestRootLayout.SuspendLayout();
        requestToolbar.SuspendLayout();
        SuspendLayout();
        // 
        // rootLayout
        // 
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(connectionGroup, 0, 0);
        rootLayout.Controls.Add(workspaceSplitContainer, 0, 1);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Location = new Point(0, 0);
        rootLayout.Name = "rootLayout";
        rootLayout.RowCount = 2;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppTheme.SectionHeaderHeight));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.Size = new Size(704, 292);
        rootLayout.TabIndex = 0;
        // 
        // connectionGroup
        // 
        connectionGroup.Controls.Add(connectionLayout);
        connectionGroup.Dock = DockStyle.Fill;
        connectionGroup.Location = new Point(0, 0);
        connectionGroup.Margin = AppTheme.WorkspaceGroupMargin;
        connectionGroup.Name = "connectionGroup";
        connectionGroup.Padding = AppTheme.WorkspaceGroupPadding;
        connectionGroup.Size = new Size(704, 72);
        connectionGroup.TabIndex = 0;
        connectionGroup.TabStop = false;
        connectionGroup.Text = "Connection";
        // 
        // connectionLayout
        // 
        connectionLayout.ColumnCount = 13;
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        connectionLayout.Controls.Add(portLabel, 0, 0);
        connectionLayout.Controls.Add(portComboBox, 1, 0);
        connectionLayout.Controls.Add(baudRateLabel, 2, 0);
        connectionLayout.Controls.Add(baudRateComboBox, 3, 0);
        connectionLayout.Controls.Add(frameLabel, 4, 0);
        connectionLayout.Controls.Add(frameComboBox, 5, 0);
        connectionLayout.Controls.Add(pollIntervalLabel, 6, 0);
        connectionLayout.Controls.Add(pollIntervalTextBox, 7, 0);
        connectionLayout.Controls.Add(rtsCheckBox, 8, 0);
        connectionLayout.Controls.Add(dtrCheckBox, 9, 0);
        connectionLayout.Controls.Add(connectButton, 10, 0);
        connectionLayout.Controls.Add(refreshPortsButton, 11, 0);
        connectionLayout.Controls.Add(connectionStatusLabel, 12, 0);
        connectionLayout.Dock = DockStyle.Fill;
        connectionLayout.Location = new Point(10, 24);
        connectionLayout.Margin = new Padding(0);
        connectionLayout.Name = "connectionLayout";
        connectionLayout.RowCount = 1;
        connectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        connectionLayout.Size = new Size(684, 34);
        connectionLayout.TabIndex = 0;
        // 
        // portLabel
        // 
        portLabel.Anchor = AnchorStyles.Left;
        portLabel.AutoSize = true;
        portLabel.Location = new Point(3, 7);
        portLabel.Name = "portLabel";
        portLabel.Size = new Size(29, 15);
        portLabel.TabIndex = 0;
        portLabel.Text = "Port";
        // 
        // portComboBox
        // 
        portComboBox.Dock = DockStyle.Fill;
        portComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        portComboBox.FormattingEnabled = true;
        portComboBox.Items.AddRange(new object[] { "COM1", "COM2", "COM3", "COM4" });
        portComboBox.Location = new Point(37, 3);
        portComboBox.Name = "portComboBox";
        portComboBox.DropDownWidth = 420;
        portComboBox.Size = new Size(136, 23);
        portComboBox.TabIndex = 1;
        // 
        // baudRateLabel
        // 
        baudRateLabel.Anchor = AnchorStyles.Left;
        baudRateLabel.AutoSize = true;
        baudRateLabel.Location = new Point(113, 7);
        baudRateLabel.Name = "baudRateLabel";
        baudRateLabel.Size = new Size(34, 15);
        baudRateLabel.TabIndex = 2;
        baudRateLabel.Text = "";
        
        
        // 
        // baudRateComboBox
        // 
        baudRateComboBox.Dock = DockStyle.Fill;
        baudRateComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        baudRateComboBox.FormattingEnabled = true;
        baudRateComboBox.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
        baudRateComboBox.Location = new Point(155, 3);
        baudRateComboBox.Name = "baudRateComboBox";
        baudRateComboBox.Size = new Size(64, 23);
        baudRateComboBox.TabIndex = 3;
        // 
        // frameLabel
        // 
        frameLabel.Anchor = AnchorStyles.Left;
        frameLabel.AutoSize = true;
        frameLabel.Location = new Point(241, 7);
        frameLabel.Name = "frameLabel";
        frameLabel.Size = new Size(28, 15);
        frameLabel.TabIndex = 4;
        frameLabel.Text = "";
        
        
        // 
        // frameComboBox
        // 
        frameComboBox.Dock = DockStyle.Fill;
        frameComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        frameComboBox.FormattingEnabled = true;
        frameComboBox.Items.AddRange(new object[] { "8N1", "8E1", "8O1", "8N2", "7E1" });
        frameComboBox.Location = new Point(283, 3);
        frameComboBox.Name = "frameComboBox";
        frameComboBox.Size = new Size(54, 23);
        frameComboBox.TabIndex = 5;
        // 
        // pollIntervalLabel
        // 
        pollIntervalLabel.Anchor = AnchorStyles.Left;
        pollIntervalLabel.AutoSize = true;
        pollIntervalLabel.Location = new Point(343, 7);
        pollIntervalLabel.Name = "pollIntervalLabel";
        pollIntervalLabel.Size = new Size(46, 15);
        pollIntervalLabel.TabIndex = 6;
        pollIntervalLabel.Text = "Poll ms";
        // 
        // pollIntervalTextBox
        // 
        pollIntervalTextBox.Dock = DockStyle.Fill;
        pollIntervalTextBox.Location = new Point(395, 3);
        pollIntervalTextBox.Name = "pollIntervalTextBox";
        pollIntervalTextBox.Size = new Size(52, 23);
        pollIntervalTextBox.TabIndex = 7;
        pollIntervalTextBox.Text = "1000";
        // 
        // rtsCheckBox
        // 
        rtsCheckBox.Anchor = AnchorStyles.Left;
        rtsCheckBox.AutoSize = true;
        rtsCheckBox.Location = new Point(453, 5);
        rtsCheckBox.Name = "rtsCheckBox";
        rtsCheckBox.Size = new Size(46, 19);
        rtsCheckBox.TabIndex = 8;
        rtsCheckBox.Text = "RTS";
        rtsCheckBox.UseVisualStyleBackColor = true;
        // 
        // dtrCheckBox
        // 
        dtrCheckBox.Anchor = AnchorStyles.Left;
        dtrCheckBox.AutoSize = true;
        dtrCheckBox.Location = new Point(507, 5);
        dtrCheckBox.Name = "dtrCheckBox";
        dtrCheckBox.Size = new Size(47, 19);
        dtrCheckBox.TabIndex = 9;
        dtrCheckBox.Text = "DTR";
        dtrCheckBox.UseVisualStyleBackColor = true;
        // 
        // connectButton
        // 
        connectButton.Dock = DockStyle.Fill;
        connectButton.Location = new Point(561, 3);
        connectButton.Name = "connectButton";
        connectButton.Size = new Size(86, 28);
        connectButton.TabIndex = 10;
        connectButton.Text = "Open Port";
        connectButton.UseVisualStyleBackColor = true;
        connectButton.Click += connectButton_Click;
        // 
        // refreshPortsButton
        // 
        refreshPortsButton.Dock = DockStyle.Fill;
        refreshPortsButton.Location = new Point(653, 3);
        refreshPortsButton.Name = "refreshPortsButton";
        refreshPortsButton.Size = new Size(74, 28);
        refreshPortsButton.TabIndex = 11;
        refreshPortsButton.Text = "Refresh";
        refreshPortsButton.UseVisualStyleBackColor = true;
        refreshPortsButton.Click += refreshPortsButton_Click;
        // 
        // connectionStatusLabel
        // 
        connectionStatusLabel.Anchor = AnchorStyles.Left;
        connectionStatusLabel.AutoSize = true;
        connectionStatusLabel.ForeColor = Color.FromArgb(0, 102, 68);
        connectionStatusLabel.Location = new Point(733, 7);
        connectionStatusLabel.Name = "connectionStatusLabel";
        connectionStatusLabel.Size = new Size(37, 15);
        connectionStatusLabel.TabIndex = 12;
        connectionStatusLabel.Text = "Ready";
        // 
        // workspaceSplitContainer
        // 
        workspaceSplitContainer.Dock = DockStyle.Fill;
        workspaceSplitContainer.Location = new Point(0, 80);
        workspaceSplitContainer.Margin = new Padding(0);
        workspaceSplitContainer.Name = "workspaceSplitContainer";
        workspaceSplitContainer.Orientation = Orientation.Horizontal;
        // 
        // workspaceSplitContainer.Panel1
        // 
        workspaceSplitContainer.Panel1.Controls.Add(requestGroup);
        // 
        // workspaceSplitContainer.Panel2
        // 
        workspaceSplitContainer.Size = new Size(704, 212);
        workspaceSplitContainer.SplitterDistance = 212;
        workspaceSplitContainer.IsSplitterFixed = true;
        workspaceSplitContainer.Panel2Collapsed = true;
        workspaceSplitContainer.TabIndex = 1;
        // 
        // requestGroup
        // 
        requestGroup.Controls.Add(requestRootLayout);
        requestGroup.Dock = DockStyle.Fill;
        requestGroup.Location = new Point(0, 0);
        requestGroup.Name = "requestGroup";
        requestGroup.Padding = AppTheme.WorkspaceGroupPadding;
        requestGroup.Size = new Size(704, 128);
        requestGroup.TabIndex = 0;
        requestGroup.TabStop = false;
        requestGroup.Text = "Read Data";
        // 
        // requestRootLayout
        // 
        requestRootLayout.ColumnCount = 1;
        requestRootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        requestRootLayout.Controls.Add(requestToolbar, 0, 0);
        requestRootLayout.Controls.Add(slaveTablesHostPanel, 0, 1);
        requestRootLayout.Dock = DockStyle.Fill;
        requestRootLayout.Location = new Point(10, 24);
        requestRootLayout.Name = "requestRootLayout";
        requestRootLayout.RowCount = 2;
        requestRootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppTheme.ToolbarRowHeight));
        requestRootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        requestRootLayout.Size = new Size(684, 96);
        requestRootLayout.TabIndex = 0;
        // 
        // requestToolbar
        // 
        requestToolbar.Controls.Add(addSlaveButton);
        requestToolbar.Controls.Add(pollButton);
        requestToolbar.Controls.Add(saveConfigButton);
        requestToolbar.Controls.Add(updateConfigButton);
        requestToolbar.Controls.Add(configPresetComboBox);
        requestToolbar.Controls.Add(renameConfigButton);
        requestToolbar.Controls.Add(deleteConfigButton);
        requestToolbar.Dock = DockStyle.Fill;
        requestToolbar.FlowDirection = FlowDirection.LeftToRight;
        requestToolbar.Location = new Point(0, 0);
        requestToolbar.Margin = new Padding(0);
        requestToolbar.Name = "requestToolbar";
        requestToolbar.Size = new Size(684, 34);
        requestToolbar.TabIndex = 0;
        requestToolbar.WrapContents = false;
        // 
        // addSlaveButton
        // 
        addSlaveButton.Location = new Point(0, 3);
        addSlaveButton.Margin = AppTheme.ToolbarButtonMargin;
        addSlaveButton.Name = "addSlaveButton";
        addSlaveButton.Size = AppTheme.ToolbarButtonSize;
        addSlaveButton.TabIndex = 1;
        addSlaveButton.Text = "Add Slave";
        addSlaveButton.UseVisualStyleBackColor = true;
        addSlaveButton.Click += addSlaveButton_Click;
        // 
        // pollButton
        // 
        pollButton.Enabled = false;
        pollButton.Location = new Point(104, 3);
        pollButton.Margin = AppTheme.ToolbarButtonMargin;
        pollButton.Name = "pollButton";
        pollButton.Size = AppTheme.ToolbarButtonSize;
        pollButton.TabIndex = 2;
        pollButton.Text = "Start Poll";
        pollButton.UseVisualStyleBackColor = true;
        pollButton.Click += pollButton_Click;
        // 
        // saveConfigButton
        // 
        saveConfigButton.Location = new Point(196, 3);
        saveConfigButton.Margin = AppTheme.ToolbarButtonMargin;
        saveConfigButton.Name = "saveConfigButton";
        saveConfigButton.Size = AppTheme.ToolbarButtonSize;
        saveConfigButton.TabIndex = 3;
        saveConfigButton.Text = "Save Config";
        saveConfigButton.UseVisualStyleBackColor = true;
        saveConfigButton.Click += saveConfigButton_Click;
        // 
        // updateConfigButton
        // 
        updateConfigButton.Enabled = false;
        updateConfigButton.Location = new Point(296, 3);
        updateConfigButton.Margin = AppTheme.ToolbarButtonMargin;
        updateConfigButton.Name = "updateConfigButton";
        updateConfigButton.Size = AppTheme.ToolbarWideButtonSize;
        updateConfigButton.TabIndex = 4;
        updateConfigButton.Text = "Update Config";
        updateConfigButton.UseVisualStyleBackColor = true;
        updateConfigButton.Click += updateConfigButton_Click;
        // 
        // configPresetComboBox
        // 
        configPresetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        configPresetComboBox.FormattingEnabled = true;
        configPresetComboBox.Location = new Point(404, 4);
        configPresetComboBox.Margin = AppTheme.ToolbarComboMargin;
        configPresetComboBox.Name = "configPresetComboBox";
        configPresetComboBox.Size = AppTheme.ToolbarPresetSize;
        configPresetComboBox.TabIndex = 5;
        configPresetComboBox.SelectedIndexChanged += configPresetComboBox_SelectedIndexChanged;
        // 
        // renameConfigButton
        // 
        renameConfigButton.Enabled = false;
        renameConfigButton.Location = new Point(542, 3);
        renameConfigButton.Margin = AppTheme.ToolbarTrailingButtonMargin;
        renameConfigButton.Name = "renameConfigButton";
        renameConfigButton.Size = AppTheme.ToolbarRenameButtonSize;
        renameConfigButton.TabIndex = 6;
        renameConfigButton.Text = "Rename";
        renameConfigButton.UseVisualStyleBackColor = true;
        renameConfigButton.Click += renameConfigButton_Click;
        // 
        // deleteConfigButton
        // 
        deleteConfigButton.Enabled = false;
        deleteConfigButton.Location = new Point(614, 3);
        deleteConfigButton.Margin = AppTheme.ToolbarLastButtonMargin;
        deleteConfigButton.Name = "deleteConfigButton";
        deleteConfigButton.Size = AppTheme.ToolbarDeleteButtonSize;
        deleteConfigButton.TabIndex = 7;
        deleteConfigButton.Text = "Delete";
        deleteConfigButton.UseVisualStyleBackColor = true;
        deleteConfigButton.Click += deleteConfigButton_Click;
        // 
        // slaveTablesHostPanel
        // 
        slaveTablesHostPanel.AutoScroll = true;
        slaveTablesHostPanel.Dock = DockStyle.Fill;
        slaveTablesHostPanel.FlowDirection = FlowDirection.LeftToRight;
        slaveTablesHostPanel.Location = new Point(0, 34);
        slaveTablesHostPanel.Margin = new Padding(0);
        slaveTablesHostPanel.Name = "slaveTablesHostPanel";
        slaveTablesHostPanel.Size = new Size(684, 62);
        slaveTablesHostPanel.TabIndex = 1;
        slaveTablesHostPanel.WrapContents = false;
        // 
        // ModbusRtuClientView
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(rootLayout);
        Name = "ModbusRtuClientView";
        Size = new Size(704, 292);
        rootLayout.ResumeLayout(false);
        connectionGroup.ResumeLayout(false);
        connectionLayout.ResumeLayout(false);
        connectionLayout.PerformLayout();
        workspaceSplitContainer.Panel1.ResumeLayout(false);
        workspaceSplitContainer.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)workspaceSplitContainer).EndInit();
        workspaceSplitContainer.ResumeLayout(false);
        requestGroup.ResumeLayout(false);
        requestRootLayout.ResumeLayout(false);
        requestToolbar.ResumeLayout(false);
        requestToolbar.PerformLayout();
        ResumeLayout(false);

        portComboBox.SelectedIndex = 0;
        baudRateComboBox.SelectedIndex = 1;
        frameComboBox.SelectedIndex = 0;
    }
}
