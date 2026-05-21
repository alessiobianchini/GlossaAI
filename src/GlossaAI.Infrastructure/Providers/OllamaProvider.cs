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
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }

    public async Task<string> SummarizeAsync(string transcription, string promptSystem)
    {
        _logger.LogInformation("OllamaProvider: Summarizing with model '{Model}'...", _settings.SelectedModel);

        var payload = new OllamaRequest
        {
            Model = _settings.SelectedModel,
            Prompt = transcription,
            System = promptSystem,
            Stream = false
        };

        try
        {
            var targetUri = new Uri(new Uri(_settings.ApiUrl), "/api/generate");
            var response = await _httpClient.PostAsJsonAsync(targetUri, payload);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();

            if (result == null || string.IsNullOrEmpty(result.Response))
            {
                _logger.LogWarning("OllamaProvider: Empty response received.");
                return string.Empty;
            }

            return result.Response;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "OllamaProvider: Request timed out.");
            return $"### Error generating recap\nCould not communicate with Ollama: The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.\n\n*Please ensure Ollama is running and has model '{_settings.SelectedModel}' loaded.*";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OllamaProvider: Request failed.");
            return $"### Error generating recap\nCould not communicate with Ollama: {ex.Message}\n\n*Please ensure Ollama is running and has model '{_settings.SelectedModel}' loaded.*";
        }
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage)
    {
        var prompt = $"You are a professional translator. Translate the user input into {targetLanguage}. Output ONLY the direct translation.";
        return await SummarizeAsync(text, prompt);
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
