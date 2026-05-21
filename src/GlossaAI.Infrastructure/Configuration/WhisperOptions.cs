using Whisper.net.Ggml;

namespace GlossaAI.Infrastructure.Configuration;

/// <summary>
/// Configuration options for local Whisper.net execution.
/// </summary>
public class WhisperOptions
{
    /// <summary>
    /// Directory path where downloaded Whisper GGML model binaries are cached.
    /// </summary>
    public string ModelDirectory { get; set; } = string.Empty;

    /// <summary>
    /// The size classification of the model (e.g. Base, Tiny, Small) to fetch and execute.
    /// </summary>
    public GgmlType ModelType { get; set; } = GgmlType.Base;

    /// <summary>
    /// The language tag to configure for transcription (e.g., "auto" for auto-detection, "it" for Italian).
    /// </summary>
    public string Language { get; set; } = "auto";
}
