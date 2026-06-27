using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;

namespace CharacterManager.Analytics
{
    /// <summary>
    /// A lightweight, roster-wide win/loss history loader for the manager list sparklines (M16).
    ///
    /// <para>The analytics screen's <see cref="CharacterAnalytics.Compute"/> does a deep per-floor
    /// parse for ONE character; calling it for every row would be N full parses on screen open. The
    /// manager list only needs a chronological win/loss series per character, so this does a single
    /// O(files) pass over all <c>.run</c> files and buckets each run's outcome under every character
    /// that played it — reading only top-level fields (no floor walk). One shared pass fills every
    /// row.</para>
    ///
    /// <para><b>Scope.</b> All game modes (Standard + Custom + Daily), decisive runs only — wins and
    /// deaths; abandoned runs are excluded (matching the official win-rate definition). This is a
    /// richer set than the detail panel's Standard-only W/L by design (M16 decision).</para>
    ///
    /// <para><b>Invalidation.</b> Same cheap generation token as <see cref="AnalyticsCache"/>: the
    /// count of run-history files. A changed count forces a reload. A failed file-list read is never
    /// cached, so the list isn't pinned to empty at startup before the save system is ready.</para>
    /// </summary>
    public static class RosterWinHistory
    {
        /// <summary>How one run ended. Abandoned runs are kept but excluded from the win-rate %.</summary>
        public enum Outcome { Win, Loss, Abandoned }

        /// <summary>A character's run outcomes, oldest first.</summary>
        public sealed class Series
        {
            /// <summary>Every run outcome, chronological (oldest first), including abandoned.</summary>
            public readonly List<Outcome> Outcomes = new();
            public int Wins;
            public int Losses;
            public int Abandoned;

            /// <summary>Decisive runs (wins + losses); abandons don't count toward the rate.</summary>
            public int Decisive => Wins + Losses;

            /// <summary>Win rate as a percentage, or -1 when there are no decisive runs.</summary>
            public double WinRatePct => Decisive > 0 ? 100.0 * Wins / Decisive : -1.0;

            /// <summary>
            /// Win rate as a percentage. When <paramref name="countAbandoned"/> is true, abandoned
            /// runs are folded into the denominator as losses (a hopeless run you bailed on counts
            /// against you); otherwise this matches <see cref="WinRatePct"/>. Returns -1 when the
            /// denominator is empty.
            /// </summary>
            public double WinRatePctCounting(bool countAbandoned)
            {
                int denom = countAbandoned ? Wins + Losses + Abandoned : Decisive;
                return denom > 0 ? 100.0 * Wins / denom : -1.0;
            }

            /// <summary>
            /// The most-recent <paramref name="n"/> outcomes, oldest first (newest last). When
            /// <paramref name="includeAbandoned"/> is false, abandoned runs are dropped before windowing.
            /// </summary>
            public List<Outcome> Recent(int n, bool includeAbandoned)
            {
                var src = includeAbandoned
                    ? Outcomes
                    : Outcomes.FindAll(o => o != Outcome.Abandoned);
                if (n <= 0 || src.Count <= n) return src;
                return src.GetRange(src.Count - n, n);
            }
        }

        // Transient (start-time, outcome) pairs collected per character during a load, so the series
        // can be sorted chronologically before the start times are discarded.
        private sealed class Builder
        {
            public readonly List<(long start, Outcome o)> Runs = new();
        }

        private static readonly Dictionary<ModelId, Series> _cache = new();
        private static readonly Series Empty = new();
        private static int _generation = int.MinValue;

        /// <summary>
        /// Returns the win/loss series for <paramref name="characterId"/>, loading (or reloading) the
        /// whole roster's history on the first call after a run-count change. Always non-null; a
        /// character with no decisive runs returns an empty series.
        /// </summary>
        public static Series Get(ModelId characterId)
        {
            EnsureLoaded();
            return _cache.TryGetValue(characterId, out var s) ? s : Empty;
        }

        /// <summary>Forces a reload on the next <see cref="Get"/>.</summary>
        public static void Invalidate() => _generation = int.MinValue;

        private static void EnsureLoaded()
        {
            int gen = CurrentGeneration();
            if (gen == _generation && _generation != int.MinValue) return;
            if (gen < 0)
            {
                // Save system not ready — don't cache an empty roster; retry next time.
                return;
            }

            Load();
            _generation = gen;
        }

        private static void Load()
        {
            _cache.Clear();

            List<string>? names = null;
            try { names = SaveManager.Instance.GetAllRunHistoryNames(); }
            catch (Exception e) { Log.Warn("[CharacterManager] RosterWinHistory names failed: " + e.Message); }
            if (names == null) return;

            var builders = new Dictionary<ModelId, Builder>();

            foreach (var name in names)
            {
                try
                {
                    var result = SaveManager.Instance.LoadRunHistory(name);
                    if (!result.Success) continue;
                    RunHistory? h = result.SaveData;
                    if (h == null) continue;

                    // Keep abandoned runs (shown as muted ticks when the user opts in); they're still
                    // excluded from the win-rate % via the Outcome split below.
                    Outcome outcome = h.WasAbandoned ? Outcome.Abandoned
                        : (h.Win ? Outcome.Win : Outcome.Loss);
                    if (h.Players == null) continue;
                    foreach (var p in h.Players)
                    {
                        var id = p?.Character;
                        if (id == null || id == ModelId.none) continue;
                        if (!builders.TryGetValue(id, out var b)) { b = new Builder(); builders[id] = b; }
                        b.Runs.Add((h.StartTime, outcome));
                    }
                }
                catch (Exception e)
                {
                    Log.Warn($"[CharacterManager] RosterWinHistory could not read '{name}': {e.Message}");
                }
            }

            foreach (var kv in builders)
            {
                var list = kv.Value.Runs;
                list.Sort((x, y) => x.start.CompareTo(y.start)); // oldest first
                var s = new Series();
                foreach (var (_, o) in list)
                {
                    s.Outcomes.Add(o);
                    switch (o)
                    {
                        case Outcome.Win: s.Wins++; break;
                        case Outcome.Loss: s.Losses++; break;
                        default: s.Abandoned++; break;
                    }
                }
                _cache[kv.Key] = s;
            }
        }

        /// <summary>Generation token = number of run-history files; -1 on failure (forces reload).</summary>
        private static int CurrentGeneration()
        {
            try
            {
                var names = SaveManager.Instance.GetAllRunHistoryNames();
                return names?.Count ?? -1;
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] RosterWinHistory generation check failed: " + e.Message);
                return -1;
            }
        }
    }
}
