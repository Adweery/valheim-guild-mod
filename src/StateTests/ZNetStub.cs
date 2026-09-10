using System;

// Reproduce the real game's null-world handshake behavior, without game binaries.
public class ZNet
{
    public string Name;
    public long Uid;
    public string GetWorldName() => Name;
    public long GetWorldUID() => Name == null ? throw new NullReferenceException() : Uid;
}
