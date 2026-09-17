namespace Photobooth.Delivery;

/// <summary>Where a guest goes to collect a finished session.</summary>
/// <param name="Url">
/// What the QR encodes: an absolute URL a phone can reach, built from the
/// configured base URL rather than stored, because the booth's address changes
/// with the network it is plugged into.
/// </param>
/// <param name="QrUrl">
/// Where the booth's own screens fetch the QR image. Relative, because only the
/// iPad and the operator load it and they already know the origin.
/// </param>
public sealed record DeliveryLink(string Url, string QrUrl);

/// <summary>
/// Makes a finished session reachable by a guest.
///
/// One synchronous method that cannot fail, which is the whole difference from
/// the Drive publisher this replaces. That one needed an authorisation flag, a
/// "the link is ready now" callback and a retry queue because an upload is slow,
/// happens after the guest has walked away, and fails for four different reasons.
/// Local delivery has none of those properties: <see cref="SessionArchive.Save"/>
/// has already written the files and minted the token, so the guest's URL is
/// known the instant it returns.
///
/// It stays an interface so cloud delivery can come back later without the
/// call site changing shape -- though a cloud implementation will want its own
/// queue, and that is a real change whenever it happens.
/// </summary>
public interface ISessionPublisher
{
    DeliveryLink Publish(SessionRecord record);
}

public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    /// <summary>
    /// The origin guests' phones use, e.g. <c>http://192.168.8.2:8080</c>.
    ///
    /// Empty means "work it out from the active network adapter", which is the
    /// normal case; the override exists because the booth runs behind a travel
    /// router at one event, a laptop hotspot at the next, and venue wifi at the
    /// third, and only the operator knows which.
    ///
    /// Deliberately not the same origin the iPad uses. The iPad needs a real
    /// certificate and therefore a real hostname; a phone reaching a raw LAN IP
    /// cannot be given one, so it gets plain HTTP instead of a warning.
    /// </summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// The port guests' phones connect to, used when building the fallback
    /// address. Kept in step with the server's actual HTTP listener at startup,
    /// because a detected address on the wrong port is exactly as dead as a
    /// detected address on the wrong network.
    /// </summary>
    public int Port { get; set; } = 8080;
}
