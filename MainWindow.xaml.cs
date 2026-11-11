using System.ComponentModel;
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

/// <summary>
/// 设备监控中心主窗口。界面由左侧导航、顶部面包屑、页面容器和底部状态栏组成，
/// 页面容器同样是六个互斥显示的 ScrollViewer（设备总览 / NewFile / 实时监控 / bake / 报警记录 / 系统信息）。
/// <para>
/// 测点数值目前由 <see cref="UpdateSimulationDisplay"/> 生成模拟值，尚未接入 PLC；
/// 串口通信是唯一与真实外部设备交互的部分，收发逻辑封装在 <see cref="SerialPortSession"/> 中。
/// </para>
/// </summary>
public partial class MainWindow : Window
{
    // 接收区最多保留的字符数，超出时丢弃最旧的内容，防止长时间运行后文本框无限膨胀
    private const int MaxReceiveLength = 20000;

    // 模拟数采：每秒刷新一次仪表数值
    private readonly DispatcherTimer _simulationTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    // 接收区的刷新节拍。串口数据在线程池线程上高频到达，会话类先攒进缓冲区，
    // 由界面线程按此节拍批量取走，避免小包逐字节刷新界面造成卡顿
    private readonly DispatcherTimer _receiveFlushTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(60)
    };

    private readonly SerialPortSession _serialSession = new();

    // 自上次“清空接收”以来收到的字节数，只在界面线程访问
    private long _totalReceivedBytes;

    // “计次示例”按钮的点击次数
    private int _demoClickCount;

    public MainWindow()
    {
        InitializeComponent();
        UpdateSimulationDisplay();
        ScanSerialPorts();

        _simulationTimer.Tick += (_, _) => UpdateSimulationDisplay();
        _simulationTimer.Start();
        _receiveFlushTimer.Tick += (_, _) => RefreshSerialReceiveArea();
        _receiveFlushTimer.Start();

        Closed += (_, _) => ShutdownCommunication();
    }

    // 导航按钮与页面按相同顺序排列，便于统一切换显示与选中样式
    private Button[] NavigationButtons =>
        [OverviewNavButton, NewFileNavButton, RealtimeNavButton, RecipeNavButton, AlarmNavButton, SystemNavButton];

    private UIElement[] NavigationPages =>
        [OverviewPage, NewFilePage, RealtimePage, RecipePage, AlarmPage, SystemPage];

    /// <summary>停止定时器并释放串口，避免端口在进程退出前一直被占用。</summary>
    private void ShutdownCommunication()
    {
        _simulationTimer.Stop();
        _receiveFlushTimer.Stop();
        CloseSerialPort();
        _serialSession.Dispose();
    }

    /// <summary>
    /// 生成一组围绕额定值小幅波动的模拟测点数据并刷新界面。
    /// 这些数值不是从 PLC 或传感器读取的，接入真实设备后应替换为采集结果。
    /// </summary>
    private void UpdateSimulationDisplay()
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

    /// <summary>重新枚举系统串口，供“扫描”按钮调用。</summary>
    private void ScanPortsClick(object sender, RoutedEventArgs e) => ScanSerialPorts();

    /// <summary>把接收区内容复制到剪贴板，便于把现场日志贴给同事或存档。</summary>
    private void CopyCommunicationLogClick(object sender, RoutedEventArgs e)
    {
        var content = ReceiveTextBox.Text;
        if (content.Length == 0)
        {
            AppendCommunicationLog("接收区为空，没有可复制的内容。");
            return;
        }

        try
        {
            Clipboard.SetText(content);
            AppendCommunicationLog($"已复制 {content.Length} 个字符到剪贴板。");
        }
        catch (System.Runtime.InteropServices.ExternalException exception)
        {
            // 剪贴板被其他进程占用时会失败，提示即可
            AppendCommunicationLog($"复制到剪贴板失败：{exception.Message}");
        }
    }

    /// <summary>把接收区内容另存为 UTF-8 文本文件。</summary>
    private void SaveCommunicationLogClick(object sender, RoutedEventArgs e)
    {
        var content = ReceiveTextBox.Text;
        if (content.Length == 0)
        {
            AppendCommunicationLog("接收区为空，没有可保存的内容。");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "保存通讯日志",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"serial-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, content, Encoding.UTF8);
            AppendCommunicationLog($"日志已保存：{dialog.FileName}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            AppendCommunicationLog($"保存日志失败：{exception.Message}");
        }
    }

    // ===== 页面导航 =====

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

    /// <summary>
    /// 切换主内容区显示的页面，并同步面包屑与导航按钮的选中样式。
    /// 页面已经可见时直接返回，避免重复播放入场动画。
    /// </summary>
    private void ShowPage(UIElement page, Button activeButton, string title)
    {
        if (page.Visibility == Visibility.Visible)
        {
            return;
        }

        foreach (var candidate in NavigationPages)
        {
            candidate.Visibility = Visibility.Collapsed;
        }

        page.Visibility = Visibility.Visible;
        AnimatePageEntry(page);

        foreach (var button in NavigationButtons)
        {
            var isActive = button == activeButton;
            button.Background = isActive ? ThemeBrush("NavigationActiveBackgroundBrush") : Brushes.Transparent;
            button.Foreground = isActive ? Brushes.White : ThemeBrush("NavigationForegroundBrush");
        }

        PageBreadcrumbText.Text = title;
    }

    /// <summary>页面首次显示时的入场动画：从全透明淡入，同时由下往上移动 8px。</summary>
    private static void AnimatePageEntry(UIElement page)
    {
        page.Opacity = 0; // 起始完全透明
        page.RenderTransform = new TranslateTransform(0, 8); // 起始位置向下偏移 8px

        var duration = TimeSpan.FromMilliseconds(2000); // 动画时长
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut }; // 先快后慢，收尾更自然
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

    /// <summary>
    /// “计次示例”按钮：累加点击次数并直接显示在界面上。
    /// 早期版本用 Debug.WriteLine 输出到调试窗口，普通运行时看不到，现已改为界面反馈。
    /// </summary>
    private void DemoClickCountClick(object sender, RoutedEventArgs e)
    {
        _demoClickCount++;
        DemoClickCountText.Text = $"已点击 {_demoClickCount} 次";
    }

    /// <summary>
    /// 枚举系统串口并填充端口下拉框。这里只获取端口名，不代表已经和设备建立通信。
    /// </summary>
    private void ScanSerialPorts()
    {
        // 打开中的端口不能重新枚举：重设 ItemsSource 会把已选端口清空，导致无法正常关闭
        if (_serialSession.IsOpen)
        {
            PortScanStatusText.Text = "串口已打开，请先关闭串口再重新扫描。";
            PortScanStatusText.Foreground = ThemeBrush("StateWarningBrush");
            return;
        }

        try
        {
            var ports = SerialPort.GetPortNames()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetComPortNumber)
                .ThenBy(port => port, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            PortComboBox.ItemsSource = ports;
            if (ports.Length > 0)
            {
                PortComboBox.SelectedIndex = 0;
                PortScanStatusText.Text = $"检测到 {ports.Length} 个串口。这里只枚举系统端口，尚未连接设备。";
                PortScanStatusText.Foreground = ThemeBrush("StateSuccessBrush");
            }
            else
            {
                PortScanStatusText.Text = "未检测到串口。请检查设备、驱动或 USB 转串口连接后重新扫描。";
                PortScanStatusText.Foreground = ThemeBrush("StateWarningBrush");
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException or Win32Exception)
        {
            PortComboBox.ItemsSource = Array.Empty<string>();
            PortScanStatusText.Text = $"扫描串口失败：{exception.Message}";
            PortScanStatusText.Foreground = ThemeBrush("StateDangerBrush");
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

    // 同一个按钮承担开与关，先看会话实际状态再决定动作
    private void TogglePortClick(object sender, RoutedEventArgs e)
    {
        if (_serialSession.IsOpen)
        {
            CloseSerialPort();
        }
        else
        {
            OpenSerialPort();
        }
    }

    /// <summary>
    /// 打开所选串口。端口被占用、设备已拔出等失败只在接收区提示，不向调用方抛出。
    /// </summary>
    private void OpenSerialPort()
    {
        // 端口列表由上方“串口（COM）扫描”填充，这里直接复用选中项
        if (PortComboBox.SelectedItem is not string portName || string.IsNullOrWhiteSpace(portName))
        {
            AppendCommunicationLog("未选择串口，请先点击“扫描”并选择端口。");
            return;
        }

        var baudRate = GetSelectedBaudRate();
        try
        {
            _serialSession.Open(portName, baudRate);
            UpdateSerialConnectionUi(true);
            AppendCommunicationLog($"已打开 {portName}，波特率 {baudRate}（8-N-1）。");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException
                                              or InvalidOperationException or ArgumentException)
        {
            UpdateSerialConnectionUi(false);
            AppendCommunicationLog($"打开串口失败：{exception.Message}");
        }
    }

    /// <summary>关闭并释放串口，同时把界面恢复到“未连接”状态。</summary>
    private void CloseSerialPort()
    {
        var wasOpen = _serialSession.IsOpen;
        try
        {
            _serialSession.Close();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            AppendCommunicationLog($"关闭串口时出现异常：{exception.Message}");
        }

        UpdateSerialConnectionUi(false);
        if (wasOpen)
        {
            AppendCommunicationLog("串口已关闭。");
        }
    }

    /// <summary>
    /// 发送输入框内容。文本模式按 UTF-8 写出；十六进制模式解析成字节，
    /// 需要时在帧尾追加 Modbus RTU 的 CRC16（低字节在前）。
    /// </summary>
    private void SendClick(object sender, RoutedEventArgs e)
    {
        if (!_serialSession.IsOpen)
        {
            AppendCommunicationLog("串口未打开，无法发送。");
            return;
        }

        var payload = SendTextBox.Text;
        if (payload.Length == 0)
        {
            return;
        }

        try
        {
            if (HexSendCheckBox.IsChecked == true)
            {
                if (!HexText.TryParse(payload, out var frame))
                {
                    AppendCommunicationLog("十六进制内容无效：只能包含 0-9、A-F 和空格/逗号等分隔符，且字节须成对。");
                    return;
                }

                if (AppendModbusCrcCheckBox.IsChecked == true)
                {
                    var crc = ModbusCrc16.Compute(frame);
                    AppendCommunicationLog($"Modbus CRC16 = 0x{crc:X4}（帧尾字节 {ModbusCrc16.FormatCrcBytes(crc)}）");
                    frame = ModbusCrc16.AppendCrc(frame);
                }

                _serialSession.WriteBytes(frame);
                AppendCommunicationLog($"发送 {frame.Length} 字节：{HexText.Format(frame)}");
                return;
            }

            if (AppendCrLfCheckBox.IsChecked == true)
            {
                payload += "\r\n";
            }

            _serialSession.WriteText(payload);
            AppendCommunicationLog($"发送 {payload.Length} 字符：{EscapeControlChars(payload)}");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
        {
            AppendCommunicationLog($"发送失败：{exception.Message}");
        }
    }

    // 十六进制模式整帧下发，追加 \r\n 会把报文尾的 CRC 冲掉，因此两种模式互斥
    private void HexSendModeChanged(object sender, RoutedEventArgs e)
    {
        var hexMode = HexSendCheckBox.IsChecked == true;
        AppendCrLfCheckBox.IsEnabled = !hexMode;
        AppendModbusCrcCheckBox.IsEnabled = hexMode;
    }

    // 清空接收区时会话里待刷新的数据也要一起丢弃，否则下一次刷新会把旧数据补回来
    private void ClearReceiveClick(object sender, RoutedEventArgs e)
    {
        _serialSession.DiscardPendingData();
        _totalReceivedBytes = 0;
        ReceiveTextBox.Clear();
        ReceiveCountText.Text = "已接收 0 字节";
    }

    /// <summary>
    /// UI 线程定时调用：取走后台线程攒下的接收数据，检查通讯故障，再一次性刷进接收区。
    /// </summary>
    private void RefreshSerialReceiveArea()
    {
        if (_serialSession.TryTakePendingText(out var text, out var byteCount))
        {
            AppendReceiveContent(text, byteCount);
        }

        if (_serialSession.TryTakeFault(out var faultMessage))
        {
            AppendCommunicationLog($"读取中断：{faultMessage}");
        }
    }

    /// <summary>
    /// 把一段内容追加到接收区，只能在 UI 线程调用。
    /// receivedBytes 为 0 表示这是程序生成的状态日志，不计入接收字节数。
    /// </summary>
    private void AppendReceiveContent(string text, long receivedBytes)
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

        // 整段重新赋值而不是逐次追加，便于超长时直接从头部截断
        var combined = ReceiveTextBox.Text + text;
        if (combined.Length > MaxReceiveLength)
        {
            combined = combined[^MaxReceiveLength..];
        }

        ReceiveTextBox.Text = combined;
        ReceiveTextBox.ScrollToEnd();
    }

    /// <summary>带时间戳的状态日志，写入接收区便于和收发数据对照时序。</summary>
    private void AppendCommunicationLog(string message) =>
        AppendReceiveContent($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}", 0);

    /// <summary>把 CR/LF 转成可见的转义形式，避免一行日志被拆成多行。</summary>
    private static string EscapeControlChars(string text) =>
        text.Replace("\r", "\\r").Replace("\n", "\\n");

    /// <summary>
    /// 串口状态集中在这里切换：按钮文案与配色、发送是否可用、端口配置是否可改、右上角状态文字。
    /// </summary>
    private void UpdateSerialConnectionUi(bool isOpen)
    {
        TogglePortButton.Content = isOpen ? "关闭串口" : "打开串口";
        TogglePortButton.Background = ThemeBrush(isOpen ? "StateDangerBrush" : "BrandBrush");
        SendButton.IsEnabled = isOpen;
        PortComboBox.IsEnabled = !isOpen;
        SerialPortComboBox.IsEnabled = !isOpen;
        BaudRateComboBox.IsEnabled = !isOpen;
        SerialStatusText.Text = isOpen
            ? $"{_serialSession.PortName} 已连接 · {_serialSession.BaudRate}"
            : "未连接";
        SerialStatusText.Foreground = ThemeBrush(isOpen ? "StateSuccessBrush" : "TextSubtleBrush");
    }

    /// <summary>按资源键取画刷；主题色只在 XAML 色板里定义一份，代码不再硬编码 RGB。</summary>
    private Brush ThemeBrush(string resourceKey) => (Brush)FindResource(resourceKey);

    /// <summary>
    /// 端口排序用：解析 COM 后面的数字，让 COM2 排在 COM10 前面（字符串比较会得到相反的顺序）。
    /// 不符合 COM+数字格式的名字排到最后。
    /// </summary>
    private static int GetComPortNumber(string port)
    {
        return port.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(port.AsSpan(3), out var number)
                ? number
                : int.MaxValue;
    }
}