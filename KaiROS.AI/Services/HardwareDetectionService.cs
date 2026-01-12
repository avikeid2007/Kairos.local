using KaiROS.AI.Models;
using System.Management;
using System.Runtime.InteropServices;

namespace KaiROS.AI.Services;

public class HardwareDetectionService : IHardwareDetectionService
{
    private HardwareInfo? _cachedInfo;

    public async Task<HardwareInfo> DetectHardwareAsync()
    {
        if (_cachedInfo != null) return _cachedInfo;

        var info = new HardwareInfo();

        await Task.Run(() =>
        {
            // Detect RAM
            try
            {
                var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
                info.TotalRamBytes = (long)computerInfo.TotalPhysicalMemory;
                info.AvailableRamBytes = (long)computerInfo.AvailablePhysicalMemory;
            }
            catch
            {
                // Fallback: use GC info
                info.TotalRamBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                info.AvailableRamBytes = info.TotalRamBytes - Environment.WorkingSet;
            }

            // Detect GPU via WMI - prioritize discrete GPUs (NVIDIA/AMD) over integrated
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                string? bestGpuName = null;
                long bestGpuMemory = 0;
                bool foundNvidia = false;
                bool foundAmd = false;

                foreach (ManagementObject mo in searcher.Get())
                {
                    var gpuName = mo["Name"]?.ToString() ?? "";
                    long gpuRam = 0;

                    // AdapterRAM is a uint32, which caps at ~4GB
                    // For high-VRAM GPUs, we need alternative detection
                    var ramValue = mo["AdapterRAM"];
                    if (ramValue is uint ram32)
                        gpuRam = ram32;
                    else if (ramValue is ulong ram64)
                        gpuRam = (long)ram64;

                    // Workaround for 4GB cap: Detect known high-VRAM GPU models
                    // WMI caps at 4GB, so manually set correct VRAM for known models
                    if (gpuRam > 0 && gpuRam <= 4L * 1024 * 1024 * 1024)
                    {
                        // Check for known high-VRAM GPUs and correct the value
                        gpuRam = GetActualVramForGpu(gpuName, gpuRam);
                    }

                    bool isNvidia = gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
                    bool isAmd = gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                                 gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase);
                    bool isIntegrated = gpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                                        gpuName.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase);

                    // Priority: NVIDIA > AMD > Others > Intel Integrated
                    if (isNvidia && !foundNvidia)
                    {
                        bestGpuName = gpuName;
                        bestGpuMemory = gpuRam;
                        foundNvidia = true;
                    }
                    else if (isAmd && !foundNvidia && !foundAmd)
                    {
                        bestGpuName = gpuName;
                        bestGpuMemory = gpuRam;
                        foundAmd = true;
                    }
                    else if (!foundNvidia && !foundAmd && !isIntegrated)
                    {
                        bestGpuName = gpuName;
                        bestGpuMemory = gpuRam;
                    }
                    else if (bestGpuName == null)
                    {
                        // Fallback: use any GPU if nothing better found
                        bestGpuName = gpuName;
                        bestGpuMemory = gpuRam;
                    }
                }

                info.GpuName = bestGpuName;
                info.GpuMemoryBytes = bestGpuMemory;
            }
            catch
            {
                info.GpuName = "Unknown";
            }

