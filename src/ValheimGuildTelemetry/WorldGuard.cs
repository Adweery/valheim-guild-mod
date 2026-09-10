namespace ValheimGuildTelemetry;

internal static class WorldGuard
{
    internal static bool Matches(ZNet network, string expectedUid)
    {
        // ZNet exists during the handshake, before its world has arrived.
        // GetWorldName handles a null world; GetWorldUID dereferences it.
        return network != null && network.GetWorldName() != null &&
               network.GetWorldUID().ToString() == expectedUid;
    }
}
