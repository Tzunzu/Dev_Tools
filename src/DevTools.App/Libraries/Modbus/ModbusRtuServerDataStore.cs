using System;
using System.Collections.Generic;

namespace DevTools.App.Libraries.Modbus;

internal enum ModbusDataArea
{
    Coils,
    DiscreteInputs,
    HoldingRegisters,
    InputRegisters
}

internal sealed class ModbusRtuServerDataStore
{
    private readonly object sync = new();
    private readonly Dictionary<ushort, bool> coils = new();
    private readonly Dictionary<ushort, bool> discreteInputs = new();
    private readonly Dictionary<ushort, ushort> holdingRegisters = new();
    private readonly Dictionary<ushort, ushort> inputRegisters = new();

    public void ReplaceBooleanArea(ModbusDataArea area, ushort startAddress, IReadOnlyList<bool> values)
    {
        if (area is not ModbusDataArea.Coils and not ModbusDataArea.DiscreteInputs)
        {
            throw new ArgumentOutOfRangeException(nameof(area), "Area must be Coils or DiscreteInputs.");
        }

        lock (sync)
        {
            ValidateAddressRange(startAddress, values.Count);
            var target = GetBooleanArea(area);
            target.Clear();

            for (var i = 0; i < values.Count; i++)
            {
                target[(ushort)(startAddress + i)] = values[i];
            }
        }
    }

    public void ReplaceRegisterArea(ModbusDataArea area, ushort startAddress, IReadOnlyList<ushort> values)
    {
        if (area is not ModbusDataArea.HoldingRegisters and not ModbusDataArea.InputRegisters)
        {
            throw new ArgumentOutOfRangeException(nameof(area), "Area must be HoldingRegisters or InputRegisters.");
        }

        lock (sync)
        {
            ValidateAddressRange(startAddress, values.Count);
            var target = GetRegisterArea(area);
            target.Clear();

            for (var i = 0; i < values.Count; i++)
            {
                target[(ushort)(startAddress + i)] = values[i];
            }
        }
    }

    public bool[] ReadBooleans(ModbusDataArea area, ushort startAddress, ushort count)
    {
        if (count == 0)
        {
            throw new ModbusServerException(0x03, "Quantity must be greater than zero.");
        }

        lock (sync)
        {
            ValidateAddressRange(startAddress, count);
            var source = GetBooleanArea(area);
            var result = new bool[count];
            for (var i = 0; i < count; i++)
            {
                var address = (ushort)(startAddress + i);
                if (!source.TryGetValue(address, out var value))
                {
                    throw new ModbusServerException(0x02, "Requested address is not mapped.");
                }

                result[i] = value;
            }

            return result;
        }
    }

    public ushort[] ReadRegisters(ModbusDataArea area, ushort startAddress, ushort count)
    {
        if (count == 0)
        {
            throw new ModbusServerException(0x03, "Quantity must be greater than zero.");
        }

        lock (sync)
        {
            ValidateAddressRange(startAddress, count);
            var source = GetRegisterArea(area);
            var result = new ushort[count];
            for (var i = 0; i < count; i++)
            {
                var address = (ushort)(startAddress + i);
                if (!source.TryGetValue(address, out var value))
                {
                    throw new ModbusServerException(0x02, "Requested address is not mapped.");
                }

                result[i] = value;
            }

            return result;
        }
    }

    public void WriteSingleCoil(ushort address, bool value)
    {
        lock (sync)
        {
            if (!coils.ContainsKey(address))
            {
                throw new ModbusServerException(0x02, "Requested coil address is not mapped.");
            }

            coils[address] = value;
        }
    }

    public void WriteSingleRegister(ushort address, ushort value)
    {
        lock (sync)
        {
            if (!holdingRegisters.ContainsKey(address))
            {
                throw new ModbusServerException(0x02, "Requested register address is not mapped.");
            }

            holdingRegisters[address] = value;
        }
    }

    public void WriteMultipleCoils(ushort startAddress, IReadOnlyList<bool> values)
    {
        lock (sync)
        {
            ValidateAddressRange(startAddress, values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                var address = (ushort)(startAddress + i);
                if (!coils.ContainsKey(address))
                {
                    throw new ModbusServerException(0x02, "Requested coil address is not mapped.");
                }
            }

            for (var i = 0; i < values.Count; i++)
            {
                coils[(ushort)(startAddress + i)] = values[i];
            }
        }
    }

    public void WriteMultipleRegisters(ushort startAddress, IReadOnlyList<ushort> values)
    {
        lock (sync)
        {
            ValidateAddressRange(startAddress, values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                var address = (ushort)(startAddress + i);
                if (!holdingRegisters.ContainsKey(address))
                {
                    throw new ModbusServerException(0x02, "Requested register address is not mapped.");
                }
            }

            for (var i = 0; i < values.Count; i++)
            {
                holdingRegisters[(ushort)(startAddress + i)] = values[i];
            }
        }
    }

    private Dictionary<ushort, bool> GetBooleanArea(ModbusDataArea area)
    {
        return area switch
        {
            ModbusDataArea.Coils => coils,
            ModbusDataArea.DiscreteInputs => discreteInputs,
            _ => throw new ArgumentOutOfRangeException(nameof(area), "Area must be Coils or DiscreteInputs.")
        };
    }

    private Dictionary<ushort, ushort> GetRegisterArea(ModbusDataArea area)
    {
        return area switch
        {
            ModbusDataArea.HoldingRegisters => holdingRegisters,
            ModbusDataArea.InputRegisters => inputRegisters,
            _ => throw new ArgumentOutOfRangeException(nameof(area), "Area must be HoldingRegisters or InputRegisters.")
        };
    }

    private static void ValidateAddressRange(ushort startAddress, int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return;
        }

        var endAddress = startAddress + count - 1;
        if (endAddress > ushort.MaxValue)
        {
            throw new ModbusServerException(0x02, "Requested address range is out of bounds.");
        }
    }
}

internal sealed class ModbusServerException : Exception
{
    public ModbusServerException(byte exceptionCode, string message)
        : base(message)
    {
        ExceptionCode = exceptionCode;
    }

    public byte ExceptionCode { get; }
}
