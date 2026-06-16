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
    /// Persists whether each character's stats block is visible on the Compendium stats screen.
    /// Default: visible. Stored at {user_data}/mod_configs/charactermanager_visibility.json.
    /// </summary>
    public static class VisibilityStore
    {
        private static readonly Dictionary<string, bool> _choices = new();
        private static bool _loaded;

        private static string FilePath =>
            Path.Combine(OS.GetUserDataDir(), "mod_configs", "charactermanager_visibility.json");

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
                Log.Warn("[CharacterManager] Failed to load visibility config: " + e.Message);
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
                Log.Error("[CharacterManager] Failed to save visibility config: " + e.Message);
            }
        }

        public static bool IsVisible(ModelId id, bool defaultVisible = true)
        {
            EnsureLoaded();
            return _choices.TryGetValue(id.ToString(), out var v) ? v : defaultVisible;
        }

        public static bool Toggle(ModelId id, bool defaultVisible = true)
        {
            bool now = !IsVisible(id, defaultVisible);
            _choices[id.ToString()] = now;
            Save();
            return now;
        }

        public static void Set(ModelId id, bool visible)
        {
            EnsureLoaded();
            _choices[id.ToString()] = visible;
            Save();
        }
    }
}
