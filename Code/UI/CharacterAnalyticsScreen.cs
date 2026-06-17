using System;
using System.Collections.Generic;
using CharacterManager.Analytics;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;

namespace CharacterManager.UI
{
    /// <summary>
    /// Read-only per-character analytics (M4). Pushed from a manager row. Two data sources,
    /// both read-only — nothing here writes to the save:
    ///   1. <see cref="CharacterStats"/> (from <c>SaveManager.Instance.Progress</c>) — the
    ///      game's own lifetime tallies (wins, losses, ascension, streaks, playtime, badges).
    ///   2. The <c>.run</c> history files (via <c>SaveManager.LoadRunHistory</c>) — parsed and
    ///      aggregated for this character (per-ascension W/L, act/floor reached, run lengths).
    ///
    /// Loading every run file is O(n) disk reads, acceptable for a user-triggered screen and
    /// the same approach the run-history filter (M3) already uses.
    /// </summary>
    public class CharacterAnalyticsScreen : NSubmenu
    {
        // ─── Layout constants (M6: compact, game palette via UiTheme) ─────────
        private const float PaddingH = UiTheme.PaddingH;
        private const float PaddingTop = UiTheme.PaddingTop;
        private const float HeaderHeight = UiTheme.HeaderHeight;

        private static readonly Color BgColor = UiTheme.Backdrop;
        private static readonly Color HeaderColor = UiTheme.Title;
        private static readonly Color MutedColor = UiTheme.Muted;
        private static readonly Color BodyColor = UiTheme.Body;
        private static readonly Color SectionColor = UiTheme.Heading;

        // ─── State ───────────────────────────────────────────────────────────
        private CharacterModel? _character;
        private Label? _titleLabel;
        private Label? _subtitleLabel;
        private Label? _statusLabel;
        private VBoxContainer? _contentContainer;

        protected override Control? InitialFocusedControl => null;

        /// <summary>Sets the character to analyze. Call before pushing this screen.</summary>
        public void SetCharacter(CharacterModel character) => _character = character;

        // ─── Godot entry point ────────────────────────────────────────────────
        public override void _Ready()
        {
            UiTheme.ApplyGameTheme(this);
            ConnectSignals();
            BuildLayout();
        }

        protected override void ConnectSignals()
        {
            // Intentionally empty — see CharacterManagerScreen for why we skip the base.
        }

        public override void OnSubmenuOpened()
        {
            PopulateContent();
        }

        public override void OnSubmenuClosed()
        {
            base.OnSubmenuClosed();
        }

        // ─── One-time chrome ──────────────────────────────────────────────────

