using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlossaAI.Core.Domain.Interfaces;
using GlossaAI.Core.Domain.Models;
using GlossaAI.Core.Exceptions;
using GlossaAI.Core.Services;
using Microsoft.Extensions.Logging;

namespace GlossaAI.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly MeetingManager      _meetingManager;
    private readonly IAudioEngine        _audioEngine;
    private readonly ILLMProviderFactory _llmProviderFactory;
    private readonly ProviderSettings    _providerSettings;
    private readonly GlossaAI.Core.Services.ConfigurationService _configService;
    private readonly HistoryService      _historyService;
    private readonly UpdateService       _updateService;
    private readonly ILogger<MainViewModel> _logger;

    // Navigation (0 = Recorder, 1 = History, 2 = Settings)
    [ObservableProperty] private int _selectedPageIndex = 0;

    [ObservableProperty] private string _recapText = "Select audio sources and start recording, or import a video file.";
    [ObservableProperty] private bool   _isRecording;
    [ObservableProperty] private bool   _isProcessing;
    [ObservableProperty] private string _statusText    = "Ready";
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private string? _lastFilePath;
    [ObservableProperty] private bool _lastWasVideo;

    [ObservableProperty] private double _micLevel;
    [ObservableProperty] private double _desktopLevel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    // Output content buckets
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportTranscriptionCommand))]
    private string _transcriptionText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportAiRecapCommand))]
    [NotifyCanExecuteChangedFor(nameof(TranslateRecapCommand))]
    private string _aiRecapText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportTranslationCommand))]
    private string _translationText = string.Empty;

    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private string _newVersionName = string.Empty;
    [ObservableProperty] private bool _isDownloadingUpdate;
    [ObservableProperty] private int _downloadProgress;
    private string _updateDownloadUrl = string.Empty;

    public SettingsViewModel Settings { get; }

    public ObservableCollection<AudioDevice> Microphones    { get; } = [];
    public ObservableCollection<AudioDevice> DesktopSources { get; } = [];
    public ObservableCollection<MeetingRecord> PastMeetings { get; } = [];

    [ObservableProperty] private AudioDevice? _selectedMicrophone;
    [ObservableProperty] private AudioDevice? _selectedDesktopSource;
    [ObservableProperty] private MeetingRecord? _selectedMeeting;

    public MainViewModel(
        MeetingManager meetingManager,
        IAudioEngine audioEngine,
        ILLMProviderFactory llmProviderFactory,
        ProviderSettings providerSettings,
        GlossaAI.Core.Services.ConfigurationService configService,
        HistoryService historyService,
        UpdateService updateService,
        ILogger<MainViewModel> logger)
    {
        _meetingManager     = meetingManager     ?? throw new ArgumentNullException(nameof(meetingManager));
        _audioEngine        = audioEngine        ?? throw new ArgumentNullException(nameof(audioEngine));
        _llmProviderFactory = llmProviderFactory ?? throw new ArgumentNullException(nameof(llmProviderFactory));
        _providerSettings   = providerSettings   ?? throw new ArgumentNullException(nameof(providerSettings));
        _configService      = configService      ?? throw new ArgumentNullException(nameof(configService));
        _historyService     = historyService     ?? throw new ArgumentNullException(nameof(historyService));
        _updateService      = updateService      ?? throw new ArgumentNullException(nameof(updateService));
        _logger             = logger             ?? throw new ArgumentNullException(nameof(logger));
        Settings = new SettingsViewModel(providerSettings, configService);

        _audioEngine.MicLevelChanged     += level => Dispatcher.UIThread.Post(() => MicLevel     = level * 100.0);
        _audioEngine.DesktopLevelChanged += level => Dispatcher.UIThread.Post(() => DesktopLevel = level * 100.0);
        _audioEngine.DevicesChanged      += () => Dispatcher.UIThread.Post(() => RefreshDevicesCommand.Execute(null));

        _ = LoadHistoryAsync();
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        var (available, newVersion, url) = await _updateService.CheckForUpdatesAsync();
        if (available)
        {
            Dispatcher.UIThread.Post(() =>
            {
                NewVersionName = newVersion;
                _updateDownloadUrl = url;
                IsUpdateAvailable = true;
            });
        }
    }

    [RelayCommand]
    private async Task StartUpdateAsync()
    {
        IsDownloadingUpdate = true;
        try
        {
            await _updateService.DownloadAndInstallUpdateAsync(_updateDownloadUrl, progress =>
            {
                Dispatcher.UIThread.Post(() => DownloadProgress = progress);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update failed.");
            IsDownloadingUpdate = false;
        }
    }

    [RelayCommand]
    private void SwitchTab(string indexStr)
    {
        if (int.TryParse(indexStr, out int index))
        {
            SelectedPageIndex = index;
            OnPropertyChanged(nameof(IsRecorderVisible));
            OnPropertyChanged(nameof(IsHistoryVisible));
            OnPropertyChanged(nameof(IsSettingsVisible));
            // Clear selections when switching to recorder
            if (index == 0)
            {
                SelectedMeeting = null;
                if (!IsRecording && !IsProcessing)
                {
                    TranscriptionText = "";
                    AiRecapText = "";
                    TranslationText = "";
                    RecapText = "Select audio sources and start recording, or import a video file.";
                }
            }
        }
    }

    public bool IsRecorderVisible => SelectedPageIndex == 0;
    public bool IsHistoryVisible => SelectedPageIndex == 1;
    public bool IsSettingsVisible => SelectedPageIndex == 2;

    private async Task LoadHistoryAsync()
    {
        var history = await _historyService.LoadHistoryAsync();
        Dispatcher.UIThread.Post(() =>
        {
            PastMeetings.Clear();
            foreach (var record in history.OrderByDescending(r => r.Date))
            {
                PastMeetings.Add(record);
            }
        });
    }

    private async Task SaveMeetingToHistoryAsync(string transcription, string recap)
    {
        var title = "Meeting " + DateTime.Now.ToString("g");
        
        // Attempt to extract a better title from the recap
        var lines = recap.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0 && lines[0].StartsWith("# "))
        {
            title = lines[0].Substring(2).Trim();
        }
        else if (lines.Length > 0 && lines[0].StartsWith("**Executive Summary**"))
        {
            title = "Executive Summary " + DateTime.Now.ToString("g");
        }

        var record = new MeetingRecord
        {
            Date = DateTime.Now,
            Title = title,
            TranscriptionText = transcription,
            AiRecapText = recap
        };

        Dispatcher.UIThread.Post(() =>
        {
            PastMeetings.Insert(0, record);
            SelectedMeeting = record;
        });

        // Save to disk
        var currentList = PastMeetings.ToList();
        await _historyService.SaveHistoryAsync(currentList);
    }

    partial void OnSelectedMeetingChanged(MeetingRecord? value)
    {
        if (value != null)
        {
            TranscriptionText = value.TranscriptionText;
            AiRecapText = value.AiRecapText;
            RecapText = value.AiRecapText;
            TranslationText = string.Empty;
        }
    }

    // ── Recording ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadDevicesAsync()
    {
        await RefreshDevicesAsync();
    }

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        StatusText = "Refreshing audio devices...";
        var previousMicId = SelectedMicrophone?.Id;
        var previousDesktopId = SelectedDesktopSource?.Id;

        try
        {
            var devices = await _audioEngine.GetAvailableDevicesAsync();
            Microphones.Clear();
            DesktopSources.Clear();

            foreach (var device in devices)
            {
                if (device.Type == DeviceType.Microphone)           
                    Microphones.Add(device);
                else if (device.Type == DeviceType.DesktopLoopback) 
                    DesktopSources.Add(device);
            }

            // Restore microphone selection or fallback
            AudioDevice? matchedMic = null;
            if (!string.IsNullOrEmpty(previousMicId))
            {
                foreach (var m in Microphones)
                {
                    if (m.Id == previousMicId)
                    {
                        matchedMic = m;
                        break;
                    }
                }
            }
            SelectedMicrophone = matchedMic ?? (Microphones.Count > 0 ? Microphones[0] : null);

            // Restore desktop loopback selection or fallback
            AudioDevice? matchedDesktop = null;
            if (!string.IsNullOrEmpty(previousDesktopId))
            {
                foreach (var d in DesktopSources)
                {
                    if (d.Id == previousDesktopId)
                    {
                        matchedDesktop = d;
                        break;
                    }
                }
            }
            SelectedDesktopSource = matchedDesktop ?? (DesktopSources.Count > 0 ? DesktopSources[0] : null);

            StatusText = "Audio devices loaded.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh audio devices.");
            StatusText    = "Error loading audio devices.";
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task StartRecordingAsync()
    {
        if (IsRecording || IsProcessing) return;

        RecordingConfig? config = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } parent)
        {
            if (!parent.IsVisible || parent.WindowState == Avalonia.Controls.WindowState.Minimized)
            {
                parent.Show();
                parent.WindowState = Avalonia.Controls.WindowState.Normal;
                parent.Activate();
            }

            var dialogViewModel = new RecordSetupViewModel(_providerSettings);
            var dialog = new Views.RecordSetupDialog { DataContext = dialogViewModel };
            config = await dialog.ShowDialog<RecordingConfig?>(parent);
            if (config == null)
            {
                return;
            }

            var matchInput = dialogViewModel.AvailableLanguages.FirstOrDefault(l => l.Code.Equals(config.InputLanguage, StringComparison.OrdinalIgnoreCase));
            var matchOutput = dialogViewModel.AvailableLanguages.FirstOrDefault(l => l.Code.Equals(config.OutputLanguage, StringComparison.OrdinalIgnoreCase));
            var outputLanguageName = matchOutput?.Name ?? "English";

            _meetingManager.UpdateRecordingConfig(config.SelectedModel, config.InputLanguage, outputLanguageName, config.Context);

            Settings.SelectedModel = config.SelectedModel;
            if (matchInput != null)
                Settings.RecLang = matchInput;
            if (matchOutput != null)
                Settings.OutLang = matchOutput;

            _ = _configService.SaveSettingsAsync(_providerSettings);
        }

        var tempFile = Path.Combine(Path.GetTempPath(), "GlossaAI_Meeting_Capture.wav");
        try
        {
            _audioEngine.StartRecording(SelectedMicrophone, SelectedDesktopSource, tempFile);
            IsRecording   = _audioEngine.IsRecording;
            StatusText    = "Recording audio...";
            StatusMessage = string.Empty;
            RecapText     = "Listening... Click 'Stop & Process' when done.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start recording.");
            StatusText    = "Unable to start recording.";
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        if (!IsRecording || IsProcessing) return;
        StatusText    = "Processing...";
        IsProcessing  = true;
        StatusMessage = string.Empty;
        ErrorMessage  = string.Empty;
        try
        {
            _audioEngine.StopRecording();
            IsRecording = _audioEngine.IsRecording;

            var file = Path.Combine(Path.GetTempPath(), "GlossaAI_Meeting_Capture.wav");
            LastFilePath = file;
            LastWasVideo = false;

            var progress = new Progress<string>(s => StatusText = s);
            
            AiRecapText = string.Empty;
            RecapText = string.Empty;
            var recapProgress = new Progress<string>(s => 
            {
                AiRecapText += s;
                RecapText = AiRecapText
                    .Replace("<think>", "🧠 RAGIONAMENTO IN CORSO...\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n")
                    .Replace("</think>", "\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n🎯 RISPOSTA FINALE:\n");
            });

            var (transcription, recap) = await _meetingManager.ProcessMeetingWithTranscriptAsync(file, progress, recapProgress, System.Threading.CancellationToken.None);

            TranscriptionText = transcription;
            AiRecapText       = recap;
            RecapText         = recap;
            StatusText        = "Processing completed.";

            await SaveMeetingToHistoryAsync(transcription, recap);
        }
        catch (NoSpeechException)
        {
            const string msg = "### Transcription Error\nNo speech could be extracted or transcribed from the audio file.";
            ErrorMessage = msg;
            RecapText    = msg;
            StatusText   = "No speech detected.";
            StatusMessage = "No speech detected. You can try again if you think this is an error.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during stop/process.");
            StatusText    = "Error during processing.";
            StatusMessage = ex.Message;
        }
        finally { IsProcessing = false; }
    }

    [RelayCommand]
    private async Task RetryProcessAsync()
    {
        if (string.IsNullOrEmpty(LastFilePath)) return;
        
        IsProcessing = true;
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        RecapText = "Retrying process...";

        var progress = new Progress<string>(s => StatusText = s);
        
        AiRecapText = string.Empty;
        RecapText = string.Empty;
        var recapProgress = new Progress<string>(s => 
        {
            AiRecapText += s;
            RecapText = AiRecapText
                .Replace("<think>", "🧠 RAGIONAMENTO IN CORSO...\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n")
                .Replace("</think>", "\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n🎯 RISPOSTA FINALE:\n");
        });

        try
        {
            (string transcription, string recap) result;
            if (LastWasVideo)
            {
                result = await _meetingManager.ProcessVideoMeetingWithTranscriptAsync(LastFilePath, progress, recapProgress, System.Threading.CancellationToken.None);
            }
            else
            {
                result = await _meetingManager.ProcessMeetingWithTranscriptAsync(LastFilePath, progress, recapProgress, System.Threading.CancellationToken.None);
            }

            TranscriptionText = result.transcription;
            AiRecapText       = result.recap;
            RecapText         = result.recap;
            StatusText        = "Processing completed.";

            await SaveMeetingToHistoryAsync(result.transcription, result.recap);
        }
        catch (NoSpeechException)
        {
            const string msg = "### Transcription Error\nNo speech could be extracted or transcribed from the audio file.";
            ErrorMessage = msg;
            RecapText    = msg;
            StatusText   = "No speech detected.";
            StatusMessage = "No speech detected. You can try again if you think this is an error.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during retry process.");
            StatusText    = "Error during processing.";
            StatusMessage = ex.Message;
        }
        finally { IsProcessing = false; }
    }

    [RelayCommand]
    private async Task SelectAndProcessVideoAsync()
    {
        if (IsProcessing || IsRecording) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null) return;

        StatusText = "Selecting video file...";
        try
        {
            var result = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Meeting Media",
                AllowMultiple = false,
                FileTypeFilter = [
                    new FilePickerFileType("Media Files") { Patterns = ["*.mp4", "*.mkv", "*.mov", "*.avi", "*.mp3", "*.wav", "*.m4a", "*.flac", "*.ogg"] },
                    new FilePickerFileType("Video Files") { Patterns = ["*.mp4", "*.mkv", "*.mov", "*.avi"] },
                    new FilePickerFileType("Audio Files") { Patterns = ["*.mp3", "*.wav", "*.m4a", "*.flac", "*.ogg"] }
                ]
            });

            if (result.Count == 0) { StatusText = "No file selected."; return; }

            IsProcessing  = true;
            StatusMessage = string.Empty;
            ErrorMessage  = string.Empty;
            RecapText     = "Video loaded. Extracting audio...";
            
            LastFilePath = result[0].Path.LocalPath;
            LastWasVideo = true;

            var progress = new Progress<string>(s => StatusText = s);
            
            AiRecapText = string.Empty;
            RecapText = string.Empty;
            var recapProgress = new Progress<string>(s => 
            {
                AiRecapText += s;
                RecapText = AiRecapText
                    .Replace("<think>", "🧠 RAGIONAMENTO IN CORSO...\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n")
                    .Replace("</think>", "\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n🎯 RISPOSTA FINALE:\n");
            });

            var (transcription, recap) = await _meetingManager.ProcessVideoMeetingWithTranscriptAsync(result[0].Path.LocalPath, progress, recapProgress, System.Threading.CancellationToken.None);

            TranscriptionText = transcription;
            AiRecapText       = recap;
            RecapText         = recap;
            StatusText        = "Processing complete.";

            await SaveMeetingToHistoryAsync(transcription, recap);
        }
        catch (NoSpeechException)
        {
            const string msg = "### Transcription Error\nNo speech could be extracted or transcribed from the audio file.";
            ErrorMessage = msg;
            RecapText    = msg;
            StatusText   = "No speech detected.";
            StatusMessage = "No speech detected. You can try again if you think this is an error.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Video processing pipeline error.");
            StatusText    = "Error during processing.";
            StatusMessage = ex.Message;
            RecapText     = $"Could not complete video recap:\n{ex.Message}";
        }
        finally { IsProcessing = false; }
    }

    [RelayCommand]
    private async Task DeleteMeetingAsync(MeetingRecord record)
    {
        if (record == null) return;
        PastMeetings.Remove(record);
        if (SelectedMeeting == record)
        {
            SelectedMeeting = PastMeetings.FirstOrDefault();
        }
        await _historyService.SaveHistoryAsync(PastMeetings.ToList());
    }

    // ── Translation ─────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(HasAiRecap))]
    private async Task TranslateRecapAsync()
    {
        StatusText    = "Translating...";
        IsProcessing  = true;
        StatusMessage = string.Empty;
        try
        {
            var provider = _llmProviderFactory.GetProvider(_providerSettings.SelectedProvider);
            TranslationText = await provider.TranslateAsync(AiRecapText, Settings.OutLang.Name);
            StatusText = "Translation complete.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation failed.");
            StatusText    = "Translation error.";
            StatusMessage = ex.Message;
        }
        finally { IsProcessing = false; }
    }

    // ── Export ──────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(HasTranscription))]
    private async Task ExportTranscriptionAsync()
        => await SaveFileAsync("Export Transcription", "transcription", "txt", TranscriptionText);

    [RelayCommand(CanExecute = nameof(HasAiRecap))]
    private async Task ExportAiRecapAsync()
        => await SaveFileAsync("Export AI Recap", "recap", "md", AiRecapText);

    [RelayCommand(CanExecute = nameof(HasTranslation))]
    private async Task ExportTranslationAsync()
        => await SaveFileAsync("Export Translation", "translation", "md", TranslationText);

    private async Task SaveFileAsync(string title, string suggestedName, string extension, string content)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null) return;

        var file = await desktop.MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title               = title,
            SuggestedFileName   = $"GlossaAI_{suggestedName}_{DateTime.Now:yyyyMMdd_HHmm}.{extension}",
            DefaultExtension    = extension,
            SuggestedStartLocation = await desktop.MainWindow.StorageProvider
                .TryGetFolderFromPathAsync(Settings.ExportFolderPath),
            FileTypeChoices =
            [
                new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = [$"*.{extension}"] }
            ]
        });

        if (file is null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(content);
            StatusText = $"Saved to {file.Name}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File export failed.");
            StatusMessage = ex.Message;
        }
    }

    // ── CanExecute guards ───────────────────────────────────────────────────────

    private bool HasTranscription => !string.IsNullOrWhiteSpace(TranscriptionText);
    private bool HasAiRecap       => !string.IsNullOrWhiteSpace(AiRecapText);
    private bool HasTranslation   => !string.IsNullOrWhiteSpace(TranslationText);
}
