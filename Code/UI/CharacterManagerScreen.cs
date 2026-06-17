using System;
using System.Collections.Generic;
using CharacterManager.Config;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Saves;

namespace CharacterManager.UI
{
    /// <summary>
    /// The main Character Manager screen — a code-built NSubmenu (no .tscn). Pushed onto the menu
    /// stack from the Compendium.
    ///
    /// M6 (cont.): split into a two-pane layout — a left scrollable list (portrait, name, and the
    /// Stats + In-Select toggles) and a right detail panel for the selected character (large
    /// character-select portrait, W/L, and History / Analytics / Info buttons). Clicking anywhere
    /// on a row selects it; the first character is selected on open.
    ///
    /// Implementation notes:
    /// - We do NOT call base._Ready() — it checks GetType() == typeof(NSubmenu) and throws for subclasses.
    /// - We do NOT call base.ConnectSignals() — that would GetNode&lt;NBackButton&gt;("BackButton") and throw.
    /// - _stack is set by NSubmenuStack.SetStack(this) before we are pushed; it is valid on OnSubmenuOpened.
    /// </summary>
    public class CharacterManagerScreen : NSubmenu
    {
        private const float ColRowHeight = 34f;
        private const float ColWidth = 92f;
        private const float PortraitSize = 38f;
        private const float DetailImageHeight = 360f;
        // Compact card height: image + (name + W/L + two button rows + separations + margins).
        private const float DetailPanelHeight = DetailImageHeight + 215f;

        private VBoxContainer? _rowContainer;
        private VBoxContainer? _detailContent;       // right panel, rebuilt per selection
        private readonly List<RowVisual> _rows = new();
        private CharacterModel? _selected;

        private CharacterInfoScreen? _infoScreen;       // reused M2 drill-in
        private CharacterAnalyticsScreen? _analyticsScreen; // reused M4 drill-in

        protected override Control? InitialFocusedControl => null;

        public override void _Ready()
        {
            UiTheme.ApplyGameTheme(this);
            ConnectSignals();
            BuildLayout();
        }

        protected override void ConnectSignals() { /* see class note — base would look up a BackButton */ }

        public override void OnSubmenuOpened() => PopulateRows();

        public override void OnSubmenuClosed() => base.OnSubmenuClosed();

        // ─── One-time layout ──────────────────────────────────────────────────

        private void BuildLayout()
        {
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;

            AddChild(UiTheme.MakeBackdrop());

            float top = UiTheme.PaddingTop;

            var title = UiTheme.MakeLabel("Character Manager", UiTheme.Title, UiTheme.TitleFontSize);
            UiTheme.PlaceInColumn(title, top, UiTheme.HeaderHeight);
            AddChild(title);

            var backBtn = UiTheme.MakeButton("← Back", null, 120f);
            UiTheme.PlaceColumnRight(backBtn, top, UiTheme.HeaderHeight, 120f);
            backBtn.Pressed += () => _stack?.Pop();
            AddChild(backBtn);

            // ── Left list: column header ──
            float colY = top + UiTheme.HeaderHeight + 8f;
            var colPanel = UiTheme.MakePanel(UiTheme.PanelBg, border: false);
            UiTheme.PlaceListColumn(colPanel, colY, ColRowHeight);
            AddChild(colPanel);

            var colHbox = MakeHbox(colPanel, 10);
            colHbox.AddChild(new Control { CustomMinimumSize = new Vector2(PortraitSize, 0f) });
            AddColLabel(colHbox, "Character", SizeFlags.ExpandFill);
            AddColLabel(colHbox, "Stats", SizeFlags.ShrinkCenter, ColWidth);
            AddColLabel(colHbox, "In Select", SizeFlags.ShrinkCenter, ColWidth);

            // ── Left list: scrollable rows ──
            float scrollY = colY + ColRowHeight + 4f;
            var scroll = new ScrollContainer();
            UiTheme.PlaceListColumnStretch(scroll, scrollY, UiTheme.PaddingTop);
            AddChild(scroll);

            _rowContainer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _rowContainer.AddThemeConstantOverride("separation", UiTheme.RowSpacing);
            scroll.AddChild(_rowContainer);

            // ── Right detail panel ── compact, content-sized card at the top with a clear border.
            var detailPanel = UiTheme.MakePanel(UiTheme.PanelBg, border: true, borderWidth: 2, borderColor: UiTheme.Border);
            UiTheme.PlaceDetailPanelTop(detailPanel, colY, DetailPanelHeight);
            AddChild(detailPanel);

            _detailContent = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            _detailContent.AddThemeConstantOverride("separation", 12);
            detailPanel.AddChild(_detailContent);
        }

