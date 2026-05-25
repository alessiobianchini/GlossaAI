using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GlossaAI.Core.Domain.Interfaces;
using GlossaAI.Core.Domain.Models;
using Microsoft.Extensions.Logging;

namespace GlossaAI.Infrastructure.Providers;

public class OllamaProvider : ILLMProvider
{
    private readonly HttpClient _httpClient;
    private readonly ProviderSettings _settings;
    private readonly ILogger<OllamaProvider> _logger;

    public OllamaProvider(HttpClient httpClient, ProviderSettings settings, ILogger<OllamaProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> SummarizeAsync(string transcription, string promptSystem, IProgress<string>? onTokenGenerated = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("OllamaProvider: Summarizing with model '{Model}' (Streaming)...", _settings.SelectedModel);

        var payload = new OllamaRequest
        {
            Model = _settings.SelectedModel,
            Prompt = transcription,
            System = promptSystem,
            Stream = true
        };

        try
        {
            var targetUri = new Uri(new Uri(_settings.ApiUrl), "/api/generate");
            var request = new HttpRequestMessage(HttpMethod.Post, targetUri) { Content = JsonContent.Create(payload) };
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new System.IO.StreamReader(stream);
            var fullResponse = new System.Text.StringBuilder();

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var chunk = System.Text.Json.JsonSerializer.Deserialize<OllamaResponse>(line);
                    if (chunk != null && !string.IsNullOrEmpty(chunk.Response))
                    {
                        fullResponse.Append(chunk.Response);
                        onTokenGenerated?.Report(chunk.Response);
                    }
                }
                catch { /* Ignore parsing errors on partial streams */ }
            }

            return fullResponse.ToString();
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "OllamaProvider: Request timed out or cancelled.");
            return $"### Error generating recap\nCould not communicate with Ollama: The request was canceled.\n\n*Please ensure Ollama is running and has model '{_settings.SelectedModel}' loaded.*";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OllamaProvider: Request failed.");
            return $"### Error generating recap\nCould not communicate with Ollama: {ex.Message}\n\n*Please ensure Ollama is running and has model '{_settings.SelectedModel}' loaded.*";
        }
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, IProgress<string>? onTokenGenerated = null, CancellationToken cancellationToken = default)
    {
        var prompt = $"You are a professional translator. Translate the user input into {targetLanguage}. Output ONLY the direct translation.";
        return await SummarizeAsync(text, prompt, onTokenGenerated, cancellationToken);
    }

    private class OllamaRequest
    {
        [JsonPropertyName("model")]  public string Model  { get; set; } = string.Empty;
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = string.Empty;
        [JsonPropertyName("system")] public string System { get; set; } = string.Empty;
        [JsonPropertyName("stream")] public bool   Stream { get; set; }
    }

    private class OllamaResponse
    {
        [JsonPropertyName("response")] public string Response { get; set; } = string.Empty;
        [JsonPropertyName("done")]     public bool   Done     { get; set; }
    }
}
