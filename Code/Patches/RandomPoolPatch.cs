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

                // Debug: log ALL instructions around call sites
                for (int i = 0; i < list.Count; i++)
                {
                    var ci = list[i];
                    if (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Newobj || ci.opcode == OpCodes.Newarr)
                    {
                        Log.Info($"[CharacterManager] random pool: IL[{i}] = {ci.opcode} {ci.operand}");
                    }
                }

                // Find the call to Rng.NextItem(IEnumerable<CharacterModel>)
                int nextItemIndex = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Calls(nextItemMethod))
                    {
                        nextItemIndex = i;
                        Log.Info($"[CharacterManager] random pool: found NextItem at IL[{i}]");
                        break;
                    }
                }

                if (nextItemIndex == -1)
                {
                    Log.Warn("[CharacterManager] random pool: could not find Rng.NextItem call in BeginRunLocally; leaving vanilla draw.");
                    return list;
                }

                // The argument to NextItem is loaded by the instruction immediately before it.
                // In the running game, this is a call to GetRandomEligibleCharacters() instead of ModelDb.AllCharacters.
                // We don't care WHAT loads the collection - we just replace whatever instruction loads it
                // with our own GetPool() call.
                int targetIndex = nextItemIndex - 1;
                if (targetIndex < 0)
                {
                    Log.Warn("[CharacterManager] random pool: NextItem is at index 0, no argument to replace; leaving vanilla draw.");
                    return list;
                }

                // Verify the target instruction loads a collection (call, callvirt, newobj, newarr)
                var targetCi = list[targetIndex];
                bool loadsCollection = targetCi.opcode == OpCodes.Call 
                    || targetCi.opcode == OpCodes.Callvirt 
                    || targetCi.opcode == OpCodes.Newobj 
                    || targetCi.opcode == OpCodes.Newarr;
                
                if (!loadsCollection)
                {
                    Log.Warn($"[CharacterManager] random pool: instruction at IL[{targetIndex}] ({targetCi.opcode}) doesn't appear to load a collection; leaving vanilla draw.");
                    return list;
                }

                // Replace the instruction that loads the collection with our GetPool call
                targetCi.opcode = OpCodes.Call;
                targetCi.operand = replacement;
                Log.Info($"[CharacterManager] random pool: patched BeginRunLocally at IL index {targetIndex} (was {list[targetIndex].opcode}) to draw from the configured pool.");
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
