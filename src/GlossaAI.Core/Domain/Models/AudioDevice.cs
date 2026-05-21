namespace GlossaAI.Core.Domain.Models;

/// <summary>
/// Specifies the classification of an audio input source.
/// </summary>
public enum DeviceType
{
    /// <summary>
    /// A hardware microphone recording voice.
    /// </summary>
    Microphone,

    /// <summary>
    /// System audio output redirected back as input (e.g., meeting participants' voices).
    /// </summary>
    DesktopLoopback
}

/// <summary>
/// Represents a selectable audio device in the system.
/// </summary>
/// <param name="Id">Unique identifier of the hardware or loopback device.</param>
/// <param name="Name">Friendly name displayed in the UI.</param>
/// <param name="Type">The device type classification.</param>
public record AudioDevice(string Id, string Name, DeviceType Type);
