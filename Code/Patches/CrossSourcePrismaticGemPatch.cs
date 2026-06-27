using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace CharacterManager.Patches
{
    [HarmonyPatch(typeof(PrismaticGem), "ModifyCardRewardCreationOptions")]
    internal static class CrossSourcePrismaticGemPatch
    {
        // Target: the expression `player.UnlockState.CharacterCardPools.Union(options.CardPools)` inside
        // ModifyCardRewardCreationOptions. We want to replace the load of
        // player.UnlockState.CharacterCardPools with CrossSourceFilter.PoolsFor(player.UnlockState).
        // The Union with options.CardPools should remain (that's the SharedCardPools part).
        //
        // Strategy: transpile the getter call for CharacterCardPools on the player's UnlockState.

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
                    $"[CharacterManager] CrossSourcePrismaticGemPatch: expected at least 1 call to CharacterCardPools getter, found {replaced}. Patch disabled (fail-closed to vanilla).");
                return codes;
            }

            MegaCrit.Sts2.Core.Logging.Log.Info($"[CharacterManager] CrossSourcePrismaticGemPatch applied (replaced {replaced} getter calls).");
            return codes;
        }
    }
}