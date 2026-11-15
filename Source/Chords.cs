using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace CommandHotkeys;

// The modifiers recorded with one slot of one of this mod's bindings. The base key stays in the
// game's KeyPrefs; the modifiers count only while that key is still the one they were recorded with,
// so a key changed in the vanilla keyboard screen simply loses them.
public class KeyChord : IExposable
{
    public string defName;
    public KeyPrefs.BindingSlot slot;
    public KeyCode key;
    public List<KeyCode> mods = new();

    public void ExposeData()
    {
        Scribe_Values.Look(ref defName, "def");
        Scribe_Values.Look(ref slot, "slot");
        Scribe_Values.Look(ref key, "key");
        Scribe_Collections.Look(ref mods, "mods", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            mods ??= new List<KeyCode>();
    }
}

// Key matching for this mod's own bindings: base key plus exactly the recorded modifiers,
// left and right told apart (Input.GetKey does; Event.control does not).
// A chord record is (def, slot, base key) → modifiers. It counts only while that slot holds that
// key, so records written from the keyboard screen's working copy stay harmless if it is cancelled.
public static class Chords
{
    public static readonly KeyCode[] Modifiers =
    {
        KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt,
    };

    private static readonly List<KeyCode> NoMods = new();

    public static bool IsModifier(KeyCode k) => System.Array.IndexOf(Modifiers, k) >= 0;

    private static List<KeyCode> Required(KeyBindingDef def, KeyPrefs.BindingSlot slot, KeyCode bound)
    {
        if (bound == KeyCode.None)
            return NoMods;
        foreach (var c in CommandHotkeysMod.Settings.chords)
        {
            if (c.key == bound && c.slot == slot && c.defName == def.defName)
                return c.mods;
        }
        return NoMods;
    }

    public static bool HasMods(KeyBindingDef def)
    {
        if (!KeyPrefs.KeyPrefsData.keyPrefs.TryGetValue(def, out var d))
            return false;
        return Required(def, KeyPrefs.BindingSlot.A, d.keyBindingA).Count > 0 ||
               Required(def, KeyPrefs.BindingSlot.B, d.keyBindingB).Count > 0;
    }

    // True on the KeyDown event of this binding's key with exactly its modifiers held.
    public static bool Pressed(KeyBindingDef def)
    {
        Event ev = Event.current;
        if (ev.type != EventType.KeyDown || ev.keyCode == KeyCode.None || IsModifier(ev.keyCode))
            return false;
        if (!KeyPrefs.KeyPrefsData.keyPrefs.TryGetValue(def, out var d) || Find.WindowStack.AnySearchWidgetFocused)
            return false;
        return (d.keyBindingA == ev.keyCode && HeldExactly(Required(def, KeyPrefs.BindingSlot.A, d.keyBindingA))) ||
               (d.keyBindingB == ev.keyCode && HeldExactly(Required(def, KeyPrefs.BindingSlot.B, d.keyBindingB)));
    }

    public static List<KeyCode> HeldModifiers() => Modifiers.Where(Input.GetKey).ToList();

    private static bool HeldExactly(List<KeyCode> required)
    {
        foreach (var m in Modifiers)
        {
            if (Input.GetKey(m) != required.Contains(m))
                return false;
        }
        return true;
    }

    public static string Text(KeyCode key, List<KeyCode> mods)
    {
        if (key == KeyCode.None)
            return null;
        string s = "";
        foreach (var m in Modifiers)
        {
            if (mods.Contains(m))
                s += ShortName(m) + "+";
        }
        return s + key.ToStringReadable();
    }

    public static string SlotText(KeyPrefsData data, KeyBindingDef def, KeyPrefs.BindingSlot slot)
    {
        var key = data.GetBoundKeyCode(def, slot);
        return Text(key, Required(def, slot, key)) ?? KeyCode.None.ToStringReadable();
    }

    public static string Text(KeyBindingDef def)
    {
        var data = KeyPrefs.KeyPrefsData;
        if (!data.keyPrefs.TryGetValue(def, out var d))
            return "-";
        string a = Text(d.keyBindingA, Required(def, KeyPrefs.BindingSlot.A, d.keyBindingA));
        string b = Text(d.keyBindingB, Required(def, KeyPrefs.BindingSlot.B, d.keyBindingB));
        return a != null && b != null ? a + " / " + b : a ?? b ?? "-";
    }

