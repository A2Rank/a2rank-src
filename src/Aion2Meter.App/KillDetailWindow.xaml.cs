using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Aion2Meter.Core;
using Aion2Meter.Protocol;

namespace Aion2Meter.App;

/// <summary>One figure of a skill's hit statistics: label and value.</summary>
public sealed record HitChip(string Label, string Value);

/// <summary>One skill card: total damage and its share (with a bar), how its hits landed (share of the skill's hits), uses, hits, average and biggest hit.</summary>
public sealed record SkillLine(string Name, string Total, string Share, Brush Bar, IReadOnlyList<HitChip> Chips, Visibility ChipsVisibility, string Usage);

/// <summary>One attacker in the list of who hurt me before a death: name, amount and share, and their biggest skills.</summary>
/// <param name="Skills">The attacker's skills as the same cards the skill lists use (empty for the "others" line); shown when the line is opened.</param>
public sealed record AttackerLine(string Name, string Amount, string Detail, Visibility DetailVisibility, IReadOnlyList<SkillLine> Skills, bool Expanded, bool CanExpand);

/// <summary>One choice in the left list: all kills of this player together, or a single one.</summary>
public sealed record EntryChoice(string Title, string Points, Brush PointsColor, string Sub,
    long Dealt, long Received, IReadOnlyList<SkillDamage>? DealtSkills, IReadOnlyList<SkillDamage>? ReceivedSkills, IReadOnlyList<AttackerDamage>? Attackers,
    HitStats? DealtHow = null, HitStats? ReceivedHow = null, IReadOnlyList<SkillDamage>? HealSkills = null);

/// <summary>
/// Everything known about one player this hour: totals, every single kill (or death) with its own damage, and the
/// damage per skill - of all kills together or of the one picked in the list.
/// </summary>
public partial class KillDetailWindow : Window
{
    private const int MaxLines = 20;

    private int _selected;
    private (string Name, int Server)? _who;

    public KillDetailWindow()
    {
        InitializeComponent();
        Topmost = WindowPrefs.AlwaysOnTop;
    }

    public void Display(KillTally t, bool death)
    {
        Brush Res(string key) => (Brush)FindResource(key);
        Brush gain = Res("GainBrush"), loss = Res("LossBrush");

        SetTitle(t.Name, t.Server, DamageLines.ClassOf(t.Class, t.ReceivedSkills));
        Subtitle.Text = death ? $"나를 {t.Count}번 처치 · 마지막 {Kst.Of(t.Last):HH:mm}" : $"{t.Count}번 처치 · 마지막 {Kst.Of(t.Last):HH:mm}";
        TileOneLabel.Text = death ? "잃은 포인트" : "얻은 포인트";
        TileOne.Text = Fmt.Signed(t.Points);
        TileOne.Foreground = death ? loss : gain;
        TileDealt.Text = t.Dealt > 0 ? Fmt.Damage(t.Dealt) : "-";
        TileReceived.Text = t.Received > 0 ? Fmt.Damage(t.Received) : "-";
        long healed = t.HealedSkills?.Where(h => Protocol.Skills.IsLifeRecovery(h.Key)).Sum(h => h.Total) ?? 0;   // life only (older records may hold 보호의 방패's 정신력/행동력)
        TileHealed.Text = healed > 0 ? Fmt.Damage(healed) : "-";

        var choices = new List<EntryChoice>();
        var entries = t.Entries ?? [];
        if (entries.Count != 1)
            choices.Add(new EntryChoice($"전체 {t.Count}회", Fmt.Signed(t.Points), death ? loss : gain, Summary(t.Dealt, t.Received),
                t.Dealt, t.Received, t.DealtSkills, t.ReceivedSkills, t.Attackers, t.DealtHow, t.ReceivedHow, t.HealedSkills));
        int n = 0;
        foreach (var e in entries)
        {
            n++;
            var d = e.Damage;
            choices.Add(new EntryChoice($"{n}번째 · {Kst.Of(e.Time):HH:mm:ss}" + (e.Duel ? " · 결투" : ""), e.Duel ? "결투" : Fmt.Signed(e.Delta), death ? loss : gain,
                Summary(d?.Dealt ?? 0, d?.Received ?? 0), d?.Dealt ?? 0, d?.Received ?? 0, d?.DealtBySkill, d?.ReceivedBySkill, e.Attackers, d?.DealtHow, d?.ReceivedHow, d?.HealBySkill));
        }

        // A single kill has nothing to choose from: hide the list.
        ListColumn.Width = choices.Count > 1 ? new GridLength(190) : new GridLength(0);
        Entries.Visibility = choices.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        int keep = Math.Min(_selected, choices.Count - 1);
        Entries.ItemsSource = choices;
        Entries.SelectedIndex = Math.Max(0, keep);
    }

