using System.Net;

namespace FastRead.Core;

internal sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string ApiUrl { get; set; } = "https://api.minimaxi.com/v1/chat/completions";
    public string Model { get; set; } = "MiniMax-M3";
    public string OutputLanguage { get; set; } = "zh-CN";
    public int SummaryMaxChars { get; set; } = 500;
    public int ExplanationMaxChars { get; set; } = 1500;
    public string SummaryHotkey { get; set; } = "Ctrl+Alt+S";
    public string ExplanationHotkey { get; set; } = "Ctrl+Alt+E";
    public int RequestTimeoutSeconds { get; set; } = 60;
    public bool StartWithWindows { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!Uri.TryCreate(ApiUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add("API 地址必须是有效的 HTTP/HTTPS 绝对地址。");
        }
        else if (uri.Scheme == Uri.UriSchemeHttp && !IsLoopback(uri.Host))
        {
            errors.Add("为保护密钥，HTTP 地址仅允许 localhost 或回环地址；远程服务请使用 HTTPS。");
        }

        if (string.IsNullOrWhiteSpace(Model))
            errors.Add("模型名称不能为空。");
        if (OutputLanguage is not ("zh-CN" or "en"))
            errors.Add("输出语言只能选择简体中文或 English。");
        if (SummaryMaxChars is < 50 or > 20_000)
            errors.Add("总结长度必须在 50～20,000 字符之间。");
        if (ExplanationMaxChars is < 50 or > 20_000)
            errors.Add("解释长度必须在 50～20,000 字符之间。");
        if (RequestTimeoutSeconds is < 5 or > 300)
            errors.Add("请求超时必须在 5～300 秒之间。");

        if (!HotkeyDefinition.TryParse(SummaryHotkey, out var summary, out var summaryError))
            errors.Add($"总结快捷键无效：{summaryError}");
        if (!HotkeyDefinition.TryParse(ExplanationHotkey, out var explanation, out var explanationError))
            errors.Add($"解释快捷键无效：{explanationError}");
        if (summary is not null && explanation is not null && summary == explanation)
            errors.Add("总结快捷键和解释快捷键不能相同。");

        return errors;
    }

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
}
