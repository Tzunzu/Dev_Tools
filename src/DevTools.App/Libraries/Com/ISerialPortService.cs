using System.Threading;
using System.Threading.Tasks;

namespace DevTools.App.Libraries.Com;

internal interface ISerialPortService
{
    bool IsOpen { get; }

    SerialPortSettings CurrentSettings { get; }

    void Configure(SerialPortSettings settings);

    void Open();

    void Close();

    void DiscardInBuffer();

    Task WriteAsync(byte[] frame, CancellationToken cancellationToken);

    Task<byte[]> ReadAsync(int bytesToRead, int timeoutMs, CancellationToken cancellationToken);
}
