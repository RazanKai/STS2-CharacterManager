using System;
using CharacterManager.UI;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace CharacterManager.Patches
{
    /// <summary>
    /// Shows the <see cref="RandomPoolPanel"/> on the character-select screen while the <b>Random</b>
    /// option is selected, and hides it when any concrete character is selected.
    ///
    /// <para><b>Hook.</b> <c>NCharacterSelectScreen.SelectCharacter(NCharacterSelectButton, CharacterModel)</c>
    /// is the screen's selection callback (from <c>ICharacterSelectButtonDelegate</c>); it fires for
    /// every selection and exposes the chosen button, whose <c>IsRandom</c> flag tells us whether the
    /// Random tile was picked. We postfix it to toggle the pool card. The card is parented to the
    /// screen, so it is freed automatically when the screen rebuilds (e.g. on a roster toggle).</para>
    ///
    /// <para><b>Scope.</b> Singleplayer only for v1. The random pick is resolved by the host in
    /// <c>BeginRunLocally</c> from the host's local pool; we don't sync the pool across peers, so we
    /// don't surface the panel in multiplayer lobbies to avoid implying it affects other players.</para>
    /// </summary>
    [HarmonyPatch(typeof(NCharacterSelectScreen), "SelectCharacter")]
    public static class RandomPoolUiPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NCharacterSelectScreen __instance, NCharacterSelectButton charSelectButton)
        {
            try
            {
                if (__instance == null || !GodotObject.IsInstanceValid(__instance)) return;

                bool wantPanel = charSelectButton != null
                                 && charSelectButton.IsRandom
                                 && IsSingleplayer(__instance);

                var existing = __instance.GetNodeOrNull(RandomPoolPanel.NodeName) as RandomPoolPanel;

                if (wantPanel)
                {
                    if (existing == null || !GodotObject.IsInstanceValid(existing))
                    {
                        var panel = RandomPoolPanel.Create();
                        __instance.AddChild(panel);
                        // Inherit the game theme/font like the mod's other code-built screens.
                        UiTheme.ApplyGameTheme(panel);
                    }
                    else
                    {
                        existing.Visible = true;
                    }
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

        private static bool IsSingleplayer(NCharacterSelectScreen screen)
        {
            try
            {
                var lobby = screen.Lobby;
                return lobby != null && lobby.NetService != null
                    && lobby.NetService.Type == NetGameType.Singleplayer;
            }
            catch
            {
                // If we can't tell, default to showing it (the common case is singleplayer).
                return true;
            }
        }
    }
}
