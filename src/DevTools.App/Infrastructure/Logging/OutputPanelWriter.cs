using System;
using System.IO;
using System.Text;

namespace DevTools.App.Infrastructure.Logging;

internal sealed class OutputPanelWriter : TextWriter
{
    private readonly Action<string> writeLine;
    private readonly StringBuilder buffer = new();

    public OutputPanelWriter(Action<string> writeLine)
    {
        this.writeLine = writeLine;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        if (value == '\r')
        {
            return;
        }

        if (value == '\n')
        {
            FlushBuffer();
            return;
        }

        buffer.Append(value);
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        foreach (var character in value)
        {
            Write(character);
        }
    }

    public override void WriteLine(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            buffer.Append(value);
        }

        FlushBuffer();
    }

    private void FlushBuffer()
    {
        if (buffer.Length == 0)
        {
            return;
        }

        writeLine(buffer.ToString());
        buffer.Clear();
    }
}
