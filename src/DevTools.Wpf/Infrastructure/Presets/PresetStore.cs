using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace DevTools.Wpf.Infrastructure.Presets;

public sealed class PresetStore<TPreset> where TPreset : class, IPresetNamed
{
    private readonly string filePath;
    private readonly JsonSerializerOptions serializerOptions;

    public PresetStore(string filePath, JsonSerializerOptions serializerOptions)
    {
        this.filePath = filePath;
        this.serializerOptions = serializerOptions;
    }

    public List<TPreset> LoadPresets()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new List<TPreset>();
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<TPreset>>(json, serializerOptions)?
                .Where(preset => !string.IsNullOrWhiteSpace(preset.Name))
                .ToList() ?? new List<TPreset>();
        }
        catch
        {
            return new List<TPreset>();
        }
    }

    public void SavePresets(IEnumerable<TPreset> presets)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(presets, serializerOptions);
        File.WriteAllText(filePath, json);
    }

    public void RefreshPresetNames(IEnumerable<TPreset> presets, ObservableCollection<string> targetNames)
    {
        targetNames.Clear();

        foreach (var name in presets
            .Select(preset => preset.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            targetNames.Add(name);
        }
    }

    public TPreset? FindPreset(IEnumerable<TPreset> presets, string name)
    {
        return presets.FirstOrDefault(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}