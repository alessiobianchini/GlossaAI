namespace GlossaAI.Core.Exceptions;

public sealed class NoSpeechException(string audioSource)
    : Exception($"No speech detected in the {audioSource}. The audio track may be silent or in an unsupported format.")
{
    public string AudioSource { get; } = audioSource;
}
