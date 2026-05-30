using System.Windows.Controls;
using System.Reflection;

namespace DevTools.Wpf.Views;

public partial class HelpView : UserControl
{
    public string AppVersionText { get; }

    public HelpView()
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        AppVersionText = string.IsNullOrWhiteSpace(version)
            ? "Version unknown"
            : $"Version {version}";

        InitializeComponent();
    }
}
