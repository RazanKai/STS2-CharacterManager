using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.StatsScreen;
using MegaCrit.Sts2.Core.Saves;

namespace CharacterManager.Patches
{
    /// <summary>
    /// Keeps non-playable "meta" characters out of the Compendium → Statistics screen.
    ///
    /// <para><b>Problem.</b> The game has characters with <c>IsPlayable == false</c> that are not
    /// real, selectable characters — <c>RandomCharacter</c> (the "?" random-pick placeholder) and
    /// <c>Deprived</c> (a test character). They have no meaningful stats and no icon scene. Vanilla
    /// never lists them on the Statistics screen. RitsuLib's stats injection does not filter on
    /// <c>IsPlayable</c>, so it builds an <c>NCharacterStats</c> for <c>RandomCharacter</c>. Before
    /// the icon guard that produced a hard crash (missing <c>random_character_icon.tscn</c>); after
    /// it, a meaningless empty "Random" card with 0/0 stats — which is nonsensical, since "Random"
    /// isn't a character, just an instruction to pick one from the pool.</para>
    ///
    /// <para><b>Fix.</b> Every stats card — base game, RitsuLib, or this mod — is built through the
    /// game's own <c>NCharacterStats._Ready</c>, which resolves its character from
    /// <c>_characterStats.Id</c>. We prefix that single choke point: if the resolved character is
    /// non-playable, free the just-added node and skip the body, so no card is rendered for it.
    /// The parent container re-lays-out on removal, leaving no gap. This is independent of which
    /// mod injected the card, so it also removes any stray <c>Deprived</c> section.</para>
    /// </summary>
    [HarmonyPatch(typeof(NCharacterStats), "_Ready")]
    public static class NonPlayableStatsGuardPatch
    {
        private static readonly FieldInfo? StatsField =
            AccessTools.Field(typeof(NCharacterStats), "_characterStats");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool SkipNonPlayable(NCharacterStats __instance)
        {
            try
            {
                if (StatsField?.GetValue(__instance) is CharacterStats cs && cs.Id is ModelId id)
                {
                    var character = ModelDb.GetById<CharacterModel>(id);
                    if (character != null && !character.IsPlayable)
                    {
                        Log.Info($"[CharacterManager] stats: skipping non-playable character '{id.Entry}' " +
                                 "(e.g. RandomCharacter) so it doesn't get an empty stats card.");
                        __instance.QueueFree(); // remove the empty card another mod just added
                        return false;           // skip _Ready: don't build the section (or touch its icon)
                    }
                }
            }
            catch (Exception e)
            {
                // Never let the guard break the stats screen; fall through to normal rendering.
                Log.Warn("[CharacterManager] non-playable stats guard failed: " + e.Message);
            }
            return true;
        }
    }
}
