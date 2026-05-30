namespace DevTools.Wpf.Infrastructure.Modbus;

public static class ModbusStatusText
{
    public static string DescribePollFailure(Exception ex)
    {
        var message = ex.Message;

        if (message.Contains("not connected", StringComparison.OrdinalIgnoreCase) || message.Contains("not open", StringComparison.OrdinalIgnoreCase))
        {
            return "Not connected";
        }

        if (message.Contains("(ILLEGAL_FUNCTION)", StringComparison.OrdinalIgnoreCase))
        {
            return "Illegal function";
        }

        if (message.Contains("(ILLEGAL_DATA_ADDRESS)", StringComparison.OrdinalIgnoreCase))
        {
            return "Illegal data address";
        }

        if (message.Contains("(ILLEGAL_DATA_VALUE)", StringComparison.OrdinalIgnoreCase))
        {
            return "Illegal data value";
        }

        if (message.Contains("(SERVER_DEVICE_FAILURE)", StringComparison.OrdinalIgnoreCase))
        {
            return "Server device failure";
        }

        if (message.Contains("(ACKNOWLEDGE)", StringComparison.OrdinalIgnoreCase))
        {
            return "Acknowledge";
        }

        if (message.Contains("(SERVER_DEVICE_BUSY)", StringComparison.OrdinalIgnoreCase))
        {
            return "Server busy";
        }

        if (message.Contains("(MEMORY_PARITY_ERROR)", StringComparison.OrdinalIgnoreCase))
        {
            return "Memory parity error";
        }

        if (message.Contains("(GATEWAY_PATH_UNAVAILABLE)", StringComparison.OrdinalIgnoreCase))
        {
            return "Gateway path unavailable";
        }

        if (message.Contains("(GATEWAY_TARGET_FAILED_TO_RESPOND)", StringComparison.OrdinalIgnoreCase))
        {
            return "Gateway target failed";
        }

        if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "Timeout";
        }

        if (message.Contains("crc", StringComparison.OrdinalIgnoreCase))
        {
            return "CRC error";
        }

        return "Error";
    }

    public static string DescribeExceptionCode(byte exceptionCode)
    {
        return exceptionCode switch
        {
            0x01 => "ILLEGAL_FUNCTION",
            0x02 => "ILLEGAL_DATA_ADDRESS",
            0x03 => "ILLEGAL_DATA_VALUE",
            0x04 => "SERVER_DEVICE_FAILURE",
            0x05 => "ACKNOWLEDGE",
            0x06 => "SERVER_DEVICE_BUSY",
            0x08 => "MEMORY_PARITY_ERROR",
            0x0A => "GATEWAY_PATH_UNAVAILABLE",
            0x0B => "GATEWAY_TARGET_FAILED_TO_RESPOND",
            _ => "UNKNOWN_EXCEPTION"
        };
    }
}