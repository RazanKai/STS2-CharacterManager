using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CharacterManager.Analytics;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;

namespace CharacterManager.Analytics
{
    /// <summary>
    /// Result of an export: the directory written to and the individual files.
    /// <see cref="Ok"/> is false if nothing could be written.
    /// </summary>
    public sealed class ExportResult
    {
        public bool Ok;
        public string Directory = "";
        public string JsonPath = "";
        public string CsvPath = "";
        public string Error = "";
        /// <summary>Every file written this export (JSON + the per-run and per-aggregate CSVs).</summary>
        public List<string> Files = new();
    }

    /// <summary>
    /// Writes a character's aggregate stats and per-run summaries to JSON + CSV files (M5).
    /// Strictly read-only with respect to the game: it reads <see cref="CharacterStats"/> and the
    /// run-history files, and writes only into the mod's own config directory — it never touches
    /// the game's save files.
    ///
    /// Output: {user_data}/mod_configs/charactermanager_exports/{character}_{timestamp}.{json,csv}
    /// </summary>
    public static class StatsExporter
    {
        private static string ExportDir =>
            Path.Combine(OS.GetUserDataDir(), "mod_configs", "charactermanager_exports");

        public static ExportResult Export(CharacterModel character)
        {
            var res = new ExportResult { Directory = ExportDir };
            try
            {
                Directory.CreateDirectory(ExportDir);

                var analytics = CharacterAnalytics.Compute(character.Id);
                CharacterStats? stats = null;
                try { stats = SaveManager.Instance.Progress?.GetStatsForCharacter(character.Id); }
                catch (Exception e) { Log.Warn("[CharacterManager] export: GetStatsForCharacter failed: " + e.Message); }

                string stem = SafeFileStem(character) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                res.JsonPath = Path.Combine(ExportDir, stem + ".json");
                res.CsvPath = Path.Combine(ExportDir, stem + ".csv");

                File.WriteAllText(res.JsonPath, BuildJson(character, stats, analytics));
                res.Files.Add(res.JsonPath);

                File.WriteAllText(res.CsvPath, BuildCsv(analytics));
                res.Files.Add(res.CsvPath);

                // Per-aggregate CSVs (M13). Each only written when it has rows.
                WriteCsv(res, stem, "cards", BuildCardCsv(analytics.ComputeCardStats(false)));
                WriteCsv(res, stem, "encounters", BuildEncounterCsv(analytics.ComputeEncounterStats()));
                WriteCsv(res, stem, "relics", BuildPickCsv(analytics.ComputeRelicStats()));
                WriteCsv(res, stem, "potions", BuildPickCsv(analytics.ComputePotionStats()));
                WriteCsv(res, stem, "ancients", BuildPickCsv(analytics.ComputeAncientStats()));
                WriteCsv(res, stem, "deaths", BuildDeathCsv(analytics.ComputeDeathCauses()));

                res.Ok = true;
                Log.Info($"[CharacterManager] Exported {res.Files.Count} files to {ExportDir}");
            }
            catch (Exception e)
            {
                res.Ok = false;
                res.Error = e.Message;
                Log.Error("[CharacterManager] Stats export failed: " + e.Message);
            }
            return res;
        }

        // ─── JSON ────────────────────────────────────────────────────────────

