using System.Collections.Generic;
using System.Linq;
using CharacterManager.Config;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;

namespace CharacterManager.Patches
{
    /// <summary>
    /// Hides custom characters the player has disabled (via the Character Manager screen's
    /// "In Select" toggle, persisted in <see cref="EnabledStore"/>) from the character-select
    /// screens.
    ///
    /// <para><b>Why we filter the model list instead of hiding buttons.</b> An earlier version
    /// postfixed <c>InitCharacterButtons</c> and set <c>Visible = false</c> on the disabled
    /// character's button. That did nothing, because character libraries (notably STS2 RitsuLib)
    /// reshape this screen — they reparent the buttons into their own scroller
    /// (<c>NCharacterButtonStripScroller</c>) and re-measure from the live roster.</para>
    ///
    /// <para><b>Approach.</b> Both select screens build one button per entry in
    /// <c>ModelDb.AllCharacters</c>, so we make the disabled character simply <i>absent</i> from
    /// that enumeration for the duration of button construction. A prefix arms a flag before
    /// <c>InitCharacterButtons</c> runs, a finalizer disarms it afterwards (even on exception),
    /// and a postfix on the <c>ModelDb.AllCharacters</c> getter removes disabled custom
    /// characters while the flag is armed. No button is ever created for a disabled character,
    /// regardless of which container or scroller hosts it.</para>
    ///
    /// <para><b>Ordering is critical.</b> Modded characters are themselves <i>appended</i> to
    /// <c>ModelDb.AllCharacters</c> by other Harmony postfixes (each character mod / library
    /// patches the getter). If our removal postfix ran before those add-postfixes, the character
    /// wouldn't be in <c>__result</c> yet and we'd filter nothing. So this postfix runs LAST —
    /// <see cref="Priority.Last"/> plus <c>[HarmonyAfter]</c> the known adders — exactly as
    /// RitsuLib's own <c>CharacterVanillaSelectionPolicyAllCharactersPatch</c> does.</para>
    ///
    /// <para>The filter is scoped tightly to button construction, so the global
    /// <c>ModelDb.AllCharacters</c> (card pools, stats, run setup, etc.) is unchanged everywhere
    /// else. Base-game characters are never affected. NOTE: a custom character only appears in
    /// select because its own mod/library patches the getter to add it — we only remove what is
    /// already there; we never add characters.</para>
    /// </summary>
    [HarmonyPatch]
    public static class CharacterSelectPatch
    {
        // Armed only while an InitCharacterButtons method is executing. The select screens are
        // built on the main (Godot) thread, so a plain static is sufficient.
        private static bool _filtering;

        // ─── Arm/disarm around button construction (both select screens) ──────

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NCharacterSelectScreen), "InitCharacterButtons")]
        [HarmonyPatch(typeof(NCustomRunScreen), "InitCharacterButtons")]
        public static void InitCharacterButtons_Prefix() => _filtering = true;

        // Finalizer runs whether InitCharacterButtons returns normally or throws.
        [HarmonyFinalizer]
        [HarmonyPatch(typeof(NCharacterSelectScreen), "InitCharacterButtons")]
        [HarmonyPatch(typeof(NCustomRunScreen), "InitCharacterButtons")]
        public static void InitCharacterButtons_Finalizer() => _filtering = false;

        // ─── Remove disabled customs from the roster during construction ──────
        // Runs LAST so modded characters added by other getter postfixes are present in __result
        // before we filter. Mirrors RitsuLib's CharacterVanillaSelectionPolicyAllCharactersPatch.

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("BaseLib", "KitLib", "com.ritsukage.sts2-RitsuLib.framework-content-registry", "Ryoshu", "TheCursedMod")]
        [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllCharacters), MethodType.Getter)]
        public static void AllCharacters_Getter_Postfix(ref IEnumerable<CharacterModel> __result)
        {
            if (!_filtering || __result == null) return;

            var list = __result.ToList();
            int before = list.Count;
            list.RemoveAll(IsDisabledCustom);
            if (list.Count != before)
                Log.Info($"[CharacterManager] character-select: hid {before - list.Count} disabled custom character(s).");
            __result = list;
        }

        private static bool IsDisabledCustom(CharacterModel c)
        {
            return c != null
                && !CharacterHelper.IsBaseCharacter(c.Id)
                && !EnabledStore.IsEnabled(c.Id);
        }
    }
}
