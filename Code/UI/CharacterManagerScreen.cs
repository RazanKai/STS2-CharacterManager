using System;
using System.Collections.Generic;
using System.Reflection;
using CharacterManager.Analytics;
using CharacterManager.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
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
        private const float WinRateColWidth = 156f;   // M16: win-rate % + recent-results tick strip
        private const int SparkTicks = 20;            // most-recent decisive runs shown in the strip
        private const float PortraitSize = 38f;
        private const float DetailImageHeight = 360f;
        // Compact card height: image + (name + W/L + two button rows + separations + margins).
        private const float DetailPanelHeight = DetailImageHeight + 215f;

        // Column hover tooltips. The native Godot tooltip does not word-wrap, so these are
        // hard-wrapped with newlines to keep each line a sensible width.
        private const string WinRateTooltip =
            "Win rate and recent results across all run\n" +
            "history (every game mode). Each tick is one\n" +
            "run — green win, red loss, grey abandoned —\n" +
            "newest on the right.";
        private const string StatsTooltip =
            "Show this character's win/loss stats in the\n" +
            "in-game Compendium stats grid.";
        private const string InSelectTooltip =
            "Show this character on the Character Select\n" +
            "and Custom Run screens. Turn off to hide it\n" +
            "from those screens (it stays installed).\n" +
            "The roster can never be fully emptied.";
        private const string LendCardsTooltip =
            "Allow other characters' cross-pool effects —\n" +
            "Kaleidoscope, Colorful Philosophers, Splash,\n" +
            "Prismatic Gem, and Orobas/SeaGlass — to draw\n" +
            "cards and relics from this character's pool.\n" +
            "Turn off to exclude this character's pool as\n" +
            "a source for those effects.";

        private VBoxContainer? _rowContainer;
        private VBoxContainer? _detailContent;       // right panel, rebuilt per selection
        private readonly List<RowVisual> _rows = new();
        private CharacterModel? _selected;

        // How abandoned runs are treated in the Win Rate column (cycled by clicking the header):
        //  Hidden  — excluded from both the strip and the %  (default)
        //  Shown   — shown as grey ticks in the strip, but NOT counted in the %
        //  Counted — shown as grey ticks AND counted as losses in the %
        private enum AbandonMode { Hidden, Shown, Counted }
        private Button? _winRateHeaderBtn;           // clickable header cycling the mode below
        private AbandonMode _abandonMode = AbandonMode.Hidden;

        // Detail-panel W/L scope (cycled by clicking the W/L line):
        //  Official     — the game's Standard-only ranked tallies (excludes Custom/Daily)
        //  AllDecisive  — wins + losses across every mode (abandons excluded)
        //  AllRuns      — every mode including abandoned (shown as a separate A count)
        private enum WlScope { Official, AllDecisive, AllRuns }
        private WlScope _wlScope = WlScope.Official;

        private CharacterInfoScreen? _infoScreen;       // reused M2 drill-in
        private CharacterAnalyticsScreen? _analyticsScreen; // reused M4 drill-in
        private CharacterHelpScreen? _helpScreen;       // reused M16 features/help drill-in

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

            // "?" Help button, just left of Back — opens the features/how-it-works screen (M16).
            var helpBtn = UiTheme.MakeButton("?", UiTheme.Title, 44f);
            helpBtn.TooltipText = "How this screen works — features & toggles explained.";
            float half = UiTheme.MaxContentWidth / 2f;
            helpBtn.AnchorLeft = 0.5f; helpBtn.AnchorRight = 0.5f;
            helpBtn.AnchorTop = 0f; helpBtn.AnchorBottom = 0f;
            helpBtn.OffsetRight = half - 132f;        // 120 (Back) + 12 gap
            helpBtn.OffsetLeft = half - 132f - 44f;
            helpBtn.OffsetTop = top; helpBtn.OffsetBottom = top + UiTheme.HeaderHeight;
            helpBtn.Pressed += OpenHelp;
            AddChild(helpBtn);

            // ── Left list: column header ──
            float colY = top + UiTheme.HeaderHeight + 8f;
            var colPanel = UiTheme.MakePanel(UiTheme.PanelBg, border: false);
            UiTheme.PlaceListColumn(colPanel, colY, ColRowHeight);
            AddChild(colPanel);

            var colHbox = MakeHbox(colPanel, 10);
            colHbox.AddChild(new Control { CustomMinimumSize = new Vector2(PortraitSize, 0f) });
            AddColLabel(colHbox, "Character", SizeFlags.ExpandFill);

            // Win Rate is a clickable header (M16): cycles how abandoned runs are treated. FocusMode
            // None so it never stays "selected" after a click (Godot keeps focus otherwise, which
            // both sticks until another control is clicked and overrides our state colour with white).
            _winRateHeaderBtn = UiTheme.MakeButton("Win Rate", UiTheme.Muted, WinRateColWidth);
            _winRateHeaderBtn.AddThemeFontSizeOverride("font_size", UiTheme.SmallFontSize);
            _winRateHeaderBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            _winRateHeaderBtn.FocusMode = FocusModeEnum.None;
            _winRateHeaderBtn.Pressed += CycleAbandonMode;
            colHbox.AddChild(_winRateHeaderBtn);
            UpdateWinRateHeader();

            AddColLabel(colHbox, "Stats", SizeFlags.ShrinkCenter, ColWidth, StatsTooltip);
            AddColLabel(colHbox, "In Select", SizeFlags.ShrinkCenter, ColWidth, InSelectTooltip);
            AddColLabel(colHbox, "Lend Cards", SizeFlags.ShrinkCenter, ColWidth, LendCardsTooltip);

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

            // Win-rate sparkline: % + recent-results tick strip (M16). One shared history pass
            // (RosterWinHistory) fills every row, so this is cheap even for a long roster. The holder
            // is rebuilt in place when the Win Rate header toggles abandoned-run visibility.
            var sparkHolder = MakeSparkHolder();
            RebuildSparkline(sparkHolder, RosterWinHistory.Get(character.Id));
            hbox.AddChild(sparkHolder);

            // Stats-shown toggle (custom only; base always shown)
            if (isCustom)
                hbox.AddChild(MakeToggle(VisibilityStore.IsVisible(character.Id),
                    v => VisibilityStore.Toggle(character.Id)));
            else
                hbox.AddChild(MakeFixedLabel("Always", UiTheme.Muted, ColWidth));

            // In-select toggle (base + custom: any character can be hidden from the select screens).
            // Tooltips now live on the column headers + the ? Help screen (M16), not every button.
            hbox.AddChild(MakeToggle(EnabledStore.IsEnabled(character.Id),
                v => EnabledStore.Toggle(character.Id)));

            // Cross-source toggle (base + custom: any character's pool can be excluded as a cross-source).
            // Yes/No reads more naturally than Shown/Hidden for an eligibility flag (M16).
            hbox.AddChild(MakeToggle(CrossSourceStore.IsEligible(character.Id),
                v => CrossSourceStore.Toggle(character.Id), "Yes", "No"));

            _rows.Add(new RowVisual(character, panel, normal, selected, sparkHolder));
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

            // W/L line — click to cycle scope: Standard official → all runs → all + abandoned (M16).
            var official = SaveManager.Instance.Progress.GetStatsForCharacter(character.Id);
            var allRuns = RosterWinHistory.Get(character.Id);
            bool hasHistory = (official?.TotalWins ?? 0) + (official?.TotalLosses ?? 0) > 0
                || allRuns.Wins + allRuns.Losses + allRuns.Abandoned > 0;

            (int wlWins, int wlLosses, int wlAband, string wlCaption, bool showAband) = _wlScope switch
            {
                WlScope.AllDecisive => (allRuns.Wins, allRuns.Losses, 0, "All runs · every mode", false),
                WlScope.AllRuns => (allRuns.Wins, allRuns.Losses, allRuns.Abandoned, "All runs · incl. abandoned", true),
                _ => (official?.TotalWins ?? 0, official?.TotalLosses ?? 0, 0, "Standard · official", false),
            };

            var wlBox = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Stop, // receive clicks to cycle scope
            };
            wlBox.AddThemeConstantOverride("separation", 0);
            wlBox.TooltipText =
                "Win / loss scope — click to cycle:\n" +
                "Standard (official) → all runs → all runs + abandoned.\n" +
                "Official counts only Standard mode; the others include Custom and Daily.";
            wlBox.GuiInput += e =>
            {
                if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                {
                    CycleWlScope();
                    AcceptEvent();
                }
            };

            var wl = new HBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            wl.AddThemeConstantOverride("separation", 14);
            wl.AddChild(UiTheme.MakeLabel($"W: {wlWins}", UiTheme.Good, UiTheme.BodyFontSize + 2, HorizontalAlignment.Center));
            wl.AddChild(UiTheme.MakeLabel($"L: {wlLosses}", UiTheme.Bad, UiTheme.BodyFontSize + 2, HorizontalAlignment.Center));
            if (showAband)
                wl.AddChild(UiTheme.MakeLabel($"A: {wlAband}", UiTheme.Muted, UiTheme.BodyFontSize + 2, HorizontalAlignment.Center));
            wlBox.AddChild(wl);

            var wlCap = UiTheme.MakeLabel(wlCaption, UiTheme.Muted, UiTheme.SmallFontSize, HorizontalAlignment.Center);
            wlCap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            wlCap.MouseFilter = MouseFilterEnum.Ignore;
            wlBox.AddChild(wlCap);

            _detailContent.AddChild(wlBox);

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
        /// A bordered frame holding the character's live animated visuals (via CreateVisuals) or,
        /// as a fallback, the large static character-select portrait. The live visuals are scaled
        /// to fit the frame; the static image uses KeepAspectCentered over a dark matte.
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

            // Try live animated visuals first
            if (TryAttachLiveVisuals(frame, character))
                return frame;

            // Fallback: static portrait
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

        // Internal pixel size of the off-screen viewport the creature renders into. The TextureRect
        // displays this with KeepAspectCentered, so the exact numbers only set the render resolution
        // and the working coordinate space for placement — not the on-screen size.
        private const int VisualsViewportW = 340;
        private const int VisualsViewportH = 360;

        // CharacterModel.VisualsPath is private; cache its getter so we can resolve the scene path
        // (correct for base AND modded characters) without going through CreateVisuals().
        private static readonly MethodInfo? VisualsPathGetter =
            AccessTools.PropertyGetter(typeof(CharacterModel), "VisualsPath");

        /// <summary>
        /// Attempts to render the character's live combat visuals into the frame. Returns true only
        /// when a creature node was actually loaded and attached; on any failure returns false so the
        /// caller falls back to the static portrait.
        ///
        /// <para>Two problems from the first attempt are addressed here:</para>
        /// <list type="bullet">
        /// <item>Crash: <see cref="CharacterModel.CreateVisuals"/> routes through the game's
        /// <c>AssetCache.LoadAsset</c>. On a menu the creature scene isn't preloaded, so that is a
        /// cold load — which other mods (e.g. Ryoshu) patch and fatally crash on. We load the scene
        /// directly via <see cref="ResourceLoader"/> instead, bypassing that patched path.</item>
        /// <item>Black frame: <see cref="NCreatureVisuals"/> is a <c>Node2D</c>; parenting it into a
        /// Control renders nothing. We host it in a <see cref="SubViewport"/> and display the
        /// viewport texture, positioning the creature with its own <c>Bounds</c> marker.</item>
        /// </list>
        /// </summary>
        private static bool TryAttachLiveVisuals(PanelContainer frame, CharacterModel character)
        {
            NCreatureVisuals? visuals = null;
            try
            {
                if (VisualsPathGetter?.Invoke(character, null) is not string path || string.IsNullOrEmpty(path))
                    return false;

                // Bypass AssetCache (and any mod patches on it) by loading the PackedScene directly.
                var scene = ResourceLoader.Load<PackedScene>(path, null, ResourceLoader.CacheMode.Reuse);
                if (scene == null) return false;

                visuals = scene.Instantiate<NCreatureVisuals>(PackedScene.GenEditState.Disabled);
                if (visuals == null || !GodotObject.IsInstanceValid(visuals)) return false;
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] live visuals load failed for " + character.Id.Entry + ": " + e.Message);
                visuals?.QueueFree();
                return false;
            }

            // Off-screen viewport that actually renders the Node2D.
            var viewport = new SubViewport
            {
                Size = new Vector2I(VisualsViewportW, VisualsViewportH),
                TransparentBg = true,
                Disable3D = true,
                RenderTargetClearMode = SubViewport.ClearMode.Always,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            frame.AddChild(viewport);
            viewport.AddChild(visuals); // triggers _Ready (sets up Bounds + spine body)

            // Initial best-effort placement (refined once _Ready has populated Bounds).
            float s0 = (VisualsViewportH * 0.82f) / 400f;
            visuals.SetScaleAndHue(s0, 0f);
            visuals.Position = new Vector2(VisualsViewportW / 2f, VisualsViewportH * 0.9f);

            // Display the viewport texture in the Control tree, fit inside the frame.
            frame.AddChild(new TextureRect
            {
                Texture = viewport.GetTexture(),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore,
            });

            // After _Ready: start the idle loop and fit precisely using the creature's Bounds.
            Callable.From(() => FitAndAnimate(viewport, visuals)).CallDeferred();
            return true;
        }

        /// <summary>
        /// Runs one frame after the visuals enter the tree: plays the idle animation and rescales /
        /// repositions the creature so its <c>Bounds</c> sit centred near the bottom of the viewport.
        /// All best-effort — guarded so a missing animation or null Bounds never throws.
        /// </summary>
        private static void FitAndAnimate(SubViewport viewport, NCreatureVisuals visuals)
        {
            if (!GodotObject.IsInstanceValid(visuals)) return;

            try { visuals.SpineAnimation.SetAnimation("idle_loop", true); }
            catch (Exception e) { Log.Warn("[CharacterManager] idle anim failed: " + e.Message); }

            try
            {
                var bounds = visuals.Bounds;
                if (bounds == null || bounds.Size.Y <= 1f) return;

                var size = bounds.Size;                       // local (pre-scale) bounds
                // Fit the bounds to ~70% of the viewport height. Bounds is the *body* rect; raised
                // weapons, horns, and flames extend above it, so the remaining ~25% top margin is
                // deliberate headroom for that overflow.
                float s = (VisualsViewportH * 0.70f) / size.Y;
                visuals.SetScaleAndHue(s, 0f);

                // Align bounds centre-x to the viewport centre, and bounds bottom near the floor.
                float bottom = bounds.Position.Y + size.Y;
                float centerX = bounds.Position.X + size.X / 2f;
                visuals.Position = new Vector2(
                    VisualsViewportW / 2f - centerX * s,
                    VisualsViewportH * 0.95f - bottom * s);
            }
            catch (Exception e) { Log.Warn("[CharacterManager] visuals fit failed: " + e.Message); }
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

        /// <summary>
        /// A two-state toggle button. <paramref name="toggle"/> returns the new state. The on/off
        /// labels default to Shown/Hidden but can be overridden (e.g. Yes/No for Lend Cards — M16).
        /// </summary>
        private static Button MakeToggle(bool active, Func<bool, bool> toggle,
            string onText = "Shown", string offText = "Hidden")
        {
            var btn = UiTheme.MakeButton(active ? onText : offText, active ? UiTheme.Good : UiTheme.Bad, ColWidth);
            btn.FocusMode = FocusModeEnum.None; // don't keep a "selected" highlight after clicking
            btn.Pressed += () =>
            {
                bool now = toggle(active);
                btn.Text = now ? onText : offText;
                btn.AddThemeColorOverride("font_color", now ? UiTheme.Good : UiTheme.Bad);
                active = now;
            };
            return btn;
        }

        // Tick strip geometry. Kept integer so ticks land on whole pixels.
        private const int TickW = 5;
        private const int TickH = 12;
        private const int TickGap = 2;

        /// <summary>Cycles the detail W/L scope (official → all → all+abandoned) and rebuilds the panel.</summary>
        private void CycleWlScope()
        {
            _wlScope = (WlScope)(((int)_wlScope + 1) % 3);
            if (_selected != null) BuildDetail(_selected);
        }

        /// <summary>Cycles Hidden → Shown → Counted and rebuilds every row's cell in place.</summary>
        private void CycleAbandonMode()
        {
            _abandonMode = (AbandonMode)(((int)_abandonMode + 1) % 3);
            UpdateWinRateHeader();
            foreach (var row in _rows)
                RebuildSparkline(row.SparkHolder, RosterWinHistory.Get(row.Character.Id));
        }

        // Orange signals "abandons now hurt the %" — distinct from the gold "shown only" state.
        private static readonly Color CountedColor = new("ff9933");

        /// <summary>Reflects the abandon mode on the header via colour + tooltip (text stays "Win Rate").</summary>
        private void UpdateWinRateHeader()
        {
            if (_winRateHeaderBtn == null) return;

            (Color color, string state) = _abandonMode switch
            {
                AbandonMode.Shown => (UiTheme.Title,
                    "Abandoned runs shown as grey ticks — not counted in the %."),
                AbandonMode.Counted => (CountedColor,
                    "Abandoned runs shown as grey ticks AND counted as losses in the %."),
                _ => (UiTheme.Muted,
                    "Abandoned runs hidden — excluded from the strip and the %."),
            };

            // Override every state's font colour so the focus/hover/pressed white never hides it.
            foreach (var key in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
                _winRateHeaderBtn.AddThemeColorOverride(key, color);

            _winRateHeaderBtn.TooltipText = WinRateTooltip + "\n\n" + state + "\nClick to cycle.";
        }

        /// <summary>The stable per-row container the win-rate cell is (re)built into.</summary>
        private static VBoxContainer MakeSparkHolder()
        {
            var cell = new VBoxContainer
            {
                CustomMinimumSize = new Vector2(WinRateColWidth, 0f),
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            cell.AddThemeConstantOverride("separation", 3);
            return cell;
        }

        /// <summary>
        /// (Re)builds the win-rate cell (M16): a coloured win-rate % over a strip of recent-result
        /// ticks (green win · red loss · grey abandoned when toggled on), oldest left → newest right.
        /// Honours <see cref="_showAbandonedTicks"/>. The % itself is always decisive-only.
        /// </summary>
        private void RebuildSparkline(Control holder, RosterWinHistory.Series series)
        {
            foreach (Node child in holder.GetChildren())
                child.QueueFree();

            bool includeAbandoned = _abandonMode != AbandonMode.Hidden;
            bool countAbandoned = _abandonMode == AbandonMode.Counted;
            double rate = series.WinRatePctCounting(countAbandoned);
            var recent = series.Recent(SparkTicks, includeAbandoned);
            if (recent.Count == 0 && rate < 0)
            {
                holder.AddChild(MakeFixedLabel("—", UiTheme.Muted, WinRateColWidth));
                return;
            }

            var pct = UiTheme.MakeLabel(rate < 0 ? "—" : $"{rate:0}%",
                RateColor(rate), UiTheme.BodyFontSize, HorizontalAlignment.Center);
            pct.CustomMinimumSize = new Vector2(WinRateColWidth, 0f);
            pct.TooltipText = $"{series.Wins}W · {series.Losses}L"
                + (series.Abandoned > 0 ? $" · {series.Abandoned} abandoned" : "")
                + $"  ({series.Decisive} decisive)";
            pct.MouseFilter = MouseFilterEnum.Stop; // enable the W/L hover tooltip
            holder.AddChild(pct);

            holder.AddChild(BuildTickStrip(recent));
        }

        /// <summary>
        /// A fixed-size <see cref="Control"/> holding the tick rects at integer offsets, so the strip
        /// shifts as a whole when centred instead of each tick being re-rounded independently by a
        /// container (which is what made the spacing look uneven). ColorRects, not custom <c>_Draw</c>
        /// (see CLAUDE.md).
        /// </summary>
        private static Control BuildTickStrip(List<RosterWinHistory.Outcome> recent)
        {
            int n = recent.Count;
            int pitch = TickW + TickGap;
            var strip = new Control
            {
                CustomMinimumSize = new Vector2(n > 0 ? n * pitch - TickGap : 0f, TickH),
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            for (int i = 0; i < n; i++)
            {
                strip.AddChild(new ColorRect
                {
                    Color = TickColor(recent[i]),
                    Position = new Vector2(i * pitch, 0f),
                    Size = new Vector2(TickW, TickH),
                    MouseFilter = MouseFilterEnum.Ignore,
                });
            }
            return strip;
        }

        private static Color TickColor(RosterWinHistory.Outcome o) => o switch
        {
            RosterWinHistory.Outcome.Win => UiTheme.Good,
            RosterWinHistory.Outcome.Loss => UiTheme.Bad,
            _ => UiTheme.Muted,
        };

        /// <summary>Colour for a win-rate %: green ≥50, gold ≥30, red below (muted when unknown).</summary>
        private static Color RateColor(double pct)
        {
            if (pct < 0) return UiTheme.Muted;
            if (pct >= 50.0) return UiTheme.Good;
            if (pct >= 30.0) return UiTheme.Title;
            return UiTheme.Bad;
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

        private static void AddColLabel(HBoxContainer parent, string text, SizeFlags horizontal, float minWidth = 0f, string? tooltip = null)
        {
            var lbl = UiTheme.MakeLabel(text, UiTheme.Muted, UiTheme.SmallFontSize,
                horizontal == SizeFlags.ExpandFill ? HorizontalAlignment.Left : HorizontalAlignment.Center);
            lbl.SizeFlagsHorizontal = horizontal;
            lbl.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            if (minWidth > 0f) lbl.CustomMinimumSize = new Vector2(minWidth, 0f);
            if (!string.IsNullOrEmpty(tooltip))
            {
                lbl.TooltipText = tooltip;
                // Labels default to MouseFilter.Ignore, which suppresses hover — needed for the tooltip.
                lbl.MouseFilter = MouseFilterEnum.Stop;
            }
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

        private void OpenHelp()
        {
            if (_stack == null) { Log.Error("[CharacterManager] _stack is null — cannot open help."); return; }
            if (_helpScreen == null || !GodotObject.IsInstanceValid(_helpScreen))
            {
                _helpScreen = new CharacterHelpScreen { Visible = false };
                _stack.AddChild(_helpScreen);
            }
            _stack.Push(_helpScreen);
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
            public readonly Control SparkHolder;   // rebuilt in place on the abandoned-ticks toggle

            public RowVisual(CharacterModel character, PanelContainer panel, StyleBoxFlat normal,
                StyleBoxFlat selected, Control sparkHolder)
            {
                Character = character;
                Panel = panel;
                Normal = normal;
                Selected = selected;
                SparkHolder = sparkHolder;
            }
        }
    }
}
