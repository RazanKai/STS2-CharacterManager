using System;
using System.Collections.Generic;
using CharacterManager.Analytics;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rooms;
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

        private CharacterAnalytics? _fullAgg;         // loaded once per open (via AnalyticsCache)
        private GameModeFilter _currentFilter = GameModeFilter.All;
        private readonly List<(GameModeFilter Mode, Button Btn)> _filterBtns = new();

        // Composite filter axes (M8): minimum ascension + most-recent-N window, cycled by two buttons.
        private static readonly int[] AscSteps = { 0, 1, 5, 10, 15, 20 };
        private static readonly int[] RecentSteps = { 0, 10, 50, 100 };
        private int _minAscension;   // 0 = any
        private int _recentCount;    // 0 = all
        private bool _cardUpgradeAware;  // M9: collapse Strike/Strike+ (false) or keep separate (true)
        private Button? _ascBtn;
        private Button? _recentBtn;
        private Button? _upgradeBtn;

        // M9 ranked-list sizing.
        private const int CardListLimit = 10;     // rows shown per card list
        private const int CardMinSample = 3;      // min offers/runs before a rate is trusted (caveat 6)
        private const int CombatMinSample = 3;    // min fights before an encounter rate is trusted (caveat 6)

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

            // Game-mode filter bar
            float filterY = PaddingTop + HeaderHeight + 34f;
            BuildFilterBar(filterY);

            float scrollY = filterY + 34f;
            var scroll = new ScrollContainer();
            UiTheme.PlaceColumnStretch(scroll, scrollY, UiTheme.PaddingTop);
            AddChild(scroll);

            _contentContainer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _contentContainer.AddThemeConstantOverride("separation", 10);
            scroll.AddChild(_contentContainer);
        }

        private void BuildFilterBar(float top)
        {
            _filterBtns.Clear();
            var bar = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            UiTheme.PlaceInColumn(bar, top, 28f);
            AddChild(bar);

            foreach (var mode in new[] { GameModeFilter.All, GameModeFilter.Standard, GameModeFilter.Custom, GameModeFilter.Daily })
            {
                var label = mode switch
                {
                    GameModeFilter.All => "All",
                    GameModeFilter.Standard => "Standard",
                    GameModeFilter.Custom => "Custom",
                    GameModeFilter.Daily => "Daily",
                    _ => "All",
                };

                var btn = UiTheme.MakeButton(label, null, 80f);
                RefreshFilterButton(btn, mode);
                btn.Pressed += () => SetFilter(mode);
                bar.AddChild(btn);
                _filterBtns.Add((mode, btn));
            }

            // Spacer, then the two composite-filter cycle buttons (ascension floor + recent window).
            bar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });

            _ascBtn = UiTheme.MakeButton(AscLabel(), UiTheme.Body, 96f);
            _ascBtn.TooltipText = "Minimum ascension to include";
            _ascBtn.Pressed += CycleAscension;
            bar.AddChild(_ascBtn);

            _recentBtn = UiTheme.MakeButton(RecentLabel(), UiTheme.Body, 110f);
            _recentBtn.TooltipText = "Limit to the most recent runs";
            _recentBtn.Pressed += CycleRecent;
            bar.AddChild(_recentBtn);

            _upgradeBtn = UiTheme.MakeButton(UpgradeLabel(), UiTheme.Body, 120f);
            _upgradeBtn.TooltipText = "Treat upgraded cards (Strike+) as separate from their base card";
            _upgradeBtn.Pressed += ToggleUpgradeAware;
            bar.AddChild(_upgradeBtn);
        }

        private string AscLabel() => _minAscension <= 0 ? "Asc: Any" : $"Asc: {_minAscension}+";
        private string RecentLabel() => _recentCount <= 0 ? "Recent: All" : $"Recent: {_recentCount}";
        private string UpgradeLabel() => _cardUpgradeAware ? "Upgrades: On" : "Upgrades: Off";

        private void ToggleUpgradeAware()
        {
            _cardUpgradeAware = !_cardUpgradeAware;
            if (_upgradeBtn != null) _upgradeBtn.Text = UpgradeLabel();
            UpdateDisplay();
        }

        private void CycleAscension()
        {
            int idx = Array.IndexOf(AscSteps, _minAscension);
            idx = idx < 0 ? 0 : (idx + 1) % AscSteps.Length;
            _minAscension = AscSteps[idx];
            if (_ascBtn != null) _ascBtn.Text = AscLabel();
            UpdateDisplay();
        }

        private void CycleRecent()
        {
            int idx = Array.IndexOf(RecentSteps, _recentCount);
            idx = idx < 0 ? 0 : (idx + 1) % RecentSteps.Length;
            _recentCount = RecentSteps[idx];
            if (_recentBtn != null) _recentBtn.Text = RecentLabel();
            UpdateDisplay();
        }

        private void RefreshFilterButton(Button btn, GameModeFilter mode)
        {
            bool isSelected = mode == _currentFilter;
            btn.AddThemeColorOverride("font_color", isSelected ? BodyColor : MutedColor);
            btn.Modulate = isSelected ? new Color(1f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, 0.5f);
        }

        private void SetFilter(GameModeFilter filter)
        {
            _currentFilter = filter;
            foreach (var (mode, btn) in _filterBtns)
                RefreshFilterButton(btn, mode);
            UpdateDisplay();
        }

        /// <summary>
        /// Rebuilds the content area according to the current filter, using the already-loaded
        /// full aggregate (<see cref="_fullAgg"/>).
        /// </summary>
        private void UpdateDisplay()
        {
            if (_contentContainer == null) return;
            foreach (Node child in _contentContainer.GetChildren())
                child.QueueFree();

            var c = _character;
            if (c == null) return;

            var stats = GetStats(c);
            var full = _fullAgg;

            if (full != null && full.LoadFailed)
            {
                AddTextSection("Run History",
                    "Couldn't read run history yet (the save system may still be loading). Re-open this screen to retry.");
                if (stats != null) AddSummarySection(stats);
                return;
            }

            if (stats == null && (full == null || full.Total == 0))
            {
                AddTextSection("Summary",
                    "No recorded runs for this character yet. (Run history may be disabled or pruned.)");
                return;
            }

            // Official, Standard-only lifetime summary (CharacterStats) — independent of the filters.
            if (stats != null)
                AddSummarySection(stats);

            // Win-rate moving windows: scoped by game-mode + ascension, but NOT by the recent-N cap
            // (the windows are themselves "last N"). Computed off its own filtered aggregate.
            var windowAgg = full?.GetFiltered(new RunFilter(_currentFilter, _minAscension, 0));
            if (windowAgg != null && windowAgg.Total > 0)
                AddWinRateWindows(windowAgg);

            // Main filtered view (mode + ascension + recent-N).
            var agg = full?.GetFiltered(new RunFilter(_currentFilter, _minAscension, _recentCount));

            if (agg != null && agg.Total > 0)
            {
                string suffix = FilterSuffix();

                if (_currentFilter == GameModeFilter.All && agg.CustomTotal > 0)
                    AddCustomDailySection(agg);

                AddRunDetailsSection(agg, suffix);
                AddCardSections(agg);
                AddCombatSections(agg);
                AddAscensionBars(agg, suffix);
                AddActBars(agg, suffix);
                AddFloorBars(agg, suffix);
            }
            else
            {
                AddTextSection("Run History", $"No runs match the current filter ({FilterSuffix()}).");
            }
        }

        /// <summary>Human-readable description of the active composite filter.</summary>
        private string FilterSuffix()
        {
            var parts = new List<string>
            {
                _currentFilter == GameModeFilter.All ? "all runs" : $"{_currentFilter} runs",
            };
            if (_minAscension > 0) parts.Add($"A{_minAscension}+");
            if (_recentCount > 0) parts.Add($"last {_recentCount}");
            return string.Join(", ", parts);
        }

        // ─── Content (rebuilt each open) ──────────────────────────────────────

        private static CharacterStats? GetStats(CharacterModel c)
        {
            try { return SaveManager.Instance.Progress?.GetStatsForCharacter(c.Id); }
            catch (Exception e) { Log.Warn("[CharacterManager] GetStatsForCharacter failed: " + e.Message); }
            return null;
        }

        private async void PopulateContent()
        {
            var c = _character;
            if (c == null)
            {
                if (_titleLabel != null) _titleLabel.Text = "Analytics";
                return;
            }

            if (_titleLabel != null) _titleLabel.Text = c.Title.GetFormattedText();
            if (_subtitleLabel != null) _subtitleLabel.Text = "Analytics";
            if (_statusLabel != null) _statusLabel.Text = ""; // clear any prior export message

            // Reset all filter axes on each fresh open.
            _currentFilter = GameModeFilter.All;
            _minAscension = 0;
            _recentCount = 0;
            _cardUpgradeAware = false;
            foreach (var (mode, btn) in _filterBtns) RefreshFilterButton(btn, mode);
            if (_ascBtn != null) _ascBtn.Text = AscLabel();
            if (_recentBtn != null) _recentBtn.Text = RecentLabel();
            if (_upgradeBtn != null) _upgradeBtn.Text = UpgradeLabel();

            // Paint a placeholder first, then defer the parse one frame so the screen appears
            // immediately instead of stalling on disk reads (M8, plan §4a). The aggregate is read
            // through AnalyticsCache, so re-opens of the same character are instant.
            if (_contentContainer != null)
                foreach (Node child in _contentContainer.GetChildren()) child.QueueFree();
            AddTextSection("Run History", "Crunching run history…");

            var tree = GetTree();
            if (tree != null)
                await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            // The screen may have been popped or switched to another character during the await.
            if (!IsInstanceValid(this) || _character != c) return;

            _fullAgg = AnalyticsCache.Get(c.Id);
            UpdateDisplay();
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
            // Decisive win rate (abandons excluded), consistent with the Win Rate windows section.
            body.AddChild(UiTheme.MakeBarRow("Win rate", BarLabelWidth, bar, WinRate(agg.CustomWins, agg.CustomDeaths), BarValueWidth));

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
                "Win rate excludes abandons. These runs are not counted by the game's official stats.",
                MutedColor, UiTheme.SmallFontSize));

            _contentContainer!.AddChild(panel);
        }

        /// <summary>Run details across recorded runs (not win/loss tallies).</summary>
        private void AddRunDetailsSection(CharacterAnalytics agg, string label)
        {
            var panel = MakeSectionPanel($"Run Details  ({label}, {agg.Total} run{(agg.Total == 1 ? "" : "s")})", out var body);

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

        /// <summary>Win rate over recent windows (last 10 / 50 / 100 / all decisive runs). M8.</summary>
        private void AddWinRateWindows(CharacterAnalytics agg)
        {
            var panel = MakeSectionPanel("Win Rate  (recent windows)", out var body);
            foreach (int n in new[] { 10, 50, 100, 0 })
            {
                var (wins, decisive, rate) = agg.WinRateWindow(n);
                string label = n <= 0 ? "All runs" : $"Last {n}";

                if (decisive <= 0)
                {
                    var empty = UiTheme.MakeBarTrack(16f, Array.Empty<(Color, float)>(), 1f);
                    body.AddChild(UiTheme.MakeBarRow(label, BarLabelWidth, empty, "—", BarValueWidth));
                    continue;
                }

                var segs = new (Color, float)[] { (UiTheme.Good, (float)rate) };
                var bar = UiTheme.MakeBarTrack(16f, segs, Math.Max(0f, 100f - (float)rate));
                body.AddChild(UiTheme.MakeBarRow(label, BarLabelWidth, bar, $"{rate:0.#}% ({wins}/{decisive})", BarValueWidth));
            }
            body.AddChild(UiTheme.MakeLabel(
                "Win rate over the most recent decisive runs (abandons excluded).",
                MutedColor, UiTheme.SmallFontSize));
            _contentContainer!.AddChild(panel);
        }

        private void AddFloorBars(CharacterAnalytics agg, string label)
        {
            if (agg.FloorReached.Count == 0) return;
            var panel = MakeSectionPanel($"Floors Reached Distribution  ({label})", out var body);
            var keys = new List<int>(agg.FloorReached.Keys);
            keys.Sort();
            int maxCount = 1;
            foreach (var f in keys) maxCount = Math.Max(maxCount, agg.FloorReached[f]);
            foreach (var f in keys)
            {
                int count = agg.FloorReached[f];
                var segs = new (Color, float)[] { (UiTheme.Heading, count) };
                var bar = UiTheme.MakeBarTrack(16f, segs, Math.Max(0, maxCount - count));
                body.AddChild(UiTheme.MakeBarRow($"{f} floor{(f == 1 ? "" : "s")}", BarLabelWidth, bar,
                    $"{count} run{(count == 1 ? "" : "s")}", BarValueWidth));
            }
            _contentContainer!.AddChild(panel);
        }

        // ─── Card analytics (M9) ─────────────────────────────────────────────

        /// <summary>Builds the four card ranked lists from the filtered aggregate.</summary>
        private void AddCardSections(CharacterAnalytics agg)
        {
            var stats = agg.ComputeCardStats(_cardUpgradeAware);

            bool anyData = false;
            foreach (var s in stats)
                if (s.Offered > 0 || s.RunsWith > 0) { anyData = true; break; }
            if (!anyData)
            {
                AddTextSection("Cards",
                    "No card data recorded for these runs yet. Card pick / win-rate stats come from per-floor reward history, which runs from older builds may not include.");
                return;
            }

            // Most picked — by raw pick count.
            var mostPicked = new List<CardStat>(stats);
            mostPicked.RemoveAll(s => s.Picks <= 0);
            mostPicked.Sort((a, b) => b.Picks.CompareTo(a.Picks));
            AddCardListSection("Most Picked Cards", mostPicked,
                s => $"{s.Picks} pick{(s.Picks == 1 ? "" : "s")}" + (s.Offered > 0 ? $"  {s.PickRatePct:0.#}%" : ""),
                s => s.Picks, UiTheme.Heading);

            // Highest / lowest win rate among cards seen in enough runs (caveat 6).
            var rated = new List<CardStat>(stats);
            rated.RemoveAll(s => s.RunsWith < CardMinSample);

            var best = new List<CardStat>(rated);
            best.Sort((a, b) => b.WinRatePct.CompareTo(a.WinRatePct));
            AddCardListSection($"Highest Win Rate  (≥{CardMinSample} runs)", best,
                s => $"{s.WinRatePct:0.#}% ({s.WinsWith}/{s.RunsWith})",
                s => (float)Math.Max(0, s.WinRatePct), UiTheme.Good, 100f);

            var worst = new List<CardStat>(rated);
            worst.Sort((a, b) => a.WinRatePct.CompareTo(b.WinRatePct));
            AddCardListSection($"Lowest Win Rate  (≥{CardMinSample} runs)", worst,
                s => $"{s.WinRatePct:0.#}% ({s.WinsWith}/{s.RunsWith})",
                s => (float)Math.Max(0, s.WinRatePct), UiTheme.Bad, 100f);

            // Most avoided — offered enough times but rarely taken (caveat 6).
            var avoided = new List<CardStat>(stats);
            avoided.RemoveAll(s => s.Offered < CardMinSample);
            avoided.Sort((a, b) =>
            {
                int c = a.PickRatePct.CompareTo(b.PickRatePct);
                return c != 0 ? c : b.Offered.CompareTo(a.Offered);
            });
            AddCardListSection($"Most Avoided  (≥{CardMinSample} offers)", avoided,
                s => $"{s.PickRatePct:0.#}% taken ({s.Picks}/{s.Offered})",
                s => (float)Math.Max(0, s.PickRatePct), UiTheme.Muted, 100f);
        }

        /// <summary>One capped, bar-ranked card list. <paramref name="maxWeight"/> &gt; 0 fixes the bar
        /// scale (e.g. 100 for percentages); 0 auto-scales to the largest shown value.</summary>
        private void AddCardListSection(string heading, List<CardStat> list,
            Func<CardStat, string> value, Func<CardStat, float> weight, Color color, float maxWeight = 0f)
        {
            if (list.Count == 0) return;

            int shown = Math.Min(CardListLimit, list.Count);
            string h = list.Count > shown ? $"{heading}  (top {shown} of {list.Count})" : heading;
            var panel = MakeSectionPanel(h, out var body);

            float max = maxWeight;
            if (max <= 0f) { max = 1f; for (int i = 0; i < shown; i++) max = Math.Max(max, weight(list[i])); }

            for (int i = 0; i < shown; i++)
            {
                var s = list[i];
                body.AddChild(UiTheme.MakeRankedRow(s.Name, value(s), weight(s), max, color));
            }
            _contentContainer!.AddChild(panel);
        }

        // ─── Encounter & death analytics (M10) ───────────────────────────────

        private void AddCombatSections(CharacterAnalytics agg)
        {
            var tiers = agg.ComputeTierStats();
            bool anyCombat = false;
            foreach (var t in tiers) if (t.Fights > 0) { anyCombat = true; break; }
            if (!anyCombat)
            {
                AddTextSection("Combat",
                    "No combat data recorded for these runs yet. Encounter / death stats come from per-floor combat history, which runs from older builds may not include.");
                return;
            }

            AddTierTable(tiers);

            var encounters = agg.ComputeEncounterStats();

            // Deadliest encounters — death rate, min fights (caveat 6).
            var deadliest = new List<EncounterStat>(encounters);
            deadliest.RemoveAll(e => e.Fights < CombatMinSample || e.Deaths <= 0);
            deadliest.Sort((a, b) =>
            {
                int c = b.DeathRatePct.CompareTo(a.DeathRatePct);
                return c != 0 ? c : b.Deaths.CompareTo(a.Deaths);
            });
            AddEncounterListSection($"Deadliest Encounters  (≥{CombatMinSample} fights)", deadliest,
                e => $"{e.DeathRatePct:0.#}% ({e.Deaths}/{e.Fights})",
                e => (float)Math.Max(0, e.DeathRatePct), UiTheme.Bad, 100f);

            // Most damaging encounters — average damage taken + p80, min fights.
            var damaging = new List<EncounterStat>(encounters);
            damaging.RemoveAll(e => e.Fights < CombatMinSample);
            damaging.Sort((a, b) => b.AvgDamage.CompareTo(a.AvgDamage));
            AddEncounterListSection($"Most Damaging Encounters  (≥{CombatMinSample} fights)", damaging,
                e => $"avg {e.AvgDamage:0}  ·  p80 {CharacterAnalytics.Percentile(e.Damages, 80)}",
                e => (float)e.AvgDamage, UiTheme.Heading, 0f);

            AddDeathCausesSection(agg.ComputeDeathCauses());
        }

        /// <summary>Compact Tier / Fights / Deaths / Death% / Avg-dmg table (M10).</summary>
        private void AddTierTable(List<TierStat> tiers)
        {
            var panel = MakeSectionPanel("Combat by Tier", out var body);

            var grid = new GridContainer { Columns = 5, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", 24);
            grid.AddThemeConstantOverride("v_separation", 4);

            void Cell(string text, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
            {
                var l = UiTheme.MakeLabel(text, color, UiTheme.BodyFontSize, align);
                l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                grid.AddChild(l);
            }

            Cell("Tier", MutedColor); Cell("Fights", MutedColor, HorizontalAlignment.Right);
            Cell("Deaths", MutedColor, HorizontalAlignment.Right); Cell("Death %", MutedColor, HorizontalAlignment.Right);
            Cell("Avg dmg", MutedColor, HorizontalAlignment.Right);

            foreach (var tier in new[] { RoomType.Monster, RoomType.Elite, RoomType.Boss })
            {
                TierStat? s = null;
                foreach (var t in tiers) if (t.Tier == tier) { s = t; break; }
                if (s == null || s.Fights == 0) continue;

                Cell(TierName(tier), BodyColor);
                Cell(s.Fights.ToString(), BodyColor, HorizontalAlignment.Right);
                Cell(s.Deaths.ToString(), BodyColor, HorizontalAlignment.Right);
                Cell(s.DeathRatePct >= 0 ? $"{s.DeathRatePct:0.#}%" : "—", BodyColor, HorizontalAlignment.Right);
                Cell($"{s.AvgDamage:0}", BodyColor, HorizontalAlignment.Right);
            }

            body.AddChild(grid);
            _contentContainer!.AddChild(panel);
        }

        private void AddDeathCausesSection(List<DeathCauseStat> causes)
        {
            int total = 0;
            foreach (var d in causes) total += d.Count;
            if (total <= 0) return;

            causes.Sort((a, b) => b.Count.CompareTo(a.Count));
            int shown = Math.Min(CardListLimit, causes.Count);
            string heading = causes.Count > shown ? $"Death Causes  (top {shown} of {causes.Count})" : "Death Causes";
            var panel = MakeSectionPanel(heading, out var body);

            int max = 1;
            for (int i = 0; i < shown; i++) max = Math.Max(max, causes[i].Count);

            for (int i = 0; i < shown; i++)
            {
                var d = causes[i];
                double pct = 100.0 * d.Count / total;
                Color col = d.Source switch
                {
                    DeathSource.Combat => UiTheme.Bad,
                    DeathSource.Event => UiTheme.Heading,
                    _ => UiTheme.Muted,
                };
                string label = d.Source == DeathSource.Event ? d.Name + "  (event)" : d.Name;
                body.AddChild(UiTheme.MakeRankedRow(label, $"{d.Count} ({pct:0.#}%)", d.Count, max, col));
            }
            _contentContainer!.AddChild(panel);
        }

        /// <summary>One capped, bar-ranked encounter list (mirrors <see cref="AddCardListSection"/>).</summary>
        private void AddEncounterListSection(string heading, List<EncounterStat> list,
            Func<EncounterStat, string> value, Func<EncounterStat, float> weight, Color color, float maxWeight = 0f)
        {
            if (list.Count == 0) return;

            int shown = Math.Min(CardListLimit, list.Count);
            string h = list.Count > shown ? $"{heading}  (top {shown} of {list.Count})" : heading;
            var panel = MakeSectionPanel(h, out var body);

            float max = maxWeight;
            if (max <= 0f) { max = 1f; for (int i = 0; i < shown; i++) max = Math.Max(max, weight(list[i])); }

            for (int i = 0; i < shown; i++)
            {
                var s = list[i];
                body.AddChild(UiTheme.MakeRankedRow(s.Name, value(s), weight(s), max, color));
            }
            _contentContainer!.AddChild(panel);
        }

        private static string TierName(RoomType tier) => tier switch
        {
            RoomType.Monster => "Normal",
            RoomType.Elite => "Elite",
            RoomType.Boss => "Boss",
            _ => tier.ToString(),
        };

        private void AddAscensionBars(CharacterAnalytics agg, string label)
        {
            var panel = MakeSectionPanel($"By Ascension  ({label})", out var body);
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

        private void AddActBars(CharacterAnalytics agg, string label)
        {
            var panel = MakeSectionPanel($"Act Reached Distribution  ({label})", out var body);
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
