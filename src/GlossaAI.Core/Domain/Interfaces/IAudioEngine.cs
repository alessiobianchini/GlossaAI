using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GlossaAI.Core.Domain.Models;

namespace GlossaAI.Core.Domain.Interfaces;

public interface IAudioEngine
{
    event Action<float> MicLevelChanged;
    event Action<float> DesktopLevelChanged;
    event Action DevicesChanged;

    Task<IEnumerable<AudioDevice>> GetAvailableDevicesAsync();
    void StartRecording(AudioDevice? micDevice, AudioDevice? desktopDevice, string outputWavPath);
    void StopRecording();
    bool IsRecording { get; }
}
