using System.Text;

namespace CSharpProject;

/// <summary>
/// 十六进制文本与字节数组之间的转换，供串口发送区和日志显示使用。
/// 本类只负责语法转换，不负责校验 Modbus 帧结构或 CRC；协议校验由调用方处理。
/// </summary>
internal static class HexText
{
    // 查表取字符，避免格式化每个字节时创建额外的格式化中间字符串
    private const string HexDigits = "0123456789ABCDEF";

    /// <summary>
    /// 解析 <c>01 03 00 00 00 0A</c>、<c>0x01,0x03</c> 这类写法。
    /// 空格、逗号、短横线、分号、冒号都视为分隔符，<c>0x</c> 标记会被移除。
    /// 出现非十六进制字符、字节数为奇数或内容为空时返回 <c>false</c>。
    /// </summary>
    /// <param name="text">待解析的十六进制文本；调用方应传入非 <see langword="null"/> 字符串。</param>
    /// <param name="bytes">成功时为解析得到的字节；失败时为空数组。</param>
    /// <returns>文本由完整的十六进制字节组成时返回 <see langword="true"/>。</returns>
    public static bool TryParse(string text, out byte[] bytes)
    {
        bytes = [];
        var digits = new StringBuilder(text.Length);

        // 允许常见的 0x 前缀；先移除前缀，再统一检查剩余字符，避免分隔符规则分散在解析循环里
        var normalized = text.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character) || character is ',' or '-' or ';' or ':')
            {
                continue;
            }

            if (!Uri.IsHexDigit(character))
            {
                return false;
            }

            digits.Append(character);
        }

        // 每两个十六进制数字组成一个字节；空内容和半个字节都不是有效报文
        if (digits.Length == 0 || digits.Length % 2 != 0)
        {
            return false;
        }

        var result = new byte[digits.Length / 2];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = Convert.ToByte(digits.ToString(index * 2, 2), 16);
        }

        bytes = result;
        return true;
    }

    /// <summary>
    /// 格式化为大写、以空格分隔的形式，便于和报文手册逐字节对照。
    /// 例如 <c>[0x01, 0x0A]</c> 会格式化为 <c>"01 0A"</c>。
    /// </summary>
    /// <param name="bytes">要格式化的字节序列；空序列返回空字符串。</param>
    /// <returns>每字节两位大写十六进制、字节之间以空格分隔的字符串。</returns>
    public static string Format(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(bytes.Length * 3 - 1);
        for (var index = 0; index < bytes.Length; index++)
        {
            // 只在字节之间插入空格，不在首尾添加分隔符
            if (index > 0)
            {
                builder.Append(' ');
            }

            var value = bytes[index];
            builder.Append(HexDigits[value >> 4]).Append(HexDigits[value & 0x0F]);
        }

        return builder.ToString();
    }
}
