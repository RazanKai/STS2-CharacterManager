using System;
using System.Collections.Generic;
using CharacterManager;
using CharacterManager.Config;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;

namespace CharacterManager.UI
{
    /// <summary>
    /// Floating "Random Pool" card shown on the character-select screen while the Random option is
    /// selected. Lists every drawable character (<c>ModelDb.AllCharacters</c> runtime contents — the
    /// exact set the random draw indexes into) with a portrait, name, and an In/Out toggle wired to
    /// <see cref="RandomPoolStore"/>. Unchecked characters are excluded from the random draw.
    ///
    /// <para>Built entirely code-side with <see cref="UiTheme"/> so it matches the rest of the mod
    /// and is immune to the scrolling button strip / RitsuLib reshaping (it is a self-contained
    /// card parented to the screen, not an overlay tracking individual buttons).</para>
    /// </summary>
    public sealed partial class RandomPoolPanel : PanelContainer
    {
        public const string NodeName = "CM_RandomPoolPanel";

        private const float PanelWidth = 340f;
        private const float PanelMaxHeight = 540f;
        private const float RightMargin = 40f;
        private const float TopMargin = 70f;
        private const float PortraitSize = 36f;

        private readonly List<Action> _rowRefreshers = new();

        public static RandomPoolPanel Create()
        {
            var panel = new RandomPoolPanel { Name = NodeName };
            panel.Build();
            return panel;
        }

        private void Build()
        {
            // Anchor as a fixed-size card on the TOP-right edge. Top-anchored (not centred) so the
            // card stays in the upper-right and never overlaps the bottom-right Embark/accept button.
            AnchorLeft = 1f; AnchorRight = 1f;
            AnchorTop = 0f; AnchorBottom = 0f;
            OffsetRight = -RightMargin;
            OffsetLeft = -RightMargin - PanelWidth;
            OffsetTop = TopMargin;
            OffsetBottom = TopMargin + PanelMaxHeight;

            var style = new StyleBoxFlat
            {
                BgColor = UiTheme.PanelBg,
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthTop = 1,
                BorderWidthBottom = 1,
                BorderColor = UiTheme.Border,
                ContentMarginLeft = 14f,
                ContentMarginRight = 14f,
                ContentMarginTop = 12f,
                ContentMarginBottom = 12f,
            };
            AddThemeStyleboxOverride("panel", style);

            var root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            root.AddThemeConstantOverride("separation", 8);
            AddChild(root);

            root.AddChild(UiTheme.MakeLabel("Random Pool", UiTheme.Title, UiTheme.SectionFontSize + 2,
                HorizontalAlignment.Center));
            root.AddChild(UiTheme.MakeLabel("Characters the Random option may draw", UiTheme.Muted,
                UiTheme.SmallFontSize, HorizontalAlignment.Center));

            // All / None quick toggles.
            var quick = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            quick.AddThemeConstantOverride("separation", 8);
            var allBtn = UiTheme.MakeButton("All", UiTheme.Good, 0f);
            allBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            allBtn.Pressed += () => SetAll(true);
            var noneBtn = UiTheme.MakeButton("None", UiTheme.Bad, 0f);
            noneBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            noneBtn.Pressed += () => SetAll(false);
            quick.AddChild(allBtn);
            quick.AddChild(noneBtn);
            root.AddChild(quick);

            root.AddChild(MakeDivider());

            var scroll = new ScrollContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
            root.AddChild(scroll);

            var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            rows.AddThemeConstantOverride("separation", UiTheme.RowSpacing);
            scroll.AddChild(rows);

            foreach (var c in GetDrawableCharacters())
                rows.AddChild(MakeRow(c));
        }

        /// <summary>The drawable roster, in <see cref="CharacterHelper.GetAllCharacters"/> order. Shows all characters so they can be toggled In/Out.</summary>
        private static List<CharacterModel> GetDrawableCharacters()
        {
            try
            {
                return CharacterHelper.GetAllCharacters();
            }
            catch (Exception e)
            {
                Log.Error("[CharacterManager] random pool panel: failed to read characters: " + e.Message);
                return new List<CharacterModel>();
            }
        }

        private Control MakeRow(CharacterModel character)
        {
            var id = character.Id;
            var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var style = new StyleBoxFlat
            {
                BgColor = UiTheme.RowBg,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                ContentMarginLeft = 8f,
                ContentMarginRight = 8f,
                ContentMarginTop = 5f,
                ContentMarginBottom = 5f,
            };
            panel.AddThemeStyleboxOverride("panel", style);

            var hbox = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            hbox.AddThemeConstantOverride("separation", 10);
            panel.AddChild(hbox);

            hbox.AddChild(MakePortrait(character));

            var nameLbl = UiTheme.MakeLabel(SafeTitle(character), UiTheme.Body, UiTheme.BodyFontSize);
            nameLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            nameLbl.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            hbox.AddChild(nameLbl);

            var toggle = UiTheme.MakeButton("", null, 64f);
            toggle.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            void Apply()
            {
                bool inPool = RandomPoolStore.IsInPool(id);
                toggle.Text = inPool ? "In" : "Out";
                toggle.AddThemeColorOverride("font_color", inPool ? UiTheme.Good : UiTheme.Muted);
            }
            toggle.Pressed += () => { RandomPoolStore.Toggle(id); Apply(); };
            Apply();
            _rowRefreshers.Add(Apply);
            hbox.AddChild(toggle);

            return panel;
        }

        private void SetAll(bool inPool)
        {
            foreach (var c in GetDrawableCharacters())
                RandomPoolStore.Set(c.Id, inPool);
            foreach (var refresh in _rowRefreshers) refresh();
            // One notification for the whole bulk edit (re-broadcasts the pool in MP once).
            RandomPoolStore.RaisePoolChanged();
        }

        private static string SafeTitle(CharacterModel c)
        {
            try { return c.Title.GetFormattedText(); }
            catch { return c.Id.Entry; }
        }

        private static Control MakePortrait(CharacterModel character)
        {
            var holder = new Control
            {
                CustomMinimumSize = new Vector2(PortraitSize, PortraitSize),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            try
            {
                var tex = character.IconTexture;
                if (tex != null)
                {
                    holder.AddChild(new TextureRect
                    {
                        Texture = tex,
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        AnchorRight = 1f,
                        AnchorBottom = 1f,
                        MouseFilter = Control.MouseFilterEnum.Ignore,
                    });
                }
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] random pool panel: portrait load failed: " + e.Message);
            }
            return holder;
        }

        private static Control MakeDivider()
        {
            return new ColorRect
            {
                Color = UiTheme.Divider,
                CustomMinimumSize = new Vector2(0f, 1f),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
        }
    }
}
