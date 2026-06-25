using System;
using System.Collections.Generic;
using System.Text;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace CharacterManager.Analytics
{
    /// <summary>
    /// Resolves a <see cref="ModelId"/> (as recorded in <c>.run</c> history — cards, relics, potions,
    /// encounters, monsters, events) to a human-readable display name (M8, plan §4b). Infrastructure
    /// for the M9+ ranked lists, where run history gives us raw ids and the UI needs titles.
    ///
    /// Resolution path, all against the game's own localization tables via
    /// <see cref="LocString.Exists"/> (never reproducing any reference mod's mapping):
    ///   1. If the id's category maps to a known table, try <c>{entry}.title</c> then <c>{entry}.name</c>.
    ///   2. Otherwise probe every known table with the same suffixes.
    ///   3. Fall back to a prettified entry string (e.g. <c>JAW_WORM_ELITE</c> → "Jaw Worm Elite"),
    ///      so unknown or modded ids degrade to something readable instead of a raw id or a crash.
    /// Results are memoised; localization lookups are cheap but run lists can repeat ids heavily.
    /// </summary>
    public static class NameResolver
    {
        // Tables that hold display titles for the entity kinds we read from run history.
        private static readonly string[] _allTables =
            { "cards", "relics", "potions", "encounters", "monsters", "events" };

        // Map a ModelId.Category to its most-likely table, so the common case is one lookup.
        private static readonly Dictionary<string, string> _categoryTable = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CARD"] = "cards",
            ["RELIC"] = "relics",
            ["POTION"] = "potions",
            ["ENCOUNTER"] = "encounters",
            ["MONSTER"] = "monsters",
            ["EVENT"] = "events",
        };

        private static readonly Dictionary<string, string> _cache = new();

        /// <summary>Resolves a <see cref="ModelId"/> to a display name (never null/empty).</summary>
        public static string Resolve(ModelId id)
        {
            if (id == null) return "—";
            return Resolve(id.Category, id.Entry);
        }

        /// <summary>Resolves a category/entry pair to a display name (never null/empty).</summary>
        public static string Resolve(string category, string entry)
        {
            entry ??= "";
            string cacheKey = (category ?? "") + "." + entry;
            if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

            string resolved = Lookup(category, entry) ?? Prettify(entry);
            _cache[cacheKey] = resolved;
            return resolved;
        }

        private static string? Lookup(string? category, string entry)
        {
            if (string.IsNullOrEmpty(entry)) return null;

            // Preferred table for this category first.
            if (category != null && _categoryTable.TryGetValue(category, out var preferred))
            {
                var hit = TryTable(preferred, entry);
                if (hit != null) return hit;
            }

            // Fall back to probing every table (handles tier-suffixed encounter/monster ids and
            // categories we didn't map explicitly).
            foreach (var table in _allTables)
            {
                var hit = TryTable(table, entry);
                if (hit != null) return hit;
            }
            return null;
        }

        private static string? TryTable(string table, string entry)
        {
            try
            {
                foreach (var suffix in new[] { ".title", ".name" })
                {
                    string key = entry + suffix;
                    if (LocString.Exists(table, key))
                    {
                        string text = new LocString(table, key).GetFormattedText();
                        if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
                    }
                }
            }
            catch
            {
                // Unknown table or formatting failure — treat as a miss and let the caller fall back.
            }
            return null;
        }

        /// <summary>
        /// Turns a SCREAMING_SNAKE entry into Title Case words: <c>JAW_WORM_ELITE</c> → "Jaw Worm Elite".
        /// Used only when localization has no entry (modded/unknown ids).
        /// </summary>
        private static string Prettify(string entry)
        {
            if (string.IsNullOrEmpty(entry)) return "—";

            var sb = new StringBuilder(entry.Length);
            bool newWord = true;
            foreach (char raw in entry)
            {
                if (raw == '_' || raw == '.' || raw == '-')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                    newWord = true;
                    continue;
                }
                char c = char.ToLowerInvariant(raw);
                sb.Append(newWord ? char.ToUpperInvariant(c) : c);
                newWord = false;
            }
            string s = sb.ToString().Trim();
            return s.Length == 0 ? "—" : s;
        }
    }
}
