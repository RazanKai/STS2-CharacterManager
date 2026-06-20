# Roadmap — From "Custom Character Stats" to a Character Management Mod

This document plans the evolution of the mod from a single-purpose stats injector
into a roster/character management suite. Every claim below was cross-checked
against the decompiled game source (game version per `mod_manifest.json`
`min_game_version`). No feature here mutates the player's save files.

Audience note: the implementation will be done by a model that should treat this
as a spec. Concrete class/method names, signatures, patch targets, and reflection
gotchas are given in each milestone and in the **Verified API reference** appendix.
Verify names still exist with `get_entity_source` / `search_game_code` before
patching, since the game updates.

## Implementation status (updated 2026-06-19)

### M1 — core complete & verified in-game (In-Select works; live rebuild added in v0.4.0 — no restart)

The Character Manager screen opens from the Compendium and renders all characters. As of
this session, **Bug 1 (duplicates) and Bug 2 (stats toggle) are fixed and confirmed live**
via screenshots: Ryoshu and The Cursed now appear once each, and the per-custom-character
"Stats Shown" toggle drives the Compendium Statistics screen. Two follow-up items from
in-game testing were then addressed (see "In-Select toggle" and "Base-character stats
toggle" below); the In-Select fix is built/deployed but awaiting a confirming re-test.

**Bug 1 — Duplicate custom characters in the manager list — FIXED (verified).**
Root cause (proven by inspecting the installed mod DLLs): the installed character
libraries **BaseLib, KitLib, and STS2 RitsuLib all Harmony-patch
`ModelDb.get_AllCharacters`**, and the Ryoshu mod ships its own
`ModelDbAllCharactersPatch`, to append modded characters to that array. So at runtime
`ModelDb.AllCharacters` already contains Ryoshu and The Cursed. The old
`GetAllCharacters()` then did `AddRange(ModelDb.AllCharacters)` **plus**
`AddRange(custom from _contentById)` with **no dedup across the merge**, so every
custom character was listed twice.

The three earlier dedup attempts (ReferenceEquals, `HashSet<ModelId>`, `HashSet<Type>`)
all failed for the same reason: each deduped only *within* the `_contentById` slice and
so never collapsed an `AllCharacters` entry against a registry entry. The prior
inference that "the mods register multiple subclasses" was incorrect — the duplication
is across the two enumeration sources, not within one.

Fix (`Code/CharacterHelper.cs`): merge both sources (`ModelDb.AllCharacters` +
`_contentById`) and deduplicate the **whole** result by `ModelId` — the key every store
in this mod uses. Base characters are emitted first in canonical order, customs follow
sorted by title. `GetCustomCharacters()` is now derived from `GetAllCharacters()` so the
manager list and the Compendium stats injection always see the identical deduped set.

**Bug 2 — "Stats Shown" toggle had no effect — FIXED (verified).**
The `VisibilityStore` was toggled/persisted by the UI but nothing read it to drive the
Compendium stats display, because the base mod's `NGeneralStatsGridPatch` had never been
ported. Added `Code/Patches/StatsGridPatch.cs` — a Harmony postfix on
`NGeneralStatsGrid.LoadStats` that appends one `NCharacterStats.Create(stats)` section to
the private `_characterStatContainer` for each **custom** character that is visible
(`VisibilityStore.IsVisible`) and has recorded stats (`GetStatsForCharacter != null`,
matching the game's own base-character rendering). The game's `LoadStats` already adds the
5 base sections, so the postfix is custom-only by design — the one place "custom-only"
applies.

**In-Select enable/disable toggle — FIXED (built/deployed; awaiting confirming re-test).**
The "In Select" toggle (`EnabledStore` + `Code/Patches/CharacterSelectPatch.cs`) is meant
to hide a disabled custom character from the character-select screen. Two successive
attempts and what was learned:

1. *First attempt — hide the button (failed).* Postfix `InitCharacterButtons`, find the
   disabled character's `NCharacterSelectButton` under `_charButtonContainer`, set
   `Visible = false`. Did nothing in-game. Decompiling STS2 RitsuLib showed why: it reshapes
   the select screen — on `NCharacterSelectScreen._Ready` it installs an
   `NCharacterButtonStripScroller` (reparents/measures the buttons), and it manages select
   visibility through a thread-static **selection-policy scope** plus a postfix on the
   `ModelDb.AllCharacters` getter (`CharacterVanillaSelectionPolicyAllCharactersPatch`),
   filtering by an opt-in `IModCharacterVanillaSelectionPolicy.HideFromVanillaCharacterSelect`.
   Hiding a button after the fact doesn't survive that pipeline.

2. *Second attempt — filter the roster, wrong order (failed).* Mirror RitsuLib: arm a flag in
   a prefix on `InitCharacterButtons`, disarm in a finalizer, and in a postfix on the
   `ModelDb.AllCharacters` getter remove disabled customs while armed → the button is never
   built. Correct design, but it still showed Ryoshu. **Root cause: Harmony postfix ordering.**
   Modded characters are themselves *appended* to `ModelDb.AllCharacters` by other getter
   postfixes (Ryoshu patches the getter itself; The Cursed via the framework). Our removal
   postfix ran at default priority — *before* those add-postfixes — so it filtered a list that
   didn't yet contain Ryoshu. (Confirmed our patches do apply: `ModEntry.Init` logs
   "initialized" only *after* `PatchAll()`, which throws on failure.)

3. *Third attempt — getter postfix runs last (still showed Ryoshu, logged nothing).* Same
   scope-flag + getter-postfix approach, with the getter postfix at `[HarmonyPriority(Priority.Last)]`
   + `[HarmonyAfter(...)]`. The roadmap claimed the arming scope covered "both" select screens,
   but the arm/disarm methods stacked **two** `[HarmonyPatch]` target attributes on a single
   method each. **That was the bug.**

4. *Root cause — stacked `[HarmonyPatch]` attributes merge into ONE target (fixed 2026-06-16,
   verified by log analysis).* Harmony does not treat multiple `[HarmonyPatch(typeof(X),"M")]`
   attributes on one patch method as "patch X and Y" — it **merges** them into a single target
   descriptor with the last type winning. So the prefix/finalizer only ever patched
   `NCustomRunScreen.InitCharacterButtons`; the normal `NCharacterSelectScreen` was never armed.
   When the player opened normal character select, `_filtering` stayed false, the getter postfix
   short-circuited, Ryoshu was never removed, and the "hid N" line never logged — exactly the
   reported symptom (Ryoshu present, `grep` empty). Confirmed against the live log: the mod loads
   and `[CharacterManager] initialized` prints (so `PatchAll` succeeded), and `charactermanager_enabled.json`
   correctly holds `"CHARACTER.RYOSHU": false`, so the store and detection were never the problem.
   Decompiling RitsuLib confirmed the rest of the design is sound: `CharacterVanillaSelectionPolicyPatches`
   wraps `NCharacterSelectScreen.InitCharacterButtons` with a prefix/finalizer (it does **not**
   skip the original), and `CharacterVanillaSelectionPolicyAllCharactersPatch` filters the getter
   at `Priority(0)` after the add-postfixes — i.e. the vanilla `foreach (… in ModelDb.AllCharacters)`
   does run, so our getter postfix fires once armed.

   *Fix.* Split the arming into **four single-target methods** (`Arm_Select`/`Disarm_Select` on
   `NCharacterSelectScreen`, `Arm_CustomRun`/`Disarm_CustomRun` on `NCustomRunScreen`), each with
   exactly one `[HarmonyPatch]`. `_filtering` is now a depth counter for nesting safety. The getter
   postfix keeps `Priority.Last` + `[HarmonyAfter]`. Built, deployed (Release dll copied to the mods
   folder; decompile of the deployed assembly confirms 4 distinct single-target patches). **Needs a
   full game restart** (code hot-reload is unreliable here), then verify: disable Ryoshu in the
   Character Manager, open the normal character-select screen, confirm Ryoshu is gone, and check the
   log: `grep -iE "CharacterManager.*character-select|hid .* disabled" <godot.log>` should now show
   the "hid 1 disabled custom character(s)" line. **CONFIRMED WORKING (2026-06-16):** disabling a
   custom character now hides it from character select.

**~~KNOWN LIMITATION — In-Select changes require a game restart~~ — RESOLVED in v0.4.0.**
The "In Select" toggle filters the roster only during `InitCharacterButtons`, which runs when the
character-select screen is first built. The screen is built once (eagerly, in
`NMainMenuSubmenuStack._Ready`) and cached in the stack's `_characterSelectSubmenu` field, so a
runtime toggle was not reflected until the screen was rebuilt from scratch — i.e. after a restart.
Mutating the live buttons doesn't work (a disabled char has no button; RitsuLib's
`NCharacterButtonStripScroller` ignores per-button `Visible`). **v0.4.0 fix:** on
`EnabledStore.OnToggle`, free + null the stack's cached `_characterSelectSubmenu` and
`_customRunScreen` so the next open rebuilds the screen fresh under the existing `AllCharacters`
filter (identical to a launch-time build, which honors the toggle). Implemented in
`CharacterSelectPatch` (postfix on `NMainMenuSubmenuStack._Ready` captures the stack + wires the
handler; guards against freeing a currently-visible screen). Verified in-game — no restart needed.

