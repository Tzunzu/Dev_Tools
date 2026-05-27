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

        outputEventsGrid.Rows.Add(DateTime.Now.ToString("HH:mm:ss"), message);

        const int maxRows = 1500;
        while (outputEventsGrid.Rows.Count > maxRows)
        {
            outputEventsGrid.Rows.RemoveAt(0);
        }

        if (outputEventsGrid.Rows.Count > 0)
        {
            var lastIndex = outputEventsGrid.Rows.Count - 1;
            outputEventsGrid.ClearSelection();
            outputEventsGrid.Rows[lastIndex].Selected = true;
            try
            {
                outputEventsGrid.FirstDisplayedScrollingRowIndex = lastIndex;
            }
            catch
            {
                // Ignore occasional scroll index races while resizing.
            }
        }
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
