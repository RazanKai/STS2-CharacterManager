using System;
using System.Collections.Generic;
using CharacterManager.Config;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Saves;

namespace CharacterManager.UI
{
    /// <summary>
    /// The main Character Manager screen — a full-screen NSubmenu subclass built entirely
    /// in code (no .tscn scene). Pushed onto the menu stack from the Compendium.
    ///
    /// Layout:
    ///   ┌─────────────────────────────────────────┐
    ///   │  CHARACTER MANAGER             [← Back] │
    ///   ├─────────────────────────────────────────┤
    ///   │  Column header (sticky)                  │
    ///   │  ScrollContainer with character rows     │
    ///   └─────────────────────────────────────────┘
    ///
    /// Implementation notes:
    /// - We do NOT call base._Ready() — it checks GetType() == typeof(NSubmenu) and throws for subclasses.
    /// - We do NOT call base.ConnectSignals() — that would GetNode<NBackButton>("BackButton") and throw.
    /// - NSubmenuStack.Push/Pop never call HideBackButtonImmediately, so _backButton=null is safe.
    /// - _stack is set by NSubmenuStack.SetStack(this) before we are pushed; it is valid on OnSubmenuOpened.
    /// </summary>
    public class CharacterManagerScreen : NSubmenu
    {
        // ─── Layout constants ────────────────────────────────────────────────────
        private const float PaddingH = 80f;
        private const float PaddingTop = 40f;
        private const float HeaderHeight = 72f;
        private const float ColRowHeight = 48f;
        private const float RowHeight = 90f;
        private const int RowSpacing = 8;

        private static readonly Color BgColor = new Color(0.08f, 0.08f, 0.12f, 0.96f);
        private static readonly Color HeaderColor = new Color(0.85f, 0.72f, 0.4f);
        private static readonly Color MutedColor = new Color(0.55f, 0.55f, 0.6f);
        private static readonly Color GreenColor = new Color(0.25f, 0.75f, 0.35f);
        private static readonly Color RedColor = new Color(0.8f, 0.25f, 0.2f);

        // ─── Child nodes built in _Ready ────────────────────────────────────────
        private VBoxContainer? _rowContainer;

        // ─── NSubmenu contract ────────────────────────────────────────────────
        protected override Control? InitialFocusedControl => null;

        // ─── Godot entry point ────────────────────────────────────────────────
        public override void _Ready()
        {
            // Do NOT call base._Ready() — it throws for subclasses by design.
            ConnectSignals();
            BuildLayout();
        }

        protected override void ConnectSignals()
        {
            // Do NOT call base.ConnectSignals() — that GetNode<NBackButton>("BackButton") would throw.
            // Push/Pop in NSubmenuStack only set Visible=true/false; they never touch _backButton,
            // and OnScreenVisibilityChange (which does) is only wired via base.ConnectSignals().
        }

        public override void OnSubmenuOpened()
        {
            PopulateRows();
        }

        public override void OnSubmenuClosed()
        {
            base.OnSubmenuClosed(); // clears _lastFocusedControl — safe (doesn't access _backButton)
        }

        // ─── One-time layout construction ────────────────────────────────────