**Base-character "Stats Shown" toggle removed.** In-game the manager showed a Stats-Shown
toggle on base-character rows, but base characters always render on the Compendium stats
screen (the game adds them itself; `StatsGridPatch` only injects custom rows), so the toggle
did nothing. `CharacterManagerScreen.BuildCharacterRow` now shows a muted "Always" label for
base rows and keeps the toggle only for custom characters.

**Remaining for M1:** In-Select hide is confirmed working **without a restart** (live rebuild shipped
in v0.4.0, above). Optional stretch goals still open: reorder/pin; conflict detection.

### M2 — COMPLETE (verified in-game)

`CharacterInfoScreen` (`Code/UI/CharacterInfoScreen.cs`): a read-only drill-in opened by
clicking a character's name in the manager list. Shows starting HP/gold/energy/orb slots,
gender, source mod + version + author, grouped starting deck (`Name ×N`), starting relics,
starting potions, and unlock text. All members read are public on `CharacterModel`; identical
for base and custom. One reused instance, pushed with the Compendium's `AddChild` + `Push`
pattern.

### M3 — COMPLETE (verified in-game)

`RunHistoryPatch` + `RunHistoryFilter` (`Code/Patches/`, `Code/Config/`): the per-row History
button sets a static filter then opens the native `NRunHistory`; a postfix on
`OnSubmenuOpened` rebuilds the private `_runNames` to only this character's runs and re-invokes
`RefreshAndSelectRun(0)`; a postfix on `OnSubmenuClosed` clears the filter. Known minor edge:
a character with lifetime stats but no surviving `.run` files shows a stale entry (the native
screen has no empty state); the History button is disabled unless stats show activity, so this
is rare.

