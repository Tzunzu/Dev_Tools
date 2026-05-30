using System.Windows.Controls;
using DevTools.Wpf.Infrastructure.UI;

namespace DevTools.Wpf.Views;

public partial class SettingsView : UserControl
{
    private bool suppressThemeChange;

    public SettingsView()
    {
        InitializeComponent();
        InitializeThemeSelection();
    }

    private void InitializeThemeSelection()
    {
        suppressThemeChange = true;
        ThemeComboBox.SelectedIndex = ThemeManager.CurrentTheme == AppTheme.Light ? 1 : 0;
        suppressThemeChange = false;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressThemeChange)
        {
            return;
        }

        var selectedTheme = ThemeComboBox.SelectedIndex == 1 ? AppTheme.Light : AppTheme.Dark;
        if (selectedTheme == ThemeManager.CurrentTheme)
        {
            return;
        }

        ThemeManager.ApplyTheme(selectedTheme, persist: true);
    }
}
