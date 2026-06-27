using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace CharacterManager.UI
{
    /// <summary>
    /// A read-only features / how-it-works reference for the Character Manager (M16). Pushed from the
    /// "?" button in the manager header. Replaces the old per-button hover tooltips (which fired on
    /// every toggle and felt noisy) with one discoverable place that documents every feature; the
    /// column headers keep concise tooltips for quick hints.
    ///
    /// Same code-built NSubmenu pattern as the other modded screens: we do NOT call base._Ready() /
    /// base.ConnectSignals() (they look up a BackButton child we don't have), and visuals are built
    /// from containers + labels (no custom _Draw — see CLAUDE.md).
    /// </summary>
    public class CharacterHelpScreen : NSubmenu
    {
        private const float PaddingTop = UiTheme.PaddingTop;
        private const float HeaderHeight = UiTheme.HeaderHeight;

        private VBoxContainer? _contentContainer;
        private bool _built;

        protected override Control? InitialFocusedControl => null;

        public override void _Ready()
        {
            UiTheme.ApplyGameTheme(this);
            ConnectSignals();
            BuildLayout();
        }

        protected override void ConnectSignals() { /* see CharacterManagerScreen note */ }

        public override void OnSubmenuOpened()
        {
            // Static content — build once, then reuse on subsequent opens.
            if (!_built) { PopulateContent(); _built = true; }
        }

        public override void OnSubmenuClosed() => base.OnSubmenuClosed();

        // ─── One-time chrome ──────────────────────────────────────────────────

        private void BuildLayout()
        {
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;

            AddChild(UiTheme.MakeBackdrop());

            var title = UiTheme.MakeLabel("Character Manager — Help", UiTheme.Title, UiTheme.TitleFontSize);
            UiTheme.PlaceInColumn(title, PaddingTop, HeaderHeight);
            AddChild(title);

            var subtitle = UiTheme.MakeLabel("What each part of the screen does.", UiTheme.Muted, UiTheme.SmallFontSize);
            UiTheme.PlaceInColumn(subtitle, PaddingTop + HeaderHeight - 6f, 26f);
            AddChild(subtitle);

            var backBtn = UiTheme.MakeButton("← Back", null, 120f);
            UiTheme.PlaceColumnRight(backBtn, PaddingTop, HeaderHeight, 120f);
            backBtn.Pressed += () => _stack?.Pop();
            AddChild(backBtn);

            float scrollY = PaddingTop + HeaderHeight + 30f;
            var scroll = new ScrollContainer();
            UiTheme.PlaceColumnStretch(scroll, scrollY, UiTheme.PaddingTop);
            AddChild(scroll);

            _contentContainer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _contentContainer.AddThemeConstantOverride("separation", 14);
            scroll.AddChild(_contentContainer);
        }

        // ─── Content ──────────────────────────────────────────────────────────

        private void PopulateContent()
        {
            if (_contentContainer == null) return;

            AddSection("Overview", new[]
            {
                "The Character Manager lists every installed character — base game and modded — in one " +
                "place. Pick a row to see that character on the right, with its portrait, lifetime " +
                "win/loss, and buttons to drill into its run history, analytics, and info card.",
                "Everything here is read-only with respect to your saves: toggles change this mod's own " +
                "config, never the game's save files.",
            });

            AddSection("Win Rate column", new[]
            {
                "A quick read on how a character has performed, straight from the list. The percentage " +
                "is the win rate across all your run history for that character — every game mode " +
                "(Standard, Custom, Daily).",
                "Below it, each tick is one run — green for a win, red for a loss, grey for an " +
                "abandoned run — with the oldest on the left and your most recent runs on the right, " +
                "so you can see streaks and recent form at a glance. Hover the percentage for the " +
                "exact W/L and run count. Characters you haven't finished a decisive run with show a dash.",
            });
            AddBullets("Win-rate % colour", new[]
            {
                "Green — 50% or better.",
                "Gold — 30–49%.",
                "Red — below 30%.",
            });
            AddSection("Abandoned runs (click the header)", new[]
            {
                "The \"Win Rate\" column header is a button — click it to cycle how abandoned runs are " +
                "treated. The header colour shows the current mode:",
            });
            AddBullets("Modes", new[]
            {
                "Hidden (grey header, default) — abandoned runs are left out of both the strip and the %.",
                "Shown (gold header) — abandoned runs appear as grey ticks in the strip, but don't " +
                "affect the %.",
                "Counted (orange header) — abandoned runs appear as grey ticks AND count as losses in " +
                "the %. Handy because, outside of testing, an abandoned run is usually one you'd " +
                "already lost.",
            });

            AddSection("In Select", new[]
            {
                "Controls whether the character appears on the Character Select and Custom Run screens. " +
                "Turn it off (Hidden) to hide a character you don't want to see there — it stays " +
                "installed and keeps its stats; it just won't show up when starting a run.",
                "The roster can never be fully emptied: the last visible character can't be hidden, so " +
                "you always have something to play.",
            });

            AddSection("Stats", new[]
            {
                "Controls whether the character's win/loss stats appear in the in-game Compendium stats " +
                "grid. Base characters are Always shown; for modded characters you can hide a noisy or " +
                "test character from that grid without affecting anything else.",
            });

            AddSection("Lend Cards", new[]
            {
                "A Yes/No switch over whether other characters' cross-pool effects may pull cards and " +
                "relics from this character's pool. It governs Kaleidoscope, Colorful Philosophers, " +
                "Splash, Prismatic Gem, and Orobas/SeaGlass.",
                "Yes (default) keeps this character's pool available as a source for those effects. No " +
                "excludes it — useful when a modded character's cards are unbalanced or thematically " +
                "out of place in someone else's run. At least one pool always remains eligible, so the " +
                "effects never break.",
            });

            AddSection("The detail panel", new[]
            {
                "The W/L line under the portrait is clickable — click it to cycle the scope. It starts " +
                "on the game's official tally, which counts Standard mode only, then switches to all " +
                "runs across every mode (Custom and Daily included), then to all runs with abandoned " +
                "runs shown as a separate A count. The caption beneath always names the current scope.",
                "History opens the game's run-history viewer filtered to just this character. Analytics " +
                "opens a deep per-character breakdown — win-rate windows, card / relic / potion / " +
                "ancient pick and win rates, encounter and death analytics, act/floor distributions, " +
                "and a single-run autopsy — with filters for game mode, minimum ascension, and a " +
                "most-recent-N window, plus JSON/CSV export. Info shows the character's starting deck, " +
                "relics, stats, and deck composition.",
            });

            AddSection("Random-character pool", new[]
            {
                "Separately from this screen, the mod adds a configurable random-character pool you set " +
                "up from the run lobby: choose which characters the \"random\" pick can roll, synced " +
                "across multiplayer. It's independent of the In Select and Lend Cards toggles here.",
            });
        }

        // ─── Section builders ─────────────────────────────────────────────────

        private void AddSection(string heading, IEnumerable<string> paragraphs)
        {
            var panel = MakeSectionPanel(heading, out var body);
            foreach (var p in paragraphs)
            {
                var lbl = new Label
                {
                    Text = p,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                };
                lbl.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontSize);
                lbl.AddThemeColorOverride("font_color", UiTheme.Body);
                body.AddChild(lbl);
            }
            _contentContainer!.AddChild(panel);
        }

        private void AddBullets(string heading, IEnumerable<string> items)
        {
            var panel = MakeSectionPanel(heading, out var body);
            foreach (var item in items)
            {
                var lbl = new Label
                {
                    Text = "•  " + item,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                };
                lbl.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontSize);
                lbl.AddThemeColorOverride("font_color", UiTheme.Body);
                body.AddChild(lbl);
            }
            _contentContainer!.AddChild(panel);
        }

        private static PanelContainer MakeSectionPanel(string heading, out VBoxContainer body)
        {
            var panel = UiTheme.MakePanel(UiTheme.PanelBg);

            var outer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            outer.AddThemeConstantOverride("separation", 6);
            panel.AddChild(outer);

            outer.AddChild(UiTheme.MakeLabel(heading, UiTheme.Heading, UiTheme.SectionFontSize));

            body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            body.AddThemeConstantOverride("separation", 5);
            outer.AddChild(body);

            return panel;
        }
    }
}
