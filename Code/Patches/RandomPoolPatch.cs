using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CharacterManager.Config;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
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

        /// <summary>
        /// Arms the per-player resolution context before the draw loop runs. The loop resolves every
        /// player whose character is Random, in <c>Players</c> order, with one <c>rng.NextItem</c> call
        /// each — and our transpiler points each of those at the parameterless
        /// <see cref="RandomPoolStore.GetPool"/>. By queuing the random-pickers' net ids here (same
        /// order), <c>GetPool</c> can return the correct player's pool per call without threading the
        /// loop variable through IL. Runs identically on host and every client (the client sets
        /// <c>Players</c> from the host's begin message before this method runs), so all peers resolve
        /// each player's slot from that player's synced pool. Guarded: any failure disarms the context,
        /// so <c>GetPool</c> falls back to the local pool (singleplayer-safe).
        /// </summary>
        private static void Prefix(StartRunLobby __instance)
        {
            try
            {
                if (__instance?.Players == null || __instance.NetService == null)
                {
                    RandomPoolStore.EndResolution();
                    return;
                }

                ulong localNetId = __instance.NetService.NetId;
                var pickers = __instance.Players
                    .Where(p => p.character is RandomCharacter)
                    .Select(p => p.id);
                RandomPoolStore.BeginResolution(pickers, localNetId);
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] random pool: failed to arm resolution context (" + e.Message + "); using local pool.");
                RandomPoolStore.EndResolution();
            }
        }

        /// <summary>Disarm the resolution context once the draw loop has run.</summary>
        private static void Postfix()
        {
            try { RandomPoolStore.EndResolution(); }
            catch { /* never let teardown throw */ }
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
