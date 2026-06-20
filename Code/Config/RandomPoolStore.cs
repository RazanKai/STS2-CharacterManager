using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;

namespace CharacterManager.Config
{
    /// <summary>
    /// Persists which characters are eligible for the <b>Random</b> character draw on the
    /// character-select screen. Default: in-pool (checked). Stored at
    /// {user_data}/mod_configs/charactermanager_randompool.json.
    ///
    /// <para>This is independent of <see cref="EnabledStore"/> (the in-select hide toggle): a
    /// character can be hidden from the manual select grid yet still be drawable at random, or
    /// vice-versa. They are deliberately separate concepts with separate stores.</para>
    ///
    /// <para><b>Determinism.</b> The vanilla random pick is
    /// <c>rng.NextItem(ModelDb.AllCharacters)</c> — one rng draw indexing into that enumerable.
    /// <see cref="GetPool"/> derives strictly from <c>ModelDb.AllCharacters</c> (runtime contents,
    /// order preserved) and only removes unchecked ids. When every box is checked the returned
    /// list is identical (same members, same order, same count), so <c>NextItem</c> yields the
    /// same character for a given seed as vanilla. Filtering changes the count and therefore which
    /// character a seed produces — expected for singleplayer, not a seed bug.</para>
    /// </summary>
    public static class RandomPoolStore
    {
        /// <summary>Fires after a character's pool membership is toggled so live UI can react.</summary>
        public static event Action<ModelId>? OnToggle;
        private static readonly Dictionary<string, bool> _choices = new();
        private static bool _loaded;

        private static string FilePath =>
            Path.Combine(OS.GetUserDataDir(), "mod_configs", "charactermanager_randompool.json");

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (File.Exists(FilePath))
                {
                    var map = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(FilePath));
                    _choices.Clear();
                    if (map != null)
                        foreach (var kvp in map) _choices[kvp.Key] = kvp.Value;
                }
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] Failed to load random-pool config: " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath,
                    JsonSerializer.Serialize(_choices, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e)
            {
                Log.Error("[CharacterManager] Failed to save random-pool config: " + e.Message);
            }
        }

        /// <param name="id">Character model id.</param>
        /// <param name="defaultIn">True: characters are in the random pool by default.</param>
        public static bool IsInPool(ModelId id, bool defaultIn = true)
        {
            EnsureLoaded();
            return _choices.TryGetValue(id.ToString(), out var v) ? v : defaultIn;
        }

        public static bool Toggle(ModelId id, bool defaultIn = true)
        {
            bool now = !IsInPool(id, defaultIn);
            _choices[id.ToString()] = now;
            Save();
            OnToggle?.Invoke(id);
            return now;
        }

        public static void Set(ModelId id, bool inPool)
        {
            EnsureLoaded();
            _choices[id.ToString()] = inPool;
            Save();
        }

        /// <summary>
        /// The set of characters the Random option may draw from, in the same order
        /// <c>ModelDb.AllCharacters</c> presents them. Unchecked characters are removed. If the
        /// filter would empty the pool, falls back to the full roster so <c>NextItem</c> is never
        /// handed an empty set (which would resolve Random to a null character).
        ///
        /// <para>Called from the <c>BeginRunLocally</c> transpiler in place of the original
        /// <c>ModelDb.AllCharacters</c> load. Hardened against any failure: on exception it returns
        /// the unfiltered roster, preserving vanilla behavior.</para>
        /// </summary>
        public static IEnumerable<CharacterModel> GetPool()
        {
            List<CharacterModel> all;
            try
            {
                all = ModelDb.AllCharacters?.Where(c => c != null).ToList() ?? new List<CharacterModel>();
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] random pool: reading AllCharacters failed (" + e.Message + "); leaving vanilla draw.");
                return ModelDb.AllCharacters;
            }

            if (all.Count == 0) return all;

            try
            {
                EnsureLoaded();
                var filtered = all.Where(c => IsInPool(c.Id)).ToList();
                if (filtered.Count == 0)
                {
                    Log.Warn("[CharacterManager] random pool empty after filter; falling back to the full roster.");
                    return all;
                }
                if (filtered.Count != all.Count)
                    Log.Info($"[CharacterManager] random pool: drawing from {filtered.Count}/{all.Count} character(s).");
                return filtered;
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] random pool: filter failed (" + e.Message + "); using full roster.");
                return all;
            }
        }
    }
}