        private void BuildLayout()
        {
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;

            var bg = new ColorRect
            {
                Color = BgColor,
                AnchorRight = 1f,
                AnchorBottom = 1f,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(bg);

            _titleLabel = UiTheme.MakeLabel("Analytics", HeaderColor, UiTheme.TitleFontSize);
            UiTheme.PlaceInColumn(_titleLabel, PaddingTop, HeaderHeight);
            AddChild(_titleLabel);

            _subtitleLabel = UiTheme.MakeLabel("Analytics", MutedColor, UiTheme.SmallFontSize);
            UiTheme.PlaceInColumn(_subtitleLabel, PaddingTop + HeaderHeight - 6f, 26f);
            AddChild(_subtitleLabel);

            var backBtn = UiTheme.MakeButton("← Back", null, 120f);
            UiTheme.PlaceColumnRight(backBtn, PaddingTop, HeaderHeight, 120f);
            backBtn.Pressed += () => _stack?.Pop();
            AddChild(backBtn);

            // Export button (M5) — sits to the left of Back, both anchored to the column's right edge.
            var exportBtn = UiTheme.MakeButton("Export", null, 110f);
            exportBtn.TooltipText = "Write this character's stats to JSON + CSV";
            UiTheme.PlaceColumnRight(exportBtn, PaddingTop, HeaderHeight, 110f);
            exportBtn.OffsetRight -= 128f; // shift left of the 120-wide Back button + gap
            exportBtn.OffsetLeft -= 128f;
            exportBtn.Pressed += OnExport;
            AddChild(exportBtn);

            // Status line under the header (shows the export destination after a click).
            _statusLabel = UiTheme.MakeLabel("", SectionColor, UiTheme.SmallFontSize);
            _statusLabel.ClipText = true;
            UiTheme.PlaceInColumn(_statusLabel, PaddingTop + HeaderHeight + 12f, 20f);
            AddChild(_statusLabel);

            float scrollY = PaddingTop + HeaderHeight + 34f;
            var scroll = new ScrollContainer();
            UiTheme.PlaceColumnStretch(scroll, scrollY, UiTheme.PaddingTop);
            AddChild(scroll);

            _contentContainer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _contentContainer.AddThemeConstantOverride("separation", 10);
            scroll.AddChild(_contentContainer);
        }

        // ─── Content (rebuilt each open) ──────────────────────────────────────

        private void PopulateContent()
        {
            if (_contentContainer == null) return;
            foreach (Node child in _contentContainer.GetChildren())
                child.QueueFree();

            var c = _character;
            if (c == null)
            {
                if (_titleLabel != null) _titleLabel.Text = "Analytics";
                return;
            }

            if (_titleLabel != null) _titleLabel.Text = c.Title.GetFormattedText();
            if (_subtitleLabel != null) _subtitleLabel.Text = "Analytics";
            if (_statusLabel != null) _statusLabel.Text = ""; // clear any prior export message

            // Official stats (Standard runs only — the game excludes Custom/Daily from these).
            CharacterStats? stats = null;
            try { stats = SaveManager.Instance.Progress?.GetStatsForCharacter(c.Id); }
            catch (Exception e) { Log.Warn("[CharacterManager] GetStatsForCharacter failed: " + e.Message); }

            // Full run-history aggregate (all game modes), with per-mode splits.
            var agg = CharacterAnalytics.Compute(c.Id);

            if (stats == null && agg.Total == 0)
            {
                AddTextSection("Summary",
                    "No recorded runs for this character yet. (Run history may be disabled or pruned.)");
                return;
            }

            // 1) Official summary — Standard runs, matches the manager list and the game's own stats.
            AddSummarySection(stats);

            // 2) Custom / Daily runs — recorded in run history but NOT counted by official stats.
            if (agg.CustomTotal > 0)
                AddCustomDailySection(agg);

            if (agg.Total > 0)
            {
                // 3) Mode-agnostic run details (all runs).
                AddRunDetailsSection(agg);
                // 4) Breakdowns across all runs.
                AddAscensionBars(agg);
                AddActBars(agg);
            }
        }

        // ─── Bar sections (M6 cont.: fill the page with the data we already have) ──

        private const float BarLabelWidth = 170f;
        private const float BarValueWidth = 90f;

        /// <summary>Official, Standard-only summary from CharacterStats (matches the manager list).</summary>
        private void AddSummarySection(CharacterStats? stats)
        {
            var panel = MakeSectionPanel("Summary  (Standard runs)", out var body);

            int w = stats?.TotalWins ?? 0;
            int l = stats?.TotalLosses ?? 0;

            var segs = new (Color, float)[] { (UiTheme.Good, w), (UiTheme.Bad, l) };
            var bar = UiTheme.MakeBarTrack(18f, segs, 0f);
            body.AddChild(UiTheme.MakeBarRow("Win rate", BarLabelWidth, bar, WinRate(w, l), BarValueWidth));

            // Spacer between the bar and the grid.
            body.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 4f), MouseFilter = MouseFilterEnum.Ignore });

            var rows = new List<(string, string)>
            {
                ("Wins", w.ToString()),
                ("Losses", l.ToString()),
                ("Max ascension", (stats?.MaxAscension ?? 0).ToString()),
                ("Preferred ascension", (stats?.PreferredAscension ?? 0).ToString()),
                ("Best win streak", (stats?.BestWinStreak ?? 0).ToString()),
                ("Current win streak", (stats?.CurrentWinStreak ?? 0).ToString()),
                ("Fastest win", stats != null && stats.FastestWinTime >= 0 ? FormatDuration(stats.FastestWinTime) : "—"),
                ("Total playtime", stats != null ? FormatDuration(stats.Playtime) : "—"),
                ("Badges earned", stats?.Badges != null ? stats.Badges.Count.ToString() : "0"),
            };
            AddStatsGrid(body, rows, 2);

