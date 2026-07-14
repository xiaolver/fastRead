using System.Text;
using System.Runtime.InteropServices;
using FastRead.Core;
using FastRead.Infrastructure;

namespace FastRead.UI;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _apiUrl = new();
    private readonly TextBox _model = new();
    private readonly TextBox _apiKey = new();
    private readonly NumericUpDown _summaryLength = new();
    private readonly NumericUpDown _explanationLength = new();
    private readonly TextBox _summaryHotkey = new();
    private readonly TextBox _explanationHotkey = new();
    private readonly NumericUpDown _timeout = new();
    private readonly ComboBox _outputLanguage = new();
    private readonly CheckBox _startWithWindows = new();
    private readonly Label _status = new();
    private readonly Button _testButton = new();
    private readonly Button _saveButton = new();
    private readonly CredentialStore _credentialStore;
    private readonly LlmClient _llmClient;
    private readonly Func<AppSettings, string?, string?> _save;

    public SettingsForm(
        AppSettings settings,
        CredentialStore credentialStore,
        LlmClient llmClient,
        Func<AppSettings, string?, string?> save)
    {
        _credentialStore = credentialStore;
        _llmClient = llmClient;
        _save = save;

        Text = "FastRead 设置";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(620, 510);
        Size = new Size(680, 570);
        Font = new Font("Microsoft YaHei UI", 9F);
        MaximizeBox = false;

        ConfigureNumeric(_summaryLength, 50, 20000, settings.SummaryMaxChars);
        ConfigureNumeric(_explanationLength, 50, 20000, settings.ExplanationMaxChars);
        ConfigureNumeric(_timeout, 5, 300, settings.RequestTimeoutSeconds);
        _apiUrl.Text = settings.ApiUrl;
        _model.Text = settings.Model;
        _apiKey.UseSystemPasswordChar = true;
        _apiKey.PlaceholderText = HasStoredKey() ? "已安全保存；留空表示不修改" : "输入 API Key";
        _summaryHotkey.Text = settings.SummaryHotkey;
        _explanationHotkey.Text = settings.ExplanationHotkey;
        _startWithWindows.Checked = settings.StartWithWindows;
        _outputLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
        _outputLanguage.Items.AddRange(["简体中文", "English"]);
        _outputLanguage.SelectedIndex = settings.OutputLanguage.Equals("en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        CaptureHotkey(_summaryHotkey);
        CaptureHotkey(_explanationHotkey);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 11,
            Padding = new Padding(18),
            AutoScroll = true
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(grid, 0, "API 地址", _apiUrl);
        AddRow(grid, 1, "模型名称", _model);

        var keyPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var clearKey = new Button { Text = "清除密钥", AutoSize = true };
        clearKey.Click += (_, _) => ClearKey();
        keyPanel.Controls.Add(_apiKey, 0, 0);
        keyPanel.Controls.Add(clearKey, 1, 0);
        AddRow(grid, 2, "API Key", keyPanel);
        AddRow(grid, 3, "总结最大字符数", _summaryLength);
        AddRow(grid, 4, "解释最大字符数", _explanationLength);
        AddRow(grid, 5, "总结快捷键", _summaryHotkey);
        AddRow(grid, 6, "解释快捷键", _explanationHotkey);
        AddRow(grid, 7, "请求超时（秒）", _timeout);
        AddRow(grid, 8, "输出语言", _outputLanguage);
        AddRow(grid, 9, "常规", _startWithWindows);
        _startWithWindows.Text = "随 Windows 启动";

        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Color.DimGray;
        _status.Text = "快捷键输入框获得焦点后，直接按下组合键即可。";
        grid.Controls.Add(_status, 0, 10);
        grid.SetColumnSpan(_status, 2);

        _testButton.Text = "测试连接";
        _testButton.AutoSize = true;
        _testButton.Click += async (_, _) => await TestConnectionAsync();
        _saveButton.Text = "保存";
        _saveButton.AutoSize = true;
        _saveButton.Click += (_, _) => SaveAndClose();
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            Padding = new Padding(12, 6, 12, 6),
            FlowDirection = FlowDirection.RightToLeft
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_saveButton);
        buttons.Controls.Add(_testButton);

        Controls.Add(grid);
        Controls.Add(buttons);
        AcceptButton = _saveButton;
        CancelButton = cancel;
    }

    private AppSettings BuildSettings() => new()
    {
        ApiUrl = _apiUrl.Text.Trim(),
        Model = _model.Text.Trim(),
        SummaryMaxChars = (int)_summaryLength.Value,
        ExplanationMaxChars = (int)_explanationLength.Value,
        SummaryHotkey = _summaryHotkey.Text.Trim(),
        ExplanationHotkey = _explanationHotkey.Text.Trim(),
        RequestTimeoutSeconds = (int)_timeout.Value,
        StartWithWindows = _startWithWindows.Checked,
        OutputLanguage = _outputLanguage.SelectedIndex == 1 ? "en" : "zh-CN"
    };

    private void SaveAndClose()
    {
        var settings = BuildSettings();
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            ShowStatus(string.Join("\n", errors), true);
            return;
        }

        var error = _save(settings, string.IsNullOrWhiteSpace(_apiKey.Text) ? null : _apiKey.Text.Trim());
        if (error is not null)
        {
            ShowStatus(error, true);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task TestConnectionAsync()
    {
        var settings = BuildSettings();
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            ShowStatus(string.Join("\n", errors), true);
            return;
        }
        var key = string.IsNullOrWhiteSpace(_apiKey.Text) ? ReadStoredKey() : _apiKey.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowStatus("请先输入 API Key。", true);
            return;
        }

        SetBusy(true);
        ShowStatus("正在连接模型服务…", false);
        try
        {
            var result = new StringBuilder();
            var messages = new[]
            {
                new ChatMessage("system", "你是连接测试助手。"),
                new ChatMessage("user", "请只回复：连接成功")
            };
            var options = new LlmRequestOptions(settings.ApiUrl, settings.Model, key, 50, settings.RequestTimeoutSeconds);
            await _llmClient.StreamCompletionAsync(messages, options, part => result.Append(part), CancellationToken.None);
            ShowStatus($"连接成功：{result.ToString().Trim()}", false);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ClearKey()
    {
        try
        {
            _credentialStore.DeleteApiKey();
            _apiKey.Clear();
            _apiKey.PlaceholderText = "输入 API Key";
            ShowStatus("已从 Windows 凭据管理器清除 API Key。", false);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, true);
        }
    }

    private bool HasStoredKey()
    {
        try { return !string.IsNullOrWhiteSpace(_credentialStore.ReadApiKey()); }
        catch { return false; }
    }

    private string? ReadStoredKey()
    {
        try { return _credentialStore.ReadApiKey(); }
        catch (Exception ex) { ShowStatus(ex.Message, true); return null; }
    }

    private void SetBusy(bool busy)
    {
        _testButton.Enabled = !busy;
        _saveButton.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void ShowStatus(string text, bool error)
    {
        _status.Text = text;
        _status.ForeColor = error ? Color.Firebrick : Color.DarkGreen;
    }

    private static void ConfigureNumeric(NumericUpDown input, int min, int max, int value)
    {
        input.Minimum = min;
        input.Maximum = max;
        input.Value = Math.Clamp(value, min, max);
        input.ThousandsSeparator = true;
        input.Dock = DockStyle.Left;
        input.Width = 160;
    }

    private static void AddRow(TableLayoutPanel grid, int row, string label, Control input)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var caption = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        input.Dock = input is NumericUpDown ? DockStyle.Left : DockStyle.Fill;
        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(input, 1, row);
    }

    private static void CaptureHotkey(TextBox textBox)
    {
        textBox.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu) return;
            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Alt) parts.Add("Alt");
            if (e.Shift) parts.Add("Shift");
            if (IsKeyDown(Keys.LWin) || IsKeyDown(Keys.RWin)) parts.Add("Win");
            parts.Add(e.KeyCode.ToString());
            textBox.Text = string.Join('+', parts);
        };
    }

    private static bool IsKeyDown(Keys key) => (GetKeyState((int)key) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}
