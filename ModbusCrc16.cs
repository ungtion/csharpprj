namespace CSharpProject;

/// <summary>
/// Modbus RTU 使用的 CRC-16 校验：初值 0xFFFF，反向多项式 0xA001，结果不做最终异或。
/// 逐位计算即可，一帧报文通常不超过 256 字节，无需建表。
/// </summary>
public static class ModbusCrc16
{
    private const ushort Polynomial = 0xA001;
    private const ushort InitialValue = 0xFFFF;

    /// <summary>
    /// 计算 CRC16/MODBUS。已知用例：ASCII 字符串 "123456789" 的结果为 0x4B37。
    /// </summary>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        var crc = InitialValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                var leastBitSet = (crc & 1) != 0;
                crc >>= 1;
                if (leastBitSet)
                {
                    crc ^= Polynomial;
                }
            }
        }

        return crc;
    }

    /// <summary>
    /// 返回“原始帧 + CRC”的新数组。Modbus RTU 规定 CRC 低字节先发，所以这里低字节在前、高字节在后。
    /// </summary>
    public static byte[] AppendCrc(ReadOnlySpan<byte> frame)
    {
        var withCrc = new byte[frame.Length + 2];
        frame.CopyTo(withCrc);

        var crc = Compute(frame);
        withCrc[^2] = (byte)(crc & 0xFF);
        withCrc[^1] = (byte)(crc >> 8);
        return withCrc;
    }

    /// <summary>把 CRC 拆成帧尾的两个字节（低字节在前），用于日志显示。</summary>
    public static string FormatCrcBytes(ushort crc) => $"{crc & 0xFF:X2} {crc >> 8:X2}";
}
