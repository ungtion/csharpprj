using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Windows.Threading;

namespace CSharpProject;

internal sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private const int MaxTextLength = 20000;

    private readonly SerialPortSession _serialSession = new();
    private readonly SerialPortSession _adcSerialSession = new();
    private readonly ModbusTcpSession _modbusTcpSession = new();
    private readonly AdcCollectionService _adcCollectionService = new();
    private readonly IInteractionService _interactionService;
    private readonly DispatcherTimer _simulationTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _receiveFlushTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private long _totalReceivedBytes;
    private int _demoClickCount;
    private string _currentPage = "设备总览";
    private string _clockText = "";
    private string _updateTimeText = "";
    private string _temperatureText = "";
    private string _pressureText = "";
    private string _speedText = "";
    private string _efficiencyText = "";
    private string[] _serialPorts = [];
    private string? _selectedSerialPort;
    private string _scanStatus = "正在读取串口信息…";
    private string _serialStatus = "未连接";
    private bool _isSerialOpen;
    private string _baudRate = "115200";
    private string _sendText = "";
    private bool _appendCrLf = true;
    private bool _hexSendMode;
    private bool _appendModbusCrc;
    private string _receiveText = "";
    private string _receiveCountText = "已接收 0 字节";
    private string _adcSerialNumberInput = "";
    private string[] _adcPorts = [];
    private string? _selectedAdcPort;
    private string _adcBaudRate = "115200";
    private string _adcPortStatus = "正在读取串口信息…";
    private string _adcSavePathText = "保存路径：adc_record.txt";
    private string _adcValueText = "当前 ADC 值：无数据";
    private bool _hasAdcValue;
    private bool _isAdcValueInAcceptedRange;
    private string _adcLogText = "";
    private string _mc405Host = "192.168.0.250";
    private string _mc405Port = "502";
    private string _mc405UnitId = "1";
    private string _mc405FunctionCode = "3";
    private string _mc405StartAddress = "0";
    private string _mc405Quantity = "10";
    private string _mc405ConnectionStatus = "未连接";
    private string _mc405ResultText = "";
    private string _mc405LogText = "";
    private bool _isMc405Connected;
    private bool _isMc405Connecting;
    private double _demoFontSize = 12;

    public MainWindowViewModel(IInteractionService interactionService)
    {
        _interactionService = interactionService;

        NavigateCommand = new RelayCommand<string>(Navigate);
        ScanPortsCommand = new RelayCommand(ScanSerialPorts);
        CopyLogCommand = new RelayCommand(CopyCommunicationLog);
        SaveLogCommand = new RelayCommand(SaveCommunicationLog);
        ToggleSerialCommand = new RelayCommand(ToggleSerialPort);
        SendCommand = new RelayCommand(Send, () => IsSerialOpen);
        ClearReceiveCommand = new RelayCommand(ClearReceive);
        AutoCollectCommand = new RelayCommand(AutoOpenAdcBySn);
        RefreshAdcPortsCommand = new RelayCommand(RefreshAdcPorts);
        ToggleAdcSerialCommand = new RelayCommand(ToggleAdcSerialPort);
        ChooseAdcPathCommand = new RelayCommand(ChooseAdcPath);
        SaveAdcValueCommand = new RelayCommand(SaveAdcValue);
        ConnectMc405Command = new AsyncRelayCommand(ConnectMc405Async, () => !IsMc405Connecting && !IsMc405Connected);
        DisconnectMc405Command = new RelayCommand(DisconnectMc405, () => IsMc405Connected && !IsMc405Connecting);
        ReadMc405Command = new AsyncRelayCommand(ReadMc405Async, () => IsMc405Connected && !IsMc405Connecting);
        DemoClickCountCommand = new AsyncRelayCommand(DemoClickCountAsync);

        _simulationTimer.Tick += (_, _) => UpdateSimulationDisplay();
        _receiveFlushTimer.Tick += (_, _) =>
        {
            RefreshSerialReceiveArea();
            RefreshAdcReceiveArea();
        };

        UpdateSimulationDisplay();
        ScanSerialPorts();
        RefreshAdcPorts();
        _simulationTimer.Start();
        _receiveFlushTimer.Start();
    }

    public RelayCommand<string> NavigateCommand { get; }
    public RelayCommand ScanPortsCommand { get; }
    public RelayCommand CopyLogCommand { get; }
    public RelayCommand SaveLogCommand { get; }
    public RelayCommand ToggleSerialCommand { get; }
    public RelayCommand SendCommand { get; }
    public RelayCommand ClearReceiveCommand { get; }
    public RelayCommand AutoCollectCommand { get; }
    public RelayCommand RefreshAdcPortsCommand { get; }
    public RelayCommand ToggleAdcSerialCommand { get; }
    public RelayCommand ChooseAdcPathCommand { get; }
    public RelayCommand SaveAdcValueCommand { get; }
    public AsyncRelayCommand ConnectMc405Command { get; }
    public RelayCommand DisconnectMc405Command { get; }
    public AsyncRelayCommand ReadMc405Command { get; }
    public AsyncRelayCommand DemoClickCountCommand { get; }

    public string CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsOverviewPage));
                OnPropertyChanged(nameof(IsAdcPage));
                OnPropertyChanged(nameof(IsMc405Page));
                OnPropertyChanged(nameof(IsRealtimePage));
                OnPropertyChanged(nameof(IsRecipePage));
                OnPropertyChanged(nameof(IsAlarmPage));
                OnPropertyChanged(nameof(IsSystemPage));
            }
        }
    }

    public bool IsOverviewPage => CurrentPage == "设备总览";
    public bool IsAdcPage => CurrentPage == "ADC 采集";
    public bool IsMc405Page => CurrentPage == "MC405 通讯测试";
    public bool IsRealtimePage => CurrentPage == "实时监控";
    public bool IsRecipePage => CurrentPage == "bake";
    public bool IsAlarmPage => CurrentPage == "报警记录";
    public bool IsSystemPage => CurrentPage == "系统信息";

    public string ClockText { get => _clockText; private set => SetProperty(ref _clockText, value); }
    public string UpdateTimeText { get => _updateTimeText; private set => SetProperty(ref _updateTimeText, value); }
    public string TemperatureText { get => _temperatureText; private set => SetProperty(ref _temperatureText, value); }
    public string PressureText { get => _pressureText; private set => SetProperty(ref _pressureText, value); }
    public string SpeedText { get => _speedText; private set => SetProperty(ref _speedText, value); }
    public string EfficiencyText { get => _efficiencyText; private set => SetProperty(ref _efficiencyText, value); }
    public string[] SerialPorts { get => _serialPorts; private set => SetProperty(ref _serialPorts, value); }
    public string? SelectedSerialPort { get => _selectedSerialPort; set => SetProperty(ref _selectedSerialPort, value); }
    public string ScanStatus { get => _scanStatus; private set => SetProperty(ref _scanStatus, value); }
    public string SerialStatus { get => _serialStatus; private set => SetProperty(ref _serialStatus, value); }
    public bool IsSerialOpen
    {
        get => _isSerialOpen;
        private set
        {
            if (SetProperty(ref _isSerialOpen, value))
            {
                OnPropertyChanged(nameof(IsSerialConfigurationEnabled));
                OnPropertyChanged(nameof(SerialToggleText));
                SendCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsSerialConfigurationEnabled => !IsSerialOpen;
    public string SerialToggleText => IsSerialOpen ? "关闭串口" : "打开串口";
    public string BaudRate { get => _baudRate; set => SetProperty(ref _baudRate, value); }
    public string SendText { get => _sendText; set => SetProperty(ref _sendText, value); }
    public bool AppendCrLf { get => _appendCrLf; set => SetProperty(ref _appendCrLf, value); }
    public bool HexSendMode
    {
        get => _hexSendMode;
        set
        {
            if (SetProperty(ref _hexSendMode, value))
            {
                OnPropertyChanged(nameof(IsCrLfEnabled));
                OnPropertyChanged(nameof(IsCrcEnabled));
            }
        }
    }

    public bool AppendModbusCrc { get => _appendModbusCrc; set => SetProperty(ref _appendModbusCrc, value); }
    public bool IsCrLfEnabled => !HexSendMode;
    public bool IsCrcEnabled => HexSendMode;
    public string ReceiveText { get => _receiveText; private set => SetProperty(ref _receiveText, value); }
    public string ReceiveCountText { get => _receiveCountText; private set => SetProperty(ref _receiveCountText, value); }

    public string AdcSerialNumberInput
    {
        get => _adcSerialNumberInput;
        set => SetProperty(ref _adcSerialNumberInput, value);
    }

    public string[] AdcPorts { get => _adcPorts; private set => SetProperty(ref _adcPorts, value); }
    public string? SelectedAdcPort { get => _selectedAdcPort; set => SetProperty(ref _selectedAdcPort, value); }
    public string AdcBaudRate { get => _adcBaudRate; set => SetProperty(ref _adcBaudRate, value); }
    public string AdcPortStatus { get => _adcPortStatus; private set => SetProperty(ref _adcPortStatus, value); }
    public string AdcToggleText => _adcSerialSession.IsOpen ? "关闭串口" : "手动打开串口";
    public bool IsAdcPortConfigurationEnabled => !_adcSerialSession.IsOpen;
    public bool IsAdcSerialOpen => _adcSerialSession.IsOpen;
    public string AdcSavePathText { get => _adcSavePathText; private set => SetProperty(ref _adcSavePathText, value); }
    public string AdcValueText { get => _adcValueText; private set => SetProperty(ref _adcValueText, value); }
    public bool HasAdcValue { get => _hasAdcValue; private set => SetProperty(ref _hasAdcValue, value); }
    public bool IsAdcValueInAcceptedRange
    {
        get => _isAdcValueInAcceptedRange;
        private set => SetProperty(ref _isAdcValueInAcceptedRange, value);
    }

    public string AdcLogText { get => _adcLogText; private set => SetProperty(ref _adcLogText, value); }
    public string Mc405Host { get => _mc405Host; set => SetProperty(ref _mc405Host, value); }
    public string Mc405Port { get => _mc405Port; set => SetProperty(ref _mc405Port, value); }
    public string Mc405UnitId { get => _mc405UnitId; set => SetProperty(ref _mc405UnitId, value); }
    public string Mc405FunctionCode { get => _mc405FunctionCode; set => SetProperty(ref _mc405FunctionCode, value); }
    public string Mc405StartAddress { get => _mc405StartAddress; set => SetProperty(ref _mc405StartAddress, value); }
    public string Mc405Quantity { get => _mc405Quantity; set => SetProperty(ref _mc405Quantity, value); }
    public string Mc405ConnectionStatus { get => _mc405ConnectionStatus; private set => SetProperty(ref _mc405ConnectionStatus, value); }
    public string Mc405ResultText { get => _mc405ResultText; private set => SetProperty(ref _mc405ResultText, value); }
    public string Mc405LogText { get => _mc405LogText; private set => SetProperty(ref _mc405LogText, value); }
    public bool IsMc405Connected
    {
        get => _isMc405Connected;
        private set
        {
            if (SetProperty(ref _isMc405Connected, value))
            {
                OnPropertyChanged(nameof(IsMc405ConfigurationEnabled));
                DisconnectMc405Command.NotifyCanExecuteChanged();
                ReadMc405Command.NotifyCanExecuteChanged();
                ConnectMc405Command.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsMc405Connecting
    {
        get => _isMc405Connecting;
        private set
        {
            if (SetProperty(ref _isMc405Connecting, value))
            {
                ConnectMc405Command.NotifyCanExecuteChanged();
                ReadMc405Command.NotifyCanExecuteChanged();
                DisconnectMc405Command.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsMc405ConfigurationEnabled => !IsMc405Connected && !IsMc405Connecting;
    public double DemoFontSize { get => _demoFontSize; private set => SetProperty(ref _demoFontSize, value); }
    public string DemoClickCountText => $"点击{_demoClickCount} 次";

    private void Navigate(string? page)
    {
        if (page is not null)
        {
            CurrentPage = page;
        }
    }

    private void UpdateSimulationDisplay()
    {
        var now = DateTime.Now;
        ClockText = now.ToString("yyyy-MM-dd  HH:mm:ss");
        UpdateTimeText = now.ToString("HH:mm:ss");
        TemperatureText = (42.6 + (Random.Shared.NextDouble() - 0.5) * 0.6).ToString("F1");
        PressureText = (0.62 + (Random.Shared.NextDouble() - 0.5) * 0.02).ToString("F2");
        SpeedText = (1480 + Random.Shared.Next(-4, 5)).ToString();
        EfficiencyText = (98.6 + (Random.Shared.NextDouble() - 0.5) * 0.2).ToString("F1");
    }

    private void ScanSerialPorts()
    {
        if (_serialSession.IsOpen)
        {
            ScanStatus = "串口已打开，请先关闭串口再重新扫描。";
            return;
        }

        try
        {
            SerialPorts = GetAvailablePorts();
            SelectedSerialPort = SerialPorts.FirstOrDefault();
            ScanStatus = SerialPorts.Length > 0
                ? $"检测到 {SerialPorts.Length} 个串口。这里只枚举系统端口，尚未连接设备。"
                : "未检测到串口。请检查设备、驱动或 USB 转串口连接后重新扫描。";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException or Win32Exception)
        {
            SerialPorts = [];
            ScanStatus = $"扫描串口失败：{exception.Message}";
        }
    }

    private void CopyCommunicationLog()
    {
        if (ReceiveText.Length == 0)
        {
            AppendCommunicationLog("接收区为空，没有可复制的内容。");
            return;
        }

        try
        {
            var content = ReceiveText;
            _interactionService.SetClipboardText(content);
            AppendCommunicationLog($"已复制 {content.Length} 个字符到剪贴板。");
        }
        catch (InvalidOperationException exception)
        {
            AppendCommunicationLog($"复制到剪贴板失败：{exception.Message}");
        }
    }

    private void SaveCommunicationLog()
    {
        if (ReceiveText.Length == 0)
        {
            AppendCommunicationLog("接收区为空，没有可保存的内容。");
            return;
        }

        var path = _interactionService.SelectSavePath(
            "保存通讯日志",
            $"serial-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*");
        if (path is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(path, ReceiveText, Encoding.UTF8);
            AppendCommunicationLog($"日志已保存：{path}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            AppendCommunicationLog($"保存日志失败：{exception.Message}");
        }
    }

    private void ToggleSerialPort()
    {
        if (_serialSession.IsOpen)
        {
            CloseSerialPort();
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedSerialPort))
        {
            AppendCommunicationLog("未选择串口，请先点击“扫描”并选择端口。");
            return;
        }

        if (!int.TryParse(BaudRate, out var baudRate))
        {
            baudRate = 9600;
        }

        try
        {
            _serialSession.Open(SelectedSerialPort, baudRate);
            UpdateSerialState(true);
            AppendCommunicationLog($"已打开 {SelectedSerialPort}，波特率 {baudRate}（8-N-1）。");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException
                                              or InvalidOperationException or ArgumentException)
        {
            UpdateSerialState(false);
            AppendCommunicationLog($"打开串口失败：{exception.Message}");
        }
    }

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

        UpdateSerialState(false);
        if (wasOpen)
        {
            AppendCommunicationLog("串口已关闭。");
        }
    }

    private void UpdateSerialState(bool isOpen)
    {
        IsSerialOpen = isOpen;
        SerialStatus = isOpen ? $"{_serialSession.PortName} 已连接 · {_serialSession.BaudRate}" : "未连接";
    }

    private void Send()
    {
        if (!_serialSession.IsOpen)
        {
            AppendCommunicationLog("串口未打开，无法发送。");
            return;
        }

        var payload = SendText;
        if (payload.Length == 0)
        {
            return;
        }

        try
        {
            if (HexSendMode)
            {
                if (!HexText.TryParse(payload, out var frame))
                {
                    AppendCommunicationLog("十六进制内容无效：只能包含 0-9、A-F 和空格/逗号等分隔符，且字节须成对。");
                    return;
                }

                if (AppendModbusCrc)
                {
                    var crc = ModbusCrc16.Compute(frame);
                    AppendCommunicationLog($"Modbus CRC16 = 0x{crc:X4}（帧尾字节 {ModbusCrc16.FormatCrcBytes(crc)}）");
                    frame = ModbusCrc16.AppendCrc(frame);
                }

                _serialSession.WriteBytes(frame);
                AppendCommunicationLog($"发送 {frame.Length} 字节：{HexText.Format(frame)}");
                return;
            }

            if (AppendCrLf)
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

    private void ClearReceive()
    {
        _serialSession.DiscardPendingData();
        _totalReceivedBytes = 0;
        ReceiveText = "";
        ReceiveCountText = "已接收 0 字节";
    }

    private void RefreshSerialReceiveArea()
    {
        if (_serialSession.TryTakePendingText(out var text, out var byteCount))
        {
            if (byteCount > 0)
            {
                _totalReceivedBytes += byteCount;
                ReceiveCountText = $"已接收 {_totalReceivedBytes} 字节";
            }

            AppendText(ref _receiveText, text, value => ReceiveText = value);
        }

        if (_serialSession.TryTakeFault(out var faultMessage))
        {
            AppendCommunicationLog($"读取中断：{faultMessage}");
        }
    }

    private void AppendCommunicationLog(string message) =>
        AppendReceiveText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

    private void AppendReceiveText(string text) => AppendText(ref _receiveText, text, value => ReceiveText = value);

    private void RefreshAdcPorts()
    {
        if (_adcSerialSession.IsOpen)
        {
            AdcPortStatus = "串口已打开，请先关闭串口再刷新。";
            return;
        }

        try
        {
            AdcPorts = GetAvailablePorts();
            SelectedAdcPort = AdcPorts.FirstOrDefault();
            AdcPortStatus = AdcPorts.Length > 0
                ? $"检测到 {AdcPorts.Length} 个串口，等待打开。"
                : "未检测到任何串口，请检查 CH340 驱动 / USB 线。";
            AppendAdcLog("串口列表刷新完成。");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException or Win32Exception)
        {
            AdcPorts = [];
            AdcPortStatus = $"扫描串口失败：{exception.Message}";
        }
    }

    private void ChooseAdcPath()
    {
        var path = _interactionService.SelectSavePath("保存数据", "adc_record.txt", "文本文件 (*.txt)|*.txt");
        if (path is null)
        {
            return;
        }

        _adcCollectionService.SavePath = path;
        AdcSavePathText = $"保存路径：{path}";
        AppendAdcLog($"保存路径修改为：{path}");
    }

    private void AutoOpenAdcBySn()
    {
        var serialNumber = AdcSerialNumberInput.Trim();
        if (serialNumber.Length == 0)
        {
            AppendAdcLog("SN 码不能为空！");
            return;
        }

        if (_adcSerialSession.IsOpen)
        {
            if (_adcCollectionService.SerialNumber != serialNumber)
            {
                _adcCollectionService.BindSerialNumber(serialNumber);
                ResetAdcStatus();
                AppendAdcLog($"更换 SN 为：{serialNumber}，区间锁定已解除，重新识别区间。");
            }
            else
            {
                AppendAdcLog("串口已打开，当前 SN 无需重复确认。");
            }

            return;
        }

        _adcCollectionService.BindSerialNumber(serialNumber);
        ResetAdcStatus();
        AppendAdcLog($"当前绑定 SN 码：{serialNumber}");
        OpenAdcSerial();
    }

    private void ResetAdcStatus()
    {
        _adcCollectionService.Reset();
        AdcValueText = "当前 ADC 值：无数据";
        IsAdcValueInAcceptedRange = false;
        HasAdcValue = false;
    }

    private void ToggleAdcSerialPort()
    {
        if (_adcSerialSession.IsOpen)
        {
            CloseAdcSerialPort();
        }
        else
        {
            OpenAdcSerial();
        }
    }

    private void OpenAdcSerial()
    {
        if (string.IsNullOrWhiteSpace(SelectedAdcPort))
        {
            AppendAdcLog("错误：请先刷新并选择有效串口。");
            return;
        }

        if (!int.TryParse(AdcBaudRate, out var baudRate))
        {
            baudRate = 115200;
        }

        try
        {
            _adcSerialSession.Open(SelectedAdcPort, baudRate);
            UpdateAdcSerialState();
            AppendAdcLog($"串口 {SelectedAdcPort} 连接成功，波特率 {baudRate}（8-N-1）。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidOperationException or ArgumentException)
        {
            UpdateAdcSerialState();
            AppendAdcLog($"串口打开失败：{exception.Message}");
        }
    }

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

        UpdateAdcSerialState();
        if (wasOpen)
        {
            AppendAdcLog("串口已关闭。");
        }
    }

    private void UpdateAdcSerialState()
    {
        OnPropertyChanged(nameof(AdcToggleText));
        OnPropertyChanged(nameof(IsAdcPortConfigurationEnabled));
        OnPropertyChanged(nameof(IsAdcSerialOpen));
        AdcPortStatus = _adcSerialSession.IsOpen
            ? $"{_adcSerialSession.PortName} 已连接 · {_adcSerialSession.BaudRate}"
            : "串口未打开。";
    }

    private void RefreshAdcReceiveArea()
    {
        if (_adcSerialSession.TryTakePendingText(out var text, out _))
        {
            foreach (var line in _adcCollectionService.ProcessReceivedText(text))
            {
                AppendAdcLog(line.Text);
                if (line.Value is int value)
                {
                    OnAdcValue(value);
                }
            }
        }

        if (_adcSerialSession.TryTakeFault(out var faultMessage))
        {
            AppendAdcLog($"读取中断：{faultMessage}");
        }
    }

    private void OnAdcValue(int value)
    {
        var update = _adcCollectionService.ProcessValue(value);
        AdcValueText = update.DisplayText;
        IsAdcValueInAcceptedRange = update.IsWithinAcceptedRange;
        HasAdcValue = true;

        if (update.RangeLogError is not null)
        {
            AppendAdcLog($"区间日志写入失败：{update.RangeLogError}");
        }

        if (update.AutoSaveAttempted)
        {
            if (update.AutoSaveError is not null)
            {
                AppendAdcLog($"文件写入失败：{update.AutoSaveError}");
            }

            AppendAdcLog($"SN:{_adcCollectionService.SerialNumber} ADC {value} 自动保存完成");
        }
    }

    private void SaveAdcValue()
    {
        if (_adcCollectionService.CurrentValue is not int currentValue)
        {
            AppendAdcLog("暂无有效电压数据，无法保存。");
            return;
        }

        if (_adcCollectionService.SerialNumber.Length == 0)
        {
            AppendAdcLog("请先输入 SN 码！");
            return;
        }

        var error = _adcCollectionService.TryWriteRecord(currentValue, "手动点击保存");
        if (error is not null)
        {
            AppendAdcLog($"文件写入失败：{error}");
        }

        AppendAdcLog($"SN:{_adcCollectionService.SerialNumber} 手动保存 ADC:{currentValue}");
        AdcSerialNumberInput = "";
        _adcCollectionService.ClearSerialNumber();
    }

    private async Task ConnectMc405Async()
    {
        var host = Mc405Host.Trim();
        if (host.Length == 0)
        {
            AppendMc405Log("请输入 MC405 的 IP 地址或主机名。");
            return;
        }

        if (!int.TryParse(Mc405Port, out var port) || port is < 1 or > 65535)
        {
            AppendMc405Log("TCP 端口须为 1–65535 之间的整数。");
            return;
        }

        IsMc405Connecting = true;
        Mc405ConnectionStatus = $"正在连接 {host}:{port}…";
        try
        {
            await _modbusTcpSession.ConnectAsync(host, port);
            IsMc405Connected = true;
            Mc405ConnectionStatus = $"已连接 {host}:{port}";
            AppendMc405Log($"已连接到 {host}:{port}。");
        }
        catch (Exception exception) when (exception is SocketException or IOException
                                              or OperationCanceledException or ArgumentException
                                              or InvalidOperationException)
        {
            _modbusTcpSession.Close();
            IsMc405Connected = false;
            Mc405ConnectionStatus = "连接失败";
            AppendMc405Log($"连接 {host}:{port} 失败：{exception.Message}");
        }
        finally
        {
            IsMc405Connecting = false;
        }
    }

    private void DisconnectMc405()
    {
        _modbusTcpSession.Close();
        IsMc405Connected = false;
        Mc405ConnectionStatus = "未连接";
        AppendMc405Log("已断开 MC405 连接。");
    }

    private async Task ReadMc405Async()
    {
        if (!IsMc405Connected)
        {
            AppendMc405Log("尚未连接控制器，请先建立 Modbus TCP 连接。");
            return;
        }

        if (!byte.TryParse(Mc405UnitId, out var unitId))
        {
            AppendMc405Log("Unit ID 须为 0–255 之间的整数。");
            return;
        }

        if (!ushort.TryParse(Mc405StartAddress, out var startAddress))
        {
            AppendMc405Log("起始地址须为 0–65535 之间的整数。");
            return;
        }

        if (!ushort.TryParse(Mc405Quantity, out var quantity) || quantity is < 1 or > 125
            || (uint)startAddress + quantity > 65536)
        {
            AppendMc405Log("读取数量须为 1–125，且地址范围不能超过 65535。");
            return;
        }

        if (!byte.TryParse(Mc405FunctionCode, out var functionCode) || functionCode is not (3 or 4))
        {
            AppendMc405Log("请选择保持寄存器（03）或输入寄存器（04）。");
            return;
        }

        IsMc405Connecting = true;
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

            Mc405ResultText = output.ToString();
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
            IsMc405Connected = false;
            Mc405ConnectionStatus = "连接中断";
            AppendMc405Log($"读取失败，连接已关闭：{exception.Message}");
        }
        finally
        {
            IsMc405Connecting = false;
        }
    }

    private void AppendAdcLog(string text) =>
        AppendText(ref _adcLogText, text + Environment.NewLine, value => AdcLogText = value);

    private void AppendMc405Log(string message) =>
        AppendText(ref _mc405LogText, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}", value => Mc405LogText = value);

    private async Task DemoClickCountAsync()
    {
        _demoClickCount++;
        OnPropertyChanged(nameof(DemoClickCountText));
        await Task.Delay(TimeSpan.FromSeconds(1));
        DemoFontSize += 1;
    }

    private static string[] GetAvailablePorts() =>
        SerialPort.GetPortNames()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetComPortNumber)
            .ThenBy(port => port, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static int GetComPortNumber(string port) =>
        port.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(port.AsSpan(3), out var number)
            ? number
            : int.MaxValue;

    private static string FormatModbusFrame(byte[] frame) =>
        string.Join(' ', Convert.ToHexString(frame).Chunk(2).Select(pair => new string(pair)));

    private static string EscapeControlChars(string text) =>
        text.Replace("\r", "\\r").Replace("\n", "\\n");

    private static void AppendText(ref string current, string text, Action<string> update)
    {
        var combined = current + text;
        if (combined.Length > MaxTextLength)
        {
            combined = combined[^MaxTextLength..];
        }

        update(combined);
    }

    public void Dispose()
    {
        _simulationTimer.Stop();
        _receiveFlushTimer.Stop();
        CloseSerialPort();
        CloseAdcSerialPort();
        _serialSession.Dispose();
        _adcSerialSession.Dispose();
        _modbusTcpSession.Dispose();
    }
}
