using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace CharacterManager
{
    [ModInitializer("Init")]
    public class ModEntry
    {
        private static Harmony? _harmony;

        public static void Init()
        {
            _harmony = new Harmony("CharacterManager");
            _harmony.PatchAll();
            Log.Info("[CharacterManager] initialized.");
        }

        public static void Dispose()
        {
            _harmony?.UnpatchAll("CharacterManager");
        }
    }
}
