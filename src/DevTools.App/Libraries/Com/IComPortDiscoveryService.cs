using System.Collections.Generic;

namespace DevTools.App.Libraries.Com;

internal interface IComPortDiscoveryService
{
    IReadOnlyList<ComPortDeviceInfo> Discover();
}
