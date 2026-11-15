using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace CommandHotkeys;

// All input handling. GameComponentOnGUI runs at the start of every GUI event, before the map UI
// and the window stack draw (UIRoot.UIRootOnGUI), so a key it consumes never reaches the game.
// Keys are matched by Chords.Pressed (base key + exact modifiers); details: docs/design.md.
public class CommandHotkeysGameComp : GameComponent
{
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    // GizmoGridDrawer caches the selection's gizmos for one frame (cacheFrame/gizmoList).
    private static readonly Func<int> CacheFrame = StaticGetter<int>(typeof(GizmoGridDrawer), "cacheFrame");
    private static readonly List<Gizmo> CachedGizmos =
        typeof(GizmoGridDrawer).GetField("gizmoList", StaticPrivate)?.GetValue(null) as List<Gizmo>;
    private static readonly FieldInfo DesPanelsField =
        typeof(MainTabWindow_Architect).GetField("desPanelsCached", BindingFlags.Instance | BindingFlags.NonPublic);

    private static int appliedFrame = -1;
    private static readonly List<Command> tmpMatches = new();
    private static readonly List<Gizmo> tmpGroup = new();
    private static Dictionary<(Type, string), Designator> designatorCache;

    public CommandHotkeysGameComp(Game game)
    {
        // Ideology adds designators per game; rebuild the lookup for each game.
        designatorCache = null;
        if (CacheFrame == null || CachedGizmos == null)
            Log.WarningOnce("[CommandHotkeys] GizmoGridDrawer cache not found; command keys still work but show no key label.", 0x5c3a01);
    }

    public override void GameComponentOnGUI()
    {
        ApplyToCachedGizmos();

        Event ev = Event.current;
        if (ev.type != EventType.KeyDown || ev.keyCode == KeyCode.None)
            return;
        if (MapToggles.TryToggle(ev))
            return;
        if (Find.CurrentMap == null || WorldRendererUtility.WorldSelected)
            return;
        if (GUIUtility.keyboardControl != 0 || AnyBlockingWindow())
            return;

        if (Chords.Pressed(Registry.AssignKey))
        {
            OpenAssignMenu();
            ev.Use();
            return;
        }
        if (TryGizmo(ev) || TryDesignator(ev))
            return;
        TryCategoryTab(ev);
    }

    private static bool GizmoGridShown
    {
        get
        {
            var tab = Find.MainTabsRoot.OpenTab;
            return tab == null || tab == MainButtonDefOf.Inspect;
        }
    }

    // Once per frame, as soon as the grid has built this frame's gizmo list, give the matching
    // commands our key so the grid draws its letter. Only for chord-less keys: the vanilla grid
    // ignores modifiers. The press itself is always handled by TryGizmo, which runs first.
    private static void ApplyToCachedGizmos()
    {
        int frame = Time.frameCount;
        if (appliedFrame == frame || CacheFrame == null || CachedGizmos == null || !Registry.HasGizmoEntries)
            return;
        if (CacheFrame() != frame)
            return;
        appliedFrame = frame;
        for (int i = 0; i < CachedGizmos.Count; i++)
        {
            if (CachedGizmos[i] is Command c && Registry.TryGizmo(c) is HotkeyEntry e && e.keyDef != null && Registry.IsBound(e.keyDef) && !Chords.HasMods(e.keyDef))
                c.hotKey = e.keyDef;
        }
    }

    // Handled the way the grid handles a click: first enabled command of the group, plus the
    // group members it inherits from. Using the event keeps the grid from handling it again.
    private static bool TryGizmo(Event ev)
    {
        if (!Registry.HasGizmoEntries || !GizmoGridShown)
            return false;
        tmpMatches.Clear();
        foreach (object obj in Find.Selector.SelectedObjects)
        {
            if (!(obj is ISelectable sel))
                continue;
            foreach (Gizmo g in sel.GetGizmos())
            {
                if (g is Command c && c.Visible && Registry.TryGizmo(c) is HotkeyEntry e && e.keyDef != null && Chords.Pressed(e.keyDef))
                    tmpMatches.Add(c);
            }
        }
        if (tmpMatches.Count == 0)
            return false;

        Command first = tmpMatches[0];
        tmpGroup.Clear();
        Command target = null;
        foreach (Command c in tmpMatches)
        {
            if (c != first && !first.GroupsWith(c))
                continue;
            tmpGroup.Add(c);
            if (target == null && !c.Disabled)
                target = c;
        }
        if (target == null)
        {
            if (!first.disabledReason.NullOrEmpty())
                Messages.Message(first.disabledReason, MessageTypeDefOf.RejectInput, historical: false);
            ev.Use();
            return true;
        }
        foreach (Gizmo g in tmpGroup)
        {
            if (g != target && !g.Disabled && target.InheritInteractionsFrom(g))
                g.ProcessInput(ev);
        }
        target.ProcessInput(ev);
        target.ProcessGroupInput(ev, tmpGroup);
        ev.Use();
        return true;
    }

