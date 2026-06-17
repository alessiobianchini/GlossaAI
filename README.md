# GlossaAI - Desktop Audio Coordinator

GlossaAI is a premium, cross-platform .NET 10 desktop application designed to capture, transcribe, and summarize meeting audio. It captures audio from your microphone and desktop loopback, transcribes it locally using Whisper.net, and utilizes Ollama or OpenAI to generate professional summaries based on the selected meeting context.

---

## 🚀 Key Features

- **Dual-Source Audio Recording**: Captures audio simultaneously from a selected microphone and desktop system audio.
- **Local STT (Speech-to-Text)**: High-precision offline transcription powered by **Whisper.net**.
- **Flexible LLM Summarization**:
  - Supports **Ollama** (local models like `qwen3:8b`, `llama3.2:3b`, etc.) or **OpenAI** API integration.
  - Dynamically optimizes prompts based on the selected **Meeting Context** (e.g., General, Developer, Medical, Friends, Business Analysis).
- **Real-time Configuration Persistence**: Auto-saves settings to a local JSON file (`%LocalAppData%/GlossaAI/settings.json`).
- **CI/CD Pipeline**: GitHub Actions workflow automatically builds, packages, and deploys platform-specific installers.

---

## 🏗️ Repository Structure

The project follows a clean architecture separation of concerns:

- `src/GlossaAI.Core`: Domain models, interfaces, exception definitions, and core coordination services like `MeetingManager` and `ConfigurationService`.
- `src/GlossaAI.Infrastructure`: Implementation details including WASAPI audio capture, Whisper translation, LLM connectivity, and FFmpeg media manipulation.
- `src/GlossaAI.Desktop`: Cross-platform desktop interface built using Avalonia UI and MVVM (`CommunityToolkit.Mvvm`).

---

## 📦 Getting Started

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

## 🛠️ Troubleshooting / FAQ

**Error:** `System.UnauthorizedAccessException: Access to the path is denied.` (During WASAPI capture)
**Solution:** Ensure the application has the necessary permissions to access the microphone and system audio. On macOS, this requires granting Microphone permissions in System Settings.

**Error:** `Whisper.net initialization failed` or missing native binaries.
**Solution:** Whisper.net relies on native libraries. Ensure you have the correct runtime identifiers (RIDs) targeted during build, or install the appropriate platform-specific Whisper.net runtime packages for Windows/macOS/Linux.

**Error:** `System.Net.Http.HttpRequestException: Connection refused (127.0.0.1:11434)`
**Solution:** This indicates Ollama is not running. Start Ollama locally before launching GlossaAI:
```bash
ollama serve
```

---

## ⚙️ Settings Persistence

Application configurations are automatically stored locally in:
- **Windows**: `%USERPROFILE%\AppData\Local\GlossaAI\settings.json`
- **Linux/macOS**: `~/.local/share/GlossaAI/settings.json` or equivalent directory.
