using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevTools.App.Libraries.Modbus;

internal interface IModbusRtuClient
{
    Task<ModbusReadResult> ReadAsync(ModbusReadRequest request, CancellationToken cancellationToken);

    Task WriteSingleCoilAsync(byte slaveId, ushort address, bool value, CancellationToken cancellationToken);

    Task WriteSingleRegisterAsync(byte slaveId, ushort address, ushort value, CancellationToken cancellationToken);

    Task WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, IReadOnlyList<ushort> values, CancellationToken cancellationToken);
}
