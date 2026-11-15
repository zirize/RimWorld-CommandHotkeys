using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CommandHotkeys;

// The vanilla keyboard screen knows base keys only. For this mod's bindings, draw the row with the
// chord text and let its buttons open the chord dialog on the screen's working copy (which the
// screen applies on accept). Every other row is left to the game.
[HarmonyPatch(typeof(Dialog_KeyBindings), "DrawKeyEntry")]
public static class Patch_DrawKeyEntry
{
    private static readonly AccessTools.FieldRef<Dialog_KeyBindings, KeyPrefsData> DataRef =
        AccessTools.FieldRefAccess<Dialog_KeyBindings, KeyPrefsData>("keyPrefsData");

    public static bool Prefix(Dialog_KeyBindings __instance, KeyBindingDef keyDef, Rect parentRect, ref float curY, bool skipDrawing)
    {
        if (!Registry.IsOwn(keyDef))
            return true;
        if (!skipDrawing)
        {
            var data = DataRef(__instance);
            // Same layout as the vanilla row.
            Rect rect = new Rect(parentRect.x, parentRect.y + curY, parentRect.width, 34f).ContractedBy(3f);
            GenUI.SetLabelAlign(TextAnchor.MiddleLeft);
            Widgets.Label(rect, keyDef.LabelCap);
            GenUI.ResetLabelAlign();
            var size = new Vector2(140f, 28f);
            var rectA = new Rect(rect.xMax - size.x * 2f - 4f, rect.y, size.x, size.y);
            var rectB = new Rect(rect.xMax - size.x, rect.y, size.x, size.y);
            TooltipHandler.TipRegion(rectA, "CmdHk.ChordTip".Translate());
            TooltipHandler.TipRegion(rectB, "CmdHk.ChordTip".Translate());
            if (Widgets.ButtonText(rectA, Chords.SlotText(data, keyDef, KeyPrefs.BindingSlot.A)))
                Clicked(data, keyDef, KeyPrefs.BindingSlot.A);
            if (Widgets.ButtonText(rectB, Chords.SlotText(data, keyDef, KeyPrefs.BindingSlot.B)))
                Clicked(data, keyDef, KeyPrefs.BindingSlot.B);
        }
        curY += 34f;
        return false;
    }

    private static void Clicked(KeyPrefsData data, KeyBindingDef def, KeyPrefs.BindingSlot slot)
    {
        if (Event.current.button == 0)
        {
            Find.WindowStack.Add(new Dialog_BindCommandKey(data, def, slot));
        }
        else if (Event.current.button == 1)
        {
            var reset = slot == KeyPrefs.BindingSlot.A ? def.defaultKeyCodeA : def.defaultKeyCodeB;
            Find.WindowStack.Add(new FloatMenu(new System.Collections.Generic.List<FloatMenuOption>
            {
                new("ResetBinding".Translate(), () => Chords.Set(data, def, slot, reset, new System.Collections.Generic.List<KeyCode>(), save: false)),
                new("ClearBinding".Translate(), () => Chords.Set(data, def, slot, KeyCode.None, new System.Collections.Generic.List<KeyCode>(), save: false)),
            }));
        }
        Event.current.Use();
    }
}
