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

            _titleLabel = new Label
            {
                Text = "Analytics",
                AnchorRight = 1f,
                OffsetLeft = PaddingH,
                OffsetRight = -PaddingH - 200f,
                OffsetTop = PaddingTop,
                OffsetBottom = PaddingTop + HeaderHeight,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _titleLabel.AddThemeFontSizeOverride("font_size", UiTheme.TitleFontSize);
            _titleLabel.AddThemeColorOverride("font_color", HeaderColor);
            AddChild(_titleLabel);

            _subtitleLabel = new Label
            {
                Text = "Analytics",
                AnchorRight = 1f,
                OffsetLeft = PaddingH,
                OffsetRight = -PaddingH - 200f,
                OffsetTop = PaddingTop + HeaderHeight - 6f,
                OffsetBottom = PaddingTop + HeaderHeight + 22f,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _subtitleLabel.AddThemeFontSizeOverride("font_size", UiTheme.SmallFontSize);
            _subtitleLabel.AddThemeColorOverride("font_color", MutedColor);
            AddChild(_subtitleLabel);

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
            backBtn.AddThemeFontSizeOverride("font_size", UiTheme.ButtonFontSize);
            backBtn.Pressed += () => _stack?.Pop();
            AddChild(backBtn);

            // Export button (M5) — sits to the left of Back.
            var exportBtn = new Button
            {
                Text = "Export",
                AnchorLeft = 1f,
                AnchorRight = 1f,
                OffsetLeft = -360f,
                OffsetRight = -210f,
                OffsetTop = PaddingTop,
                OffsetBottom = PaddingTop + HeaderHeight,
                TooltipText = "Write this character's stats to JSON + CSV",
            };
            exportBtn.AddThemeFontSizeOverride("font_size", UiTheme.ButtonFontSize);
            exportBtn.Pressed += OnExport;
            AddChild(exportBtn);

            // Status line under the header (shows the export destination after a click).
            _statusLabel = new Label
            {
                Text = "",
                AnchorRight = 1f,
                OffsetLeft = PaddingH,
                OffsetRight = -PaddingH,
                OffsetTop = PaddingTop + HeaderHeight + 14f,
                OffsetBottom = PaddingTop + HeaderHeight + 34f,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.Off,
                ClipText = true,
            };
            _statusLabel.AddThemeFontSizeOverride("font_size", UiTheme.SmallFontSize);
            _statusLabel.AddThemeColorOverride("font_color", SectionColor);
            AddChild(_statusLabel);

            float scrollY = PaddingTop + HeaderHeight + 36f;
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

            _contentContainer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _contentContainer.AddThemeConstantOverride("separation", 14);
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

            // ── Section 1: lifetime summary from CharacterStats ──────────────
            CharacterStats? stats = null;
            try { stats = SaveManager.Instance.Progress?.GetStatsForCharacter(c.Id); }
            catch (Exception e) { Log.Warn("[CharacterManager] GetStatsForCharacter failed: " + e.Message); }

            if (stats == null)
            {
                AddTextSection("Summary", "No recorded stats for this character yet.");
            }
            else
            {
                int w = stats.TotalWins, l = stats.TotalLosses;
                var summary = new List<(string, string)>
                {
                    ("Wins", w.ToString()),
                    ("Losses", l.ToString()),
                    ("Win rate", WinRate(w, l)),
                    ("Max ascension", stats.MaxAscension.ToString()),
                    ("Preferred ascension", stats.PreferredAscension.ToString()),
                    ("Best win streak", stats.BestWinStreak.ToString()),
                    ("Current win streak", stats.CurrentWinStreak.ToString()),
                    ("Fastest win", stats.FastestWinTime >= 0 ? FormatDuration(stats.FastestWinTime) : "—"),
                    ("Total playtime", FormatDuration(stats.Playtime)),
                    ("Badges earned", stats.Badges != null ? stats.Badges.Count.ToString() : "0"),
                };
                AddStatsSection("Summary", summary);
            }

            // ── Sections 2–4: aggregates parsed from run-history files ───────
            var agg = CharacterAnalytics.Compute(c.Id);
            if (agg.Total == 0)
            {
                AddTextSection("Run History",
                    "No run-history files found for this character. (Stats above still reflect " +
                    "lifetime totals; run history may be disabled or pruned.)");
                return;
            }

            var runRows = new List<(string, string)>
            {
                ("Runs recorded", agg.Total.ToString()),
                ("Wins", agg.Wins.ToString()),
                ("Deaths", agg.Deaths.ToString()),
                ("Abandoned", agg.Abandoned.ToString()),
                ("Highest act reached", agg.MaxAct.ToString()),
                ("Highest floor reached", agg.MaxFloor.ToString()),
                ("Average run length", FormatDuration(agg.AvgRunTime)),
                ("Longest run", FormatDuration(agg.MaxRunTime)),
                ("Fastest clear", agg.FastestWin >= 0 ? FormatDuration(agg.FastestWin) : "—"),
            };
            AddStatsSection($"Run History  ({agg.Total} run{(agg.Total == 1 ? "" : "s")})", runRows);

            // By ascension
            var ascLines = new List<string>();
            var ascKeys = new List<int>(agg.PerAscension.Keys);
            ascKeys.Sort();
            foreach (var asc in ascKeys)
            {
                var (aw, al) = agg.PerAscension[asc];
                ascLines.Add($"Ascension {asc}:  {aw}W / {al}L");
            }
            AddListSection("By Ascension", ascLines);

            // Act reached distribution
            var actLines = new List<string>();
            var actKeys = new List<int>(agg.ActReached.Keys);
            actKeys.Sort();
            foreach (var act in actKeys)
            {
                int count = agg.ActReached[act];
                actLines.Add($"Reached act {act}:  {count} run{(count == 1 ? "" : "s")}");
            }
            AddListSection("Act Reached Distribution", actLines);
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
