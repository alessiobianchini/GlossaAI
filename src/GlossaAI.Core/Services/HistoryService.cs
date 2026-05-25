using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using GlossaAI.Core.Domain.Models;

namespace GlossaAI.Core.Services;

public class HistoryService
{
    private readonly string _filePath;

    public HistoryService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GlossaAI");
            
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "meetings_history.json");
    }

    public async Task<List<MeetingRecord>> LoadHistoryAsync()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new List<MeetingRecord>();

            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<List<MeetingRecord>>(json) ?? new List<MeetingRecord>();
        }
        catch
        {
            return new List<MeetingRecord>();
        }
    }

    public async Task SaveHistoryAsync(List<MeetingRecord> history)
    {
        try
        {
            var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch
        {
            // Fail silently on save error to prevent app crash
        }
    }
}
