namespace GlossaAI.Core.Domain.Interfaces;

public interface ISTTProvider
{
    Task<string> TranscribeAudioAsync(string filePath, string language = "auto", System.Threading.CancellationToken cancellationToken = default);
}
