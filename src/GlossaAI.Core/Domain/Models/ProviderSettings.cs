using GlossaAI.Core.Domain.Models;
using System;

namespace GlossaAI.Core.Domain.Models;

public class ProviderSettings
{
    public AiProviderType SelectedProvider { get; set; } = AiProviderType.Ollama;
    public string ApiUrl { get; set; } = "http://127.0.0.1:11434";
    public string ApiKey { get; set; } = string.Empty;
    public string SelectedModel { get; set; } = "qwen3:8b";
    public string RecLangCode { get; set; } = "en";
    public string OutLangName { get; set; } = "English";
    public string ExportFolderPath { get; set; } = AppContext.BaseDirectory;
    public MeetingContext SelectedContext { get; set; } = MeetingContext.General;
}
