using System;
using GlossaAI.Core.Domain.Interfaces;
using GlossaAI.Core.Domain.Models;
using Microsoft.Extensions.DependencyInjection;

namespace GlossaAI.Infrastructure.Providers;

public class LlmProviderFactory(IServiceProvider serviceProvider) : ILLMProviderFactory
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    public ILLMProvider GetProvider(AiProviderType providerType)
    {
        return providerType switch
        {
            AiProviderType.Ollama => _serviceProvider.GetRequiredService<OllamaProvider>(),
            AiProviderType.OpenAI => _serviceProvider.GetRequiredService<OpenAiProvider>(),
            _ => throw new ArgumentOutOfRangeException(nameof(providerType), providerType, null)
        };
    }
}
