using System.IO;
using System.Text.Json;
using System.Windows;

namespace DevTools.Wpf.Infrastructure.UI;

public static class ThemeManager
{
    private const string ThemeDictionaryPrefix = "Styles/Themes/";
    private const string DarkThemeDictionaryPath = ThemeDictionaryPrefix + "Dark.xaml";
    private const string LightThemeDictionaryPath = ThemeDictionaryPrefix + "Light.xaml";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "devtools-wpf-user-settings.json");

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public static void Initialize()
    {
        var loaded = LoadTheme();
        ApplyTheme(loaded, persist: false);
    }

    public static void ApplyTheme(AppTheme theme, bool persist = true)
    {
        CurrentTheme = theme;

        if (Application.Current is null)
        {
            return;
        }

        ReplaceThemeDictionary(theme);

        if (persist)
        {
            SaveTheme(theme);
        }
    }

    private static void ReplaceThemeDictionary(AppTheme theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            var source = dictionaries[i].Source?.OriginalString;
            if (!string.IsNullOrWhiteSpace(source) && source.Contains(ThemeDictionaryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(i);
            }
        }

        dictionaries.Insert(0, new ResourceDictionary
        {
            Source = new Uri(theme == AppTheme.Light ? LightThemeDictionaryPath : DarkThemeDictionaryPath, UriKind.Relative)
        });
    }

    private static AppTheme LoadTheme()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return AppTheme.Dark;
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<ThemeSettings>(json, JsonOptions);
            return settings?.Theme ?? AppTheme.Dark;
        }
        catch
        {
            return AppTheme.Dark;
        }
    }

    private static void SaveTheme(AppTheme theme)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(new ThemeSettings { Theme = theme }, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Intentionally ignore persistence failures.
        }
    }

    private sealed class ThemeSettings
    {
        public AppTheme Theme { get; set; } = AppTheme.Dark;
    }
}