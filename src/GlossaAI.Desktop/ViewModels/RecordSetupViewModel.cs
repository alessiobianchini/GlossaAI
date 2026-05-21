using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlossaAI.Core.Domain.Models;

namespace GlossaAI.Desktop.ViewModels;

public record ContextOption(MeetingContext Value, string DisplayName);

public partial class RecordSetupViewModel : ViewModelBase
{
    [ObservableProperty] private string _selectedModel = "qwen3:8b";
    [ObservableProperty] private string _inputLanguage = "it";
    [ObservableProperty] private string _outputLanguage = "it";
    [ObservableProperty] private MeetingContext _selectedContext = MeetingContext.General;

    public ObservableCollection<string> AvailableModels { get; } =
    [
        "qwen3:8b",
        "llama3.2:3b",
        "qwen2.5-coder:14b",
        "qwen2.5-coder:7b"
    ];

    public ObservableCollection<AppLang> AvailableLanguages { get; } =
    [
        new AppLang("en", "English"),
        new AppLang("it", "Italian"),
        new AppLang("es", "Spanish"),
        new AppLang("fr", "French"),
        new AppLang("de", "German")
    ];

    public ObservableCollection<ContextOption> AvailableContexts { get; } =
    [
        new ContextOption(MeetingContext.General, "General/Standard"),
        new ContextOption(MeetingContext.Friends, "Friends/Informal"),
        new ContextOption(MeetingContext.Medical, "Medical/Healthcare"),
        new ContextOption(MeetingContext.Developer, "Software Development"),
        new ContextOption(MeetingContext.BusinessAnalysis, "Business Analysis")
    ];

    public RecordSetupViewModel()
    {
    }

    public RecordSetupViewModel(ProviderSettings settings)
    {
        if (settings != null)
        {
            if (!string.IsNullOrEmpty(settings.SelectedModel))
                SelectedModel = settings.SelectedModel;
            if (!string.IsNullOrEmpty(settings.RecLangCode))
                InputLanguage = settings.RecLangCode;
            
            var match = AvailableLanguages.FirstOrDefault(l => l.Code.Equals(settings.OutLangName, StringComparison.OrdinalIgnoreCase) || l.Name.Equals(settings.OutLangName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                OutputLanguage = match.Code;

            SelectedContext = settings.SelectedContext;
        }
    }

    [RelayCommand]
    private void Confirm(object? windowObj)
    {
        if (windowObj is Window window)
        {
            window.Close(new RecordingConfig(SelectedModel, InputLanguage, OutputLanguage, SelectedContext));
        }
    }

    [RelayCommand]
    private void Cancel(object? windowObj)
    {
        if (windowObj is Window window)
        {
            window.Close(null);
        }
    }
}
