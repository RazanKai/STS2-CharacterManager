using System;
using CharacterManager.UI;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.Blitz;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace CharacterManager.Patches
{
    /// <summary>
    /// Shows the <see cref="RandomPoolPanel"/> on the character-select screen while the <b>Random</b>
    /// option is selected, and hides it when any concrete character is selected.
    ///
    /// <para><b>Hook.</b> <c>NCharacterSelectScreen.SelectCharacter(NCharacterSelectButton)</c>
    /// is the screen's selection callback (from <c>ICharacterSelectButtonDelegate</c>); it fires for
    /// every selection and exposes the chosen button, whose <c>IsRandom</c> flag tells us whether the
    /// Random tile was picked. We postfix it to toggle the pool card. (v0.113.0 dropped the separate
    /// <c>CharacterModel</c> argument — the button owns the character now.) The card is parented to
    /// the screen, so it is freed automatically when the screen rebuilds (e.g. on a roster toggle).</para>
    ///
    /// <para><b>Blitz.</b> v0.113.0's <c>NBlitzScreen</c> has the same Random option and the same
    /// selection callback, and its Random draw also goes through <c>BeginRunLocally</c> (so the pool
    /// applies). It gets the same panel. The Dread screen's rotating pick deliberately does not —
    /// Dread rotation is resolved by the game's streak logic, not from this pool.</para>
    ///
    /// <para><b>Scope.</b> Shown for the local player's own Random pick in both singleplayer and
    /// multiplayer — it edits the local player's pool, which governs that player's own draw on every
    /// peer (the pool is synced via <see cref="RandomPoolNet"/>). It is inherently local: this hook is
    /// the local selection callback, so it never surfaces for remote players' choices.</para>
    /// </summary>
    [HarmonyPatch(typeof(NCharacterSelectScreen), "SelectCharacter")]
    public static class RandomPoolUiPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NCharacterSelectScreen __instance, NCharacterSelectButton charSelectButton)
            => Toggle(__instance, charSelectButton);

        internal static void Toggle(Node screen, NCharacterSelectButton charSelectButton)
        {
            try
            {
                if (screen == null || !GodotObject.IsInstanceValid(screen)) return;

                bool wantPanel = charSelectButton != null && charSelectButton.IsRandom;

                var existing = screen.GetNodeOrNull(RandomPoolPanel.NodeName) as RandomPoolPanel;

                if (wantPanel)
                {
                    if (existing == null || !GodotObject.IsInstanceValid(existing))
                    {
                        var panel = RandomPoolPanel.Create();
                        screen.AddChild(panel);
                        // Inherit the game theme/font like the mod's other code-built screens.
                        UiTheme.ApplyGameTheme(panel);
                    }
                    else
                    {
                        existing.Visible = true;
                    }

                    // Advertise our current pool to peers the moment we pick Random (no-op in SP).
                    RandomPoolNet.BroadcastLocalPool();
                }
                else if (existing != null && GodotObject.IsInstanceValid(existing))
                {
                    existing.Visible = false;
                }
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] random pool panel toggle failed: " + e.Message);
            }
        }
    }

    /// <summary>Same pool panel on the v0.113.0 Blitz character-select screen.</summary>
    [HarmonyPatch(typeof(NBlitzScreen), "SelectCharacter")]
    public static class RandomPoolUiBlitzPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NBlitzScreen __instance, NCharacterSelectButton charSelectButton)
            => RandomPoolUiPatch.Toggle(__instance, charSelectButton);
    }
}
