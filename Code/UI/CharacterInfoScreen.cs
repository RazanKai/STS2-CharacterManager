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
        private Label? _sourceLabel;
        private VBoxContainer? _contentContainer;

        protected override Control? InitialFocusedControl => null;

        /// <summary>Sets the character to display. Call before pushing this screen.</summary>
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
                Text = "Character",
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

            _sourceLabel = new Label
            {
                Text = "",
                AnchorRight = 1f,
                OffsetLeft = PaddingH,
                OffsetRight = -PaddingH - 200f,
                OffsetTop = PaddingTop + HeaderHeight - 6f,
                OffsetBottom = PaddingTop + HeaderHeight + 22f,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _sourceLabel.AddThemeFontSizeOverride("font_size", 14);
            _sourceLabel.AddThemeColorOverride("font_color", MutedColor);
            AddChild(_sourceLabel);

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
                if (_titleLabel != null) _titleLabel.Text = "Character";
                return;
            }

            // Header
            if (_titleLabel != null)
            {
                _titleLabel.Text = c.Title.GetFormattedText();
                try { _titleLabel.AddThemeColorOverride("font_color", c.NameColor); }
                catch { /* keep default header color if NameColor is unavailable */ }
            }
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

            // Unlock requirement
            string unlock = SafeName(() => c.GetUnlockText().GetFormattedText(), "");
            if (!string.IsNullOrWhiteSpace(unlock))
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

        /// <summary>A titled section panel; out-parameter is the VBox to add body rows into.</summary>
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
