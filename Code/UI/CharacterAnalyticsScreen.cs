using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
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
        // ─── Layout constants ────────────────────────────────────────────────
        private const float PaddingH = 80f;
        private const float PaddingTop = 40f;
        private const float HeaderHeight = 72f;

        private static readonly Color BgColor = new Color(0.08f, 0.08f, 0.12f, 0.98f);
        private static readonly Color HeaderColor = new Color(0.85f, 0.72f, 0.4f);
        private static readonly Color MutedColor = new Color(0.55f, 0.55f, 0.6f);
        private static readonly Color BodyColor = new Color(0.9f, 0.88f, 0.82f);
        private static readonly Color SectionColor = new Color(0.7f, 0.78f, 0.9f);

        // ─── State ───────────────────────────────────────────────────────────
        private CharacterModel? _character;
        private Label? _titleLabel;
        private Label? _subtitleLabel;
        private VBoxContainer? _contentContainer;

        protected override Control? InitialFocusedControl => null;

        /// <summary>Sets the character to analyze. Call before pushing this screen.</summary>
        public void SetCharacter(CharacterModel character) => _character = character;

        // ─── Godot entry point ────────────────────────────────────────────────
        public override void _Ready()
        {
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
            _titleLabel.AddThemeFontSizeOverride("font_size", 38);
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
            _subtitleLabel.AddThemeFontSizeOverride("font_size", 14);
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
            backBtn.AddThemeFontSizeOverride("font_size", 20);
            backBtn.Pressed += () => _stack?.Pop();
            AddChild(backBtn);

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
            var agg = AggregateRuns(c.Id);
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

        // ─── Aggregation ─────────────────────────────────────────────────────

        private sealed class RunAggregate
        {
            public int Total, Wins, Deaths, Abandoned, MaxAct, MaxFloor;
            public float MaxRunTime;
            public double SumRunTime;
            public float FastestWin = -1f;
            public double AvgRunTime => Total > 0 ? SumRunTime / Total : 0;
            public readonly Dictionary<int, (int w, int l)> PerAscension = new();
            public readonly Dictionary<int, int> ActReached = new();
        }

        /// <summary>Loads every run-history file once and aggregates the runs for this character.</summary>
        private static RunAggregate AggregateRuns(ModelId characterId)
        {
            var agg = new RunAggregate();
            List<string>? names = null;
            try { names = SaveManager.Instance.GetAllRunHistoryNames(); }
            catch (Exception e) { Log.Warn("[CharacterManager] GetAllRunHistoryNames failed: " + e.Message); }
            if (names == null) return agg;

            foreach (var name in names)
            {
                try
                {
                    var result = SaveManager.Instance.LoadRunHistory(name);
                    if (!result.Success) continue;
                    RunHistory? h = result.SaveData;
                    if (h == null) continue;

                    bool matches = false;
                    foreach (var p in h.Players)
                        if (p.Character == characterId) { matches = true; break; }
                    if (!matches) continue;

                    agg.Total++;
                    if (h.Win) agg.Wins++;
                    else if (h.WasAbandoned) agg.Abandoned++;
                    else agg.Deaths++;

                    // Run length
                    agg.SumRunTime += h.RunTime;
                    if (h.RunTime > agg.MaxRunTime) agg.MaxRunTime = h.RunTime;
                    if (h.Win && (agg.FastestWin < 0 || h.RunTime < agg.FastestWin))
                        agg.FastestWin = h.RunTime;

                    // Acts reached (number of acts entered) and floors (sum of map points)
                    int actsReached = h.Acts?.Count ?? 0;
                    if (actsReached > agg.MaxAct) agg.MaxAct = actsReached;
                    if (!agg.ActReached.ContainsKey(actsReached)) agg.ActReached[actsReached] = 0;
                    agg.ActReached[actsReached]++;

                    int floors = 0;
                    if (h.MapPointHistory != null)
                        foreach (var rooms in h.MapPointHistory)
                            floors += rooms?.Count ?? 0;
                    if (floors > agg.MaxFloor) agg.MaxFloor = floors;

                    // Per-ascension W/L
                    int asc = h.Ascension;
                    var cur = agg.PerAscension.TryGetValue(asc, out var v) ? v : (0, 0);
                    agg.PerAscension[asc] = h.Win ? (cur.Item1 + 1, cur.Item2) : (cur.Item1, cur.Item2 + 1);
                }
                catch (Exception e)
                {
                    Log.Warn($"[CharacterManager] Could not aggregate run '{name}': {e.Message}");
                }
            }
            return agg;
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
                l.AddThemeFontSizeOverride("font_size", 16);
                l.AddThemeColorOverride("font_color", MutedColor);
                line.AddChild(l);

                var v = new Label { Text = value, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                v.AddThemeFontSizeOverride("font_size", 16);
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
                    lbl.AddThemeFontSizeOverride("font_size", 16);
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
            lbl.AddThemeFontSizeOverride("font_size", 16);
            lbl.AddThemeColorOverride("font_color", BodyColor);
            body.AddChild(lbl);
            _contentContainer!.AddChild(panel);
        }

        private static PanelContainer MakeSectionPanel(string heading, out VBoxContainer body)
        {
            var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.13f, 0.13f, 0.18f, 0.9f),
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6,
                ContentMarginLeft = 18f,
                ContentMarginRight = 18f,
                ContentMarginTop = 12f,
                ContentMarginBottom = 14f,
            };
            panel.AddThemeStyleboxOverride("panel", style);

            var outer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            outer.AddThemeConstantOverride("separation", 8);
            panel.AddChild(outer);

            var headingLbl = new Label { Text = heading };
            headingLbl.AddThemeFontSizeOverride("font_size", 19);
            headingLbl.AddThemeColorOverride("font_color", SectionColor);
            outer.AddChild(headingLbl);

            body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            body.AddThemeConstantOverride("separation", 4);
            outer.AddChild(body);

            return panel;
        }
    }
}