        private static string BuildJson(CharacterModel c, CharacterStats? stats, CharacterAnalytics a)
        {
            var perAsc = new List<object>();
            var ascKeys = new List<int>(a.PerAscension.Keys);
            ascKeys.Sort();
            foreach (var asc in ascKeys)
            {
                var (w, l) = a.PerAscension[asc];
                perAsc.Add(new { ascension = asc, wins = w, losses = l });
            }

            var actDist = new List<object>();
            var actKeys = new List<int>(a.ActReached.Keys);
            actKeys.Sort();
            foreach (var act in actKeys)
                actDist.Add(new { actsReached = act, runs = a.ActReached[act] });

            var runs = new List<object>(a.Runs.Count);
            foreach (var r in a.Runs)
                runs.Add(new
                {
                    seed = r.Seed,
                    startTimeUnix = r.StartTime,
                    win = r.Win,
                    abandoned = r.Abandoned,
                    ascension = r.Ascension,
                    runTimeSeconds = r.RunTime,
                    actsReached = r.ActsReached,
                    floorsReached = r.FloorsReached,
                });

            object lifetime = stats == null
                ? null!
                : new
                {
                    wins = stats.TotalWins,
                    losses = stats.TotalLosses,
                    winRatePct = WinRatePct(stats.TotalWins, stats.TotalLosses),
                    maxAscension = stats.MaxAscension,
                    preferredAscension = stats.PreferredAscension,
                    bestWinStreak = stats.BestWinStreak,
                    currentWinStreak = stats.CurrentWinStreak,
                    fastestWinTimeSeconds = stats.FastestWinTime, // -1 = none
                    playtimeSeconds = stats.Playtime,
                    badgesEarned = stats.Badges?.Count ?? 0,
                };

            // ─── M13: richer per-entity aggregates ───────────────────────────
            var floorDist = new List<object>();
            var floorKeys = new List<int>(a.FloorReached.Keys);
            floorKeys.Sort();
            foreach (var f in floorKeys) floorDist.Add(new { floorsReached = f, runs = a.FloorReached[f] });

            var winWindows = new List<object>();
            foreach (int n in new[] { 10, 50, 100, 0 })
            {
                var (wins, decisive, rate) = a.WinRateWindow(n);
                winWindows.Add(new { window = n == 0 ? "all" : n.ToString(), wins, decisiveRuns = decisive, winRatePct = decisive > 0 ? (double?)Math.Round(rate, 1) : null });
            }

            var cards = new List<object>();
            var cardStats = a.ComputeCardStats(false);
            cardStats.Sort((x, y) => y.Picks.CompareTo(x.Picks));
            foreach (var s in cardStats)
                cards.Add(new
                {
                    id = s.Id.ToString(), name = s.Name,
                    offered = s.Offered, picks = s.Picks, pickRatePct = Pct(s.PickRatePct),
                    runsWith = s.RunsWith, winsWith = s.WinsWith, winRatePct = Pct(s.WinRatePct),
                    removed = s.Removed, upgrades = s.Upgrades,
                });

            var encounters = new List<object>();
            var encStats = a.ComputeEncounterStats();
            encStats.Sort((x, y) => y.Fights.CompareTo(x.Fights));
            foreach (var s in encStats)
                encounters.Add(new
                {
                    id = s.Id.ToString(), name = s.Name, tier = s.Tier.ToString(),
                    fights = s.Fights, deaths = s.Deaths, deathRatePct = Pct(s.DeathRatePct),
                    avgDamage = Math.Round(s.AvgDamage, 1), p80Damage = CharacterAnalytics.Percentile(s.Damages, 80),
                });

            var tiers = new List<object>();
            foreach (var s in a.ComputeTierStats())
                tiers.Add(new { tier = s.Tier.ToString(), fights = s.Fights, deaths = s.Deaths, deathRatePct = Pct(s.DeathRatePct), avgDamage = Math.Round(s.AvgDamage, 1) });

            var deathCauses = new List<object>();
            var dcStats = a.ComputeDeathCauses();
            dcStats.Sort((x, y) => y.Count.CompareTo(x.Count));
            foreach (var s in dcStats)
                deathCauses.Add(new { name = s.Name, source = s.Source.ToString(), count = s.Count });

            var payload = new
            {
                exportedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exportedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                character = new
                {
                    id = c.Id.ToString(),
                    title = SafeTitle(c),
                    source = SourceText(c),
                    isBaseGame = CharacterHelper.IsBaseCharacter(c.Id),
                },
                lifetimeStats = lifetime,
                runHistoryAggregate = new
                {
                    runsRecorded = a.Total,
                    wins = a.Wins,
                    deaths = a.Deaths,
                    abandoned = a.Abandoned,
                    highestActReached = a.MaxAct,
                    highestFloorReached = a.MaxFloor,
                    averageRunTimeSeconds = a.AvgRunTime,
                    longestRunSeconds = a.MaxRunTime,
                    fastestClearSeconds = a.FastestWin, // -1 = none
                    perAscension = perAsc,
                    actReachedDistribution = actDist,
                    floorReachedDistribution = floorDist,
                    winRateWindows = winWindows,
                },
                cards = cards,
                encounters = encounters,
                combatTiers = tiers,
                deathCauses = deathCauses,
                relics = PickList(a.ComputeRelicStats()),
                potions = PickList(a.ComputePotionStats()),
                ancients = PickList(a.ComputeAncientStats()),
                runs = runs,
            };

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }

