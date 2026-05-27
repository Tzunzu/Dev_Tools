using DevTools.App.Libraries.Modbus;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevTools.App.Controllers.Modbus;

internal sealed class ModbusRtuReadController
{
    private readonly IModbusRtuClient modbusRtuClient;

    public ModbusRtuReadController(IModbusRtuClient modbusRtuClient)
    {
        this.modbusRtuClient = modbusRtuClient;
    }

    public async Task<IReadOnlyList<RegisterPoint>> ReadAsync(SlaveReadProfile profile, CancellationToken cancellationToken)
    {
        var request = new ModbusReadRequest
        {
            SlaveId = profile.SlaveId,
            StartAddress = profile.StartRegister,
            RegisterCount = profile.RegisterCount,
            FunctionCode = profile.FunctionCode
        };

        var result = await modbusRtuClient.ReadAsync(request, cancellationToken);
        var points = new List<RegisterPoint>(result.Values.Length);

        for (var index = 0; index < result.Values.Length; index++)
        {
            points.Add(new RegisterPoint
            {
                RowIndex = index,
                RegisterNumber = (ushort)(profile.StartRegister + index),
                Value = result.Values[index]
            });
        }

        return points;
    }
    public Task WriteCoilAsync(byte slaveId, ushort address, bool value, CancellationToken cancellationToken)
    {
        return modbusRtuClient.WriteSingleCoilAsync(slaveId, address, value, cancellationToken);
    }

    public Task WriteHoldingRegistersAsync(byte slaveId, ushort startAddress, IReadOnlyList<ushort> values, CancellationToken cancellationToken)
    {
        return values.Count switch
        {
            0 => Task.CompletedTask,
            1 => modbusRtuClient.WriteSingleRegisterAsync(slaveId, startAddress, values[0], cancellationToken),
            _ => modbusRtuClient.WriteMultipleRegistersAsync(slaveId, startAddress, values, cancellationToken)
        };
    }
}
