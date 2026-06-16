using CharacterManager.UI;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace CharacterManager.Patches
{
    /// <summary>
    /// Adds a "Manage Characters" button to the Compendium screen.
    /// The button is injected once in _Ready; on press we lazily create and push
    /// the CharacterManagerScreen onto the submenu stack.
    ///
    /// Button positioning: we anchor it alongside the existing bottom-row buttons
    /// (Statistics, Run History). We can't use NCompendiumBottomButton (scene-loaded),
    /// so we use a plain Godot Button placed at the bottom of the compendium.
    /// </summary>
    [HarmonyPatch(typeof(NCompendiumSubmenu), "_Ready")]
    public static class CompendiumPatch
    {
        private const string BtnName = "CharacterManagerBtn";

        // Lazily-created screen; re-used across Compendium opens.
        // Null until first button press (so we don't create it unless the player
        // actually clicks Manage Characters).
        private static CharacterManagerScreen? _screen;

        [HarmonyPostfix]
        public static void Postfix(NCompendiumSubmenu __instance)
        {
            // Guard: only add once per instance (scene might be re-instantiated).
            if (__instance.FindChild(BtnName, owned: false) != null)
                return;

            var btn = new Button
            {
                Name = BtnName,
                Text = "Manage Characters",
                // Position: bottom-left area, anchored to parent bottom.
                AnchorTop = 1f,
                AnchorBottom = 1f,
                AnchorLeft = 0f,
                AnchorRight = 0f,
                OffsetTop = -80f,
                OffsetBottom = -20f,
                OffsetLeft = 40f,
                OffsetRight = 280f,
            };
            btn.AddThemeFontSizeOverride("font_size", 18);

            // Use a closure over the compendium instance — at press time _stack is set.
            btn.Pressed += () => OpenManagerScreen(__instance);

            __instance.AddChild(btn);
        }

        private static void OpenManagerScreen(NCompendiumSubmenu compendium)
        {
            var stack = Traverse.Create(compendium).Field("_stack").GetValue<NSubmenuStack>();
            if (stack == null)
            {
                Log.Error("[CharacterManager] _stack is null in CompendiumPatch — cannot open manager screen.");
                return;
            }

            // Lazy-init: create and add to tree exactly once.
            if (_screen == null || !GodotObject.IsInstanceValid(_screen))
            {
                _screen = new CharacterManagerScreen();
                _screen.Visible = false;
                stack.AddChild(_screen);
            }

            stack.Push(_screen);
        }
    }
}
