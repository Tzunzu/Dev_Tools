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
        ["Modbus TCP Server"] = static () => new ModbusTcpServerView(),
        ["Settings"] = static () => new SettingsView()
    };
    private readonly Dictionary<string, Control> toolViewCache = new(StringComparer.Ordinal);
    private Control? welcomeView;

    private void InitializeToolWorkspace()
    {
        welcomeView = new WelcomeView();
        ShowToolView("Welcome", welcomeView);
        navigationTree.ExpandAll();
        navigationTree.SelectedNode = navigationTree.Nodes.Count > 0 ? navigationTree.Nodes[0] : null;
    }

    private void ShowSelectedTool(string selectedText)
    {
        if (toolViewFactories.TryGetValue(selectedText, out var createView))
        {
            if (!toolViewCache.TryGetValue(selectedText, out var view))
            {
                view = createView();
                toolViewCache[selectedText] = view;
            }

            ShowToolView(selectedText, view);
            return;
        }

        if (welcomeView is null || welcomeView.IsDisposed)
        {
            welcomeView = new WelcomeView();
        }

        ShowToolView(selectedText, welcomeView);
    }

    private void ShowToolView(string title, Control view)
    {
        workAreaGroup.Text = title;
        workAreaHostPanel.SuspendLayout();
        if (!workAreaHostPanel.Controls.Contains(view))
        {
            foreach (Control control in workAreaHostPanel.Controls)
            {
                control.Visible = false;
            }

            AppTheme.Apply(view);
            view.Dock = DockStyle.Fill;
            workAreaHostPanel.Controls.Add(view);
        }

        foreach (Control control in workAreaHostPanel.Controls)
        {
            control.Visible = ReferenceEquals(control, view);
        }

        AppTheme.Apply(view);
        view.Dock = DockStyle.Fill;
        view.BringToFront();
        workAreaHostPanel.ResumeLayout();
    }
}