        // ─── Rows ─────────────────────────────────────────────────────────────

        private void PopulateRows()
        {
            if (_rowContainer == null) return;
            foreach (Node child in _rowContainer.GetChildren())
                child.QueueFree();
            _rows.Clear();
            _selected = null;

            var progress = SaveManager.Instance.Progress;
            int i = 0;
            CharacterModel? first = null;
            foreach (var character in CharacterHelper.GetAllCharacters())
            {
                first ??= character;
                bool isCustom = !CharacterHelper.IsBaseCharacter(character.Id);
                _rowContainer.AddChild(BuildCharacterRow(character, isCustom, progress, i++));
            }

            if (first != null) SelectCharacter(first);
            else BuildDetail(null);
        }

        private Control BuildCharacterRow(CharacterModel character, bool isCustom, ProgressState progress, int index)
        {
            var normal = MakeRowStyle(index % 2 == 0 ? UiTheme.RowBg : UiTheme.RowAltBg, selected: false);
            var selected = MakeRowStyle(UiTheme.RowAltBg, selected: true);

            var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            panel.CustomMinimumSize = new Vector2(0f, UiTheme.RowHeight);
            panel.AddThemeStyleboxOverride("panel", normal);
            panel.MouseFilter = MouseFilterEnum.Stop;
            panel.GuiInput += e =>
            {
                if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                {
                    SelectCharacter(character);
                    AcceptEvent();
                }
            };

            var hbox = MakeHbox(panel, 10);
            hbox.MouseFilter = MouseFilterEnum.Pass; // empty-area clicks fall through to the panel

            // Portrait (guarded — some custom characters may not provide one).
            hbox.AddChild(MakePortrait(character));

            // Name + source
            var nameCol = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Pass,
            };
            nameCol.AddThemeConstantOverride("separation", 0);

            var nameLbl = UiTheme.MakeLabel(character.Title.GetFormattedText(), UiTheme.Body, UiTheme.BodyFontSize + 3);
            nameLbl.MouseFilter = MouseFilterEnum.Ignore;
            nameCol.AddChild(nameLbl);

            string sourceText = isCustom ? GetSourceText(character) : "Base game";
            var srcLbl = UiTheme.MakeLabel(sourceText, UiTheme.Muted, UiTheme.SmallFontSize);
            srcLbl.MouseFilter = MouseFilterEnum.Ignore;
            nameCol.AddChild(srcLbl);
            hbox.AddChild(nameCol);

            // Stats-shown toggle (custom only; base always shown)
            if (isCustom)
                hbox.AddChild(MakeToggle(VisibilityStore.IsVisible(character.Id),
                    v => VisibilityStore.Toggle(character.Id)));
            else
                hbox.AddChild(MakeFixedLabel("Always", UiTheme.Muted, ColWidth));

            // In-select toggle (custom only)
            if (isCustom)
                hbox.AddChild(MakeToggle(EnabledStore.IsEnabled(character.Id),
                    v => EnabledStore.Toggle(character.Id)));
            else
                hbox.AddChild(MakeFixedLabel("—", UiTheme.Muted, ColWidth));

            _rows.Add(new RowVisual(character, panel, normal, selected));
            return panel;
        }

        // ─── Selection + detail panel ──────────────────────────────────────────

        private void SelectCharacter(CharacterModel character)
        {
            _selected = character;
            foreach (var row in _rows)
                row.Panel.AddThemeStyleboxOverride("panel",
                    row.Character.Id == character.Id ? row.Selected : row.Normal);
            BuildDetail(character);
        }