### M4 — COMPLETE (verified in-game)

`CharacterAnalyticsScreen` (`Code/UI/`) + shared `CharacterAnalytics` (`Code/Analytics/`):
an Analytics drill-in per row. Summary from `CharacterStats` (W/L, win rate, max & preferred
ascension, best & current streak, fastest win, playtime, badges) plus aggregates parsed from
`.run` files (runs recorded, wins/deaths/abandoned, highest act & floor, avg & longest run,
fastest clear, per-ascension W/L, act-reached distribution). Every read guarded. No save writes.
NOTE on units: `FastestWinTime`/`Playtime`/`RunTime` are treated as seconds.

### M5 — COMPLETE (verified in-game)

`StatsExporter` (`Code/Analytics/`): an Export button on the analytics screen writes the
character's summary + run aggregate + per-run table to JSON **and** CSV under
`{user_data}/mod_configs/charactermanager_exports/`. Strictly read-only — reads `CharacterStats`
and `.run` files, writes only to the mod's own config dir. A status line shows the destination.

### M6 — UI overhaul (COMPLETE — shipped v0.2.0 + v0.3.0)

The three code-built screens (`CharacterManagerScreen`, `CharacterInfoScreen`,
`CharacterAnalyticsScreen`) were restyled to the game's native theme/fonts, made denser, and then
taken to the paper mockup. Chose the lower-risk path (a): reuse the game's existing `Theme`; no
custom PCK art (`has_pck` stays false).

**v0.2.0 (first pass).** `Code/UI/UiTheme.cs` copies the nearest ancestor's game `Theme` onto our
screens (game font), exposes the `StsColors` palette, compact metrics, and bordered warm-dark
panels, laid out in a centred fixed-width column (`UiTheme.MaxContentWidth`). Manager rows gained
character portraits (`IconTexture`) and a two-line green/red W·L.

**v0.3.0 (mockup pass).**
- **Manager = left list + right detail panel.** Click any row to select (first auto-selected,
  gold-bordered). The detail panel (content-sized card anchored at the top, no full-height void)
  shows the character name, the large `CharacterSelectIcon` portrait (framed, KeepAspectCentered
  over a dark matte; guarded fallback to `IconTexture` → "(no image)"), a green/red `W: x  L: y`
  line, and History / Analytics / Info buttons. Info moved off the name onto a panel button. New
  two-pane placement helpers (`PlaceListColumn*` / `PlaceDetailPanelTop`) in `UiTheme`.
- **Data viz (pure `ColorRect` bars, no PCK):** added `UiTheme.MakeBarTrack` / `MakeBarRow`.
  Info gained a **Deck Composition** section (bars by card type via `CardModel.Type`). Analytics
  gained an outcomes bar, per-ascension W/L bars, act-distribution bars, and a two-column stats
  grid (`AddStatsGrid`).
- **Game-mode correctness (resolved conflicting W/L numbers).** Verified in game source that
  `ProgressSaveManager.UpdateWithRunData` only increments `CharacterStats.TotalWins/Losses` for
  `GameMode.Standard` (Custom + Daily are excluded; `GameModeExtension.AreAchievementsAndEpochsLocked`).
  So the official counter and the run-history `.run` files legitimately disagree. Analytics now
  shows: **Summary (Standard runs)** from `CharacterStats` (matches the manager list), a separate
  **Custom / Daily Runs** section from run history (flagged "not counted by official stats"), a
  **Run Details (all runs)** grid, and **(all runs)** ascension/act bars. `CharacterAnalytics`
  now tracks per-mode tallies (`Standard*` / `Custom*`).
- **Semi-transparent backdrop** on all three screens (`UiTheme.Backdrop` α 0.2) so the game shows
  behind the menus.
