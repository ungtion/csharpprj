using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CSharpProject;

public partial class MainWindow : Window
{
    // 接收区最多保留的字符数，超出时丢弃最旧的内容，防止长时间运行后文本框无限膨胀
    private const int MaxReceiveLength = 20000;

    // 模拟数采：每秒刷新一次仪表数值
    private readonly DispatcherTimer _simulationTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    // 接收区的刷新节拍。串口的 DataReceived 在线程池线程上高频触发，
    // 先攒进缓冲区再批量刷 UI，避免小包逐字节刷新界面造成卡顿
    private readonly DispatcherTimer _receiveFlushTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(60)
    };

    // 后台线程只写缓冲区，UI 线程只读缓冲区，全部访问都在 _receiveBufferLock 内完成
    private readonly StringBuilder _receiveBuffer = new();
    private readonly object _receiveBufferLock = new();

    // 当前打开的串口；为 null 或 IsOpen == false 即表示未连接
    private SerialPort? _serialPort;
    private long _pendingReceivedBytes; // 两次刷新之间收到的字节数，刷完即清零
    private long _totalReceivedBytes;   // 自上次“清空接收”以来的累计字节数

    public MainWindow()
    {
        InitializeComponent();
        UpdateDisplay();
        ScanSerialPorts();
        _simulationTimer.Tick += (_, _) => UpdateDisplay();
        _simulationTimer.Start();
        _receiveFlushTimer.Tick += (_, _) => FlushReceiveBuffer();
        _receiveFlushTimer.Start();

        // 窗口关闭时必须释放串口，否则进程退出前端口会一直处于占用状态
        Closed += (_, _) =>
        {
            _simulationTimer.Stop();
            _receiveFlushTimer.Stop();
            CloseSerialPort();
        };
    }
    private void UpdateDisplay()
    {
        var now = DateTime.Now;
        var temperature = 42.6 + (Random.Shared.NextDouble() - 0.5) * 0.6;
        var pressure = 0.62 + (Random.Shared.NextDouble() - 0.5) * 0.02;
        var speed = 1480 + Random.Shared.Next(-4, 5);
        var efficiency = 98.6 + (Random.Shared.NextDouble() - 0.5) * 0.2;

        ClockText.Text = now.ToString("yyyy-MM-dd  HH:mm:ss");
        UpdateTimeText.Text = now.ToString("HH:mm:ss");
        TemperatureText.Text = temperature.ToString("F1");
        PressureText.Text = pressure.ToString("F2");
        SpeedText.Text = speed.ToString();
        EfficiencyText.Text = efficiency.ToString("F1");

    }
