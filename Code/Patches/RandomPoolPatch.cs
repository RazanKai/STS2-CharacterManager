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
using MegaCrit.Sts2.Core.Random;

namespace CharacterManager.Patches
{
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
                var replacement = AccessTools.Method(typeof(RandomPoolStore), nameof(RandomPoolStore.GetPool));
                var nextItemGeneric = AccessTools.Method(typeof(Rng), nameof(Rng.NextItem));
                var nextItemMethod = nextItemGeneric?.MakeGenericMethod(typeof(CharacterModel));

                if (replacement == null || nextItemMethod == null)
                {
                    Log.Warn("[CharacterManager] random pool: could not resolve GetPool or NextItem; leaving vanilla draw.");
                    return list;
                }

                // The collection NextItem draws from is whatever the parameter expects
                // (IEnumerable<CharacterModel>). We require the producing call to return a type
                // assignable to it so our GetPool() swap is type- and stack-compatible.
                var collectionType = nextItemMethod.GetParameters().FirstOrDefault()?.ParameterType;
                if (collectionType == null)
                {
                    Log.Warn("[CharacterManager] random pool: NextItem has no collection parameter; leaving vanilla draw.");
                    return list;
                }

                // Find the single Rng.NextItem<CharacterModel> call. More than one is an ambiguous
                // future layout we won't guess at; zero means the draw moved — bail either way.
                int nextItemIndex = -1;
                int nextItemCount = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Calls(nextItemMethod))
                    {
                        nextItemCount++;
                        if (nextItemIndex == -1) nextItemIndex = i;
                    }
                }

                if (nextItemCount != 1)
                {
                    Log.Warn($"[CharacterManager] random pool: expected exactly 1 Rng.NextItem<CharacterModel> call in BeginRunLocally, found {nextItemCount}; leaving vanilla draw (re-verify after game update).");
                    return list;
                }

                // The collection is produced by the instruction immediately before NextItem. We only
                // rewrite it when it is a static, parameterless Call whose return type the NextItem
                // parameter accepts. That matches the vanilla producer (ModelDb.AllCharacters getter
                // in the decompiled source / GetRandomEligibleCharacters() in the live build) and
                // guarantees a stack-neutral swap with our static, parameterless GetPool(): both
                // consume nothing and push one IEnumerable<CharacterModel>. Anything else (an
                // instance call, a newobj/newarr, an intervening arg) fails closed to vanilla.
                int targetIndex = nextItemIndex - 1;
                if (targetIndex < 0)
                {
                    Log.Warn("[CharacterManager] random pool: NextItem is at index 0, no producer to replace; leaving vanilla draw.");
                    return list;
                }

                var targetCi = list[targetIndex];
                if (targetCi.opcode != OpCodes.Call
                    || targetCi.operand is not MethodInfo producer
                    || !producer.IsStatic
                    || producer.GetParameters().Length != 0
                    || !collectionType.IsAssignableFrom(producer.ReturnType))
                {
                    Log.Warn($"[CharacterManager] random pool: producer before NextItem (IL[{targetIndex}] {targetCi.opcode} {targetCi.operand}) is not a static parameterless call returning {collectionType.Name}; leaving vanilla draw (re-verify after game update).");
                    return list;
                }

                // Swap the producer for GetPool(). Stays a static call; rng sequencing is untouched.
                targetCi.operand = replacement;
                Log.Info($"[CharacterManager] random pool: patched BeginRunLocally (replaced {producer.Name} at IL[{targetIndex}]) to draw from the configured pool.");
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
