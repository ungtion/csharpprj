using System.IO; // 提供 IOException，用于报告串口读写错误。
using System.IO.Ports; // 提供 SerialPort 及串口读取、写入所需的类型。
using System.Text; // 提供 Encoding，用于串口文本数据的编码和解码。

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
    // 接收回调运行在后台线程；界面取数据、清缓冲区也可能同时执行，因此共享状态由同一把锁保护
    private readonly object _sync = new();
    private readonly StringBuilder _pendingText = new();

    // 串口对象的开关与写操作由 UI 线程顺序调用；后台回调只读取并写入上面的接收缓冲区
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
    /// <param name="portName">系统枚举出的串口名，例如 <c>COM3</c>。</param>
    /// <param name="baudRate">波特率，必须是串口驱动支持的正数。</param>
    /// <exception cref="ObjectDisposedException">会话已释放。</exception>
    /// <exception cref="InvalidOperationException">端口已经打开。</exception>
    /// <exception cref="ArgumentException">端口名或参数无效。</exception>
    /// <exception cref="IOException">端口无法打开，例如设备不存在。</exception>
    /// <exception cref="UnauthorizedAccessException">端口正被其他程序占用或无权访问。</exception>
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
            // Text 接收依赖此编码；Modbus 等二进制报文应由串口字节流解析，不应按 UTF-8 文本解码
            Encoding = Encoding.UTF8,
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

    /// <summary>
    /// 关闭并释放端口。未打开时直接返回，可重复调用。
    /// 此方法与 <see cref="Open"/>、发送方法应由同一前台线程串行调用。
    /// </summary>
    /// <exception cref="IOException">底层端口关闭失败；资源仍会在 <see langword="finally"/> 中释放。</exception>
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

    /// <summary>按当前串口编码（本类配置为 UTF-8）发送文本。</summary>
    /// <param name="text">要发送的文本。</param>
    /// <exception cref="InvalidOperationException">端口未打开或已释放。</exception>
    /// <exception cref="IOException">底层写入失败。</exception>
    public void WriteText(string text) => RequireOpenPort().Write(text);

    /// <summary>按字节原样发送，不执行文本编码；用于十六进制报文和 Modbus RTU 帧。</summary>
    /// <param name="bytes">要发送的完整字节序列。</param>
    /// <exception cref="InvalidOperationException">端口未打开或已释放。</exception>
    /// <exception cref="IOException">底层写入失败。</exception>
    public void WriteBytes(byte[] bytes) => RequireOpenPort().Write(bytes, 0, bytes.Length);

    /// <summary>
    /// 原子地取走上次调用之后累积的接收内容并清空缓冲区。
    /// 返回 <see langword="false"/> 表示期间没有收到可显示的内容，此时输出文本为空、字节数为 0。
    /// 此方法可从 UI 线程调用，不会阻塞等待串口数据（内部仅短暂加锁）。
    /// </summary>
    /// <param name="text">取出的文本内容。</param>
    /// <param name="byteCount">按当前 UTF-8 解码后文本反算的字节数；对有效 UTF-8 数据不等同于字符数。</param>
    /// <returns>有待取出的接收内容时为 <see langword="true"/>。</returns>
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

    /// <summary>
    /// 取走最近一次通讯故障的描述；没有故障时返回 <see langword="false"/>。
    /// 多次故障在界面读取之前发生时，仅保留最后一条错误消息。此方法可跨线程调用。
    /// </summary>
    /// <param name="message">故障说明；没有故障时为空字符串。</param>
    /// <returns>存在尚未读取的故障消息时为 <see langword="true"/>。</returns>
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

    /// <summary>
    /// 丢弃尚未取走的接收内容和故障消息，例如界面执行“清空接收”时。
    /// 不会关闭串口，也不会清除界面已经显示的文本。此方法可跨线程调用。
    /// </summary>
    public void DiscardPendingData()
    {
        lock (_sync)
        {
            _pendingText.Clear();
            _pendingByteCount = 0;
            _faultMessage = null;
        }
    }

    /// <summary>关闭并释放串口资源；重复释放不会产生副作用。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Close();
        _disposed = true;
    }

    // SerialPort 在线程池线程触发此回调：只读取数据并放进缓冲区，不碰 WPF 控件
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

            // 当前接收区是文本视图：先解码后缓存；计数按解码结果反算，适用于有效 UTF-8 文本流。
            // 二进制报文或不符合 UTF-8 的数据应改用字节缓冲区，不能依赖这里的文本字节计数。
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

    /// <summary>获取可写的已打开端口；未打开时显式报错，避免把发送失败伪装成成功。</summary>
    /// <exception cref="ObjectDisposedException">会话已释放。</exception>
    /// <exception cref="InvalidOperationException">端口未打开或已被设备断开。</exception>
    private SerialPort RequireOpenPort()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _port is { IsOpen: true } port
            ? port
            : throw new InvalidOperationException("串口未打开。");
    }
}
