using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Photobooth.Delivery;

/// <summary>
/// Hands the guest a link to files that are already on this laptop's disk.
///
/// There is no upload, so there is nothing to wait for and nothing to retry --
/// publishing is just deciding what URL to print on the QR.
/// </summary>
public sealed class LocalPublisher(IOptions<DeliveryOptions> options) : ISessionPublisher
{
    private readonly DeliveryOptions _options = options.Value;

    public DeliveryLink Publish(SessionRecord record) => new(
        $"{BaseUrl()}/s/{record.Token}",
        $"/api/sessions/{record.FolderName}/qr.png");

    /// <summary>
    /// The origin to print on the QR: the operator's override if there is one,
    /// otherwise the address of whichever network this laptop is actually on.
    /// </summary>
    public string BaseUrl()
    {
        var configured = _options.BaseUrl.Trim().TrimEnd('/');
        return configured.Length > 0 ? configured : Detected();
    }

    /// <summary>
    /// What the address would be with no override. Shown in Setup beside the
    /// effective address, because it is the one thing an operator cannot look up
    /// from inside the app and exactly what they need when the QR turns out to
    /// point somewhere no phone can reach.
    /// </summary>
    public string Detected() => $"http://{LocalAddress()}:{_options.Port}";

    /// <summary>
    /// This machine's address on the booth network.
    ///
    /// Picks the adapter with a gateway rather than the first one found: a laptop
    /// at an event routinely has a virtual switch, a VPN adapter and a disconnected
    /// ethernet port, and any of them will happily hand back an address that no
    /// guest's phone can reach. Having a gateway is the cheapest available proxy
    /// for "this is the network other devices are on".
    ///
    /// Returns loopback when nothing qualifies, which produces an obviously broken
    /// QR rather than a plausible one that silently fails -- the operator needs to
    /// find this out in setup, not from a guest.
    /// </summary>
    public static string LocalAddress()
    {
        var candidates =
            from nic in NetworkInterface.GetAllNetworkInterfaces()
            where nic.OperationalStatus == OperationalStatus.Up
               && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
            let props = nic.GetIPProperties()
            where props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork
                && !g.Address.Equals(System.Net.IPAddress.Any))
            from addr in props.UnicastAddresses
            where addr.Address.AddressFamily == AddressFamily.InterNetwork
            select addr.Address.ToString();

        return candidates.FirstOrDefault() ?? "127.0.0.1";
    }
}
