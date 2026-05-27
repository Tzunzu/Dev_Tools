using System;
using System.Windows.Forms;

namespace DevTools.App.Forms;

internal sealed partial class MainForm
{
    private void AddOutput(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AddOutput(message));
            return;
        }

        if (IsRawDataMessage(message))
        {
            AddRawData(message);
            return;
        }

        var item = new ListViewItem(DateTime.Now.ToString("HH:mm:ss"));
        item.SubItems.Add(message);
        outputListView.Items.Add(item);
        outputListView.EnsureVisible(outputListView.Items.Count - 1);
    }

    private static bool IsRawDataMessage(string message)
    {
        return message.StartsWith("[raw]", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("[trace]", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("[response]", StringComparison.OrdinalIgnoreCase);
    }

    private void AddRawData(string message)
    {
        var line = DateTime.Now.ToString("HH:mm:ss") + " " + message;
        if (outputRawDataRichTextBox.TextLength == 0 || outputRawDataRichTextBox.Text == "[ready]")
        {
            outputRawDataRichTextBox.Text = line;
            return;
        }

        outputRawDataRichTextBox.AppendText(Environment.NewLine + line);
        outputRawDataRichTextBox.SelectionStart = outputRawDataRichTextBox.TextLength;
        outputRawDataRichTextBox.ScrollToCaret();
    }
}
