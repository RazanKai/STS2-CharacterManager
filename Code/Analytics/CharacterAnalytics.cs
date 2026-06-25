using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace CharacterManager.Analytics
{
    public enum GameModeFilter { All, Standard, Custom, Daily }

    /// <summary>A single card offered in a reward this run, with whether it was taken (M9).</summary>
    public sealed class CardChoiceRec
    {
        public ModelId Id = ModelId.none;
        public int Upgrade;
        public bool Picked;
    }

    /// <summary>A card instance reference (id + upgrade level) recorded in a run (M9).</summary>
    public readonly struct CardRef
    {
        public readonly ModelId Id;
        public readonly int Upgrade;
        public CardRef(ModelId id, int upgrade) { Id = id; Upgrade = upgrade; }
    }

    /// <summary>Aggregated stats for one card (or upgraded variant) across a run set (M9).</summary>
    public sealed class CardStat
    {
        public string Key = "";
        public string Name = "";
        public ModelId Id = ModelId.none;
        public bool Upgraded;     // true if this entry is an upgraded variant (upgrade-aware mode)
        public int Offered;       // times shown in a card reward (per occurrence)
        public int Picks;         // times chosen from a reward (per occurrence)
        public int RunsWith;      // runs whose final/gained deck included this card (once per run)
        public int WinsWith;      // wins among RunsWith
        public int Removed;       // times removed (per occurrence)
        public int Upgrades;      // times upgraded (per occurrence)

        /// <summary>Pick rate %, or -1 if never offered.</summary>
        public double PickRatePct => Offered > 0 ? 100.0 * Picks / Offered : -1.0;
        /// <summary>Win rate % among runs that had it, or -1 if it was never in a deck.</summary>
        public double WinRatePct => RunsWith > 0 ? 100.0 * WinsWith / RunsWith : -1.0;
    }

    /// <summary>One run's worth of fields, extracted from a <see cref="RunHistory"/> file.</summary>
    public sealed class RunSummary
    {
        public string Seed = "";
        public long StartTime;       // unix seconds
        public bool Win;
        public bool Abandoned;
        public int Ascension;
        public float RunTime;        // seconds
        public int ActsReached;
        public int FloorsReached;
        public GameMode GameMode;    // which game mode this run was in

        // ─── Per-run card facts (M9), populated by the deep parse and aggregated on demand ───
        /// <summary>Every card offered in a reward this run, with its picked flag (per-occurrence).</summary>
        public List<CardChoiceRec> CardChoices = new();
        /// <summary>Cards the player ended up with: union of floor-by-floor CardsGained and the final
        /// deck snapshot (caveat 1 — starter cards aren't in CardsGained). May contain duplicates;
        /// aggregation de-dupes per run (caveat 3).</summary>
        public List<CardRef> DeckCards = new();
        /// <summary>Cards removed this run (per-occurrence).</summary>
        public List<CardRef> RemovedCards = new();
        /// <summary>Card ids upgraded this run (per-occurrence).</summary>
        public List<ModelId> UpgradedCardIds = new();
    }

    /// <summary>
    /// A composite run-history filter (M8). Combines the game-mode axis with a minimum-ascension
    /// floor and an optional "most recent N runs" window. <see cref="MinAscension"/> = 0 keeps all
    /// ascensions; <see cref="RecentCount"/> &lt;= 0 keeps all runs. Used as the cache key dimension
    /// the filter bar drives.
    /// </summary>
    public readonly struct RunFilter
    {
        public readonly GameModeFilter Mode;
        public readonly int MinAscension;
        public readonly int RecentCount;   // 0/negative = all

        public RunFilter(GameModeFilter mode, int minAscension = 0, int recentCount = 0)
        {
            Mode = mode;
            MinAscension = minAscension < 0 ? 0 : minAscension;
            RecentCount = recentCount;
        }

        public static readonly RunFilter AllRuns = new(GameModeFilter.All);

        public bool IsAll => Mode == GameModeFilter.All && MinAscension == 0 && RecentCount <= 0;
    }

    /// <summary>
    /// Read-only aggregation of a character's run-history files. Loads every <c>.run</c> file
    /// once (the same O(n) approach the M3 filter uses) and computes both summary totals and a
    /// per-run list. Shared by the analytics screen (M4) and the stats exporter (M5) so there is
    /// a single source of truth. Never writes to any save.
    /// </summary>
    public sealed class CharacterAnalytics
    {
        public int Total, Wins, Deaths, Abandoned, MaxAct, MaxFloor;
        // Split by game mode: only Standard runs count toward the game's official CharacterStats
        // (Custom and Daily are excluded by ProgressSaveManager). We surface both views.
        public int StandardTotal, StandardWins, StandardDeaths, StandardAbandoned;
        public int CustomTotal, CustomWins, CustomDeaths, CustomAbandoned; // Custom + Daily (non-Standard)
        public float MaxRunTime;
        public double SumRunTime;
        public float FastestWin = -1f;
        public double AvgRunTime => Total > 0 ? SumRunTime / Total : 0;

        public readonly Dictionary<int, (int w, int l)> PerAscension = new();
        public readonly Dictionary<int, int> ActReached = new();
        public readonly Dictionary<int, int> FloorReached = new();   // floors -> run count (M8)
        public readonly List<RunSummary> Runs = new();

        /// <summary>
        /// True when the run-history file list couldn't be read at all (save system not ready).
        /// The cache (M8) refuses to store such snapshots so the screen doesn't get poisoned with
        /// zeros at startup; see <see cref="AnalyticsCache"/>.
        /// </summary>
        public bool LoadFailed;

        /// <summary>Loads and aggregates every run-history file for the given character.</summary>
        public static CharacterAnalytics Compute(ModelId characterId)
        {
            var a = new CharacterAnalytics();

            List<string>? names = null;
            try { names = SaveManager.Instance.GetAllRunHistoryNames(); }
            catch (Exception e) { Log.Warn("[CharacterManager] GetAllRunHistoryNames failed: " + e.Message); }
            if (names == null) { a.LoadFailed = true; return a; }

            foreach (var name in names)
            {
                try
                {
                    var result = SaveManager.Instance.LoadRunHistory(name);
                    if (!result.Success) continue;
                    RunHistory? h = result.SaveData;
                    if (h == null) continue;

                    // Find this character's player (for its NetId, used to pick the right per-floor
                    // PlayerStats in multiplayer, and for the final deck snapshot).
                    RunHistoryPlayer? me = null;
                    foreach (var p in h.Players)
                        if (p.Character == characterId) { me = p; break; }
                    if (me == null) continue;

                    // Acts/floors *reached* come from MapPointHistory (outer list = acts actually
                    // entered, inner lists = floors within each act). h.Acts is the run's full planned
                    // act list — always ~3-4 regardless of how far the player got — so using it for
                    // "acts reached" wrongly reports e.g. a floor-1 death as having reached act 3.
                    int actsReached = h.MapPointHistory?.Count ?? 0;
                    int floors = 0;
                    var summary = new RunSummary
                    {
                        Seed = h.Seed ?? "",
                        StartTime = h.StartTime,
                        Win = h.Win,
                        Abandoned = h.WasAbandoned,
                        Ascension = h.Ascension,
                        RunTime = h.RunTime,
                        ActsReached = actsReached,
                        GameMode = h.GameMode,
                    };
                    if (h.MapPointHistory != null)
                        foreach (var rooms in h.MapPointHistory)
                        {
                            if (rooms == null) continue;
                            floors += rooms.Count;
                            foreach (var entry in rooms)
                                ExtractCardFacts(entry, me.Id, summary);
                        }

                    // Final deck snapshot completes the "runs with" union (caveat 1: starter cards and
                    // anything not logged in CardsGained still show up here).
                    if (me.Deck != null)
                        foreach (var c in me.Deck)
                            if (c?.Id != null) summary.DeckCards.Add(new CardRef(c.Id, c.CurrentUpgradeLevel));

                    a.Total++;
                    if (h.Win) a.Wins++;
                    else if (h.WasAbandoned) a.Abandoned++;
                    else a.Deaths++;

                    // Per-mode tallies (Standard = official; everything else = Custom/Daily).
                    if (h.GameMode == GameMode.Standard)
                    {
                        a.StandardTotal++;
                        if (h.Win) a.StandardWins++;
                        else if (h.WasAbandoned) a.StandardAbandoned++;
                        else a.StandardDeaths++;
                    }
                    else
                    {
                        a.CustomTotal++;
                        if (h.Win) a.CustomWins++;
                        else if (h.WasAbandoned) a.CustomAbandoned++;
                        else a.CustomDeaths++;
                    }

                    a.SumRunTime += h.RunTime;
                    if (h.RunTime > a.MaxRunTime) a.MaxRunTime = h.RunTime;
                    if (h.Win && (a.FastestWin < 0 || h.RunTime < a.FastestWin))
                        a.FastestWin = h.RunTime;

                    if (actsReached > a.MaxAct) a.MaxAct = actsReached;
                    if (!a.ActReached.ContainsKey(actsReached)) a.ActReached[actsReached] = 0;
                    a.ActReached[actsReached]++;

                    if (floors > a.MaxFloor) a.MaxFloor = floors;
                    a.FloorReached[floors] = a.FloorReached.TryGetValue(floors, out var fc) ? fc + 1 : 1;

                    int asc = h.Ascension;
                    var cur = a.PerAscension.TryGetValue(asc, out var v) ? v : (0, 0);
                    a.PerAscension[asc] = h.Win ? (cur.Item1 + 1, cur.Item2) : (cur.Item1, cur.Item2 + 1);

                    summary.FloorsReached = floors;
                    a.Runs.Add(summary);
                }
                catch (Exception e)
                {
                    Log.Warn($"[CharacterManager] Could not aggregate run '{name}': {e.Message}");
                }
            }
            return a;
        }

        /// <summary>Back-compat overload: filter by game mode only.</summary>
        public CharacterAnalytics GetFiltered(GameModeFilter filter) =>
            GetFiltered(new RunFilter(filter));

        /// <summary>
        /// Returns a new aggregate containing only runs matching <paramref name="filter"/>
        /// (game mode + minimum ascension + most-recent-N window). Re-derives every distribution
        /// from the filtered run list and copies the surviving runs through, so the result is itself
        /// a fully-formed aggregate (windows, floor bars, exports all work off it) without re-reading
        /// any files.
        /// </summary>
        public CharacterAnalytics GetFiltered(RunFilter filter)
        {
            // Mode + ascension pass first, then take the most-recent N by start time.
            var matched = new List<RunSummary>(Runs.Count);
            foreach (var run in Runs)
            {
                if (!ModeMatchesFilter(run.GameMode, filter.Mode)) continue;
                if (run.Ascension < filter.MinAscension) continue;
                matched.Add(run);
            }

            if (filter.RecentCount > 0 && matched.Count > filter.RecentCount)
            {
                matched.Sort((x, y) => y.StartTime.CompareTo(x.StartTime)); // newest first
                matched.RemoveRange(filter.RecentCount, matched.Count - filter.RecentCount);
            }

            var r = new CharacterAnalytics();
            foreach (var run in matched)
            {
                r.Runs.Add(run);
                r.Total++;
                if (run.Win) r.Wins++;
                else if (run.Abandoned) r.Abandoned++;
                else r.Deaths++;

                // Per-mode tallies so the Custom/Daily section still works after filtering.
                if (run.GameMode == GameMode.Standard)
                {
                    r.StandardTotal++;
                    if (run.Win) r.StandardWins++;
                    else if (run.Abandoned) r.StandardAbandoned++;
                    else r.StandardDeaths++;
                }
                else
                {
                    r.CustomTotal++;
                    if (run.Win) r.CustomWins++;
                    else if (run.Abandoned) r.CustomAbandoned++;
                    else r.CustomDeaths++;
                }

                r.SumRunTime += run.RunTime;
                if (run.RunTime > r.MaxRunTime) r.MaxRunTime = run.RunTime;
                if (run.Win && (r.FastestWin < 0 || run.RunTime < r.FastestWin))
                    r.FastestWin = run.RunTime;
                if (run.ActsReached > r.MaxAct) r.MaxAct = run.ActsReached;
                r.ActReached[run.ActsReached] = r.ActReached.TryGetValue(run.ActsReached, out var ac) ? ac + 1 : 1;
                if (run.FloorsReached > r.MaxFloor) r.MaxFloor = run.FloorsReached;
                r.FloorReached[run.FloorsReached] = r.FloorReached.TryGetValue(run.FloorsReached, out var fc) ? fc + 1 : 1;
                var cur = r.PerAscension.TryGetValue(run.Ascension, out var v) ? v : (0, 0);
                r.PerAscension[run.Ascension] = run.Win ? (cur.Item1 + 1, cur.Item2) : (cur.Item1, cur.Item2 + 1);
            }
            return r;
        }

        /// <summary>
        /// Win rate over the most-recent <paramref name="n"/> runs (by start time), counting only
        /// decisive runs (wins + deaths; abandons excluded, matching the official rate). Pass
        /// <paramref name="n"/> &lt;= 0 for all runs. Returns (wins, decisive, ratePercent); rate is
        /// -1 when there are no decisive runs in the window. (M8 moving windows.)
        /// </summary>
        public (int wins, int decisive, double ratePct) WinRateWindow(int n)
        {
            // Newest first so "last N" is well-defined regardless of file iteration order.
            var ordered = new List<RunSummary>(Runs);
            ordered.Sort((x, y) => y.StartTime.CompareTo(x.StartTime));

            int wins = 0, decisive = 0;
            foreach (var run in ordered)
            {
                if (run.Abandoned) continue;           // not a decisive result
                decisive++;
                if (run.Win) wins++;
                if (n > 0 && decisive >= n) break;
            }
            double rate = decisive > 0 ? 100.0 * wins / decisive : -1.0;
            return (wins, decisive, rate);
        }

        /// <summary>
        /// Pulls this character's card events out of one floor entry into <paramref name="summary"/>.
        /// In multiplayer a floor has one <see cref="PlayerMapPointHistoryEntry"/> per player keyed by
        /// net id; we match <paramref name="playerId"/>, falling back to the sole entry in single-player.
        /// </summary>
        private static void ExtractCardFacts(MegaCrit.Sts2.Core.Runs.History.MapPointHistoryEntry? entry, ulong playerId, RunSummary summary)
        {
            var stats = entry?.PlayerStats;
            if (stats == null || stats.Count == 0) return;

            MegaCrit.Sts2.Core.Runs.PlayerMapPointHistoryEntry? pe = null;
            foreach (var ps in stats)
                if (ps.PlayerId == playerId) { pe = ps; break; }
            if (pe == null && stats.Count == 1) pe = stats[0];
            if (pe == null) return;

            if (pe.CardsGained != null)
                foreach (var c in pe.CardsGained)
                    if (c?.Id != null) summary.DeckCards.Add(new CardRef(c.Id, c.CurrentUpgradeLevel));

            if (pe.CardChoices != null)
                foreach (var ch in pe.CardChoices)
                    if (ch.Card?.Id != null)
                        summary.CardChoices.Add(new CardChoiceRec { Id = ch.Card.Id, Upgrade = ch.Card.CurrentUpgradeLevel, Picked = ch.wasPicked });

            if (pe.CardsRemoved != null)
                foreach (var c in pe.CardsRemoved)
                    if (c?.Id != null) summary.RemovedCards.Add(new CardRef(c.Id, c.CurrentUpgradeLevel));

            if (pe.UpgradedCards != null)
                foreach (var id in pe.UpgradedCards)
                    if (id != null) summary.UpgradedCardIds.Add(id);
        }

        /// <summary>
        /// Aggregates per-card stats over the (already filtered) run list (M9). Cheap, in-memory —
        /// re-runs on each filter change off the per-run facts captured during the deep parse, so the
        /// card lists honour the active game-mode / ascension / recent-N filter for free.
        ///
        /// <para>Counting rules: Offered/Picks are per-occurrence (a card offered twice in a run counts
        /// twice); RunsWith/WinsWith are de-duped once per run (caveat 3). RunsWith's source is the
        /// union of floor CardsGained and the final deck snapshot (caveat 1). The min-sample threshold
        /// (caveat 6) is applied by the UI when ranking, not here.</para>
        ///
        /// <para><paramref name="upgradeAware"/> = false collapses Strike and Strike+ into one entry
        /// (the default); true keeps upgraded variants separate.</para>
        /// </summary>
        public List<CardStat> ComputeCardStats(bool upgradeAware)
        {
            var map = new Dictionary<string, CardStat>();

            CardStat GetStat(ModelId id, int upgrade)
            {
                bool up = upgradeAware && upgrade > 0;
                string key = up ? id.Entry + "+" : id.Entry;
                if (!map.TryGetValue(key, out var st))
                {
                    st = new CardStat
                    {
                        Key = key,
                        Id = id,
                        Upgraded = up,
                        Name = NameResolver.Resolve(id) + (up ? "+" : ""),
                    };
                    map[key] = st;
                }
                return st;
            }

            foreach (var run in Runs)
            {
                // RunsWith / WinsWith — once per unique card key per run (caveat 3).
                var seen = new HashSet<string>();
                foreach (var c in run.DeckCards)
                {
                    var st = GetStat(c.Id, c.Upgrade);
                    if (seen.Add(st.Key)) { st.RunsWith++; if (run.Win) st.WinsWith++; }
                }
                // Offered / Picks — per occurrence.
                foreach (var ch in run.CardChoices)
                {
                    var st = GetStat(ch.Id, ch.Upgrade);
                    st.Offered++;
                    if (ch.Picked) st.Picks++;
                }
                foreach (var c in run.RemovedCards) GetStat(c.Id, c.Upgrade).Removed++;
                foreach (var id in run.UpgradedCardIds) GetStat(id, 0).Upgrades++;
            }

            return new List<CardStat>(map.Values);
        }

        private static bool ModeMatchesFilter(GameMode mode, GameModeFilter filter)
        {
            return filter switch
            {
                GameModeFilter.All => true,
                GameModeFilter.Standard => mode == GameMode.Standard,
                GameModeFilter.Custom => mode == GameMode.Custom,
                GameModeFilter.Daily => mode == GameMode.Daily,
                _ => true,
            };
        }
    }
}
