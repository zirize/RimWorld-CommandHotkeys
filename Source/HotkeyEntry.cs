using System;
using Verse;

namespace CommandHotkeys;

public enum EntryKind
{
    // A command button of the current selection; works while the button is shown.
    Gizmo,
    // An Architect item (designator); works anywhere on the map.
    Designator,
}

// One assigned hotkey. The key itself is stored by the game in KeyPrefs.xml under defName;
// this record only says which command the generated KeyBindingDef stands for.
public class HotkeyEntry : IExposable
{
    public EntryKind kind;
    public string typeName;
    // Designator_Build: the built def's defName; anything else: the command's defaultLabel.
    public string id;
    public string label;
    public string defName;

    [Unsaved] public Type type;
    [Unsaved] public KeyBindingDef keyDef;

    public void ExposeData()
    {
        Scribe_Values.Look(ref kind, "kind");
        Scribe_Values.Look(ref typeName, "type");
        Scribe_Values.Look(ref id, "id");
        Scribe_Values.Look(ref label, "label");
        Scribe_Values.Look(ref defName, "defName");
    }
}
