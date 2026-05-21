using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlossaAI.Core.Domain.Models;
using GlossaAI.Core.Services;

namespace GlossaAI.Desktop.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ProviderSettings _settings;
    private readonly ConfigurationService _configService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsApiKeyRequired))]
    private AiProviderType _selectedProvider;

    public bool IsApiKeyRequired => SelectedProvider == AiProviderType.OpenAI;

    [ObservableProperty] private string  _apiUrl           = "http://127.0.0.1:11434";
    [ObservableProperty] private string  _apiKey           = string.Empty;
    [ObservableProperty] private string  _selectedModel    = "qwen3:8b";
    [ObservableProperty] private string  _exportFolderPath = AppContext.BaseDirectory;
    [ObservableProperty] private AppLang _recLang;
    [ObservableProperty] private AppLang _outLang;

    public ObservableCollection<AiProviderType> AvailableProviders { get; } = new(Enum.GetValues<AiProviderType>());

    public ObservableCollection<string> AvailableModels { get; } =
    [
        "qwen3:8b",
        "llama3.2:3b",
        "qwen2.5-coder:14b",
        "qwen2.5-coder:7b"
    ];

    public ObservableCollection<AppLang> AvailableLangs { get; } =
    [
        new AppLang("en", "English"),
        new AppLang("it", "Italian"),
        new AppLang("es", "Spanish"),
        new AppLang("fr", "French"),
        new AppLang("de", "German")
    ];

    public SettingsViewModel(ProviderSettings settings, ConfigurationService configService)
    {
        _settings      = settings ?? throw new ArgumentNullException(nameof(settings));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _selectedProvider = _settings.SelectedProvider;
        _apiUrl        = string.IsNullOrWhiteSpace(_settings.ApiUrl)       ? "http://127.0.0.1:11434" : _settings.ApiUrl;
        _apiKey        = _settings.ApiKey;
        _selectedModel = string.IsNullOrWhiteSpace(_settings.SelectedModel) ? "qwen3:8b" : _settings.SelectedModel;
        _exportFolderPath = string.IsNullOrWhiteSpace(_settings.ExportFolderPath) ? AppContext.BaseDirectory : _settings.ExportFolderPath;
        _recLang       = AvailableLangs.FirstOrDefault(l => l.Code.Equals(_settings.RecLangCode, StringComparison.OrdinalIgnoreCase)) ?? AvailableLangs[1];
        _outLang       = AvailableLangs.FirstOrDefault(l => l.Name.Equals(_settings.OutLangName, StringComparison.OrdinalIgnoreCase)) ?? AvailableLangs[1];
    }

    private void SaveSettings()
    {
        _ = _configService.SaveSettingsAsync(_settings);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SelectFolderAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null) return;

        var folders = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title         = "Select Export Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
            ExportFolderPath = folders[0].Path.LocalPath;
    }

    partial void OnSelectedProviderChanged(AiProviderType value)
    {
        _settings.SelectedProvider = value;
        if (value == AiProviderType.Ollama && (string.IsNullOrWhiteSpace(ApiUrl) || ApiUrl.Contains("openai.com")))
        {
            ApiUrl = "http://127.0.0.1:11434";
            SelectedModel = "qwen3:8b";
        }
        else if (value == AiProviderType.OpenAI && (string.IsNullOrWhiteSpace(ApiUrl) || ApiUrl.Contains("local-server") || ApiUrl.Contains("localhost")))
        {
            ApiUrl = "https://api.openai.com/v1/chat/completions";
            SelectedModel = "gpt-4o";
        }
        SaveSettings();
    }

    partial void OnApiUrlChanged(string value)
    {
        _settings.ApiUrl = value;
        SaveSettings();
    }

    partial void OnApiKeyChanged(string value)
    {
        _settings.ApiKey = value;
        SaveSettings();
    }

    partial void OnSelectedModelChanged(string value)
    {
        _settings.SelectedModel = value;
        SaveSettings();
    }

    partial void OnRecLangChanged(AppLang value)
    {
        _settings.RecLangCode = value?.Code ?? "en";
        SaveSettings();
    }

    partial void OnOutLangChanged(AppLang value)
    {
        _settings.OutLangName = value?.Name ?? "English";
        SaveSettings();
    }

    partial void OnExportFolderPathChanged(string value)
    {
        _settings.ExportFolderPath = value;
        SaveSettings();
    }
}
