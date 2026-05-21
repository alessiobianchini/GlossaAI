using GlossaAI.Core.Domain.Models;

namespace GlossaAI.Core.Domain.Interfaces;

public interface ILLMProviderFactory
{
    ILLMProvider GetProvider(AiProviderType providerType);
}