        private void BuildDetail(CharacterModel? character)
        {
            if (_detailContent == null) return;
            foreach (Node child in _detailContent.GetChildren())
                child.QueueFree();

            if (character == null)
            {
                _detailContent.AddChild(UiTheme.MakeLabel("No characters installed.",
                    UiTheme.Muted, UiTheme.BodyFontSize, HorizontalAlignment.Center));
                return;
            }

            // Name header
            var name = UiTheme.MakeLabel(character.Title.GetFormattedText(),
                UiTheme.Title, UiTheme.SectionFontSize + 3, HorizontalAlignment.Center);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _detailContent.AddChild(name);

            // Large portrait, framed
            _detailContent.AddChild(BuildImageFrame(character));

            // W/L line
            var stats = SaveManager.Instance.Progress.GetStatsForCharacter(character.Id);
            int wins = stats?.TotalWins ?? 0;
            int losses = stats?.TotalLosses ?? 0;
            bool hasHistory = wins > 0 || losses > 0;

            var wl = new HBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            wl.AddThemeConstantOverride("separation", 14);
            wl.AddChild(UiTheme.MakeLabel($"W: {wins}", UiTheme.Good, UiTheme.BodyFontSize + 2, HorizontalAlignment.Center));
            wl.AddChild(UiTheme.MakeLabel($"L: {losses}", UiTheme.Bad, UiTheme.BodyFontSize + 2, HorizontalAlignment.Center));
            _detailContent.AddChild(wl);

            // History + Analytics (side by side)
            var btnRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            btnRow.AddThemeConstantOverride("separation", 8);

            var histBtn = UiTheme.MakeButton("History");
            histBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            histBtn.Disabled = !hasHistory;
            histBtn.Pressed += () => OpenFilteredRunHistory(character.Id);
            btnRow.AddChild(histBtn);

            var analyticsBtn = UiTheme.MakeButton("Analytics");
            analyticsBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            analyticsBtn.Pressed += () => OpenAnalytics(character);
            btnRow.AddChild(analyticsBtn);
            _detailContent.AddChild(btnRow);

            // Info (full width, below)
            var infoBtn = UiTheme.MakeButton("Info");
            infoBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            infoBtn.Pressed += () => OpenInfo(character);
            _detailContent.AddChild(infoBtn);
        }

        /// <summary>
        /// A bordered frame holding the character's large portrait. The image is fit fully inside
        /// (KeepAspectCentered) over a dark matte, so it reads as an intentional framed portrait
        /// regardless of the source asset's aspect, with no extra cropping of the character.
        /// </summary>
        private static Control BuildImageFrame(CharacterModel character)
        {
            var frame = new PanelContainer
            {
                CustomMinimumSize = new Vector2(0f, DetailImageHeight),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipContents = true,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0f, 0f, 0f, 0.35f),
                CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
                BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
                BorderColor = UiTheme.Border,
                ContentMarginLeft = 2f, ContentMarginRight = 2f, ContentMarginTop = 2f, ContentMarginBottom = 2f,
            };
            frame.AddThemeStyleboxOverride("panel", style);

