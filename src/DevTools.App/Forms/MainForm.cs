using DevTools.App.Infrastructure.Logging;
using DevTools.App.Infrastructure.UI;
using System;
using System.IO;
using System.Windows.Forms;

namespace DevTools.App.Forms;

internal sealed partial class MainForm : Form
{
    private readonly TextWriter originalOut;
    private readonly TextWriter originalError;

    public MainForm()
    {
        InitializeComponent();
        AppTheme.Apply(this);
        InitializeToolWorkspace();

        originalOut = Console.Out;
        originalError = Console.Error;

        Console.SetOut(new OutputPanelWriter(AddOutput));
        Console.SetError(new OutputPanelWriter(message => AddOutput("[ERR] " + message)));

        AddOutput("Application started.");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Console.SetOut(originalOut);
        Console.SetError(originalError);
        base.OnFormClosed(e);
    }
}
