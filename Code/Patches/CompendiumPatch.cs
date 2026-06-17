using System;
using CharacterManager.UI;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace CharacterManager.Patches
{
    /// <summary>
    /// Adds a proper "Manage Characters" button to the Compendium, styled to match the native
    /// bottom-row buttons (Character Stats / Run History). Rather than hand-build the look, we
    /// DUPLICATE the existing Statistics button (so we inherit its exact background texture, HSV
    /// shader, layout and hover/press animations), then:
    ///   - give it its own copy of the HSV material (so our hover doesn't tint the real Statistics
    ///     button — they'd otherwise share the resource),
    ///   - recolour it by shifting the shader's hue (falling back to a modulate tint),
    ///   - replace the single icon with a row of character portraits (so it doesn't read as one
    ///     specific character),
    ///   - set the label to "Manage Characters",
    ///   - place it just to the LEFT of Character Stats,
    ///   - and wire its Released signal to open the manager.
    ///
    /// The whole thing is wrapped in try/catch so a failure can never break the Compendium screen.
    /// </summary>
    [HarmonyPatch(typeof(NCompendiumSubmenu), "_Ready")]
    public static class CompendiumPatch
    {
        private const string BtnName = "ManageCharactersBtn";

        // Lazily-created screen, re-used across Compendium opens (created on first press).
        private static CharacterManagerScreen? _screen;

        [HarmonyPostfix]
        public static void Postfix(NCompendiumSubmenu __instance)
        {
            if (__instance.FindChild(BtnName, recursive: true, owned: false) != null)
                return;

            try
            {
                BuildButton(__instance);
            }
            catch (Exception e)
            {
                Log.Error("[CharacterManager] Failed to build Manage Characters button: " + e);
            }
        }

        private static void BuildButton(NCompendiumSubmenu compendium)
        {
            var statsBtn = Traverse.Create(compendium).Field("_statisticsButton").GetValue<NCompendiumBottomButton>();
            if (statsBtn == null)
            {
                Log.Warn("[CharacterManager] Statistics button not found; skipping Manage Characters button.");
                return;
            }

            var parent = statsBtn.GetParent();
            if (parent == null) return;
            int statsIndex = statsBtn.GetIndex();

            // Duplicate WITHOUT signals so we don't inherit Statistics' Released→OpenStatistics wiring.
            var dup = (NCompendiumBottomButton)statsBtn.Duplicate(
                (int)(Node.DuplicateFlags.Scripts | Node.DuplicateFlags.Groups));
            dup.Name = BtnName;

            parent.AddChild(dup);           // triggers _Ready → ConnectSignals (caches child nodes)
            parent.MoveChild(dup, statsIndex); // sit left of Character Stats (for container layouts)

            // Don't let the locale-change notification rewrite our label back to "Statistics".
            Traverse.Create(dup).Field("_locKeyPrefix").SetValue(null);

            // Label
            var label = Traverse.Create(dup).Field("_label").GetValue<MegaLabel>();
            label?.SetTextAutoSize("Manage Characters");

            // Recolour + own material
            Recolor(dup);

            // Multiple character portraits instead of a single icon
            var icon = Traverse.Create(dup).Field("_icon").GetValue<TextureRect>();
            if (icon != null) PopulateIcons(icon);

            // Position to the left of Character Stats (for free / anchored layouts)
            float w = statsBtn.OffsetRight - statsBtn.OffsetLeft;
            if (w < 1f) w = statsBtn.Size.X;
            float shift = w + 24f;
            dup.AnchorLeft = statsBtn.AnchorLeft;
            dup.AnchorRight = statsBtn.AnchorRight;
            dup.AnchorTop = statsBtn.AnchorTop;
            dup.AnchorBottom = statsBtn.AnchorBottom;
            dup.OffsetTop = statsBtn.OffsetTop;
            dup.OffsetBottom = statsBtn.OffsetBottom;
            dup.OffsetLeft = statsBtn.OffsetLeft - shift;
            dup.OffsetRight = statsBtn.OffsetRight - shift;

            // Controller focus: stitch it in to the left of Character Stats.
            dup.FocusNeighborRight = statsBtn.GetPath();
            statsBtn.FocusNeighborLeft = dup.GetPath();

            // Open the manager on click.
            dup.Released += _ => OpenManagerScreen(compendium);
        }

        /// <summary>Gives the button its own HSV material and shifts its hue (modulate tint fallback).</summary>
        private static void Recolor(NCompendiumBottomButton dup)
        {
            var bg = Traverse.Create(dup).Field("_bgPanel").GetValue<Control>();
            if (bg == null) return;

            if (bg.Material is ShaderMaterial sm && sm.Shader != null)
            {
                var unique = (ShaderMaterial)sm.Duplicate(true);
                bg.Material = unique;
                Traverse.Create(dup).Field("_hsv").SetValue(unique); // keep the cached ref in sync

                foreach (var u in unique.Shader.GetShaderUniformList())
                {
                    string n = u.AsGodotDictionary()["name"].AsString();
                    if (n == "h" || n == "hue" || n == "hue_shift" || n == "hueShift")
                    {
                        float cur = unique.GetShaderParameter(n).AsSingle();
                        unique.SetShaderParameter(n, Mathf.PosMod(cur + 0.45f, 1f));
                        return;
                    }
                }
            }

            // Fallback: tint just the background (survives the focus/press modulate tweens).
            bg.SelfModulate = new Color("5bb0c4");
        }

        /// <summary>
        /// Fills the icon slot with up to three portraits. The middle slot uses the game's
        /// question-mark sprite when available (so it reads as "characters" generically rather than
        /// any one of them). The row is drawn slightly larger than the icon slot to read clearly.
        /// </summary>
        private static void PopulateIcons(TextureRect icon)
        {
            icon.Texture = null;

            var hbox = new HBoxContainer { Name = "MultiIcons", MouseFilter = Control.MouseFilterEnum.Ignore };
            hbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            // Grow the drawing area a bit past the icon slot so the portraits render larger.
            hbox.OffsetLeft -= 22f; hbox.OffsetRight += 22f;
            hbox.OffsetTop -= 8f; hbox.OffsetBottom += 8f;
            hbox.AddThemeConstantOverride("separation", 1);
            icon.AddChild(hbox);

            Texture2D? qmark = null;
            try { qmark = PreloadManager.Cache.GetTexture2D(ImageHelper.GetImagePath("atlases/stats_screen_atlas.sprites/stats_questionmark.tres")); }
            catch (Exception e) { Log.Warn("[CharacterManager] question-mark icon load failed: " + e.Message); }

            var chars = CharacterHelper.GetAllCharacters();
            int slots = Math.Min(3, chars.Count);
            for (int i = 0; i < slots; i++)
            {
                Texture2D? tex;
                if (i == 1 && qmark != null)
                {
                    tex = qmark; // replace the middle (Silent) portrait with the question mark
                }
                else
                {
                    try { tex = chars[i].IconTexture; } catch { tex = null; }
                }
                if (tex == null) continue;

                hbox.AddChild(new TextureRect
                {
                    Texture = tex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
            }
        }

        private static void OpenManagerScreen(NCompendiumSubmenu compendium)
        {
            var stack = Traverse.Create(compendium).Field("_stack").GetValue<NSubmenuStack>();
            if (stack == null)
            {
                Log.Error("[CharacterManager] _stack is null in CompendiumPatch — cannot open manager screen.");
                return;
            }

            if (_screen == null || !GodotObject.IsInstanceValid(_screen))
            {
                _screen = new CharacterManagerScreen { Visible = false };
                stack.AddChild(_screen);
            }

            stack.Push(_screen);
        }
    }
}