- **Native "Manage Characters" Compendium button.** `CompendiumPatch` duplicates the Statistics
  `NCompendiumBottomButton` (inheriting its background texture, HSV shader and hover/press
  animations), gives it its own material copy (so hover doesn't tint the real Statistics button),
  recolours by shifting the HSV `h` uniform (modulate-tint fallback), replaces the single icon with
  a row of character portraits + the game's `stats_questionmark` sprite in the middle slot, sets the
  `MegaLabel` to "Manage Characters", positions it to the left of Character Stats, stitches controller
  focus, and connects its `Released` event. Replaces the earlier plain-text bottom-left button.

**Out of scope / NOT sourceable (unchanged):** per-ability usage %, per-gear equipped-hours and
win-rate contribution, per-encounter attempt/kill tallies, global leaderboards. None are derivable
from the read-only `RunHistory`/`CharacterStats` data. A real compare-characters table *is* feasible
from existing per-character aggregates and remains a stretch goal. (Embedding live `CreateVisuals`
for full-body portraits, once listed here as a stretch goal, shipped in v0.4.0 — see below.)

---

### v0.3.1 — game v0.107.1 compatibility + multi-platform release (COMPLETE)

**Compatibility.** STS2 updated 0.107.0 → 0.107.1 (commit 59260271, released
2026-06-18) — a large refactor: **1481 modified source files** and **116 changed hook
signatures** (the `After*`/`Before*`/`Should*`/`Modify*` gameplay hooks dropped their
`ICombatState` / `IRunState` parameters, moving to `PlayerChoiceContext` + entity args).
Re-decompiled the game (`decompile_game force:true`; the old 0.107.0 source was copied to
`/home/nazar/sts2-modding-mcp/decompiled_v0.107.0_backup` first so `diff_game_versions`
could run) and verified the mod against the fresh source:

- **All 7 Harmony patch targets still exist, same names:** `NRunHistory.OnSubmenuOpened`,
  `NSubmenu.OnSubmenuClosed`, `NGeneralStatsGrid.LoadStats`, `NCompendiumSubmenu._Ready`,
  `NCharacterSelectScreen.InitCharacterButtons`, `NCustomRunScreen.InitCharacterButtons`,
  `ModelDb.AllCharacters` (getter).
- **All reflected members intact:** `NRunHistory._runNames` / `RefreshAndSelectRun`;
  `NGeneralStatsGrid._characterStatContainer`; `NCompendiumBottomButton`
  `_bgPanel`/`_label`/`_icon`/`_locKeyPrefix`/`_hsv`; `NCompendiumSubmenu._statisticsButton`/
  `_stack`; `ModelDb._contentById`; `ProgressState.GetStatsForCharacter`;
  `CharacterStats.TotalWins`/`TotalLosses`; `NCharacterStats.Create(CharacterStats)`;
  `CharacterModel.IconTexture`/`CharacterSelectIcon`/`StartingDeck`/`Id`; `GameMode.Standard`.
- The 116 hook-signature changes **do not affect this mod** — it implements no gameplay
  hooks (menu-screen patches + reflection only), and none of the menu classes it touches
  changed. `check_mod_compatibility` reported **0 issues** and the mod **compiles clean**
  against the new game DLLs (Debug + Release).
- **No code changes required.** Bumped `min_game_version` 0.107.0 → 0.107.1 and mod
  `version` 0.3.0 → 0.3.1 in `mod_manifest.json` (commit `6e8618f`, pushed to `main`).
- Branch note: the experimental `stretch` branch (live visuals / select-screen rebuild /
  game-mode filter, commit `7dc5f8b`) was intentionally **left untouched** — this compat
  pass targets `main` only.

**Distribution (3 channels).** Exact commands/paths live in `CLAUDE.md` →
"Distribution / publishing".

- **GitHub:** release **v0.3.1** on `RazanKai/STS2-CharacterManager` (latest), with the
  packaged `charactermanager-v0.3.1.zip`.
- **Steam Workshop:** uploaded via megacrit's `sts2-mod-uploader` (built for linux-x64,
  staged under git-ignored `workshop-upload/`). Public **item 3747550119** (Steam account
  "Opalization"), BaseLib declared as Workshop dependency **3737335127**. New-item search
  indexing lags the working direct link by up to a day or two — not a fault.
- **Nexus Mods:** automated via committed CI `.github/workflows/nexus-upload.yml`
  (`Nexus-Mods/upload-action`, v3 Upload API, `file_id 7549724`). The `NEXUS_API_KEY`
  repo secret is set; trigger with `gh workflow run nexus-upload.yml -f tag=v0.3.1`
  (also auto-fires on release publish).

---

### v0.4.0 — two stretch features delivered (COMPLETE, verified in-game)

The experimental `stretch` branch was renamed **`beta`** and brought up to date with `main`.
Its three extras were reviewed for feasibility/quality: the analytics **game-mode filter**
(All/Standard/Custom/Daily, in `CharacterAnalytics.GetFiltered`) was sound and kept as-is;
the two headline features were broken on first pass and were reworked before release.

- **Live animated character portraits** (`CharacterManagerScreen.TryAttachLiveVisuals` +
  `FitAndAnimate`). First-pass bugs fixed: (1) **hard crash on modded chars** —
  `CharacterModel.CreateVisuals()` loads the visuals `.tscn` through `AssetCache`, a cold load
  on the menu that other mods' patches hijack (Ryoshu's `RyoshuAssetCachePatch` fataled casting
  the scene to a texture; the game's log ended mid-backtrace = native crash, uncatchable by
  `try/catch`). Fix: resolve the private `CharacterModel.VisualsPath` via reflection and load the
  `PackedScene` with `ResourceLoader.Load` directly, bypassing `AssetCache` and its patches.
  (2) **black frame** — `NCreatureVisuals` is a `Node2D` and won't render inside a Control; host
  it in a `SubViewport` shown via a `TextureRect`, play `idle_loop`, and fit/centre using the
  creature's `Bounds` marker (~70% fill for weapon/horn/flame headroom). Guarded; falls back to
  the static portrait on any failure.
