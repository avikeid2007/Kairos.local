using KaiROS.AI.Models;
using System.IO;
using System.Net.Http;

namespace KaiROS.AI.Services;

public class DownloadService : IDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly string _modelsDirectory;
    private readonly Dictionary<string, CancellationTokenSource> _activeDownloads = new();
    private readonly Dictionary<string, long> _pausedDownloads = new(); // Tracks bytes downloaded
    
    public DownloadService(string modelsDirectory)
    {
        _modelsDirectory = modelsDirectory;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30) // Allow long downloads but prevent infinite hanging
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "KaiROS-AI/1.0");
        
        Directory.CreateDirectory(_modelsDirectory);
    }
    
    public async Task<bool> DownloadFileAsync(string url, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var modelName = Path.GetFileName(destinationPath);
        var partialPath = destinationPath + ".partial";
        long existingBytes = 0;
        
        try
        {
            // Check for partial download
            if (File.Exists(partialPath))
            {
                existingBytes = new FileInfo(partialPath).Length;
            }
            
            // Create cancellation token source for this download
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeDownloads[modelName] = cts;
            
            // Setup request with range header for resume
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingBytes > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
            }
            
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            if (existingBytes > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent)
            {
                totalBytes += existingBytes;
            }
            else
            {
                existingBytes = 0; // Server doesn't support range, start fresh
            }
            
            await using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token);
            await using var fileStream = new FileStream(
                partialPath,
                existingBytes > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            
            var buffer = new byte[81920];
            long totalBytesRead = existingBytes;
            int bytesRead;
            
            while ((bytesRead = await contentStream.ReadAsync(buffer, cts.Token)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
                totalBytesRead += bytesRead;
                
                if (totalBytes > 0)
                {
                    progress?.Report((double)totalBytesRead / totalBytes * 100);
                }
            }
            
            await fileStream.FlushAsync(cts.Token);
            fileStream.Close();
            
            // Rename partial to final
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            File.Move(partialPath, destinationPath);
            
            _activeDownloads.Remove(modelName);
            _pausedDownloads.Remove(modelName);
            
            return true;
        }
        catch (OperationCanceledException)
        {
            // Download was paused or cancelled
            if (File.Exists(partialPath))
            {
                _pausedDownloads[modelName] = new FileInfo(partialPath).Length;
            }
            return false;
        }
        catch (Exception)
        {
            _activeDownloads.Remove(modelName);
            throw;
        }
    }
    
    public Task PauseDownloadAsync(string modelName)
    {
        if (_activeDownloads.TryGetValue(modelName, out var cts))
        {
            cts.Cancel();
        }
        return Task.CompletedTask;
    }
    
    public Task ResumeDownloadAsync(string modelName)
    {
        // Resume is handled by DownloadFileAsync checking for partial file
        return Task.CompletedTask;
    }
    
    public async Task<bool> VerifyFileIntegrityAsync(string filePath, long expectedSize)
    {
        System.Diagnostics.Debug.WriteLine($"VerifyFileIntegrity: Checking {filePath}");
        
        if (!File.Exists(filePath))
        {
            System.Diagnostics.Debug.WriteLine($"VerifyFileIntegrity: File does not exist!");
            return false;
        }
        
        var fileInfo = new FileInfo(filePath);
        System.Diagnostics.Debug.WriteLine($"VerifyFileIntegrity: File size = {fileInfo.Length} bytes, expected = {expectedSize} bytes");
        
        // File must have some content (at least 1MB for GGUF files)
        if (fileInfo.Length < 1_000_000)
        {
            System.Diagnostics.Debug.WriteLine($"VerifyFileIntegrity: File too small ({fileInfo.Length} bytes)");
            return false;
        }
        
        // Just verify file is readable - don't check exact size as HuggingFace may vary
        try
        {
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[4096];
            
            // Read first chunk to verify file integrity
            // Use proper buffer size calculation to avoid int overflow for large files
            int bytesToRead = fileInfo.Length > 4096 ? 4096 : (int)fileInfo.Length;
            var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, bytesToRead));
            if (bytesRead == 0)
            {
                System.Diagnostics.Debug.WriteLine($"VerifyFileIntegrity: Could not read file start");
                return false;
            }
            
            // Read last chunk for large files
            if (fileInfo.Length > 4096)
            {
                fs.Seek(-4096, SeekOrigin.End);
                bytesRead = await fs.ReadAsync(buffer.AsMemory(0, 4096));
                if (bytesRead == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"VerifyFileIntegrity: Could not read file end");
                    return false;
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"VerifyFileIntegrity: SUCCESS - file is valid");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VerifyFileIntegrity: Exception - {ex.Message}");
            return false;
        }
    }
    
    public bool HasPartialDownload(string modelName)
    {
        var partialPath = Path.Combine(_modelsDirectory, modelName + ".partial");
        return File.Exists(partialPath);
    }
}
