using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Unlocks;

namespace CharacterManager
{
    /// <summary>
    /// Shared filter logic for cross-character source eligibility. Applies <see cref="Config.CrossSourceStore"/>
    /// to a roster of <see cref="CharacterModel"/> or their <see cref="CardPoolModel"/>s.
    /// Never returns an empty set — falls back to the vanilla roster (or own-excluded set) so
    /// consumers that assume ≥1 source (Colorful Philosophers, Kaleidoscope, Splash) never crash.
    /// </summary>
    public static class CrossSourceFilter
    {
        /// <summary>
        /// Filters <paramref name="pools"/> to only those whose character is eligible per the cross-source store.
        /// Since CardPoolModel doesn't directly reference its character, we match pools to characters by
        /// comparing <see cref="CharacterModel.CardPool"/> identity.
        /// If filtering would produce an empty set, returns <paramref name="fallback"/> if non-empty,
        /// otherwise the original unfiltered <paramref name="pools"/>.
        /// </summary>
        public static IEnumerable<CardPoolModel> FilterPools(IEnumerable<CardPoolModel> pools, IEnumerable<CardPoolModel>? fallback = null)
        {
            var poolList = pools.ToList();
            if (poolList.Count == 0) return poolList;

            try
            {
                // Get all characters to map pools back to their owners
                var allCharacters = CharacterHelper.GetAllCharacters();
                var poolToCharacter = allCharacters.ToDictionary(c => c.CardPool, c => c);

                var filtered = poolList.Where(p => poolToCharacter.TryGetValue(p, out var c) && Config.CrossSourceStore.IsEligible(c.Id)).ToList();
                if (filtered.Count > 0) return filtered;

                var fb = fallback?.ToList();
                if (fb != null && fb.Count > 0) return fb;

                Log.Warn("[CharacterManager] cross-source: pool filter empty; falling back to unfiltered pools.");
                return poolList;
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] cross-source: pool filter failed (" + e.Message + "); using unfiltered pools.");
                return poolList;
            }
        }

        /// <summary>
        /// Filters <paramref name="characters"/> to only those eligible per the cross-source store.
        /// If filtering would produce an empty set, returns <paramref name="fallback"/> if non-empty,
        /// otherwise the original unfiltered <paramref name="characters"/>.
        /// </summary>
        public static IEnumerable<CharacterModel> FilterCharacters(IEnumerable<CharacterModel> characters, IEnumerable<CharacterModel>? fallback = null)
        {
            var charList = characters.ToList();
            if (charList.Count == 0) return charList;

            try
            {
                var filtered = charList.Where(c => Config.CrossSourceStore.IsEligible(c.Id)).ToList();
                if (filtered.Count > 0) return filtered;

                var fb = fallback?.ToList();
                if (fb != null && fb.Count > 0) return fb;

                Log.Warn("[CharacterManager] cross-source: character filter empty; falling back to unfiltered characters.");
                return charList;
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] cross-source: character filter failed (" + e.Message + "); using unfiltered characters.");
                return charList;
            }
        }

        /// <summary>
        /// Convenience: the <see cref="UnlockState.CharacterCardPools"/> property, filtered by the cross-source store.
        /// Falls back to vanilla if filtering produces an empty set.
        /// </summary>
        public static IEnumerable<CardPoolModel> PoolsFor(UnlockState unlockState)
        {
            var pools = unlockState.CharacterCardPools;
            // Fallback: own-color-excluded set is a reasonable default for Kaleidoscope/Splash
            var fallback = unlockState.CharacterCardPools;
            return FilterPools(pools, fallback);
        }

        /// <summary>
        /// Convenience: the <see cref="UnlockState.Characters"/> property, filtered by the cross-source store.
        /// Falls back to vanilla if filtering produces an empty set.
        /// </summary>
        public static IEnumerable<CharacterModel> CharactersFor(UnlockState unlockState)
        {
            var chars = unlockState.Characters;
            var fallback = unlockState.Characters;
            return FilterCharacters(chars, fallback);
        }
    }
}