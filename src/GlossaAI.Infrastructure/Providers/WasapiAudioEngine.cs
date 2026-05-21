using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GlossaAI.Core.Domain.Interfaces;
using GlossaAI.Core.Domain.Models;
using Microsoft.Extensions.Logging;

namespace GlossaAI.Infrastructure.Providers;

public class WasapiAudioEngine : IAudioEngine, IDisposable
{
    private readonly ILogger<WasapiAudioEngine> _logger;
    private readonly NAudio.CoreAudioApi.MMDeviceEnumerator? _deviceEnumerator;
    private readonly AudioDeviceNotificationClient? _notificationClient;
    
    private readonly object _lock = new();
    private bool _isRecording;
    private string? _currentOutputPath;
    private string? _micTempPath;
    private string? _desktopTempPath;

    private NAudio.CoreAudioApi.WasapiCapture? _micCapture;
    private NAudio.Wave.WasapiLoopbackCapture? _desktopCapture;
    private NAudio.Wave.WaveFileWriter? _micWriter;
    private NAudio.Wave.WaveFileWriter? _desktopWriter;

    public event Action<float>? MicLevelChanged;
    public event Action<float>? DesktopLevelChanged;
    public event Action? DevicesChanged;

    public bool IsRecording => _isRecording;

    public WasapiAudioEngine(ILogger<WasapiAudioEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (OperatingSystem.IsWindows())
        {
            try
            {
                _deviceEnumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                _notificationClient = new AudioDeviceNotificationClient(this);
                _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationClient);
                _logger.LogInformation("WasapiAudioEngine: Registered MMDeviceEnumerator notifications.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WasapiAudioEngine: Failed to register MMDeviceEnumerator notifications.");
            }
        }
    }

    public Task<IEnumerable<AudioDevice>> GetAvailableDevicesAsync()
    {
        _logger.LogInformation("WasapiAudioEngine: Querying system audio endpoints...");
        var devices = new List<AudioDevice>();

        if (!OperatingSystem.IsWindows() || _deviceEnumerator == null)
        {
            return Task.FromResult<IEnumerable<AudioDevice>>(devices);
        }

        try
        {
            var micEndpoints = _deviceEnumerator.EnumerateAudioEndPoints(
                NAudio.CoreAudioApi.DataFlow.Capture,
                NAudio.CoreAudioApi.DeviceState.Active);
            foreach (var endpoint in micEndpoints)
            {
                devices.Add(new AudioDevice(endpoint.ID, endpoint.FriendlyName, DeviceType.Microphone));
            }

            var renderEndpoints = _deviceEnumerator.EnumerateAudioEndPoints(
                NAudio.CoreAudioApi.DataFlow.Render,
                NAudio.CoreAudioApi.DeviceState.Active);
            foreach (var endpoint in renderEndpoints)
            {
                devices.Add(new AudioDevice(endpoint.ID, endpoint.FriendlyName, DeviceType.DesktopLoopback));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WasapiAudioEngine: Failed to enumerate audio endpoints.");
        }

        return Task.FromResult<IEnumerable<AudioDevice>>(devices);
    }

    public void StartRecording(AudioDevice? micDevice, AudioDevice? desktopDevice, string outputWavPath)
    {
        if (!OperatingSystem.IsWindows() || _deviceEnumerator == null)
            throw new PlatformNotSupportedException("WASAPI recording is only supported on Windows.");

        if (micDevice == null && desktopDevice == null)
            throw new ArgumentException("At least one audio input source must be selected.");

        if (string.IsNullOrWhiteSpace(outputWavPath))
            throw new ArgumentException("WAV output path cannot be null or empty.", nameof(outputWavPath));

        lock (_lock)
        {
            if (_isRecording)
                throw new InvalidOperationException("A recording session is already active.");

            _logger.LogInformation("WasapiAudioEngine: Starting session → mic={Mic} loopback={Loop} out={Out}",
                micDevice?.Name ?? "none", desktopDevice?.Name ?? "none", outputWavPath);

            _currentOutputPath = outputWavPath;
            _micTempPath = null;
            _desktopTempPath = null;

            var directory = Path.GetDirectoryName(outputWavPath);
            if (directory != null && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            try
            {
                if (micDevice != null)
                {
                    var mmDevice = _deviceEnumerator.GetDevice(micDevice.Id);
                    _micCapture = new NAudio.CoreAudioApi.WasapiCapture(mmDevice);
                    
                    string writerPath = desktopDevice != null ? (outputWavPath + ".mic.tmp") : outputWavPath;
                    if (desktopDevice != null) _micTempPath = writerPath;

                    _micWriter = new NAudio.Wave.WaveFileWriter(writerPath, new NAudio.Wave.WaveFormat(16000, 16, 1));
                    _micCapture.DataAvailable += (s, e) => OnMicDataAvailable(e.Buffer, e.BytesRecorded);
                }

                if (desktopDevice != null)
                {
                    var mmDevice = _deviceEnumerator.GetDevice(desktopDevice.Id);
                    _desktopCapture = new NAudio.Wave.WasapiLoopbackCapture(mmDevice);
                    
                    string writerPath = micDevice != null ? (outputWavPath + ".desktop.tmp") : outputWavPath;
                    if (micDevice != null) _desktopTempPath = writerPath;

                    _desktopWriter = new NAudio.Wave.WaveFileWriter(writerPath, new NAudio.Wave.WaveFormat(16000, 16, 1));
                    _desktopCapture.DataAvailable += (s, e) => OnDesktopDataAvailable(e.Buffer, e.BytesRecorded);
                }

                _isRecording = true;
                _micCapture?.StartRecording();
                _desktopCapture?.StartRecording();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WasapiAudioEngine: Failed to initialize WASAPI recording.");
                CleanupResources();
                throw;
            }
        }
    }

    public void StopRecording()
    {
        lock (_lock)
        {
            if (!_isRecording)
            {
                _logger.LogWarning("WasapiAudioEngine: StopRecording called but no session is active.");
                return;
            }

            _logger.LogInformation("WasapiAudioEngine: Stopping recording session...");

            try { _micCapture?.StopRecording(); } catch (Exception ex) { _logger.LogError(ex, "Error stopping mic capture"); }
            try { _desktopCapture?.StopRecording(); } catch (Exception ex) { _logger.LogError(ex, "Error stopping desktop capture"); }

            _micCapture?.Dispose();
            _micCapture = null;
            _desktopCapture?.Dispose();
            _desktopCapture = null;

            if (_micWriter != null)
            {
                try { _micWriter.Flush(); } catch (Exception ex) { _logger.LogError(ex, "Error flushing mic writer"); }
                try { _micWriter.Dispose(); } catch (Exception ex) { _logger.LogError(ex, "Error disposing mic writer"); }
                _micWriter = null;
            }

            if (_desktopWriter != null)
            {
                try { _desktopWriter.Flush(); } catch (Exception ex) { _logger.LogError(ex, "Error flushing desktop writer"); }
                try { _desktopWriter.Dispose(); } catch (Exception ex) { _logger.LogError(ex, "Error disposing desktop writer"); }
                _desktopWriter = null;
            }

            _isRecording = false;

            if (_micTempPath != null && _desktopTempPath != null && _currentOutputPath != null)
            {
                try
                {
                    MixWavFiles(_micTempPath, _desktopTempPath, _currentOutputPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WasapiAudioEngine: Failed to mix temporary WAV files.");
                }
            }

            SafeDeleteFile(_micTempPath);
            SafeDeleteFile(_desktopTempPath);

            _micTempPath = null;
            _desktopTempPath = null;
            _currentOutputPath = null;

            MicLevelChanged?.Invoke(0f);
            DesktopLevelChanged?.Invoke(0f);

            _logger.LogInformation("WasapiAudioEngine: Session stopped and resources cleaned up.");
        }
    }

    private void OnMicDataAvailable(byte[] buffer, int bytesRecorded)
    {
        lock (_lock)
        {
            if (!_isRecording || _micCapture == null || _micWriter == null) return;

            try
            {
                var format = _micCapture.WaveFormat;
                var floatSamples = GetFloatSamples(buffer, bytesRecorded, format);
                
                var rms = ComputeRms(floatSamples);
                MicLevelChanged?.Invoke(rms);

                var monoSamples = DownmixToMono(floatSamples, format.Channels);
                var resampled = Resample(monoSamples, format.SampleRate, 16000);
                
                WriteFloatSamplesToPcm(resampled, _micWriter);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WasapiAudioEngine: Error in Microphone DataAvailable handler.");
            }
        }
    }

    private void OnDesktopDataAvailable(byte[] buffer, int bytesRecorded)
    {
        lock (_lock)
        {
            if (!_isRecording || _desktopCapture == null || _desktopWriter == null) return;

            try
            {
                var format = _desktopCapture.WaveFormat;
                var floatSamples = GetFloatSamples(buffer, bytesRecorded, format);
                
                var rms = ComputeRms(floatSamples);
                DesktopLevelChanged?.Invoke(rms);

                var monoSamples = DownmixToMono(floatSamples, format.Channels);
                var resampled = Resample(monoSamples, format.SampleRate, 16000);
                
                WriteFloatSamplesToPcm(resampled, _desktopWriter);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WasapiAudioEngine: Error in Desktop DataAvailable handler.");
            }
        }
    }

    private static float ComputeRms(float[] samples)
    {
        if (samples.Length == 0) return 0f;
        double sumSq = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            sumSq += samples[i] * samples[i];
        }
        return Math.Clamp((float)Math.Sqrt(sumSq / samples.Length), 0f, 1f);
    }

    private static float[] GetFloatSamples(byte[] buffer, int bytesRecorded, NAudio.Wave.WaveFormat format)
    {
        int bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample == 0) return Array.Empty<float>();
        int sampleCount = bytesRecorded / bytesPerSample;
        float[] samples = new float[sampleCount];

        if (format.Encoding == NAudio.Wave.WaveFormatEncoding.IeeeFloat ||
            (format.Encoding == NAudio.Wave.WaveFormatEncoding.Extensible && format.BitsPerSample == 32))
        {
            for (int i = 0; i < sampleCount; i++)
            {
                samples[i] = BitConverter.ToSingle(buffer, i * 4);
            }
        }
        else
        {
            if (format.BitsPerSample == 16)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    short val = BitConverter.ToInt16(buffer, i * 2);
                    samples[i] = val / 32768f;
                }
            }
            else if (format.BitsPerSample == 24)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    int val = (buffer[i * 3] << 8) | (buffer[i * 3 + 1] << 16) | (buffer[i * 3 + 2] << 24);
                    samples[i] = (val >> 8) / 8388608f;
                }
            }
            else if (format.BitsPerSample == 32)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    samples[i] = BitConverter.ToSingle(buffer, i * 4);
                }
            }
            else if (format.BitsPerSample == 8)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    samples[i] = (buffer[i] - 128) / 128f;
                }
            }
        }
        return samples;
    }

    private static float[] DownmixToMono(float[] multiChannelSamples, int channels)
    {
        if (channels <= 1) return multiChannelSamples;
        int monoLength = multiChannelSamples.Length / channels;
        float[] monoSamples = new float[monoLength];
        for (int i = 0; i < monoLength; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
            {
                sum += multiChannelSamples[i * channels + c];
            }
            monoSamples[i] = sum / channels;
        }
        return monoSamples;
    }

    private static float[] Resample(float[] samples, int fromSampleRate, int toSampleRate)
    {
        if (fromSampleRate == toSampleRate) return samples;

        double ratio = (double)toSampleRate / fromSampleRate;
        int newLength = (int)(samples.Length * ratio);
        float[] resampled = new float[newLength];

        for (int i = 0; i < newLength; i++)
        {
            double oldIndex = i / ratio;
            int index1 = (int)Math.Floor(oldIndex);
            int index2 = index1 + 1;
            
            if (index2 >= samples.Length)
            {
                resampled[i] = index1 < samples.Length ? samples[index1] : 0f;
                continue;
            }

            float t = (float)(oldIndex - index1);
            resampled[i] = (1f - t) * samples[index1] + t * samples[index2];
        }

        return resampled;
    }

    private static void WriteFloatSamplesToPcm(float[] samples, NAudio.Wave.WaveFileWriter writer)
    {
        if (samples.Length == 0) return;
        byte[] pcmBytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short sample16 = (short)Math.Clamp((int)(samples[i] * 32767f), -32768, 32767);
            pcmBytes[i * 2] = (byte)(sample16 & 0xff);
            pcmBytes[i * 2 + 1] = (byte)((sample16 >> 8) & 0xff);
        }
        writer.Write(pcmBytes, 0, pcmBytes.Length);
    }

    private void MixWavFiles(string micPath, string desktopPath, string outputPath)
    {
        _logger.LogInformation("WasapiAudioEngine: Mixing '{Mic}' and '{Desktop}' to '{Out}'", micPath, desktopPath, outputPath);

        if (!File.Exists(micPath) && !File.Exists(desktopPath))
        {
            _logger.LogWarning("WasapiAudioEngine: Neither temp file exists for mixing.");
            return;
        }

        if (!File.Exists(micPath))
        {
            File.Copy(desktopPath, outputPath, overwrite: true);
            return;
        }

        if (!File.Exists(desktopPath))
        {
            File.Copy(micPath, outputPath, overwrite: true);
            return;
        }

        try
        {
            using var micReader = new NAudio.Wave.WaveFileReader(micPath);
            using var desktopReader = new NAudio.Wave.WaveFileReader(desktopPath);
            using var mixWriter = new NAudio.Wave.WaveFileWriter(outputPath, new NAudio.Wave.WaveFormat(16000, 16, 1));

            byte[] micBuffer = new byte[4096];
            byte[] desktopBuffer = new byte[4096];

            while (true)
            {
                int micBytesRead = micReader.Read(micBuffer, 0, micBuffer.Length);
                int desktopBytesRead = desktopReader.Read(desktopBuffer, 0, desktopBuffer.Length);

                if (micBytesRead == 0 && desktopBytesRead == 0)
                    break;

                int maxBytes = Math.Max(micBytesRead, desktopBytesRead);
                byte[] mixBuffer = new byte[maxBytes];

                for (int i = 0; i < maxBytes; i += 2)
                {
                    short micSample = 0;
                    if (i + 1 < micBytesRead)
                    {
                        micSample = (short)(micBuffer[i] | (micBuffer[i + 1] << 8));
                    }

                    short desktopSample = 0;
                    if (i + 1 < desktopBytesRead)
                    {
                        desktopSample = (short)(desktopBuffer[i] | (desktopBuffer[i + 1] << 8));
                    }

                    int mixed = micSample + desktopSample;
                    short mixedShort = (short)Math.Clamp(mixed, -32768, 32767);

                    mixBuffer[i] = (byte)(mixedShort & 0xff);
                    mixBuffer[i + 1] = (byte)((mixedShort >> 8) & 0xff);
                }

                mixWriter.Write(mixBuffer, 0, maxBytes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WasapiAudioEngine: Error during audio mixing process.");
            throw;
        }
    }

    private void SafeDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WasapiAudioEngine: Failed to delete temporary file '{Path}'", path);
        }
    }

    private void CleanupResources()
    {
        try { _micCapture?.StopRecording(); } catch { }
        try { _desktopCapture?.StopRecording(); } catch { }

        _micCapture?.Dispose();
        _micCapture = null;
        _desktopCapture?.Dispose();
        _desktopCapture = null;

        if (_micWriter != null) { try { _micWriter.Dispose(); } catch { } _micWriter = null; }
        if (_desktopWriter != null) { try { _desktopWriter.Dispose(); } catch { } _desktopWriter = null; }

        SafeDeleteFile(_micTempPath);
        SafeDeleteFile(_desktopTempPath);

        _micTempPath = null;
        _desktopTempPath = null;
        _currentOutputPath = null;
        _isRecording = false;
    }


    private void RaiseDevicesChanged()
    {
        _logger.LogInformation("WasapiAudioEngine: OS audio device topology changed.");
        DevicesChanged?.Invoke();
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows() && _deviceEnumerator != null && _notificationClient != null)
        {
            try
            {
                _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationClient);
                _deviceEnumerator.Dispose();
                _logger.LogInformation("WasapiAudioEngine: Unregistered and disposed MMDeviceEnumerator.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WasapiAudioEngine: Failed to unregister/dispose MMDeviceEnumerator.");
            }
        }

        lock (_lock)
        {
            CleanupResources();
        }

        GC.SuppressFinalize(this);
    }

    private class AudioDeviceNotificationClient : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
    {
        private readonly WasapiAudioEngine _engine;

        public AudioDeviceNotificationClient(WasapiAudioEngine engine)
        {
            _engine = engine;
        }

        public void OnDeviceStateChanged(string deviceId, NAudio.CoreAudioApi.DeviceState newState)
            => _engine.RaiseDevicesChanged();

        public void OnDeviceAdded(string deviceId)
            => _engine.RaiseDevicesChanged();

        public void OnDeviceRemoved(string deviceId)
            => _engine.RaiseDevicesChanged();

        public void OnDefaultDeviceChanged(NAudio.CoreAudioApi.DataFlow flow, NAudio.CoreAudioApi.Role role, string defaultDeviceId)
            => _engine.RaiseDevicesChanged();

        public void OnPropertyValueChanged(string deviceId, NAudio.CoreAudioApi.PropertyKey key)
        {
        }
    }
}
