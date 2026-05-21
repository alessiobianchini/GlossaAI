using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using GlossaAI.Core.Domain.Models;

namespace GlossaAI.Core.Services;

public class ConfigurationService
{
    private readonly string _filePath;

    public ConfigurationService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "GlossaAI");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
    }

    public async Task SaveSettingsAsync(ProviderSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch
        {
            // Suppress errors during background saving to ensure application remains stable
        }
    }

    public async Task<ProviderSettings> LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath);
                var settings = JsonSerializer.Deserialize<ProviderSettings>(json);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // Suppress errors and fallback to default configuration
        }
        return new ProviderSettings();
    }
}
