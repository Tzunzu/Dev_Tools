using System.Configuration;
using System.Data;
using System.Windows;
using DevTools.Wpf.Infrastructure;
using DevTools.Wpf.Infrastructure.UI;

namespace DevTools.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		ThemeManager.Initialize();
		base.OnStartup(e);
	}

	protected override void OnExit(ExitEventArgs e)
	{
		SharedRuntimes.Instance.Cleanup();
		base.OnExit(e);
	}
}

