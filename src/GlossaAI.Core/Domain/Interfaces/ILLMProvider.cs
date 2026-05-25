namespace GlossaAI.Core.Domain.Interfaces;

/// <summary>
/// Abstraction for LLM orchestration services (summarization, translation, processing).
/// </summary>
public interface ILLMProvider
{
    /// <summary>
    /// Summarizes the given transcription using a custom system instructions prompt.
    /// </summary>
    /// <param name="transcription">The raw text transcription of the meeting.</param>
    /// <param name="promptSystem">System instructions defining the recap style, format, and structure.</param>
    /// <returns>The generated recap or summary.</returns>
    Task<string> SummarizeAsync(string transcription, string promptSystem, System.IProgress<string>? onTokenGenerated = null, System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Translates the given text into a target language.
    /// </summary>
    /// <param name="text">The source text to translate.</param>
    /// <param name="targetLanguage">Target language name or code.</param>
    /// <returns>The translated text.</returns>
    Task<string> TranslateAsync(string text, string targetLanguage, System.IProgress<string>? onTokenGenerated = null, System.Threading.CancellationToken cancellationToken = default);
}
