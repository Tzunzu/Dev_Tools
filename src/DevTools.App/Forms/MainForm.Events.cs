using System;
using System.Windows.Forms;

namespace DevTools.App.Forms;

internal sealed partial class MainForm
{
    private void actionButton_Click(object? sender, EventArgs e)
    {
        statusTextLabel.Text = "Sample action completed.";
        Console.WriteLine("Sample action executed.");
    }

    private void exitMenuItem_Click(object? sender, EventArgs e)
    {
        Close();
    }

    private void aboutMenuItem_Click(object? sender, EventArgs e)
    {
        MessageBox.Show(
            "DevTools starter desktop shell\nBuilt with WinForms on .NET 8.",
            "About",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void navigationTree_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        var selectedText = e.Node?.Text ?? "(none)";
        statusTextLabel.Text = "Selected: " + selectedText;
        ShowSelectedTool(selectedText);
        Console.WriteLine("Navigation changed to '" + selectedText + "'.");
    }
}
