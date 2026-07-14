using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FastRead.Core;

namespace FastRead.Infrastructure;

internal sealed record LlmRequestOptions(
    string ApiUrl,
    string Model,
    string ApiKey,
    int MaxChars,
    int TimeoutSeconds);

internal sealed class LlmClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public LlmClient(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FastRead/1.0");
    }

    public async Task StreamCompletionAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmRequestOptions options,
        Action<string> onContent,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        var body = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }),
            ["temperature"] = 0.2,
            ["max_tokens"] = Math.Clamp(options.MaxChars * 2, 128, 32768),
            ["stream"] = true
        };

        if (options.Model.Equals("MiniMax-M3", StringComparison.OrdinalIgnoreCase))
        {
            body["thinking"] = new { type = "disabled" };
            body["reasoning_split"] = true;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, ResolveEndpoint(options.ApiUrl))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmException("请求超时。请检查网络，或在设置中增大超时时间。");
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException($"无法连接模型服务：{ex.Message}", ex);
        }

        using (response)
        {
            try
            {
                if (!response.IsSuccessStatusCode)
                    throw await CreateResponseExceptionAsync(response, options.ApiKey, timeout.Token);

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    await ReadEventStreamAsync(response, onContent, timeout.Token);
                }
                else
                {
                    var payload = await response.Content.ReadAsStringAsync(timeout.Token);
                    if (ResponseParser.LooksLikeEventStream(payload))
                    {
                        ReadBufferedEventStream(payload, onContent);
                    }
                    else
                    {
                        var content = ResponseParser.ExtractMessageContent(payload);
                        if (string.IsNullOrEmpty(content))
                            throw new LlmException("模型服务返回成功，但响应中没有可显示的内容。");
                        onContent(content);
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new LlmException("请求超时。请检查网络，或在设置中增大超时时间。");
            }
        }
    }

    private static async Task ReadEventStreamAsync(
        HttpResponseMessage response,
        Action<string> onContent,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var receivedContent = false;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].TrimStart();
            if (data == "[DONE]") break;
            if (data.Length == 0) continue;

            var content = ResponseParser.TryExtractDeltaContent(data);
            if (!string.IsNullOrEmpty(content))
            {
                receivedContent = true;
                onContent(content);
            }
            else if (ResponseParser.TryExtractStructuredError(data) is { } error)
            {
                throw new LlmException($"模型服务返回错误：{error}");
            }
        }

        if (!receivedContent)
            throw new LlmException("模型服务结束了响应，但没有返回可显示的内容。");
    }

    private static void ReadBufferedEventStream(string payload, Action<string> onContent)
    {
        var receivedContent = false;
        using var reader = new StringReader(payload);
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].TrimStart();
            if (data.Length == 0 || data == "[DONE]") continue;
            var content = ResponseParser.TryExtractDeltaContent(data);
            if (!string.IsNullOrEmpty(content))
            {
                receivedContent = true;
                onContent(content);
            }
            else if (ResponseParser.TryExtractStructuredError(data) is { } error)
            {
                throw new LlmException($"模型服务返回错误：{error}");
            }
        }
        if (!receivedContent)
            throw new LlmException("模型服务返回了流式数据，但其中没有可显示的内容。");
    }

    internal static Uri ResolveEndpoint(string apiUrl)
    {
        var builder = new UriBuilder(apiUrl);
        var path = builder.Path.TrimEnd('/');
        if (path.Equals("/v1", StringComparison.OrdinalIgnoreCase))
            builder.Path = "/v1/chat/completions";
        return builder.Uri;
    }

    private static async Task<LlmException> CreateResponseExceptionAsync(
        HttpResponseMessage response,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        detail = ResponseParser.ExtractErrorMessage(detail);
        if (!string.IsNullOrEmpty(apiKey))
            detail = detail.Replace(apiKey, "***", StringComparison.Ordinal);
        if (detail.Length > 500) detail = detail[..500] + "…";

        var prefix = response.StatusCode switch
        {
            HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect or
                HttpStatusCode.PermanentRedirect =>
                $"API 地址发生重定向，请填写完整的 Chat Completions 地址。服务端目标：{response.Headers.Location}",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "认证失败，请检查 API Key、模型权限和 API 地址。",
            (HttpStatusCode)429 => "请求过于频繁或账户额度不足。",
            HttpStatusCode.NotFound => "接口或模型不存在，请检查 API 地址和模型名称。",
            _ => $"模型服务返回错误 {(int)response.StatusCode} ({response.ReasonPhrase})。"
        };
        return new LlmException(string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix}\n{detail}");
    }

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }
}

