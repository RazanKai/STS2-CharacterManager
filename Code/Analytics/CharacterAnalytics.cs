using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace CharacterManager.Analytics
{
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
        public float MaxRunTime;
        public double SumRunTime;
        public float FastestWin = -1f;
        public double AvgRunTime => Total > 0 ? SumRunTime / Total : 0;

        public readonly Dictionary<int, (int w, int l)> PerAscension = new();
        public readonly Dictionary<int, int> ActReached = new();
        public readonly List<RunSummary> Runs = new();

        /// <summary>Loads and aggregates every run-history file for the given character.</summary>
        public static CharacterAnalytics Compute(ModelId characterId)
        {
            var a = new CharacterAnalytics();

            List<string>? names = null;
            try { names = SaveManager.Instance.GetAllRunHistoryNames(); }
            catch (Exception e) { Log.Warn("[CharacterManager] GetAllRunHistoryNames failed: " + e.Message); }
            if (names == null) return a;

            foreach (var name in names)
            {
                try
                {
                    var result = SaveManager.Instance.LoadRunHistory(name);
                    if (!result.Success) continue;
                    RunHistory? h = result.SaveData;
                    if (h == null) continue;

                    bool matches = false;
                    foreach (var p in h.Players)
                        if (p.Character == characterId) { matches = true; break; }
                    if (!matches) continue;

                    int actsReached = h.Acts?.Count ?? 0;
                    int floors = 0;
                    if (h.MapPointHistory != null)
                        foreach (var rooms in h.MapPointHistory)
                            floors += rooms?.Count ?? 0;

                    a.Total++;
                    if (h.Win) a.Wins++;
                    else if (h.WasAbandoned) a.Abandoned++;
                    else a.Deaths++;

                    a.SumRunTime += h.RunTime;
                    if (h.RunTime > a.MaxRunTime) a.MaxRunTime = h.RunTime;
                    if (h.Win && (a.FastestWin < 0 || h.RunTime < a.FastestWin))
                        a.FastestWin = h.RunTime;

                    if (actsReached > a.MaxAct) a.MaxAct = actsReached;
                    if (!a.ActReached.ContainsKey(actsReached)) a.ActReached[actsReached] = 0;
                    a.ActReached[actsReached]++;

                    if (floors > a.MaxFloor) a.MaxFloor = floors;

                    int asc = h.Ascension;
                    var cur = a.PerAscension.TryGetValue(asc, out var v) ? v : (0, 0);
                    a.PerAscension[asc] = h.Win ? (cur.Item1 + 1, cur.Item2) : (cur.Item1, cur.Item2 + 1);

                    a.Runs.Add(new RunSummary
                    {
                        Seed = h.Seed ?? "",
                        StartTime = h.StartTime,
                        Win = h.Win,
                        Abandoned = h.WasAbandoned,
                        Ascension = asc,
                        RunTime = h.RunTime,
                        ActsReached = actsReached,
                        FloorsReached = floors,
                    });
                }
                catch (Exception e)
                {
                    Log.Warn($"[CharacterManager] Could not aggregate run '{name}': {e.Message}");
                }
            }
            return a;
        }
    }
}
