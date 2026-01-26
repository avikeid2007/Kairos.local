using KaiROS.AI.Models;

namespace KaiROS.AI.Services;

public interface IRagSourceProvider
{
    RagSourceType SupportedType { get; }
    Task<string> GetContentAsync(RagSource source);
}