            // Check for CUDA availability (NVIDIA GPU)
            info.HasCuda = !string.IsNullOrEmpty(info.GpuName) &&
                           info.GpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);

            // DirectML is available on Windows 10+ with any GPU
            info.HasDirectML = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                               Environment.OSVersion.Version.Major >= 10;

            // NPU detection (limited - check for Intel NPU or Qualcomm)
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%NPU%' OR Name LIKE '%Neural%' OR Name LIKE '%AI Boost%'");
                info.HasNpu = searcher.Get().Count > 0;
            }
            catch
            {
                info.HasNpu = false;
            }

            // Build available backends list
            info.AvailableBackends.Add(ExecutionBackend.Cpu); // Always available

            if (info.HasCuda)
                info.AvailableBackends.Add(ExecutionBackend.Cuda);

            if (info.HasDirectML)
                info.AvailableBackends.Add(ExecutionBackend.DirectML);

            if (info.HasNpu)
                info.AvailableBackends.Add(ExecutionBackend.Npu);

            // Determine recommended backend
            info.RecommendedBackend = DetermineRecommendedBackend(info);
            info.SelectedBackend = info.RecommendedBackend;

            // Build status message
            info.StatusMessage = BuildStatusMessage(info);
        });

        _cachedInfo = info;
        return info;
    }

    public ExecutionBackend GetRecommendedBackend()
    {
        return _cachedInfo?.RecommendedBackend ?? ExecutionBackend.Cpu;
    }

    public bool IsBackendAvailable(ExecutionBackend backend)
    {
        return _cachedInfo?.AvailableBackends.Contains(backend) ?? backend == ExecutionBackend.Cpu;
    }

    public void ClearCache()
    {
        _cachedInfo = null;
    }

    public void SetSelectedBackend(ExecutionBackend backend)
    {
        if (_cachedInfo != null)
        {
            _cachedInfo.SelectedBackend = backend;
        }
    }

    private static ExecutionBackend DetermineRecommendedBackend(HardwareInfo info)
    {
        // Prefer CUDA for NVIDIA GPUs (best performance)
        if (info.HasCuda)
            return ExecutionBackend.Cuda;

        // NPU if available (power efficient)
        if (info.HasNpu)
            return ExecutionBackend.Npu;

        // DirectML for AMD/Intel GPUs on Windows
        if (info.HasDirectML && info.GpuMemoryBytes > 2L * 1024 * 1024 * 1024)
            return ExecutionBackend.DirectML;

        // Fallback to CPU
        return ExecutionBackend.Cpu;
    }

    private static string BuildStatusMessage(HardwareInfo info)
    {
        var parts = new List<string>
        {
            $"RAM: {info.TotalRamText}"
        };

        if (!string.IsNullOrEmpty(info.GpuName))
            parts.Add($"GPU: {info.GpuName}");

        if (info.HasCuda)
            parts.Add("CUDA ✓");
        else if (info.HasDirectML)
            parts.Add("DirectML ✓");

        if (info.HasNpu)
            parts.Add("NPU ✓");

        return string.Join(" | ", parts);
    }

    /// <summary>
    /// Get actual VRAM for known high-memory GPUs that WMI incorrectly reports due to 4GB cap.
    /// </summary>
    private static long GetActualVramForGpu(string gpuName, long reportedVram)
    {
        // WMI reports uint32 which caps at ~4GB
        // For known high-VRAM GPUs, return the correct value

        // NVIDIA RTX 50 series (Blackwell - 2025)
        if (gpuName.Contains("5090", StringComparison.OrdinalIgnoreCase))
            return 32L * 1024 * 1024 * 1024; // 32GB
        if (gpuName.Contains("5080", StringComparison.OrdinalIgnoreCase))
            return 16L * 1024 * 1024 * 1024; // 16GB
        if (gpuName.Contains("5070 Ti", StringComparison.OrdinalIgnoreCase))
            return 16L * 1024 * 1024 * 1024; // 16GB
        if (gpuName.Contains("5070", StringComparison.OrdinalIgnoreCase))
            return 12L * 1024 * 1024 * 1024; // 12GB

        // NVIDIA RTX 40 series (Ada Lovelace)
        if (gpuName.Contains("4090", StringComparison.OrdinalIgnoreCase))
            return 24L * 1024 * 1024 * 1024; // 24GB
        if (gpuName.Contains("4080", StringComparison.OrdinalIgnoreCase))
            return 16L * 1024 * 1024 * 1024; // 16GB
        if (gpuName.Contains("4070 Ti", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("4070Ti", StringComparison.OrdinalIgnoreCase))
            return 16L * 1024 * 1024 * 1024; // 16GB (Super) or 12GB
        if (gpuName.Contains("4070", StringComparison.OrdinalIgnoreCase))
            return 12L * 1024 * 1024 * 1024; // 12GB

        // NVIDIA RTX 30 series (Ampere)
        if (gpuName.Contains("3090", StringComparison.OrdinalIgnoreCase))
            return 24L * 1024 * 1024 * 1024; // 24GB
        if (gpuName.Contains("3080 Ti", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("3080Ti", StringComparison.OrdinalIgnoreCase))
            return 12L * 1024 * 1024 * 1024; // 12GB
        if (gpuName.Contains("3080", StringComparison.OrdinalIgnoreCase))
            return 10L * 1024 * 1024 * 1024; // 10GB
        if (gpuName.Contains("3070 Ti", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("3070Ti", StringComparison.OrdinalIgnoreCase))
            return 8L * 1024 * 1024 * 1024; // 8GB
        if (gpuName.Contains("3070", StringComparison.OrdinalIgnoreCase))
            return 8L * 1024 * 1024 * 1024; // 8GB

        // AMD Radeon RX 7000 series
        if (gpuName.Contains("7900 XTX", StringComparison.OrdinalIgnoreCase))
            return 24L * 1024 * 1024 * 1024; // 24GB
        if (gpuName.Contains("7900 XT", StringComparison.OrdinalIgnoreCase))
            return 20L * 1024 * 1024 * 1024; // 20GB
        if (gpuName.Contains("7800 XT", StringComparison.OrdinalIgnoreCase))
            return 16L * 1024 * 1024 * 1024; // 16GB
        if (gpuName.Contains("7700 XT", StringComparison.OrdinalIgnoreCase))
            return 12L * 1024 * 1024 * 1024; // 12GB

        // AMD Radeon RX 6000 series
        if (gpuName.Contains("6900", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("6800", StringComparison.OrdinalIgnoreCase))
            return 16L * 1024 * 1024 * 1024; // 16GB
        if (gpuName.Contains("6700", StringComparison.OrdinalIgnoreCase))
            return 12L * 1024 * 1024 * 1024; // 12GB

        // For unknown GPUs, use heuristic: if WMI reports exactly 4GB (overflow), 
        // assume 8GB as a conservative middle-ground estimate
        if (reportedVram >= 4290772992 && reportedVram <= 4294967296) // Near uint32 max
            return 8L * 1024 * 1024 * 1024; // Assume 8GB

        return reportedVram;
    }
}
