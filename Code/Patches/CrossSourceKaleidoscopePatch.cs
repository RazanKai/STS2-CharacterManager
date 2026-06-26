using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Unlocks;

namespace CharacterManager.Patches
{
    [HarmonyPatch(typeof(Kaleidoscope), "AfterObtained")]
    internal static class CrossSourceKaleidoscopePatch
    {
        // Target: the call to Owner.UnlockState.CharacterCardPools.Where(...) inside AfterObtained.
        // We replace the load of Owner.UnlockState.CharacterCardPools with a call to CrossSourceFilter.PoolsFor.
        // Note: IsAllowedAtNeow reads CharacterCardPools.Count() directly — we intentionally do NOT patch that,
        // so the relic's Neow eligibility gate uses the vanilla (unfiltered) count. This means filtering only
        // affects what the relic OFFERS once obtained, not whether it CAN appear at Neow.
        //
        // AfterObtained is an async method with a for-loop that accesses CharacterCardPools each iteration.
        // The transpiler must replace ALL calls to the getter (not just one) since the loop runs multiple times.

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
                    $"[CharacterManager] CrossSourceKaleidoscopePatch: expected at least 1 call to CharacterCardPools getter, found {replaced}. Patch disabled (fail-closed to vanilla).");
                return codes;
            }

            MegaCrit.Sts2.Core.Logging.Log.Info($"[CharacterManager] CrossSourceKaleidoscopePatch applied (replaced {replaced} getter calls).");
            return codes;
        }
    }
}