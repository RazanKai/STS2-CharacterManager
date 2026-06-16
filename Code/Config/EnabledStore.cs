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
    /// Persists whether each custom character appears in the character-select screen.
    /// Default: enabled (shown). Stored at {user_data}/mod_configs/charactermanager_enabled.json.
    ///
    /// NOTE: A custom character only appears in character select if its own mod patches
    /// get_AllCharacters to include itself. We only HIDE it by setting Visible=false on
    /// the button the character mod already added — we don't add or remove the character.
    /// </summary>
    public static class EnabledStore
    {
        private static readonly Dictionary<string, bool> _choices = new();
        private static bool _loaded;

        private static string FilePath =>
            Path.Combine(OS.GetUserDataDir(), "mod_configs", "charactermanager_enabled.json");

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
                Log.Warn("[CharacterManager] Failed to load enabled config: " + e.Message);
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
                Log.Error("[CharacterManager] Failed to save enabled config: " + e.Message);
            }
        }

        /// <param name="id">Character model id.</param>
        /// <param name="defaultEnabled">True for installed characters (default shown).</param>
        public static bool IsEnabled(ModelId id, bool defaultEnabled = true)
        {
            EnsureLoaded();
            return _choices.TryGetValue(id.ToString(), out var v) ? v : defaultEnabled;
        }

        public static bool Toggle(ModelId id, bool defaultEnabled = true)
        {
            bool now = !IsEnabled(id, defaultEnabled);
            _choices[id.ToString()] = now;
            Save();
            return now;
        }

        public static void Set(ModelId id, bool enabled)
        {
            EnsureLoaded();
            _choices[id.ToString()] = enabled;
            Save();
        }
    }
}
