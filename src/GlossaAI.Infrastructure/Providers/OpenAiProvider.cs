using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using GlossaAI.Core.Domain.Interfaces;
using GlossaAI.Core.Domain.Models;
using Microsoft.Extensions.Logging;

namespace GlossaAI.Infrastructure.Providers;

public class OpenAiProvider : ILLMProvider
{
    private readonly HttpClient _httpClient;
    private readonly ProviderSettings _settings;
    private readonly ILogger<OpenAiProvider> _logger;

    public OpenAiProvider(HttpClient httpClient, ProviderSettings settings, ILogger<OpenAiProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> SummarizeAsync(string transcription, string promptSystem)
    {
        var model = string.IsNullOrWhiteSpace(_settings.SelectedModel) ? "gpt-4o" : _settings.SelectedModel;
        var apiUrl = string.IsNullOrWhiteSpace(_settings.ApiUrl) ? "https://api.openai.com/v1/chat/completions" : _settings.ApiUrl;

        _logger.LogInformation("Sending request to OpenAI using model '{Model}'...", model);

        var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        }

        var payload = new OpenAiChatRequest
        {
            Model = model,
            Messages = new[]
            {
                new OpenAiChatMessage { Role = "system", Content = promptSystem },
                new OpenAiChatMessage { Role = "user", Content = transcription }
            }
        };

        request.Content = JsonContent.Create(payload);

        try
        {
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>();
            var responseText = result?.Choices?[0]?.Message?.Content;

            if (string.IsNullOrEmpty(responseText))
            {
                _logger.LogWarning("OpenAI returned an empty response.");
                return string.Empty;
            }

            return responseText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call OpenAI server at '{ApiUrl}'.", apiUrl);
            return $"### Error generating recap via OpenAI\nCould not communicate with OpenAI: {ex.Message}\n\n*Please verify your API key and settings configuration.*";
        }
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage)
    {
        var promptSystem = $"You are a professional translator. Translate the user input into {targetLanguage}. Output ONLY the direct translation.";
        return await SummarizeAsync(text, promptSystem);
    }

    private class OpenAiChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public OpenAiChatMessage[] Messages { get; set; } = Array.Empty<OpenAiChatMessage>();
    }

    private class OpenAiChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private class OpenAiChatResponse
    {
        [JsonPropertyName("choices")]
        public OpenAiChatChoice[] Choices { get; set; } = Array.Empty<OpenAiChatChoice>();
    }

    private class OpenAiChatChoice
    {
        [JsonPropertyName("message")]
        public OpenAiChatMessage Message { get; set; } = new();
    }
}
