namespace DevTools.App.Libraries.Modbus;

internal static class ModbusCrc16
{
    public static ushort Compute(byte[] data, int length)
    {
        ushort crc = 0xFFFF;

        for (var i = 0; i < length; i++)
        {
            crc ^= data[i];
            for (var bit = 0; bit < 8; bit++)
            {
                var lsbSet = (crc & 0x0001) != 0;
                crc >>= 1;
                if (lsbSet)
                {
                    crc ^= 0xA001;
                }
            }
        }

        return crc;
    }
}
