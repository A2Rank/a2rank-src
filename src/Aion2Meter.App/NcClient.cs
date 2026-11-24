using System.Net;
using System.Net.Http;
using Aion2Meter.Core;

namespace Aion2Meter.App;

/// <summary>What was found of a character: the profile, the worn pieces and (after <see cref="NcClient.LoadDetailsAsync"/>) the details of the ones that have details.</summary>
internal sealed record NcLoaded(int Server, string Name, NcProfile? Profile, IReadOnlyList<NcSlot> Slots, IReadOnlyList<NcItem> Items, string PageUrl, int DetailsExpected, string? Problem,
    IReadOnlyList<NcStatTotal>? Stats = null)
{
    public bool Found => Profile is not null;
}

/// <summary>
/// Asks the official character search (aion2.plaync.com) for a character, only when the user clicks a name, and as little as possible:
/// first one search, the profile and the equipment list (3 small requests); the stones and the engraving of each weapon/armor/accessory (about 19 requests,
/// three at a time, with a pause between them) only when the user asks for them. The site's "too many requests"
/// or any error stops it (what was read is shown). The requests carry the name of this program. What was read is kept in memory for ten minutes (never on disk): the next look at the same one in that time asks nothing.
/// </summary>
internal static class NcClient
{
    private static readonly HttpClient Http = Create();

    // What was read of a character is kept in memory only, for ten minutes (the hotkey for the one I aim at must not ask the site again and again); nothing is written to disk.
    private static readonly Dictionary<(int Server, string Name), (DateTimeOffset At, NcLoaded Data)> Cache = [];
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    private const int MaxCached = 40;

    /// <summary>What was read of this character less than ten minutes ago, or null.</summary>
    public static NcLoaded? Cached(string name, int server)
    {
        lock (Cache) return Cache.TryGetValue((server, name), out var c) && DateTimeOffset.UtcNow - c.At < CacheFor ? c.Data : null;
    }

    /// <summary>Keeps what was read (a character that was found), the fuller one after the stones and engraving were read.</summary>
    public static void Remember(NcLoaded data)
    {
        if (!data.Found) return;
        lock (Cache)
        {
            if (Cache.Count >= MaxCached) Cache.Remove(Cache.MinBy(x => x.Value.At).Key);
            Cache[(data.Server, data.Name)] = (DateTimeOffset.UtcNow, data);
        }
    }

    private static HttpClient Create()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("A2Rank/" + (typeof(NcClient).Assembly.GetName().Version?.ToString(3) ?? "0"));
        return http;
    }

    /// <summary>The search, the profile and the equipment list: who it is and what is worn (no stones or engraving yet).</summary>
    public static async Task<NcLoaded> LoadAsync(string name, int server, CancellationToken cancel)
    {
        string page = NcCharacter.SearchPageUrl(name, server);
        try
        {
            string? id = NcCharacter.FindCharacterId(await Get(NcCharacter.SearchApiUrl(name, server), cancel), name, server);
            if (id is null)
                return new NcLoaded(server, name, null, [], [], page, 0, "공식 정보실에서 이 이름을 찾지 못했습니다 (이름을 바꿨거나 삭제됐거나, 검색에 나오지 않는 캐릭터일 수 있습니다).");
            page = NcCharacter.DetailUrl(server, id);
            string info = await Get(NcCharacterData.InfoUrl(server, id), cancel);
            var profile = NcCharacterData.ParseProfile(info);
            var stats = NcCharacterData.ParseStats(info);
            var slots = NcCharacterData.ParseEquipment(await Get(NcCharacterData.EquipmentUrl(server, id), cancel));
            return new NcLoaded(server, name, profile, slots, [], page, slots.Count(NcCharacterData.HasDetails), null, stats);
        }
        catch (HttpRequestException e)
        {
            return new NcLoaded(server, name, null, [], [], page, 0, Explain(e));
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new NcLoaded(server, name, null, [], [], page, 0, "공식 사이트의 응답이 늦어 멈췄습니다. 잠시 뒤 다시 눌러 보세요.");
        }
    }

    /// <summary>The stones and the engraving of every piece that can have them; <paramref name="progress"/> gets the state after each piece.</summary>
    public static async Task<NcLoaded> LoadDetailsAsync(NcLoaded basic, Action<NcLoaded> progress, CancellationToken cancel)
    {
        string? id = IdOf(basic);
        if (id is null || !basic.Found) return basic;
        var wanted = basic.Slots.Where(NcCharacterData.HasDetails).ToList();
        var items = new List<NcItem>();
        string? problem = null;
        using var gate = new SemaphoreSlim(3);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        try
        {
            await Task.WhenAll(wanted.Select(async slot =>
            {
                await gate.WaitAsync(stop.Token).ConfigureAwait(false);
                try
                {
                    if (stop.IsCancellationRequested) return;
                    var item = NcCharacterData.ParseItem(slot, await Get(NcCharacterData.ItemUrl(basic.Server, id, slot), stop.Token));
                    NcLoaded snapshot;
                    lock (items)
                    {
                        items.Add(item);
                        snapshot = basic with { Items = [.. items] };
                    }
                    progress(snapshot);
                    await Task.Delay(150, stop.Token);
                }
                catch (HttpRequestException e)
                {
                    problem ??= Explain(e);
                    stop.Cancel();
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested && !cancel.IsCancellationRequested) { }
                finally { gate.Release(); }
            }));
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested) { problem ??= "공식 사이트의 응답이 늦어 멈췄습니다. 잠시 뒤 다시 눌러 보세요."; }
        cancel.ThrowIfCancellationRequested();
        return basic with { Items = [.. items.OrderBy(i => i.Slot.Slot)], Problem = problem };
    }

    private static string? IdOf(NcLoaded d)
    {
        string prefix = NcCharacter.Site + "/ko-kr/characters/" + d.Server + "/";
        return d.PageUrl.StartsWith(prefix) ? d.PageUrl[prefix.Length..] : null;
    }

    private static async Task<string> Get(string url, CancellationToken cancel)
    {
        using var response = await Http.GetAsync(url, cancel);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{(int)response.StatusCode}", null, response.StatusCode);
        return await response.Content.ReadAsStringAsync(cancel);
    }

    private static string Explain(HttpRequestException e) => e.StatusCode switch
    {
        HttpStatusCode.TooManyRequests => "공식 사이트가 요청이 많다고 막았습니다. 잠시 뒤에 다시 눌러 보세요 (읽은 데까지만 보입니다).",
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "공식 사이트가 접근을 막았습니다 (읽은 데까지만 보입니다).",
        null => "공식 사이트에 연결하지 못했습니다. 인터넷 연결을 확인해 주세요.",
        var code => $"공식 사이트가 오류({(int)code})로 답했습니다. 잠시 뒤 다시 눌러 보세요.",
    };
}
