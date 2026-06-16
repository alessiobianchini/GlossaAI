using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GlossaAI.Core.Domain.Interfaces;
using GlossaAI.Core.Domain.Models;
using GlossaAI.Core.Exceptions;

namespace GlossaAI.Core.Services;

public class MeetingManager(
    ISTTProvider sttProvider,
    ILLMProviderFactory llmProviderFactory,
    IVideoProcessor videoProcessor,
    ProviderSettings settings)
{
    private readonly ISTTProvider        _sttProvider        = sttProvider        ?? throw new ArgumentNullException(nameof(sttProvider));
    private readonly ILLMProviderFactory _llmProviderFactory = llmProviderFactory ?? throw new ArgumentNullException(nameof(llmProviderFactory));
    private readonly IVideoProcessor     _videoProcessor     = videoProcessor     ?? throw new ArgumentNullException(nameof(videoProcessor));
    private readonly ProviderSettings    _settings           = settings           ?? throw new ArgumentNullException(nameof(settings));

    public MeetingContext CurrentContext
    {
        get => _settings.SelectedContext;
        set => _settings.SelectedContext = value;
    }

    public void UpdateRecordingConfig(string selectedModel, string inputLanguageCode, string outputLanguageName, MeetingContext context)
    {
        _settings.SelectedModel = selectedModel;
        _settings.RecLangCode = inputLanguageCode;
        _settings.OutLangName = outputLanguageName;
        CurrentContext = context;
    }

    private string GetContextInstruction() => CurrentContext switch
    {
        MeetingContext.Friends => "Tone: Informal, casual. Focus on jokes, plans, personal stories.",
        MeetingContext.Medical => "Tone: Strict, clinical, highly technical. Extract patient symptoms, diagnoses, prescriptions, treatments, and medical terminology with absolute precision.",
        MeetingContext.Developer => "Tone: Technical, clear. Extract architecture patterns, code snippets, git branches, bugs, APIs, and sprint tasks.",
        MeetingContext.BusinessAnalysis => "Tone: Executive, analytical. Extract KPIs, requirements, deadlines, risk factors, and action item owners.",
        _ => string.Empty
    };

    private string SystemPrompt
    {
        get
        {
            var contextInstruction = GetContextInstruction();
            var contextPart = string.IsNullOrEmpty(contextInstruction) ? "" : $"\n\n{contextInstruction}";
            return $"""
                You are an expert executive secretary and software architect assistant.
                Analyze the meeting transcription below and generate a professional, structured recap in {_settings.OutLangName}.{contextPart}
                Since the raw transcription lacks speaker tags, try to reconstruct the dialogue by inferring different speakers based on context and conversational turn-taking (e.g. Speaker A, Speaker B).

                The recap MUST include:
                1. **Executive Summary**: A brief paragraph summarizing the main topic and goals.
                2. **Dialogue Reconstruction**: A brief summary of who said what, guessing the speakers from the flow of conversation.
                3. **Key Discussion Points**: Bullet points detailing the topics explored.
                4. **Decisions Made**: A list of agreed-upon items.
                5. **Action Items**: Clear tasks assigned to owners (if mentioned) with deadlines.

                Format the output clearly using clean Markdown.
                """;
        }
    }

    private ILLMProvider ActiveLlm => _llmProviderFactory.GetProvider(_settings.SelectedProvider);

    public async Task<string> ProcessMeetingAsync(string audioPath, IProgress<string>? progress = null, IProgress<string>? recapProgress = null, CancellationToken cancellationToken = default)
    {
        var (_, recap) = await ProcessMeetingWithTranscriptAsync(audioPath, progress, recapProgress, cancellationToken);
        return recap;
    }

    public async Task<(string Transcription, string Recap)> ProcessMeetingWithTranscriptAsync(string audioPath, IProgress<string>? progress = null, IProgress<string>? recapProgress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(audioPath))
            throw new ArgumentException("Audio file path cannot be null or empty.", nameof(audioPath));

        // Whisper runs independently of the recording lifecycle — always use None here
        // so that a recording shutdown cancellation never bleeds into transcription.
        progress?.Report("Initializing transcription...");
        var transcription = await _sttProvider.TranscribeAudioAsync(audioPath, _settings.RecLangCode, progress, CancellationToken.None);

        if (string.IsNullOrWhiteSpace(transcription))
        {
            // Only honour the outer token at this decision boundary
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(audioPath);
            if (!fileInfo.Exists || fileInfo.Length <= 44)
                throw new InvalidOperationException("Audio recording file is empty or invalid.");

            throw new NoSpeechException("audio recording");
        }

        progress?.Report("Analyzing transcription and generating summary...");
        var recap = await ActiveLlm.SummarizeAsync(transcription, SystemPrompt, recapProgress, cancellationToken);
        return (transcription, recap);
    }

    public async Task<string> ProcessVideoMeetingAsync(string videoPath, IProgress<string>? progress = null, IProgress<string>? recapProgress = null, CancellationToken cancellationToken = default)
    {
        var (_, recap) = await ProcessVideoMeetingWithTranscriptAsync(videoPath, progress, recapProgress, cancellationToken);
        return recap;
    }

    public async Task<(string Transcription, string Recap)> ProcessVideoMeetingWithTranscriptAsync(
        string videoPath, IProgress<string>? progress = null, IProgress<string>? recapProgress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            throw new ArgumentException("Video file path cannot be null or empty.", nameof(videoPath));

        if (!File.Exists(videoPath))
            throw new FileNotFoundException($"Source video file not found: '{videoPath}'");

        var tempAudioPath = Path.Combine(Path.GetTempPath(), $"GlossaAI_Video_Temp_{Guid.NewGuid():N}.wav");

        try
        {
            progress?.Report("Extracting audio stream...");
            await _videoProcessor.ExtractAudioAsync(videoPath, tempAudioPath);

            progress?.Report("Transcribing voice activity...");

            // Whisper is isolated from the caller's token — pass None explicitly.
            var transcription = await _sttProvider.TranscribeAudioAsync(tempAudioPath, _settings.RecLangCode, progress, CancellationToken.None);

            if (string.IsNullOrWhiteSpace(transcription))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileInfo = new FileInfo(tempAudioPath);
                if (!fileInfo.Exists || fileInfo.Length <= 44)
                    throw new InvalidOperationException("Extracted audio file is empty or invalid.");

                throw new NoSpeechException("video file");
            }

            progress?.Report("Generating summary report...");
            var recap = await ActiveLlm.SummarizeAsync(transcription, SystemPrompt, recapProgress, cancellationToken);
            return (transcription, recap);
        }
        finally
        {
            if (File.Exists(tempAudioPath))
                try { File.Delete(tempAudioPath); } catch { }
        }
    }
}
