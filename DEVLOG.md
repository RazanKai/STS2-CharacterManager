# Devlog — Character Management Mod

This document tracks the development of the mod from a single-purpose stats injector into a roster/character management suite. Every claim below was cross-checked against the decompiled game source (game version per `mod_manifest.json` `min_game_version`). No feature here mutates the player's save files.

This devlog serves as both a historical record and a technical reference. For the current project guide (tooling, conventions, workflows), see `CLAUDE.md`.

## Project Overview

The Character Management Mod extends the existing **CustomCharacterStats** mod into a full custom-character management suite with:
- Roster control (list all characters, visibility toggle, in-select enable/disable)
- Per-character info cards (drill-in from manager list)
- Run-history filtering by character
- Per-character analytics (W/L, streaks, run aggregates)
- Read-only stats export to JSON + CSV

**Technical Architecture:**
- Built on BaseLib for config and character pools
- Uses Harmony patches for menu injection and gameplay modifications
- Leverages reflection for private game state access (`ModelDb._contentById`, `NRunHistory._runNames`)
- Implements custom `NSubmenu` subclasses for modded screens
- Uses Godot UI construction for all code-built screens
- Follows STS2 modding conventions for compatibility and stability

**Core Components:**
- `Code/ModEntry.cs` — ModInitializer entry point
- `Code/CharacterHelper.cs` — Character enumeration and deduplication
- `Code/Patches/` — Harmony patches for game hooks
- `Code/UI/` — Custom Godot screens (manager, info, analytics, random pool)
- `Code/Config/` — Stores for visibility, random pool, and BaseLib config
- `Code/Analytics/` — Character analytics and export logic

## Current Status (v0.5.0 — released)

**M1 — Character Manager submenu (anchor) — COMPLETE**
- Character Manager screen opens from Compendium
- Renders all characters (base + custom) with deduplication
- Per-custom-character visibility toggle drives Compendium stats display
- **In-Select toggle without restart** (v0.4.0 fix via cache invalidation)

**M2 — Character info card — COMPLETE**
- Read-only drill-in for every character
- Shows HP/gold/energy, gender, source mod/version, starting deck/relics/potions, unlock text

**M3 — Run-history filtering — COMPLETE**
- Per-row History button filters runs by character
- Rebuilds `_runNames` and calls `RefreshAndSelectRun(0)`

**M4 — Character analytics — COMPLETE**
- Summary from `CharacterStats` + run aggregates
- Per-ascension W/L, act distribution, fastest clear, playtime, badges
- Game-mode correctness (Standard vs Custom/Daily runs)

**M5 — Stats export — COMPLETE**
- Export button writes JSON + CSV to mod config dir
- Read-only, never touches game saves

**M6 — UI overhaul — COMPLETE**
- Native theme restyling (v0.2.0) + left list + right detail panel (v0.3.0)
- Semi-transparent backdrop on all screens
- Native "Manage Characters" Compendium button
**M7 — Random-character pool toggle — COMPLETE (released v0.5.0)**

- Runtime `ModelDb.AllCharacters` pool source
- Robust pool panel with per-character toggles
- Harmony transpiler on `BeginRunLocally` for gameplay filtering
- **Multiplayer-synced:** each player advertises their own pool; every peer re-derives every player's draw locally from the synced pools
- **In-select hide is a hard override:** a character hidden from the select grid is never drawn at random and is omitted from the pool panel (v0.5.0 fix)

**Technical Implementation:**

**Pool store (`Code/Config/RandomPoolStore.cs`):**
- `EnabledStore` clone (`charactermanager_randompool.json`, `Dictionary<string,bool>` keyed by `ModelId`, default `true`, `OnToggle` event)
- `BuildLocalPool()` builds from `ModelDb.AllCharacters` (runtime order preserved), keeps a character only if it is BOTH enabled in-select (`EnabledStore`) AND in-pool — so the in-select hide is a hard override on the draw
- Empty-pool fallback draws from the **in-select-visible** roster, never the full roster, so a hidden character can never leak into the draw even when every visible character is unchecked (v0.5.0 fix)
- Multiplayer: `BuildLocalPool()` is both what we draw our own slot from and what we broadcast; per-player resolution (`BeginResolution`/`GetPool`/`PoolForPlayer`) maps each draw to the right player's synced pool

