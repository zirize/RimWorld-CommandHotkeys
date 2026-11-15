using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CommandHotkeys;

// Generated KeyBindingDefs and the lookups from a command to its entry.
// The defs exist from startup on, so KeyPrefs.xml keeps their keys like any vanilla binding
// (and key-chord mods that extend vanilla bindings apply to them too).
[StaticConstructorOnStartup]
public static class Registry
{
    public const string TabPrefix = "CommandHotkeys_Tab_";

    public static readonly KeyBindingDef AssignKey = DefDatabase<KeyBindingDef>.GetNamed("CommandHotkeys_Assign");
    private static readonly KeyBindingCategoryDef GizmoCategory = DefDatabase<KeyBindingCategoryDef>.GetNamed("CommandHotkeys_Gizmos");
    private static readonly KeyBindingCategoryDef ArchitectCategory = DefDatabase<KeyBindingCategoryDef>.GetNamed("CommandHotkeys_Architect");
    private static readonly KeyBindingCategoryDef ToggleCategory = DefDatabase<KeyBindingCategoryDef>.GetNamed("CommandHotkeys_Toggles");

    // Gizmo and designator entries, by (command type, id).
    private static readonly Dictionary<(Type, string), HotkeyEntry> gizmos = new();
    private static readonly Dictionary<(Type, string), HotkeyEntry> designators = new();
    public static readonly List<HotkeyEntry> DesignatorEntries = new();
    // Architect category tab bindings, with the category each one opens.
    public static readonly List<(KeyBindingDef key, DesignationCategoryDef cat)> Tabs = new();

    static Registry()
    {
        foreach (var cat in DefDatabase<DesignationCategoryDef>.AllDefsListForReading)
        {
            var def = MakeDef(TabPrefix + cat.defName, "CmdHk.OpenTab".Translate(cat.label), ArchitectCategory);
            Tabs.Add((def, cat));
        }
        foreach (var e in CommandHotkeysMod.Settings.entries)
        {
            e.type = GenTypes.GetTypeInAnyAssembly(e.typeName);
            e.keyDef = MakeDef(e.defName, EntryDefLabel(e), e.kind == EntryKind.Gizmo ? GizmoCategory : ArchitectCategory);
            Index(e);
        }
        // The game read KeyPrefs.xml before these defs existed and kept their keys aside; resolve them now.
        KeyPrefs.Init();
    }

    // Designator_Build is shared by every buildable, so its id is the built def; other commands use their label.
    public static void Identify(Command c, out Type type, out string id)
    {
        type = c.GetType();
        if (c is Designator_Build b && b.PlacingDef != null)
            id = b.PlacingDef.defName;
        else
            id = !c.defaultLabel.NullOrEmpty() ? c.defaultLabel : c.Label ?? "";
    }

    public static HotkeyEntry Lookup(Command c, EntryKind kind)
    {
        Identify(c, out var type, out var id);
        (kind == EntryKind.Gizmo ? gizmos : designators).TryGetValue((type, id), out var e);
        return e;
    }

    public static bool HasGizmoEntries => gizmos.Count > 0;

    public static bool IsOwn(KeyBindingDef def) =>
        def.category == GizmoCategory || def.category == ArchitectCategory || def.category == ToggleCategory || def == AssignKey;

    // Every Architect item, dropdowns flattened. Ideology items need the player faction, so
    // outside a game (main menu settings) only the fixed ones are listed.
    public static IEnumerable<Designator> AllDesignators()
    {
        bool inGame = Current.ProgramState == ProgramState.Playing && Current.Game?.World != null;
        foreach (var cat in DefDatabase<DesignationCategoryDef>.AllDefsListForReading)
        {
            foreach (var des in inGame ? cat.AllResolvedAndIdeoDesignators : cat.AllResolvedDesignators)
            {
                if (des is Designator_Dropdown dd)
                {
                    foreach (var inner in dd.Elements)
                        yield return inner;
                }
                else
                    yield return des;
            }
        }
    }

    public static HotkeyEntry TryGizmo(Command c)
    {
        if (gizmos.Count == 0)
            return null;
        Identify(c, out var type, out var id);
        return gizmos.TryGetValue((type, id), out var e) ? e : null;
    }

