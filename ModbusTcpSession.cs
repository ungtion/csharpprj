using System.IO;
using System.Net.Sockets;

namespace CSharpProject;

internal sealed class ModbusTcpSession : IDisposable
{
    private const int TimeoutMilliseconds = 3000;

    private TcpClient? _client;
    private ushort _transactionId;

    public bool IsConnected { get; private set; }

    public async Task ConnectAsync(string host, int port)
    {
        Close();

        var client = new TcpClient();
        try
        {
            using var timeout = new CancellationTokenSource(TimeoutMilliseconds);
            await client.ConnectAsync(host, port, timeout.Token);
            _client = client;
            IsConnected = true;
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException
                                              or ArgumentException or InvalidOperationException)
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<ModbusTcpReadResult> ReadRegistersAsync(
        byte unitId,
        byte functionCode,
        ushort startAddress,
        ushort quantity)
    {
        if (_client is null || !IsConnected)
        {
            throw new InvalidOperationException("Modbus TCP 连接尚未建立。");
        }

        if (functionCode is not (3 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(functionCode), "只支持只读功能码 03 和 04。");
        }

        if (quantity is < 1 or > 125 || (uint)startAddress + quantity > 65536)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "寄存器数量或地址范围无效。");
        }

        var transactionId = unchecked(++_transactionId);
        var request = new byte[]
        {
            (byte)(transactionId >> 8),
            (byte)transactionId,
            0,
            0,
            0,
            6,
            unitId,
            functionCode,
            (byte)(startAddress >> 8),
            (byte)startAddress,
            (byte)(quantity >> 8),
            (byte)quantity
        };

        using var timeout = new CancellationTokenSource(TimeoutMilliseconds);
        var stream = _client.GetStream();
        await stream.WriteAsync(request, timeout.Token);

        var header = new byte[7];
        await stream.ReadExactlyAsync(header, timeout.Token);
        var responseTransactionId = (ushort)((header[0] << 8) | header[1]);
        var protocolId = (ushort)((header[2] << 8) | header[3]);
        var responseLength = (ushort)((header[4] << 8) | header[5]);
        if (responseTransactionId != transactionId || protocolId != 0
            || header[6] != unitId || responseLength is < 3 or > 254)
        {
            throw new InvalidDataException("Modbus TCP 响应头无效或与请求不匹配。");
        }

        var pdu = new byte[responseLength - 1];
        await stream.ReadExactlyAsync(pdu, timeout.Token);
        var response = new byte[header.Length + pdu.Length];
        header.CopyTo(response, 0);
        pdu.CopyTo(response, header.Length);

        if (pdu[0] == (byte)(functionCode | 0x80))
        {
            if (pdu.Length != 2)
            {
                throw new InvalidDataException("Modbus 异常响应格式无效。");
            }

            throw new ModbusTcpException(pdu[1]);
        }

        var expectedByteCount = quantity * 2;
        if (pdu[0] != functionCode || pdu.Length != expectedByteCount + 2
            || pdu[1] != expectedByteCount)
        {
            throw new InvalidDataException("Modbus 响应功能码或数据长度与请求不匹配。");
        }

        var registers = new ushort[quantity];
        for (var index = 0; index < registers.Length; index++)
        {
            registers[index] = (ushort)((pdu[2 + index * 2] << 8) | pdu[3 + index * 2]);
        }

        return new ModbusTcpReadResult(registers, request, response);
    }

    public void Close()
    {
        _client?.Dispose();
        _client = null;
        IsConnected = false;
    }

    public void Dispose() => Close();
}

internal sealed record ModbusTcpReadResult(ushort[] Registers, byte[] RequestFrame, byte[] ResponseFrame);

internal sealed class ModbusTcpException(byte exceptionCode)
    : Exception($"异常码 0x{exceptionCode:X2}")
{
    public byte ExceptionCode { get; } = exceptionCode;
}