            var img = TryGetLargePortrait(character);
            if (img != null)
            {
                frame.AddChild(new TextureRect
                {
                    Texture = img,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    SizeFlagsVertical = SizeFlags.ExpandFill,
                    MouseFilter = MouseFilterEnum.Ignore,
                });
            }
            else
            {
                frame.AddChild(UiTheme.MakeLabel("(no image)", UiTheme.Muted, UiTheme.BodyFontSize, HorizontalAlignment.Center));
            }
            return frame;
        }

        /// <summary>The large character-select portrait, falling back to the small icon, then null.</summary>
        private static Texture2D? TryGetLargePortrait(CharacterModel character)
        {
            try
            {
                var t = character.CharacterSelectIcon;
                if (t != null) return t;
            }
            catch (Exception e) { Log.Warn("[CharacterManager] select icon load failed: " + e.Message); }

            try
            {
                var t = character.IconTexture;
                if (t != null) return t;
            }
            catch (Exception e) { Log.Warn("[CharacterManager] icon fallback load failed: " + e.Message); }

            return null;
        }

        // ─── Small builders ───────────────────────────────────────────────────

        private static StyleBoxFlat MakeRowStyle(Color bg, bool selected)
        {
            var style = new StyleBoxFlat
            {
                BgColor = selected ? UiTheme.RowAltBg.Lightened(0.08f) : bg,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                ContentMarginLeft = 14f,
                ContentMarginRight = 14f,
                ContentMarginTop = 8f,
                ContentMarginBottom = 8f,
            };
            int w = selected ? 2 : 1;
            style.BorderWidthLeft = style.BorderWidthRight = style.BorderWidthTop = style.BorderWidthBottom = w;
            style.BorderColor = selected ? UiTheme.Title : UiTheme.Border;
            return style;
        }

        private static Control MakePortrait(CharacterModel character)
        {
            var holder = new Control
            {
                CustomMinimumSize = new Vector2(PortraitSize, PortraitSize),
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
                        MouseFilter = MouseFilterEnum.Ignore,
                    });
                }
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] portrait load failed: " + e.Message);
            }
            return holder;
        }

        /// <summary>A Shown/Hidden toggle button. <paramref name="toggle"/> returns the new state.</summary>
        private static Button MakeToggle(bool active, Func<bool, bool> toggle)
        {
            var btn = UiTheme.MakeButton(active ? "Shown" : "Hidden", active ? UiTheme.Good : UiTheme.Bad, ColWidth);
            btn.Pressed += () =>
            {
                bool now = toggle(active);
                btn.Text = now ? "Shown" : "Hidden";
                btn.AddThemeColorOverride("font_color", now ? UiTheme.Good : UiTheme.Bad);
                active = now;
            };
            return btn;
        }

        private static Label MakeFixedLabel(string text, Color color, float width)
        {
            var lbl = UiTheme.MakeLabel(text, color, UiTheme.BodyFontSize, HorizontalAlignment.Center);
            lbl.CustomMinimumSize = new Vector2(width, 0f);
            lbl.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            lbl.MouseFilter = Control.MouseFilterEnum.Ignore;
            return lbl;
        }

        private static HBoxContainer MakeHbox(Container parent, int separation)
        {
            var hbox = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            hbox.AddThemeConstantOverride("separation", separation);
            parent.AddChild(hbox);
            return hbox;
        }

        private static void AddColLabel(HBoxContainer parent, string text, SizeFlags horizontal, float minWidth = 0f)
        {
            var lbl = UiTheme.MakeLabel(text, UiTheme.Muted, UiTheme.SmallFontSize,
                horizontal == SizeFlags.ExpandFill ? HorizontalAlignment.Left : HorizontalAlignment.Center);
            lbl.SizeFlagsHorizontal = horizontal;
            lbl.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            if (minWidth > 0f) lbl.CustomMinimumSize = new Vector2(minWidth, 0f);
            parent.AddChild(lbl);
        }

        private static string GetSourceText(CharacterModel character)
        {
            var mod = CharacterHelper.GetSourceMod(character);
            return mod != null ? $"{mod.manifest.name} v{mod.manifest.version}" : "Unknown mod";
        }

        // ─── Drill-ins ────────────────────────────────────────────────────────

        private void OpenInfo(CharacterModel character)
        {
            if (_stack == null) { Log.Error("[CharacterManager] _stack is null — cannot open info card."); return; }
            if (_infoScreen == null || !GodotObject.IsInstanceValid(_infoScreen))
            {
                _infoScreen = new CharacterInfoScreen { Visible = false };
                _stack.AddChild(_infoScreen);
            }
            _infoScreen.SetCharacter(character);
            _stack.Push(_infoScreen);
        }

        private void OpenAnalytics(CharacterModel character)
        {
            if (_stack == null) { Log.Error("[CharacterManager] _stack is null — cannot open analytics."); return; }
            if (_analyticsScreen == null || !GodotObject.IsInstanceValid(_analyticsScreen))
            {
                _analyticsScreen = new CharacterAnalyticsScreen { Visible = false };
                _stack.AddChild(_analyticsScreen);
            }
            _analyticsScreen.SetCharacter(character);
            _stack.Push(_analyticsScreen);
        }

        private void OpenFilteredRunHistory(ModelId characterId)
        {
            try
            {
                RunHistoryFilter.Character = characterId;
                _stack!.PushSubmenuType<NRunHistory>();
            }
            catch (Exception e)
            {
                Log.Error("[CharacterManager] Failed to open run history: " + e.Message);
                RunHistoryFilter.Character = null;
            }
        }

        // ─── Row bookkeeping ───────────────────────────────────────────────────

        private sealed class RowVisual
        {
            public readonly CharacterModel Character;
            public readonly PanelContainer Panel;
            public readonly StyleBoxFlat Normal;
            public readonly StyleBoxFlat Selected;

            public RowVisual(CharacterModel character, PanelContainer panel, StyleBoxFlat normal, StyleBoxFlat selected)
            {
                Character = character;
                Panel = panel;
                Normal = normal;
                Selected = selected;
            }
        }
    }
}
