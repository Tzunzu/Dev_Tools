using System.Windows.Forms;

namespace DevTools.App.Forms;

internal sealed partial class MainForm
{
    private MenuStrip mainMenu = null!;
    private ToolStripMenuItem fileMenuItem = null!;
    private ToolStripMenuItem exitMenuItem = null!;
    private ToolStripMenuItem helpMenuItem = null!;
    private ToolStripMenuItem aboutMenuItem = null!;
    private StatusStrip mainStatusStrip = null!;
    private ToolStripStatusLabel statusTextLabel = null!;
    private SplitContainer mainSplitContainer = null!;
    private SplitContainer contentSplitContainer = null!;
    private TreeView navigationTree = null!;
    private GroupBox workAreaGroup = null!;
    private Panel workAreaHostPanel = null!;
    private GroupBox outputGroup = null!;
    private TabControl outputTabControl = null!;
    private TabPage outputEventsTabPage = null!;
    private TabPage outputRawDataTabPage = null!;
    private ListView outputListView = null!;
    private RichTextBox outputRawDataRichTextBox = null!;
    private ColumnHeader outputTimeColumn = null!;
    private ColumnHeader outputEventColumn = null!;

    private void InitializeComponent()
    {
        mainMenu = new MenuStrip();
        fileMenuItem = new ToolStripMenuItem();
        exitMenuItem = new ToolStripMenuItem();
        helpMenuItem = new ToolStripMenuItem();
        aboutMenuItem = new ToolStripMenuItem();
        mainStatusStrip = new StatusStrip();
        statusTextLabel = new ToolStripStatusLabel();
        mainSplitContainer = new SplitContainer();
        navigationTree = new TreeView();
        contentSplitContainer = new SplitContainer();
        workAreaGroup = new GroupBox();
        workAreaHostPanel = new Panel();
        outputGroup = new GroupBox();
        outputTabControl = new TabControl();
        outputEventsTabPage = new TabPage();
        outputRawDataTabPage = new TabPage();
        outputListView = new ListView();
        outputRawDataRichTextBox = new RichTextBox();
        outputTimeColumn = new ColumnHeader();
        outputEventColumn = new ColumnHeader();
        mainMenu.SuspendLayout();
        mainStatusStrip.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)mainSplitContainer).BeginInit();
        mainSplitContainer.Panel1.SuspendLayout();
        mainSplitContainer.Panel2.SuspendLayout();
        mainSplitContainer.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)contentSplitContainer).BeginInit();
        contentSplitContainer.Panel1.SuspendLayout();
        contentSplitContainer.Panel2.SuspendLayout();
        contentSplitContainer.SuspendLayout();
        workAreaGroup.SuspendLayout();
        outputTabControl.SuspendLayout();
        outputEventsTabPage.SuspendLayout();
        outputRawDataTabPage.SuspendLayout();
        outputGroup.SuspendLayout();
        SuspendLayout();
        // 
        // mainMenu
        // 
        mainMenu.Items.AddRange(new ToolStripItem[] { fileMenuItem, helpMenuItem });
        mainMenu.Location = new System.Drawing.Point(0, 0);
        mainMenu.Name = "mainMenu";
        mainMenu.Size = new System.Drawing.Size(1000, 24);
        mainMenu.TabIndex = 0;
        mainMenu.Text = "mainMenu";
        // 
        // fileMenuItem
        // 
        fileMenuItem.DropDownItems.AddRange(new ToolStripItem[] { exitMenuItem });
        fileMenuItem.Name = "fileMenuItem";
        fileMenuItem.Size = new System.Drawing.Size(37, 20);
        fileMenuItem.Text = "File";
        // 
        // exitMenuItem
        // 
        exitMenuItem.Name = "exitMenuItem";
        exitMenuItem.Size = new System.Drawing.Size(93, 22);
        exitMenuItem.Text = "Exit";
        exitMenuItem.Click += exitMenuItem_Click;
        // 
        // helpMenuItem
        // 
        helpMenuItem.DropDownItems.AddRange(new ToolStripItem[] { aboutMenuItem });
        helpMenuItem.Name = "helpMenuItem";
        helpMenuItem.Size = new System.Drawing.Size(44, 20);
        helpMenuItem.Text = "Help";
        // 
        // aboutMenuItem
        // 
        aboutMenuItem.Name = "aboutMenuItem";
        aboutMenuItem.Size = new System.Drawing.Size(107, 22);
        aboutMenuItem.Text = "About";
        aboutMenuItem.Click += aboutMenuItem_Click;
        // 
        // mainStatusStrip
        // 
        mainStatusStrip.Items.AddRange(new ToolStripItem[] { statusTextLabel });
        mainStatusStrip.Location = new System.Drawing.Point(0, 628);
        mainStatusStrip.Name = "mainStatusStrip";
        mainStatusStrip.Size = new System.Drawing.Size(1000, 22);
        mainStatusStrip.TabIndex = 2;
        mainStatusStrip.Text = "mainStatusStrip";
        // 
        // statusTextLabel
        // 
        statusTextLabel.Name = "statusTextLabel";
        statusTextLabel.Size = new System.Drawing.Size(39, 17);
        statusTextLabel.Text = "Ready";
        // 
        // mainSplitContainer
        // 
        mainSplitContainer.Dock = DockStyle.Fill;
        mainSplitContainer.Location = new System.Drawing.Point(0, 24);
        mainSplitContainer.Name = "mainSplitContainer";
        // 
        // mainSplitContainer.Panel1
        // 
        mainSplitContainer.Panel1.Controls.Add(navigationTree);
        // 
        // mainSplitContainer.Panel2
        // 
        mainSplitContainer.Panel2.Controls.Add(contentSplitContainer);
        mainSplitContainer.Size = new System.Drawing.Size(1000, 604);
        mainSplitContainer.SplitterDistance = 260;
        mainSplitContainer.TabIndex = 1;
        // 
        // navigationTree
        // 
        navigationTree.Dock = DockStyle.Fill;
        navigationTree.Location = new System.Drawing.Point(0, 0);
        navigationTree.Name = "navigationTree";
        navigationTree.Nodes.AddRange(new TreeNode[]
        {
            new TreeNode("Tools", new TreeNode[]
            {
                new TreeNode("Modbus RTU client"),
                new TreeNode("Modbus RTU Server"),
                new TreeNode("Modbus TCP client"),
                new TreeNode("Modbus TCP Server")
            }),
            new TreeNode("Settings"),
            new TreeNode("Help")
        });
        navigationTree.Size = new System.Drawing.Size(260, 604);
        navigationTree.TabIndex = 0;
        navigationTree.AfterSelect += navigationTree_AfterSelect;
        // 
        // contentSplitContainer
        // 
        contentSplitContainer.Dock = DockStyle.Fill;
        contentSplitContainer.Location = new System.Drawing.Point(0, 0);
        contentSplitContainer.Name = "contentSplitContainer";
        contentSplitContainer.Orientation = Orientation.Horizontal;
        // 
        // contentSplitContainer.Panel1
        // 
        contentSplitContainer.Panel1.Controls.Add(workAreaGroup);
        // 
        // contentSplitContainer.Panel2
        // 
        contentSplitContainer.Panel2.Controls.Add(outputGroup);
        contentSplitContainer.Size = new System.Drawing.Size(736, 604);
        contentSplitContainer.SplitterDistance = 470;
        contentSplitContainer.TabIndex = 0;
        // 
        // workAreaGroup
        // 
        workAreaGroup.Controls.Add(workAreaHostPanel);
        workAreaGroup.Dock = DockStyle.Fill;
        workAreaGroup.Location = new System.Drawing.Point(0, 0);
        workAreaGroup.Name = "workAreaGroup";
        workAreaGroup.Padding = new Padding(16);
        workAreaGroup.Size = new System.Drawing.Size(736, 340);
        workAreaGroup.TabIndex = 0;
        workAreaGroup.TabStop = false;
        workAreaGroup.Text = "Work Area";
        // 
        // workAreaHostPanel
        // 
        workAreaHostPanel.Dock = DockStyle.Fill;
        workAreaHostPanel.Location = new System.Drawing.Point(16, 32);
        workAreaHostPanel.Name = "workAreaHostPanel";
        workAreaHostPanel.Size = new System.Drawing.Size(704, 292);
        workAreaHostPanel.TabIndex = 0;
        // 
        // outputGroup
        // 
        outputGroup.Controls.Add(outputTabControl);
        outputGroup.Dock = DockStyle.Fill;
        outputGroup.Location = new System.Drawing.Point(0, 0);
        outputGroup.Name = "outputGroup";
        outputGroup.Padding = new Padding(8);
        outputGroup.Size = new System.Drawing.Size(736, 260);
        outputGroup.TabIndex = 0;
        outputGroup.TabStop = false;
        outputGroup.Text = "Output";
        // 
        // outputTabControl
        // 
        outputTabControl.Controls.Add(outputEventsTabPage);
        outputTabControl.Controls.Add(outputRawDataTabPage);
        outputTabControl.Dock = DockStyle.Fill;
        outputTabControl.Location = new System.Drawing.Point(8, 24);
        outputTabControl.Name = "outputTabControl";
        outputTabControl.SelectedIndex = 0;
        outputTabControl.Size = new System.Drawing.Size(720, 228);
        outputTabControl.TabIndex = 0;
        // 
        // outputEventsTabPage
        // 
        outputEventsTabPage.Controls.Add(outputListView);
        outputEventsTabPage.Location = new System.Drawing.Point(4, 24);
        outputEventsTabPage.Name = "outputEventsTabPage";
        outputEventsTabPage.Padding = new Padding(3);
        outputEventsTabPage.Size = new System.Drawing.Size(712, 200);
        outputEventsTabPage.TabIndex = 0;
        outputEventsTabPage.Text = "Events";
        outputEventsTabPage.UseVisualStyleBackColor = true;
        // 
        // outputRawDataTabPage
        // 
        outputRawDataTabPage.Controls.Add(outputRawDataRichTextBox);
        outputRawDataTabPage.Location = new System.Drawing.Point(4, 24);
        outputRawDataTabPage.Name = "outputRawDataTabPage";
        outputRawDataTabPage.Padding = new Padding(3);
        outputRawDataTabPage.Size = new System.Drawing.Size(712, 200);
        outputRawDataTabPage.TabIndex = 1;
        outputRawDataTabPage.Text = "Raw data";
        outputRawDataTabPage.UseVisualStyleBackColor = true;
        // 
        // outputListView
        // 
        outputListView.Columns.AddRange(new ColumnHeader[] { outputTimeColumn, outputEventColumn });
        outputListView.Dock = DockStyle.Fill;
        outputListView.FullRowSelect = true;
        outputListView.GridLines = true;
        outputListView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        outputListView.Location = new System.Drawing.Point(3, 3);
        outputListView.MultiSelect = false;
        outputListView.Name = "outputListView";
        outputListView.Size = new System.Drawing.Size(706, 194);
        outputListView.TabIndex = 0;
        outputListView.UseCompatibleStateImageBehavior = false;
        outputListView.View = View.Details;
        // 
        // outputRawDataRichTextBox
        // 
        outputRawDataRichTextBox.BackColor = System.Drawing.Color.FromArgb(249, 250, 251);
        outputRawDataRichTextBox.BorderStyle = BorderStyle.FixedSingle;
        outputRawDataRichTextBox.Dock = DockStyle.Fill;
        outputRawDataRichTextBox.Font = new System.Drawing.Font("Consolas", 9F);
        outputRawDataRichTextBox.Location = new System.Drawing.Point(3, 3);
        outputRawDataRichTextBox.Name = "outputRawDataRichTextBox";
        outputRawDataRichTextBox.ReadOnly = true;
        outputRawDataRichTextBox.Size = new System.Drawing.Size(706, 194);
        outputRawDataRichTextBox.TabIndex = 0;
        outputRawDataRichTextBox.Text = "[ready]";
        // 
        // outputTimeColumn
        // 
        outputTimeColumn.Text = "Time";
        outputTimeColumn.Width = 140;
        // 
        // outputEventColumn
        // 
        outputEventColumn.Text = "Event";
        outputEventColumn.Width = 540;
        // 
        // MainForm
        // 
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(1000, 650);
        Controls.Add(mainSplitContainer);
        Controls.Add(mainStatusStrip);
        Controls.Add(mainMenu);
        MainMenuStrip = mainMenu;
        MinimumSize = new System.Drawing.Size(860, 560);
        Name = "MainForm";
        StartPosition = FormStartPosition.Manual;
        WindowState = FormWindowState.Maximized;
        Text = "DevTools Desktop App";
        mainMenu.ResumeLayout(false);
        mainMenu.PerformLayout();
        mainStatusStrip.ResumeLayout(false);
        mainStatusStrip.PerformLayout();
        mainSplitContainer.Panel1.ResumeLayout(false);
        mainSplitContainer.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)mainSplitContainer).EndInit();
        mainSplitContainer.ResumeLayout(false);
        contentSplitContainer.Panel1.ResumeLayout(false);
        contentSplitContainer.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)contentSplitContainer).EndInit();
        contentSplitContainer.ResumeLayout(false);
        workAreaGroup.ResumeLayout(false);
        workAreaGroup.PerformLayout();
        outputTabControl.ResumeLayout(false);
        outputEventsTabPage.ResumeLayout(false);
        outputRawDataTabPage.ResumeLayout(false);
        outputGroup.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
