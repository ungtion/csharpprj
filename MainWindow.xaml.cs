using System.ComponentModel; // 提供 Win32Exception，用于处理串口枚举相关的系统错误。
using System.IO; // 提供文件读写及 IOException，用于保存日志和处理通信错误。
using System.IO.Ports; // 提供 SerialPort，用于枚举本机串口。
using System.Net.Sockets; // 提供 SocketException，用于处理 Modbus TCP 连接错误。
using System.Security; // 提供 SecurityException，用于处理文件访问权限错误。
using System.Text; // 提供 StringBuilder，用于构造寄存器读取结果。
using System.Text.RegularExpressions; // 提供 Regex，用于去除 ADC 串口输出中的 ANSI 颜色转义。
using System.Windows; // 提供 WPF 窗口、控件基类、路由事件及剪贴板等基础类型。
using System.Windows.Controls; // 提供 Button、TextBox、ComboBox 等 WPF 控件类型。
using System.Windows.Input; // 提供 KeyEventArgs / Key，用于 SN 输入框的回车自动采集。
using System.Windows.Media; // 提供 Brush、Brushes、Transform 等界面绘制类型。
using System.Windows.Media.Animation; // 提供页面切换时使用的动画和缓动类型。
using System.Windows.Threading; // 提供 DispatcherTimer，用于刷新模拟数据和串口接收区。
using Microsoft.Win32; // 提供 SaveFileDialog，用于选择日志保存位置。

namespace CSharpProject;

