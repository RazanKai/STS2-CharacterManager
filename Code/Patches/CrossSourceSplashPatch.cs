using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Unlocks;

namespace CharacterManager.Patches
{
    [HarmonyPatch]
    internal static class CrossSourceSplashPatch
    {
        // Target: the call to Owner.UnlockState.CharacterCardPools.ToList() inside OnPlay.
        // We replace the load of Owner.UnlockState.CharacterCardPools with a call to CrossSourceFilter.PoolsFor.
        // The method already removes own pool and needs ≥1 attack source; our filter's fallback to vanilla
        // ensures it never gets an empty set.
        //
        // OnPlay is an `async Task`: its body (incl. the CharacterCardPools getter call) compiles into a
        // generated state-machine MoveNext, NOT the OnPlay stub. Transpiling the stub would match 0 getter
        // calls and silently no-op, so we retarget the MoveNext via AsyncMoveNext.
        static MethodBase TargetMethod() => AccessTools.AsyncMoveNext(
            AccessTools.Method(typeof(Splash), "OnPlay"));

        private static readonly MethodInfo TargetGetter = AccessTools.PropertyGetter(
            typeof(MegaCrit.Sts2.Core.Unlocks.UnlockState), nameof(MegaCrit.Sts2.Core.Unlocks.UnlockState.CharacterCardPools));

        private static readonly MethodInfo Replacement = AccessTools.Method(
            typeof(CrossSourceFilter), nameof(CrossSourceFilter.PoolsFor));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            int replaced = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(TargetGetter))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, Replacement);
                    replaced++;
                }
            }

            if (replaced == 0)
            {
                MegaCrit.Sts2.Core.Logging.Log.Warn(
                    $"[CharacterManager] CrossSourceSplashPatch: expected at least 1 call to CharacterCardPools getter, found {replaced}. Patch disabled (fail-closed to vanilla).");
                return codes;
            }

            MegaCrit.Sts2.Core.Logging.Log.Info($"[CharacterManager] CrossSourceSplashPatch applied (replaced {replaced} getter calls).");
            return codes;
        }
    }
}