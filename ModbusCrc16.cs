namespace CSharpProject;

/// <summary>
/// Modbus RTU 使用的 CRC-16 校验（CRC-16/MODBUS）。
/// 参数为初值 <c>0xFFFF</c>、反向多项式 <c>0xA001</c>，结果不做最终异或。
/// 逐位计算即可，算法本身不依赖帧长或外部状态；校验字段在 RTU 帧尾按低字节优先发送。
/// </summary>
public static class ModbusCrc16
{
    // CRC-16/MODBUS 的反向多项式（LSB-first 逐位计算时使用）
    private const ushort Polynomial = 0xA001;

    // 每个独立帧都从全 1 状态开始，Compute 不会保留上一帧的计算状态
    private const ushort InitialValue = 0xFFFF;

    /// <summary>
    /// 计算指定报文数据的 CRC16/MODBUS。
    /// 已知校验向量：ASCII 字符串 <c>"123456789"</c> 的结果为 <c>0x4B37</c>。
    /// </summary>
    /// <param name="data">参与校验的报文字节，不包含尚未生成的 CRC 字节。</param>
    /// <returns>16 位 CRC 数值；将其写入 Modbus RTU 帧时低字节先发。</returns>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        var crc = InitialValue;
        foreach (var value in data)
        {
            // 先把当前输入字节并入低 8 位，再按最低位方向逐位除以生成多项式
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
    /// 返回“原始帧 + CRC”的新数组。Modbus RTU 规定 CRC 低字节先发，所以数组中低字节在前、高字节在后。
    /// 输入帧不会被修改；空帧也会得到包含初始 CRC（<c>FF FF</c>）的两字节结果。
    /// </summary>
    /// <param name="frame">不含 CRC 的 RTU 帧字节。</param>
    /// <returns>新分配的报文数组，最后两个字节为计算出的 CRC。</returns>
    public static byte[] AppendCrc(ReadOnlySpan<byte> frame)
    {
        // 每次都返回新数组，避免调用方原始报文被就地修改后再次发送时重复附加 CRC
        var withCrc = new byte[frame.Length + 2];
        frame.CopyTo(withCrc);

        var crc = Compute(frame);
        withCrc[^2] = (byte)(crc & 0xFF);
        withCrc[^1] = (byte)(crc >> 8);
        return withCrc;
    }

    /// <summary>
    /// 把 CRC 格式化为 RTU 帧尾顺序的两个十六进制字节（低字节在前），用于日志显示。
    /// 例如数值 <c>0x4B37</c> 返回 <c>"37 4B"</c>。
    /// </summary>
    /// <param name="crc">由 <see cref="Compute"/> 返回的 CRC 数值。</param>
    /// <returns>两位大写十六进制字节，中间以空格分隔。</returns>
    public static string FormatCrcBytes(ushort crc) => $"{crc & 0xFF:X2} {crc >> 8:X2}";
}