//
    private void ScanPortsClick(object sender, RoutedEventArgs e) => ScanSerialPorts();

    private void ScanxxClick(object sender, RoutedEventArgs e) => somesyco();

    private void ScanxxxsClick(object sender, RoutedEventArgs e) => someresyco();

    // 未被 XAML 引用；如需绑定 Click，请使用 RoutedEventArgs
    private void recycol(object sender, RoutedEventArgs e) => sometouch(sender, e);

    private void OverviewNavClick(object sender, RoutedEventArgs e) =>
        ShowPage(OverviewPage, OverviewNavButton, "设备总览");

    private void NewFileNavClick(object sender, RoutedEventArgs e) =>
        ShowPage(NewFilePage, NewFileNavButton, "New File");

    private void RealtimeNavClick(object sender, RoutedEventArgs e) =>
        ShowPage(RealtimePage, RealtimeNavButton, "实时监控");

    private void RecipeNavClick(object sender, RoutedEventArgs e) =>
        ShowPage(RecipePage, RecipeNavButton, "bake");

    private void AlarmNavClick(object sender, RoutedEventArgs e) =>
        ShowPage(AlarmPage, AlarmNavButton, "报警记录");

    private void SystemNavClick(object sender, RoutedEventArgs e) =>
        ShowPage(SystemPage, SystemNavButton, "系统信息");

    private void ShowPage(UIElement page, Button activeButton, string title)
    {
        if (page.Visibility == Visibility.Visible)
        {
            return;
        }

        OverviewPage.Visibility = Visibility.Collapsed;
        NewFilePage.Visibility = Visibility.Collapsed;
        RealtimePage.Visibility = Visibility.Collapsed;
        RecipePage.Visibility = Visibility.Collapsed;
        AlarmPage.Visibility = Visibility.Collapsed;
        SystemPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        AnimatePageEntry(page);

        foreach (var button in new[]
                 {
                     OverviewNavButton,
                     NewFileNavButton,
                     RealtimeNavButton,
                     RecipeNavButton,
                     AlarmNavButton,
                     SystemNavButton
                 })
        {
            var isActive = button == activeButton;
            button.Background = isActive
                ? System.Windows.Media.Brushes.DarkSlateGray
                : System.Windows.Media.Brushes.Transparent;
            button.Foreground = isActive
                ? System.Windows.Media.Brushes.White
                : new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(195, 203, 214));
        }

        PageBreadcrumbText.Text = title;
    }

    private static void AnimatePageEntry(UIElement page)
    {
        page.Opacity = 0;//起始状态0为全透明
        page.RenderTransform = new TranslateTransform(0, 8);//（ x, y）y轴偏移5px

        var duration = TimeSpan.FromMilliseconds(2000);//动画时长
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };//缓动曲线
        var fadeIn = new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        var slideUp = new DoubleAnimation(8, 0, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };

        fadeIn.Completed += (_, _) => page.Opacity = 1;
        slideUp.Completed += (_, _) => page.RenderTransform = Transform.Identity;

        page.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        page.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
    }
    int i;

    private void sometouch(object sender, RoutedEventArgs e)
    {
        i++;
        Debug.WriteLine($"why touch me? touch time:{i}!");
    }

    private void someresyco()
    {
        int retime=0;
        while(retime==0)
        {   retime++;
            Debug.WriteLine($"retime{retime}");
        }
    }
    private void somesyco()
    {
        for (var i = 0; i < 10; i++)
        {
            Debug.WriteLine($"hello {i}");
        }
    }


    private void ScanSerialPorts()
    {
        // 打开中的端口不能重新枚举：重设 ItemsSource 会把已选端口清空，导致无法正常关闭
        if (_serialPort is { IsOpen: true })
        {
            PortScanStatusText.Text = "串口已打开，请先关闭串口再重新扫描。";
            PortScanStatusText.Foreground = System.Windows.Media.Brushes.DarkOrange;
            return;
        }

        try
        {
            using var serialPortsKey =
                Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");

            var ports = serialPortsKey?
                .GetValueNames()
                .Select(name => serialPortsKey.GetValue(name) as string)
                .Where(port => !string.IsNullOrWhiteSpace(port))
                .Select(port => port!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetComPortNumber)
                .ThenBy(port => port, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];

            PortComboBox.ItemsSource = ports;
            if (ports.Length > 0)
            {
                PortComboBox.SelectedIndex = 0;
                PortScanStatusText.Text = $"检测到 {ports.Length} 个串口。这里只枚举系统端口，尚未连接设备。";
                PortScanStatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
            }
            else
            {
                PortScanStatusText.Text = "未检测到串口。请检查设备、驱动或 USB 转串口连接后重新扫描。";
                PortScanStatusText.Foreground = System.Windows.Media.Brushes.DarkOrange;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException or Win32Exception)
        {
            PortComboBox.ItemsSource = Array.Empty<string>();
            PortScanStatusText.Text = $"扫描串口失败：{exception.Message}";
            PortScanStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
    }

    // 读取波特率下拉框的选中项；解析失败时退回 9600，保证 SerialPort 构造参数一定合法
    private int GetSelectedBaudRate()
    {
        return BaudRateComboBox.SelectedItem is ComboBoxItem { Content: string text }
            && int.TryParse(text, out var baudRate)
                ? baudRate
                : 9600;
    }

    // ===== 串口通信：打开 / 关闭 / 发送 / 接收 =====

    // 同一个按钮承担开与关，先看串口实际状态再决定动作
    private void TogglePortClick(object sender, RoutedEventArgs e)
    {
        if (_serialPort is { IsOpen: true })
        {
            CloseSerialPort();
        }
        else
        {
            OpenSerialPort();
        }
    }

    /// <summary>
    /// 打开所选串口。参数固定 8 数据位 / 无校验 / 1 停止位 / 无流控，
    /// 这是 PLC、单片机调试口最常见的配置；其他组合需要设备手册另行确认。
    /// 端口被占用、拔线等失败只在接收区提示，不向上抛异常。
    /// </summary>
    private void OpenSerialPort()
    {
        // 端口列表由上方“串口（COM）扫描”填充，这里直接复用选中项
        if (PortComboBox.SelectedItem is not string portName || string.IsNullOrWhiteSpace(portName))
        {
            AppendSerialLog("未选择串口，请先点击“扫描”并选择端口。");
            return;
        }

        var baudRate = GetSelectedBaudRate();
        SerialPort? serialPort = null;
        try
        {
            serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                Encoding = Encoding.UTF8, // 按文本收发；二进制协议需要改成十六进制显示
                ReadTimeout = 500,
                WriteTimeout = 1000
            };
            serialPort.DataReceived += SerialPortDataReceived;
            serialPort.Open();
            _serialPort = serialPort;
            UpdateSerialPortState(true, portName, baudRate);
            AppendSerialLog($"已打开 {portName}，波特率 {baudRate}（8-N-1）。");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException
                                              or InvalidOperationException or ArgumentException)
        {
            // 打开失败时把刚创建的实例彻底丢掉，避免残留事件订阅和句柄
            if (serialPort is not null)
            {
                serialPort.DataReceived -= SerialPortDataReceived;
                serialPort.Dispose();
            }

            _serialPort = null;
            UpdateSerialPortState(false);
            AppendSerialLog($"打开串口失败：{exception.Message}");
        }
    }

    /// <summary>关闭并释放串口，同时把界面恢复到“未连接”状态。</summary>
    private void CloseSerialPort()
    {
        var serialPort = _serialPort;
        _serialPort = null;
        UpdateSerialPortState(false);
        if (serialPort is null)
        {
            return;
        }

        try
        {
            // 先退订事件再关闭，防止关闭过程中还有回调进来访问已释放的对象
            serialPort.DataReceived -= SerialPortDataReceived;
            if (serialPort.IsOpen)
            {
                serialPort.Close();
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            AppendSerialLog($"关闭串口时出现异常：{exception.Message}");
        }
        finally
        {
            serialPort.Dispose();
        }

        AppendSerialLog("串口已关闭。");
    }

    // 发送区内容写入串口；勾选“追加 \r\n”时补 CRLF，便于行协议设备（AT 指令等）识别一帧结束
    private void SendClick(object sender, RoutedEventArgs e)
    {
        if (_serialPort is not { IsOpen: true } serialPort)
        {
            AppendSerialLog("串口未打开，无法发送。");
            return;
        }

        var payload = SendTextBox.Text;
        if (string.IsNullOrEmpty(payload))
        {
            return;
        }

        if (AppendCrLfCheckBox.IsChecked == true)
        {
            payload += "\r\n";
        }

        try
        {
            serialPort.Write(payload);
            AppendSerialLog($"发送 {payload.Length} 字符：{EscapeForLog(payload)}");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
        {
            AppendSerialLog($"发送失败：{exception.Message}");
        }
    }

    // 清空接收区时缓冲区也要一起清，否则下一次刷新会把还没来得及显示的旧数据补回来
    private void ClearReceiveClick(object sender, RoutedEventArgs e)
    {
        lock (_receiveBufferLock)
        {
            _receiveBuffer.Clear();
            _pendingReceivedBytes = 0;
        }

        _totalReceivedBytes = 0;
        ReceiveTextBox.Clear();
        ReceiveCountText.Text = "已接收 0 字节";
    }

    // 在后台线程触发，这里只做“读 -> 放进缓冲区”，不碰任何 UI 控件；
    // 真正刷新界面交给 UI 线程上的 _receiveFlushTimer
    private void SerialPortDataReceived(object? sender, SerialDataReceivedEventArgs e)
    {
        if (sender is not SerialPort port)
        {
            return;
        }

        string text;
        try
        {
            text = port.ReadExisting();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
                                              or UnauthorizedAccessException or TimeoutException)
        {
            // 设备被拔出时读操作会抛异常，忽略即可，UI 侧仍可通过“关闭串口”恢复
            return;
        }

        if (text.Length == 0)
        {
            return;
        }

        // Encoding.GetByteCount 反映线上实际字节数，比字符数更接近设备真实发送量
        lock (_receiveBufferLock)
        {
            _receiveBuffer.Append(text);
            _pendingReceivedBytes += port.Encoding.GetByteCount(text);
        }
    }

    // UI 线程定时调用：把攒了 60ms 的数据一次刷进接收区
    private void FlushReceiveBuffer()
    {
        string pending;
        long pendingBytes;
        lock (_receiveBufferLock)
        {
            if (_receiveBuffer.Length == 0)
            {
                return;
            }

            pending = _receiveBuffer.ToString();
            pendingBytes = _pendingReceivedBytes;
            _receiveBuffer.Clear();
            _pendingReceivedBytes = 0;
        }

        AppendReceiveText(pending, pendingBytes);
    }

    // 仅 UI 线程调用。receivedBytes > 0 表示这是设备数据，需要累计到接收计数
    private void AppendReceiveText(string text, long receivedBytes)
    {
        if (receivedBytes > 0)
        {
            _totalReceivedBytes += receivedBytes;
            ReceiveCountText.Text = $"已接收 {_totalReceivedBytes} 字节";
        }

        if (text.Length == 0)
        {
            return;
        }

        // 整段重新赋值而不是追加，便于在超长时从头部截断
        var combined = ReceiveTextBox.Text + text;
        if (combined.Length > MaxReceiveLength)
        {
            combined = combined[^MaxReceiveLength..];
        }

        ReceiveTextBox.Text = combined;
        ReceiveTextBox.ScrollToEnd();
    }

    // 状态信息带时间戳写进接收区，方便对照收发时序；不计入接收字节数
    private void AppendSerialLog(string message) =>
        AppendReceiveText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}", 0);

    // 日志里把 CR/LF 显示成可见字符，否则一行日志会被拆成多行
    private static string EscapeForLog(string text) =>
        text.Replace("\r", "\\r").Replace("\n", "\\n");

    // 串口状态集中在这里切换：按钮文案与颜色、发送是否可用、端口配置是否可改、右上角状态文字
    private void UpdateSerialPortState(bool isOpen, string? portName = null, int baudRate = 0)
    {
        TogglePortButton.Content = isOpen ? "关闭串口" : "打开串口";
        TogglePortButton.Background = isOpen
            ? System.Windows.Media.Brushes.Firebrick
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x16, 0x86, 0x6F));
        SendButton.IsEnabled = isOpen;
        PortComboBox.IsEnabled = !isOpen;
        SerialPortComboBox.IsEnabled = !isOpen;
        BaudRateComboBox.IsEnabled = !isOpen;
        SerialStatusText.Text = isOpen ? $"{portName} 已连接 · {baudRate}" : "未连接";
        SerialStatusText.Foreground = isOpen
            ? System.Windows.Media.Brushes.SeaGreen
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x9A, 0xA5, 0xB3));
    }

    // 用于端口排序：把 COM 后面的数字解析出来，让 COM2 排在 COM10 前面（字符串比较会反过来）
    private static int GetComPortNumber(string port)
    {
        return port.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(port.AsSpan(3), out var number)
                ? number
                : int.MaxValue;
    }
}