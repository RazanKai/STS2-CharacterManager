using System;
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

        public static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.2f);          // overlay dim (mostly transparent: game shows through)
        public static readonly Color PanelBg = new Color(0.07f, 0.06f, 0.05f, 0.92f); // warm dark
        public static readonly Color RowBg = new Color(0.10f, 0.09f, 0.08f, 0.92f);
        public static readonly Color RowAltBg = new Color(0.13f, 0.11f, 0.09f, 0.92f);
        public static readonly Color Border = new Color(0.45f, 0.38f, 0.26f, 0.85f);  // muted gold
        public static readonly Color Divider = new Color(0.45f, 0.38f, 0.26f, 0.5f);
        public static readonly Color BarTrack = new Color(1f, 1f, 1f, 0.09f);          // empty bar groove

        // ─── Compact metrics (denser than the old layout) ────────────────────
        // Content is laid out in a centred fixed-width column so it doesn't stretch across
        // ultra-wide screens (the old full-width rows were the main "sparse" complaint).
        public const float MaxContentWidth = 1180f;
        private const float Half = MaxContentWidth / 2f;

        // Two-pane split (M6 cont.): the manager screen divides its centred column into a
        // left list region and a fixed-width right detail panel.
        public const float DetailPanelWidth = 360f;
        public const float PaneGap = 20f;
        /// <summary>Horizontal space the right detail panel + gap reserve on the right of the column.</summary>
        public const float ListRightReserve = DetailPanelWidth + PaneGap;

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

        // ─── Centred-column placement ────────────────────────────────────────
        // All chrome is positioned inside a fixed-width column centred on screen, so the layout
        // looks contained (like the game's panels) instead of stretching across an ultra-wide display.

        /// <summary>Places a fixed-height control in the centred column at vertical offset <paramref name="top"/>.</summary>
        public static void PlaceInColumn(Control c, float top, float height)
        {
            c.AnchorLeft = 0.5f; c.AnchorRight = 0.5f;
            c.AnchorTop = 0f; c.AnchorBottom = 0f;
            c.OffsetLeft = -Half; c.OffsetRight = Half;
            c.OffsetTop = top; c.OffsetBottom = top + height;
        }

        /// <summary>Places a control in the centred column that stretches to the bottom (minus <paramref name="bottomPad"/>).</summary>
        public static void PlaceColumnStretch(Control c, float top, float bottomPad)
        {
            c.AnchorLeft = 0.5f; c.AnchorRight = 0.5f;
            c.AnchorTop = 0f; c.AnchorBottom = 1f;
            c.OffsetLeft = -Half; c.OffsetRight = Half;
            c.OffsetTop = top; c.OffsetBottom = -bottomPad;
        }

        /// <summary>Places a fixed-size control anchored to the RIGHT edge of the centred column.</summary>
        public static void PlaceColumnRight(Control c, float top, float height, float width)
        {
            c.AnchorLeft = 0.5f; c.AnchorRight = 0.5f;
            c.AnchorTop = 0f; c.AnchorBottom = 0f;
            c.OffsetRight = Half; c.OffsetLeft = Half - width;
            c.OffsetTop = top; c.OffsetBottom = top + height;
        }

        // ─── Two-pane placement (left list + right detail panel) ─────────────
        // The right panel reserves <see cref="ListRightReserve"/> on the right of the column;
        // the left list fills the remainder.

        /// <summary>Fixed-height control in the LEFT list region (column minus the reserved right panel).</summary>
        public static void PlaceListColumn(Control c, float top, float height)
        {
            c.AnchorLeft = 0.5f; c.AnchorRight = 0.5f;
            c.AnchorTop = 0f; c.AnchorBottom = 0f;
            c.OffsetLeft = -Half; c.OffsetRight = Half - ListRightReserve;
            c.OffsetTop = top; c.OffsetBottom = top + height;
        }

        /// <summary>Control in the LEFT list region that stretches to the bottom (minus <paramref name="bottomPad"/>).</summary>
        public static void PlaceListColumnStretch(Control c, float top, float bottomPad)
        {
            c.AnchorLeft = 0.5f; c.AnchorRight = 0.5f;
            c.AnchorTop = 0f; c.AnchorBottom = 1f;
            c.OffsetLeft = -Half; c.OffsetRight = Half - ListRightReserve;
            c.OffsetTop = top; c.OffsetBottom = -bottomPad;
        }

        /// <summary>The right detail panel: fixed width on the right edge of the column, stretching to the bottom.</summary>
        public static void PlaceDetailPanel(Control c, float top, float bottomPad)
        {
            c.AnchorLeft = 0.5f; c.AnchorRight = 0.5f;
            c.AnchorTop = 0f; c.AnchorBottom = 1f;
            c.OffsetRight = Half; c.OffsetLeft = Half - DetailPanelWidth;
            c.OffsetTop = top; c.OffsetBottom = -bottomPad;
        }

        /// <summary>The right detail panel as a compact, content-sized card anchored at the top.</summary>
        public static void PlaceDetailPanelTop(Control c, float top, float height)
        {
            c.AnchorLeft = 0.5f; c.AnchorRight = 0.5f;
            c.AnchorTop = 0f; c.AnchorBottom = 0f;
            c.OffsetRight = Half; c.OffsetLeft = Half - DetailPanelWidth;
            c.OffsetTop = top; c.OffsetBottom = top + height;
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
        public static PanelContainer MakePanel(Color bg, bool border = true, int borderWidth = 1, Color? borderColor = null)
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
                    style.BorderWidthTop = style.BorderWidthBottom = borderWidth;
                style.BorderColor = borderColor ?? Border;
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

        // ─── Bars (data viz without any PCK art) ─────────────────────────────

        /// <summary>
        /// A rounded, clipped bar track filled by proportional coloured segments. Segment widths are
        /// distributed by weight (via stretch ratios) alongside an optional empty remainder, so the
        /// bar resizes with the layout. Pass <paramref name="emptyWeight"/> &gt; 0 to make the filled
        /// portion represent a magnitude relative to some max (track shows for the remainder).
        /// </summary>
        public static Control MakeBarTrack(float height, (Color color, float weight)[] segments, float emptyWeight)
        {
            var track = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(60f, height),
                ClipContents = true,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            int radius = (int)(height / 2f);
            var style = new StyleBoxFlat
            {
                BgColor = BarTrack,
                CornerRadiusTopLeft = radius,
                CornerRadiusTopRight = radius,
                CornerRadiusBottomLeft = radius,
                CornerRadiusBottomRight = radius,
            };
            track.AddThemeStyleboxOverride("panel", style);

            var hbox = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            hbox.AddThemeConstantOverride("separation", 0);
            track.AddChild(hbox);

            // Identify the first/last drawn segments so we can round only the OUTER corners of the
            // fill — the left end always rounds to meet the groove; the right end rounds only when the
            // bar is completely full (no empty remainder). Interior segment joins stay square. This is
            // what makes the colour follow the pill shape instead of poking square corners out of it.
            float total = emptyWeight < 0f ? 0f : emptyWeight;
            int firstFilled = -1, lastFilled = -1;
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].weight <= 0f) continue;
                total += segments[i].weight;
                if (firstFilled < 0) firstFilled = i;
                lastFilled = i;
            }
            if (total <= 0f) return track; // nothing to draw — just the empty groove

            bool hasEmpty = emptyWeight > 0f;
            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (seg.weight <= 0f) continue;

                var fill = new Panel
                {
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsStretchRatio = seg.weight,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                var fillStyle = new StyleBoxFlat { BgColor = seg.color };
                if (i == firstFilled)
                {
                    fillStyle.CornerRadiusTopLeft = radius;
                    fillStyle.CornerRadiusBottomLeft = radius;
                }
                if (i == lastFilled && !hasEmpty)
                {
                    fillStyle.CornerRadiusTopRight = radius;
                    fillStyle.CornerRadiusBottomRight = radius;
                }
                fill.AddThemeStyleboxOverride("panel", fillStyle);
                hbox.AddChild(fill);
            }
            if (hasEmpty)
                hbox.AddChild(new Control
                {
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsStretchRatio = emptyWeight,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
            return track;
        }

        /// <summary>A row of: left label (fixed width) · bar (expands) · right value (fixed width).</summary>
        public static HBoxContainer MakeBarRow(string label, float labelWidth, Control bar, string value, float valueWidth)
        {
            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 10);

            var l = MakeLabel(label, Muted, BodyFontSize);
            l.CustomMinimumSize = new Vector2(labelWidth, 0f);
            l.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(l);

            bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(bar);

            var v = MakeLabel(value, Body, BodyFontSize, HorizontalAlignment.Right);
            v.CustomMinimumSize = new Vector2(valueWidth, 0f);
            v.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(v);

            return row;
        }

        /// <summary>
        /// A ranked-list row (M8, plan §4c): left <paramref name="name"/> that expands to fill,
        /// a fixed-width proportional bar showing <paramref name="fillWeight"/> out of
        /// <paramref name="maxWeight"/>, and a right-aligned <paramref name="value"/> column. The
        /// shared row shape behind card / relic / encounter / death lists (M9+). Sorting and a
        /// show-more affordance are layered on by the caller; this is just the row primitive.
        /// </summary>
        public static HBoxContainer MakeRankedRow(
            string name, string value, float fillWeight, float maxWeight, Color fill,
            float nameWidth = 150f, float valueWidth = 108f, float height = 16f)
        {
            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 10);

            // Fixed name column (clipped, full name on hover) so every bar in the list starts at the
            // same x. The bar then EXPANDS to fill the slack up to the fixed value column — no dead gap
            // between the name and a right-anchored bar, and bars line up start AND end across rows.
            var n = MakeLabel(name, Body, BodyFontSize);
            n.CustomMinimumSize = new Vector2(nameWidth, 0f);
            n.SizeFlagsHorizontal = Control.SizeFlags.Fill; // fixed at min width (no Expand)
            n.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            n.ClipText = true;
            n.TooltipText = name;
            row.AddChild(n);

            float empty = maxWeight > 0f ? Math.Max(0f, maxWeight - Math.Max(0f, fillWeight)) : 0f;
            var bar = MakeBarTrack(height, new[] { (fill, Math.Max(0f, fillWeight)) }, empty);
            bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; // fill the space between name and value
            bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(bar);

            var v = MakeLabel(value, Body, BodyFontSize, HorizontalAlignment.Right);
            v.CustomMinimumSize = new Vector2(valueWidth, 0f);
            v.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            v.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            v.ClipText = true;
            row.AddChild(v);

            return row;
        }

        /// <summary>Local copies of the theme override keys (avoids a dependency on the game's internal addon namespace).</summary>
        private static class ThemeConstantsLite
        {
            public const string FontSize = "font_size";
            public const string FontColor = "font_color";
        }
    }
}
