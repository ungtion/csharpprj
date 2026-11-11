using System.IO;
using System.IO.Ports;
using System.Text;

namespace CSharpProject;

/// <summary>
/// 封装单个串口连接。端口参数固定为 8 数据位 / 无校验 / 1 停止位 / 无流控，
/// 与 PLC、单片机调试口最常见的配置一致；其他组合需要按设备手册另行调整。
/// <para>
/// 线程约定：<see cref="SerialPort.DataReceived"/> 在线程池线程上触发，本类在该线程里只把数据写入内部缓冲区，
/// 界面线程通过 <see cref="TryTakePendingText"/> 定时批量取走，避免后台线程直接操作 WPF 控件。
/// 除标注了“可跨线程”的成员外，其余成员都应在界面线程调用。
/// </para>
/// </summary>
internal sealed class SerialPortSession : IDisposable
{
    private readonly object _sync = new();
    private readonly StringBuilder _pendingText = new();

    private SerialPort? _port;
    private long _pendingByteCount;
    private string? _faultMessage;
    private bool _disposed;

    /// <summary>端口当前是否处于打开状态。</summary>
    public bool IsOpen => _port is { IsOpen: true };

    /// <summary>已打开的端口名，未打开时为 <c>null</c>。</summary>
    public string? PortName { get; private set; }

    /// <summary>当前波特率，未打开时为 0。</summary>
    public int BaudRate { get; private set; }

    /// <summary>
    /// 打开端口。端口被占用、设备已拔出等情况会把 <see cref="SerialPort"/> 抛出的异常原样交给调用方，
    /// 由界面决定如何提示；失败时本对象保持未打开状态。
    /// </summary>
    public void Open(string portName, int baudRate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsOpen)
        {
            throw new InvalidOperationException("串口已打开，请先关闭后再尝试。");
        }

        var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            Encoding = Encoding.UTF8, // 按文本接收；二进制协议由界面侧转成十六进制显示
            ReadTimeout = 500,
            WriteTimeout = 1000
        };

        port.DataReceived += OnDataReceived;
        try
        {
            port.Open();
        }
        catch
        {
            // 打开失败时不能留下事件订阅和句柄
            port.DataReceived -= OnDataReceived;
            port.Dispose();
            throw;
        }

        _port = port;
        PortName = portName;
        BaudRate = baudRate;
    }

    /// <summary>关闭并释放端口。未打开时直接返回，可重复调用。</summary>
    public void Close()
    {
        var port = _port;
        _port = null;
        PortName = null;
        BaudRate = 0;
        if (port is null)
        {
            return;
        }

        // 先退订再关闭，避免关闭过程中还有回调访问已释放的实例
        port.DataReceived -= OnDataReceived;
        try
        {
            if (port.IsOpen)
            {
                port.Close();
            }
        }
        finally
        {
            port.Dispose();
        }
    }

    /// <summary>按 UTF-8 发送文本。</summary>
    public void WriteText(string text) => RequireOpenPort().Write(text);

    /// <summary>按字节原样发送，用于十六进制报文和 Modbus 帧。</summary>
    public void WriteBytes(byte[] bytes) => RequireOpenPort().Write(bytes, 0, bytes.Length);

    /// <summary>
    /// 取走上次调用之后累积的接收内容。返回 <c>false</c> 表示期间没有收到数据。
    /// 可跨线程调用（内部加锁）。
    /// </summary>
    public bool TryTakePendingText(out string text, out long byteCount)
    {
        lock (_sync)
        {
            if (_pendingText.Length == 0)
            {
                text = string.Empty;
                byteCount = 0;
                return false;
            }

            text = _pendingText.ToString();
            byteCount = _pendingByteCount;
            _pendingText.Clear();
            _pendingByteCount = 0;
            return true;
        }
    }

    /// <summary>取走最近一次通讯故障的描述；没有故障时返回 <c>false</c>。可跨线程调用。</summary>
    public bool TryTakeFault(out string message)
    {
        lock (_sync)
        {
            if (_faultMessage is null)
            {
                message = string.Empty;
                return false;
            }

            message = _faultMessage;
            _faultMessage = null;
            return true;
        }
    }

    /// <summary>丢弃尚未取走的接收内容，例如界面执行“清空接收”时。可跨线程调用。</summary>
    public void DiscardPendingData()
    {
        lock (_sync)
        {
            _pendingText.Clear();
            _pendingByteCount = 0;
            _faultMessage = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Close();
        _disposed = true;
    }

    // 在线程池线程上执行：只读取数据并放进缓冲区，不碰界面
    private void OnDataReceived(object? sender, SerialDataReceivedEventArgs e)
    {
        if (sender is not SerialPort port)
        {
            return;
        }

        try
        {
            var chunk = port.ReadExisting();
            if (chunk.Length == 0)
            {
                return;
            }

            // Encoding.GetByteCount 反映线上实际字节数，比字符数更接近设备真实发送量
            lock (_sync)
            {
                _pendingText.Append(chunk);
                _pendingByteCount += port.Encoding.GetByteCount(chunk);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
                                              or UnauthorizedAccessException or TimeoutException)
        {
            // 设备被拔出时读操作会失败；记录下来交给界面提示，不把异常抛到线程池
            lock (_sync)
            {
                _faultMessage = exception.Message;
            }
        }
    }

    private SerialPort RequireOpenPort()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _port is { IsOpen: true } port
            ? port
            : throw new InvalidOperationException("串口未打开。");
    }
}
