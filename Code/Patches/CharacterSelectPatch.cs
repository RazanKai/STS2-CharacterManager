using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CharacterManager.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

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
        // Depth counter, armed only while an InitCharacterButtons method is executing. The select
        // screens are built on the main (Godot) thread, so a plain static is sufficient. A counter
        // (not a bool) keeps the scope correct even if construction paths ever nest.
        private static int _filterDepth;
        private static bool _filtering => _filterDepth > 0;

        // ─── Arm/disarm around button construction (both select screens) ──────
        //
        // IMPORTANT: each arm/disarm method targets EXACTLY ONE method. Stacking multiple
        // [HarmonyPatch] attributes on a single patch method does NOT patch several methods —
        // Harmony MERGES them into one target descriptor (the last type wins). The previous version
        // stacked NCharacterSelectScreen + NCustomRunScreen on one method, so only NCustomRunScreen
        // was ever patched and the normal character-select screen was never armed (the getter
        // postfix below short-circuited, so disabled customs were never hidden and nothing logged).
        // One method per target is the reliable way to patch both.

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NCharacterSelectScreen), "InitCharacterButtons")]
        public static void Arm_Select() => _filterDepth++;

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(NCharacterSelectScreen), "InitCharacterButtons")]
        public static void Disarm_Select() => _filterDepth--;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NCustomRunScreen), "InitCharacterButtons")]
        public static void Arm_CustomRun() => _filterDepth++;

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(NCustomRunScreen), "InitCharacterButtons")]
        public static void Disarm_CustomRun() => _filterDepth--;

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

        // ─── Live select-screen rebuild (no restart) ────────────────────────
        //
        // The roster is filtered correctly only while the screen's buttons are BUILT (see the
        // AllCharacters getter postfix above). The select screen is built once — eagerly, in
        // NMainMenuSubmenuStack._Ready — then cached in the stack's private _characterSelectSubmenu
        // field and reused for every open. That cache is exactly why toggling a character used to
        // require a game restart.
        //
        // An earlier attempt tried to mutate the live button strip (flipping each button's Visible)
        // on toggle. That can't work: the filter means a disabled character never gets a button to
        // flip, and library reshapers (RitsuLib's NCharacterButtonStripScroller) re-measure from the
        // roster and ignore per-button Visible. So instead we DISCARD the cached screen when the
        // roster changes. The next open makes the stack rebuild it from scratch — re-running
        // InitCharacterButtons (and any reshaping) under the filter above. A fresh build is the same
        // path as launching the game, which is known to honor the toggle. Same for the custom-run
        // screen, which also lists the roster.

        private static NMainMenuSubmenuStack? _menuStack;
        private static bool _subscribed;

        private static readonly FieldInfo? CharSelectField =
            AccessTools.Field(typeof(NMainMenuSubmenuStack), "_characterSelectSubmenu");
        private static readonly FieldInfo? CustomRunField =
            AccessTools.Field(typeof(NMainMenuSubmenuStack), "_customRunScreen");

        // Capture the live main-menu stack and wire the toggle handler when the stack is ready.
        // Doing it here (rather than in a static constructor) guarantees the subscription is active
        // without depending on when the CLR first touches this type.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(NMainMenuSubmenuStack), "_Ready")]
        public static void CaptureStack(NMainMenuSubmenuStack __instance)
        {
            _menuStack = __instance;
            if (!_subscribed)
            {
                EnabledStore.OnToggle += OnRosterToggled;
                _subscribed = true;
            }
        }

        private static void OnRosterToggled(ModelId _)
        {
            var stack = _menuStack;
            if (stack == null || !GodotObject.IsInstanceValid(stack)) return;

            DiscardCachedScreen(stack, CharSelectField);
            DiscardCachedScreen(stack, CustomRunField);
        }

        /// <summary>
        /// Frees the cached submenu held in <paramref name="field"/> on the stack and nulls the field
        /// so the stack rebuilds it on next request. No-ops if the field is empty/invalid, or if the
        /// screen is currently displayed (it can't be while the manager is open, but we guard anyway
        /// so a visible screen is never freed out from under the player).
        /// </summary>
        private static void DiscardCachedScreen(NMainMenuSubmenuStack stack, FieldInfo? field)
        {
            if (field == null) return;
            if (field.GetValue(stack) is not Node screen) return;
            if (!GodotObject.IsInstanceValid(screen)) { field.SetValue(stack, null); return; }
            if (screen is Control c && c.Visible) return; // currently on-screen — leave it

            field.SetValue(stack, null);
            screen.GetParent()?.RemoveChild(screen);
            screen.QueueFree();
            Log.Info("[CharacterManager] discarded cached " + screen.GetType().Name + "; will rebuild on next open.");
        }
    }
}
