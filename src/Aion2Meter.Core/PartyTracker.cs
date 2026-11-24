using Aion2Meter.Protocol;

namespace Aion2Meter.Core;

/// <summary>
/// Who is in my party: the party GUID, and its members by character id (me included once the game names me).
/// Evidence: docs/PROTOCOL.md §3.7 (one recording, a two-person party formed and left twice).
/// </summary>
public sealed class PartyTracker
{
    /// <summary>A member heard of only through status messages is dropped when none came for this long.</summary>
    public static readonly TimeSpan StatusSilence = TimeSpan.FromSeconds(60);
    private const ulong Me = 0;

    private readonly Dictionary<ulong, string> _members = [];
    private readonly Dictionary<ulong, DateTimeOffset> _heard = [];
    // Invited or announced by a join message (08 92) but not yet heard from: not counted until a status message of theirs arrives (a declined invitation sends none).
    private readonly Dictionary<ulong, string> _pending = [];
    private bool _observed;

    /// <summary>The GUID of the message that formed the party (each member has its own GUID); null while I am not in a party.</summary>
    public string? PartyId { get; private set; }

    /// <summary>Number of people in my party; 0 when I am not in one.</summary>
    public int Size => PartyId is null ? 0 : _members.Count;

    /// <summary>Members I heard of only through status messages (no name yet; I am not counted): the names come when a party is formed or joined while I am measuring.</summary>
    public int UnknownCount => PartyId is null ? 0 : _members.Count(m => m.Key != Me && m.Value.Length == 0);

    /// <summary>Names of the members known by name.</summary>
    public IEnumerable<string> Names => _members.Values.Where(n => n.Length > 0);

    /// <returns>The party changed (size or members).</returns>
    public bool Handle(PartyMessage m, string selfName)
    {
        int before = Size;
        switch (m)
        {
            case PartyFormedMessage f:
                // A new party starts from the one named here; its other members follow in member messages.
                _members.Clear();
                _heard.Clear();
                _observed = false;
                PartyId = f.PartyId;
                _members[f.CharacterId] = f.Name;
                foreach (var (id, name) in f.Others ?? []) _members[id] = name;
                break;
            case PartyMemberMessage p:
                if (p.Pending && !_members.ContainsKey(p.CharacterId)) { _pending[p.CharacterId] = p.Name; break; }
                _pending.Remove(p.CharacterId);
                PartyId ??= p.PartyId; // the GUID is per member, not per party: a member message never starts another party
                if (p.Name.Length > 0 || !_members.ContainsKey(p.CharacterId)) _members[p.CharacterId] = p.Name; // a join message may lack the name
                break;
            case PartyLeftMessage l:
                _pending.Remove(l.CharacterId);
                _heard.Remove(l.CharacterId);
                if (PartyId is null) break;
                if (_members.Remove(l.CharacterId, out string? who) && who == selfName) Clear(); // I left
                else if (_members.Count < 2) Clear(); // a party of one is no party
                break;
        }
        return Size != before;
    }

    /// <summary>
    /// A status message of a member (1C 92): I am in a party with this character, whether or not I saw the party being formed.
    /// A party known only this way is "observed": its members expire when silent (<see cref="Expire"/>).
    /// </summary>
    public bool Observe(PartyStatusMessage s, DateTimeOffset time)
    {
        int before = Size;
        _heard[s.CharacterId] = time;
        string pendingName = "";
        bool wasPending = _pending.Remove(s.CharacterId, out pendingName!);
        if (PartyId is null)
        {
            PartyId = "observed";
            _observed = true;
            _members[Me] = "";
        }
        if (!_members.ContainsKey(s.CharacterId)) _members[s.CharacterId] = wasPending ? pendingName : "";
        return Size != before;
    }

    /// <summary>Drops members of an observed party that went silent; the party ends when none is left.</summary>
    public bool Expire(DateTimeOffset now)
    {
        if (!_observed) return false;
        int before = Size;
        foreach (var id in _heard.Where(h => now - h.Value > StatusSilence).Select(h => h.Key).ToList())
        {
            _heard.Remove(id);
            _members.Remove(id);
        }
        if (_members.Count < 2) Clear();
        return Size != before;
    }

    /// <summary>The connection restarted or I changed character: what was known is stale.</summary>
    public bool Clear()
    {
        bool had = PartyId is not null;
        PartyId = null;
        _observed = false;
        _members.Clear();
        _heard.Clear();
        _pending.Clear();
        return had;
    }
}
