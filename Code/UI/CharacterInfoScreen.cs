using System;
using System.Collections.Generic;
using CharacterManager.Config;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace CharacterManager.UI
{
    /// <summary>
    /// A read-only drill-in card showing one character's details (M2). Pushed onto the
    /// submenu stack from a row in <see cref="CharacterManagerScreen"/>. Works identically
    /// for base and custom characters — every member read here is public on
    /// <see cref="CharacterModel"/> and identical across both.
    ///
    /// Like <see cref="CharacterManagerScreen"/>, this is a fully code-built NSubmenu:
    /// - We do NOT call base._Ready() / base.ConnectSignals() (they throw for subclasses /
    ///   look up a BackButton child we don't have).
    /// - A single instance is reused; <see cref="SetCharacter"/> is called before each Push
    ///   and the content is (re)built in <see cref="OnSubmenuOpened"/>.
    /// </summary>
    public class CharacterInfoScreen : NSubmenu
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
        private Label? _sourceLabel;
        private VBoxContainer? _contentContainer;

        protected override Control? InitialFocusedControl => null;

        /// <summary>Sets the character to display. Call before pushing this screen.</summary>
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

            _titleLabel = UiTheme.MakeLabel("Character", HeaderColor, UiTheme.TitleFontSize);
            UiTheme.PlaceInColumn(_titleLabel, PaddingTop, HeaderHeight);
            AddChild(_titleLabel);

            _sourceLabel = UiTheme.MakeLabel("", MutedColor, UiTheme.SmallFontSize);
            UiTheme.PlaceInColumn(_sourceLabel, PaddingTop + HeaderHeight - 6f, 26f);
            AddChild(_sourceLabel);

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

        // ─── Content (rebuilt each open) ──────────────────────────────────────

        private void PopulateContent()
        {
            if (_contentContainer == null) return;
            foreach (Node child in _contentContainer.GetChildren())
                child.QueueFree();

            var c = _character;
            if (c == null)
            {
                if (_titleLabel != null) _titleLabel.Text = "Character";
                return;
            }

            // Header
            if (_titleLabel != null)
                _titleLabel.Text = c.Title.GetFormattedText();
            if (_sourceLabel != null)
                _sourceLabel.Text = BuildSourceText(c);

            // Core stats
            var stats = new List<(string, string)>
            {
                ("Starting HP", c.StartingHp.ToString()),
                ("Starting Gold", c.StartingGold.ToString()),
                ("Max Energy", c.MaxEnergy.ToString()),
            };
            if (c.BaseOrbSlotCount > 0)
                stats.Add(("Orb Slots", c.BaseOrbSlotCount.ToString()));
            stats.Add(("Gender", c.Gender.ToString()));
            AddStatsSection("Overview", stats);

            // Starting deck (grouped with counts)
            AddListSection("Starting Deck", GroupDeck(c));

            // Deck composition by card type (bars)
            AddDeckComposition(c);

            // Starting relics
            var relics = new List<string>();
            try
            {
                if (c.StartingRelics != null)
                    foreach (var r in c.StartingRelics)
                        relics.Add(SafeName(() => r.Title.GetFormattedText(), "Unknown relic"));
            }
            catch (Exception e) { Log.Error("[CharacterManager] Failed reading relics: " + e.Message); }
            AddListSection("Starting Relics", relics);

            // Starting potions (only if any)
            var potions = new List<string>();
            try
            {
                if (c.StartingPotions != null)
                    foreach (var p in c.StartingPotions)
                        potions.Add(SafeName(() => p.Title.GetFormattedText(), "Unknown potion"));
            }
            catch (Exception e) { Log.Error("[CharacterManager] Failed reading potions: " + e.Message); }
            if (potions.Count > 0)
                AddListSection("Starting Potions", potions);

            // Unlock requirement. Skip when the lookup didn't resolve (returns the raw key, e.g.
            // "characters.IRONCLAD.unlockText") — base characters are unlocked by default and have
            // no unlock string.
            string unlock = SafeName(() => c.GetUnlockText().GetFormattedText(), "");
            if (!string.IsNullOrWhiteSpace(unlock) && !LooksLikeLocKey(unlock))
                AddTextSection("Unlock", unlock);
        }

        /// <summary>Groups the starting deck by card, preserving first-seen order, as "Name ×N".</summary>
        private static List<string> GroupDeck(CharacterModel c)
        {
            var order = new List<string>();
            var counts = new Dictionary<string, int>();
            try
            {
                if (c.StartingDeck != null)
                {
                    foreach (var card in c.StartingDeck)
                    {
                        string name = SafeName(() => card.Title, "Unknown card");
                        if (!counts.ContainsKey(name)) { counts[name] = 0; order.Add(name); }
                        counts[name]++;
                    }
                }
            }
            catch (Exception e) { Log.Error("[CharacterManager] Failed reading deck: " + e.Message); }

            var result = new List<string>(order.Count);
            foreach (var name in order)
                result.Add(counts[name] > 1 ? $"{name}  ×{counts[name]}" : name);
            return result;
        }

        // ─── Deck composition (M6 cont.: bars by card type to fill the page) ──

        private void AddDeckComposition(CharacterModel c)
        {
            var comp = GroupDeckByType(c);
            if (comp.Count == 0) return;

            int total = 0, max = 1;
            foreach (var (_, n) in comp) { total += n; max = Math.Max(max, n); }

            var panel = MakeSectionPanel($"Deck Composition  ({total} cards)", out var body);
            foreach (var (type, n) in comp)
            {
                var segs = new (Color, float)[] { (TypeColor(type), n) };
                var bar = UiTheme.MakeBarTrack(16f, segs, Math.Max(0, max - n));
                body.AddChild(UiTheme.MakeBarRow(type, 120f, bar, n.ToString(), 60f));
            }
            _contentContainer!.AddChild(panel);
        }

        /// <summary>Counts the starting deck by card type, in canonical type order.</summary>
        private static List<(string type, int count)> GroupDeckByType(CharacterModel c)
        {
            var counts = new Dictionary<string, int>();
            try
            {
                if (c.StartingDeck != null)
                {
                    foreach (var card in c.StartingDeck)
                    {
                        string t;
                        try { t = card.Type.ToString(); }
                        catch { t = "Other"; }
                        counts[t] = counts.TryGetValue(t, out var n) ? n + 1 : 1;
                    }
                }
            }
            catch (Exception e) { Log.Error("[CharacterManager] Failed reading deck types: " + e.Message); }

            var result = new List<(string, int)>();
            foreach (var t in new[] { "Attack", "Skill", "Power", "Status", "Curse" })
                if (counts.TryGetValue(t, out var n)) { result.Add((t, n)); counts.Remove(t); }
            foreach (var kv in counts) result.Add((kv.Key, kv.Value));
            return result;
        }

        private static Color TypeColor(string type) => type switch
        {
            "Attack" => new Color("ff6b5e"),
            "Skill" => UiTheme.Heading,
            "Power" => UiTheme.Title,
            "Status" => UiTheme.Muted,
            "Curse" => new Color("9b6bd6"),
            _ => UiTheme.Body,
        };

        private static string BuildSourceText(CharacterModel c)
        {
            if (CharacterHelper.IsBaseCharacter(c.Id))
                return "Base game";
            var mod = CharacterHelper.GetSourceMod(c);
            if (mod == null) return "Unknown mod";
            string author = string.IsNullOrWhiteSpace(mod.manifest.author) ? "" : $"  ·  by {mod.manifest.author}";
            return $"{mod.manifest.name}  v{mod.manifest.version}{author}";
        }

        // ─── Section builders ─────────────────────────────────────────────────

        private void AddStatsSection(string heading, List<(string label, string value)> rows)
        {
            var panel = MakeSectionPanel(heading, out var body);
            foreach (var (label, value) in rows)
            {
                var line = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                line.AddThemeConstantOverride("separation", 12);

                var l = new Label { Text = label, CustomMinimumSize = new Vector2(180f, 0f) };
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

        /// <summary>A titled section panel; out-parameter is the VBox to add body rows into.</summary>
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

        /// <summary>True if the text looks like an unresolved localization key (no spaces, dotted,
        /// or the literal unlockText/LOCKED markers) rather than real display text.</summary>
        private static bool LooksLikeLocKey(string s)
        {
            if (s.Contains("unlockText") || s.Contains("LOCKED.title")) return true;
            // Dotted token with no spaces, e.g. "characters.IRONCLAD.unlockText".
            return !s.Contains(' ') && s.Contains('.');
        }

        /// <summary>Resolves a possibly-throwing name lookup, falling back on failure.</summary>
        private static string SafeName(Func<string> get, string fallback)
        {
            try
            {
                var s = get();
                return string.IsNullOrEmpty(s) ? fallback : s;
            }
            catch { return fallback; }
        }
    }
}
