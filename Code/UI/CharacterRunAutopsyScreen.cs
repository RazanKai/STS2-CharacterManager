using System;
using System.Collections.Generic;
using System.Text;
using CharacterManager.Analytics;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;

namespace CharacterManager.UI
{
    /// <summary>
    /// Single-run "autopsy" (M12). A read-only drill-in for one <c>.run</c> file: summary, HP
    /// timeline, ancients taken, boss damage, and a per-act floor-by-floor event log. Opened from the
    /// analytics screen on the most recent run; Prev/Next walk the character's run history (newest
    /// first). Loads the one selected run directly — no aggregate cache needed.
    /// </summary>
    public class CharacterRunAutopsyScreen : NSubmenu
    {
        private const float PaddingTop = UiTheme.PaddingTop;
        private const float HeaderHeight = UiTheme.HeaderHeight;

        private static readonly Color HeaderColor = UiTheme.Title;
        private static readonly Color MutedColor = UiTheme.Muted;
        private static readonly Color BodyColor = UiTheme.Body;
        private static readonly Color SectionColor = UiTheme.Heading;

        private CharacterModel? _character;
        private List<RunSummary> _runs = new();
        private int _index;

        private Label? _titleLabel;
        private Label? _subtitleLabel;
        private Label? _navLabel;
        private Button? _prevBtn;
        private Button? _nextBtn;
        private VBoxContainer? _content;

        protected override Control? InitialFocusedControl => null;

        /// <summary>Sets the character and the (newest-first) run list, plus the starting index.</summary>
        public void SetRuns(CharacterModel character, List<RunSummary> runs, int startIndex)
        {
            _character = character;
            _runs = runs ?? new List<RunSummary>();
            _index = Math.Clamp(startIndex, 0, Math.Max(0, _runs.Count - 1));
        }

        public override void _Ready()
        {
            UiTheme.ApplyGameTheme(this);
            ConnectSignals();
            BuildLayout();
        }

        protected override void ConnectSignals() { /* see CharacterManagerScreen for why we skip base */ }

        public override void OnSubmenuOpened() => Populate();

        // ─── Chrome ───────────────────────────────────────────────────────────

        private void BuildLayout()
        {
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;

            AddChild(new ColorRect
            {
                Color = UiTheme.Backdrop,
                AnchorRight = 1f,
                AnchorBottom = 1f,
                MouseFilter = MouseFilterEnum.Ignore,
            });

            _titleLabel = UiTheme.MakeLabel("Run Autopsy", HeaderColor, UiTheme.TitleFontSize);
            UiTheme.PlaceInColumn(_titleLabel, PaddingTop, HeaderHeight);
            AddChild(_titleLabel);

            _subtitleLabel = UiTheme.MakeLabel("", MutedColor, UiTheme.SmallFontSize);
            UiTheme.PlaceInColumn(_subtitleLabel, PaddingTop + HeaderHeight - 6f, 26f);
            AddChild(_subtitleLabel);

            var backBtn = UiTheme.MakeButton("← Back", null, 120f);
            UiTheme.PlaceColumnRight(backBtn, PaddingTop, HeaderHeight, 120f);
            backBtn.Pressed += () => _stack?.Pop();
            AddChild(backBtn);

            // Navigation row.
            float navY = PaddingTop + HeaderHeight + 18f;
            var navBar = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            navBar.AddThemeConstantOverride("separation", 10);
            UiTheme.PlaceInColumn(navBar, navY, 28f);
            AddChild(navBar);

            _prevBtn = UiTheme.MakeButton("◀ Older", UiTheme.Body, 110f);
            _prevBtn.Pressed += () => Step(+1); // older = later in the newest-first list
            navBar.AddChild(_prevBtn);

            _nextBtn = UiTheme.MakeButton("Newer ▶", UiTheme.Body, 110f);
            _nextBtn.Pressed += () => Step(-1);
            navBar.AddChild(_nextBtn);

            _navLabel = UiTheme.MakeLabel("", MutedColor, UiTheme.BodyFontSize);
            _navLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            navBar.AddChild(_navLabel);

            float scrollY = navY + 40f;
            var scroll = new ScrollContainer();
            UiTheme.PlaceColumnStretch(scroll, scrollY, UiTheme.PaddingTop);
            AddChild(scroll);

            _content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _content.AddThemeConstantOverride("separation", 10);
            scroll.AddChild(_content);
        }

