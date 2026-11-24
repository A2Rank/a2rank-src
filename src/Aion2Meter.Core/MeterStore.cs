using Microsoft.Data.Sqlite;

namespace Aion2Meter.Core;

/// <summary>
/// SQLite file with periods, kills and small state values. Records belong to a character
/// (<see cref="CharacterKey.Id"/>, "" while unknown). Times are stored as Unix milliseconds.
/// </summary>
public sealed class MeterStore : IDisposable
{
    private readonly SqliteConnection _db;


    /// <summary>
    /// Copies the database of another installation into a new file with SQLite's own backup (right also while that installation has it open: its
    /// -wal file is part of the data, so copying only the .db file can give a broken database). False when it could not be read; nothing is left behind then.
    /// </summary>
    public static bool CopyDatabase(string from, string to)
    {
        try
        {
            using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = from, Mode = SqliteOpenMode.ReadOnly }.ToString());
            source.Open();
            using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = to, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
            target.Open();
            source.BackupDatabase(target);
            return true;
        }
        catch (SqliteException)
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(to); } catch (IOException) { }
            return false;
        }
        finally { SqliteConnection.ClearAllPools(); }
    }
    /// <param name="path">Database file, or ":memory:" for tests.</param>
    public MeterStore(string path)
    {
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        _db.Open();
        try
        {
        Execute("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS buckets (
                id INTEGER PRIMARY KEY, character TEXT NOT NULL DEFAULT '', start_ms INTEGER NOT NULL, end_ms INTEGER NOT NULL,
                gain INTEGER NOT NULL, loss INTEGER NOT NULL, events INTEGER NOT NULL, partial INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS kills (
                id INTEGER PRIMARY KEY, character TEXT NOT NULL DEFAULT '', time_ms INTEGER NOT NULL, kind INTEGER NOT NULL,
                name TEXT NOT NULL, server INTEGER NOT NULL, race INTEGER NOT NULL, delta INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS unmatched (
                id INTEGER PRIMARY KEY, character TEXT NOT NULL DEFAULT '', time_ms INTEGER NOT NULL, delta INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS state (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            -- an earlier development build kept window records in "windows" (one per start); they are made again as window_records
            DROP TABLE IF EXISTS windows;
            CREATE TABLE IF NOT EXISTS window_records (
                id INTEGER PRIMARY KEY, character TEXT NOT NULL, start_ms INTEGER NOT NULL, end_ms INTEGER NOT NULL, json TEXT NOT NULL,
                sent INTEGER NOT NULL DEFAULT 0, UNIQUE (character, start_ms, end_ms));
            """);
        // ... and whether the record is still to be written to the record files (exported = 0: waiting; older rows had no such need).
        if (!Query("SELECT name FROM pragma_table_info('window_records')", [], r => r.GetString(0)).Contains("exported"))
            Execute("ALTER TABLE window_records ADD COLUMN exported INTEGER NOT NULL DEFAULT 1");
        // Databases from before characters were told apart.
        foreach (var table in new[] { "buckets", "kills", "unmatched" })
            if (!Query($"SELECT name FROM pragma_table_info('{table}')", [], r => r.GetString(0)).Contains("character"))
                Execute($"ALTER TABLE {table} ADD COLUMN character TEXT NOT NULL DEFAULT ''");
        // Databases from before periods were split by party size ("size:gain:loss:events;...").
        if (!Query("SELECT name FROM pragma_table_info('buckets')", [], r => r.GetString(0)).Contains("parties"))
            Execute("ALTER TABLE buckets ADD COLUMN parties TEXT NOT NULL DEFAULT ''");
        // Databases from before damage was recorded with the kills.
        var killColumns = Query("SELECT name FROM pragma_table_info('kills')", [], r => r.GetString(0));
        foreach (var column in new[] { "dealt", "received", "dealt_hits", "received_hits" })
            if (!killColumns.Contains(column))
                Execute($"ALTER TABLE kills ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0");
        // ... and the life I received during the fight (per skill, see SkillDamage.Encode).
        if (!killColumns.Contains("healed_skills")) Execute("ALTER TABLE kills ADD COLUMN healed_skills TEXT NOT NULL DEFAULT ''");
        // ... and how the hits landed ("hits,crit,smash,block,front,back,multi,regen,healed", see HitStats).
        foreach (var column in new[] { "dealt_how", "received_how" })
            if (!killColumns.Contains(column))
                Execute($"ALTER TABLE kills ADD COLUMN {column} TEXT NOT NULL DEFAULT ''");
        // ... and who hurt me before a death (JSON, see AttackerDamage).
        if (!killColumns.Contains("attackers")) Execute("ALTER TABLE kills ADD COLUMN attackers TEXT NOT NULL DEFAULT ''");
        // ... and per skill ("6563:3:12345;69ad:2:999", see SkillDamage.Encode).
        foreach (var column in new[] { "dealt_skills", "received_skills" })
            if (!killColumns.Contains(column))
                Execute($"ALTER TABLE kills ADD COLUMN {column} TEXT NOT NULL DEFAULT ''");
        // ... and the class of the other player (PlayerClass code, 0 = not known).
        if (!killColumns.Contains("class")) Execute("ALTER TABLE kills ADD COLUMN class INTEGER NOT NULL DEFAULT 0");
        // Development builds stored the name alone for a while: server not known.
        foreach (var table in new[] { "buckets", "kills", "unmatched" })
            Execute($"UPDATE {table} SET character = '0:' || character WHERE character <> '' AND character NOT LIKE '%:%'");
        Execute("""
            CREATE INDEX IF NOT EXISTS buckets_character_start ON buckets (character, start_ms);
            CREATE INDEX IF NOT EXISTS buckets_end ON buckets (end_ms);
            CREATE INDEX IF NOT EXISTS kills_character_time ON kills (character, time_ms);
            CREATE INDEX IF NOT EXISTS unmatched_character_time ON unmatched (character, time_ms);
            """);
        }
        catch
        {
            _db.Dispose();   // a damaged database must not stay open (the caller may want to move the file away)
            SqliteConnection.ClearAllPools();
            throw;
        }
    }

    public void SaveBucket(string character, Bucket b)
    {
        var values = new object[] { character, Ms(b.Start), Ms(b.End), b.Gain, b.Loss, b.Events, b.Partial ? 1 : 0, b.EncodeParties() };
        if (b.Id == 0)
            b.Id = (long)Scalar("INSERT INTO buckets (character, start_ms, end_ms, gain, loss, events, partial, parties) VALUES ($0, $1, $2, $3, $4, $5, $6, $7) RETURNING id", values)!;
        else
            Execute("UPDATE buckets SET character = $0, start_ms = $1, end_ms = $2, gain = $3, loss = $4, events = $5, partial = $6, parties = $7 WHERE id = $8", [.. values, b.Id]);
    }

    /// <summary>The character's periods that started in [<paramref name="from"/>, <paramref name="to"/>), oldest first.</summary>
    public List<Bucket> Buckets(string character, DateTimeOffset from, DateTimeOffset to) =>
        Query("SELECT id, start_ms, end_ms, gain, loss, events, partial, parties FROM buckets WHERE character = $0 AND start_ms >= $1 AND start_ms < $2 ORDER BY start_ms, id",
            [character, Ms(from), Ms(to)], r => new Bucket
            {
                Id = r.GetInt64(0), Start = Time(r.GetInt64(1)), End = Time(r.GetInt64(2)),
                Gain = r.GetInt64(3), Loss = r.GetInt64(4), Events = r.GetInt32(5), Partial = r.GetInt64(6) != 0,
                Parties = Bucket.DecodeParties(r.GetString(7)),
            });

    public List<Bucket> BucketsOnDay(string character, DateOnly day) => Buckets(character, Kst.DayStart(day), Kst.DayStart(day.AddDays(1)));

    /// <summary>Periods of every character that ended in (<paramref name="after"/>, <paramref name="until"/>], by end time.</summary>
    public List<(string Character, Bucket Bucket)> BucketsEnded(DateTimeOffset after, DateTimeOffset until) =>
        Query("SELECT character, id, start_ms, end_ms, gain, loss, events, partial, parties FROM buckets WHERE end_ms > $0 AND end_ms <= $1 ORDER BY end_ms, id",
            [Ms(after), Ms(until)], r => (r.GetString(0), new Bucket
            {
                Id = r.GetInt64(1), Start = Time(r.GetInt64(2)), End = Time(r.GetInt64(3)),
                Gain = r.GetInt64(4), Loss = r.GetInt64(5), Events = r.GetInt32(6), Partial = r.GetInt64(7) != 0,
                Parties = Bucket.DecodeParties(r.GetString(8)),
            }));

    /// <summary>Characters with any saved period, most recently active first.</summary>
    public List<string> Characters() =>
        Query("SELECT character FROM buckets GROUP BY character ORDER BY MAX(start_ms) DESC", [], r => r.GetString(0));

    public void AddKill(string character, KillRecord k) =>
        Execute("INSERT INTO kills (character, time_ms, kind, name, server, race, delta, dealt, received, dealt_hits, received_hits, dealt_skills, received_skills, attackers, dealt_how, received_how, healed_skills, class) VALUES ($0, $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17)",
            [character, Ms(k.Time), (int)k.Kind, k.Name, k.Server, k.Race, k.Delta,
             k.Damage?.Dealt ?? 0, k.Damage?.Received ?? 0, k.Damage?.DealtHits ?? 0, k.Damage?.ReceivedHits ?? 0,
             SkillDamage.Encode(k.Damage?.DealtBySkill), SkillDamage.Encode(k.Damage?.ReceivedBySkill),
             k.Attackers is { Count: > 0 } a ? System.Text.Json.JsonSerializer.Serialize(a) : "",
             k.Damage?.DealtHow?.Encode() ?? "", k.Damage?.ReceivedHow?.Encode() ?? "", SkillDamage.Encode(k.Damage?.HealBySkill), k.Class]);

    public List<KillRecord> KillsSince(string character, DateTimeOffset since) =>
        Query("SELECT kind, name, server, race, delta, time_ms, dealt, received, dealt_hits, received_hits, dealt_skills, received_skills, attackers, dealt_how, received_how, healed_skills, class FROM kills WHERE character = $0 AND time_ms >= $1 ORDER BY time_ms, id",
            [character, Ms(since)],
            r => new KillRecord((KillKind)r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt64(4), Time(r.GetInt64(5)),
                Damage: r.GetInt64(6) == 0 && r.GetInt64(7) == 0
                    ? null
                    : new DamageStats(r.GetInt64(6), r.GetInt64(7), r.GetInt32(8), r.GetInt32(9),
                        SkillDamage.Decode(r.GetString(10)), SkillDamage.Decode(r.GetString(11)),
                        HitStats.Decode(r.GetString(13)), HitStats.Decode(r.GetString(14)), SkillDamage.Decode(r.GetString(15))),
                Attackers: DecodeAttackers(r.GetString(12)), Class: r.GetInt32(16)));

    private static IReadOnlyList<AttackerDamage>? DecodeAttackers(string json)
    {
        if (json.Length == 0) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<List<AttackerDamage>>(json); }
        catch (System.Text.Json.JsonException) { return null; }   // a damaged value only loses this detail
    }

    public void AddUnmatched(string character, UnmatchedGain g) =>
        Execute("INSERT INTO unmatched (character, time_ms, delta) VALUES ($0, $1, $2)", [character, Ms(g.Time), g.Delta]);

    public List<UnmatchedGain> UnmatchedSince(string character, DateTimeOffset since) =>
        Query("SELECT delta, time_ms FROM unmatched WHERE character = $0 AND time_ms >= $1 ORDER BY time_ms, id", [character, Ms(since)],
            r => new UnmatchedGain(r.GetInt64(0), Time(r.GetInt64(1))));

    /// <summary>Records made before the character was known become <paramref name="character"/>'s.</summary>
    public void ClaimUnassigned(string character)
    {
        foreach (var table in new[] { "buckets", "kills", "unmatched" })
            Execute($"UPDATE {table} SET character = $0 WHERE character = ''", [character]);
    }

    /// <summary>Records saved under <paramref name="from"/> (e.g. before the server was known) become <paramref name="to"/>'s.</summary>
    public void RenameCharacter(string from, string to)
    {
        foreach (var table in new[] { "buckets", "kills", "unmatched" })
            Execute($"UPDATE {table} SET character = $1 WHERE character = $0", [from, to]);
        Execute("UPDATE OR IGNORE state SET key = $1 WHERE key = $0", ["total:" + from, "total:" + to]);
    }

    // ---- Window records ("구간 기록") waiting to be sent, and the ones already sent (kept a while to show them in the app).

    /// <summary>
    /// Keeps a window; the same character's window with the same start and end is kept once (a period cut short and its follower share the start).
    /// </summary>
    /// <param name="send">To be sent to the server (the consent is on).</param>
    /// <param name="export">To be written to the record files (a record folder is chosen).</param>
    public void AddWindow(string character, DateTimeOffset start, DateTimeOffset end, string json, bool send = true, bool export = false) =>
        Execute("INSERT OR IGNORE INTO window_records (character, start_ms, end_ms, json, sent, exported) VALUES ($0, $1, $2, $3, $4, $5)",
            [character, Ms(start), Ms(end), json, send ? 0 : 1, export ? 0 : 1]);

    /// <summary>Windows still to be written to the record files, oldest first.</summary>
    public List<(long Id, string Json)> PendingExports(int limit) =>
        Query("SELECT id, json FROM window_records WHERE exported = 0 ORDER BY start_ms, end_ms, id LIMIT $0", [limit], r => (r.GetInt64(0), r.GetString(1)));

    public void MarkExported(IEnumerable<long> ids)
    {
        foreach (var id in ids) Execute("UPDATE window_records SET exported = 1 WHERE id = $0", [id]);
    }

    /// <summary>The most kills kept on this PC when names are kept for good (the oldest go first): about 100-200 MB at most.</summary>
    public const int MaxKeptKills = 150_000;

    /// <summary>Deletes the oldest kills (and gains that could not be tied to a kill) beyond <paramref name="max"/>.</summary>
    public int TrimKills(int max)
    {
        int over = Convert.ToInt32(Scalar("SELECT COUNT(*) FROM kills", [])) - max;
        if (over <= 0) return 0;
        ExecuteCount("DELETE FROM kills WHERE id IN (SELECT id FROM kills ORDER BY time_ms, id LIMIT $0)", [over]);
        return over;
    }

    /// <summary>
    /// Deletes everything that holds the names of other players and is older than <paramref name="before"/>: the kills (and the gains that could
    /// not be tied to one) and the copies of window records kept for sending and for the record files. The points per period (buckets) have no names
    /// and stay. Files the user asked for in the record folder are the user's and are not touched.
    /// </summary>
    /// <returns>How many kills and how many window records were deleted.</returns>
    public (int Kills, int Windows) PruneNames(DateTimeOffset before)
    {
        long ms = Ms(before);
        int kills = ExecuteCount("DELETE FROM kills WHERE time_ms < $0", [ms]);
        ExecuteCount("DELETE FROM unmatched WHERE time_ms < $0", [ms]);
        int windows = ExecuteCount("DELETE FROM window_records WHERE end_ms < $0", [ms]);
        return (kills, windows);
    }

    /// <summary>The record files are turned off: what waited to be written is not written later.</summary>
    public void DiscardExports() => Execute("UPDATE window_records SET exported = 1 WHERE exported = 0");

    /// <summary>Windows not sent yet, oldest first.</summary>
    public List<(long Id, string Json)> PendingWindows(int limit) =>
        Query("SELECT id, json FROM window_records WHERE sent = 0 ORDER BY start_ms, id LIMIT $0", [limit], r => (r.GetInt64(0), r.GetString(1)));

    /// <summary>The kept windows (sent or not), newest first, as the JSON they were queued with.</summary>
    public List<string> RecentWindows(int limit) =>
        Query("SELECT json FROM window_records ORDER BY start_ms DESC, end_ms DESC, id DESC LIMIT $0", [limit], r => r.GetString(0));

    public int PendingWindowCount() => Convert.ToInt32(Scalar("SELECT COUNT(*) FROM window_records WHERE sent = 0", []));

    public int WindowCount() => Convert.ToInt32(Scalar("SELECT COUNT(*) FROM window_records", []));

    public void MarkWindowsSent(IEnumerable<long> ids)
    {
        foreach (var id in ids) Execute("UPDATE window_records SET sent = 1 WHERE id = $0", [id]);
    }

    /// <summary>Forgets the windows that were kept to be sent (consent withdrawn); what still waits for the record files is theirs and stays.</summary>
    public void DeleteWindows()
    {
        Execute("UPDATE window_records SET sent = 1 WHERE exported = 0");
        Execute("DELETE FROM window_records WHERE exported = 1");
    }

    /// <summary>Drops windows that were sent and written that started before <paramref name="before"/>; the others stay until they are.</summary>
    public void PruneWindows(DateTimeOffset before) => Execute("DELETE FROM window_records WHERE sent = 1 AND exported = 1 AND start_ms < $0", [Ms(before)]);

    public string? Get(string key) => Scalar("SELECT value FROM state WHERE key = $0", [key]) as string;

    public void Set(string key, string value) =>
        Execute("INSERT INTO state (key, value) VALUES ($0, $1) ON CONFLICT (key) DO UPDATE SET value = excluded.value", [key, value]);

    public void Dispose() => _db.Dispose();

    private static long Ms(DateTimeOffset t) => t.ToUnixTimeMilliseconds();
    private static DateTimeOffset Time(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(Kst.Offset);

    private SqliteCommand Command(string sql, object[]? args)
    {
        var c = _db.CreateCommand();
        c.CommandText = sql;
        for (int i = 0; args is not null && i < args.Length; i++) c.Parameters.AddWithValue("$" + i, args[i]);
        return c;
    }

    private void Execute(string sql, object[]? args = null)
    {
        using var c = Command(sql, args);
        c.ExecuteNonQuery();
    }

    private int ExecuteCount(string sql, object[]? args = null)
    {
        using var c = Command(sql, args);
        return c.ExecuteNonQuery();
    }

    private object? Scalar(string sql, object[] args)
    {
        using var c = Command(sql, args);
        return c.ExecuteScalar();
    }

    private List<T> Query<T>(string sql, object[] args, Func<SqliteDataReader, T> read)
    {
        using var c = Command(sql, args);
        using var r = c.ExecuteReader();
        var items = new List<T>();
        while (r.Read()) items.Add(read(r));
        return items;
    }
}
