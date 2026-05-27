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
        AppTheme.ThemeChanged += AppTheme_ThemeChanged;
        darkModeMenuItem.Checked = AppTheme.CurrentMode == ThemeMode.Dark;
        ApplyThemeToWorkspace();
        InitializeToolWorkspace();

        originalOut = Console.Out;
        originalError = Console.Error;

        Console.SetOut(new OutputPanelWriter(AddOutput));
        Console.SetError(new OutputPanelWriter(message => AddOutput("[ERR] " + message)));

        AddOutput("Application started.");
    }

    private void ApplyThemeToWorkspace()
    {
        AppTheme.Apply(this);

        if (welcomeView is not null && !welcomeView.IsDisposed)
        {
            AppTheme.Apply(welcomeView);
        }

        foreach (var view in toolViewCache.Values)
        {
            if (!view.IsDisposed)
            {
                AppTheme.Apply(view);
            }
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        AppTheme.ThemeChanged -= AppTheme_ThemeChanged;
        Console.SetOut(originalOut);
        Console.SetError(originalError);
        base.OnFormClosed(e);
    }

    private void AppTheme_ThemeChanged(ThemeOptions options)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => ApplyThemeFromOptions(options));
            return;
        }

        ApplyThemeFromOptions(options);
    }

    private void ApplyThemeFromOptions(ThemeOptions options)
    {
        darkModeMenuItem.Checked = options.Mode == ThemeMode.Dark;
        ApplyThemeToWorkspace();
    }
}
