using System.Text.Json.Serialization;

namespace KaiROS.AI.Models;

public enum RagSourceType
{
    File,
    Web,
    Text,
    Database,
    GitHub,
    Other
}

public class RagSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public RagSourceType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty; // Path, URL, or Connection String
    public bool IsEnabled { get; set; } = true;
    public Dictionary<string, string> Metadata { get; set; } = new();

    [JsonIgnore]
    public string DisplayIcon => Type switch
    {
        RagSourceType.File => "📄",
        RagSourceType.Web => "🌐",
        RagSourceType.Text => "📝",
        RagSourceType.Database => "🗄️",
        RagSourceType.GitHub => "💻",
        _ => "❓"
    };
}
