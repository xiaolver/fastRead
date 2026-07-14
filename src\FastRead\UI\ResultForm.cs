namespace FastRead.UI;

internal sealed class ResultForm : Form
{
    private readonly Label _statusLabel = new();
    private readonly RichTextBox _resultBox = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _copyButton = new();
    private readonly Button _cancelButton = new();
    private bool _isRunning;
    private bool _allowClose;

    public event EventHandler? CancellationRequested;

    public ResultForm()
    {
        Text = "FastRead";
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(520, 360);
        Size = new Size(720, 520);
        ShowInTaskbar = true;
        TopMost = true;
        Font = new Font("Microsoft YaHei UI", 9F);

        _statusLabel.AutoSize = true;
        _statusLabel.Font = new Font(Font, FontStyle.Bold);
        _statusLabel.Text = "准备就绪";
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 28;
        _progress.Dock = DockStyle.Fill;
        _progress.Visible = false;

        _resultBox.Dock = DockStyle.Fill;
        _resultBox.ReadOnly = true;
        _resultBox.BackColor = Color.White;
        _resultBox.BorderStyle = BorderStyle.FixedSingle;
        _resultBox.Font = new Font("Microsoft YaHei UI", 10.5F);
        _resultBox.DetectUrls = true;

        _copyButton.Text = "复制结果";
        _copyButton.AutoSize = true;
        _copyButton.Enabled = false;
        _copyButton.Click += (_, _) => CopyResult();

        _cancelButton.Text = "关闭";
        _cancelButton.AutoSize = true;
        _cancelButton.Click += (_, _) =>
        {
            if (_isRunning) CancellationRequested?.Invoke(this, EventArgs.Empty);
            else Hide();
        };

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        header.Controls.Add(_statusLabel, 0, 0);
        header.Controls.Add(_progress, 1, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_copyButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            Padding = new Padding(14)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_resultBox, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
    }

    public void BeginRequest(string title)
    {
        OnUi(() =>
        {
            Text = $"FastRead - {title}";
            _statusLabel.Text = $"正在{title}…";
            _resultBox.Clear();
            _copyButton.Enabled = false;
            _cancelButton.Text = "取消";
            _progress.Visible = true;
            _isRunning = true;
            PositionOnCurrentScreen();
            if (!Visible) Show();
            Activate();
        });
    }

    public void Append(string content) => OnUi(() =>
    {
        _resultBox.AppendText(content);
        _resultBox.SelectionStart = _resultBox.TextLength;
        _resultBox.ScrollToCaret();
        _copyButton.Enabled = _resultBox.TextLength > 0;
    });

    public void Complete(bool truncated = false) => OnUi(() =>
    {
        if (truncated) _resultBox.AppendText("\n\n[已达到设置的输出长度，后续内容已停止生成]");
        SetStopped(truncated ? "已完成（达到长度上限）" : "已完成");
    });

    public void Cancelled() => OnUi(() => SetStopped(
        _resultBox.TextLength > 0 ? "已取消，保留已生成内容" : "已取消"));

    public void ShowError(string message) => OnUi(() =>
    {
        if (_resultBox.TextLength > 0) _resultBox.AppendText($"\n\n[错误] {message}");
        else _resultBox.Text = message;
        _copyButton.Enabled = _resultBox.TextLength > 0;
        SetStopped("发生错误");
        if (!Visible) Show();
        Activate();
    });

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            if (_isRunning) CancellationRequested?.Invoke(this, EventArgs.Empty);
            else Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    private void SetStopped(string status)
    {
        _isRunning = false;
        _statusLabel.Text = status;
        _progress.Visible = false;
        _cancelButton.Text = "关闭";
        _copyButton.Enabled = _resultBox.TextLength > 0;
    }

    private void CopyResult()
    {
        try
        {
            if (_resultBox.TextLength > 0) Clipboard.SetText(_resultBox.Text);
            _statusLabel.Text = "结果已复制";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法写入剪贴板：{ex.Message}", "FastRead", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void PositionOnCurrentScreen()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
    }

    private void OnUi(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }
}
