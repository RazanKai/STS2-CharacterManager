using CharacterManager.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.StatsScreen;
using MegaCrit.Sts2.Core.Saves;

namespace CharacterManager.Patches
{
    /// <summary>
    /// Injects per-character stat sections for installed CUSTOM characters into the
    /// Compendium → Statistics screen, honoring the per-character "Stats Shown" toggle
    /// managed by the Character Manager screen (<see cref="VisibilityStore"/>).
    ///
    /// <para>The game's own <c>NGeneralStatsGrid.LoadStats</c> adds sections for the 5
    /// base characters only (via private <c>CreateCharacterSection</c>). This postfix runs
    /// afterwards and appends one section per visible custom character that has recorded
    /// stats, mirroring the game's rendering exactly: <c>NCharacterStats.Create(stats)</c>
    /// added to the private <c>_characterStatContainer</c> via <c>AddChildSafely</c>.</para>
    ///
    /// <para>Ported from the base CustomCharacterStats mod's NGeneralStatsGridPatch; the
    /// only change is reading this mod's <see cref="VisibilityStore"/> instead of the base
    /// mod's CharacterVisibilityStore. Custom-only injection is intentional and lives in
    /// exactly this one place — the manager list itself shows base + custom.</para>
    /// </summary>
    [HarmonyPatch(typeof(NGeneralStatsGrid), nameof(NGeneralStatsGrid.LoadStats))]
    public static class StatsGridPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NGeneralStatsGrid __instance)
        {
            var container = Traverse.Create(__instance)
                .Field("_characterStatContainer")
                .GetValue<Control>();
            if (container == null) return;

            var progress = SaveManager.Instance.Progress;

            foreach (var character in CharacterHelper.GetCustomCharacters())
            {
                if (!VisibilityStore.IsVisible(character.Id, defaultVisible: true)) continue;

                // GetStatsForCharacter returns null until the character has been played,
                // so an unplayed custom character is naturally skipped (matches the game's
                // own base-character behavior, which only renders sections for stats != null).
                var stats = progress.GetStatsForCharacter(character.Id);
                if (stats == null) continue;

                var child = NCharacterStats.Create(stats);
                container.AddChildSafely(child);
            }
        }
    }
}
