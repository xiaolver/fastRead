using FastRead.UI;

namespace FastRead;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(initiallyOwned: true, @"Local\FastRead.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("FastRead 已在运行，请查看系统托盘。", "FastRead", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.ThreadException += (_, e) => ShowUnexpectedError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ShowUnexpectedError(e.ExceptionObject as Exception ?? new Exception("未知错误"));

        using var context = new FastReadApplicationContext();
        Application.Run(context);
    }

    private static void ShowUnexpectedError(Exception exception)
    {
        MessageBox.Show(
            $"FastRead 遇到未预期的错误：\n{exception.Message}",
            "FastRead",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
