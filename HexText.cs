using System.Text;

namespace CSharpProject;

/// <summary>十六进制文本与字节数组之间的转换，供串口发送区和日志显示使用。</summary>
internal static class HexText
{
    private const string HexDigits = "0123456789ABCDEF";

    /// <summary>
    /// 解析 <c>01 03 00 00 00 0A</c>、<c>0x01,0x03</c> 这类写法。
    /// 空格、逗号、短横线、分号、冒号都视为分隔符，<c>0x</c> 前缀会被忽略。
    /// 出现非十六进制字符、字节数为奇数或内容为空时返回 <c>false</c>。
    /// </summary>
    public static bool TryParse(string text, out byte[] bytes)
    {
        bytes = [];
        var digits = new StringBuilder(text.Length);
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

    /// <summary>格式化为大写、以空格分隔的形式，便于和报文手册逐字节对照。</summary>
    public static string Format(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(bytes.Length * 3 - 1);
        for (var index = 0; index < bytes.Length; index++)
        {
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
