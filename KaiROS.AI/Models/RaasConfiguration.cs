using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace KaiROS.AI.Models;

public class RaasConfiguration : INotifyPropertyChanged
{
    private bool _isRunning;
    private int _requestCount;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New RAG Service";
    public string Description { get; set; } = string.Empty;
    public int Port { get; set; } = 5001;
    public string SystemPrompt { get; set; } = "You are a helpful AI assistant.";
    
    public List<RagSource> Sources { get; set; } = new();

    [JsonIgnore]
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonIgnore]
    public int RequestCount
    {
        get => _requestCount;
        set
        {
            if (_requestCount != value)
            {
                _requestCount = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
