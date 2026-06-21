using System.Net;
using System.Net.Sockets;

namespace BookTrak.Hosting;

internal static class PortFinder
{
    /// <summary>
    /// Starting at <paramref name="preferredPort"/>, probes upward for the first free TCP port
    /// on loopback.
    /// </summary>
    public static int FindFreePort(int preferredPort)
    {
        for (var port = preferredPort; port < preferredPort + 1000; port++)
        {
            if (IsFree(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException(
            $"Could not find a free port in range {preferredPort}-{preferredPort + 999}.");
    }

    private static bool IsFree(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
