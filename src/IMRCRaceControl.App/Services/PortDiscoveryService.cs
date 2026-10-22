using System.IO.Ports;

namespace IMRCRaceControl.Services;

public static class PortDiscoveryService
{
    public static IReadOnlyList<string> GetPorts() =>
        SerialPort.GetPortNames()
            .OrderBy(PortSortKey)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static int PortSortKey(string port)
    {
        if (port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(port[3..], out var number))
            return number;

        return int.MaxValue;
    }
}
