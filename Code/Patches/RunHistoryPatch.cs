using System;
using System.Collections.Generic;
using System.Reflection;
using CharacterManager.Config;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace CharacterManager.Patches
{
    /// <summary>
    /// Filters the Run History screen to show only runs for a specific character
    /// when <see cref="RunHistoryFilter.Character"/> is set.
    ///
    /// How it works:
    ///   1. Postfix on <c>NRunHistory.OnSubmenuOpened</c>: if filter is active, rebuild
    ///      <c>_runNames</c> with only runs matching the character, then call
    ///      <c>RefreshAndSelectRun(0)</c> to re-render.
    ///   2. Postfix on <c>NSubmenu.OnSubmenuClosed</c>: when NRunHistory closes, clear
    ///      the filter so the next unfiltered open works normally.
    /// </summary>
    [HarmonyPatch]
    public static class RunHistoryPatch
    {
        // Cached reflection references.
        private static readonly FieldInfo? RunNamesField =
            AccessTools.Field(typeof(NRunHistory), "_runNames");

        private static readonly MethodInfo? RefreshMethod =
            AccessTools.Method(typeof(NRunHistory), "RefreshAndSelectRun");

        // ─── Patch 1: filter run names after the screen loads them ────────────

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NRunHistory), nameof(NRunHistory.OnSubmenuOpened))]
        public static void OnSubmenuOpened_Postfix(NRunHistory __instance)
        {
            ModelId? filter = RunHistoryFilter.Character;
            if (filter == null) return;

            if (RunNamesField == null || RefreshMethod == null)
            {
                Log.Error("[CharacterManager] RunHistoryPatch: reflection failed — can't filter run names.");
                return;
            }

            var allNames = RunNamesField.GetValue(__instance) as List<string>;
            if (allNames == null) return;

            // Build a filtered list: only runs belonging to the target character.
            // We load each run in turn; the number of runs is typically small (<200).
            var filtered = new List<string>(allNames.Count);
            foreach (var name in allNames)
            {
                try
                {
                    var result = SaveManager.Instance.LoadRunHistory(name);
                    if (!result.Success) continue;

                    RunHistory? history = result.SaveData;
                    if (history == null) continue;
                    foreach (var player in history.Players)
                    {
                        if (player.Character == filter)
                        {
                            filtered.Add(name);
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Warn($"[CharacterManager] Could not check run '{name}': {e.Message}");
                }
            }

            allNames.Clear();
            allNames.AddRange(filtered);

            // Re-render: RefreshAndSelectRun(0) is private Task-returning but synchronous.
            if (allNames.Count > 0)
            {
                try
                {
                    RefreshMethod.Invoke(__instance, new object[] { 0 });
                }
                catch (Exception e)
                {
                    Log.Error($"[CharacterManager] RefreshAndSelectRun failed: {e.Message}");
                }
            }
        }

        // ─── Patch 2: clear filter when NRunHistory is dismissed ──────────────

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NSubmenu), nameof(NSubmenu.OnSubmenuClosed))]
        public static void OnSubmenuClosed_Postfix(NSubmenu __instance)
        {
            if (__instance is NRunHistory)
                RunHistoryFilter.Character = null;
        }
    }
}
