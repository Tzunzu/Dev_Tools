using System.Globalization;

namespace DevTools.Wpf.Infrastructure.Modbus;

public enum RegisterNumberFormat
{
    Decimal,
    Hex
}

public enum RegisterValueDataType
{
    UInt,
    Int,
    Hex,
    Bits,
    String,
    DInt,
    UDInt,
    Float
}

internal static class RegisterDisplayFormatter
{
    public static string FormatAddress(int address, RegisterNumberFormat format)
    {
        return format == RegisterNumberFormat.Hex
            ? $"0x{address:X4}"
            : address.ToString(CultureInfo.InvariantCulture);
    }

    public static bool TryParseAddress(string? text, out int address)
    {
        address = -1;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
        }

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out address);
    }

    public static string FormatValue(IReadOnlyList<ushort> values, int rowIndex, RegisterValueDataType dataType)
    {
        if (rowIndex < 0 || rowIndex >= values.Count)
        {
            return "-";
        }

        var current = values[rowIndex];
        return dataType switch
        {
            RegisterValueDataType.Hex => $"0x{current:X4}",
            RegisterValueDataType.Bits => Convert.ToString(current, 2).PadLeft(16, '0'),
            RegisterValueDataType.Int => unchecked((short)current).ToString(CultureInfo.InvariantCulture),
            RegisterValueDataType.String => FormatStringValue(current),
            RegisterValueDataType.DInt => FormatDIntValue(values, rowIndex),
            RegisterValueDataType.UDInt => FormatUDIntValue(values, rowIndex),
            RegisterValueDataType.Float => FormatFloatValue(values, rowIndex),
            _ => current.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string FormatStringValue(ushort value)
    {
        var high = (char)((value >> 8) & 0xFF);
        var low = (char)(value & 0xFF);
        return string.Concat(ToPrintable(high), ToPrintable(low));
    }

    private static char ToPrintable(char value)
    {
        return char.IsControl(value) ? '.' : value;
    }

    private static string FormatDIntValue(IReadOnlyList<ushort> values, int rowIndex)
    {
        if (!TryGetWordPair(values, rowIndex, out var combined, out var isLeadWord))
        {
            return "n/a";
        }

        if (!isLeadWord)
        {
            return string.Empty;
        }

        return unchecked((int)combined).ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatUDIntValue(IReadOnlyList<ushort> values, int rowIndex)
    {
        if (!TryGetWordPair(values, rowIndex, out var combined, out var isLeadWord))
        {
            return "n/a";
        }

        if (!isLeadWord)
        {
            return string.Empty;
        }

        return combined.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatFloatValue(IReadOnlyList<ushort> values, int rowIndex)
    {
        if (!TryGetWordPair(values, rowIndex, out var combined, out var isLeadWord))
        {
            return "n/a";
        }

        if (!isLeadWord)
        {
            return string.Empty;
        }

        var floatValue = BitConverter.Int32BitsToSingle(unchecked((int)combined));
        return floatValue.ToString("G6", CultureInfo.InvariantCulture);
    }

    private static bool TryGetWordPair(IReadOnlyList<ushort> values, int rowIndex, out uint combined, out bool isLeadWord)
    {
        combined = 0;
        isLeadWord = rowIndex % 2 == 0;

        var baseIndex = isLeadWord ? rowIndex : rowIndex - 1;
        if (baseIndex < 0 || baseIndex + 1 >= values.Count)
        {
            return false;
        }

        combined = ((uint)values[baseIndex] << 16) | values[baseIndex + 1];
        return true;
    }
}
