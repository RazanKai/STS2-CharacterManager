using System.Linq;
using CharacterManager.Config;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace CharacterManager.Patches
{
    /// <summary>
    /// Hides character-select buttons for custom characters the player has disabled
    /// via the Character Manager screen.
    ///
    /// How it works:
    ///   Postfix on the private <c>NCharacterSelectScreen.InitCharacterButtons()</c>.
    ///   After the screen populates <c>_charButtonContainer</c>, we iterate all
    ///   <c>NCharacterSelectButton</c> children and set <c>Visible = false</c> for
    ///   any non-random custom character that is disabled in <see cref="EnabledStore"/>.
    ///
    /// Base-game characters are never hidden (we only manage custom ones).
    /// Random button is skipped (<c>IsRandom == true</c>).
    /// </summary>
    [HarmonyPatch(typeof(NCharacterSelectScreen))]
    public static class CharacterSelectPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("InitCharacterButtons")]
        public static void InitCharacterButtons_Postfix(NCharacterSelectScreen __instance)
        {
            // _charButtonContainer is private; get it via Traverse.
            var container = Traverse.Create(__instance)
                .Field("_charButtonContainer")
                .GetValue<Godot.Control>();

            if (container == null)
            {
                Log.Warn("[CharacterManager] CharacterSelectPatch: _charButtonContainer is null — skipping.");
                return;
            }

            foreach (var btn in container.GetChildren().OfType<NCharacterSelectButton>())
            {
                // Skip the "random character" button.
                if (btn.IsRandom) continue;

                // Only hide custom characters — leave base-game ones alone.
                if (CharacterHelper.IsBaseCharacter(btn.Character.Id)) continue;

                if (!EnabledStore.IsEnabled(btn.Character.Id))
                    btn.Visible = false;
            }
        }
    }
}
