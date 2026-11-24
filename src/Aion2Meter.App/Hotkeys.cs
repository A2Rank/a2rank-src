using System.Windows.Input;

namespace Aion2Meter.App;

/// <summary>What a global shortcut does.</summary>
public enum HotkeyAction { Overlay, Passthrough, KillList, Live, AimedInfo }

public static class HotkeyActions
{
    public static readonly HotkeyAction[] All = [HotkeyAction.Overlay, HotkeyAction.Passthrough, HotkeyAction.KillList, HotkeyAction.Live, HotkeyAction.AimedInfo];

    public static string Label(HotkeyAction a) => a switch
    {
        HotkeyAction.Overlay => "표시창 보이기/숨기기",
        HotkeyAction.Passthrough => "마우스 통과 켜기/끄기",
        HotkeyAction.Live => "실시간 전투 창 보이기/숨기기",
        HotkeyAction.AimedInfo => "조준한 상대의 캐릭터 정보 보기/닫기",
        _ => "처치 명단 보이기/숨기기",
    };

    public static string Default(HotkeyAction a) => a switch
    {
        HotkeyAction.Overlay => "Ctrl+Alt+O",
        HotkeyAction.Passthrough => "Ctrl+Alt+P",
        HotkeyAction.Live => "Ctrl+Alt+K",
        HotkeyAction.AimedInfo => "Ctrl+Alt+I",
        _ => "Ctrl+Alt+L",
    };
}

/// <summary>A key with modifiers, written like "Ctrl+Alt+P". WPF's ModifierKeys use the same bits as RegisterHotKey.</summary>
public readonly record struct Gesture(ModifierKeys Modifiers, Key Key)
{
    private static readonly (ModifierKeys Flag, string Name)[] Names =
        [(ModifierKeys.Control, "Ctrl"), (ModifierKeys.Alt, "Alt"), (ModifierKeys.Shift, "Shift"), (ModifierKeys.Windows, "Win")];

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    /// <summary>Function keys may stand alone; anything else needs a modifier so typing in the game is not taken.</summary>
    public bool IsUsable => Key is >= Key.F1 and <= Key.F24 || Modifiers != ModifierKeys.None;

    public override string ToString()
    {
        var mods = Modifiers;
        return string.Join("+", Names.Where(n => mods.HasFlag(n.Flag)).Select(n => n.Name).Append(KeyName(Key)));
    }

    public static bool TryParse(string? text, out Gesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = ModifierKeys.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts[..^1])
        {
            var flag = Names.FirstOrDefault(n => n.Name.Equals(part, StringComparison.OrdinalIgnoreCase)).Flag;
            if (flag == ModifierKeys.None) return false;
            mods |= flag;
        }
        string last = parts[^1];
        if (last.Length == 1 && char.IsDigit(last[0])) last = "D" + last;
        if (!Enum.TryParse(last, true, out Key key) || IsModifier(key)) return false;
        gesture = new Gesture(mods, key);
        return gesture.IsUsable;
    }

    /// <summary>The gesture being pressed, or null while only modifiers are down.</summary>
    public static Gesture? FromKeyEvent(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifier(key)) return null;
        return new Gesture(Keyboard.Modifiers, key);
    }

    private static bool IsModifier(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.System or Key.None;

    private static string KeyName(Key k) => k is >= Key.D0 and <= Key.D9 ? ((int)(k - Key.D0)).ToString() : k.ToString();
}
