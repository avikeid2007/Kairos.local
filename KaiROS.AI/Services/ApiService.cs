using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KaiROS.AI.Models;

namespace KaiROS.AI.Services;

public interface IApiService
{
    bool IsRunning { get; }
    int Port { get; }
    Task StartAsync(int port = 5000);
    Task StopAsync();
    event EventHandler<string>? RequestReceived;
}

public class ApiService : IApiService
{
    private readonly IChatService _chatService;
    private readonly IModelManagerService _modelManager;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    
    public bool IsRunning { get; private set; }
    public int Port { get; private set; }
    
    public event EventHandler<string>? RequestReceived;
    
    public ApiService(IChatService chatService, IModelManagerService modelManager)
    {
        _chatService = chatService;
        _modelManager = modelManager;
    }
    
    public async Task StartAsync(int port = 5000)
    {
        if (IsRunning) return;
        
        Port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        
        try
        {
            _listener.Start();
            IsRunning = true;
            _cts = new CancellationTokenSource();
            
            // Start listening for requests
            _listenerTask = ListenAsync(_cts.Token);
            
            System.Diagnostics.Debug.WriteLine($"API Server started on http://localhost:{port}/");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start API server: {ex.Message}");
            IsRunning = false;
        }
        
        await Task.CompletedTask;
    }
    
    public async Task StopAsync()
    {
        if (!IsRunning) return;
        
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
        
        if (_listenerTask != null)
        {
            try { await _listenerTask; } catch { }
        }
        
        IsRunning = false;
        System.Diagnostics.Debug.WriteLine("API Server stopped");
    }
    
    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = HandleRequestAsync(context, ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Listener error: {ex.Message}");
            }
        }
    }
    
    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        var response = context.Response;
        
        var path = request.Url?.AbsolutePath ?? "/";
        var method = request.HttpMethod;
        
        RequestReceived?.Invoke(this, $"{method} {path}");
        
        try
        {
            // Enable CORS
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
            
            if (method == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }
            
            // Route requests
            switch (path.ToLowerInvariant())
            {
                case "/health":
                    await HandleHealthAsync(response);
                    break;
                    
                case "/models":
                    await HandleModelsAsync(response);
                    break;
                    
                case "/chat":
                    await HandleChatAsync(request, response, streaming: false, ct);
                    break;
                    
                case "/chat/stream":
                    await HandleChatAsync(request, response, streaming: true, ct);
                    break;
                    
                default:
                    await SendErrorAsync(response, 404, "Endpoint not found");
                    break;
            }
        }
        catch (Exception ex)
        {
            await SendErrorAsync(response, 500, ex.Message);
        }
    }
    
    private async Task HandleHealthAsync(HttpListenerResponse response)
    {
        var health = new
        {
            Status = "ok",
            Model = _modelManager.ActiveModel?.Name ?? "none",
            Version = "1.0.0"
        };
        await SendJsonAsync(response, health);
    }
    
    private async Task HandleModelsAsync(HttpListenerResponse response)
    {
        var models = new ModelsListResponse
        {
            Data = _modelManager.Models
                .Where(m => m.IsDownloaded)
                .Select(m => new Models.ModelInfo
                {
                    Id = m.Name,
                    OwnedBy = "kairos-local"
                })
                .ToList()
        };
        await SendJsonAsync(response, models);
    }
    
    private async Task HandleChatAsync(HttpListenerRequest request, HttpListenerResponse response, bool streaming, CancellationToken ct)
    {
        if (_modelManager.ActiveModel == null)
        {
            await SendErrorAsync(response, 503, "No model loaded. Please load a model first.");
            return;
        }
        
        // Parse request body
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var body = await reader.ReadToEndAsync();
        
        SimpleChatRequest? chatRequest;
        try
        {
            chatRequest = JsonSerializer.Deserialize<SimpleChatRequest>(body, JsonOptions);
        }
        catch
        {
            await SendErrorAsync(response, 400, "Invalid JSON request body");
            return;
        }
        
        if (chatRequest?.Messages == null || chatRequest.Messages.Count == 0)
        {
            await SendErrorAsync(response, 400, "Messages array is required");
            return;
        }
        
        // Convert to internal message format
        var messages = chatRequest.Messages.Select(m => new ChatMessage
        {
            Role = m.Role switch
            {
                "user" => ChatRole.User,
                "assistant" => ChatRole.Assistant,
                "system" => ChatRole.System,
                _ => ChatRole.User
            },
            Content = m.Content
        }).ToList();
        
        if (streaming)
        {
            await HandleStreamingResponseAsync(response, messages, ct);
        }
        else
        {
            await HandleNonStreamingResponseAsync(response, messages, ct);
        }
    }
    
    private async Task HandleStreamingResponseAsync(HttpListenerResponse response, List<ChatMessage> messages, CancellationToken ct)
    {
        response.ContentType = "text/event-stream";
        response.Headers.Add("Cache-Control", "no-cache");
        
        var modelName = _modelManager.ActiveModel?.Name ?? "unknown";
        
        using var writer = new StreamWriter(response.OutputStream, Encoding.UTF8);
        
        await foreach (var token in _chatService.GenerateResponseStreamAsync(messages, ct))
        {
            var chunk = new { content = token };
            var json = JsonSerializer.Serialize(chunk, JsonOptions);
            await writer.WriteAsync($"data: {json}\n\n");
            await writer.FlushAsync();
        }
        
        // Send done signal
        await writer.WriteAsync("data: [DONE]\n\n");
        await writer.FlushAsync();
    }
    
    private async Task HandleNonStreamingResponseAsync(HttpListenerResponse response, List<ChatMessage> messages, CancellationToken ct)
    {
        var fullResponse = await _chatService.GenerateResponseAsync(messages, ct);
        var modelName = _modelManager.ActiveModel?.Name ?? "unknown";
        
        var result = new SimpleChatResponse
        {
            Model = modelName,
            Content = fullResponse,
            TokenCount = fullResponse.Length / 4
        };
        
        await SendJsonAsync(response, result);
    }
    
    private async Task SendJsonAsync<T>(HttpListenerResponse response, T data, int statusCode = 200)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
    
    private async Task SendErrorAsync(HttpListenerResponse response, int statusCode, string message)
    {
        var error = new ApiErrorResponse
        {
            Error = new ApiError { Message = message }
        };
        await SendJsonAsync(response, error, statusCode);
    }
}
