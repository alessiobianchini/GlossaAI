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

    public async Task<string> SummarizeAsync(string transcription, string promptSystem, IProgress<string>? onTokenGenerated = null, System.Threading.CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(_settings.SelectedModel) ? "gpt-4o" : _settings.SelectedModel;
        var apiUrl = string.IsNullOrWhiteSpace(_settings.ApiUrl) ? "https://api.openai.com/v1/chat/completions" : _settings.ApiUrl;

        _logger.LogInformation("Sending streaming request to OpenAI using model '{Model}'...", model);

        var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        }

        var payload = new OpenAiChatRequest
        {
            Model = model,
            Stream = true,
            Messages = new[]
            {
                new OpenAiChatMessage { Role = "system", Content = promptSystem },
                new OpenAiChatMessage { Role = "user", Content = transcription }
            }
        };

        request.Content = JsonContent.Create(payload);

        try
        {
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new System.IO.StreamReader(stream);
            var fullResponse = new System.Text.StringBuilder();

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                var data = line.Substring(6).Trim();
                if (data == "[DONE]") break;

                try
                {
                    var chunk = System.Text.Json.JsonSerializer.Deserialize<OpenAiStreamResponse>(data);
                    var content = chunk?.Choices?[0]?.Delta?.Content;
                    
                    if (!string.IsNullOrEmpty(content))
                    {
                        fullResponse.Append(content);
                        onTokenGenerated?.Report(content);
                    }
                }
                catch { /* Ignore parsing errors on partial streams */ }
            }

            return fullResponse.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call OpenAI server at '{ApiUrl}'.", apiUrl);
            throw new InvalidOperationException($"Could not communicate with OpenAI: {ex.Message}. Please verify your API key and settings configuration.", ex);
        }
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, IProgress<string>? onTokenGenerated = null, System.Threading.CancellationToken cancellationToken = default)
    {
        var promptSystem = $"You are a professional translator. Translate the user input into {targetLanguage}. Output ONLY the direct translation.";
        return await SummarizeAsync(text, promptSystem, onTokenGenerated, cancellationToken);
    }

    private class OpenAiChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

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

    private class OpenAiStreamResponse
    {
        [JsonPropertyName("choices")]
        public OpenAiStreamChoice[] Choices { get; set; } = Array.Empty<OpenAiStreamChoice>();
    }

    private class OpenAiStreamChoice
    {
        [JsonPropertyName("delta")]
        public OpenAiStreamDelta Delta { get; set; } = new();
    }

    private class OpenAiStreamDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
