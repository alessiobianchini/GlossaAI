using System;

namespace GlossaAI.Core.Domain.Models;

public class MeetingRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; } = DateTime.Now;
    public string Title { get; set; } = "Untitled Meeting";
    public string TranscriptionText { get; set; } = string.Empty;
    public string AiRecapText { get; set; } = string.Empty;
    public string? SourceFilePath { get; set; }
    public bool WasVideo { get; set; }
    public bool IsDraft { get; set; } = false;
    public string Status { get; set; } = "Completed";
}
