using DevTools.App.Controls.ToolViews;
using DevTools.App.Infrastructure.UI;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace DevTools.App.Forms;

internal sealed partial class MainForm
{
    private readonly Dictionary<string, Func<Control>> toolViewFactories = new(StringComparer.Ordinal)
    {
        ["Modbus RTU client"] = static () => new ModbusRtuClientView(),
        ["Modbus RTU Server"] = static () => new ModbusRtuServerView(),
        ["Modbus TCP client"] = static () => new ModbusTcpClientView(),
        ["Modbus TCP Server"] = static () => new ModbusTcpServerView()
    };

    private void InitializeToolWorkspace()
    {
        ShowToolView("Welcome", new WelcomeView());
        navigationTree.ExpandAll();
        navigationTree.SelectedNode = navigationTree.Nodes.Count > 0 ? navigationTree.Nodes[0] : null;
    }

    private void ShowSelectedTool(string selectedText)
    {
        if (toolViewFactories.TryGetValue(selectedText, out var createView))
        {
            ShowToolView(selectedText, createView());
            return;
        }

        ShowToolView(selectedText, new WelcomeView());
    }

    private void ShowToolView(string title, Control view)
    {
        workAreaGroup.Text = title;
        workAreaHostPanel.SuspendLayout();
        workAreaHostPanel.Controls.Clear();
        AppTheme.Apply(view);
        view.Dock = DockStyle.Fill;
        workAreaHostPanel.Controls.Add(view);
        workAreaHostPanel.ResumeLayout();
    }
}
