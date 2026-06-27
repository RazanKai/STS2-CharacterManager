using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Unlocks;

namespace CharacterManager.Patches
{
    [HarmonyPatch(typeof(Orobas), "GenerateInitialOptions")]
    internal static class CrossSourceOrobasPatch
    {
        // Target: the expression `Owner.UnlockState.Characters.Where(c => c.Id != character.Id)` inside
        // GenerateInitialOptions. We want to replace the load of Owner.UnlockState.Characters with
        // CrossSourceFilter.CharactersFor(Owner.UnlockState), then the Where clause will still apply.
        //
        // Strategy: transpile the getter call for Characters on the Owner's UnlockState.

        private static readonly MethodInfo TargetGetter = AccessTools.PropertyGetter(
            typeof(MegaCrit.Sts2.Core.Unlocks.UnlockState), nameof(MegaCrit.Sts2.Core.Unlocks.UnlockState.Characters));

        private static readonly MethodInfo Replacement = AccessTools.Method(
            typeof(CrossSourceFilter), nameof(CrossSourceFilter.CharactersFor));

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
                    $"[CharacterManager] CrossSourceOrobasPatch: expected at least 1 call to Characters getter, found {replaced}. Patch disabled (fail-closed to vanilla).");
                return codes;
            }

            MegaCrit.Sts2.Core.Logging.Log.Info($"[CharacterManager] CrossSourceOrobasPatch applied (replaced {replaced} getter calls).");
            return codes;
        }
    }
}