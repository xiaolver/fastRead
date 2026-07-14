using FastRead.Core;
using FastRead.Infrastructure;

var tests = new (string Name, Action Run)[]
{
    ("Hotkey parses and normalizes", TestHotkey),
    ("Hotkey rejects unsafe key", TestInvalidHotkey),
    ("Settings validates duplicate shortcuts", TestDuplicateHotkeys),
    ("Settings allows loopback HTTP", TestLoopbackHttp),
    ("Settings rejects remote HTTP", TestRemoteHttp),
    ("Prompt contains requested task and limit", TestPrompt),
    ("Prompt always requests Simplified Chinese", TestChinesePrompt),
    ("Prompt supports English configuration", TestEnglishPrompt),
    ("MiniMax is the default provider", TestMiniMaxDefaults),
    ("SSE delta parser extracts content", TestDeltaParser),
    ("Mislabeled SSE response is recognized", TestMislabeledSse),
    ("JSON response parser extracts content", TestResponseParser),
    ("JSON array content is joined", TestArrayContent),
    ("MiniMax base response error is extracted", TestMiniMaxError),
    ("Version base URL is expanded", TestEndpointExpansion),
    ("Error parser extracts API message", TestErrorParser)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL  {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"\n{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void TestHotkey()
{
    Assert(HotkeyDefinition.TryParse("alt+ctrl+s", out var hotkey, out _), "Expected valid hotkey");
    Equal("Ctrl+Alt+S", hotkey!.ToString());
}

static void TestInvalidHotkey()
{
    Assert(!HotkeyDefinition.TryParse("S", out _, out _), "Unmodified key must be rejected");
    Assert(!HotkeyDefinition.TryParse("Ctrl+Alt", out _, out _), "Missing ordinary key must be rejected");
}

static void TestDuplicateHotkeys()
{
    var settings = new AppSettings { SummaryHotkey = "Ctrl+Alt+S", ExplanationHotkey = "Ctrl+Alt+S" };
    Assert(settings.Validate().Any(e => e.Contains("不能相同")), "Expected duplicate error");
}

static void TestLoopbackHttp()
{
    var settings = new AppSettings { ApiUrl = "http://127.0.0.1:11434/v1/chat/completions" };
    Assert(!settings.Validate().Any(e => e.Contains("HTTPS")), "Loopback HTTP should be allowed");
}

static void TestRemoteHttp()
{
    var settings = new AppSettings { ApiUrl = "http://example.com/v1/chat/completions" };
    Assert(settings.Validate().Any(e => e.Contains("HTTPS")), "Remote HTTP should be rejected");
}

static void TestPrompt()
{
    var messages = PromptFactory.Create(ActionKind.Explain, "quantum text", 321);
    Assert(messages[1].Content.Contains("详细解释"), "Explain instruction missing");
    Assert(messages[1].Content.Contains("321"), "Limit missing");
    Assert(messages[1].Content.Contains("quantum text"), "Source text missing");
}

static void TestChinesePrompt()
{
    var messages = PromptFactory.Create(ActionKind.Summarize, "An English source paragraph.", 500);
    Assert(messages[0].Content.Contains("必须使用简体中文"), "System prompt must force Chinese output");
    Assert(messages[1].Content.Contains("简体中文") && messages[1].Content.Contains("总结"), "Summary prompt must force Chinese output");
}

static void TestEnglishPrompt()
{
    var messages = PromptFactory.Create(ActionKind.Summarize, "中文原文", 500, "en");
    Assert(messages[0].Content.Contains("必须使用 English"), "English language instruction missing");
    Assert(messages[1].Content.Contains("用 English"), "English task instruction missing");
}

static void TestMiniMaxDefaults()
{
    var settings = new AppSettings();
    Equal("https://api.minimaxi.com/v1/chat/completions", settings.ApiUrl);
    Equal("MiniMax-M3", settings.Model);
}

static void TestDeltaParser()
{
    var value = ResponseParser.TryExtractDeltaContent("{\"choices\":[{\"delta\":{\"content\":\"你好\"}}]}");
    Equal("你好", value);
}

static void TestResponseParser()
{
    var value = ResponseParser.ExtractMessageContent("{\"choices\":[{\"message\":{\"content\":\"完成\"}}]}");
    Equal("完成", value);
}

static void TestMislabeledSse()
{
    const string payload = "data: {\"choices\":[{\"delta\":{\"content\":\"你\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"好\"}}]}\n\ndata: [DONE]\n";
    Assert(ResponseParser.LooksLikeEventStream(payload), "SSE payload should be detected regardless of Content-Type");
}

static void TestArrayContent()
{
    var value = ResponseParser.ExtractMessageContent("{\"choices\":[{\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"第一\"},{\"type\":\"text\",\"text\":\"第二\"}]}}]}");
    Equal("第一第二", value);
}

static void TestMiniMaxError()
{
    var value = ResponseParser.TryExtractStructuredError("{\"base_resp\":{\"status_code\":1004,\"status_msg\":\"model not found\"}}");
    Equal("model not found", value);
}

static void TestEndpointExpansion()
{
    Equal("https://api.minimaxi.com/v1/chat/completions", LlmClient.ResolveEndpoint("https://api.minimaxi.com/v1").ToString().TrimEnd('/'));
    Equal("https://example.com/custom/chat", LlmClient.ResolveEndpoint("https://example.com/custom/chat").ToString().TrimEnd('/'));
}

static void TestErrorParser()
{
    Equal("bad key", ResponseParser.ExtractErrorMessage("{\"error\":{\"message\":\"bad key\"}}"));
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'");
}