        private void Step(int delta)
        {
            int next = _index + delta;
            if (next < 0 || next >= _runs.Count) return;
            _index = next;
            Populate();
        }

        // ─── Population ────────────────────────────────────────────────────────

        private void Populate()
        {
            if (_content == null) return;
            foreach (Node child in _content.GetChildren()) child.QueueFree();

            var c = _character;
            if (_titleLabel != null) _titleLabel.Text = c != null ? c.Title.GetFormattedText() : "Run Autopsy";
            if (_subtitleLabel != null) _subtitleLabel.Text = "Run Autopsy";

            if (_runs.Count == 0 || c == null)
            {
                if (_navLabel != null) _navLabel.Text = "";
                AddText("Run", "No runs available to inspect.");
                RefreshNavButtons();
                return;
            }

            var summary = _runs[_index];
            RunHistory? h = LoadRun(summary.HistoryName);
            if (h == null)
            {
                AddText("Run", "Could not load this run file.");
                RefreshNavButtons();
                return;
            }

            string result = h.Win ? "Victory" : (h.WasAbandoned ? "Abandoned" : "Death");
            if (_navLabel != null)
                _navLabel.Text = $"Run {_index + 1} of {_runs.Count}   ·   {FormatDate(h.StartTime)}   ·   {result}";
            RefreshNavButtons();

            // Walk the run for this character's player.
            RunHistoryPlayer? me = null;
            foreach (var p in h.Players)
                if (p.Character == c.Id) { me = p; break; }

            var floors = BuildFloorViews(h, me);

            AddSummarySection(h, floors);
            AddHpTimelineSection(floors);
            AddAncientsSection(floors);
            AddBossDamageSection(floors);
            AddFloorLog(floors);
        }

        private void RefreshNavButtons()
        {
            if (_prevBtn != null) _prevBtn.Disabled = _index >= _runs.Count - 1;
            if (_nextBtn != null) _nextBtn.Disabled = _index <= 0;
        }

