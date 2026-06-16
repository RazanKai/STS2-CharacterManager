using System;
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
    /// stack from the Compendium. M6 restyle: denser rows, the game palette (<see cref="UiTheme"/>),
    /// row portraits, and the inherited game theme/font.
    ///
    /// Implementation notes:
    /// - We do NOT call base._Ready() — it checks GetType() == typeof(NSubmenu) and throws for subclasses.
    /// - We do NOT call base.ConnectSignals() — that would GetNode<NBackButton>("BackButton") and throw.
    /// - _stack is set by NSubmenuStack.SetStack(this) before we are pushed; it is valid on OnSubmenuOpened.
    /// </summary>
    public class CharacterManagerScreen : NSubmenu
    {
        private const float ColRowHeight = 34f;
        private const float ColWidth = 92f;
        private const float StatsColWidth = 70f;
        private const float PortraitSize = 38f;

        private VBoxContainer? _rowContainer;
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
            title.AnchorRight = 1f;
            title.OffsetLeft = UiTheme.PaddingH;
            title.OffsetRight = -UiTheme.PaddingH;
            title.OffsetTop = top;
            title.OffsetBottom = top + UiTheme.HeaderHeight;
            AddChild(title);

            var backBtn = UiTheme.MakeButton("← Back", null, 120f);
            backBtn.AnchorLeft = 1f;
            backBtn.AnchorRight = 1f;
            backBtn.OffsetLeft = -120f - UiTheme.PaddingH;
            backBtn.OffsetRight = -UiTheme.PaddingH;
            backBtn.OffsetTop = top;
            backBtn.OffsetBottom = top + UiTheme.HeaderHeight;
            backBtn.Pressed += () => _stack?.Pop();
            AddChild(backBtn);

            // Column header
            float colY = top + UiTheme.HeaderHeight + 8f;
            var colPanel = UiTheme.MakePanel(UiTheme.PanelBg, border: false);
            colPanel.AnchorRight = 1f;
            colPanel.OffsetLeft = UiTheme.PaddingH;
            colPanel.OffsetRight = -UiTheme.PaddingH;
            colPanel.OffsetTop = colY;
            colPanel.OffsetBottom = colY + ColRowHeight;
            AddChild(colPanel);

            var colHbox = MakeHbox(colPanel, 10);
            // Spacer matching the portrait column so headers line up with row content.
            colHbox.AddChild(new Control { CustomMinimumSize = new Vector2(PortraitSize, 0f) });
            AddColLabel(colHbox, "Character", SizeFlags.ExpandFill);
            AddColLabel(colHbox, "Stats", SizeFlags.ShrinkCenter, ColWidth);
            AddColLabel(colHbox, "In Select", SizeFlags.ShrinkCenter, ColWidth);
            AddColLabel(colHbox, "W/L", SizeFlags.ShrinkCenter, StatsColWidth);
            AddColLabel(colHbox, "History", SizeFlags.ShrinkCenter, ColWidth);
            AddColLabel(colHbox, "Analytics", SizeFlags.ShrinkCenter, ColWidth);

            // Scrollable rows
            float scrollY = colY + ColRowHeight + 4f;
            var scroll = new ScrollContainer
            {
                AnchorRight = 1f,
                AnchorBottom = 1f,
                OffsetLeft = UiTheme.PaddingH,
                OffsetRight = -UiTheme.PaddingH,
                OffsetTop = scrollY,
                OffsetBottom = -UiTheme.PaddingTop,
            };
            AddChild(scroll);

            _rowContainer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _rowContainer.AddThemeConstantOverride("separation", UiTheme.RowSpacing);
            scroll.AddChild(_rowContainer);
        }

        // ─── Rows ─────────────────────────────────────────────────────────────

        private void PopulateRows()
        {
            if (_rowContainer == null) return;
            foreach (Node child in _rowContainer.GetChildren())
                child.QueueFree();

            var progress = SaveManager.Instance.Progress;
            int i = 0;
            foreach (var character in CharacterHelper.GetAllCharacters())
            {
                bool isCustom = !CharacterHelper.IsBaseCharacter(character.Id);
                _rowContainer.AddChild(BuildCharacterRow(character, isCustom, progress, i++));
            }
        }

        private Control BuildCharacterRow(CharacterModel character, bool isCustom, ProgressState progress, int index)
        {
            var panel = UiTheme.MakePanel(index % 2 == 0 ? UiTheme.RowBg : UiTheme.RowAltBg);
            panel.CustomMinimumSize = new Vector2(0f, UiTheme.RowHeight);

            var hbox = MakeHbox(panel, 10);

            // Portrait (guarded — some custom characters may not provide one).
            hbox.AddChild(MakePortrait(character));

            // Name + source
            var nameCol = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            nameCol.AddThemeConstantOverride("separation", 0);

            var nameBtn = new Button
            {
                Text = character.Title.GetFormattedText(),
                Flat = true,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Alignment = HorizontalAlignment.Left,
                TooltipText = "View details",
            };
            nameBtn.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontSize + 3);
            nameBtn.AddThemeColorOverride("font_color", UiTheme.Body);
            nameBtn.AddThemeColorOverride("font_hover_color", UiTheme.Title);
            nameBtn.Pressed += () => OpenInfo(character);
            nameCol.AddChild(nameBtn);

            string sourceText = isCustom ? GetSourceText(character) : "Base game";
            nameCol.AddChild(UiTheme.MakeLabel(sourceText, UiTheme.Muted, UiTheme.SmallFontSize));
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

            // W/L
            var stats = progress.GetStatsForCharacter(character.Id);
            hbox.AddChild(MakeWinLoss(stats));

            // History
            bool hasHistory = stats != null && (stats.TotalWins > 0 || stats.TotalLosses > 0);
            var histBtn = UiTheme.MakeButton("History", null, ColWidth);
            histBtn.Disabled = !hasHistory;
            histBtn.Pressed += () => OpenFilteredRunHistory(character.Id);
            hbox.AddChild(histBtn);

            // Analytics
            var analyticsBtn = UiTheme.MakeButton("Analytics", null, ColWidth);
            analyticsBtn.Pressed += () => OpenAnalytics(character);
            hbox.AddChild(analyticsBtn);

            return panel;
        }

        // ─── Small builders ───────────────────────────────────────────────────

        private static Control MakePortrait(CharacterModel character)
        {
            var holder = new Control { CustomMinimumSize = new Vector2(PortraitSize, PortraitSize) };
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

        private static Control MakeWinLoss(CharacterStats? stats)
        {
            var box = new VBoxContainer
            {
                CustomMinimumSize = new Vector2(StatsColWidth, 0f),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            box.AddThemeConstantOverride("separation", 0);
            if (stats == null)
            {
                box.AddChild(UiTheme.MakeLabel("—", UiTheme.Muted, UiTheme.BodyFontSize, HorizontalAlignment.Center));
                return box;
            }
            box.AddChild(UiTheme.MakeLabel($"W {stats.TotalWins}", UiTheme.Good, UiTheme.SmallFontSize + 1, HorizontalAlignment.Center));
            box.AddChild(UiTheme.MakeLabel($"L {stats.TotalLosses}", UiTheme.Bad, UiTheme.SmallFontSize + 1, HorizontalAlignment.Center));
            return box;
        }

        private static Label MakeFixedLabel(string text, Color color, float width)
        {
            var lbl = UiTheme.MakeLabel(text, color, UiTheme.BodyFontSize, HorizontalAlignment.Center);
            lbl.CustomMinimumSize = new Vector2(width, 0f);
            lbl.SizeFlagsVertical = SizeFlags.ShrinkCenter;
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
    }
}