        // ─── CSV (one row per run) ───────────────────────────────────────────

        private static string BuildCsv(CharacterAnalytics a)
        {
            var sb = new StringBuilder();
            sb.AppendLine("seed,start_time_unix,result,ascension,run_time_seconds,acts_reached,floors_reached");
            foreach (var r in a.Runs)
            {
                string result = r.Win ? "win" : (r.Abandoned ? "abandoned" : "death");
                sb.Append(CsvField(r.Seed)).Append(',')
                  .Append(r.StartTime.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(result).Append(',')
                  .Append(r.Ascension.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.RunTime.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.ActsReached.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.FloorsReached.ToString(CultureInfo.InvariantCulture))
                  .Append('\n');
            }
            return sb.ToString();
        }

        // ─── M13 aggregate JSON/CSV builders ─────────────────────────────────

        /// <summary>Rounds a rate to 1 dp, or null when the source had no samples (rate &lt; 0).</summary>
        private static double? Pct(double v) => v < 0 ? (double?)null : Math.Round(v, 1);

        /// <summary>Same as <see cref="Pct"/> but for CSV cells — empty string when no samples.</summary>
        private static string PctCsv(double v) => v < 0 ? "" : Math.Round(v, 1).ToString(CultureInfo.InvariantCulture);

        /// <summary>JSON rows for a relic/potion/ancient pick-stat list (sorted by runs-with).</summary>
        private static List<object> PickList(List<PickStat> stats)
        {
            stats.Sort((x, y) => y.RunsWith.CompareTo(x.RunsWith));
            var list = new List<object>(stats.Count);
            foreach (var s in stats)
                list.Add(new
                {
                    id = s.Id.ToString(), key = s.Key, name = s.Name,
                    offered = s.Offered, picks = s.Picks, pickRatePct = Pct(s.PickRatePct),
                    runsWith = s.RunsWith, winsWith = s.WinsWith, winRatePct = Pct(s.WinRatePct),
                });
            return list;
        }

        private static void WriteCsv(ExportResult res, string stem, string suffix, string? content)
        {
            if (string.IsNullOrEmpty(content)) return;
            string path = Path.Combine(ExportDir, $"{stem}_{suffix}.csv");
            File.WriteAllText(path, content);
            res.Files.Add(path);
        }

        private static string? BuildCardCsv(List<CardStat> stats)
        {
            if (stats.Count == 0) return null;
            stats.Sort((x, y) => y.Picks.CompareTo(x.Picks));
            var sb = new StringBuilder();
            sb.AppendLine("name,id,offered,picks,pick_rate_pct,runs_with,wins_with,win_rate_pct,removed,upgrades");
            foreach (var s in stats)
                sb.Append(CsvField(s.Name)).Append(',').Append(CsvField(s.Id.ToString())).Append(',')
                  .Append(s.Offered).Append(',').Append(s.Picks).Append(',').Append(PctCsv(s.PickRatePct)).Append(',')
                  .Append(s.RunsWith).Append(',').Append(s.WinsWith).Append(',').Append(PctCsv(s.WinRatePct)).Append(',')
                  .Append(s.Removed).Append(',').Append(s.Upgrades).Append('\n');
            return sb.ToString();
        }

        private static string? BuildEncounterCsv(List<EncounterStat> stats)
        {
            if (stats.Count == 0) return null;
            stats.Sort((x, y) => y.Fights.CompareTo(x.Fights));
            var sb = new StringBuilder();
            sb.AppendLine("name,id,tier,fights,deaths,death_rate_pct,avg_damage,p80_damage");
            foreach (var s in stats)
                sb.Append(CsvField(s.Name)).Append(',').Append(CsvField(s.Id.ToString())).Append(',')
                  .Append(s.Tier).Append(',').Append(s.Fights).Append(',').Append(s.Deaths).Append(',')
                  .Append(PctCsv(s.DeathRatePct)).Append(',')
                  .Append(Math.Round(s.AvgDamage, 1).ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(CharacterAnalytics.Percentile(s.Damages, 80)).Append('\n');
            return sb.ToString();
        }

        private static string? BuildPickCsv(List<PickStat> stats)
        {
            if (stats.Count == 0) return null;
            stats.Sort((x, y) => y.RunsWith.CompareTo(x.RunsWith));
            var sb = new StringBuilder();
            sb.AppendLine("name,id,key,offered,picks,pick_rate_pct,runs_with,wins_with,win_rate_pct");
            foreach (var s in stats)
                sb.Append(CsvField(s.Name)).Append(',').Append(CsvField(s.Id.ToString())).Append(',')
                  .Append(CsvField(s.Key)).Append(',')
                  .Append(s.Offered).Append(',').Append(s.Picks).Append(',').Append(PctCsv(s.PickRatePct)).Append(',')
                  .Append(s.RunsWith).Append(',').Append(s.WinsWith).Append(',').Append(PctCsv(s.WinRatePct)).Append('\n');
            return sb.ToString();
        }

        private static string? BuildDeathCsv(List<DeathCauseStat> stats)
        {
            if (stats.Count == 0) return null;
            stats.Sort((x, y) => y.Count.CompareTo(x.Count));
            var sb = new StringBuilder();
            sb.AppendLine("name,source,count");
            foreach (var s in stats)
                sb.Append(CsvField(s.Name)).Append(',').Append(s.Source).Append(',').Append(s.Count).Append('\n');
            return sb.ToString();
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static double WinRatePct(int wins, int losses)
        {
            int total = wins + losses;
            return total <= 0 ? 0 : Math.Round(100.0 * wins / total, 1);
        }

        private static string SafeTitle(CharacterModel c)
        {
            try { return c.Title.GetFormattedText(); }
            catch { return c.Id.ToString(); }
        }

        private static string SourceText(CharacterModel c)
        {
            if (CharacterHelper.IsBaseCharacter(c.Id)) return "Base game";
            try
            {
                var mod = CharacterHelper.GetSourceMod(c);
                if (mod == null) return "Unknown mod";
                return $"{mod.manifest.name} v{mod.manifest.version}";
            }
            catch { return "Unknown mod"; }
        }

        /// <summary>Filesystem-safe file stem derived from the character id (e.g. CHARACTER.RYOSHU → character_ryoshu).</summary>
        private static string SafeFileStem(CharacterModel c)
        {
            string raw;
            try { raw = c.Id.ToString(); }
            catch { raw = "character"; }

            var sb = new StringBuilder(raw.Length);
            foreach (char ch in raw.ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');

            string s = sb.ToString().Trim('_');
            while (s.Contains("__")) s = s.Replace("__", "_");
            return string.IsNullOrEmpty(s) ? "character" : s;
        }

        /// <summary>Quotes a CSV field if it contains a comma, quote, or newline.</summary>
        private static string CsvField(string value)
        {
            value ??= "";
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