internal static class ResponseParser
{
    public static bool LooksLikeEventStream(string payload)
    {
        using var reader = new StringReader(payload);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrWhiteSpace(line)) return false;
        }
        return false;
    }

    public static string? TryExtractDeltaContent(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                return null;
            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("content", out var deltaContent))
                return ReadContent(deltaContent);
            if (choice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var messageContent))
                return ReadContent(messageContent);
            if (choice.TryGetProperty("text", out var text))
                return ReadContent(text);
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static string? ExtractMessageContent(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var messageContent))
                    return ReadContent(messageContent);
                if (choice.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var deltaContent))
                    return ReadContent(deltaContent);
                if (choice.TryGetProperty("text", out var text))
                    return ReadContent(text);
            }
            if (root.TryGetProperty("output_text", out var outputText))
                return ReadContent(outputText);
            if (TryExtractStructuredError(root) is { } error)
                throw new LlmException($"模型服务返回错误：{error}");

            var keys = root.ValueKind == JsonValueKind.Object
                ? string.Join(", ", root.EnumerateObject().Take(8).Select(p => p.Name))
                : root.ValueKind.ToString();
            throw new LlmException($"模型服务返回了 JSON，但格式不是 Chat Completions（顶层字段：{keys}）。请检查 API 地址是否完整。 ");
        }
        catch (JsonException ex)
        {
            throw new LlmException("模型服务返回的内容不是有效 JSON，也不是可识别的 SSE 数据。请检查 API 地址是否指向 /v1/chat/completions。", ex);
        }
    }

    public static string? TryExtractStructuredError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryExtractStructuredError(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                    return message.GetString() ?? string.Empty;
                if (error.ValueKind == JsonValueKind.String) return error.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Fall back to a sanitized plain-text snippet.
        }
        return body.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static string? TryExtractStructuredError(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
                return message.GetString();
            if (error.ValueKind == JsonValueKind.String) return error.GetString();
        }
        if (root.TryGetProperty("base_resp", out var baseResponse) &&
            baseResponse.ValueKind == JsonValueKind.Object &&
            baseResponse.TryGetProperty("status_code", out var code) &&
            code.TryGetInt32(out var statusCode) && statusCode != 0)
        {
            var message = baseResponse.TryGetProperty("status_msg", out var statusMessage)
                ? statusMessage.GetString()
                : null;
            return string.IsNullOrWhiteSpace(message) ? $"MiniMax 状态码 {statusCode}" : message;
        }
        return null;
    }

    private static string? ReadContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) return content.GetString();
        if (content.ValueKind != JsonValueKind.Array) return null;

        var parts = new List<string>();
        foreach (var item in content.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                parts.Add(item.GetString() ?? string.Empty);
            }
            else if (item.ValueKind == JsonValueKind.Object &&
                     item.TryGetProperty("text", out var text) &&
                     text.ValueKind == JsonValueKind.String)
            {
                parts.Add(text.GetString() ?? string.Empty);
            }
        }
        return parts.Count == 0 ? null : string.Concat(parts);
    }
}

internal sealed class LlmException : Exception
{
    public LlmException(string message) : base(message) { }
    public LlmException(string message, Exception innerException) : base(message, innerException) { }
}
