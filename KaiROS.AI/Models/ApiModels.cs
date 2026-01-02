namespace KaiROS.AI.Models;

/// <summary>
/// OpenAI-compatible API request/response models
/// </summary>

// Chat Completion Request
public class ChatCompletionRequest
{
    public string Model { get; set; } = string.Empty;
    public List<ChatCompletionMessage> Messages { get; set; } = new();
    public bool Stream { get; set; } = false;
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2048;
}

public class ChatCompletionMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}

// Chat Completion Response
public class ChatCompletionResponse
{
    public string Id { get; set; } = $"chatcmpl-{Guid.NewGuid():N}";
    public string Object { get; set; } = "chat.completion";
    public long Created { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public string Model { get; set; } = string.Empty;
    public List<ChatCompletionChoice> Choices { get; set; } = new();
    public UsageInfo Usage { get; set; } = new();
}

public class ChatCompletionChoice
{
    public int Index { get; set; }
    public ChatCompletionMessage Message { get; set; } = new();
    public string? FinishReason { get; set; }
}

public class UsageInfo
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

// Streaming Response (SSE format)
public class ChatCompletionChunk
{
    public string Id { get; set; } = $"chatcmpl-{Guid.NewGuid():N}";
    public string Object { get; set; } = "chat.completion.chunk";
    public long Created { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public string Model { get; set; } = string.Empty;
    public List<ChatCompletionChunkChoice> Choices { get; set; } = new();
}

public class ChatCompletionChunkChoice
{
    public int Index { get; set; }
    public ChatCompletionDelta Delta { get; set; } = new();
    public string? FinishReason { get; set; }
}

public class ChatCompletionDelta
{
    public string? Role { get; set; }
    public string? Content { get; set; }
}

// Models List Response
public class ModelsListResponse
{
    public string Object { get; set; } = "list";
    public List<ModelInfo> Data { get; set; } = new();
}

public class ModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string Object { get; set; } = "model";
    public long Created { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public string OwnedBy { get; set; } = "kairos-local";
}

// Error Response
public class ApiErrorResponse
{
    public ApiError Error { get; set; } = new();
}

public class ApiError
{
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "invalid_request_error";
    public string? Code { get; set; }
}

// Simplified Request/Response (KaiROS format - no model parameter needed)
public class SimpleChatRequest
{
    public List<ChatCompletionMessage> Messages { get; set; } = new();
}

public class SimpleChatResponse
{
    public string Model { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int TokenCount { get; set; }
}

