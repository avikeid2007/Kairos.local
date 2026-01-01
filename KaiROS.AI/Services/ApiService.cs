using KaiROS.AI.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KaiROS.AI.Services;

public class ApiService
{
    private WebApplication? _app;
    private readonly IChatService _chatService;
    private readonly IModelManagerService _modelManager;
    private const int Port = 11435;

    public ApiService(IChatService chatService, IModelManagerService modelManager)
    {
        _chatService = chatService;
        _modelManager = modelManager;
    }

    public async Task StartAsync()
    {
        if (_app != null) return;

        try
        {
            var builder = WebApplication.CreateBuilder();

            // Configure Kestrel
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenLocalhost(Port);
            });

            // Add services
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            // Suppress logging to console (avoid messing with WPF stdout if any)
            builder.Logging.ClearProviders();
            builder.Logging.AddDebug();

            _app = builder.Build();

            _app.UseCors();

            // Endpoints
            _app.MapGet("/api/status", HandleStatus);
            _app.MapPost("/v1/chat/completions", HandleChatCompletions);
            _app.MapGet("/v1/models", HandleListModels); // Standard OpenAI endpoint

            await _app.StartAsync();
            System.Diagnostics.Debug.WriteLine($"API Server started on port {Port}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start API Server: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private IResult HandleStatus()
    {
        return Results.Ok(new
        {
            status = "running",
            model_loaded = _chatService.IsModelLoaded,
            active_model = _modelManager.ActiveModel?.Name,
            backend = _chatService.LastStats.BackendInUse ?? "Unknown"
        });
    }

    private IResult HandleListModels()
    {
        var models = _modelManager.Models
            .Where(m => m.IsDownloaded)
            .Select(m => new
            {
                id = m.Name,
                object_type = "model",
                created = 0,
                owned_by = "kairos-ai"
            });

        return Results.Ok(new { object_type = "list", data = models });
    }

    private async Task HandleChatCompletions(HttpContext context)
    {
        if (!_chatService.IsModelLoaded)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "No model loaded" });
            return;
        }

        try
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            var request = JsonSerializer.Deserialize<ChatCompletionRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (request?.Messages == null || !request.Messages.Any())
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "Messages are required" });
                return;
            }

            // Convert API messages to internal ChatMessage
            var messages = request.Messages.Select(m => new ChatMessage
            {
                Role = ParseRole(m.Role),
                Content = m.Content
            }).ToList();

            context.Response.Headers.Append("Content-Type", "text/event-stream");
            context.Response.Headers.Append("Cache-Control", "no-cache");
            context.Response.Headers.Append("Connection", "keep-alive");

            var cancellationToken = context.RequestAborted;
            var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var id = $"chatcmpl-{Guid.NewGuid()}";

            await foreach (var token in _chatService.GenerateResponseStreamAsync(messages, cancellationToken))
            {
                var chunk = new
                {
                    id,
                    @object = "chat.completion.chunk",
                    created,
                    model = _modelManager.ActiveModel?.Name,
                    choices = new[]
                    {
                        new
                        {
                            index = 0,
                            delta = new { content = token },
                            finish_reason = (string?)null
                        }
                    }
                };

                await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }

            // Send finish chunk
            var finishChunk = new
            {
                id,
                @object = "chat.completion.chunk",
                created,
                model = _modelManager.ActiveModel?.Name,
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new { },
                        finish_reason = "stop"
                    }
                }
            };
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(finishChunk)}\n\n", cancellationToken);
            await context.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        }
        catch (Exception ex)
        {
            // If we haven't started streaming, send error JSON
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { error = ex.Message });
            }
        }
    }

    private static ChatRole ParseRole(string role)
    {
        return role.ToLower() switch
        {
            "system" => ChatRole.System,
            "assistant" => ChatRole.Assistant,
            _ => ChatRole.User
        };
    }
}

// Request Models
public class ChatCompletionRequest
{
    public string Model { get; set; } = "";
    public List<MessageDto> Messages { get; set; } = new();
    public bool Stream { get; set; } = true;
}

public class MessageDto
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}
