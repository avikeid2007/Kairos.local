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
                case "/":
                    await HandleHomeAsync(response);
                    break;
                    
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
    
    private async Task HandleHomeAsync(HttpListenerResponse response)
    {
        var modelName = _modelManager.ActiveModel?.Name ?? "No model loaded";
        var modelCount = _modelManager.Models.Count(m => m.IsDownloaded);
        
        var html = $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>KaiROS AI - Local API</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{
            font-family: 'Segoe UI', system-ui, sans-serif;
            background: linear-gradient(135deg, #0f0f23 0%, #1a1a3e 50%, #0f0f23 100%);
            color: #e0e0e0;
            min-height: 100vh;
            padding: 40px 20px;
        }}
        .container {{ max-width: 800px; margin: 0 auto; }}
        .header {{
            text-align: center;
            margin-bottom: 40px;
        }}
        .logo {{
            font-size: 3rem;
            margin-bottom: 10px;
        }}
        h1 {{
            font-size: 2.5rem;
            background: linear-gradient(90deg, #667eea 0%, #764ba2 100%);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            margin-bottom: 10px;
        }}
        .subtitle {{ color: #888; font-size: 1.1rem; }}
        .status-card {{
            background: rgba(255,255,255,0.05);
            border: 1px solid rgba(255,255,255,0.1);
            border-radius: 12px;
            padding: 24px;
            margin-bottom: 24px;
        }}
        .status-indicator {{
            display: inline-flex;
            align-items: center;
            gap: 8px;
            padding: 8px 16px;
            background: rgba(76, 175, 80, 0.2);
            border-radius: 20px;
            color: #4caf50;
            font-weight: 600;
        }}
        .dot {{ width: 10px; height: 10px; background: #4caf50; border-radius: 50%; animation: pulse 2s infinite; }}
        @keyframes pulse {{ 0%, 100% {{ opacity: 1; }} 50% {{ opacity: 0.5; }} }}
        .endpoints {{ margin-top: 30px; }}
        .endpoint {{
            background: rgba(255,255,255,0.03);
            border: 1px solid rgba(255,255,255,0.08);
            border-radius: 8px;
            padding: 16px;
            margin-bottom: 12px;
        }}
        .method {{
            display: inline-block;
            padding: 4px 10px;
            border-radius: 4px;
            font-weight: 700;
            font-size: 0.85rem;
            margin-right: 10px;
        }}
        .get {{ background: #2196f3; color: white; }}
        .post {{ background: #4caf50; color: white; }}
        .path {{ font-family: 'Consolas', monospace; color: #fff; }}
        .desc {{ color: #888; margin-top: 8px; font-size: 0.9rem; }}
        code {{
            background: rgba(0,0,0,0.3);
            padding: 2px 6px;
            border-radius: 4px;
            font-family: 'Consolas', monospace;
            color: #e0e0e0;
        }}
        pre {{
            background: rgba(0,0,0,0.4);
            padding: 16px;
            border-radius: 8px;
            overflow-x: auto;
            margin-top: 16px;
            font-size: 0.85rem;
        }}
        .footer {{
            text-align: center;
            color: #666;
            margin-top: 40px;
            font-size: 0.9rem;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <div class=""logo"">🧠</div>
            <h1>KaiROS AI</h1>
            <p class=""subtitle"">Local LLM API Server</p>
        </div>
        
        <div class=""status-card"">
            <div class=""status-indicator"">
                <span class=""dot""></span> API Running
            </div>
            <p style=""margin-top: 16px;"">
                <strong>Active Model:</strong> <code>{modelName}</code><br>
                <strong>Available Models:</strong> {modelCount}
            </p>
        </div>
        
        <div class=""endpoints"">
            <h2 style=""margin-bottom: 16px;"">📡 API Endpoints</h2>
            
            <div class=""endpoint"">
                <span class=""method get"">GET</span>
                <span class=""path"">/health</span>
                <p class=""desc"">Check API status and loaded model</p>
            </div>
            
            <div class=""endpoint"">
                <span class=""method get"">GET</span>
                <span class=""path"">/models</span>
                <p class=""desc"">List available models</p>
            </div>
            
            <div class=""endpoint"">
                <span class=""method post"">POST</span>
                <span class=""path"">/chat</span>
                <p class=""desc"">Send a message and get a complete response</p>
            </div>
            
            <div class=""endpoint"">
                <span class=""method post"">POST</span>
                <span class=""path"">/chat/stream</span>
                <p class=""desc"">Send a message and get streaming response (SSE)</p>
            </div>
        </div>
        
        <div class=""status-card"" style=""margin-top: 30px;"">
            <h3 style=""margin-bottom: 12px;"">💡 Example Request</h3>
            <pre>POST /chat
Content-Type: application/json

{{
  ""messages"": [
    {{ ""role"": ""user"", ""content"": ""Hello!"" }}
  ]
}}</pre>
        </div>
        
        <div class=""footer"">
            <p>KaiROS AI v1.0 • Running on localhost:{Port}</p>
        </div>
    </div>
</body>
</html>";

        response.ContentType = "text/html";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
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
        // Parse request body
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
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
