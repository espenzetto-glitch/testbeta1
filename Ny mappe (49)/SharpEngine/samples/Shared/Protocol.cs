namespace Demo;

/// <summary>Message ids shared by the demo client and server.</summary>
public static class Protocol
{
    public const ushort Position = 1;    // client -> server: float x, float y
    public const ushort Welcome = 2;     // server -> client: int playerId
    public const ushort PlayerState = 3; // server -> client: int playerId, float x, float y
    public const ushort PlayerLeft = 4;  // server -> client: int playerId
}
