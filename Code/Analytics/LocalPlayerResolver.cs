using System;
using System.Linq;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace CharacterManager.Analytics
{
    /// <summary>
    /// Resolves "which player in this run-history file is actually me" — never a co-op partner who
    /// happens to share this profile's local <c>.run</c> folder (every player in a multiplayer run
    /// gets a copy of the same run file, listing every party member).
    ///
    /// <para>Mirrors exactly how the game itself decides whose lifetime stats a run updates
    /// (<c>ProgressSaveManager.UpdateWithRunData</c>): the sole player in singleplayer, or whichever
    /// player's <see cref="RunHistoryPlayer.Id"/> matches this profile's local platform-user id
    /// (<c>PlatformUtil.GetLocalPlayerId(h.PlatformType)</c>) in multiplayer. A run whose local player
    /// can't be resolved is treated as "not mine" rather than guessed at.</para>
    ///
    /// <para>Three call sites independently got this wrong before by matching on the CHARACTER being
    /// analyzed instead of on player identity (grabbing whichever player played that character first,
    /// even if it was a partner): <see cref="RosterWinHistory"/> (Bug 4), <c>CharacterAnalytics.Compute</c>
    /// and <c>CharacterRunAutopsyScreen</c> (Bug 5) — see DEVLOG. Route any new per-run "which player is
    /// me" lookup through here instead of re-deriving it.</para>
    /// </summary>
    public static class LocalPlayerResolver
    {
        /// <summary>
        /// Returns this profile's own <see cref="RunHistoryPlayer"/> entry in <paramref name="h"/>, or
        /// null if it can't be identified (no players, or local-id resolution failed/didn't match).
        /// <paramref name="context"/> is just a label for the warning log on failure (e.g. the run's
        /// file name).
        /// </summary>
        public static RunHistoryPlayer? Resolve(RunHistory? h, string context)
        {
            if (h?.Players == null || h.Players.Count == 0) return null;
            if (h.Players.Count == 1) return h.Players[0];

            ulong localPlayerId;
            try { localPlayerId = PlatformUtil.GetLocalPlayerId(h.PlatformType); }
            catch (Exception e)
            {
                Log.Warn($"[CharacterManager] LocalPlayerResolver: couldn't resolve local player id ({context}): {e.Message}");
                return null;
            }

            return h.Players.FirstOrDefault(p => p.Id == localPlayerId);
        }
    }
}
