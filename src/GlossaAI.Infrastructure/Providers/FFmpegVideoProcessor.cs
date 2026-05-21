using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GlossaAI.Core.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace GlossaAI.Infrastructure.Providers;

/// <summary>
/// Extracts audio tracks from video files using the cross-platform FFmpeg utility.
/// </summary>
public class FFmpegVideoProcessor : IVideoProcessor
{
    private readonly ILogger<FFmpegVideoProcessor> _logger;

    public FFmpegVideoProcessor(ILogger<FFmpegVideoProcessor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> ExtractAudioAsync(string videoFilePath, string outputWavPath)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath))
        {
            throw new ArgumentException("Video file path cannot be null or empty.", nameof(videoFilePath));
        }

        if (!File.Exists(videoFilePath))
        {
            throw new FileNotFoundException($"Input video file does not exist: '{videoFilePath}'");
        }

        _logger.LogInformation("FFmpegVideoProcessor: Checking FFmpeg availability...");
        if (!IsFFmpegAvailable(out var ffmpegPath))
        {
            var errorMessage = "FFmpeg executable not found. " +
                               "Please install FFmpeg and add it to your system PATH or place " +
                               $"the executable in the application directory ({AppDomain.CurrentDomain.BaseDirectory}).";
            _logger.LogError(errorMessage);
            throw new FileNotFoundException(errorMessage);
        }

        _logger.LogInformation("FFmpegVideoProcessor: Starting audio extraction from '{VideoPath}' to '{WavPath}'", videoFilePath, outputWavPath);

        // FFmpeg command options:
        // -i: Input file path
        // -ar 16000: Set audio sampling rate to 16000Hz (Whisper requirement)
        // -ac 1: Set audio channels to 1 (mono - Whisper requirement)
        // -c:a pcm_s16le: Set audio codec to PCM signed 16-bit little-endian
        // -y: Overwrite output file without asking
        var arguments = $"-i \"{videoFilePath}\" -ar 16000 -ac 1 -c:a pcm_s16le -y \"{outputWavPath}\"";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        try
        {
            process.Start();

            // Read output streams asynchronously to avoid deadlocks
            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var stdOut = await stdOutTask;
            var stdErr = await stdErrTask;

            if (process.ExitCode != 0)
            {
                _logger.LogError("FFmpeg process exited with error code {Code}. Error details: {Details}", process.ExitCode, stdErr);
                throw new InvalidOperationException($"FFmpeg failed to extract audio. Exit code: {process.ExitCode}. Output: {stdErr}");
            }

            _logger.LogInformation("FFmpegVideoProcessor: Successfully extracted WAV audio to '{Path}'", outputWavPath);
            return outputWavPath;
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not FileNotFoundException)
        {
            _logger.LogError(ex, "An error occurred while launching or running the FFmpeg process.");
            throw new InvalidOperationException("Failed to run the FFmpeg audio extraction process.", ex);
        }
    }

    /// <summary>
    /// Checks if the FFmpeg executable is present locally or in the system environment PATH.
    /// </summary>
    /// <param name="resolvedPath">Out parameter receiving the name/path of the executable to invoke.</param>
    private bool IsFFmpegAvailable(out string resolvedPath)
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
        
        // 1. Check local base directory where application lives
        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName);
        if (File.Exists(localPath))
        {
            resolvedPath = localPath;
            _logger.LogInformation("FFmpegVideoProcessor: Found FFmpeg locally in app folder: '{Path}'", localPath);
            return true;
        }

        // 2. Check if available globally in system PATH by attempting to call "ffmpeg -version"
        try
        {
            using var testProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exeName,
                    Arguments = "-version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            testProcess.Start();
            resolvedPath = exeName; // Directly runnable via PATH
            _logger.LogInformation("FFmpegVideoProcessor: Found FFmpeg globally in system PATH.");
            return true;
        }
        catch
        {
            resolvedPath = string.Empty;
            return false;
        }
    }
}
