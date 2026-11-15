using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace CommandHotkeys;

// Import of the keys the Hotkeys mod (Aze / mitasamodel lineage) left behind. Only its saved key
// names are read: "Hotkeys_ArchitectHotkey<category>" for category tabs and
// "Hotkeys_DirectHotkey_<label>" / "Hotkeys_GizmoKeys_<label>" for items, matched by label to
// Architect items. Command-button keys cannot be matched (the label alone names no command type).
// Base keys come from KeyPrefs.xml, the modifiers Hotkeys added ("multi keybindings") from its own
// settings file. Only this mod's bindings are written (overwriting them); the game's stay as they are.
public static class HotkeysImport
{
    private const string TabPrefix = "Hotkeys_ArchitectHotkey";
    private static readonly string[] ItemPrefixes = { "Hotkeys_DirectHotkey_", "Hotkeys_GizmoKeys_" };

    public static void Run()
    {
        var saved = ReadSavedBindings();
        var mods = ReadSavedModifiers();
        var done = new HashSet<KeyBindingDef>();
        int tabs = 0, items = 0;
        var unmatched = new List<string>();

        foreach (var (name, a, b) in saved)
        {
            KeyBindingDef target = null;
            if (name.StartsWith(TabPrefix))
            {
                string cat = name.Substring(TabPrefix.Length);
                target = Registry.Tabs.FirstOrDefault(t => t.cat.defName == cat).key;
            }
            else if (ItemPrefixes.FirstOrDefault(name.StartsWith) is string prefix)
            {
                string label = name.Substring(prefix.Length);
                var des = FindDesignatorByLabel(label);
                if (des == null)
                {
                    if (!unmatched.Contains(label))
                        unmatched.Add(label);
                    continue;
                }
                target = Registry.Add(des, EntryKind.Designator).keyDef;
            }
            // Direct and gizmo entries of one item carry the same key; import it once.
            if (target == null || !done.Add(target))
                continue;
            if (name.StartsWith(TabPrefix)) tabs++; else items++;
            mods.TryGetValue(name, out var m);
            Chords.Set(target, KeyPrefs.BindingSlot.A, a, m.a ?? new List<KeyCode>(), save: false);
            Chords.Set(target, KeyPrefs.BindingSlot.B, b, m.b ?? new List<KeyCode>(), save: false);
        }
        KeyPrefs.Save();
        CommandHotkeysMod.Instance.WriteSettings();
        string msg = "CmdHk.ImportDone".Translate(tabs, items);
        if (unmatched.Count > 0)
            msg += " " + "CmdHk.ImportUnmatched".Translate(string.Join(", ", unmatched));
        Messages.Message(msg, MessageTypeDefOf.TaskCompletion, historical: false);
    }

    // Hotkeys' settings (Mod_<id>_Hotkeys.xml): Key_Modifiers/keys[i] is a binding name,
    // values[i]/KeyBindModsA|B the modifier keys of its two slots.
    private static Dictionary<string, (List<KeyCode> a, List<KeyCode> b)> ReadSavedModifiers()
    {
        var map = new Dictionary<string, (List<KeyCode>, List<KeyCode>)>();
        foreach (string path in Directory.GetFiles(GenFilePaths.ConfigFolderPath, "Mod_*_Hotkeys.xml"))
        {
            var km = XDocument.Load(path).Root?.Element("ModSettings")?.Element("Key_Modifiers");
            var keys = km?.Element("keys")?.Elements("li").Select(e => e.Value).ToList();
            var values = km?.Element("values")?.Elements("li").ToList();
            if (keys == null || values == null)
                continue;
            for (int i = 0; i < keys.Count && i < values.Count; i++)
                map[keys[i]] = (ModList(values[i].Element("KeyBindModsA")), ModList(values[i].Element("KeyBindModsB")));
        }
        return map;
    }

    private static List<KeyCode> ModList(XElement e) =>
        e?.Elements("li").Select(x => Parse(x.Value)).Where(Chords.IsModifier).ToList() ?? new List<KeyCode>();

    // Read straight from the file: when Hotkeys is not loaded the game keeps these entries only
    // in a private "unresolved" table.
    private static List<(string name, KeyCode a, KeyCode b)> ReadSavedBindings()
    {
        var list = new List<(string, KeyCode, KeyCode)>();
        string path = GenFilePaths.KeyPrefsFilePath;
        if (!File.Exists(path))
            return list;
        foreach (var li in XDocument.Load(path).Root?.Elements("li") ?? Enumerable.Empty<XElement>())
        {
            string name = li.Element("key")?.Value;
            var value = li.Element("value");
            if (name == null || value == null || !name.StartsWith("Hotkeys_"))
                continue;
            list.Add((name, Parse(value.Element("keyBindingA")?.Value), Parse(value.Element("keyBindingB")?.Value)));
        }
        return list;
    }

    private static KeyCode Parse(string s) =>
        s != null && System.Enum.TryParse<KeyCode>(s, out var k) ? k : KeyCode.None;

    private static Designator FindDesignatorByLabel(string label) =>
        Registry.AllDesignators().FirstOrDefault(d => Matches(d, label));

    private static bool Matches(Designator des, string label) =>
        string.Equals(des.LabelCap.ToString().StripTags(), label, System.StringComparison.OrdinalIgnoreCase) ||
        string.Equals(des.defaultLabel, label, System.StringComparison.OrdinalIgnoreCase);
}