    private void SetTitle(string name, int server, int cls)
    {
        _who = name.Length > 0 && Servers.IsValid(server) ? (name, server) : null;
        Title.Text = $"{(name.Length > 0 ? name : "(이름 모름)")}  {Servers.Label(server)}" + (cls != 0 ? $"  ·  {PlayerClass.Name(cls)}" : "") + (_who is null ? "" : "  ↗");
        Title.Cursor = _who is null ? null : Cursors.Hand;
        Title.ToolTip = _who is null ? null : "눌러서 이 캐릭터의 장비와 정보를 봅니다 (공식 캐릭터 정보실, 이름과 서버가 그 사이트로 전달됩니다)";
    }

    /// <summary>One opponent of the live board (<see cref="LiveCombatWindow"/>): no kills yet, just the fight so far. Called again while the fight goes on.</summary>
    public void DisplayLive(DamageLedger.LiveOpponent o)
    {
        Brush Res(string key) => (Brush)FindResource(key);
        double scroll = DetailScroll.VerticalOffset;
        SetTitle(o.Name, o.Server, DamageLines.ClassOf(o.Class, o.ReceivedSkills));
        Subtitle.Text = $"실시간 · 첫 타격 {Kst.Of(o.First):HH:mm:ss} · 마지막 {Kst.Of(o.Last):HH:mm:ss}";
        TileOneLabel.Text = "싸운 시간";
        TileOne.Text = $"{(int)(o.Last - o.First).TotalMinutes}:{(o.Last - o.First).Seconds:00}";
        TileOne.Foreground = Res("TextBrush");
        TileDealt.Text = o.Dealt > 0 ? Fmt.Damage(o.Dealt) : "-";
        TileReceived.Text = o.Received > 0 ? Fmt.Damage(o.Received) : "-";
        TileHealed.Text = "-";

        var choice = new EntryChoice("실시간", "", Res("TextBrush"), Summary(o.Dealt, o.Received), o.Dealt, o.Received, o.DealtSkills, o.ReceivedSkills, null, o.DealtHow, o.ReceivedHow);
        ListColumn.Width = new GridLength(0);
        Entries.Visibility = Visibility.Collapsed;
        Entries.ItemsSource = new[] { choice };
        Entries.SelectedIndex = 0;
        Dispatcher.BeginInvoke(() => DetailScroll.ScrollToVerticalOffset(scroll), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static string Summary(long dealt, long received)
    {
        var parts = new List<string>();
        if (dealt > 0) parts.Add($"준 {Fmt.Damage(dealt)}");
        if (received > 0) parts.Add($"받음 {Fmt.Damage(received)}");
        return parts.Count == 0 ? "피해 기록 없음" : string.Join(" · ", parts);
    }

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Entries.SelectedItem is not EntryChoice c) return;
        _selected = Entries.SelectedIndex;
        var dealt = DamageLines.Skills(c.DealtSkills, MaxLines);
        var received = DamageLines.Skills(c.ReceivedSkills, MaxLines);
        var healed = DamageLines.Skills(c.HealSkills?.Where(h => Protocol.Skills.IsLifeRecovery(h.Key)).ToList(), MaxLines, heal: true);
        var attackers = DamageLines.Attackers(c.Attackers, DamageLines.MaxAttackers);
        DealtHow.Text = DamageLines.How(c.DealtHow, "내가 회복");
        DealtHow.Visibility = DealtHow.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReceivedHow.Text = DamageLines.How(c.ReceivedHow, "상대가 회복");
        ReceivedHow.Visibility = ReceivedHow.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DealtLines.ItemsSource = dealt;
        ReceivedLines.ItemsSource = received;
        AttackerLines.ItemsSource = attackers;
        DealtHeader.Visibility = DealtLines.Visibility = dealt.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HealedLines.ItemsSource = healed;
        HealedHeader.Visibility = HealedLines.Visibility = healed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReceivedHeader.Visibility = ReceivedLines.Visibility = received.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AttackerHeader.Visibility = AttackerLines.Visibility = attackers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoDamage.Visibility = dealt.Count + received.Count + attackers.Count + healed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // The title bar moves the window by dragging; the name in it is a link (a click without a drag opens the character, a drag from the name moves the window too).
    private Point _pressAt;
    private bool _pressedName;

    private void OnTitleDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        _pressedName = _who is not null && ReferenceEquals(e.OriginalSource, Title);
        _pressAt = e.GetPosition(this);
        if (!_pressedName) DragMove();
    }

