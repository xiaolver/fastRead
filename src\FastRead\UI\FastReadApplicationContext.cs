using FastRead.Core;
using FastRead.Infrastructure;

namespace FastRead.UI;

internal sealed class FastReadApplicationContext : ApplicationContext, IDisposable
{
    private readonly SettingsStore _settingsStore = new();
    private readonly CredentialStore _credentialStore = new();
    private readonly SelectionCaptureService _captureService = new();
    private readonly LlmClient _llmClient = new();
    private readonly HotkeyWindow _hotkeyWindow = new();
    private readonly ResultForm _resultForm = new();
    private readonly NotifyIcon _trayIcon;
    private AppSettings _settings;
    private SettingsForm? _settingsForm;
    private CancellationTokenSource? _requestCancellation;
    private bool _capturing;
    private bool _disposed;

    public FastReadApplicationContext()
    {
        _settings = _settingsStore.Load();
        var menu = new ContextMenuStrip();
        menu.Items.Add("设置", null, (_, _) => ShowSettings());
        menu.Items.Add("使用说明", null, (_, _) => ShowHelp());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        _trayIcon = new NotifyIcon
        {
            Text = "FastRead 阅读助手",
            Icon = SystemIcons.Information,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowSettings();
        _hotkeyWindow.HotkeyPressed += async (_, action) => await HandleActionAsync(action);
        _resultForm.CancellationRequested += (_, _) => _requestCancellation?.Cancel();

        if (!_hotkeyWindow.TryRegister(_settings, out var error))
        {
            ShowBalloon("快捷键注册失败", error, ToolTipIcon.Warning);
        }
        else
        {
            ShowBalloon("FastRead 已启动",
                $"总结：{_settings.SummaryHotkey}\n解释：{_settings.ExplanationHotkey}", ToolTipIcon.Info);
        }

        if (string.IsNullOrWhiteSpace(TryReadApiKey()))
            ShowSettings();
    }

    private async Task HandleActionAsync(ActionKind action)
    {
        if (_capturing)
        {
            ShowBalloon("正在读取选区", "请稍候再按快捷键。", ToolTipIcon.Info);
            return;
        }

        var apiKey = TryReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ShowBalloon("需要配置", "请先在设置中填写 API Key。", ToolTipIcon.Warning);
            ShowSettings();
            return;
        }

        string selectedText;
        _capturing = true;
        try
        {
            selectedText = await _captureService.CaptureAsync(CancellationToken.None);
        }
        catch (SelectionCaptureException ex)
        {
            ShowBalloon("无法读取选区", ex.Message, ToolTipIcon.Warning);
            return;
        }
        catch (Exception ex)
        {
            ShowBalloon("读取选区失败", ex.Message, ToolTipIcon.Error);
            return;
        }
        finally
        {
            _capturing = false;
        }

        _requestCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _requestCancellation = cancellation;
        var title = action == ActionKind.Summarize ? "总结" : "解释";
        var maxChars = action == ActionKind.Summarize
            ? _settings.SummaryMaxChars
            : _settings.ExplanationMaxChars;
        _resultForm.BeginRequest(title);

        var written = 0;
        var hitLimit = false;
        try
        {
            var messages = PromptFactory.Create(action, selectedText, maxChars, _settings.OutputLanguage);
            var options = new LlmRequestOptions(
                _settings.ApiUrl,
                _settings.Model,
                apiKey,
                maxChars,
                _settings.RequestTimeoutSeconds);

            await _llmClient.StreamCompletionAsync(messages, options, part =>
            {
                if (hitLimit || cancellation.IsCancellationRequested) return;
                var remaining = maxChars - written;
                if (remaining <= 0)
                {
                    hitLimit = true;
                    cancellation.Cancel();
                    return;
                }
                var visible = part.Length <= remaining ? part : part[..remaining];
                written += visible.Length;
                _resultForm.Append(visible);
                if (visible.Length < part.Length || written >= maxChars)
                {
                    hitLimit = true;
                    cancellation.Cancel();
                }
            }, cancellation.Token);

            _resultForm.Complete(hitLimit);
        }
        catch (OperationCanceledException)
        {
            if (hitLimit) _resultForm.Complete(truncated: true);
            else _resultForm.Cancelled();
        }
        catch (LlmException ex)
        {
            _resultForm.ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            _resultForm.ShowError($"请求失败：{ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_requestCancellation, cancellation)) _requestCancellation = null;
            cancellation.Dispose();
        }
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settings.Clone(), _credentialStore, _llmClient, SaveSettings);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private string? SaveSettings(AppSettings candidate, string? newApiKey)
    {
        var oldSettings = _settings;
        if (!_hotkeyWindow.TryRegister(candidate, out var hotkeyError))
        {
            _hotkeyWindow.TryRegister(oldSettings, out _);
            return hotkeyError;
        }

        try
        {
            if (newApiKey is not null) _credentialStore.SaveApiKey(newApiKey);
            _settingsStore.Save(candidate);
            StartupManager.SetEnabled(candidate.StartWithWindows);
            _settings = candidate;
            ShowBalloon("设置已保存", "新的快捷键和模型配置已生效。", ToolTipIcon.Info);
            return null;
        }
        catch (Exception ex)
        {
            _hotkeyWindow.TryRegister(oldSettings, out _);
            try { _settingsStore.Save(oldSettings); } catch { /* Preserve the original save error. */ }
            return $"无法保存设置：{ex.Message}";
        }
    }

    private string? TryReadApiKey()
    {
        try { return _credentialStore.ReadApiKey(); }
        catch (Exception ex)
        {
            ShowBalloon("凭据读取失败", ex.Message, ToolTipIcon.Error);
            return null;
        }
    }

    private void ShowHelp()
    {
        MessageBox.Show(
            $"1. 在任意应用中选中文字。\n2. 按 {_settings.SummaryHotkey} 获取总结。\n3. 按 {_settings.ExplanationHotkey} 获取详细解释。\n\n" +
            "目标应用必须支持 Ctrl+C 复制。输出长度、模型和快捷键可在设置中调整。",
            "FastRead 使用说明",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = text;
        _trayIcon.BalloonTipIcon = icon;
        _trayIcon.ShowBalloonTip(3500);
    }

    private void ExitApplication()
    {
        _requestCancellation?.Cancel();
        _settingsForm?.Close();
        _resultForm.ClosePermanently();
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _trayIcon.Visible = false;
        base.ExitThreadCore();
    }

    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _hotkeyWindow.Dispose();
        _resultForm.Dispose();
        _settingsForm?.Dispose();
        _trayIcon.Dispose();
        _llmClient.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
