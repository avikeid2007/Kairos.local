using KaiROS.AI.Models;

namespace KaiROS.AI.Services;

public interface IChatService
{
    bool IsModelLoaded { get; }
    InferenceStats LastStats { get; }

    Task<string> GenerateResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default);
    Task<string> GenerateResponseAsync(IEnumerable<ChatMessage> messages, bool useWebSearch, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> GenerateResponseStreamAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> GenerateResponseStreamAsync(IEnumerable<ChatMessage> messages, bool useWebSearch, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> GenerateResponseStreamAsync(IEnumerable<ChatMessage> messages, bool useWebSearch, string? sessionContext, bool useGlobalRag, CancellationToken cancellationToken = default);
    void ClearContext();

    event EventHandler<string>? TokenGenerated;
    event EventHandler<InferenceStats>? StatsUpdated;
}
