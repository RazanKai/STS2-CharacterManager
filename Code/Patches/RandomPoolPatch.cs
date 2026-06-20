using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CharacterManager.Config;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;

namespace CharacterManager.Patches
{
    /// <summary>
    /// Restricts the <b>Random</b> character draw to the player's chosen pool (see
    /// <see cref="RandomPoolStore"/>).
    ///
    /// <para><b>Where the game draws.</b>
    /// <c>StartRunLobby.BeginRunLocally(string, List&lt;ModifierModel&gt;)</c> resolves any
    /// RandomCharacter lobby slot with a single line:
    /// <code>CharacterModel character = rng.NextItem(ModelDb.AllCharacters);</code>
    /// This is the only reference to <c>ModelDb.AllCharacters</c> in that method.</para>
    ///
    /// <para><b>Why a transpiler.</b> A postfix is too late (the character is already chosen); a
    /// prefix would have to re-derive the seeded <c>rng</c> after <c>ActModel.GetRandomList</c>
    /// has already advanced it earlier in the same method — fragile and determinism-breaking. The
    /// clean seam is to swap the operand of the single <c>call get_AllCharacters</c> instruction
    /// for a call to <see cref="RandomPoolStore.GetPool"/>. Both are static, parameterless, and
    /// return <c>IEnumerable&lt;CharacterModel&gt;</c>, so the surrounding IL (the
    /// <c>rng.NextItem</c> call that consumes it) is untouched and rng sequencing is identical.
    /// When every character is in the pool, <c>GetPool</c> returns the same list in the same
    /// order, so vanilla seeds reproduce exactly.</para>
    ///
    /// <para><b>Guard hard.</b> This is the mod's only gameplay-path patch and transpilers are the
    /// most update-fragile patch type. If the expected single <c>get_AllCharacters</c> call isn't
    /// found (zero, or more than one — an ambiguous future layout), we log and emit the original IL
    /// unchanged, leaving vanilla behavior intact rather than risking a wrong rewrite. Re-verify
    /// this site on every game update.</para>
    /// </summary>
    [HarmonyPatch]
    public static class RandomPoolPatch
    {
        private static MethodBase? TargetMethod()
        {
            var m = AccessTools.Method(typeof(StartRunLobby), "BeginRunLocally",
                new[] { typeof(string), typeof(List<ModifierModel>) });
            if (m == null)
                Log.Warn("[CharacterManager] random pool: StartRunLobby.BeginRunLocally not found; transpiler will not arm.");
            return m;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            try
            {
                var getter = AccessTools.PropertyGetter(typeof(ModelDb), nameof(ModelDb.AllCharacters));
                var replacement = AccessTools.Method(typeof(RandomPoolStore), nameof(RandomPoolStore.GetPool));
                if (getter == null || replacement == null)
                {
                    Log.Warn("[CharacterManager] random pool: could not resolve get_AllCharacters or GetPool; leaving vanilla draw.");
                    return list;
                }

                int matches = list.Count(ci => ci.Calls(getter));
                if (matches != 1)
                {
                    Log.Warn($"[CharacterManager] random pool: expected exactly 1 ModelDb.AllCharacters call in BeginRunLocally, found {matches}; leaving vanilla draw (re-verify after game update).");
                    return list;
                }

                foreach (var ci in list)
                {
                    if (ci.Calls(getter))
                    {
                        ci.opcode = OpCodes.Call;   // GetPool is static
                        ci.operand = replacement;
                        Log.Info("[CharacterManager] random pool: patched BeginRunLocally to draw from the configured pool.");
                        break;
                    }
                }
                return list;
            }
            catch (Exception e)
            {
                Log.Error("[CharacterManager] random pool: transpiler failed (" + e.Message + "); leaving vanilla draw.");
                return list;
            }
        }
    }
}
