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
                File.WriteAllText(res.CsvPath, BuildCsv(analytics));

                res.Ok = true;
                Log.Info($"[CharacterManager] Exported stats to {res.JsonPath}");
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
                },
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