    private void OnTitleMove(object sender, MouseEventArgs e)
    {
        if (!_pressedName || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(this);
        if (Math.Abs(at.X - _pressAt.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(at.Y - _pressAt.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressedName = false;
        DragMove();
    }

    private void OnTitleUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressedName) return;
        _pressedName = false;
        if (_who is { } w) CharacterInfoWindow.Show(w.Name, w.Server, this);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

/// <summary>Formats damage breakdowns as display lines (shared by the list and the detail window).</summary>
public static class DamageLines
{
    /// <summary>Attackers listed one by one in the detail of a death (a 30 vs 30 fight has dozens), skill cards per attacker, and how many of them start open.</summary>
    public const int MaxAttackers = 60, MaxAttackerSkills = 12, OpenAttackers = 3;

    /// <summary>The class seen in the game; else the one their own skills against me belong to (the most damaging class skill); 0 when neither says.</summary>
    public static int ClassOf(int seen, IReadOnlyList<SkillDamage>? skillsAgainstMe) =>
        seen != 0 ? seen
        : skillsAgainstMe?.Select(s => (Class: PlayerClass.FromSkillKey(s.Key), s.Total)).Where(x => x.Class != 0)
            .GroupBy(x => x.Class).OrderByDescending(g => g.Sum(x => x.Total)).Select(g => g.Key).FirstOrDefault() ?? 0;

    /// <summary>The totals line: "타수 35 · 크리 94% · 전방 20% · 후방 80% · 완벽 6% · 강타 43% · 다단 71% · 막기 11% · 완벽 막기 0% · 회피 8% · 철벽 0% · 재생 49% · 상대가 회복 6,200".</summary>
    public static string How(HitStats? h, string healedLabel)
    {
        if (h is null || h.IsEmpty) return "";
        if (h.Hits == 0) return $"회피 100% ({h.Dodge}번 모두)";   // only dodged attacks so far
        string Pct(int n) => $"{n * 100.0 / h.Hits:0.#}%";
        var parts = new List<string>
        {
            $"타수 {h.Hits}", $"크리 {Pct(h.Critical)}", $"전방 {Pct(h.Front)}", $"후방 {Pct(h.Back)}", $"완벽 {Pct(h.Perfect)}", $"강타 {Pct(h.Smash)}",
            $"다단 {Pct(h.Multi)}", $"막기 {Pct(h.Block)}", $"완벽 막기 {Pct(h.PerfectBlock)}", $"철벽 {Pct(h.Immune)}", $"재생 {Pct(h.Regen)}",
        };
        // 회피: of all attacks that came (landed or not), in both directions (mine dodged by them, theirs dodged by me); players only (mobs are not part of the details).
        parts.Insert(parts.FindIndex(p => p.StartsWith("철벽")), $"회피 {h.Dodge * 100.0 / (h.Hits + h.Dodge):0.#}%");   // 0% too: no dodge seen
        if (h.Healed > 0) parts.Add($"{healedLabel} {h.Healed:N0}");
        return string.Join("  ·  ", parts);
    }
    /// <summary>The biggest skills as table rows (shares of each skill's own hits, total damage and its share); the rest is summed up in one row.</summary>
    public static IReadOnlyList<SkillLine> Skills(IReadOnlyList<SkillDamage>? skills, int max, bool heal = false)
    {
        if (skills is null || skills.Count == 0) return [];
        skills = SkillDamage.Merge(skills, null);   // keys of one skill (천벌) count together
        long total = Math.Max(1, skills.Sum(s => s.Total));
        double Part(long v) => (double)v / total;
        string Share(long v) => v * 100 / total < 1 ? "<1%" : $"{v * 100.0 / total:0.#}%";
        string Pct(int n, int of) => of == 0 ? "-" : $"{n * 100.0 / of:0}%";
        var accent = ((SolidColorBrush)Application.Current.FindResource("AccentBrush")).Color;
        var track = ((SolidColorBrush)Application.Current.FindResource("TileBrush")).Color;
        Brush Bar(double part)
        {
            part = Math.Clamp(part, 0, 1);
            var b = new LinearGradientBrush(new GradientStopCollection
            {
                new(accent, 0), new(accent, part), new(track, part), new(track, 1),
            }, new Point(0, 0), new Point(1, 0));
            b.Freeze();
            return b;
        }
        SkillLine Row(SkillDamage s)
        {
            var h = s.How is { IsEmpty: false } how ? how : null;
            string usage = h is null
                ? (heal ? $"횟수 {s.Hits} · 평균 {Fmt.Damage(s.Total / Math.Max(1, s.Hits))}" : $"타수 {s.Hits}")
                : $"사용 {(h.Casts > 0 ? h.Casts : h.Hits)}회 · 타수 {h.Hits} · 평균 {Fmt.Damage(s.Total / Math.Max(1, h.Hits))}" + (h.Max > 0 ? $" · 최고 {Fmt.Damage(h.Max)}" : "");
            IReadOnlyList<HitChip> chips = h is null ? [] :
            [
                new("크리", Pct(h.Critical, h.Hits)), new("전방", Pct(h.Front, h.Hits)), new("후방", Pct(h.Back, h.Hits)), new("완벽", Pct(h.Perfect, h.Hits)),
                new("강타", Pct(h.Smash, h.Hits)), new("다단", Pct(h.Multi, h.Hits)), new("막기", Pct(h.Block, h.Hits)), new("완벽 막기", Pct(h.PerfectBlock, h.Hits)), new("회피", Pct(h.Dodge, h.Hits + h.Dodge)), new("철벽", Pct(h.Immune, h.Hits)), new("재생", Pct(h.Regen, h.Hits)),
            ];
            return new SkillLine(Protocol.Skills.Name(s.Key), Fmt.Damage(s.Total), Share(s.Total), Bar(Part(s.Total)), chips,
                chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed, usage);
        }
        var lines = skills.Take(max).Select(Row).ToList();
        if (skills.Count > max)
        {
            var rest = skills.Skip(max).ToList();
            long sum = rest.Sum(s => s.Total);
            lines.Add(new SkillLine($"그 외 {rest.Count}종", Fmt.Damage(sum), Share(sum), Bar(Part(sum)), [], Visibility.Collapsed, $"타수 {rest.Sum(s => s.Hits)}"));
        }
        return lines;
    }

    /// <summary>Who hurt me before the deaths of this row: name, amount, share, and their top skills.</summary>
    public static IReadOnlyList<AttackerLine> Attackers(IReadOnlyList<AttackerDamage>? attackers, int max)
    {
        if (attackers is null || attackers.Count == 0) return [];
        long total = Math.Max(1, attackers.Sum(a => a.Total));
        string Share(long v) => v * 100 / total < 1 ? "<1%" : $"{v * 100.0 / total:0}%";
        // Every attacker opens into the same skill cards as the killer's (how the hits landed too, for records made since 0.2.27); the biggest few start open.
        AttackerLine Line(string name, long amount, IReadOnlyList<SkillDamage> skills, bool open)
        {
            string top = string.Join(" · ", SkillDamage.Merge(skills, null).Take(4).Select(s => $"{Protocol.Skills.Name(s.Key)} {Fmt.Damage(s.Total)}"));
            var cards = Skills(skills, MaxAttackerSkills);
            return new AttackerLine(name, $"{Fmt.Damage(amount)}  {Share(amount)}", top, top.Length > 0 && !open ? Visibility.Visible : Visibility.Collapsed, cards, open && cards.Count > 0, cards.Count > 0);
        }
        string Name(AttackerDamage a) => a.IsNpc ? "몬스터·NPC" : a.Name.Length > 0 ? a.Name : $"#{a.Entity}";
        var lines = attackers.Take(max).Select((a, i) => Line(Name(a), a.Total, a.Skills, i < OpenAttackers)).ToList();
        if (attackers.Count > max)
        {
            var rest = attackers.Skip(max).ToList();
            lines.Add(Line($"그 외 {rest.Count}명", rest.Sum(a => a.Total), [], false));
        }
        return lines;
    }
}