**Gameplay filter (mod's first gameplay patch):**
- Harmony transpiler (`Code/Patches/RandomPoolPatch.cs`) on `StartRunLobby.BeginRunLocally(string, List<ModifierModel>)`
- Replaces single `call ModelDb.get_AllCharacters` instruction with `RandomPoolStore.GetPool()` call
- Keeps rng-sequencing identical (only changes list `NextItem` indexes)
- Guarded: if target IL instruction not found, logs and no-ops (leaves vanilla behavior)
- Re-verify on every game update (most update-fragile patch type)

**UI (`Code/UI/RandomPoolPanel.cs` + `Code/Patches/RandomPoolUiPatch.cs`):**
- Themed floating card shown via postfix on `NCharacterSelectScreen.SelectCharacter` when selected button `IsRandom`
- Lists `CharacterHelper.GetAllCharacters()` minus in-select-hidden characters (dynamic; picks up modded chars at runtime)
- Portrait + name + In/Out toggle per character, All/None quick buttons
- Parented to screen (freed on rebuild); on picking Random it broadcasts the local pool to peers

**Multiplayer (`Code/Patches/RandomPoolLobbyPatch.cs` + `Code/Multiplayer/RandomPoolMessage.cs`):**
- Each player's pool is networked; peers store remote pools keyed by net id and re-derive every player's draw locally so the result is identical on all machines

**Design Decisions:**
- **D1:** Random pool source — full roster incl. modded (chosen), filtered by in-select + pool membership
- **D2:** Random pool UI — robust pool panel (chosen) vs. literal per-button checkboxes

**Edge Cases:**
- Determinism: changing the pool changes which character a seed yields (expected); multiplayer stays consistent because each player's pool is synced
- All-unchecked + all-customs-hidden falls back to the in-select-visible (base) roster, never a hidden custom (v0.5.0 fix)
- `YummyCookie` relic's separate in-run random pick left vanilla

**Files:**
- `Code/Config/RandomPoolStore.cs` (new)
- `Code/Patches/RandomPoolPatch.cs` (new — transpiler + guard)
- `Code/Patches/RandomPoolLobbyPatch.cs` (new — multiplayer per-player resolution)
- `Code/Patches/RandomPoolUiPatch.cs` (new — select-screen panel)
- `Code/Multiplayer/RandomPoolMessage.cs` (new — pool sync message)
- `Code/UI/RandomPoolPanel.cs` (new)

**Verification:** Build clean, `check_mod_compatibility` 0 issues. **Verified in-game:** random draw respects the configured pool; in-select-hidden characters are absent from the strip, the pool panel, and the random draw (including the all-unchecked fallback)

**M8 — Analytics foundations + quick wins — IN PROGRESS (beta, built + installed, manual test pending)**

First milestone of the analytics expansion (`ANALYTICS_PLAN.md`). Ships the cross-cutting infrastructure the richer per-card / per-encounter milestones (M9–M12) depend on, plus three visible wins on the existing per-character analytics page. Still read-only over saves.

**Infrastructure (plan §4):**
- **Async/cached deep-parse scaffold (4a) — `Code/Analytics/AnalyticsCache.cs`:** process-wide cache of per-character `CharacterAnalytics`, so re-opening a character or toggling filters across opens is instant instead of re-reading every `.run` file. Invalidation uses a cheap *generation token* (count of run-history files) rather than a Harmony save hook — finishing a run (count up) or pruning (count down) forces a recompute. Snapshots whose file list couldn't be read (`CharacterAnalytics.LoadFailed`, save system not ready) are never cached, so the screen can't get pinned to zeros at startup (plan §4a poison guard).
  - **Threading decision:** the plan suggests `Task.Run`, but `SaveManager` is a Godot singleton with unverified off-thread safety. M8 reads only cheap top-level fields, so aggregation runs on the main thread and the screen stays responsive by painting `"Crunching run history…"` and deferring the parse one frame (`await ToSignal(GetTree(), ProcessFrame)`). Revisit true off-thread parsing (read raw bytes off-thread, deserialize there) in M9 if the floor-by-floor walks make it heavy.
- **Name resolver (4b) — `Code/Analytics/NameResolver.cs`:** `ModelId` → localized display name via `LocString.Exists` table-probing (`cards`/`relics`/`potions`/`encounters`/`monsters`/`events`, `.title`/`.name`), memoised, with a SCREAMING_SNAKE→Title-Case fallback so unknown/modded ids degrade gracefully and never crash. Infra for the M9+ ranked lists (not yet surfaced in the UI).
- **Ranked-list row widget (4c) — `UiTheme.MakeRankedRow`:** the shared "name — proportional bar — value" row behind future card/relic/encounter/death lists, built on the existing bar primitives. Sort + show-more container deferred to M9, where it can be exercised against real lists.

**Quick wins (plan §5 M8):**
- **Floor-reached distribution bars** — new `CharacterAnalytics.FloorReached` dict, rendered alongside the existing act-reached bars.
- **Win-rate moving windows** — last 10 / 50 / 100 / all decisive runs (abandons excluded), via `CharacterAnalytics.WinRateWindow(n)`; shown as its own bar section. Computed over the mode+ascension scope but independent of the recent-N cap.
- **Expanded filter bar** — alongside All/Standard/Custom/Daily, two cycle buttons add a **minimum-ascension** floor (Any/1/5/10/15/20+) and a **recent-N window** (All/10/50/100). Driven by a new composite `RunFilter` (mode + min-ascension + recent-N); `GetFiltered(RunFilter)` re-derives every distribution and carries the surviving runs through so windows/floor bars/exports all work off the filtered aggregate.

**Build fix:** freshly-cloned `references/` stats-mod clones (gitignored, studied for the plan) were being swept into compilation by the SDK's `**/*.cs` glob and collided. Added `<Compile Remove="references/**/*.cs" />` (+ EmbeddedResource/None) to `CharacterManager.csproj`.

**Files:** `Code/Analytics/AnalyticsCache.cs` (new), `Code/Analytics/NameResolver.cs` (new), `Code/Analytics/CharacterAnalytics.cs` (RunFilter, FloorReached, LoadFailed, WinRateWindow, richer GetFiltered), `Code/UI/UiTheme.cs` (MakeRankedRow), `Code/UI/CharacterAnalyticsScreen.cs` (async cached populate, win-rate-windows + floor-bars sections, expanded filter bar), `CharacterManager.csproj` (exclude references/).

**Post-test fixes (verified in-game on Ironclad, 32 runs):**
- **Acts-reached correctness:** "Act Reached Distribution" / "Highest act reached" used `RunHistory.Acts.Count`, which is the run's *planned* act list (confirmed via `RunHistoryUtilities.CreateRunHistoryEntry`: `Acts = run.Acts.Select(...)`) — always ~3-4 regardless of progress, so a floor-1 death wrongly read as "reached act 3". Switched to `MapPointHistory.Count` (outer list = acts actually entered; inner = floors), the same source as floors-reached. Also corrects the JSON/CSV export (shared `Compute`).
- **M4 / M8 consistency:** the Custom/Daily section's win rate counted abandons as losses (`wins/total`); aligned it to the decisive rate (`wins/(wins+deaths)`) used by the Win Rate windows, with an "excludes abandons" note.

**Verification:** `build_mod` clean (0 errors; 3 pre-existing nullable warnings), `validate_mod` valid. **Verified in-game:** win-rate windows, Asc/Recent filters, and floor-reached bars render correctly; act distribution now reflects real progression.

## Release History

### v0.3.1 (2026-06-18) — Game v0.107.1 compatibility

**Compatibility pass:** STS2 updated 0.107.0 → 0.107.1 (1481 modified source files, 116 hook-signature changes)

- All 7 Harmony patch targets still exist, same names
- All reflected members intact
- No gameplay hooks affected (menu-screen patches only)
- `check_mod_compatibility` = 0 issues
- Built clean against new DLLs

**Technical details:**
- Hook signature changes: `After*`/`Before*`/`Should*`/`Modify*` gameplay hooks dropped `ICombatState` / `IRunState` parameters, moving to `PlayerChoiceContext` + entity args
- Verified patch targets: `NRunHistory.OnSubmenuOpened`, `NSubmenu.OnSubmenuClosed`, `NGeneralStatsGrid.LoadStats`, `NCompendiumSubmenu._Ready`, `NCharacterSelectScreen.InitCharacterButtons`, `NCustomRunScreen.InitCharacterButtons`, `ModelDb.AllCharacters` (getter)
- Reflected members: `NRunHistory._runNames`, `RefreshAndSelectRun`, `NGeneralStatsGrid._characterStatContainer`, `NCompendiumBottomButton` properties, `ModelDb._contentById`, `ProgressState.GetStatsForCharacter`, `CharacterStats.TotalWins/Losses`, `CharacterModel.IconTexture/CharacterSelectIcon`, `GameMode.Standard`

**Distribution:**
- GitHub release with `charactermanager-v0.3.1.zip`
- Steam Workshop item `3747550119` (BaseLib dependency: `3737335127`)
- Nexus Mods via CI auto-fire (file_id: `7549724`)

### v0.4.0 (2026-06-??) — Two stretch features delivered

**Live animated character portraits**
- Technical implementation: `CharacterManagerScreen.TryAttachLiveVisuals` + `FitAndAnimate`
- Bug 1: Hard crash on modded chars — `CharacterModel.CreateVisuals()` loads `.tscn` through `AssetCache`, hijacked by other mods' patches (Ryoshu's `RyoshuAssetCachePatch` fataled casting scene to texture)
- Fix: Resolve private `CharacterModel.VisualsPath` via reflection, load `PackedScene` with `ResourceLoader.Load` directly, bypassing `AssetCache` and its patches
- Bug 2: Black frame — `NCreatureVisuals` is a `Node2D` and won't render inside a Control
- Fix: Host in a `SubViewport` (texture shown via `TextureRect`), play `idle_loop`, fit/centre using creature's `Bounds` marker (~70% fill for weapon/horn/flame headroom)

