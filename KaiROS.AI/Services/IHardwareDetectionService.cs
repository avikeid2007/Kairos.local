using KaiROS.AI.Models;

namespace KaiROS.AI.Services;

public interface IHardwareDetectionService
{
    Task<HardwareInfo> DetectHardwareAsync();
    ExecutionBackend GetRecommendedBackend();
    bool IsBackendAvailable(ExecutionBackend backend);
    void ClearCache();
    void SetSelectedBackend(ExecutionBackend backend);
}
