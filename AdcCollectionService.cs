using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace CSharpProject;

internal sealed class AdcCollectionService
{
    private const int MaxLineBufferLength = 20000;
    private const string SensorVoltageMarker = "Sensor Voltage:";
    private static readonly Regex AnsiEscapeRegex = new(@"\x1B\[[0-9;]*m");

    private readonly StringBuilder _lineBuffer = new();
    private string _fixedRangeText = "";
    private int _count;
    private bool _autoSaved;
    private int? _lastAdc;

    public string SavePath { get; set; } = "adc_record.txt";
    public string SerialNumber { get; private set; } = "";
    public int? CurrentValue { get; private set; }

    public void BindSerialNumber(string serialNumber)
    {
        SerialNumber = serialNumber;
        Reset();
    }

    public void ClearSerialNumber() => SerialNumber = "";

    public void Reset()
    {
        CurrentValue = null;
        _fixedRangeText = "";
        _count = 0;
        _autoSaved = false;
        _lastAdc = null;
    }

    public IReadOnlyList<AdcReceivedLine> ProcessReceivedText(string text)
    {
        _lineBuffer.Append(text);
        if (_lineBuffer.Length > MaxLineBufferLength)
        {
            _lineBuffer.Remove(0, _lineBuffer.Length - MaxLineBufferLength);
        }

        var buffered = _lineBuffer.ToString();
        var lastNewline = buffered.LastIndexOf('\n');
        if (lastNewline < 0)
        {
            return Array.Empty<AdcReceivedLine>();
        }

        _lineBuffer.Clear();
        _lineBuffer.Append(buffered[(lastNewline + 1)..]);

        return buffered[..lastNewline]
            .Split('\n')
            .Select(line => ParseLine(line.TrimEnd('\r')))
            .ToArray();
    }

    public AdcValueUpdate ProcessValue(int value)
    {
        CurrentValue = value;
        string? rangeLogError = null;
        var isWithinAcceptedRange = IsWithinAcceptedRange(_count, value);
        string displayText;
        var autoSaveAttempted = false;
        string? autoSaveError = null;

        if (_count == 0)
        {
            if (_fixedRangeText.Length == 0)
            {
                _fixedRangeText = GetRangeText(value, lockRange: false, out rangeLogError);
            }

            displayText = $"实时 {_fixedRangeText} ADC:{value}";

            if (IsTargetRange(value))
            {
                _fixedRangeText = GetRangeText(value, lockRange: true, out var error);
                rangeLogError ??= error;
                isWithinAcceptedRange = IsWithinAcceptedRange(_count, value);
                displayText = _count switch
                {
                    2 => $"区间1(1460-1500) ADC:{value}",
                    3 => $"区间2(1530-1570) ADC:{value}",
                    4 => $"区间3(1580-1620) ADC:{value}",
                    5 => $"区间4(1680-1720) ADC:{value}",
                    _ => $"else0 ADC:{value}"
                };

                if (!_autoSaved)
                {
                    autoSaveAttempted = true;
                    autoSaveError = TryWriteRecord(value, "自动采集");
                    _autoSaved = true;
                }
            }
        }
        else
        {
            displayText = _count switch
            {
                2 => $"elif1区间(1460-1500) ADC:{value}",
                3 => $"elif2区间(1530-1570) ADC:{value}",
                4 => $"elif3区间(1580-1620) ADC:{value}",
                5 => $"elif4区间(1680-1720) ADC:{value}",
                _ => GetFallbackDisplayText(value, out rangeLogError)
            };
        }

        return new AdcValueUpdate(displayText, isWithinAcceptedRange, autoSaveAttempted, autoSaveError, rangeLogError);
    }

    public string? TryWriteRecord(int value, string description)
    {
        var line =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}, SN:{SerialNumber}, {description}, ADC:{value}{Environment.NewLine}";
        return TryAppendLine(line);
    }

    private string GetFallbackDisplayText(int value, out string? rangeLogError)
    {
        GetRangeText(value, lockRange: false, out rangeLogError);
        return $"else0 ADC:{value}";
    }

    private string GetRangeText(int value, bool lockRange, out string? writeError)
    {
        var text = value switch
        {
            < 1500 => SetCount(1, "挑出！！！！！！"),
            >= 1500 and < 1700 => SetCount(2, "区间(1460-1500)"),
            > 1700 and < 2000 => SetCount(3, "区间(1530-1570)"),
            > 2000 and < 2400 => SetCount(4, "区间(1580-1620)"),
            > 2400 => SetCount(5, "区间(1680-1720)"),
            _ => "超出标准区间"
        };

        if (lockRange)
        {
            _fixedRangeText = text;
        }

        writeError = null;
        if (_lastAdc != value)
        {
            var logLine =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}, SN:{SerialNumber}, ADC:{value}, count:{_count}, 判定文本:{text}{Environment.NewLine}";
            writeError = TryAppendLine(logLine);
            _lastAdc = value;
        }

        return text;
    }

    private string SetCount(int count, string text)
    {
        _count = count;
        return text;
    }

    private static bool IsTargetRange(int value) => value >= 1700 && value < 3200;

    private static bool IsWithinAcceptedRange(int count, int value) =>
        count switch
        {
            2 => value >= 1460 && value < 1500,
            3 => value >= 1530 && value < 1570,
            4 => value >= 1580 && value < 1620,
            5 => value >= 1680 && value < 1720,
            _ => false
        };

    private static AdcReceivedLine ParseLine(string line)
    {
        line = AnsiEscapeRegex.Replace(line, "");
        var markerIndex = line.IndexOf(SensorVoltageMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return new AdcReceivedLine(line, null);
        }

        var digits = new StringBuilder();
        foreach (var character in line[(markerIndex + SensorVoltageMarker.Length)..].TrimStart())
        {
            if (!char.IsDigit(character))
            {
                break;
            }

            digits.Append(character);
        }

        return new AdcReceivedLine(
            line,
            digits.Length > 0 && int.TryParse(digits.ToString(), out var value) ? value : null);
    }

    private string? TryAppendLine(string line)
    {
        try
        {
            File.AppendAllText(SavePath, line, Encoding.UTF8);
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return exception.Message;
        }
    }
}

internal sealed record AdcReceivedLine(string Text, int? Value);

internal sealed record AdcValueUpdate(
    string DisplayText,
    bool IsWithinAcceptedRange,
    bool AutoSaveAttempted,
    string? AutoSaveError,
    string? RangeLogError);