/// <summary>
/// 设备监控中心主窗口。界面由左侧导航、顶部面包屑、页面容器和运行环境提示组成，
/// 页面容器同样是七个互斥显示的 ScrollViewer（设备总览 / ADC 采集 / MC405 通讯测试 / 实时监控 / bake / 报警记录 / 系统信息）。
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
    private readonly ModbusTcpSession _modbusTcpSession = new();

    // 自上次“清空接收”以来收到的字节数，只在界面线程访问
    private long _totalReceivedBytes;

    // “计次示例”按钮的点击次数
    private int _demoClickCount;

    // ===== ADC 采集页状态（移植自 slscs 的 SN+ADC 采集工具）=====
    // 该页使用独立的串口会话，和“设备总览”里的通用串口收发面板互不干扰。
    private static readonly Regex AnsiEscapeRegex = new(@"\x1B\[[0-9;]*m");

    private readonly SerialPortSession _adcSerialSession = new();

    // 串口数据按包到达，可能截断在某一行中间；先攒进缓冲区，凑满一行再解析
    private readonly StringBuilder _adcLineBuffer = new();

    private string _adcSavePath = "adc_record.txt"; // ADC 记录保存路径
    private int? _adcCurrentValue; // 当前接收到的 ADC 值；未收到有效数据时为 null
    private string _adcInputSn = ""; // 当前设备的 SN 码
    private string _adcFixedRangeText = ""; // 当前锁定或识别到的 ADC 区间名称
    private int _adcCount; // 当前 ADC 所属区间编号，0 表示尚未识别
    private bool _adcAutoSaved; // 当前 SN 是否已完成一次自动保存
    private int? _adcLastAdc; // 上一次成功写入记录的 ADC 值；用于避免重复保存

    public MainWindow()
    {
        InitializeComponent();
        UpdateSimulationDisplay();
        ScanSerialPorts();
        RefreshAdcPorts();

        _simulationTimer.Tick += (_, _) => UpdateSimulationDisplay();
        _simulationTimer.Start();
        _receiveFlushTimer.Tick += (_, _) => RefreshSerialReceiveArea();
        _receiveFlushTimer.Tick += (_, _) => RefreshAdcReceiveArea();
        _receiveFlushTimer.Start();

        Closed += (_, _) => ShutdownCommunication();
    }

    // 导航按钮和页面按同一顺序排列；新增页面时需同步更新这两个集合
    private Button[] NavigationButtons =>
        [OverviewNavButton, AdcCollectNavButton, Mc405ModbusNavButton, RealtimeNavButton, RecipeNavButton, AlarmNavButton, SystemNavButton];

    private UIElement[] NavigationPages =>
        [OverviewPage, AdcCollectPage, Mc405ModbusPage, RealtimePage, RecipePage, AlarmPage, SystemPage];

    /// <summary>
    /// 窗口关闭时统一停止定时器并释放会话资源，避免端口在进程退出前一直被占用。
    /// 先停止刷新计时器，确保释放串口后不再轮询接收缓冲区。
    /// </summary>
    private void ShutdownCommunication()
    {
        _simulationTimer.Stop();
        _receiveFlushTimer.Stop();
        CloseSerialPort();
        CloseAdcSerialPort();
        _serialSession.Dispose();
        _adcSerialSession.Dispose();
        _modbusTcpSession.Dispose();
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

    /// <summary>重新枚举系统串口，供设备状态卡片中的“扫描”按钮调用。</summary>
    private void ScanPortsClick(object sender, RoutedEventArgs e) => ScanSerialPorts();

    /// <summary>
    /// 把当前接收区内容复制到剪贴板。先取文本快照再追加操作日志，避免“复制成功”提示被包含进复制内容。
    /// </summary>
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

    /// <summary>
    /// 把当前接收区内容另存为 UTF-8 文本文件。用户取消保存对话框时不改动接收区。
    /// </summary>
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

    // ===== 页面导航：按钮事件只声明目标页，其余切换工作统一交给 ShowPage =====

    private void OverviewNavClick(object sender, RoutedEventArgs e) =>
        ShowPage(OverviewPage, OverviewNavButton, "设备总览");

    private void Mc405ModbusNavClick(object sender, RoutedEventArgs e) =>
        ShowPage(Mc405ModbusPage, Mc405ModbusNavButton, "MC405 通讯测试");

    private void AdcCollectNavClick(object sender, RoutedEventArgs e) =>
        ShowPage(AdcCollectPage, AdcCollectNavButton, "ADC 采集");

    private async void ConnectMc405Click(object sender, RoutedEventArgs e)
    {
        var host = Mc405HostTextBox.Text.Trim();
        if (host.Length == 0)
        {
            AppendMc405Log("请输入 MC405 的 IP 地址或主机名。");
            return;
        }

        if (!int.TryParse(Mc405PortTextBox.Text, out var port) || port is < 1 or > 65535)
        {
            AppendMc405Log("TCP 端口须为 1–65535 之间的整数。");
            return;
        }

        SetMc405ConnectionUi(false);
        Mc405ConnectButton.IsEnabled = false;
        Mc405ConnectionStatusText.Text = $"正在连接 {host}:{port}…";
        Mc405ConnectionStatusText.Foreground = ThemeBrush("TextMutedBrush");

        try
        {
            await _modbusTcpSession.ConnectAsync(host, port);
            SetMc405ConnectionUi(true);
            Mc405ConnectionStatusText.Text = $"已连接 {host}:{port}";
            Mc405ConnectionStatusText.Foreground = ThemeBrush("StateSuccessBrush");
            AppendMc405Log($"已连接到 {host}:{port}。");
        }
        catch (Exception exception) when (exception is SocketException or IOException
                                              or OperationCanceledException or ArgumentException
                                              or InvalidOperationException)
        {
            _modbusTcpSession.Close();
            SetMc405ConnectionUi(false);
            Mc405ConnectionStatusText.Text = "连接失败";
            Mc405ConnectionStatusText.Foreground = ThemeBrush("StateDangerBrush");
            AppendMc405Log($"连接 {host}:{port} 失败：{exception.Message}");
        }
    }

    private void DisconnectMc405Click(object sender, RoutedEventArgs e)
    {
        _modbusTcpSession.Close();
        SetMc405ConnectionUi(false);
        Mc405ConnectionStatusText.Text = "未连接";
        Mc405ConnectionStatusText.Foreground = ThemeBrush("TextSubtleBrush");
        AppendMc405Log("已断开 MC405 连接。");
    }

    private async void ReadMc405RegistersClick(object sender, RoutedEventArgs e)
    {
        if (!_modbusTcpSession.IsConnected)
        {
            AppendMc405Log("尚未连接控制器，请先建立 Modbus TCP 连接。");
            return;
        }

        if (!byte.TryParse(Mc405UnitIdTextBox.Text, out var unitId))
        {
            AppendMc405Log("Unit ID 须为 0–255 之间的整数。");
            return;
        }

        if (!ushort.TryParse(Mc405StartAddressTextBox.Text, out var startAddress))
        {
            AppendMc405Log("起始地址须为 0–65535 之间的整数。");
            return;
        }

        if (!ushort.TryParse(Mc405QuantityTextBox.Text, out var quantity) || quantity is < 1 or > 125
            || (uint)startAddress + quantity > 65536)
        {
            AppendMc405Log("读取数量须为 1–125，且地址范围不能超过 65535。");
            return;
        }

        if (Mc405FunctionComboBox.SelectedItem is not ComboBoxItem { Tag: string functionTag }
            || !byte.TryParse(functionTag, out var functionCode)
            || functionCode is not (3 or 4))
        {
            AppendMc405Log("请选择保持寄存器（03）或输入寄存器（04）。");
            return;
        }

        Mc405ReadButton.IsEnabled = false;
        Mc405DisconnectButton.IsEnabled = false;
        try
        {
            var result = await _modbusTcpSession.ReadRegistersAsync(
                unitId, functionCode, startAddress, quantity);

            var output = new StringBuilder();
            output.AppendLine($"功能码 {functionCode:D2} · 起始地址 {startAddress} · 数量 {quantity}");
            for (var index = 0; index < result.Registers.Length; index++)
            {
                var address = startAddress + index;
                var value = result.Registers[index];
                output.AppendLine($"地址 {address,5}  |  十进制 {value,5}  |  十六进制 0x{value:X4}");
            }

            Mc405ResultTextBox.Text = output.ToString();
            AppendMc405Log($"TX: {FormatModbusFrame(result.RequestFrame)}");
            AppendMc405Log($"RX: {FormatModbusFrame(result.ResponseFrame)}");
            AppendMc405Log($"读取成功：收到 {result.Registers.Length} 个寄存器。");
        }
        catch (ModbusTcpException exception)
        {
            AppendMc405Log($"MC405 返回 Modbus 异常：{exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or SocketException
                                              or OperationCanceledException or InvalidOperationException)
        {
            _modbusTcpSession.Close();
            SetMc405ConnectionUi(false);
            Mc405ConnectionStatusText.Text = "连接中断";
            Mc405ConnectionStatusText.Foreground = ThemeBrush("StateDangerBrush");
            AppendMc405Log($"读取失败，连接已关闭：{exception.Message}");
        }
        finally
        {
            Mc405ReadButton.IsEnabled = _modbusTcpSession.IsConnected;
            Mc405DisconnectButton.IsEnabled = _modbusTcpSession.IsConnected;
        }
    }

    private void SetMc405ConnectionUi(bool isConnected)
    {
        Mc405HostTextBox.IsEnabled = !isConnected;
        Mc405PortTextBox.IsEnabled = !isConnected;
        Mc405ConnectButton.IsEnabled = !isConnected;
        Mc405DisconnectButton.IsEnabled = isConnected;
        Mc405ReadButton.IsEnabled = isConnected;
    }

    private void AppendMc405Log(string message)
    {
        var combined = Mc405LogTextBox.Text + $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        Mc405LogTextBox.Text = combined.Length > MaxReceiveLength
            ? combined[^MaxReceiveLength..]
            : combined;
        Mc405LogTextBox.ScrollToEnd();
    }

    private static string FormatModbusFrame(byte[] frame) =>
        string.Join(' ', Convert.ToHexString(frame).Chunk(2).Select(pair => new string(pair)));

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

        var duration = TimeSpan.FromMilliseconds(500); // 动画时长
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
    private async void DemoClickCountClick(object sender, RoutedEventArgs e)
    {
        _demoClickCount++;
        DemoClickCountText.Text = $"点击{_demoClickCount} 次";

        await Task.Delay(TimeSpan.FromSeconds(1));
        DemoClickCountText.FontSize += 1;
    }

    /// <summary>
    /// 枚举系统串口并填充端口下拉框。这里只获取端口名，不代表已经和设备建立通信。
    /// </summary>
    private void ScanSerialPorts()
    {
        // 打开中的端口不能重新枚举：重设 ItemsSource 会把已选端口清空，导致无法正常关闭
        if (_serialSession.IsOpen)
        {   // 暂停当前线程 1 秒
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

    // 同一个按钮承担开与关，以会话状态为准，不依赖按钮当前显示的文案
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
    /// 用当前端口和波特率打开串口。端口被占用、参数无效等预期错误会写入接收区，
    /// 不让异常越过 WPF 按钮事件处理器。
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

    /// <summary>
    /// 关闭并释放串口，同时把界面恢复到“未连接”状态。
    /// 即使底层关闭时报告错误，也要复位界面，避免继续向已失效端口发送。
    /// </summary>
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
    /// 发送输入框内容。文本模式按 UTF-8 写出；十六进制模式解析为原始字节，
    /// 并可选在帧尾追加 Modbus RTU CRC16（低字节在前）。
    /// 这里不验证 Modbus 功能码、地址或寄存器数量，只负责字节转换和发送。
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

    // 十六进制模式下发送的是完整二进制帧；CRLF 会成为额外帧尾字节，因此禁用文本行终止选项
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
    /// <paramref name="receivedBytes"/> 为 0 表示程序生成的状态日志，不计入接收字节数。
    /// </summary>
    /// <param name="text">要追加的文本。</param>
    /// <param name="receivedBytes">该段串口数据按当前 UTF-8 编码统计的字节数；日志传 0。</param>
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

    // ===== ADC 采集页：SN 绑定、串口采集、区间判定与 TXT 记录 =====
    // 判定区间、日志格式与“Sensor Voltage:”解析规则和原 slscs 工具保持一致。

    /// <summary>重新枚举系统串口并填充 ADC 采集页的端口下拉框。</summary>
    private void RefreshAdcPorts()
    {
        if (_adcSerialSession.IsOpen)
        {
            AdcPortStatusText.Text = "串口已打开，请先关闭串口再刷新。";
            AdcPortStatusText.Foreground = ThemeBrush("StateWarningBrush");
            return;
        }

        try
        {
            var ports = SerialPort.GetPortNames()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetComPortNumber)
                .ThenBy(port => port, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            AdcPortComboBox.ItemsSource = ports;
            if (ports.Length > 0)
            {
                AdcPortComboBox.SelectedIndex = 0;
                AdcPortStatusText.Text = $"检测到 {ports.Length} 个串口，等待打开。";
                AdcPortStatusText.Foreground = ThemeBrush("StateSuccessBrush");
            }
            else
            {
                AdcPortStatusText.Text = "未检测到任何串口，请检查 CH340 驱动 / USB 线。";
                AdcPortStatusText.Foreground = ThemeBrush("StateWarningBrush");
            }

            AppendAdcLog("串口列表刷新完成。");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException or Win32Exception)
        {
            AdcPortComboBox.ItemsSource = Array.Empty<string>();
            AdcPortStatusText.Text = $"扫描串口失败：{exception.Message}";
            AdcPortStatusText.Foreground = ThemeBrush("StateDangerBrush");
        }
    }

    private void AdcRefreshPortsClick(object sender, RoutedEventArgs e) => RefreshAdcPorts();

    private void AdcChoosePathClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存数据",
            FileName = "adc_record.txt",
            Filter = "文本文件 (*.txt)|*.txt"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _adcSavePath = dialog.FileName;
        AdcSavePathText.Text = $"保存路径：{_adcSavePath}";
        AppendAdcLog($"保存路径修改为：{_adcSavePath}");
    }

    /// <summary>向 TXT 追加一行采集记录；返回是否写入成功。</summary>
    private bool WriteAdcRecord(string sn, int value, string description)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}, SN:{sn}, {description}, ADC:{value}{Environment.NewLine}";
        try
        {
            File.AppendAllText(_adcSavePath, line, Encoding.UTF8);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            AppendAdcLog($"文件写入失败：{exception.Message}");
            return false;
        }
    }

    // 命中该区间视为采集达标，会自动保存一次
    private static bool IsAdcTargetRange(int value) => value >= 1700 && value < 3200;

    /// <summary>
    /// 计算 ADC 值所属区间并返回判定文本。值变化时追加一行区间日志；
    /// <paramref name="lockRange"/> 为 true 时把结果锁定为本次采集的最终区间。
    /// </summary>
    private string GetAdcRangeText(int value, bool lockRange = false)
    {
        string text;
        if (value < 1500)
        {
            _adcCount = 1;
            text = "挑出！！！！！！";
        }
        else if (value >= 1500 && value < 1700)
        {
            _adcCount = 2;
            text = "区间(1460-1500)";
        }
        else if (value > 1700 && value < 2000)
        {
            _adcCount = 3;
            text = "区间(1530-1570)";
        }
        else if (value > 2000 && value < 2400)
        {
            _adcCount = 4;
            text = "区间(1580-1620)";
        }
        else if (value > 2400)
        {
            _adcCount = 5;
            text = "区间(1680-1720)";
        }
        else
        {
            text = "超出标准区间";
        }

        if (lockRange)
        {
            _adcFixedRangeText = text;
        }

        if (_adcLastAdc != value)
        {
            var logLine =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}, SN:{_adcInputSn}, ADC:{value}, count:{_adcCount}, 判定文本:{text}{Environment.NewLine}";
            try
            {
                File.AppendAllText(_adcSavePath, logLine, Encoding.UTF8);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                AppendAdcLog($"区间日志写入失败：{exception.Message}");
            }

            _adcLastAdc = value;
        }

        return text;
    }

    // 落在区间内部（留出边界余量）时显示绿色，其余显示红色，颜色取自主题状态色板
    private void SetAdcValueColor(int value)
    {
        var brush = ThemeBrush("StateDangerBrush");
        if (_adcCount == 2 && value >= 1460 && value < 1500)
        {
            brush = ThemeBrush("StateSuccessBrush");
        }
        else if (_adcCount == 3 && value >= 1530 && value < 1570)
        {
            brush = ThemeBrush("StateSuccessBrush");
        }
        else if (_adcCount == 4 && value >= 1580 && value < 1620)
        {
            brush = ThemeBrush("StateSuccessBrush");
        }
        else if (_adcCount == 5 && value >= 1680 && value < 1720)
        {
            brush = ThemeBrush("StateSuccessBrush");
        }

        AdcValueText.Foreground = brush;
    }

    /// <summary>在 UI 线程处理一个 ADC 值：刷新大数字与判定文本，命中目标区间时自动保存一次。</summary>
    private void OnAdcValue(int value)
    {
        _adcCurrentValue = value;
        if (_adcCount == 0)
        {
            SetAdcValueColor(value);
            if (_adcFixedRangeText.Length == 0)
            {
                _adcFixedRangeText = GetAdcRangeText(value);
            }

            AdcValueText.Text = $"实时 {_adcFixedRangeText} ADC:{value}";

            if (IsAdcTargetRange(value))
            {
                GetAdcRangeText(value, lockRange: true);
                SetAdcValueColor(value);
                if (_adcCount == 2)
                {
                    AdcValueText.Text = $"区间1(1460-1500) ADC:{value}";
                }
                else if (_adcCount == 3)
                {
                    AdcValueText.Text = $"区间2(1530-1570) ADC:{value}";
                }
                else if (_adcCount == 4)
                {
                    AdcValueText.Text = $"区间3(1580-1620) ADC:{value}";
                }
                else if (_adcCount == 5)
                {
                    AdcValueText.Text = $"区间4(1680-1720) ADC:{value}";
                }
                else
                {
                    AdcValueText.Text = $"else0 ADC:{value}";
                }

                if (!_adcAutoSaved)
                {
                    WriteAdcRecord(_adcInputSn, value, "自动采集");
                    _adcAutoSaved = true;
                    AppendAdcLog($"SN:{_adcInputSn} ADC {value} 自动保存完成");
                }
            }
        }
        else
        {
            SetAdcValueColor(value);
            if (_adcCount == 2)
            {
                AdcValueText.Text = $"elif1区间(1460-1500) ADC:{value}";
            }
            else if (_adcCount == 3)
            {
                AdcValueText.Text = $"elif2区间(1530-1570) ADC:{value}";
            }
            else if (_adcCount == 4)
            {
                AdcValueText.Text = $"elif3区间(1580-1620) ADC:{value}";
            }
            else if (_adcCount == 5)
            {
                AdcValueText.Text = $"elif4区间(1680-1720) ADC:{value}";
            }
            else
            {
                GetAdcRangeText(value);
                AdcValueText.Text = $"else0 ADC:{value}";
            }
        }
    }

    private void AdcManualSaveClick(object sender, RoutedEventArgs e)
    {
        if (_adcCurrentValue is null)
        {
            AppendAdcLog("暂无有效电压数据，无法保存。");
            return;
        }

        if (_adcInputSn.Length == 0)
        {
            AppendAdcLog("请先输入 SN 码！");
            return;
        }

        WriteAdcRecord(_adcInputSn, _adcCurrentValue.Value, "手动点击保存");
        AppendAdcLog($"SN:{_adcInputSn} 手动保存 ADC:{_adcCurrentValue.Value}");
        AdcSnTextBox.Clear();
        _adcInputSn = "";
        AdcSnTextBox.Focus();
    }

    /// <summary>向采集日志追加一行并滚动到底部；超长时从头部截断。</summary>
    private void AppendAdcLog(string text)
    {
        var combined = AdcLogTextBox.Text + text + Environment.NewLine;
        AdcLogTextBox.Text = combined.Length > MaxReceiveLength
            ? combined[^MaxReceiveLength..]
            : combined;
        AdcLogTextBox.ScrollToEnd();
    }

    private void AdcSnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AutoOpenAdcBySn();
        }
    }

    private void AdcAutoCollectClick(object sender, RoutedEventArgs e) => AutoOpenAdcBySn();

    /// <summary>
    /// 用输入框中的 SN 绑定本次采集：串口未打开时自动打开；已打开时按 SN 是否变化决定是否重置区间锁定。
    /// </summary>
    private void AutoOpenAdcBySn()
    {
        var snText = AdcSnTextBox.Text.Trim();
        if (snText.Length == 0)
        {
            AppendAdcLog("SN 码不能为空！");
            return;
        }

        if (_adcSerialSession.IsOpen)
        {
            if (_adcInputSn != snText)
            {
                _adcInputSn = snText;
                ResetAdcCollectStatus();
                AppendAdcLog($"更换 SN 为：{_adcInputSn}，区间锁定已解除，重新识别区间。");
            }
            else
            {
                AppendAdcLog("串口已打开，当前 SN 无需重复确认。");
            }

            return;
        }

        _adcInputSn = snText;
        ResetAdcCollectStatus();
        AppendAdcLog($"当前绑定 SN 码：{_adcInputSn}");
        OpenAdcSerial();
    }

    /// <summary>重置本次采集的判定状态，大数字恢复为等待数据。</summary>
    private void ResetAdcCollectStatus()
    {
        _adcCurrentValue = null;
        _adcFixedRangeText = "";
        _adcCount = 0;
        _adcAutoSaved = false;
        _adcLastAdc = null;
        AdcValueText.Foreground = ThemeBrush("BrandBrush");
        AdcValueText.Text = "当前 ADC 值：无数据";
    }

    private int GetSelectedAdcBaudRate() =>
        AdcBaudComboBox.SelectedItem is ComboBoxItem { Content: string text }
        && int.TryParse(text, out var baudRate)
            ? baudRate
            : 115200;

    private void AdcTogglePortClick(object sender, RoutedEventArgs e)
    {
        if (_adcSerialSession.IsOpen)
        {
            CloseAdcSerialPort();
            return;
        }

        OpenAdcSerial();
    }

    /// <summary>用当前端口和波特率打开 ADC 采集串口；预期错误写入日志，不让异常越过按钮事件。</summary>
    private void OpenAdcSerial()
    {
        if (AdcPortComboBox.SelectedItem is not string portName || string.IsNullOrWhiteSpace(portName))
        {
            AppendAdcLog("错误：请先刷新并选择有效串口。");
            return;
        }

        var baudRate = GetSelectedAdcBaudRate();
        try
        {
            _adcSerialSession.Open(portName, baudRate);
            UpdateAdcSerialUi(true);
            AppendAdcLog($"串口 {portName} 连接成功，波特率 {baudRate}（8-N-1）。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidOperationException or ArgumentException)
        {
            UpdateAdcSerialUi(false);
            AppendAdcLog($"串口打开失败：{exception.Message}");
        }
    }

    /// <summary>关闭 ADC 采集串口并恢复界面；未打开时调用无副作用。</summary>
    private void CloseAdcSerialPort()
    {
        var wasOpen = _adcSerialSession.IsOpen;
        try
        {
            _adcSerialSession.Close();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            AppendAdcLog($"关闭串口时出现异常：{exception.Message}");
        }

        UpdateAdcSerialUi(false);
        if (wasOpen)
        {
            AppendAdcLog("串口已关闭。");
        }
    }

    /// <summary>ADC 采集页的串口状态：按钮文案与配色、端口参数是否可改、状态文字。</summary>
    private void UpdateAdcSerialUi(bool isOpen)
    {
        AdcTogglePortButton.Content = isOpen ? "关闭串口" : "手动打开串口";
        AdcTogglePortButton.Background = ThemeBrush(isOpen ? "StateDangerBrush" : "BrandBrush");
        AdcTogglePortButton.Foreground = Brushes.White;
        AdcPortComboBox.IsEnabled = !isOpen;
        AdcBaudComboBox.IsEnabled = !isOpen;
        AdcRefreshPortsButton.IsEnabled = !isOpen;
        AdcPortStatusText.Text = isOpen
            ? $"{_adcSerialSession.PortName} 已连接 · {_adcSerialSession.BaudRate}"
            : "串口未打开。";
        AdcPortStatusText.Foreground = ThemeBrush(isOpen ? "StateSuccessBrush" : "TextSubtleBrush");
    }

    /// <summary>
    /// UI 线程定时调用：取走 ADC 串口攒下的文本，按换行切分后逐行解析；
    /// 末尾不完整的行留在缓冲区，等下次数据到达再拼接。
    /// </summary>
    private void RefreshAdcReceiveArea()
    {
        if (_adcSerialSession.TryTakePendingText(out var text, out _))
        {
            AppendAdcReceiveText(text);
        }

        if (_adcSerialSession.TryTakeFault(out var faultMessage))
        {
            AppendAdcLog($"读取中断：{faultMessage}");
        }
    }

    private void AppendAdcReceiveText(string text)
    {
        _adcLineBuffer.Append(text);
        if (_adcLineBuffer.Length > MaxReceiveLength)
        {
            _adcLineBuffer.Remove(0, _adcLineBuffer.Length - MaxReceiveLength);
        }

        var buffered = _adcLineBuffer.ToString();
        var lastNewline = buffered.LastIndexOf('\n');
        if (lastNewline < 0)
        {
            return;
        }

        _adcLineBuffer.Clear();
        _adcLineBuffer.Append(buffered[(lastNewline + 1)..]);

        foreach (var line in buffered[..lastNewline].Split('\n'))
        {
            ProcessAdcLine(line.TrimEnd('\r'));
        }
    }

    /// <summary>显示一行串口输出；含 “Sensor Voltage:” 时解析其后的整数作为 ADC 值。</summary>
    private void ProcessAdcLine(string line)
    {
        line = AnsiEscapeRegex.Replace(line, "");
        AppendAdcLog(line);

        const string marker = "Sensor Voltage:";
        var index = line.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return;
        }

        var digits = new StringBuilder();
        foreach (var c in line[(index + marker.Length)..].TrimStart())
        {
            if (!char.IsDigit(c))
            {
                break;
            }

            digits.Append(c);
        }

        if (digits.Length > 0 && int.TryParse(digits.ToString(), out var value))
        {
            OnAdcValue(value);
        }
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