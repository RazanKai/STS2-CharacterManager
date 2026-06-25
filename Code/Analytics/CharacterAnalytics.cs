using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
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

    /// <summary>One combat fought in a run: encounter id, tier, floor damage taken, turns (M10).</summary>
    public readonly struct CombatRec
    {
        public readonly ModelId Id;
        public readonly RoomType Tier;   // Monster / Elite / Boss
        public readonly int Damage;      // player's DamageTaken on that floor
        public readonly int Turns;
        public CombatRec(ModelId id, RoomType tier, int damage, int turns)
        {
            Id = id; Tier = tier; Damage = damage; Turns = turns;
        }
    }

    /// <summary>How a run ended (M10, caveat 4). None = the run was a win.</summary>
    public enum DeathSource { None, Combat, Event, Abandoned, Unknown }

    /// <summary>Death attribution for one run (M10).</summary>
    public struct DeathInfo
    {
        public DeathSource Source;
        public ModelId Id;     // encounter/event id when Source is Combat/Event
        public int Act;        // act reached when the run ended
    }

    /// <summary>Aggregated combat stats for one encounter across a run set (M10).</summary>
    public sealed class EncounterStat
    {
        public string Name = "";
        public ModelId Id = ModelId.none;
        public RoomType Tier;
        public int Fights;
        public int Deaths;
        public long SumDamage;
        public readonly List<int> Damages = new();   // for percentiles

        public double DeathRatePct => Fights > 0 ? 100.0 * Deaths / Fights : -1.0;
        public double AvgDamage => Fights > 0 ? (double)SumDamage / Fights : 0.0;
    }

    /// <summary>Aggregated combat totals for one tier (Monster/Elite/Boss) across a run set (M10).</summary>
    public sealed class TierStat
    {
        public RoomType Tier;
        public int Fights;
        public int Deaths;
        public long SumDamage;
        public double DeathRatePct => Fights > 0 ? 100.0 * Deaths / Fights : -1.0;
        public double AvgDamage => Fights > 0 ? (double)SumDamage / Fights : 0.0;
    }

    /// <summary>A single death-cause tally across a run set (M10).</summary>
    public sealed class DeathCauseStat
    {
        public string Name = "";
        public DeathSource Source;
        public int Count;
    }

    /// <summary>One ancient (Neow/elder) option offered in a run, with whether it was taken (M11).</summary>
    public sealed class AncientRec
    {
        public string Key = "";    // Title.LocEntryKey — stable identity (caveat 7)
        public string Name = "";   // localized display text
        public bool Chosen;
    }

    /// <summary>
    /// Aggregated pick / win-rate stats for one relic, potion, or ancient option across a run set
    /// (M11). Offered/Picks are per-occurrence; RunsWith/WinsWith are de-duped once per run.
    /// </summary>
    public sealed class PickStat
    {
        public string Key = "";
        public string Name = "";
        public ModelId Id = ModelId.none;   // ModelId.none for ancients (identified by Key)
        public int Offered;
        public int Picks;
        public int RunsWith;
        public int WinsWith;

        public double PickRatePct => Offered > 0 ? 100.0 * Picks / Offered : -1.0;
        public double WinRatePct => RunsWith > 0 ? 100.0 * WinsWith / RunsWith : -1.0;
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

        // ─── Per-run combat facts (M10) ──────────────────────────────────────
        /// <summary>Every combat fought this run, in floor order (deepest is last).</summary>
        public List<CombatRec> Combats = new();
        /// <summary>How this run ended (death attribution, caveat 4).</summary>
        public DeathInfo Death;

        // ─── Per-run inventory facts (M11) ───────────────────────────────────
        /// <summary>Relics offered in a choice this run (id + picked), per-occurrence.</summary>
        public List<(ModelId id, bool picked)> RelicChoices = new();
        /// <summary>Potions offered in a choice this run (id + picked), per-occurrence.</summary>
        public List<(ModelId id, bool picked)> PotionChoices = new();
        /// <summary>Relics the player had: union of final Relics, BoughtRelics, and chosen choices.</summary>
        public List<ModelId> RelicsOwned = new();
        /// <summary>Potions the player had: union of final Potions, BoughtPotions, and chosen choices.</summary>
        public List<ModelId> PotionsOwned = new();
        /// <summary>Ancient (Neow/elder) options offered this run, with their chosen flag.</summary>
        public List<AncientRec> Ancients = new();
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
                            {
                                ExtractCardFacts(entry, me.Id, summary);
                                ExtractCombatFacts(entry, me.Id, summary);
                                ExtractInventoryFacts(entry, me.Id, summary);
                            }
                        }

                    // Final deck snapshot completes the "runs with" union (caveat 1: starter cards and
                    // anything not logged in CardsGained still show up here).
                    if (me.Deck != null)
                        foreach (var c in me.Deck)
                            if (c?.Id != null) summary.DeckCards.Add(new CardRef(c.Id, c.CurrentUpgradeLevel));

                    // Final relic/potion snapshots complete the "runs with" union for M11.
                    if (me.Relics != null)
                        foreach (var r in me.Relics)
                            if (r?.Id != null) summary.RelicsOwned.Add(r.Id);
                    if (me.Potions != null)
                        foreach (var pot in me.Potions)
                            if (pot?.Id != null) summary.PotionsOwned.Add(pot.Id);

                    summary.Death = ResolveDeath(h, summary);

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

        /// <summary>Records this character's combat (if any) at one floor into <paramref name="summary"/> (M10).</summary>
        private static void ExtractCombatFacts(MapPointHistoryEntry? entry, ulong playerId, RunSummary summary)
        {
            var roomsList = entry?.Rooms;
            if (roomsList == null || roomsList.Count == 0) return;

            MapPointRoomHistoryEntry? combat = null;
            foreach (var r in roomsList)
                if (r != null && r.RoomType.IsCombatRoom()) { combat = r; break; }
            if (combat == null) return;

            int dmg = 0;
            var stats = entry!.PlayerStats;
            if (stats != null)
            {
                PlayerMapPointHistoryEntry? pe = null;
                foreach (var ps in stats)
                    if (ps.PlayerId == playerId) { pe = ps; break; }
                if (pe == null && stats.Count == 1) pe = stats[0];
                if (pe != null) dmg = pe.DamageTaken;
            }

            summary.Combats.Add(new CombatRec(combat.ModelId ?? ModelId.none, combat.RoomType, dmg, combat.TurnsTaken));
        }

        /// <summary>
        /// Attributes how a run ended (M10, caveat 4). Order: win → abandoned → killed-by-encounter →
        /// killed-by-event → deepest combat fought → unknown. Abandoned is checked early because the
        /// flag is authoritative and shouldn't be mis-attributed to the last fight.
        /// </summary>
        private static DeathInfo ResolveDeath(RunHistory h, RunSummary summary)
        {
            var d = new DeathInfo { Source = DeathSource.None, Id = ModelId.none, Act = summary.ActsReached };
            if (h.Win) return d;
            if (h.WasAbandoned) { d.Source = DeathSource.Abandoned; return d; }

            if (h.KilledByEncounter != null && h.KilledByEncounter != ModelId.none)
            { d.Source = DeathSource.Combat; d.Id = h.KilledByEncounter; return d; }

            if (h.KilledByEvent != null && h.KilledByEvent != ModelId.none)
            { d.Source = DeathSource.Event; d.Id = h.KilledByEvent; return d; }

            if (summary.Combats.Count > 0)
            { d.Source = DeathSource.Combat; d.Id = summary.Combats[summary.Combats.Count - 1].Id; return d; }

            d.Source = DeathSource.Unknown;
            return d;
        }

        /// <summary>Per-encounter combat aggregation over the filtered run list (M10).</summary>
        public List<EncounterStat> ComputeEncounterStats()
        {
            var map = new Dictionary<string, EncounterStat>();

            EncounterStat GetStat(ModelId id, RoomType tier)
            {
                var mid = id ?? ModelId.none;
                string key = mid.Entry;
                if (!map.TryGetValue(key, out var st))
                {
                    st = new EncounterStat { Id = mid, Tier = tier, Name = NameResolver.Resolve(mid) };
                    map[key] = st;
                }
                return st;
            }

            foreach (var run in Runs)
            {
                foreach (var c in run.Combats)
                {
                    var st = GetStat(c.Id, c.Tier);
                    st.Fights++;
                    st.SumDamage += c.Damage;
                    st.Damages.Add(c.Damage);
                }

                if (run.Death.Source == DeathSource.Combat && run.Death.Id != ModelId.none)
                {
                    RoomType tier = TierOfDeath(run);
                    GetStat(run.Death.Id, tier).Deaths++;
                }
            }
            return new List<EncounterStat>(map.Values);
        }

        /// <summary>Combat totals grouped by tier (Monster/Elite/Boss) over the filtered run list (M10).</summary>
        public List<TierStat> ComputeTierStats()
        {
            var map = new Dictionary<RoomType, TierStat>();
            TierStat Get(RoomType t)
            {
                if (!map.TryGetValue(t, out var s)) { s = new TierStat { Tier = t }; map[t] = s; }
                return s;
            }

            foreach (var run in Runs)
            {
                foreach (var c in run.Combats)
                {
                    var s = Get(c.Tier);
                    s.Fights++;
                    s.SumDamage += c.Damage;
                }
                if (run.Death.Source == DeathSource.Combat && run.Death.Id != ModelId.none && run.Combats.Count > 0)
                    Get(TierOfDeath(run)).Deaths++;
            }
            return new List<TierStat>(map.Values);
        }

        /// <summary>Death-cause tallies over the filtered run list (M10).</summary>
        public List<DeathCauseStat> ComputeDeathCauses()
        {
            var map = new Dictionary<string, DeathCauseStat>();
            foreach (var run in Runs)
            {
                var d = run.Death;
                if (d.Source == DeathSource.None) continue;

                string name = d.Source switch
                {
                    DeathSource.Combat => NameResolver.Resolve(d.Id),
                    DeathSource.Event => NameResolver.Resolve(d.Id),
                    DeathSource.Abandoned => "Abandoned",
                    _ => "Unknown",
                };
                string key = d.Source + "|" + name;
                if (!map.TryGetValue(key, out var s)) { s = new DeathCauseStat { Name = name, Source = d.Source }; map[key] = s; }
                s.Count++;
            }
            return new List<DeathCauseStat>(map.Values);
        }

        /// <summary>Tier of a run's death encounter, looked up from the combats it fought (default Monster).</summary>
        private static RoomType TierOfDeath(RunSummary run)
        {
            for (int i = run.Combats.Count - 1; i >= 0; i--)
                if (run.Combats[i].Id == run.Death.Id) return run.Combats[i].Tier;
            return RoomType.Monster;
        }

        /// <summary>Records this character's relic/potion/ancient choices at one floor (M11).</summary>
        private static void ExtractInventoryFacts(MapPointHistoryEntry? entry, ulong playerId, RunSummary summary)
        {
            var stats = entry?.PlayerStats;
            if (stats == null || stats.Count == 0) return;

            PlayerMapPointHistoryEntry? pe = null;
            foreach (var ps in stats)
                if (ps.PlayerId == playerId) { pe = ps; break; }
            if (pe == null && stats.Count == 1) pe = stats[0];
            if (pe == null) return;

            if (pe.RelicChoices != null)
                foreach (var rc in pe.RelicChoices)
                    if (rc.choice != null && rc.choice != ModelId.none)
                    {
                        summary.RelicChoices.Add((rc.choice, rc.wasPicked));
                        if (rc.wasPicked) summary.RelicsOwned.Add(rc.choice);
                    }

            if (pe.PotionChoices != null)
                foreach (var pc in pe.PotionChoices)
                    if (pc.choice != null && pc.choice != ModelId.none)
                    {
                        summary.PotionChoices.Add((pc.choice, pc.wasPicked));
                        if (pc.wasPicked) summary.PotionsOwned.Add(pc.choice);
                    }

            if (pe.BoughtRelics != null)
                foreach (var id in pe.BoughtRelics)
                    if (id != null && id != ModelId.none) summary.RelicsOwned.Add(id);
            if (pe.BoughtPotions != null)
                foreach (var id in pe.BoughtPotions)
                    if (id != null && id != ModelId.none) summary.PotionsOwned.Add(id);

            if (pe.AncientChoices != null)
                foreach (var a in pe.AncientChoices)
                {
                    if (a?.Title == null) continue;
                    string key = a.Title.LocEntryKey ?? "";
                    if (string.IsNullOrEmpty(key)) continue;
                    string name;
                    try { name = a.Title.GetFormattedText(); }
                    catch { name = key; }
                    summary.Ancients.Add(new AncientRec { Key = key, Name = name, Chosen = a.WasChosen });
                }
        }

        /// <summary>Per-relic or per-potion pick/win-rate aggregation over the filtered runs (M11).</summary>
        private List<PickStat> ComputeOwnedChoiceStats(
            Func<RunSummary, List<(ModelId id, bool picked)>> choices,
            Func<RunSummary, List<ModelId>> owned)
        {
            var map = new Dictionary<string, PickStat>();
            PickStat Get(ModelId id)
            {
                string key = id.Entry;
                if (!map.TryGetValue(key, out var st))
                {
                    st = new PickStat { Key = key, Id = id, Name = NameResolver.Resolve(id) };
                    map[key] = st;
                }
                return st;
            }

            foreach (var run in Runs)
            {
                var seen = new HashSet<string>();
                foreach (var id in owned(run))
                {
                    if (id == ModelId.none) continue;
                    var st = Get(id);
                    if (seen.Add(st.Key)) { st.RunsWith++; if (run.Win) st.WinsWith++; }
                }
                foreach (var (id, picked) in choices(run))
                {
                    if (id == ModelId.none) continue;
                    var st = Get(id);
                    st.Offered++;
                    if (picked) st.Picks++;
                }
            }
            return new List<PickStat>(map.Values);
        }

        /// <summary>Relic pick/win-rate stats over the filtered runs (M11).</summary>
        public List<PickStat> ComputeRelicStats() =>
            ComputeOwnedChoiceStats(r => r.RelicChoices, r => r.RelicsOwned);

        /// <summary>Potion pick/win-rate stats over the filtered runs (M11).</summary>
        public List<PickStat> ComputePotionStats() =>
            ComputeOwnedChoiceStats(r => r.PotionChoices, r => r.PotionsOwned);

        /// <summary>
        /// Ancient (Neow/elder) pick/win-rate stats over the filtered runs (M11). Identity is the
        /// option's <see cref="LocString.LocEntryKey"/> (caveat 7). Offered/Picks are per-occurrence;
        /// RunsWith/WinsWith count runs where the option was taken (de-duped).
        /// </summary>
        public List<PickStat> ComputeAncientStats()
        {
            var map = new Dictionary<string, PickStat>();
            PickStat Get(string key, string name)
            {
                if (!map.TryGetValue(key, out var st))
                {
                    st = new PickStat { Key = key, Name = string.IsNullOrEmpty(name) ? key : name };
                    map[key] = st;
                }
                return st;
            }

            foreach (var run in Runs)
            {
                var chosenSeen = new HashSet<string>();
                foreach (var a in run.Ancients)
                {
                    if (string.IsNullOrEmpty(a.Key)) continue;
                    var st = Get(a.Key, a.Name);
                    st.Offered++;
                    if (a.Chosen)
                    {
                        st.Picks++;
                        if (chosenSeen.Add(a.Key)) { st.RunsWith++; if (run.Win) st.WinsWith++; }
                    }
                }
            }
            return new List<PickStat>(map.Values);
        }

        /// <summary>Nearest-rank percentile of an int sample (0 if empty). Copies + sorts; caller-sized lists are small.</summary>
        public static int Percentile(List<int> values, double p)
        {
            if (values == null || values.Count == 0) return 0;
            var arr = new List<int>(values);
            arr.Sort();
            int idx = (int)Math.Ceiling(p / 100.0 * arr.Count) - 1;
            if (idx < 0) idx = 0;
            if (idx >= arr.Count) idx = arr.Count - 1;
            return arr[idx];
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
