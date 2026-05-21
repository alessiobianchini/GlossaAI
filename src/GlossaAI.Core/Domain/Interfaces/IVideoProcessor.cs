using System.Threading.Tasks;

namespace GlossaAI.Core.Domain.Interfaces;

/// <summary>
/// Service abstraction in charge of extracting audio streams from video files.
/// </summary>
public interface IVideoProcessor
{
    /// <summary>
    /// Extracts the audio stream from a video file and converts it to a Whisper-compatible WAV format (16kHz, mono, PCM 16-bit).
    /// </summary>
    /// <param name="videoFilePath">Absolute path to the source video file (MP4, MKV, MOV, etc.).</param>
    /// <param name="outputWavPath">Target absolute path where the WAV file should be written.</param>
    /// <returns>A task returning the path to the extracted WAV file.</returns>
    Task<string> ExtractAudioAsync(string videoFilePath, string outputWavPath);
}