    public static HotkeyEntry Add(Command c, EntryKind kind)
    {
        var existing = Lookup(c, kind);
        if (existing != null)
            return existing;
        Identify(c, out var type, out var id);
        var e = new HotkeyEntry
        {
            kind = kind,
            typeName = type.FullName,
            id = id,
            label = c.LabelCap.ToString().StripTags(),
            type = type,
        };
        e.defName = UniqueDefName((kind == EntryKind.Gizmo ? "CommandHotkeys_G_" : "CommandHotkeys_D_") + Hash(e.typeName + "|" + id));
        e.keyDef = MakeDef(e.defName, EntryDefLabel(e), kind == EntryKind.Gizmo ? GizmoCategory : ArchitectCategory);
        KeyPrefs.KeyPrefsData.AddMissingDefaultBindings();
        CommandHotkeysMod.Settings.entries.Add(e);
        Index(e);
        CommandHotkeysMod.Instance.WriteSettings();
        return e;
    }

    // The def stays in the database until restart (the game has no public way to remove one);
    // with its keys cleared and the entry gone it matches nothing and is not recreated.
    public static void Remove(HotkeyEntry e)
    {
        if (e.keyDef != null)
        {
            Chords.Set(e.keyDef, KeyPrefs.BindingSlot.A, KeyCode.None, new List<KeyCode>(), save: false);
            Chords.Set(e.keyDef, KeyPrefs.BindingSlot.B, KeyCode.None, new List<KeyCode>(), save: false);
            KeyPrefs.Save();
        }
        CommandHotkeysMod.Settings.entries.Remove(e);
        if (e.type != null)
        {
            (e.kind == EntryKind.Gizmo ? gizmos : designators).Remove((e.type, e.id));
            DesignatorEntries.Remove(e);
        }
        CommandHotkeysMod.Instance.WriteSettings();
    }

    public static bool IsBound(KeyBindingDef def) =>
        KeyPrefs.KeyPrefsData.keyPrefs.TryGetValue(def, out var d) &&
        (d.keyBindingA != KeyCode.None || d.keyBindingB != KeyCode.None);

    // Every binding this mod owns: chord conflicts are resolved among these only.
    public static IEnumerable<KeyBindingDef> OwnDefs()
    {
        yield return AssignKey;
        foreach (var t in Tabs)
            yield return t.key;
        foreach (var d in MapToggles.Defs())
            yield return d;
        foreach (var e in CommandHotkeysMod.Settings.entries)
        {
            if (e.keyDef != null)
                yield return e.keyDef;
        }
    }

    private static void Index(HotkeyEntry e)
    {
        if (e.type == null)
            return;
        if (e.kind == EntryKind.Gizmo)
            gizmos[(e.type, e.id)] = e;
        else
        {
            designators[(e.type, e.id)] = e;
            DesignatorEntries.Add(e);
        }
    }

    private static string EntryDefLabel(HotkeyEntry e) =>
        e.kind == EntryKind.Gizmo ? "CmdHk.GizmoKey".Translate(e.label) : "CmdHk.DesignatorKey".Translate(e.label);

    private static KeyBindingDef MakeDef(string defName, string label, KeyBindingCategoryDef category)
    {
        var existing = DefDatabase<KeyBindingDef>.GetNamedSilentFail(defName);
        if (existing != null)
            return existing;
        var def = new KeyBindingDef
        {
            defName = defName,
            label = label,
            category = category,
            defaultKeyCodeA = KeyCode.None,
            defaultKeyCodeB = KeyCode.None,
            modContentPack = CommandHotkeysMod.Instance.Content,
        };
        DefDatabase<KeyBindingDef>.Add(def);
        return def;
    }

    private static string UniqueDefName(string baseName)
    {
        string name = baseName;
        for (int i = 2; DefDatabase<KeyBindingDef>.GetNamedSilentFail(name) != null; i++)
            name = baseName + "_" + i;
        return name;
    }

    // FNV-1a: stable across runs, unlike string.GetHashCode on .NET Core.
    private static string Hash(string s)
    {
        uint h = 2166136261;
        foreach (char ch in s)
        {
            h ^= ch;
            h *= 16777619;
        }
        return h.ToString("x8");
    }
}
