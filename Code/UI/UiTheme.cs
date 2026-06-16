using Godot;
using MegaCrit.Sts2.Core.Helpers;

namespace CharacterManager.UI
{
    /// <summary>
    /// Shared styling for the mod's code-built screens (M6 UI overhaul). Centralises the game
    /// palette (<see cref="StsColors"/>), compact metrics, and panel construction so the three
    /// screens look consistent and native instead of oversized, sparse, hand-rolled dark boxes.
    ///
    /// Theme/font note: our screens are pushed dynamically under the main-menu submenu stack, so
    /// they should inherit the game's <see cref="Theme"/> (and therefore its fonts) from an
    /// ancestor. <see cref="ApplyGameTheme"/> makes that explicit and robust by copying the nearest
    /// ancestor theme onto our root. We then only override colours (via StsColors) and sizes — never
    /// the font family — so text renders in the game's own font.
    /// </summary>
    public static class UiTheme
    {
        // ─── Palette (game colours) ──────────────────────────────────────────
        public static readonly Color Title = StsColors.gold;        // EFC851
        public static readonly Color Heading = StsColors.blue;      // 87CEEB — section headers
        public static readonly Color Body = StsColors.cream;        // FFF6E2
        public static readonly Color Muted = StsColors.gray;        // 0.5 gray
        public static readonly Color Good = StsColors.green;        // 7FFF00
        public static readonly Color Bad = StsColors.red;           // FF5555

        public static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.9f);          // overlay dim
        public static readonly Color PanelBg = new Color(0.07f, 0.06f, 0.05f, 0.92f); // warm dark
        public static readonly Color RowBg = new Color(0.10f, 0.09f, 0.08f, 0.92f);
        public static readonly Color RowAltBg = new Color(0.13f, 0.11f, 0.09f, 0.92f);
        public static readonly Color Border = new Color(0.45f, 0.38f, 0.26f, 0.85f);  // muted gold
        public static readonly Color Divider = new Color(0.45f, 0.38f, 0.26f, 0.5f);

        // ─── Compact metrics (denser than the old layout) ────────────────────
        public const float PaddingH = 48f;
        public const float PaddingTop = 26f;
        public const float HeaderHeight = 48f;
        public const float RowHeight = 54f;
        public const int RowSpacing = 5;

        public const int TitleFontSize = 28;
        public const int SectionFontSize = 17;
        public const int BodyFontSize = 15;
        public const int SmallFontSize = 12;
        public const int ButtonFontSize = 14;

        /// <summary>
        /// Ensures <paramref name="root"/> uses the game's Theme by copying the nearest ancestor
        /// theme onto it. Call after the node is in the tree (e.g. in OnSubmenuOpened or once _stack
        /// is set). Safe no-op if no ancestor theme is found (children then fall back to the project
        /// default theme, which is still the game's).
        /// </summary>
        public static void ApplyGameTheme(Control root)
        {
            if (root == null || root.Theme != null) return;
            Node? n = root.GetParent();
            while (n != null)
            {
                if (n is Control c && c.Theme != null)
                {
                    root.Theme = c.Theme;
                    return;
                }
                n = n.GetParent();
            }
        }

        // ─── Builders ────────────────────────────────────────────────────────

        /// <summary>A full-rect dim backdrop so the busy menu behind reads as "dimmed", not replaced.</summary>
        public static ColorRect MakeBackdrop()
        {
            return new ColorRect
            {
                Color = Backdrop,
                AnchorRight = 1f,
                AnchorBottom = 1f,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
        }

        /// <summary>A bordered panel in the game's warm-dark tone. Used for rows and sections.</summary>
        public static PanelContainer MakePanel(Color bg, bool border = true)
        {
            var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var style = new StyleBoxFlat
            {
                BgColor = bg,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                ContentMarginLeft = 14f,
                ContentMarginRight = 14f,
                ContentMarginTop = 8f,
                ContentMarginBottom = 8f,
            };
            if (border)
            {
                style.BorderWidthLeft = style.BorderWidthRight =
                    style.BorderWidthTop = style.BorderWidthBottom = 1;
                style.BorderColor = Border;
            }
            panel.AddThemeStyleboxOverride("panel", style);
            return panel;
        }

        /// <summary>A label with our colour + size applied (font family left to the inherited theme).</summary>
        public static Label MakeLabel(string text, Color color, int fontSize,
            HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var lbl = new Label
            {
                Text = text,
                HorizontalAlignment = align,
                VerticalAlignment = VerticalAlignment.Center,
            };
            lbl.AddThemeFontSizeOverride(ThemeConstantsLite.FontSize, fontSize);
            lbl.AddThemeColorOverride(ThemeConstantsLite.FontColor, color);
            return lbl;
        }

        /// <summary>A compact themed button (inherits the game button stylebox; we set size + a colour).</summary>
        public static Button MakeButton(string text, Color? fontColor = null, float minWidth = 92f)
        {
            var btn = new Button
            {
                Text = text,
                CustomMinimumSize = new Vector2(minWidth, 0f),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            btn.AddThemeFontSizeOverride(ThemeConstantsLite.FontSize, ButtonFontSize);
            if (fontColor.HasValue)
                btn.AddThemeColorOverride(ThemeConstantsLite.FontColor, fontColor.Value);
            return btn;
        }

        /// <summary>Local copies of the theme override keys (avoids a dependency on the game's internal addon namespace).</summary>
        private static class ThemeConstantsLite
        {
            public const string FontSize = "font_size";
            public const string FontColor = "font_color";
        }
    }
}
