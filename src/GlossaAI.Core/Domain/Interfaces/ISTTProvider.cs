namespace GlossaAI.Core.Domain.Interfaces;

public interface ISTTProvider
{
    Task<string> TranscribeAudioAsync(string filePath, string language = "auto", IProgress<string>? progress = null, System.Threading.CancellationToken cancellationToken = default);
}