**In-Select toggle without restart**
- Root cause: `NMainMenuSubmenuStack._characterSelectSubmenu` cache
- Technical fix: Postfix on stack's `_Ready` captures stack and wires `EnabledStore.OnToggle`
- On toggle: free + null cached `_characterSelectSubmenu` / `_customRunScreen` so next open rebuilds fresh under existing `AllCharacters` filter
- Guards against freeing a currently-visible screen

**Distribution:** Released to all 3 channels (GitHub v0.4.0, Nexus, Steam)

### v0.5.0 (2026-06-25) — Random-character pool + in-select hide fixes

First public release of the M7 random-character pool, with multiplayer support and a set of fixes that make the in-select hide a hard override everywhere.

**Gameplay path (mod's first):**
- Harmony transpiler (`Code/Patches/RandomPoolPatch.cs`) on `StartRunLobby.BeginRunLocally(string, List<ModifierModel>)`
- Swaps the collection load feeding `rng.NextItem(...)` for `RandomPoolStore.GetPool()`
- Technical implementation: The running game uses `GetRandomEligibleCharacters()` instead of decompiled `ModelDb.AllCharacters`; transpiler traces backward from `NextItem` and replaces whatever instruction loads the collection (call/callvirt/newobj/newarr), so it adapts to both decompiled and live game
- Guarded: if `NextItem` isn't found or the prior instruction doesn't load a collection, logs and leaves vanilla IL
- Re-verify on every game update
- `affects_gameplay` → `true`

**Pool store (`Code/Config/RandomPoolStore.cs`):**
- `EnabledStore` clone (`charactermanager_randompool.json`, default in-pool)
- `BuildLocalPool()` = `ModelDb.AllCharacters` (order preserved) keeping only characters that are enabled in-select AND in-pool; empty-pool fallback is the in-select-visible roster (never the full roster)
- In-select hide is a hard override (a hidden character is never drawable), not independent of the pool

**Multiplayer (`Code/Patches/RandomPoolLobbyPatch.cs` + `Code/Multiplayer/RandomPoolMessage.cs`):**
- Each player broadcasts their own pool; peers store remote pools by net id and re-derive every player's draw locally, so the result matches on all machines

**UI (`Code/UI/RandomPoolPanel.cs` + `Code/Patches/RandomPoolUiPatch.cs`):**
- Themed floating card shown via a postfix on `NCharacterSelectScreen.SelectCharacter` when the selected button `IsRandom`
- Lists `CharacterHelper.GetAllCharacters()` minus in-select-hidden characters (dynamic; picks up modded chars like Ryoshu/The Cursed at runtime)
- Portrait + name + In/Out toggle per character, All/None quick buttons; broadcasts the local pool on Random pick
- Parented to the screen (freed on rebuild)

**Fixes (this release):**
- **In-select hide now works for library-injected characters.** Characters added by character libraries (BaseLib via RitsuLib — e.g. The Cursed, LittleWizard) inject their own `NCharacterSelectButton`s and never appear in `ModelDb.AllCharacters` at build time, so the getter filter missed them. Added a post-build pass on `NCharacterSelectScreen`/`NCustomRunScreen` `InitCharacterButtons` that frees disabled-custom buttons (see Technical Lessons Learned).
- **In-select hide is a hard override on Random** (pool draw + pool panel) — see Pool store above.
- **Empty-pool fallback** no longer re-introduces hidden characters.

**Scope:** `YummyCookie`'s separate in-run pick left vanilla

**Verification:** Build clean, `check_mod_compatibility` 0 issues. **Verified in-game:** in-select-hidden characters are absent from the select strip, the Random Pool panel, and the random draw (including the all-unchecked fallback).

**Distribution:** Released to all 3 channels — GitHub `v0.5.0` (merged `beta` → `main`), Nexus via CI auto-fire, Steam Workshop item `3747550119` (updated content, square thumbnail, tags `QoL` / `Tools & APIs` / `Utility` / `English`)

## M1 — Character Manager submenu (anchor)

**Core Implementation:**
- **CharacterManagerScreen** (`Code/UI/CharacterManagerScreen.cs`) — custom `NSubmenu` subclass for the main management interface
- **CompendiumPatch** (`Code/Patches/CompendiumPatch.cs`) — Harmony postfix on `NCompendiumSubmenu._Ready` to add "Manage Characters" button
- **CharacterHelper.GetAllCharacters()** — enumerates all characters (base + custom) for the manager list

**Technical Details:**
- **Submenu Registration:** Custom `NSubmenu` subclass requires manual registration via `[ScriptPath]` attribute in Godot project
- **Push Pattern:** Uses `NSubmenuStack.Push(managerInstance)` after `AddChildSafely(managerInstance)`
- **Character Enumeration:** `CharacterHelper.GetAllCharacters()` iterates `ModelDb._contentById` values, keeps `CharacterModel` where `IsPlayable`, uses `IsBaseCharacter(id)` to decide management controls
- **Per-Row Controls:** Visibility toggle (`CharacterVisibilityStore`), reorder/pin via JSON store, In-Select hide via `CharacterSelectPatch`

**Files:**
- `Code/UI/CharacterManagerScreen.cs` (new)
- `Code/Patches/CompendiumPatch.cs` (new)
- `Code/CharacterHelper.cs` (new)

**Risks:** Custom-submenu node registration (highest risk), fallback to BaseLib config UI

## M2 — Character info card (base + custom)

**Core Implementation:**
- **CharacterInfoScreen** (`Code/UI/CharacterInfoScreen.cs`) — read-only drill-in opened by clicking character name
- **CharacterModel** data access — all public members available for base and custom characters

**Technical Details:**
- **Source Mod Mapping:** `character.GetType().Assembly` ↔ `ModManager.GetLoadedMods()` → `Mod.assembly` / `Mod.manifest`
- **Unlock Chain:** Calls `CharacterModel.GetUnlockText()` for unlock text, uses reflection for prerequisite character if needed
- **Data Display:** Shows HP/gold/energy, gender, source mod/version, grouped starting deck, relics, potions, unlock text
- **Reusability:** One instance, pushed with Compendium's `AddChild` + `Push` pattern

**Files:**
- `Code/UI/CharacterInfoScreen.cs` (new)

**Risks:** Low (all CharacterModel members public)

## M3 — Run-history filtering by character

**Core Implementation:**
- **RunHistoryPatch** (`Code/Patches/RunHistoryPatch.cs`) — Harmony patches on `NRunHistory.OnSubmenuOpened` / `OnSubmenuClosed`
- **RunHistoryFilter** (`Code/Config/RunHistoryFilter.cs`) — static filter store

**Technical Details:**
- **Storage:** Each run is a JSON file at `{profile}/saves/history/{StartTime}.run`
- **API:** `SaveManager.Instance.GetAllRunHistoryNames()`, `LoadRunHistory(name)`, `GetRunHistoryCount()`
- **RunHistory Fields:** `Win`, `WasAbandoned`, `Seed`, `StartTime`, `RunTime`, `Ascension`, `Acts`, `Modifiers`, `MapPointHistory`, `Players`
- **Filtering:** Postfix on `OnSubmenuOpened` rebuilds private `_runNames` to only this character's runs, calls `RefreshAndSelectRun(0)`
- **Clear Filter:** Postfix on `OnSubmenuClosed` clears static filter

**Files:**
- `Code/Patches/RunHistoryPatch.cs` (new)
- `Code/Config/RunHistoryFilter.cs` (new)

**Risks:** Medium (reflection on private `_runNames` / `RefreshAndSelectRun`, O(n) disk reads)

## M4 — Read-only analytics per character

**Core Implementation:**
- **CharacterAnalyticsScreen** (`Code/UI/CharacterAnalyticsScreen.cs`) — analytics drill-in per row
- **CharacterAnalytics** (`Code/Analytics/CharacterAnalytics.cs`) — shared analytics logic

**Technical Details:**
- **Data Sources:** `CharacterStats` (W/L, win rate, ascension, streaks, fastest win, playtime, badges) + run aggregates from `.run` files
- **Run Aggregates:** runs recorded, wins/deaths/abandoned, highest act & floor, avg & longest run, fastest clear, per-ascension W/L, act-reached distribution
- **Game-mode Correctness:** `ProgressSaveManager.UpdateWithRunData` only increments `CharacterStats` for `GameMode.Standard` (Custom/Daily excluded)
- **Analytics Sections:** Summary (Standard runs), Custom/Daily Runs, Run Details (all runs), (all runs) ascension/act bars

**Files:**
- `Code/UI/CharacterAnalyticsScreen.cs` (new)
- `Code/Analytics/CharacterAnalytics.cs` (new)

**Risks:** Low-medium (parsing/aggregation cost, presentation)

## M5 — Read-only stats export

**Core Implementation:**
- **StatsExporter** (`Code/Analytics/StatsExporter.cs`) — Export button on analytics screen

**Technical Details:**
- **Output Format:** JSON + CSV under `{user_data}/mod_configs/charactermanager_exports/`
- **Data:** Character's summary + run aggregate + per-run table
- **Read-Only:** Never touches game saves, reads only `CharacterStats` and `.run` files
- **Status Line:** Shows destination of exported files

**Files:**
- `Code/Analytics/StatsExporter.cs` (new)

**Risks:** Low (simple file writing)

## M6 — UI overhaul (COMPLETE — shipped v0.2.0 + v0.3.0)

**Core Implementation:**
- **UiTheme** (`Code/UI/UiTheme.cs`) — native theme restyling
- **CharacterManagerScreen** (updated) — left list + right detail panel
- **CharacterInfoScreen** (updated) + **CharacterAnalyticsScreen** (updated) — data viz and mockup

**Technical Details:**
- **v0.2.0:** Game `Theme` copy, `StsColors` palette, compact metrics, bordered warm-dark panels, centred fixed-width column, character portraits, W·L lines
- **v0.3.0:** Manager = left list + right detail panel, auto-selected gold-bordered, detail panel shows name, large `CharacterSelectIcon` portrait, W·L line, History/Analytics/Info buttons, Info moved to panel button, compact content-sized card
- **Data Viz:** Pure `ColorRect` bars (no PCK): Deck Composition (card type), outcomes bar, per-ascension W/L bars, act-distribution bars, two-column stats grid
- **Backdrop:** Semi-transparent (`UiTheme.Backdrop` α 0.2) on all screens
- **Native Button:** Compendium button duplicates `NCompendiumBottomButton`, inherits texture/HSV shader/animations, gives own material, recolours via HSV hue uniform, replaces plain-text button

**Files:**
- `Code/UI/UiTheme.cs` (new)
- `Code/UI/CharacterManagerScreen.cs` (updated)
- `Code/UI/CharacterInfoScreen.cs` (updated)
- `Code/UI/CharacterAnalyticsScreen.cs` (updated)

**Risks:** Low (reuse existing game theme, no PCK art)

## Technical Lessons Learned

### Bug 1 — Duplicate custom characters (FIXED)
**Root cause:** BaseLib, KitLib, RitsuLib all patch `ModelDb.get_AllCharacters` + Ryoshu's own patch

**Fix:** Merge both sources (`ModelDb.AllCharacters` + `_contentById`) and deduplicate by `ModelId`

**Technical details:**
- The three earlier dedup attempts (ReferenceEquals, `HashSet<ModelId>`, `HashSet<Type>`) all failed for the same reason: each deduped only *within* the `_contentById` slice and so never collapsed an `AllCharacters` entry against a registry entry
- Fix (`Code/CharacterHelper.cs`): merge both sources (`ModelDb.AllCharacters` + `_contentById`) and deduplicate the **whole** result by `ModelId` — the key every store in this mod uses
- Base characters are emitted first in canonical order, customs follow sorted by title
- `GetCustomCharacters()` is now derived from `GetAllCharacters()` so the manager list and the Compendium stats injection always see the identical deduped set

### Bug 2 — "Stats Shown" toggle had no effect (FIXED)
**Root cause:** The `VisibilityStore` was toggled/persisted by the UI but nothing read it to drive the Compendium stats display, because the base mod's `NGeneralStatsGridPatch` had never been ported

**Fix:** Added `Code/Patches/StatsGridPatch.cs` — a Harmony postfix on `NGeneralStatsGrid.LoadStats` that appends one `NCharacterStats.Create(stats)` section to the private `_characterStatContainer` for each **custom** character that is visible (`VisibilityStore.IsVisible`) and has recorded stats (`GetStatsForCharacter != null`, matching the game's own base-character rendering)

### In-Select toggle (FIXED)
**Root cause:** Harmony's stacked `[HarmonyPatch]` attributes merge into ONE target

**Technical details:**
- Harmony does not treat multiple `[HarmonyPatch(typeof(X),"M")]` attributes on one patch method as "patch X and Y" — it **merges** them into a single target descriptor with the last type winning
- So the prefix/finalizer only ever patched `NCustomRunScreen.InitCharacterButtons`; the normal `NCharacterSelectScreen` was never armed
- When the player opened normal character select, `_filtering` stayed false, the getter postfix short-circuited, Ryoshu was never removed, and the "hid N" line never logged — exactly the reported symptom (Ryoshu present, `grep` empty)
- Fix: Split the arming into **four single-target methods** (`Arm_Select`/`Disarm_Select` on `NCharacterSelectScreen`, `Arm_CustomRun`/`Disarm_CustomRun` on `NCustomRunScreen`), each with exactly one `[HarmonyPatch]`. `_filtering` is now a depth counter for nesting safety. The getter postfix keeps `Priority.Last` + `[HarmonyAfter]`

### CharacterSelectPatch (v0.4.0)
**Root cause:** Screen cached in `NMainMenuSubmenuStack._characterSelectSubmenu`

**Technical details:**
- The "In Select" toggle filters the roster only during `InitCharacterButtons`, which runs when the character-select screen is first built
- The screen is built once (eagerly, in `NMainMenuSubmenuStack._Ready`) and cached in the stack's `_characterSelectSubmenu` field, so a runtime toggle was not reflected until the screen was rebuilt from scratch — i.e. after a restart
- Mutating the live buttons doesn't work (a disabled char has no button; RitsuLib's `NCharacterButtonStripScroller` ignores per-button `Visible`)
- Fix: On `EnabledStore.OnToggle`, free + null the stack's cached `_characterSelectSubmenu` / `_customRunScreen` so the next open rebuilds the screen fresh under the existing `AllCharacters` filter (identical to a launch-time build, which honors the toggle)
- Implemented in `CharacterSelectPatch` (postfix on `NMainMenuSubmenuStack._Ready` captures the stack + wires the handler; guards against freeing a currently-visible screen)

### Library-injected select buttons bypass `AllCharacters` (FIXED v0.5.0)
**Symptom:** Hiding a character in-select worked for Ryoshu but not for The Cursed or LittleWizard — they stayed on the select strip and could be picked at random.

**Root cause:** The two missing characters are BaseLib characters that RitsuLib registers into its own content catalog with mod-prefixed ids (`CHARACTER.THECURSEDMOD-THE_CURSED_MOD`, `CHARACTER.LITTLEWIZARD-LITTLE_WIZARD`) and injects directly into the strip as ordinary `NCharacterSelectButton`s. They are **never in `ModelDb.AllCharacters` at button-build time**, so the `get_AllCharacters` getter filter could not see them. Ryoshu, by contrast, adds itself via its own `get_AllCharacters` postfix and so the getter filter caught it.

**Investigation:** A diagnostic getter postfix logged that — even at `int.MinValue` priority (after every other getter postfix) — the only custom ever present in `__result` was Ryoshu. A second diagnostic that walked the live button strip confirmed The Cursed / LittleWizard exist there as `NCharacterSelectButton`s whose `Character` is the mod-prefixed model. Two earlier theories (HarmonyAfter ordering, then dropping to `int.MinValue`) were disproven by these logs — the characters simply never travel through `AllCharacters`.

**Fix (`CharacterSelectPatch`):** Keep the getter filter for the vanilla path (Ryoshu), and add a post-build pass on both `NCharacterSelectScreen.InitCharacterButtons` and `NCustomRunScreen.InitCharacterButtons` (priority `int.MinValue`, after BaseLib/RitsuLib) that walks the strip and **frees** any `NCharacterSelectButton` whose `Character` is a disabled custom. Freeing the node (not `Visible=false`) is what sticks, since library scrollers re-measure from the strip's real children.

**Takeaway:** Filtering `ModelDb.AllCharacters` only governs characters that reach the screen through the vanilla roster. Library-managed characters must be handled at the built-button level (or at the library's own catalog source).

## Current Mod State

The mod currently does three read-side primitives:

1. **Registry enumeration:** `CharacterHelper.GetCustomCharacters()` reads `ModelDb._contentById` via reflection, since `ModelDb.AllCharacters` is a hardcoded list of the 5 base characters
2. **Stats injection:** `NGeneralStatsGrid_LoadStats_Patch` Harmony postfix on `NGeneralStatsGrid.LoadStats`, respecting a master toggle and per-character visibility
3. **Visibility persistence:** `CharacterVisibilityStore` JSON file separate from the BaseLib config file

**Foundation files:**
- `Code/ModEntry.cs` (`[ModInitializer("Init")]`)
- `Code/CharacterHelper.cs`
- `Code/Patches/NGeneralStatsGridPatch.cs`
- `Code/Patches/StatsConsolePatch.cs`
- `Code/Config/CustomStatsConfig.cs` (BaseLib)
- `Code/Config/CharacterVisibilityStore.cs`

## Key Design Decisions

### No save mutation
- Backup/restore, orphan cleanup, and ModelId migration are out
- Read-only **export** to a separate file is allowed

### English only
- No new localization beyond the strings a feature needs

### Run history approach
- Already character-agnostic on disk and already rendered safely by the native viewer (see M3), so run-history work is navigation/aggregation, not storage

## Stretch Goals (Post-v0.3.0)

### UI / presentation
- Custom parchment nine-patch art for the screens (would require shipping a PCK; the mod is currently `has_pck:false`)
- Live animated character visuals (`CharacterModel.CreateVisuals()`) for full-body portraits — **DONE in v0.4.0** (`SubViewport` host + `ResourceLoader` safe load)
- Taller / denser analytics bars; add a **floor-reached distribution** chart alongside the act-reached one
- Alternate question-mark art on the Compendium button (`char_select_random.png`) if the flat `stats_questionmark` UI sprite reads too differently next to the portraits

### Roster management
- Reorder / pin characters in the manager list (and reflect that order in character select)
- Conflict detection: surface duplicate `ModelId` registrations (modded characters silently overwriting each other in `ModelDb._contentById`)

### Analytics / data
- A real **Compare Characters** view — sourceable today from the existing per-character aggregates
- Compute **win streaks / fastest win** from run history for Custom/Daily runs (the official `CharacterStats` only tracks these for Standard runs)
- A **game-mode filter** toggle on the analytics page (All / Standard / Custom / Daily) instead of the current fixed sections
- Optionally switch the manager list + detail-panel W/L to run-history-derived numbers for cross-screen consistency (cost: loading `.run` files per character; the list currently uses the cheap in-memory `CharacterStats`)

## Tooling & Setup

### Primary toolset: sts2-modding MCP (~153 tools)

**Code intelligence:**
- `get_entity_source` — full C# source for any game class
- `search_game_code` — regex search across ~1300 game files
- `browse_namespace` — list files in a game namespace
- `list_entities` — filter game entities
- `list_hooks` / `get_hook_signature` / `suggest_hooks` — hook discovery
- `suggest_patches` — Harmony patch targets from natural language
- `analyze_method_callers` — call graph for a method
- `get_entity_relationships` / `reverse_hook_lookup` — dependency mapping
- `check_mod_compatibility` — verify patch targets against current game version

**Generators & docs:**
- `generate_*` scaffolds (cards, relics, powers, harmony patches, overlays, UI, config, reflection accessors, save-data)
- `get_modding_guide <topic>`
- `get_baselib_reference <topic>`
- `create_mod_project` — full project scaffold

**Project-aware workflow:**
- `inspect_mod_project` → `apply_generated_output` → `validate_mod_project` → `deploy_mod`
- Lower-level: `build_mod`, `install_mod`, `hot_reload_project`, `watch_project`

**Live game (bridge, TCP 21337):**
- `bridge_*` — start seeded runs, drive screens, read combat/run/map state, snapshots, breakpoint debugging, AutoSlay multi-run testing

**Live scene inspection (GodotExplorer, TCP 27020):**
- `explorer_*` — walk scene tree, find/inspect nodes, read/write properties, tween, call methods

### ast-grep (v0.43.0)
Structural search/refactor for our C# code (use MCP for game source):
```bash
# find every Harmony postfix
ast-grep -p '[HarmonyPatch($$$)] public static void Postfix($$$) { $$$ }' -l csharp

# find reflection field lookups
ast-grep -p 'AccessTools.Field($$$)' -l csharp
```

### Build toolchain
- .NET SDK 10.0.108
- Python 3.14.5
- Prefer MCP's `build_mod` / `install_mod` / `hot_reload_project` over raw `dotnet`

## STS2 Modding Conventions (Carry-overs)

- **Assembly name = manifest `id` (lowercase)** — loader is case-sensitive on Linux
- **Don't bundle BaseLib** — reference compile-only, declare in manifest `dependencies`
- **BaseLib `SimpleModConfig` only renders STATIC properties**
- **`[ModInitializer("Method")]` on class, method must be `static`**
- **`ModelDb.AllCharacters` is only the 5 base characters** — enumerate `ModelDb._contentById` for the full base+modded roster
- **One `[HarmonyPatch]` target per patch method** — stacking merges them (last wins). To patch several methods, write one patch method per target (or a `TargetMethods()` enumerator)
- **Modded submenus can't use `PushSubmenuType<T>()`** — construct the instance and call the public `NSubmenuStack.Push` after `AddChildSafely`
- **Character-select roster filtering only takes effect on screen build** — cached in `NMainMenuSubmenuStack._characterSelectSubmenu`; on `EnabledStore.OnToggle`, free + null the stack's cached `_characterSelectSubmenu` / `_customRunScreen` so the next open rebuilds fresh under the existing roster filter
- **Hot-reload of code changes can fail** (`BadImageFormatException` — assembly already loaded non-collectible); a full game restart may be required
- **`min_game_version`** in manifest (currently `0.107.1`)

## Deployment

Game mods directory: `/run/media/nazar/Gaaaymes/SteamLibrary/steamapps/common/Slay the Spire 2/mods/<modid>/`

**Local testing:** Use `install_mod` to place `<modid>.dll`, `<modid>.pck` (if `has_pck`), `mod_manifest.json`

**Public releases:** Three channels (GitHub, Nexus, Steam Workshop) with automated CI for Nexus

## Updating for a new game version

When STS2 updates:
1. `get_game_info` for the new version
2. Backup `/home/nazar/sts2-modding-mcp/decompiled` → `decompiled_v<old>_backup`
3. `decompile_game force:true`
4. `diff_game_versions <old_backup> <new>` to see changed hooks/methods
5. Run `check_mod_compatibility`
6. Grep new decompiled source for each patch target / reflected member
7. If clean, bump `min_game_version`, `build_mod`, `install_mod`
8. Commit to `main`, then cut releases per Distribution/publishing

## Typical workflow

1. **Research with the MCP** (`get_entity_source` / `search_game_code` / `get_modding_guide`) to confirm exact game classes/hooks
2. **Scaffold/write** code in the project, reusing base mod's patterns. Use `ast-grep` for structural edits
3. **Build & deploy** with `build_mod` / `install_mod` (or `hot_reload_project`)
4. **Test — mostly manual.** Bridge/explorer tools are unreliable — deploy, launch game, verify by hand. Restart if hot-reload didn't take
5. **Validate** statically with `validate_mod` / `validate_mod_project` before considering a milestone done

## Key Resources

- **MCP:** https://github.com/elliotttate/sts2-modding-mcp (local: `/home/nazar/sts2-modding-mcp`, docs in `docs/`)
- **ast-grep:** https://github.com/ast-grep/ast-grep

## Base Mod Context

This mod evolved from **CustomCharacterStats**, which provided the three primitives that everything here builds on:

- **Registry enumeration:** `Code/CharacterHelper.cs` reflects `ModelDb._contentById` to enumerate installed custom characters
- **Native UI injection:** `Code/Patches/NGeneralStatsGridPatch.cs` Harmony postfix on `NGeneralStatsGrid.LoadStats`
- **Per-character config:** `Code/Config/CharacterVisibilityStore.cs` JSON persistence (separate from BaseLib config)

**Base mod files:**
- `Code/ModEntry.cs` (`[ModInitializer("Init")]`)
- `Code/CharacterHelper.cs`
- `Code/Patches/NGeneralStatsGridPatch.cs`
- `Code/Patches/StatsConsolePatch.cs`
- `Code/Config/CustomStatsConfig.cs` (BaseLib)
- `Code/Config/CharacterVisibilityStore.cs`

**Key differences from CustomCharacterStats:**
- Expanded from single-purpose stats injector to full character management suite
- Added per-character info cards, run-history filtering, analytics, export
- Implemented custom `NSubmenu` subclasses for modded screens
- Added live animated character portraits and In-Select toggle without restart
- Implemented random-character pool toggle (mod's first gameplay patch)
- Enhanced UI with native theme restyling and left list + right detail panel

## Future Priorities

1. **Character conflict detection** — surface duplicate `ModelId` registrations
2. **Compare Characters view** — sourceable from existing aggregates
3. **Custom art** — parchment nine-patch (would require PCK)
4. **Advanced analytics** — win streaks for Custom/Daily runs
5. **Game-mode filter** — All/Standard/Custom/Daily toggle on analytics

The mod is production-ready and released through v0.5.0 on all three channels. The random-character pool toggle (M7) shipped in v0.5.0 with multiplayer support, and the in-select hide is now a hard override across the select strip, the random pool, and the random draw.