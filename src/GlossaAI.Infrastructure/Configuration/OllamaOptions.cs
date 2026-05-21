namespace GlossaAI.Infrastructure.Configuration;

/// <summary>
/// Settings for the local Ollama provider.
/// </summary>
public class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>
    /// Base URL of the Ollama server (e.g., http://localhost:11434).
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Model name to use (e.g., llama3, mistral, phi3).
    /// </summary>
    public string Model { get; set; } = "llama3";
}