    private static bool TryDesignator(Event ev)
    {
        var entries = Registry.DesignatorEntries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.keyDef == null || !Chords.Pressed(e.keyDef))
                continue;
            var des = FindDesignator(e);
            if (des == null || !des.Visible)
                continue;
            des.ProcessInput(ev);
            ev.Use();
            return true;
        }
        return false;
    }

    // Opens the Architect menu on the category; pressing the key again on an open category closes it.
    private static void TryCategoryTab(Event ev)
    {
        foreach (var (key, cat) in Registry.Tabs)
        {
            if (!Chords.Pressed(key))
                continue;
            if (!(MainButtonDefOf.Architect.TabWindow is MainTabWindow_Architect architect))
                return;
            var panel = FindPanel(architect, cat);
            if (panel == null)
                return;
            ev.Use();
            bool open = Find.MainTabsRoot.OpenTab == MainButtonDefOf.Architect;
            if (open && architect.selectedDesPanel == panel)
            {
                Find.MainTabsRoot.EscapeCurrentTab();
                return;
            }
            if (!open)
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Architect);
            architect.selectedDesPanel = panel;
            Find.DesignatorManager.Deselect();
            return;
        }
    }

    private static void OpenAssignMenu()
    {
        Command target = null;
        EntryKind kind = EntryKind.Gizmo;
        // LastMouseOverGizmo is only refreshed while the map gizmo grid is drawn.
        if (GizmoGridShown && MapGizmoUtility.LastMouseOverGizmo is Command hovered)
        {
            target = hovered;
            kind = hovered is Designator ? EntryKind.Designator : EntryKind.Gizmo;
        }
        else if (Find.DesignatorManager.SelectedDesignator is Designator selected)
        {
            target = selected;
            kind = EntryKind.Designator;
        }
        if (target == null)
        {
            Messages.Message("CmdHk.NothingToAssign".Translate(), MessageTypeDefOf.RejectInput, historical: false);
            return;
        }

        string label = target.LabelCap.ToString().StripTags();
        var existing = Registry.Lookup(target, kind);
        var options = new List<FloatMenuOption>
        {
            new FloatMenuOption(
                existing != null
                    ? "CmdHk.ChangeKey".Translate(label, Chords.Text(existing.keyDef))
                    : "CmdHk.SetKey".Translate(label),
                () =>
                {
                    var e = Registry.Add(target, kind);
                    Find.WindowStack.Add(new Dialog_BindCommandKey(e.keyDef));
                }),
        };
        if (existing != null)
            options.Add(new FloatMenuOption("CmdHk.RemoveKey".Translate(label), () => Registry.Remove(existing)));
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private static Designator FindDesignator(HotkeyEntry e)
    {
        if (designatorCache == null)
        {
            designatorCache = new Dictionary<(Type, string), Designator>();
            foreach (var des in Registry.AllDesignators())
            {
                Registry.Identify(des, out var type, out var id);
                designatorCache.TryAdd((type, id), des);
            }
        }
        return designatorCache.TryGetValue((e.type, e.id), out var d) ? d : null;
    }

    private static ArchitectCategoryTab FindPanel(MainTabWindow_Architect architect, DesignationCategoryDef cat)
    {
        if (!(DesPanelsField?.GetValue(architect) is List<ArchitectCategoryTab> panels))
            return null;
        foreach (var panel in panels)
        {
            if (panel.def == cat)
                return panel;
        }
        return null;
    }

    // A modal dialog (absorbs input around itself) keeps its keys; vanilla tab hotkeys do not fire there either.
    private static bool AnyBlockingWindow()
    {
        var windows = Find.WindowStack.Windows;
        for (int i = 0; i < windows.Count; i++)
        {
            if (windows[i].absorbInputAroundWindow)
                return true;
        }
        return false;
    }

    private static Func<T> StaticGetter<T>(Type type, string field)
    {
        var fi = type.GetField(field, StaticPrivate);
        if (fi == null || fi.FieldType != typeof(T))
            return null;
        return Expression.Lambda<Func<T>>(Expression.Field(null, fi)).Compile();
    }
}