    private static string ShortName(KeyCode m) => m switch
    {
        KeyCode.LeftControl => "LCtrl",
        KeyCode.RightControl => "RCtrl",
        KeyCode.LeftShift => "LShift",
        KeyCode.RightShift => "RShift",
        KeyCode.LeftAlt => "LAlt",
        KeyCode.RightAlt => "RAlt",
        _ => m.ToString(),
    };

    // Binds key+mods to one slot of def in data (the live prefs, or the keyboard screen's working copy).
    // Another of this mod's bindings with the same chord loses it; the game's own bindings are never
    // touched (a chord rarely clashes with them, and this mod sees the event first anyway).
    // save: write KeyPrefs.xml too (only for the live prefs; the keyboard screen saves on accept).
    public static void Set(KeyPrefsData data, KeyBindingDef def, KeyPrefs.BindingSlot slot, KeyCode key, List<KeyCode> mods, bool save)
    {
        if (!data.keyPrefs.ContainsKey(def))
            data.AddMissingDefaultBindings();
        if (key != KeyCode.None)
        {
            foreach (var other in Registry.OwnDefs())
            {
                if (other == def || !data.keyPrefs.TryGetValue(other, out var od))
                    continue;
                if (od.keyBindingA == key && SameMods(Required(other, KeyPrefs.BindingSlot.A, key), mods))
                    data.SetBinding(other, KeyPrefs.BindingSlot.A, KeyCode.None);
                if (od.keyBindingB == key && SameMods(Required(other, KeyPrefs.BindingSlot.B, key), mods))
                    data.SetBinding(other, KeyPrefs.BindingSlot.B, KeyCode.None);
            }
            var chords = CommandHotkeysMod.Settings.chords;
            chords.RemoveAll(c => c.defName == def.defName && c.slot == slot && c.key == key);
            if (mods.Count > 0)
                chords.Add(new KeyChord { defName = def.defName, slot = slot, key = key, mods = new List<KeyCode>(mods) });
        }
        data.SetBinding(def, slot, key);
        if (save)
        {
            Prune();
            KeyPrefs.Save();
        }
        CommandHotkeysMod.Instance.WriteSettings();
    }

    public static void Set(KeyBindingDef def, KeyPrefs.BindingSlot slot, KeyCode key, List<KeyCode> mods, bool save = true) =>
        Set(KeyPrefs.KeyPrefsData, def, slot, key, mods, save);

    // Drop records that no longer match the live prefs (keys changed since).
    public static void Prune()
    {
        var data = KeyPrefs.KeyPrefsData;
        CommandHotkeysMod.Settings.chords.RemoveAll(c =>
        {
            var def = DefDatabase<KeyBindingDef>.GetNamedSilentFail(c.defName);
            return def == null || !data.keyPrefs.ContainsKey(def) || data.GetBoundKeyCode(def, c.slot) != c.key;
        });
    }

    private static bool SameMods(List<KeyCode> a, List<KeyCode> b) =>
        a.Count == b.Count && a.All(b.Contains);
}

// "Press a key": records the modifiers held with it. Escape cancels; holding only modifiers waits.
public class Dialog_BindCommandKey : Window
{
    private readonly KeyPrefsData data;
    private readonly KeyBindingDef def;
    private readonly KeyPrefs.BindingSlot slot;
    private readonly bool live;

    public override Vector2 InitialSize => new(400f, 200f);

    // Live prefs (assign menu, mod settings): saved at once.
    public Dialog_BindCommandKey(KeyBindingDef def) : this(KeyPrefs.KeyPrefsData, def, KeyPrefs.BindingSlot.A)
    {
    }

    // The keyboard screen's working copy: that screen saves it on accept.
    public Dialog_BindCommandKey(KeyPrefsData data, KeyBindingDef def, KeyPrefs.BindingSlot slot)
    {
        this.data = data;
        this.def = def;
        this.slot = slot;
        live = data == KeyPrefs.KeyPrefsData;
        closeOnAccept = false;
        closeOnCancel = false;
        forcePause = true;
        onlyOneOfTypeAllowed = true;
        absorbInputAroundWindow = true;
    }

    public override void DoWindowContents(Rect inRect)
    {
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(inRect, "CmdHk.PressKey".Translate(def.LabelCap, Chords.SlotText(data, def, slot)));
        Text.Anchor = TextAnchor.UpperLeft;
        Event ev = Event.current;
        if (ev.type != EventType.KeyDown || ev.keyCode == KeyCode.None || Chords.IsModifier(ev.keyCode))
            return;
        if (ev.keyCode != KeyCode.Escape)
            Chords.Set(data, def, slot, ev.keyCode, Chords.HeldModifiers(), save: live);
        ev.Use();
        Close();
    }
}
