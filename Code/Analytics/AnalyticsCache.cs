using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;

namespace CharacterManager.Analytics
{
    /// <summary>
    /// Process-wide cache of per-character <see cref="CharacterAnalytics"/> aggregates (M8, plan §4a).
    /// The analytics screen parses every <c>.run</c> file on open; caching that result means
    /// re-opening the same character — or toggling filters across opens — is instant instead of
    /// re-reading the disk each time.
    ///
    /// <para><b>Invalidation.</b> Instead of hooking the game's save pipeline, each cached entry is
    /// tagged with a cheap <i>generation token</i>: the current count of run-history files. Finishing
    /// a run writes a new <c>.run</c> file (count up) and pruning removes one (count down), so a
    /// changed count means "recompute". This covers run-end and prune without a fragile Harmony patch.
    /// (Edge case: deleting one run and adding another between opens leaves the count unchanged; the
    /// stale entry is corrected on the next count change. Acceptable for a read-only stats view.)</para>
    ///
    /// <para><b>Poison guard.</b> A snapshot whose file list couldn't be read at all
    /// (<see cref="CharacterAnalytics.LoadFailed"/>, i.e. the save system wasn't ready) is never
    /// stored, so the screen can't get pinned to zeros until a run finishes.</para>
    ///
    /// <para><b>Threading.</b> Aggregation runs on the calling (main) thread because
    /// <see cref="SaveManager"/> is a Godot singleton with unverified off-thread safety. The screen
    /// keeps the first frame responsive by painting a "Crunching…" status and deferring the compute
    /// one frame (see <c>CharacterAnalyticsScreen</c>). M8 reads only cheap top-level fields; if the
    /// floor-by-floor walks in M9+ make this too heavy, revisit with a worker that reads raw file
    /// bytes off-thread and deserializes there.</para>
    /// </summary>
    public static class AnalyticsCache
    {
        private sealed class Entry
        {
            public CharacterAnalytics Agg = null!;
            public int Generation;
        }

        private static readonly Dictionary<ModelId, Entry> _cache = new();

        /// <summary>
        /// Returns the cached aggregate for <paramref name="characterId"/> if it is still current,
        /// otherwise computes a fresh one and (unless the load failed) caches it. Always returns a
        /// non-null aggregate; check <see cref="CharacterAnalytics.LoadFailed"/> to distinguish
        /// "no runs" from "couldn't read".
        /// </summary>
        public static CharacterAnalytics Get(ModelId characterId)
        {
            int gen = CurrentGeneration();

            if (_cache.TryGetValue(characterId, out var hit) && hit.Generation == gen)
                return hit.Agg;

            var agg = CharacterAnalytics.Compute(characterId);

            if (agg.LoadFailed)
            {
                // Don't poison the cache with a startup-empty snapshot; let the next open retry.
                _cache.Remove(characterId);
                return agg;
            }

            _cache[characterId] = new Entry { Agg = agg, Generation = gen };
            return agg;
        }

        /// <summary>Drops every cached aggregate. Call if a broad refresh is ever needed.</summary>
        public static void InvalidateAll() => _cache.Clear();

        /// <summary>Drops the cached aggregate for a single character.</summary>
        public static void Invalidate(ModelId characterId) => _cache.Remove(characterId);

        /// <summary>
        /// Generation token = number of run-history files. Cheap to read (a directory listing) and
        /// changes whenever a run is recorded or pruned. Returns -1 on failure so a recompute is
        /// forced rather than serving a possibly-stale entry.
        /// </summary>
        private static int CurrentGeneration()
        {
            try
            {
                var names = SaveManager.Instance.GetAllRunHistoryNames();
                return names?.Count ?? -1;
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] AnalyticsCache generation check failed: " + e.Message);
                return -1;
            }
        }
    }
}
