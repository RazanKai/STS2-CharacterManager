using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace CharacterManager.Config
{
    /// <summary>
    /// Persists which characters' card/relic pools are usable as sources for cross-character
    /// mechanics (Kaleidoscope, Colorful Philosophers, Splash, Prismatic Gem, Orobas/SeaGlass).
    /// Default: eligible (checked). Stored at
    /// {user_data}/mod_configs/charactermanager_crosssource.json.
    ///
    /// This is independent of <see cref="EnabledStore"/> (hide from select grid) and
    /// <see cref="RandomPoolStore"/> (eligible for Random draw). Three separate concepts,
    /// three separate stores.
    ///
    /// <para><b>Determinism.</b> These mechanics resolve per-player from that player's own
    /// <see cref="MegaCrit.Sts2.Core.Unlocks.UnlockState"/> at offer time. No cross-peer sync is
    /// required — filtering only affects what the local player's offers draw from. (In multiplayer
    /// each peer filters identically when generating their own offers, so results agree.)</para>
    /// </summary>
    public static class CrossSourceStore
    {
        /// <summary>Fires after a single character's cross-source eligibility is toggled.</summary>
        public static event Action<ModelId>? OnToggle;

        private static readonly Dictionary<string, bool> _choices = new();
        private static bool _loaded;

        private static string FilePath =>
            Path.Combine(OS.GetUserDataDir(), "mod_configs", "charactermanager_crosssource.json");

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
                Log.Warn("[CharacterManager] Failed to load cross-source config: " + e.Message);
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
                Log.Error("[CharacterManager] Failed to save cross-source config: " + e.Message);
            }
        }

        /// <param name="id">Character model id.</param>
        /// <param name="defaultEligible">True: characters are eligible cross-sources by default.</param>
        public static bool IsEligible(ModelId id, bool defaultEligible = true)
        {
            EnsureLoaded();
            return _choices.TryGetValue(id.ToString(), out var v) ? v : defaultEligible;
        }

        public static bool Toggle(ModelId id, bool defaultEligible = true)
        {
            bool now = !IsEligible(id, defaultEligible);
            _choices[id.ToString()] = now;
            Save();
            OnToggle?.Invoke(id);
            return now;
        }

        public static void Set(ModelId id, bool eligible)
        {
            EnsureLoaded();
            _choices[id.ToString()] = eligible;
            Save();
        }
    }
}