- **In-Select toggle without a restart** (`CharacterSelectPatch`). The first-pass approach
  (flip each button's `Visible`) was inert — disabled chars never get a button, and RitsuLib's
  scroller ignores `Visible`. Real fix: the select screen is cached in
  `NMainMenuSubmenuStack._characterSelectSubmenu`; a postfix on the stack's `_Ready` captures it
  and wires `EnabledStore.OnToggle`, and on toggle we free + null the cached
  `_characterSelectSubmenu` / `_customRunScreen` so the next open rebuilds fresh under the existing
  roster filter. Resolves the M1 restart limitation.

**Distribution.** Released to all 3 channels: GitHub **v0.4.0** (`charactermanager-v0.4.0.zip`,
commit `cf0ddaf` on `main`), Nexus (CI auto-fired on release publish), Steam Workshop (item
`3747550119`, updated). `min_game_version` unchanged (0.107.1); version 0.3.1 → 0.4.0.

---

### M7 — Random-character pool toggle (COMPLETE — v0.5.0, beta; VERIFIED in-game)

Implements `PLAN-random-pool.md`. Decisions: **D1 = runtime `ModelDb.AllCharacters`**
(vanilla-faithful; an all-checked pool reproduces known seeds), **D2 = robust pool panel**.

- **Draw filter (gameplay path — the mod's first).** Harmony transpiler
  (`Code/Patches/RandomPoolPatch.cs`) on
  `StartRunLobby.BeginRunLocally(string, List<ModifierModel>)` swaps the collection
  load feeding `rng.NextItem(...)` for `RandomPoolStore.GetPool()`. The running game
  uses `GetRandomEligibleCharacters()` instead of the decompiled `ModelDb.AllCharacters`;
  the transpiler traces backward from `NextItem` and replaces whatever instruction loads
  the collection (call/callvirt/newobj/newarr), so it adapts to both the decompiled
  and live game. Rng sequencing is identical. Guarded: if `NextItem` isn't found or the
  prior instruction doesn't load a collection, logs and leaves vanilla IL. Re-verify on
  every game update. `affects_gameplay` → `true`.
- **Pool store** (`Code/Config/RandomPoolStore.cs`) — `EnabledStore` clone
  (`charactermanager_randompool.json`, default in-pool). `GetPool()` = `ModelDb.AllCharacters`
  (order preserved) minus unchecked ids, full-roster fallback if empty. Independent of the
  in-select hide toggle.
- **UI** (`Code/UI/RandomPoolPanel.cs` + `Code/Patches/RandomPoolUiPatch.cs`) — themed
  floating card shown via a postfix on `NCharacterSelectScreen.SelectCharacter` when the
  selected button `IsRandom` (singleplayer only). Uses `CharacterHelper.GetAllCharacters()`
  for the character list (dynamic, picks up modded chars like Ryoshu/The Cursed at runtime).
  Portrait + name + In/Out toggle per drawable character, All/None quick buttons. Parented
  to the screen (freed on rebuild).
- **Scope:** singleplayer v1 (host-local pool, not synced); `YummyCookie`'s separate
  in-run pick left vanilla. Build clean, `check_mod_compatibility` 0 issues. **Verified
  in-game:** the random draw correctly respects the configured pool (tested with single
  character checked, drew that character).

---

## Stretch goals (post-v0.3.0)

Ideas surfaced across M1–M6 that are out of current scope but feasible later. All are
optional; none are required for the mod to be complete.

**UI / presentation**
- Custom parchment nine-patch art for the screens (would require shipping a PCK; the
  mod is currently `has_pck:false`).
- ~~Embed live animated character visuals (`CharacterModel.CreateVisuals()`) for full-body
  portraits~~ — **DONE in v0.4.0** (`SubViewport` host + `ResourceLoader` safe load).
- Taller / denser analytics bars; add a **floor-reached distribution** chart alongside
  the act-reached one.
- Alternate question-mark art on the Compendium button (`char_select_random.png`) if the
  flat `stats_questionmark` UI sprite reads too differently next to the portraits.

**Roster management**
- **Reorder / pin** characters in the manager list (and reflect that order in character
  select).
- ~~Live select-screen rebuild so the **In-Select** toggle applies without a full game
  restart~~ — **DONE in v0.4.0** (cache invalidation on `EnabledStore.OnToggle`; see
  the v0.4.0 section and M1).
- **Conflict detection:** surface duplicate `ModelId` registrations (modded characters
  silently overwriting each other in `ModelDb._contentById`).

**Analytics / data**
- A real **Compare Characters** view — sourceable today from the existing per-character
  aggregates.
- Compute **win streaks / fastest win** from run history for Custom/Daily runs (the
  official `CharacterStats` only tracks these for Standard runs).
- A **game-mode filter** toggle on the analytics page (All / Standard / Custom / Daily)
  instead of the current fixed sections.
- Optionally switch the manager list + detail-panel W/L to run-history-derived numbers
  for cross-screen consistency (cost: loading `.run` files per character; the list
  currently uses the cheap in-memory `CharacterStats`).

---

## Where the mod is today

The current mod does three things, all read-side:

- Enumerates installed custom characters from the live model registry
  (`CharacterHelper.GetCustomCharacters()` reads `ModelDb._contentById` via
  reflection, since `ModelDb.AllCharacters` is a hardcoded list of the 5 base
  characters).
- Injects per-character stat sections into the Compendium Stats screen
  (`NGeneralStatsGrid_LoadStats_Patch` Harmony postfix on
  `NGeneralStatsGrid.LoadStats`), respecting a master toggle and per-character
  visibility.
- Persists visibility choices to its own JSON file
  (`CharacterVisibilityStore`, separate from the BaseLib config file).

Those three primitives — registry enumeration, save/progress reads, native UI
injection — are the foundation everything below builds on. Existing files:
`Code/ModEntry.cs`, `Code/CharacterHelper.cs`,
`Code/Patches/NGeneralStatsGridPatch.cs`, `Code/Patches/StatsConsolePatch.cs`,
`Code/Config/CustomStatsConfig.cs`, `Code/Config/CharacterVisibilityStore.cs`.

## Scope decisions

- **No save mutation.** Backup/restore, orphan cleanup, and ModelId migration are
  out. Read-only **export** to a separate file is allowed.
- **English only.** No new localization beyond the strings a feature needs.
- Run history is already character-agnostic on disk and already rendered safely by
  the native viewer (see M3), so run-history work is navigation/aggregation, not
  storage.

---

## Milestones

### M1 — Character Manager submenu (anchor)

A dedicated submenu listing **all** characters (base + custom). All rows get the
M2 info card; only custom rows get management controls (visibility, reorder,
enable/disable). "Custom-only" applies in exactly one place — the Compendium stats
injection patch — not this list.

**Opening a modded screen — the key constraint (verified).**
`NMainMenuSubmenuStack.PushSubmenuType<T>()` routes through `GetSubmenuType(Type)`,
which is a hardcoded `if/else` chain over built-in screen types and ends with
`throw new ArgumentException($"No such submenu {type}")`. **A modded screen type is
not in that chain, so `PushSubmenuType` cannot be used for it.** Instead use the
public `NSubmenuStack.Push(NSubmenu screen)`:

```csharp
var stack = NGame.Instance.MainMenu.SubmenuStack;   // NMainMenuSubmenuStack
stack.AddChildSafely(managerInstance);              // Push does NOT add the child
stack.Push(managerInstance);                         // sets stack, shows, calls OnSubmenuOpened
```

`Push` (verified): peeks/hides the current top, calls `screen.SetStack(this)`,
pushes, calls `screen.OnSubmenuOpened()`, sets `Visible = true`, enables the
backstop. It does **not** instantiate or add the node — you must `AddChildSafely`
it first, and it must already have run `_Ready`.

**Building the `NSubmenu` subclass (verified contract).**
- `NSubmenu` is `abstract : Control`, namespace
  `MegaCrit.Sts2.Core.Nodes.Screens.MainMenu`.
- Override `_Ready()` and do **not** call `base._Ready()` — the base throws unless
  `GetType() == typeof(NSubmenu)`. (Pattern confirmed in `NStatsScreen._Ready`,
  which calls `ConnectSignals()` then builds its UI.)
- Base `ConnectSignals()` does `GetNode<NBackButton>("BackButton")`. Either include
  a `BackButton` (`NBackButton`) child node, or **override `ConnectSignals()`** to
  wire your own back control to `_stack.Pop()` and skip the lookup.
- Implement `protected override Control? InitialFocusedControl { get; }`.
- `OnSubmenuOpened()` is your build/refresh entry point (called by `Push`).
- `_stack` (protected `NSubmenuStack`) is set by `Push` before `OnSubmenuOpened`.

**RISK (the real unknown):** registering a mod-defined C# Godot node subclass so
Godot recognizes it as a script type requires the `[ScriptPath]` / Godot bridge
plumbing the base screens have (`GetGodotMethodList`, `InvokeGodotClassMethod`,
etc.). Character mods already register custom node types, so it is possible, but
this is the highest-risk part of the milestone. **Lower-risk fallback** if node
registration proves painful: render the management controls inside the existing
BaseLib `SimpleModConfig` UI (already working in this mod via
`CustomStatsConfig`), which sidesteps custom-submenu registration entirely. Decide
this first; it gates the rest of M1's UI work.

**Entry point.** Add a button into the Compendium. `NCompendiumSubmenu` already
calls `_stack.PushSubmenuType<NStatsScreen>()` and `<NRunHistory>()` from button
handlers (verified, ~lines 219/224). Postfix its setup to add a "Manage
Characters" button whose handler does the `AddChildSafely` + `Push` above. The
existing `StatsConsolePatch` (console `stats` command) is a working reference for
reaching `NGame.Instance.MainMenu.SubmenuStack`.

**Enumerate all characters.** Add `CharacterHelper.GetAllCharacters()` mirroring
`GetCustomCharacters()` but without the base-id filter (iterate
`ModelDb._contentById` values, keep `CharacterModel` where `IsPlayable`). Use the
existing `IsBaseCharacter(id)` to decide whether to render management controls.

**Per-row controls (custom rows only).**
- *Visibility* — reuse `CharacterVisibilityStore` (already drives the Compendium
  postfix).
- *Reorder/pin* — add an ordered list (e.g. `List<string>` of `Id` strings) to a
  JSON store like `CharacterVisibilityStore`; have the Compendium postfix and the
  manager list honor it.
- *Enable/disable in character select* — **postfix
  `NCharacterSelectScreen.InitCharacterButtons()`** (private, verified). It builds
  one `NCharacterSelectButton` per `ModelDb.AllCharacters`, names each
  `"{Id.Entry}_button"`, and adds them to `_charButtonContainer`. In the postfix,
  reach `_charButtonContainer` (Harmony `Traverse.Create(__instance).Field(
  "_charButtonContainer")`), find children whose name matches a disabled id, and
  set `Visible = false`. NOTE: a custom character only appears in character select
  at all if its own mod patches `get_AllCharacters` to append itself — that's the
  character mod's responsibility, not ours; we only hide what's already there.

**Conflict / duplicate detection (downgraded to a stretch goal — corrected).**
ModelDb registration is an indexer assignment `_contentById[id] = value`
(ModelDb.cs ~lines 264/274), so **two mods registering the same `ModelId` silently
overwrite — the dict keeps only the survivor.** A post-hoc scan of `_contentById`
therefore *cannot* detect id collisions (the earlier model is already gone).
`ModManager` rejects duplicate *mod ids* (`MOD_ERROR.DUPLICATE_ID`) and Steam-vs-
local dupes, but not duplicate `ModelId`s across different mods. Realistic
detection options: (a) Harmony-patch the ModelDb registration write to record
overwrites during load, or (b) reflect over each loaded mod assembly's
`CharacterModel` types and flag colliding `Id.Entry` values. Both are real work;
treat conflict detection as optional and after the core.

Risk: **medium-high** (custom-submenu node registration). Fallback exists.

### M2 — Character info card (base + custom)

Per-row drill-in inside M1, shown for every character. Pure read + display.

**Reachable `CharacterModel` members (all public, verified):** `Title`,
`NameColor`, `Gender`, `StartingHp`, `StartingGold`, `MaxEnergy`,
`BaseOrbSlotCount`, `CardPool`, `RelicPool`, `PotionPool`,
`StartingDeck` (`IEnumerable<CardModel>`), `StartingRelics`
(`IReadOnlyList<RelicModel>`), `StartingPotions`, `IconTexture` (`Texture2D`),
`CharacterSelectIcon`. These are identical for base and custom characters, so the
card needs no base-vs-custom branch.

**Source mod + version (verified approach).** There is **no** direct
`ModelId → Mod` map; models register globally. Map via assembly identity:
`character.GetType().Assembly`, then find the `Mod` whose `assembly` matches in
`ModManager.GetLoadedMods()` (each `Mod` exposes `assembly` and `manifest` with
`id`, `name`, `version`, `author`). The mod's PCK/loc root folder equals
`manifest.id` (confirmed by `GetModdedLocTables`:
`res://{manifest.id}/localization/...`). Base characters' assembly is the game
assembly (no matching `Mod`) — show "Base game" for those.

**Unlock chain.** `UnlocksAfterRunAs` is `protected abstract`. Simplest: call the
public `CharacterModel.GetUnlockText()` (returns a `LocString`). If you need the
prerequisite character object itself, read the protected property by reflection
(`AccessTools.Property`); prefer `GetUnlockText()` unless that's insufficient.

Risk: **low.**

### M3 — Run-history filtering by character

The native viewer is one flat global list; add per-character filtering.

**Storage & data (verified).** Each run is a JSON file at
`{profile}/saves/history/{StartTime}.run`
(`RunHistorySaveManager`, gated by a global `SaveRunHistory` flag). Custom runs are
saved like base runs — `RunHistoryPlayer.Character` is a plain `ModelId`.

**SaveManager read API (verified):**
`SaveManager.Instance.GetAllRunHistoryNames()` → `List<string>` (file names);
`SaveManager.Instance.LoadRunHistory(name)` → `ReadSaveResult<RunHistory>` (check
`.Success`, read `.SaveData`); `SaveManager.Instance.GetRunHistoryCount()` → int.

**`RunHistory` fields (verified):** `Win`, `WasAbandoned`, `Seed`, `StartTime`
(unix seconds), `RunTime` (float seconds), `Ascension`, `Acts` (`List<ModelId>`),
`Modifiers`, `MapPointHistory` (`List<List<MapPointHistoryEntry>>` — floor count =
sum of inner counts), `Players` (`List<RunHistoryPlayer>`; each has `Character`,
`Deck`, `Relics`, `Potions`, `Badges`).

**Native viewer is already custom-safe (verified).** `NRunHistory` loads all run
files reverse-chronologically and renders each player via
`SaveUtil.CharacterOrDeprecated(player.Character)` — it shows custom characters
when their mod is installed and falls back to a placeholder otherwise. It never
filters by character.

**Filtering implementation.** `NRunHistory.OnSubmenuOpened()` (verified) clears and
fills the private `List<string> _runNames` from `GetAllRunHistoryNames()`,
reverses it, then `RefreshAndSelectRun(0)`. To filter by character:
1. Hold the target `ModelId` in a static on your patch class.
2. Postfix `NRunHistory.OnSubmenuOpened`: if the filter is set, reflect the private
   `_runNames` (Harmony `Traverse`), rebuild it keeping only files whose loaded
   `RunHistory.Players` contains the target character, then invoke the private
   `RefreshAndSelectRun(0)` (also via `Traverse`). Cost: loads every file once
   (O(n) disk reads) — acceptable for a user-triggered action.
3. `NRunHistory` **is** a built-in, so open it with
   `stack.PushSubmenuType<NRunHistory>()` — but set the static filter **before**
   that call, because `PushSubmenuType` → `Push` → `OnSubmenuOpened` runs
   synchronously inside it. Clear the filter on `OnSubmenuClosed` (postfix) so the
   global Compendium → Run History entry stays unfiltered.

Risk: **medium** (reflection on private `_runNames` / `RefreshAndSelectRun`;
load-all-to-filter).

### M4 — Read-only analytics per character

Aggregates computed by reading the existing `.run` files (M3 API) — no new
storage, no save writes.

- Per-ascension win/loss, act-reached distribution, fastest clear, average run
  length, win/loss over time — all derivable from `RunHistory` fields above.
- Also surface the `CharacterStats` fields the mod currently omits. The mod already
  reads `progressSave.GetStatsForCharacter(id)`; `CharacterStats` (verified) has
  `MaxAscension`, `PreferredAscension`, `TotalWins`, `TotalLosses`,
  `FastestWinTime` (long, `-1` = none), `BestWinStreak`, `CurrentWinStreak`,
  `Playtime` (long), `Badges`.
- Optional comparison view across characters.

Risk: **low-medium** (parsing/aggregation cost; presentation).

### M5 — Read-only stats export

Write a character's aggregate stats and run summaries to a separate JSON/CSV file
(e.g. under the mod's own config dir, like `CharacterVisibilityStore`). Reads only;
never touches game saves.

Risk: **low.**

---

## Suggested sequence

1. **M1** — decide submenu-vs-BaseLib-config UI first (it gates everything), then
   build the hub, enumeration, and visibility/reorder. Enable/disable and conflict
   detection can follow.
2. **M2** — info card; high value, low risk, immediately useful for base
   characters that have no info screen.
3. **M3** — run-history filtering.
4. **M4** then **M5** — analytics, then export.

## Verified API reference (quick lookup for implementation)

Submenu / menu
- `NGame.Instance.MainMenu.SubmenuStack` → `NMainMenuSubmenuStack : NSubmenuStack`.
- `NSubmenuStack.Push(NSubmenu)` — public; does not AddChild. `Pop()`, `Peek()`.
- `PushSubmenuType<T>()` / `GetSubmenuType(Type)` — built-in types only; throws
  `ArgumentException` for unknown (modded) types.
- `NSubmenu` (abstract): override `_Ready` (don't call base), `ConnectSignals`
  (base needs a `BackButton`/`NBackButton` child → `_stack.Pop()`),
  `InitialFocusedControl`, `OnSubmenuOpened`, `OnSubmenuClosed`.
- `NCompendiumSubmenu` — add the Manager entry button here (mirrors its
  `PushSubmenuType<NStatsScreen>()` handlers).

Characters
- `ModelDb._contentById` (private `IDictionary<ModelId, AbstractModel>`) — full
  registry incl. modded; registration is `_contentById[id] = value` (silent
  overwrite on dup id).
- `ModelDb.AllCharacters` — hardcoded 5 base unless a char mod patches it.
- `CharacterModel` members: see M2.
- `NCharacterSelectScreen.InitCharacterButtons()` (private) — builds buttons named
  `"{Id.Entry}_button"` into `_charButtonContainer` (private field).
- `character.GetType().Assembly` ↔ `ModManager.GetLoadedMods()` → `Mod.assembly` /
  `Mod.manifest` (`id`, `name`, `version`, `author`).

Saves / stats / history
- `SaveManager.Instance.Progress` (`ProgressState`); `.CharacterStats`
  (Dictionary); existing mod uses `GetStatsForCharacter(id)`.
- `CharacterStats` fields: see M4.
- `SaveManager.Instance.GetAllRunHistoryNames()` / `LoadRunHistory(name)` /
  `GetRunHistoryCount()`.
- `RunHistory` / `RunHistoryPlayer` fields: see M3.
- `NRunHistory.OnSubmenuOpened` fills private `_runNames`, calls private
  `RefreshAndSelectRun(int)`. `CanBeShown()` static gates on count > 0.

## Open technical questions

- Whether to ship the manager as a registered modded `NSubmenu` or fall back to the
  BaseLib config UI (decide before M1 UI work).
- Exact `[ScriptPath]`/Godot-bridge registration steps for a mod-defined `NSubmenu`
  subclass (look at how installed character mods register custom node types).
- Whether reflecting `_runNames` + `RefreshAndSelectRun` is stable across game
  updates, or whether to instead build a separate filtered run-list screen.
