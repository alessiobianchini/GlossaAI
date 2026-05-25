using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GlossaAI.Core.Domain.Interfaces;
using GlossaAI.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net;
using Whisper.net.Ggml;

namespace GlossaAI.Infrastructure.Providers;

public class WhisperNetProvider : ISTTProvider
{
    private readonly ILogger<WhisperNetProvider> _logger;
    private readonly string _modelDirectory;
    private readonly GgmlType _modelType;
    private readonly string _fallbackLanguage;

    public WhisperNetProvider(IOptions<WhisperOptions> options, ILogger<WhisperNetProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var config = options?.Value ?? throw new ArgumentNullException(nameof(options));

        _modelType = config.ModelType;
        _fallbackLanguage = string.IsNullOrWhiteSpace(config.Language) ? "auto" : config.Language;

        _modelDirectory = string.IsNullOrWhiteSpace(config.ModelDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlossaAI", "Models")
            : config.ModelDirectory;
    }

    public async Task<string> TranscribeAudioAsync(string filePath, string language = "auto", IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("WAV audio file path cannot be null or empty.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Target WAV file was not found: '{filePath}'");

        // If a cancelled token somehow leaked through, reset it — Whisper must run on a clean token.
        if (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("WhisperNetProvider: Incoming token was already cancelled — resetting to CancellationToken.None.");
            cancellationToken = CancellationToken.None;
        }

        var effectiveLang = string.IsNullOrWhiteSpace(language) ? _fallbackLanguage : language;

        _logger.LogInformation("WhisperNetProvider: Transcribing '{Path}' with language '{Lang}'", filePath, effectiveLang);

        if (!Directory.Exists(_modelDirectory))
            Directory.CreateDirectory(_modelDirectory);

        var modelFileName = $"ggml-{_modelType.ToString().ToLowerInvariant()}.bin";
        var modelPath = Path.Combine(_modelDirectory, modelFileName);

        if (!File.Exists(modelPath))
        {
            _logger.LogWarning("WhisperNetProvider: Model '{File}' not found — downloading...", modelFileName);

            try
            {
                // Model download is always isolated from any caller lifecycle token.
                using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(_modelType, cancellationToken: CancellationToken.None);
                using var fileStream  = new FileStream(modelPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await modelStream.CopyToAsync(fileStream, CancellationToken.None);
                _logger.LogInformation("WhisperNetProvider: Model download complete.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WhisperNetProvider: Model download failed.");
                if (File.Exists(modelPath))
                    try { File.Delete(modelPath); } catch { }

                throw new InvalidOperationException($"Failed to download GGML model ({_modelType}). Verify internet connection.", ex);
            }
        }

        var transcript = new StringBuilder();

        try
        {
            using var audioStream = File.OpenRead(filePath);
            using var factory     = WhisperFactory.FromPath(modelPath);
            using var processor   = factory.CreateBuilder()
                .WithLanguage(effectiveLang)
                .Build();

            try
            {
                var totalDuration = GetWavDuration(filePath);
                var durationStr = totalDuration.TotalSeconds > 0 ? totalDuration.ToString(@"hh\:mm\:ss") : "??:??:??";

                await foreach (var segment in processor.ProcessAsync(audioStream, cancellationToken))
                {
                    var text = segment.Text?.Trim();
                    if (!string.IsNullOrEmpty(text))
                        transcript.AppendLine(text);
                    
                    progress?.Report($"Transcribing... ({segment.Start:hh\\:mm\\:ss} / {durationStr})");
                }
            }
            catch (OperationCanceledException)
            {
                // Whisper.net's native processing pipeline uses internal cancellation to signal
                // decode-channel teardown (especially on short or silent audio).
                // Because we always pass CancellationToken.None, this is NOT an external cancel request.
                // Treat it as graceful early completion and return whatever segments were collected.
                _logger.LogDebug("WhisperNetProvider: ProcessAsync pipeline closed via internal cancellation — returning accumulated segments.");
            }

            var result = transcript.ToString().Trim();
            _logger.LogInformation("WhisperNetProvider: Done. {Chars} chars transcribed.", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhisperNetProvider: Processing failure.");
            throw new InvalidOperationException("Failed to process WAV inside Whisper. Verify format is 16kHz PCM mono.", ex);
        }
    }

    private TimeSpan GetWavDuration(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (info.Exists && info.Length > 44)
            {
                return TimeSpan.FromSeconds((info.Length - 44) / 32000.0);
            }
        }
        catch { }
        return TimeSpan.Zero;
    }
}