        private static RunHistory? LoadRun(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                var result = SaveManager.Instance.LoadRunHistory(name);
                return result.Success ? result.SaveData : null;
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] autopsy load failed: " + e.Message);
                return null;
            }
        }

        // ─── Per-floor model ───────────────────────────────────────────────────

        private sealed class FloorView
        {
            public int Act;
            public int FloorInAct;
            public RoomType RoomType;
            public string Name = "";
            public int CurrentHp;
            public int MaxHp;
            public int Damage;
            public string Events = "";
            public readonly List<string> Ancients = new();
            public bool IsBoss => RoomType == RoomType.Boss;
        }

        private static List<FloorView> BuildFloorViews(RunHistory h, RunHistoryPlayer? me)
        {
            var list = new List<FloorView>();
            if (h.MapPointHistory == null) return list;

            ulong playerId = me?.Id ?? 0UL;
            int act = 0;
            foreach (var rooms in h.MapPointHistory)
            {
                act++;
                if (rooms == null) continue;
                int floorInAct = 0;
                foreach (var entry in rooms)
                {
                    floorInAct++;
                    if (entry == null) continue;

                    var room = (entry.Rooms != null && entry.Rooms.Count > 0) ? entry.Rooms[0] : null;
                    var pe = PlayerEntry(entry, playerId);

                    var fv = new FloorView
                    {
                        Act = act,
                        FloorInAct = floorInAct,
                        RoomType = room?.RoomType ?? RoomType.Unassigned,
                        Name = RoomName(room),
                    };
                    if (pe != null)
                    {
                        fv.CurrentHp = pe.CurrentHp;
                        fv.MaxHp = pe.MaxHp;
                        fv.Damage = pe.DamageTaken;
                        fv.Events = SummariseEvents(pe);
                        if (pe.AncientChoices != null)
                            foreach (var a in pe.AncientChoices)
                                if (a != null && a.WasChosen && a.Title != null)
                                {
                                    try { fv.Ancients.Add(a.Title.GetFormattedText()); }
                                    catch { /* ignore unresolved */ }
                                }
                    }
                    list.Add(fv);
                }
            }
            return list;
        }

        private static PlayerMapPointHistoryEntry? PlayerEntry(MapPointHistoryEntry entry, ulong playerId)
        {
            var stats = entry.PlayerStats;
            if (stats == null || stats.Count == 0) return null;
            foreach (var ps in stats)
                if (ps.PlayerId == playerId) return ps;
            return stats.Count == 1 ? stats[0] : null;
        }

        private static string RoomName(MapPointRoomHistoryEntry? room)
        {
            if (room == null) return "—";
            switch (room.RoomType)
            {
                case RoomType.Monster:
                case RoomType.Elite:
                case RoomType.Boss:
                case RoomType.Event:
                    return room.ModelId != null && room.ModelId != ModelId.none
                        ? NameResolver.Resolve(room.ModelId)
                        : RoomTypeLabel(room.RoomType);
                default:
                    return RoomTypeLabel(room.RoomType);
            }
        }

        private static string RoomTypeLabel(RoomType t) => t switch
        {
            RoomType.Monster => "Combat",
            RoomType.Elite => "Elite",
            RoomType.Boss => "Boss",
            RoomType.Event => "Event",
            RoomType.Shop => "Shop",
            RoomType.Treasure => "Treasure",
            RoomType.RestSite => "Rest Site",
            _ => "—",
        };

        /// <summary>Builds a compact " · "-joined summary of what happened to the player on a floor.</summary>
        private static string SummariseEvents(PlayerMapPointHistoryEntry pe)
        {
            var parts = new List<string>();

            if (pe.DamageTaken > 0) parts.Add($"−{pe.DamageTaken} HP");
            if (pe.HpHealed > 0) parts.Add($"+{pe.HpHealed} HP");

            int goldNet = pe.GoldGained - pe.GoldSpent - pe.GoldLost;
            if (goldNet != 0) parts.Add($"{(goldNet > 0 ? "+" : "")}{goldNet}g");

            // Cards taken (from choices) else gained outright.
            var taken = new List<ModelId>();
            if (pe.CardChoices != null)
                foreach (var ch in pe.CardChoices)
                    if (ch.wasPicked && ch.Card?.Id != null) taken.Add(ch.Card.Id);
            if (taken.Count > 0) parts.Add("took " + NamesOf(taken));
            else if (pe.CardsGained != null && pe.CardsGained.Count > 0)
            {
                var gained = new List<ModelId>();
                foreach (var g in pe.CardsGained) if (g?.Id != null) gained.Add(g.Id);
                if (gained.Count > 0) parts.Add("gained " + NamesOf(gained));
            }

            if (pe.CardsRemoved != null && pe.CardsRemoved.Count > 0)
            {
                var removed = new List<ModelId>();
                foreach (var r in pe.CardsRemoved) if (r?.Id != null) removed.Add(r.Id);
                if (removed.Count > 0) parts.Add("removed " + NamesOf(removed));
            }
            if (pe.UpgradedCards != null && pe.UpgradedCards.Count > 0)
                parts.Add("upgraded " + NamesOf(pe.UpgradedCards));

            // Relics gained (chosen choices + bought).
            var relics = new List<ModelId>();
            if (pe.RelicChoices != null)
                foreach (var rc in pe.RelicChoices) if (rc.wasPicked && rc.choice != ModelId.none) relics.Add(rc.choice);
            if (pe.BoughtRelics != null) foreach (var id in pe.BoughtRelics) if (id != ModelId.none) relics.Add(id);
            if (relics.Count > 0) parts.Add("relic " + NamesOf(relics));

            if (pe.PotionUsed != null && pe.PotionUsed.Count > 0)
                parts.Add("used " + NamesOf(pe.PotionUsed));

            return string.Join("  ·  ", parts);
        }

        private static string NamesOf(List<ModelId> ids)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(NameResolver.Resolve(ids[i]));
            }
            return sb.ToString();
        }

        // ─── Sections ──────────────────────────────────────────────────────────

        private void AddSummarySection(RunHistory h, List<FloorView> floors)
        {
            var panel = MakeSection("Summary", out var body);

            int floorCount = floors.Count;
            int acts = h.MapPointHistory?.Count ?? 0;
            string killedBy = "—";
            if (!h.Win)
            {
                if (h.KilledByEncounter != null && h.KilledByEncounter != ModelId.none)
                    killedBy = NameResolver.Resolve(h.KilledByEncounter);
                else if (h.KilledByEvent != null && h.KilledByEvent != ModelId.none)
                    killedBy = NameResolver.Resolve(h.KilledByEvent) + " (event)";
                else if (h.WasAbandoned) killedBy = "Abandoned";
            }

            var rows = new List<(string, string)>
            {
                ("Result", h.Win ? "Victory" : (h.WasAbandoned ? "Abandoned" : "Death")),
                ("Ascension", h.Ascension.ToString()),
                ("Run time", FormatDuration(h.RunTime)),
                ("Acts reached", acts.ToString()),
                ("Floors", floorCount.ToString()),
                ("Killed by", killedBy),
                ("Game mode", h.GameMode.ToString()),
                ("Seed", string.IsNullOrEmpty(h.Seed) ? "—" : h.Seed),
            };
            AddGrid(body, rows, 2);
            _content!.AddChild(panel);
        }

        /// <summary>
        /// Per-floor HP as a column chart, built from containers + ColorRect (custom-drawn controls
        /// don't render in this load path). Column <i>height</i> = absolute HP (all columns share the
        /// run's peak max-HP scale, so headroom reads at a glance); column <i>colour</i> = health
        /// fraction (red when low, green when full). Bars sit in a rounded groove with a faint
        /// midline at 50% of peak for reference.
        /// </summary>
        private void AddHpTimelineSection(List<FloorView> floors)
        {
            var pts = new List<(int cur, int max)>();
            foreach (var f in floors)
                if (f.MaxHp > 0) pts.Add((f.CurrentHp, f.MaxHp));
            if (pts.Count < 2) return;

            int peak = 1;
            foreach (var p in pts) peak = Math.Max(peak, Math.Max(p.max, p.cur));

            var panel = MakeSection("HP Over Time", out var body);

            // Rounded groove behind the columns.
            var track = UiTheme.MakePanel(new Color(0f, 0f, 0f, 0.28f), border: false);

            // Stack a faint 50%-of-peak reference line under the columns.
            var stack = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0f, 116f) };
            track.AddChild(stack);

            var midline = new ColorRect
            {
                Color = new Color(UiTheme.Muted.R, UiTheme.Muted.G, UiTheme.Muted.B, 0.25f),
                AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
                OffsetBottom = 1f, MouseFilter = MouseFilterEnum.Ignore,
            };
            stack.AddChild(midline);

            var cols = new HBoxContainer
            {
                AnchorRight = 1f, AnchorBottom = 1f, MouseFilter = MouseFilterEnum.Ignore,
            };
            cols.AddThemeConstantOverride("separation", pts.Count > 28 ? 1 : 3);
            stack.AddChild(cols);

            foreach (var (cur, max) in pts)
            {
                int curC = Math.Max(0, cur);
                int empty = Math.Max(0, peak - curC);
                float health = max > 0 ? Mathf.Clamp((float)curC / max, 0f, 1f) : 0f;
                Color barColor = UiTheme.Bad.Lerp(UiTheme.Good, health);

                var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                col.AddThemeConstantOverride("separation", 0);
                if (empty > 0)
                    col.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = empty, MouseFilter = MouseFilterEnum.Ignore });
                if (curC > 0)
                    col.AddChild(new ColorRect { Color = barColor, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = curC, MouseFilter = MouseFilterEnum.Ignore });
                cols.AddChild(col);
            }

            body.AddChild(track);
            body.AddChild(UiTheme.MakeLabel($"Current HP per floor — bar height is HP (peak {peak}), colour is health % (red low, green full).",
                MutedColor, UiTheme.SmallFontSize));
            _content!.AddChild(panel);
        }

        private void AddAncientsSection(List<FloorView> floors)
        {
            var taken = new List<string>();
            foreach (var f in floors)
                foreach (var a in f.Ancients)
                    if (!string.IsNullOrWhiteSpace(a)) taken.Add(a);
            if (taken.Count == 0) return;

            var panel = MakeSection("Ancients Taken", out var body);
            foreach (var a in taken)
            {
                var lbl = UiTheme.MakeLabel("•  " + a, BodyColor, UiTheme.BodyFontSize);
                lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                body.AddChild(lbl);
            }
            _content!.AddChild(panel);
        }

        private void AddBossDamageSection(List<FloorView> floors)
        {
            var bosses = new List<FloorView>();
            foreach (var f in floors) if (f.IsBoss) bosses.Add(f);
            if (bosses.Count == 0) return;

            var panel = MakeSection("Boss Fights", out var body);
            foreach (var f in bosses)
                body.AddChild(UiTheme.MakeBarRow($"A{f.Act}  {f.Name}", 240f,
                    UiTheme.MakeBarTrack(14f, new[] { (UiTheme.Bad, (float)f.Damage) }, 0f),
                    $"−{f.Damage} HP", 90f));
            _content!.AddChild(panel);
        }

        private void AddFloorLog(List<FloorView> floors)
        {
            if (floors.Count == 0) return;
            int lastAct = -1;
            VBoxContainer? body = null;

            foreach (var f in floors)
            {
                if (f.Act != lastAct)
                {
                    var panel = MakeSection($"Act {f.Act}", out body);
                    _content!.AddChild(panel);
                    lastAct = f.Act;
                }
                if (body == null) continue;

                var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                row.AddThemeConstantOverride("separation", 10);

                var head = UiTheme.MakeLabel($"{f.FloorInAct}.  {f.Name}", TierColor(f.RoomType), UiTheme.BodyFontSize);
                head.CustomMinimumSize = new Vector2(220f, 0f);
                head.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                row.AddChild(head);

                var detail = UiTheme.MakeLabel(f.Events.Length > 0 ? f.Events : "—", MutedColor, UiTheme.BodyFontSize);
                detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                detail.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                row.AddChild(detail);

                var hp = UiTheme.MakeLabel(f.MaxHp > 0 ? $"{f.CurrentHp}/{f.MaxHp}" : "", BodyColor, UiTheme.SmallFontSize,
                    HorizontalAlignment.Right);
                hp.CustomMinimumSize = new Vector2(70f, 0f);
                hp.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                row.AddChild(hp);

                body.AddChild(row);
            }
        }

        private static Color TierColor(RoomType t) => t switch
        {
            RoomType.Boss => UiTheme.Title,
            RoomType.Elite => UiTheme.Bad,
            RoomType.Monster => UiTheme.Body,
            RoomType.Event => UiTheme.Heading,
            _ => UiTheme.Muted,
        };

        // ─── Shared section/grid builders (mirror the analytics screen) ─────────

        private static PanelContainer MakeSection(string heading, out VBoxContainer body)
        {
            var panel = UiTheme.MakePanel(UiTheme.PanelBg);
            var outer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            outer.AddThemeConstantOverride("separation", 6);
            panel.AddChild(outer);
            outer.AddChild(UiTheme.MakeLabel(heading, SectionColor, UiTheme.SectionFontSize));
            body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            body.AddThemeConstantOverride("separation", 3);
            outer.AddChild(body);
            return panel;
        }

        private void AddGrid(VBoxContainer body, List<(string label, string value)> rows, int columns)
        {
            var grid = new GridContainer { Columns = columns, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", 28);
            grid.AddThemeConstantOverride("v_separation", 4);
            foreach (var (label, value) in rows)
            {
                var cell = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                cell.AddThemeConstantOverride("separation", 10);
                var l = UiTheme.MakeLabel(label, MutedColor, UiTheme.BodyFontSize);
                l.CustomMinimumSize = new Vector2(150f, 0f);
                cell.AddChild(l);
                var v = UiTheme.MakeLabel(value, BodyColor, UiTheme.BodyFontSize);
                v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                v.ClipText = true;
                cell.AddChild(v);
                grid.AddChild(cell);
            }
            body.AddChild(grid);
        }

        private void AddText(string heading, string text)
        {
            var panel = MakeSection(heading, out var body);
            var lbl = UiTheme.MakeLabel(text, BodyColor, UiTheme.BodyFontSize);
            lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            body.AddChild(lbl);
            _content!.AddChild(panel);
        }

        // ─── Formatting ──────────────────────────────────────────────────────

        private static string FormatDate(long unixSeconds)
        {
            if (unixSeconds <= 0) return "—";
            try { return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm"); }
            catch { return "—"; }
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds <= 0) return "0s";
            long total = (long)Math.Round(seconds);
            long h = total / 3600, m = (total % 3600) / 60, s = total % 60;
            if (h > 0) return $"{h}h {m}m";
            if (m > 0) return $"{m}m {s}s";
            return $"{s}s";
        }
    }
}
