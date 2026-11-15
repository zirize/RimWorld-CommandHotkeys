using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CommandHotkeys;

// Keys for the toggle icons at the bottom right of the map screen (merged from the user's own
// Overlay Hotkeys mod, 2026-11-15). Vanilla draws them in PlaySettings.DoMapControls and binds only
// beauty and room stats; each binding here flips the public PlaySettings field (or Prefs value)
// behind its icon, so the icon shows the new state on the next frame.
public static class MapToggles
{
    private sealed class Toggle
    {
        public string Name;
        public bool WorldToo;
        public Func<bool> Available;
        public Func<bool> Get;
        public Action<bool> Set;
        public KeyBindingDef Def;
    }

    private static List<Toggle> toggles;

    public static IEnumerable<KeyBindingDef> Defs()
    {
        toggles ??= Build();
        foreach (var t in toggles)
        {
            if (t.Def != null)
                yield return t.Def;
        }
    }

    // Own skip rules (the colonist bar toggle also works on the world map), so this runs before
    // the map-only checks of the other keys.
    public static bool TryToggle(Event ev)
    {
        if (GUIUtility.keyboardControl != 0 || Find.WindowStack.WindowsPreventCameraMotion)
            return false;
        bool onMap = Find.CurrentMap != null && !WorldRendererUtility.WorldSelected;
        toggles ??= Build();
        foreach (var t in toggles)
        {
            if (t.Def == null || !Chords.Pressed(t.Def))
                continue;
            if (!(onMap || t.WorldToo) || !t.Available())
                return false;
            bool value = !t.Get();
            t.Set(value);
            ev.Use();
            (value ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            Messages.Message(
                "CmdHk.ToggleState".Translate(("CmdHk.Toggle_" + t.Name).Translate(), (value ? "CmdHk.On" : "CmdHk.Off").Translate()),
                MessageTypeDefOf.SilentInput, historical: false);
            return true;
        }
        return false;
    }

    private static List<Toggle> Build()
    {
        static PlaySettings P() => Find.PlaySettings;
        static bool Always() => true;
        var list = new List<Toggle>
        {
            new() { Name = "Zones", Available = Always, Get = () => P().showZones, Set = v => P().showZones = v },
            new() { Name = "RoofOverlay", Available = Always, Get = () => P().showRoofOverlay, Set = v => P().showRoofOverlay = v },
            new() { Name = "FertilityOverlay", Available = Always, Get = () => P().showFertilityOverlay, Set = v => P().showFertilityOverlay = v },
            new() { Name = "TerrainAffordanceOverlay", Available = Always, Get = () => P().showTerrainAffordanceOverlay, Set = v => P().showTerrainAffordanceOverlay = v },
            new() { Name = "TemperatureOverlay", Available = Always, Get = () => P().showTemperatureOverlay, Set = v => P().showTemperatureOverlay = v },
            new() { Name = "PollutionOverlay", Available = () => ModsConfig.BiotechActive, Get = () => P().showPollutionOverlay, Set = v => P().showPollutionOverlay = v },
            // Vanilla shows the vacuum icon only on vacuum maps and forces the field off elsewhere.
            new() { Name = "VacuumOverlay", Available = () => ModsConfig.OdysseyActive && Find.CurrentMap.Biome.inVacuum, Get = () => P().showVacuumOverlay, Set = v => P().showVacuumOverlay = v },
            new() { Name = "ColonistBar", WorldToo = true, Available = Always, Get = () => P().showColonistBar, Set = v => P().showColonistBar = v },
            new() { Name = "LearningHelper", Available = Always, Get = () => P().showLearningHelper, Set = v => P().showLearningHelper = v },
            new() { Name = "AutoHomeArea", Available = Always, Get = () => P().autoHomeArea, Set = v => P().autoHomeArea = v },
            new() { Name = "AutoRebuild", Available = Always, Get = () => P().autoRebuild, Set = v => P().autoRebuild = v },
            new() { Name = "ResourceReadout", Available = Always, Get = () => Prefs.ResourceReadoutCategorized, Set = v => Prefs.ResourceReadoutCategorized = v },
        };
        foreach (var t in list)
        {
            t.Def = DefDatabase<KeyBindingDef>.GetNamedSilentFail("CommandHotkeys_Toggle_" + t.Name);
            if (t.Def == null)
                Log.Warning("[CommandHotkeys] missing KeyBindingDef CommandHotkeys_Toggle_" + t.Name);
        }
        return list;
    }
}
