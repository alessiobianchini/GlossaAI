# GlossaAI - Desktop Audio Coordinator

GlossaAI is a premium, cross-platform .NET 10 / Avalonia desktop application designed to capture, transcribe, and summarize meeting audio. It captures audio from your microphone and desktop loopback, transcribes it locally using Whisper.net, and utilizes Ollama or OpenAI to generate professional summaries based on the selected meeting context.

---

## Key Features

- **Dual-Source Audio Recording**: Captures audio simultaneously from a selected microphone and desktop system audio.
- **Local STT (Speech-to-Text)**: High-precision offline transcription powered by **Whisper.net**.
- **Flexible LLM Summarization**:
  - Supports **Ollama** (local models like `qwen3:8b`, `llama3.2:3b`, etc.) or **OpenAI** API integration.
  - Dynamically optimizes prompts based on the selected **Meeting Context** (e.g., General, Developer, Medical, Friends, Business Analysis).
- **Real-time Configuration Persistence**: Auto-saves settings (API URL, model, language, output folders, last-used meeting context) to a local JSON file (`%LocalAppData%/GlossaAI/settings.json`).
- **Pre-Recording Dialog**: Simple and interactive modal shown before recording starts to refine capture parameters.
- **CI/CD Pipeline**: GitHub Actions workflow automatically builds, packages, and deploys platform-specific installers (Windows Setup, macOS DMG, Linux Tarball) upon tagging (`v*`).

---

## Repository Structure

The project follows a clean architecture separation of concerns:

- **`src/GlossaAI.Core`**: Domain models, interfaces, exception definitions, and core coordination services like `MeetingManager` and `ConfigurationService`.
- **`src/GlossaAI.Infrastructure`**: Implementation details including:
  - Audio capture using WASAPI (`WasapiAudioEngine`).
  - STT translation using Whisper (`WhisperNetProvider`).
  - LLM connectivity (`OllamaProvider`, `OpenAiProvider`).
  - Media/video manipulation using FFmpeg (`FFmpegVideoProcessor`).
- **`src/GlossaAI.Desktop`**: Cross-platform desktop interface built using Avalonia UI and MVVM (`CommunityToolkit.Mvvm`).

---

## Getting Started

### Prerequisites

1. **.NET 10 SDK** or later.
2. **Ollama** (for local AI features) running on `http://127.0.0.1:11434` with your preferred model installed:
   ```bash
   ollama pull qwen3:8b
   ```
3. Alternatively, an **OpenAI API Key** if using OpenAI's model endpoints.

### Building & Running

Restore dependencies and build the solution:

```bash
dotnet build
```

To run the desktop application:

```bash
dotnet run --project src/GlossaAI.Desktop/GlossaAI.Desktop.csproj
```

---

## Settings Persistence

Application configurations are automatically stored locally in:
- **Windows**: `%USERPROFILE%\AppData\Local\GlossaAI\settings.json`
- **Linux/macOS**: `~/.local/share/GlossaAI/settings.json` or equivalent directory.

---

## CI/CD Pipeline & Distribution

The GitHub Actions workflow in `.github/workflows/release.yml` automatically triggers on pushing version tags (e.g., `v1.0.0`) and publishes:

- **Windows**: `GlossaAI-Setup-win-x64.exe` (A complete setup wizard built using Inno Setup).
- **macOS**: `GlossaAI-osx-x64.dmg` (A double-clickable disk image with a `.app` bundle inside).
- **Linux**: `GlossaAI-linux-x64.tar.gz` (A portable standalone archive).
