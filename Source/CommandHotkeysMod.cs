using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CommandHotkeys;

public class CommandHotkeysSettings : ModSettings
{
    public List<HotkeyEntry> entries = new();
    public List<KeyChord> chords = new();

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
        Scribe_Collections.Look(ref chords, "chords", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            entries ??= new List<HotkeyEntry>();
            chords ??= new List<KeyChord>();
        }
    }
}

public class CommandHotkeysMod : Mod
{
    public static CommandHotkeysMod Instance;
    public static CommandHotkeysSettings Settings;

    private Vector2 scroll;

    public CommandHotkeysMod(ModContentPack content) : base(content)
    {
        Instance = this;
        Settings = GetSettings<CommandHotkeysSettings>();
        new Harmony("zirize.CommandHotkeys").PatchAll();
    }

    public override string SettingsCategory() => "Command Hotkeys";

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var top = new Rect(inRect.x, inRect.y, inRect.width, 60f);
        Widgets.Label(top, "CmdHk.SettingsHelp".Translate(Chords.Text(Registry.AssignKey)));
        if (Widgets.ButtonText(new Rect(inRect.xMax - 200f, inRect.y + 64f, 200f, 30f), "CmdHk.OpenKeyboard".Translate()))
            Find.WindowStack.Add(new Dialog_KeyBindings());
        if (Widgets.ButtonText(new Rect(inRect.xMax - 410f, inRect.y + 64f, 200f, 30f), "CmdHk.Import".Translate()))
            HotkeysImport.Run();

        var entries = Settings.entries;
        var outRect = new Rect(inRect.x, inRect.y + 100f, inRect.width, inRect.height - 100f);
        var view = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(entries.Count, 1) * 30f);
        Widgets.BeginScrollView(outRect, ref scroll, view);
        if (entries.Count == 0)
            Widgets.Label(new Rect(0f, 0f, view.width, 30f), "CmdHk.NoEntries".Translate());
        HotkeyEntry toRemove = null;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var row = new Rect(0f, i * 30f, view.width, 28f);
            if (i % 2 == 1)
                Widgets.DrawLightHighlight(row);
            string kind = ("CmdHk.Kind_" + e.kind).Translate();
            string key = e.keyDef != null ? Chords.Text(e.keyDef) : "-";
            Widgets.Label(new Rect(row.x + 4f, row.y + 4f, row.width - 110f, 24f), $"{e.label}  ({kind})  [{key}]");
            if (Widgets.ButtonText(new Rect(row.xMax - 100f, row.y, 100f, 28f), "CmdHk.Remove".Translate()))
                toRemove = e;
        }
        Widgets.EndScrollView();
        if (toRemove != null)
            Registry.Remove(toRemove);
    }
}