        private void BuildLayout()
        {
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;

            // Background
            var bg = new ColorRect
            {
                Color = BgColor,
                AnchorRight = 1f,
                AnchorBottom = 1f,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(bg);

            // Title
            var title = new Label
            {
                Text = "Character Manager",
                AnchorRight = 1f,
                OffsetLeft = PaddingH,
                OffsetRight = -PaddingH,
                OffsetTop = PaddingTop,
                OffsetBottom = PaddingTop + HeaderHeight,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            title.AddThemeFontSizeOverride("font_size", 38);
            title.AddThemeColorOverride("font_color", HeaderColor);
            AddChild(title);

            // Back button
            var backBtn = new Button
            {
                Text = "← Back",
                AnchorLeft = 1f,
                AnchorRight = 1f,
                OffsetLeft = -190f,
                OffsetRight = -PaddingH,
                OffsetTop = PaddingTop,
                OffsetBottom = PaddingTop + HeaderHeight,
            };
            backBtn.AddThemeFontSizeOverride("font_size", 20);
            backBtn.Pressed += () => _stack?.Pop();
            AddChild(backBtn);

            // Column header bar
            float colY = PaddingTop + HeaderHeight + 12f;
            var colPanel = MakePanel(new Color(0.18f, 0.18f, 0.22f, 0.9f));
            colPanel.AnchorRight = 1f;
            colPanel.OffsetLeft = PaddingH;
            colPanel.OffsetRight = -PaddingH;
            colPanel.OffsetTop = colY;
            colPanel.OffsetBottom = colY + ColRowHeight;
            AddChild(colPanel);

            var colHbox = AddHbox(colPanel, 12);
            AddColLabel(colHbox, "Character", SizeFlags.ExpandFill, 17);
            foreach (var txt in new[] { "Stats Shown", "In Select", "Stats", "History" })
                AddColLabel(colHbox, txt, SizeFlags.ShrinkCenter, 14, 100f);

            // Scroll container for character rows
            float scrollY = colY + ColRowHeight + 6f;
            var scroll = new ScrollContainer
            {
                AnchorRight = 1f,
                AnchorBottom = 1f,
                OffsetLeft = PaddingH,
                OffsetRight = -PaddingH,
                OffsetTop = scrollY,
                OffsetBottom = -40f,
            };
            AddChild(scroll);

            _rowContainer = new VBoxContainer();
            _rowContainer.AddThemeConstantOverride("separation", RowSpacing);
            _rowContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            scroll.AddChild(_rowContainer);
        }

        // ─── Row population (called each OnSubmenuOpened) ─────────────────────

        private void PopulateRows()
        {
            if (_rowContainer == null) return;
            foreach (Node child in _rowContainer.GetChildren())
                child.QueueFree();

            var characters = CharacterHelper.GetAllCharacters();
            var progress = SaveManager.Instance.Progress;

            foreach (var character in characters)
            {
                bool isCustom = !CharacterHelper.IsBaseCharacter(character.Id);
                _rowContainer.AddChild(BuildCharacterRow(character, isCustom, progress));
            }
        }

        // ─── Per-character row ────────────────────────────────────────────────

        private Control BuildCharacterRow(CharacterModel character, bool isCustom, ProgressState progress)
        {
            var panel = MakePanel(isCustom
                ? new Color(0.12f, 0.12f, 0.19f, 0.9f)
                : new Color(0.15f, 0.16f, 0.21f, 0.9f));
            panel.CustomMinimumSize = new Vector2(0f, RowHeight);
            panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            var hbox = AddHbox(panel, 12);

            // Name + source
            var nameCol = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            var nameLbl = new Label { Text = character.Title.GetFormattedText() };
            nameLbl.AddThemeFontSizeOverride("font_size", 21);
            nameLbl.AddThemeColorOverride("font_color", new Color(0.95f, 0.9f, 0.8f));
            nameCol.AddChild(nameLbl);

            string sourceText = isCustom ? GetSourceText(character) : "Base game";
            var sourceLbl = new Label { Text = sourceText };
            sourceLbl.AddThemeFontSizeOverride("font_size", 13);
            sourceLbl.AddThemeColorOverride("font_color", MutedColor);
            nameCol.AddChild(sourceLbl);
            hbox.AddChild(nameCol);

            // Visibility toggle (stats screen) — custom characters only. Base characters
            // always appear on the Compendium stats screen (the game renders them itself;
            // our StatsGridPatch only injects custom rows), so a toggle would be meaningless.
            if (isCustom)
            {
                bool vis = VisibilityStore.IsVisible(character.Id);
                var visBtn = MakeColorButton(vis ? "Shown" : "Hidden", vis);
                visBtn.Pressed += () =>
                {
                    bool now = VisibilityStore.Toggle(character.Id);
                    visBtn.Text = now ? "Shown" : "Hidden";
                    SetButtonColor(visBtn, now);
                };
                hbox.AddChild(visBtn);
            }
            else
            {
                hbox.AddChild(MakeAlwaysLabel());
            }

            // Enable/disable in character select (custom only)
            if (isCustom)
            {
                bool enabled = EnabledStore.IsEnabled(character.Id);
                var enableBtn = MakeColorButton(enabled ? "Shown" : "Hidden", enabled);
                enableBtn.Pressed += () =>
                {
                    bool now = EnabledStore.Toggle(character.Id);
                    enableBtn.Text = now ? "Shown" : "Hidden";
                    SetButtonColor(enableBtn, now);
                };
                hbox.AddChild(enableBtn);
            }
            else
            {
                var disabledLbl = new Label
                {
                    Text = "—",
                    CustomMinimumSize = new Vector2(100f, 0f),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    SizeFlagsVertical = SizeFlags.ShrinkCenter,
                };
                disabledLbl.AddThemeColorOverride("font_color", MutedColor);
                hbox.AddChild(disabledLbl);
            }

            // Quick stats
            var stats = progress.GetStatsForCharacter(character.Id);
            string statsText = stats != null
                ? $"W:{stats.TotalWins}\nL:{stats.TotalLosses}"
                : "—";
            var statsLbl = new Label
            {
                Text = statsText,
                CustomMinimumSize = new Vector2(100f, 0f),
                HorizontalAlignment = HorizontalAlignment.Center,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            statsLbl.AddThemeFontSizeOverride("font_size", 15);
            hbox.AddChild(statsLbl);

            // View History button
            bool hasHistory = stats != null && (stats.TotalWins > 0 || stats.TotalLosses > 0);
            var histBtn = new Button
            {
                Text = "History",
                CustomMinimumSize = new Vector2(100f, 0f),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                Disabled = !hasHistory,
            };
            histBtn.AddThemeFontSizeOverride("font_size", 16);
            histBtn.Pressed += () => OpenFilteredRunHistory(character.Id);
            hbox.AddChild(histBtn);

            return panel;
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        /// <summary>Creates an anchored PanelContainer with rounded bg. Caller positions it.</summary>
        private static PanelContainer MakePanel(Color bgColor)
        {
            var panel = new PanelContainer();
            var style = new StyleBoxFlat
            {
                BgColor = bgColor,
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6,
                ContentMarginLeft = 16f,
                ContentMarginRight = 16f,
                ContentMarginTop = 8f,
                ContentMarginBottom = 8f,
            };
            panel.AddThemeStyleboxOverride("panel", style);
            return panel;
        }

        /// <summary>Adds an HBoxContainer as an ExpandFill child of parent. Returns the HBox.</summary>
        private static HBoxContainer AddHbox(Container parent, int separation)
        {
            var hbox = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            hbox.AddThemeConstantOverride("separation", separation);
            parent.AddChild(hbox);
            return hbox;
        }

        private static void AddColLabel(HBoxContainer parent, string text, SizeFlags horizontal, int fontSize, float minWidth = 0f)
        {
            var lbl = new Label
            {
                Text = text,
                SizeFlagsHorizontal = horizontal,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                HorizontalAlignment = horizontal == SizeFlags.ExpandFill
                    ? HorizontalAlignment.Left
                    : HorizontalAlignment.Center,
            };
            if (minWidth > 0f)
                lbl.CustomMinimumSize = new Vector2(minWidth, 0f);
            lbl.AddThemeFontSizeOverride("font_size", fontSize);
            lbl.AddThemeColorOverride("font_color", MutedColor);
            parent.AddChild(lbl);
        }

        private static Button MakeColorButton(string text, bool active)
        {
            var btn = new Button
            {
                Text = text,
                CustomMinimumSize = new Vector2(100f, 0f),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            SetButtonColor(btn, active);
            return btn;
        }

        private static void SetButtonColor(Button btn, bool active)
        {
            btn.AddThemeColorOverride("font_color", active ? GreenColor : RedColor);
        }

        /// <summary>A muted "Always" label sized like the toggle buttons — used for base
        /// characters whose stats are always shown and can't be toggled.</summary>
        private static Label MakeAlwaysLabel()
        {
            var lbl = new Label
            {
                Text = "Always",
                CustomMinimumSize = new Vector2(100f, 0f),
                HorizontalAlignment = HorizontalAlignment.Center,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            lbl.AddThemeColorOverride("font_color", MutedColor);
            return lbl;
        }

        private static string GetSourceText(CharacterModel character)
        {
            var mod = CharacterHelper.GetSourceMod(character);
            return mod != null
                ? $"{mod.manifest.name} v{mod.manifest.version}"
                : "Unknown mod";
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
