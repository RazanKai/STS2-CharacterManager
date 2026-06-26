using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Runs;

namespace CharacterManager.Patches
{
    [HarmonyPatch(typeof(ColorfulPhilosophers), "GenerateInitialOptions")]
    internal static class CrossSourceColorfulPhilosophersPatch
    {
        // Target: the call to Owner.UnlockState.CharacterCardPools.ToList() inside GenerateInitialOptions.
        // We replace the load of Owner.UnlockState.CharacterCardPools with a call to CrossSourceFilter.PoolsFor.
        // This keeps the rest of the method (color order, exclusion of own pool, random capping to 3) intact.

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
                    $"[CharacterManager] CrossSourceColorfulPhilosophersPatch: expected at least 1 call to CharacterCardPools getter, found {replaced}. Patch disabled (fail-closed to vanilla).");
                return codes; // return original instructions - fail closed
            }

            MegaCrit.Sts2.Core.Logging.Log.Info($"[CharacterManager] CrossSourceColorfulPhilosophersPatch applied (replaced {replaced} getter calls).");
            return codes;
        }
    }
}