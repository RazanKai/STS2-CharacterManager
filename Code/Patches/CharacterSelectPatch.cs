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
    /// else. Base-game characters CAN now be hidden too (the manager exposes the In-Select toggle for
    /// them); a safety guard never lets the select roster be emptied. NOTE: a custom character only appears in
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
        // This handles characters that reach the select screen through the VANILLA path — i.e.
        // those present in <c>ModelDb.AllCharacters</c> at button-build time (e.g. Ryoshu, which
        // patches the getter to add itself). Removing them here means no button is ever built.
        //
        // <para>It does NOT cover characters injected by character libraries. Those register into
        // their own catalogs and add the select buttons directly, never passing through
        // <c>ModelDb.AllCharacters</c> at build time (verified at runtime: this postfix — even at
        // <c>int.MinValue</c>, i.e. after every other getter postfix — only ever saw Ryoshu, while
        // The Cursed / LittleWizard appeared in the strip as ordinary buttons). Those are removed
        // by <see cref="RemoveDisabledButtons_Select"/> / <see cref="RemoveDisabledButtons_CustomRun"/>
        // below, which operate on the built strip.</para>
        //
        // <para><b>Priority.</b> <c>int.MinValue</c> guarantees we run after every other
        // get_AllCharacters postfix (library appenders, Ryoshu's own adder, etc.) regardless of
        // owner-string matching, so the roster is fully assembled before we filter. [HarmonyAfter]
        // is kept as a hint for the named owners.</para>

        [HarmonyPostfix]
        [HarmonyPriority(int.MinValue)]
        [HarmonyAfter("BaseLib", "KitLib", "com.ritsukage.sts2-RitsuLib.framework-content-registry", "com.ritsukage.sts2-RitsuLib.framework-character-assets", "Ryoshu", "TheCursedMod")]
        [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllCharacters), MethodType.Getter)]
        public static void AllCharacters_Getter_Postfix(ref IEnumerable<CharacterModel> __result)
        {
            if (!_filtering || __result == null) return;

            var list = __result.ToList();
            int before = list.Count;
            list.RemoveAll(IsDisabled);
            // Safety: never empty the select roster. If every character in this enumeration is
            // disabled, keep the original set so the player can still start (and re-enable from the
            // manager). Base characters now go through this path too, so this guard matters.
            if (list.Count == 0 && before > 0)
            {
                Log.Warn("[CharacterManager] character-select: all characters disabled; keeping full roster so select isn't empty.");
                return;
            }
            if (list.Count != before)
                Log.Info($"[CharacterManager] character-select: hid {before - list.Count} disabled character(s).");
            __result = list;
        }

        // A character is disabled when the player has turned its In-Select toggle off. Applies to
        // base and custom characters alike (base used to be exempt; the manager now exposes the
        // toggle for them too).
        private static bool IsDisabled(CharacterModel c)
        {
            return c != null && !EnabledStore.IsEnabled(c.Id);
        }

        // ─── Remove disabled-custom buttons that bypass the AllCharacters roster ───
        //
        // <para><b>Why a second mechanism is needed.</b> The getter filter above only
        // affects characters that the select screen builds by iterating
        // <c>ModelDb.AllCharacters</c> (e.g. Ryoshu, which patches the getter directly).
        // Character LIBRARIES register their characters into their OWN catalogs and inject
        // the select buttons straight into the button strip — these characters are NEVER in
        // <c>ModelDb.AllCharacters</c> at build time (verified at runtime: the getter only
        // ever saw <c>CHARACTER.RYOSHU</c>, while The Cursed / LittleWizard appeared in the
        // strip as ordinary <see cref="NCharacterSelectButton"/>s whose <c>Character</c> is
        // the mod-prefixed model, e.g. <c>CHARACTER.LITTLEWIZARD-LITTLE_WIZARD</c>). No getter
        // postfix — at any priority — can reach those.</para>
        //
        // <para><b>Approach.</b> Run LAST on <c>InitCharacterButtons</c> (after the library has
        // injected its buttons), walk the freshly built strip, and free every
        // <see cref="NCharacterSelectButton"/> whose character is a disabled custom. Freeing the
        // node (not flipping <c>Visible</c>) is what sticks: library scrollers re-measure from
        // the strip's real children, so a removed child shrinks the strip correctly. Works for
        // both the character-select and custom-run screens.</para>

        [HarmonyPostfix]
        [HarmonyPriority(int.MinValue)]
        [HarmonyAfter("BaseLib", "com.ritsukage.sts2-RitsuLib.framework-content-registry", "com.ritsukage.sts2-RitsuLib.framework-character-assets")]
        [HarmonyPatch(typeof(NCharacterSelectScreen), "InitCharacterButtons")]
        public static void RemoveDisabledButtons_Select(NCharacterSelectScreen __instance)
            => RemoveDisabledButtons((Node)__instance);

        [HarmonyPostfix]
        [HarmonyPriority(int.MinValue)]
        [HarmonyAfter("BaseLib", "com.ritsukage.sts2-RitsuLib.framework-content-registry", "com.ritsukage.sts2-RitsuLib.framework-character-assets")]
        [HarmonyPatch(typeof(NCustomRunScreen), "InitCharacterButtons")]
        public static void RemoveDisabledButtons_CustomRun(NCustomRunScreen __instance)
            => RemoveDisabledButtons((Node)__instance);

        // Cached accessor for NCharacterSelectButton.Character (public getter; cached to avoid
        // per-button reflection cost on every screen build).
        private static readonly PropertyInfo? ButtonCharacterProp =
            AccessTools.Property(typeof(NCharacterSelectButton), nameof(NCharacterSelectButton.Character));

        private static void RemoveDisabledButtons(Node screenRoot)
        {
            if (screenRoot == null) return;
            try
            {
                var toRemove = new List<Node>();
                CollectDisabledButtons(screenRoot, toRemove, 0);
                foreach (var btn in toRemove)
                {
                    btn.GetParent()?.RemoveChild(btn);
                    btn.QueueFree();
                }
                if (toRemove.Count > 0)
                    Log.Info($"[CharacterManager] character-select: removed {toRemove.Count} disabled custom button(s) from the strip.");
            }
            catch (System.Exception e)
            {
                Log.Warn("[CharacterManager] RemoveDisabledButtons failed: " + e.Message);
            }
        }

        private static void CollectDisabledButtons(Node node, List<Node> sink, int depth)
        {
            if (node == null || depth > 8) return;
            if (node is NCharacterSelectButton btn)
            {
                if (ButtonCharacterProp?.GetValue(btn) is CharacterModel cm && IsDisabled(cm))
                    sink.Add(btn);
                return; // buttons don't nest characters; no need to recurse into one
            }
            foreach (var child in node.GetChildren())
                if (child is Node cn) CollectDisabledButtons(cn, sink, depth + 1);
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