            _contentContainer!.AddChild(panel);
        }

        /// <summary>Custom + Daily runs — recorded in run history but excluded from official stats.</summary>
        private void AddCustomDailySection(CharacterAnalytics agg)
        {
            var panel = MakeSectionPanel("Custom / Daily Runs", out var body);

            var segs = new (Color, float)[]
            {
                (UiTheme.Good, agg.CustomWins),
                (UiTheme.Bad, agg.CustomDeaths),
                (MutedColor, agg.CustomAbandoned),
            };
            var bar = UiTheme.MakeBarTrack(18f, segs, 0f);
            body.AddChild(UiTheme.MakeBarRow("Win rate", BarLabelWidth, bar, WinRate(agg.CustomWins, agg.CustomTotal - agg.CustomWins), BarValueWidth));

            body.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 4f), MouseFilter = MouseFilterEnum.Ignore });

            var rows = new List<(string, string)>
            {
                ("Runs", agg.CustomTotal.ToString()),
                ("Wins", agg.CustomWins.ToString()),
                ("Deaths", agg.CustomDeaths.ToString()),
                ("Abandoned", agg.CustomAbandoned.ToString()),
            };
            AddStatsGrid(body, rows, 2);

            body.AddChild(UiTheme.MakeLabel(
                "These runs are not counted by the game's official stats.",
                MutedColor, UiTheme.SmallFontSize));

            _contentContainer!.AddChild(panel);
        }

        /// <summary>Mode-agnostic run details across all recorded runs (not win/loss tallies).</summary>
        private void AddRunDetailsSection(CharacterAnalytics agg)
        {
            var panel = MakeSectionPanel($"Run Details  (all {agg.Total} run{(agg.Total == 1 ? "" : "s")})", out var body);

            int maxAsc = 0;
            foreach (var k in agg.PerAscension.Keys) maxAsc = Math.Max(maxAsc, k);

            var rows = new List<(string, string)>
            {
                ("Runs recorded", agg.Total.ToString()),
                ("Max ascension", maxAsc.ToString()),
                ("Highest act reached", agg.MaxAct.ToString()),
                ("Highest floor reached", agg.MaxFloor.ToString()),
                ("Average run length", FormatDuration(agg.AvgRunTime)),
                ("Longest run", FormatDuration(agg.MaxRunTime)),
                ("Fastest clear", agg.FastestWin >= 0 ? FormatDuration(agg.FastestWin) : "—"),
            };
            AddStatsGrid(body, rows, 2);

            _contentContainer!.AddChild(panel);
        }

        /// <summary>Lays label/value pairs out in <paramref name="columns"/> equal columns to fill width.</summary>
        private void AddStatsGrid(VBoxContainer body, List<(string label, string value)> rows, int columns)
        {
            var grid = new GridContainer { Columns = columns, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", 28);
            grid.AddThemeConstantOverride("v_separation", 4);
            foreach (var (label, value) in rows)
            {
                var cell = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                cell.AddThemeConstantOverride("separation", 10);

                var l = new Label { Text = label, CustomMinimumSize = new Vector2(170f, 0f) };
                l.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontSize);
                l.AddThemeColorOverride("font_color", MutedColor);
                cell.AddChild(l);

                var v = new Label { Text = value, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                v.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontSize);
                v.AddThemeColorOverride("font_color", BodyColor);
                cell.AddChild(v);

                grid.AddChild(cell);
            }
            body.AddChild(grid);
        }

        private void AddAscensionBars(CharacterAnalytics agg)
        {
            var panel = MakeSectionPanel("By Ascension  (all runs)", out var body);
            var keys = new List<int>(agg.PerAscension.Keys);
            keys.Sort();
            int maxGames = 1;
            foreach (var asc in keys) { var (w, l) = agg.PerAscension[asc]; maxGames = Math.Max(maxGames, w + l); }
            foreach (var asc in keys)
            {
                var (w, l) = agg.PerAscension[asc];
                var segs = new (Color, float)[] { (UiTheme.Good, w), (UiTheme.Bad, l) };
                var bar = UiTheme.MakeBarTrack(16f, segs, Math.Max(0, maxGames - (w + l)));
                body.AddChild(UiTheme.MakeBarRow($"Ascension {asc}", BarLabelWidth, bar, $"{w}W / {l}L", BarValueWidth));
            }
            _contentContainer!.AddChild(panel);
        }

        private void AddActBars(CharacterAnalytics agg)
        {
            var panel = MakeSectionPanel("Act Reached Distribution  (all runs)", out var body);
            var keys = new List<int>(agg.ActReached.Keys);
            keys.Sort();
            int maxCount = 1;
            foreach (var act in keys) maxCount = Math.Max(maxCount, agg.ActReached[act]);
            foreach (var act in keys)
            {
                int count = agg.ActReached[act];
                var segs = new (Color, float)[] { (UiTheme.Heading, count) };
                var bar = UiTheme.MakeBarTrack(16f, segs, Math.Max(0, maxCount - count));
                body.AddChild(UiTheme.MakeBarRow($"Reached act {act}", BarLabelWidth, bar, $"{count} run{(count == 1 ? "" : "s")}", BarValueWidth));
            }
            _contentContainer!.AddChild(panel);
        }

        // ─── Export (M5) ─────────────────────────────────────────────────────

        private void OnExport()
        {
            if (_character == null || _statusLabel == null) return;

            var result = StatsExporter.Export(_character);
            if (result.Ok)
            {
                _statusLabel.AddThemeColorOverride("font_color", SectionColor);
                _statusLabel.Text = "Exported JSON + CSV to:  " + result.Directory;
            }
            else
            {
                _statusLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.4f, 0.35f));
                _statusLabel.Text = "Export failed: " + result.Error;
            }
        }

        // ─── Formatting ──────────────────────────────────────────────────────

        private static string WinRate(int wins, int losses)
        {
            int total = wins + losses;
            if (total <= 0) return "—";
            return $"{(100.0 * wins / total):0.#}%";
        }

        /// <summary>Formats a duration given in seconds as "Xh Ym", "Ym Zs", or "Zs".</summary>
        private static string FormatDuration(double seconds)
        {
            if (seconds <= 0) return "0s";
            long total = (long)Math.Round(seconds);
            long h = total / 3600;
            long m = (total % 3600) / 60;
            long s = total % 60;
            if (h > 0) return $"{h}h {m}m";
            if (m > 0) return $"{m}m {s}s";
            return $"{s}s";
        }

        // ─── Section builders (mirror CharacterInfoScreen) ───────────────────

        private void AddStatsSection(string heading, List<(string label, string value)> rows)
        {
            var panel = MakeSectionPanel(heading, out var body);
            foreach (var (label, value) in rows)
            {
                var line = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                line.AddThemeConstantOverride("separation", 12);

                var l = new Label { Text = label, CustomMinimumSize = new Vector2(220f, 0f) };
                l.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontSize);
                l.AddThemeColorOverride("font_color", MutedColor);
                line.AddChild(l);

                var v = new Label { Text = value, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                v.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontSize);
                v.AddThemeColorOverride("font_color", BodyColor);
                line.AddChild(v);

                body.AddChild(line);
            }
            _contentContainer!.AddChild(panel);
        }

        private void AddListSection(string heading, List<string> items)
        {
            var panel = MakeSectionPanel(heading, out var body);
            if (items.Count == 0)
            {
                var none = new Label { Text = "None" };
                none.AddThemeFontSizeOverride("font_size", 15);
                none.AddThemeColorOverride("font_color", MutedColor);
                body.AddChild(none);
            }
            else
            {
                foreach (var item in items)
                {
                    var lbl = new Label { Text = "•  " + item, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                    lbl.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontSize);
                    lbl.AddThemeColorOverride("font_color", BodyColor);
                    body.AddChild(lbl);
                }
            }
            _contentContainer!.AddChild(panel);
        }

        private void AddTextSection(string heading, string text)
        {
            var panel = MakeSectionPanel(heading, out var body);
            var lbl = new Label
            {
                Text = text,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            lbl.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontSize);
            lbl.AddThemeColorOverride("font_color", BodyColor);
            body.AddChild(lbl);
            _contentContainer!.AddChild(panel);
        }

        private static PanelContainer MakeSectionPanel(string heading, out VBoxContainer body)
        {
            var panel = UiTheme.MakePanel(UiTheme.PanelBg);

            var outer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            outer.AddThemeConstantOverride("separation", 6);
            panel.AddChild(outer);

            var headingLbl = UiTheme.MakeLabel(heading, SectionColor, UiTheme.SectionFontSize);
            outer.AddChild(headingLbl);

            body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            body.AddThemeConstantOverride("separation", 3);
            outer.AddChild(body);

            return panel;
        }
    }
}
