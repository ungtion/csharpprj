using System.Windows;
using System.Windows.Threading;

namespace CSharpProject;

public partial class App : Application
{
    /// <summary>
    /// 兜住界面线程上未处理的异常：弹出提示，避免程序直接退出丢失现场。
    /// 串口等后台线程的异常已在各自位置处理，不会走到这里。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // 标记为已处理以保持界面存活；异常可能已破坏内部状态，因此提示用户重启更稳妥
        e.Handled = true;
        MessageBox.Show(
            $"发生未处理的异常，建议重启程序。\n\n{e.Exception.Message}",
            "设备监控中心",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
