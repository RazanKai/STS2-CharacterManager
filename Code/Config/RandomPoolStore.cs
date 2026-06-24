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
    /// <para><b>Determinism (singleplayer).</b> The vanilla random pick is
    /// <c>rng.NextItem(&lt;roster&gt;)</c> — one rng draw indexing into that enumerable.
    /// <see cref="BuildLocalPool"/> derives strictly from <c>ModelDb.AllCharacters</c> (runtime
    /// contents, order preserved) and only removes unchecked ids. When every box is checked the
    /// returned list is identical (same members, same order, same count), so <c>NextItem</c> yields
    /// the same character for a given seed as vanilla. Filtering changes the count and therefore
    /// which character a seed produces — expected, not a seed bug.</para>
    ///
    /// <para><b>Determinism (multiplayer).</b> Every peer re-derives every player's random pick
    /// locally in <c>BeginRunLocally</c>; nothing networks the result. So a player's pool must be
    /// identical on every machine when their slot is resolved. Each player therefore broadcasts
    /// their own pool (<see cref="CharacterManager.Multiplayer.RandomPoolMessage"/>); peers store it
    /// in <c>_remotePools</c> keyed by net id. During resolution the <c>BeginRunLocally</c> prefix
    /// loads the ordered random-pickers into <c>_resolveQueue</c>, and each (parameterless)
    /// <see cref="GetPool"/> call — one per picker, in <c>Players</c> order — returns
    /// <see cref="PoolForPlayer"/> for the next picker: the local config for our own slot, the
    /// synced copy for everyone else. Same set, same order on all peers ⇒ identical draw.</para>
    /// </summary>
    public static class RandomPoolStore
    {
        /// <summary>Fires after a single character's pool membership is toggled so live UI can react.</summary>
        public static event Action<ModelId>? OnToggle;

        /// <summary>Fires after any change to the local pool (single or bulk) so the network layer can re-broadcast.</summary>
        public static event Action? PoolChanged;

        private static readonly Dictionary<string, bool> _choices = new();
        private static bool _loaded;

        // --- Multiplayer per-player resolution state ---
        /// <summary>Other players' advertised pools, keyed by net id (received over the wire).</summary>
        private static readonly Dictionary<ulong, List<CharacterModel>> _remotePools = new();
        /// <summary>Random-picker net ids in <c>Players</c> order for the in-progress draw.</summary>
        private static readonly Queue<ulong> _resolveQueue = new();
        private static bool _resolving;
        private static ulong _localNetId;

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
            PoolChanged?.Invoke();
            return now;
        }

        public static void Set(ModelId id, bool inPool)
        {
            EnsureLoaded();
            _choices[id.ToString()] = inPool;
            Save();
        }

        /// <summary>Raise <see cref="PoolChanged"/> once after a bulk edit (e.g. All/None) so the
        /// network layer re-broadcasts a single time instead of per-character.</summary>
        public static void RaisePoolChanged() => PoolChanged?.Invoke();

        // ------------------------------------------------------------------
        // Per-player resolution (singleplayer + multiplayer)
        // ------------------------------------------------------------------

        /// <summary>Store a remote player's advertised pool, received over the network.</summary>
        public static void SetRemotePool(ulong netId, List<CharacterModel>? pool)
        {
            _remotePools[netId] = pool ?? new List<CharacterModel>();
        }

        /// <summary>Drop all remote pools (on lobby open/close).</summary>
        public static void ClearRemotePools() => _remotePools.Clear();

        /// <summary>
        /// Arm the resolution context for one <c>BeginRunLocally</c> pass. <paramref name="randomPickerIds"/>
        /// must be the net ids of players whose character is Random, in <c>Players</c> order — the same
        /// order the draw loop visits them, so each <see cref="GetPool"/> call maps to the right player.
        /// </summary>
        public static void BeginResolution(IEnumerable<ulong> randomPickerIds, ulong localNetId)
        {
            _localNetId = localNetId;
            _resolveQueue.Clear();
            foreach (var id in randomPickerIds) _resolveQueue.Enqueue(id);
            _resolving = true;
        }

        /// <summary>Disarm the resolution context (postfix of <c>BeginRunLocally</c>).</summary>
        public static void EndResolution()
        {
            _resolving = false;
            _resolveQueue.Clear();
        }

        /// <summary>
        /// Replacement for the vanilla roster load inside <c>BeginRunLocally</c> (the transpiler swaps
        /// the producer call for this). Parameterless by necessity, so it pulls the player being
        /// resolved from <see cref="_resolveQueue"/> (one dequeue per call, in draw order). Outside a
        /// resolution pass (or if the queue is exhausted) it falls back to the local pool — which is
        /// the correct singleplayer answer and a safe default.
        /// </summary>
        public static IEnumerable<CharacterModel> GetPool()
        {
            try
            {
                if (_resolving && _resolveQueue.Count > 0)
                {
                    ulong playerId = _resolveQueue.Dequeue();
                    return PoolForPlayer(playerId);
                }
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] random pool: resolution lookup failed (" + e.Message + "); using local pool.");
            }
            return BuildLocalPool();
        }

        /// <summary>
        /// The pool to draw <paramref name="netId"/>'s character from: our own local config for the
        /// local player, or the synced copy that player advertised for anyone else. If a remote
        /// player's pool hasn't arrived (shouldn't happen with reliable+buffered messaging), falls
        /// back to the full roster so the draw still resolves to a real character.
        /// </summary>
        private static IEnumerable<CharacterModel> PoolForPlayer(ulong netId)
        {
            if (netId == _localNetId) return BuildLocalPool();
            if (_remotePools.TryGetValue(netId, out var pool) && pool != null && pool.Count > 0)
                return pool;
            Log.Warn($"[CharacterManager] random pool: no synced pool for player {netId}; using full roster for their draw.");
            return ModelDb.AllCharacters;
        }

        /// <summary>
        /// The local player's pool: <c>ModelDb.AllCharacters</c> (runtime order preserved) minus
        /// unchecked ids. Falls back to the full roster if the filter empties it or anything throws,
        /// so <c>NextItem</c> is never handed an empty set. This is both what we draw our own slot
        /// from and exactly what we broadcast to peers, so the two always agree.
        /// </summary>
        public static List<CharacterModel> BuildLocalPool()
        {
            List<CharacterModel> all;
            try
            {
                all = ModelDb.AllCharacters?.Where(c => c != null).ToList() ?? new List<CharacterModel>();
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] random pool: reading AllCharacters failed (" + e.Message + "); using vanilla roster.");
                try { return ModelDb.AllCharacters?.Where(c => c != null).ToList() ?? new List<CharacterModel>(); }
                catch { return new List<CharacterModel>(); }
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
