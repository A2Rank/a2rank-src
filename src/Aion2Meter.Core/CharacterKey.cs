using Aion2Meter.Protocol;

namespace Aion2Meter.Core;

/// <summary>
/// One of my characters: in-game server + name (the same name can exist on other servers).
/// Points, periods and kill lists are kept per character.
/// Server 0 means not known yet: the ranking message carries a different code than the in-game server
/// (BEHAVIOR.md §1.8), so a character first seen there waits for the enter-world message.
/// </summary>
public sealed record CharacterKey(int Server, string Name)
{
    /// <summary>Stored with every record, e.g. "1019:아라".</summary>
    public string Id => $"{Server}:{Name}";

    public string Label => Servers.IsValid(Server) ? $"{Name} [{Servers.Label(Server)}]" : Name;

    /// <summary>Same character, allowing for a server not known yet on either side.</summary>
    public bool Matches(CharacterKey? other) =>
        other is not null && Name == other.Name && (Server == other.Server || Server == 0 || other.Server == 0);

    public static CharacterKey? Parse(string? id)
    {
        int colon = id?.IndexOf(':') ?? -1;
        return colon > 0 && int.TryParse(id![..colon], out int server) && colon < id.Length - 1
            ? new CharacterKey(server, id[(colon + 1)..])
            : null;
    }
}
