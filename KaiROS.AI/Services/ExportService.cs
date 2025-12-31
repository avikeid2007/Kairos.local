using KaiROS.AI.Models;
using System.IO;
using System.Text;
using System.Text.Json;

namespace KaiROS.AI.Services;

public interface IExportService
{
    Task<string> ExportToMarkdownAsync(ChatSession session, List<ChatMessage> messages);
    Task<string> ExportToJsonAsync(ChatSession session, List<ChatMessage> messages);
    Task<string> ExportToTextAsync(ChatSession session, List<ChatMessage> messages);
    Task<bool> ExportWithDialogAsync(ChatSession session, List<ChatMessage> messages, ExportFormat format);
}

public enum ExportFormat
{
    Markdown,
    Json,
    Text
}

public class ExportService : IExportService
{
    public Task<string> ExportToMarkdownAsync(ChatSession session, List<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        
        // Header
        sb.AppendLine($"# {session.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Model:** {session.ModelName ?? "Unknown"}");
        sb.AppendLine($"**Created:** {session.CreatedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"**Messages:** {messages.Count}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        
        // System prompt if available
        if (!string.IsNullOrEmpty(session.SystemPrompt))
        {
            sb.AppendLine("## System Prompt");
            sb.AppendLine();
            sb.AppendLine($"> {session.SystemPrompt.Replace("\n", "\n> ")}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }
        
        // Messages
        sb.AppendLine("## Conversation");
        sb.AppendLine();
        
        foreach (var message in messages)
        {
            var role = message.Role == ChatRole.User ? "👤 **User**" : "🤖 **Assistant**";
            sb.AppendLine($"### {role}");
            sb.AppendLine();
            sb.AppendLine(message.Content);
            sb.AppendLine();
        }
        
        // Footer
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"*Exported from KaiROS AI on {DateTime.Now:yyyy-MM-dd HH:mm}*");
        
        return Task.FromResult(sb.ToString());
    }
    
    public Task<string> ExportToJsonAsync(ChatSession session, List<ChatMessage> messages)
    {
        var exportData = new
        {
            session = new
            {
                id = session.Id,
                title = session.Title,
                modelName = session.ModelName,
                systemPrompt = session.SystemPrompt,
                createdAt = session.CreatedAt,
                updatedAt = session.UpdatedAt,
                messageCount = messages.Count
            },
            messages = messages.Select(m => new
            {
                role = m.Role.ToString().ToLower(),
                content = m.Content,
                timestamp = m.Timestamp
            }).ToList(),
            exportedAt = DateTime.Now,
            exportedFrom = "KaiROS AI"
        };
        
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        return Task.FromResult(JsonSerializer.Serialize(exportData, options));
    }
    
    public Task<string> ExportToTextAsync(ChatSession session, List<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        
        // Header
        sb.AppendLine($"=== {session.Title} ===");
        sb.AppendLine($"Model: {session.ModelName ?? "Unknown"}");
        sb.AppendLine($"Created: {session.CreatedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Messages: {messages.Count}");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();
        
        // Messages
        foreach (var message in messages)
        {
            var role = message.Role == ChatRole.User ? "USER" : "ASSISTANT";
            sb.AppendLine($"[{role}] ({message.Timestamp:HH:mm:ss})");
            sb.AppendLine(message.Content);
            sb.AppendLine();
            sb.AppendLine(new string('-', 30));
            sb.AppendLine();
        }
        
        // Footer
        sb.AppendLine($"Exported from KaiROS AI on {DateTime.Now:yyyy-MM-dd HH:mm}");
        
        return Task.FromResult(sb.ToString());
    }
    
    public async Task<bool> ExportWithDialogAsync(ChatSession session, List<ChatMessage> messages, ExportFormat format)
    {
        var (extension, filter) = format switch
        {
            ExportFormat.Markdown => (".md", "Markdown files (*.md)|*.md"),
            ExportFormat.Json => (".json", "JSON files (*.json)|*.json"),
            ExportFormat.Text => (".txt", "Text files (*.txt)|*.txt"),
            _ => (".txt", "Text files (*.txt)|*.txt")
        };
        
        var defaultFileName = SanitizeFileName(session.Title) + extension;
        
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            FileName = defaultFileName,
            Title = "Export Conversation"
        };
        
        if (dialog.ShowDialog() == true)
        {
            var content = format switch
            {
                ExportFormat.Markdown => await ExportToMarkdownAsync(session, messages),
                ExportFormat.Json => await ExportToJsonAsync(session, messages),
                ExportFormat.Text => await ExportToTextAsync(session, messages),
                _ => await ExportToTextAsync(session, messages)
            };
            
            await File.WriteAllTextAsync(dialog.FileName, content, Encoding.UTF8);
            return true;
        }
        
        return false;
    }
    
    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrEmpty(sanitized) ? "conversation" : sanitized;
    }
}
