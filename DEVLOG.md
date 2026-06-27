# Devlog — Character Management Mod

This document tracks the development of the mod from a single-purpose stats injector into a roster/character management suite. Every claim below was cross-checked against the decompiled game source (game version per `mod_manifest.json` `min_game_version`). No feature here mutates the player's save files.

This devlog is the **historical record + technical reference** (what was built, why, and the hard-won lessons). For *how to work* — tooling, conventions, build/deploy, version-update steps — see **`CLAUDE.md`**, which owns those topics. This file does not duplicate them.

## Project Overview

The Character Management Mod extends the earlier **CustomCharacterStats** mod into a full custom-character management suite:
- Roster control (list all characters, visibility toggle, in-select enable/disable)
- Per-character info cards (drill-in from manager list)
- Run-history filtering by character
- Per-character analytics (W/L, streaks, run aggregates, card/relic/encounter/death deep-dive)
- Single-run "autopsy" drill-in
- Read-only stats export to JSON + CSV
- Configurable random-character pool (multiplayer-synced)

**Architecture:** built on BaseLib (config + character pools); Harmony patches for menu injection and the gameplay paths; reflection for private game state (`ModelDb._contentById`, `NRunHistory._runNames`, etc.); custom `NSubmenu` subclasses + code-built Godot UI for all modded screens.

**Source layout:**
- `Code/ModEntry.cs` — `[ModInitializer]` entry point
- `Code/CharacterHelper.cs` — character enumeration + dedup (base + modded)
- `Code/Patches/` — Harmony patches (menu injection + gameplay)
- `Code/UI/` — custom Godot screens (manager, info, analytics, autopsy, random pool, theme)
- `Code/Config/` — stores (`VisibilityStore`, `EnabledStore`, `RandomPoolStore`, `RunHistoryFilter`, BaseLib config)
- `Code/Analytics/` — analytics, cache, name resolver, export
- `Code/Multiplayer/` — random-pool sync message

## Milestone status

| Milestone | Summary | Status | Shipped |
|---|---|---|---|
| M1 | Character Manager submenu (anchor) | ✅ Complete | v0.1.0 |
| M2 | Character info card (base + custom) | ✅ Complete | v0.1.0 |
| M3 | Run-history filtering by character | ✅ Complete | v0.1.0 |
| M4 | Read-only analytics per character | ✅ Complete | v0.1.0 |
| M5 | Read-only stats export | ✅ Complete | v0.1.0 |
| M6 | UI overhaul (theme + list/detail) | ✅ Complete | v0.2.0 / v0.3.0 |
| M7 | Random-character pool toggle (1st gameplay patch) | ✅ Complete | v0.5.0 |
| M8 | Analytics foundations + quick wins | ✅ Shipped | v0.6.0 |
| M9 | Card analytics (headline feature) | ✅ Shipped | v0.6.0 |
| M10 | Encounter & death analytics | ✅ Shipped | v0.6.0 |
| M11 | Relics, potions, ancients | ✅ Shipped | v0.6.0 |
| M12 | Single-run "autopsy" | ✅ Shipped | v0.6.0 |
| M13 | Export extension (all aggregates) | ✅ Shipped (advanced follow-ups open) | v0.6.0 |
| M14 | Analytics UI polish (density + bars) | ✅ Shipped | v0.6.0 |
| **M15** | **Cross-character source control (Kaleidoscope/Colorful Philosophers/…)** | ✅ Shipped | v0.7.0 |

**Current released version: v0.7.0** (all three channels). `min_game_version 0.107.1`. M15 plan: `M15-CROSS-CHARACTER-POOL-PLAN.md`.

---

## Milestone details

### M1 — Character Manager submenu (anchor)

- **CharacterManagerScreen** (`Code/UI/CharacterManagerScreen.cs`) — custom `NSubmenu` subclass, the main management interface.
- **CompendiumPatch** (`Code/Patches/CompendiumPatch.cs`) — postfix on `NCompendiumSubmenu._Ready` adds the "Manage Characters" button.
- **CharacterHelper.GetAllCharacters()** — enumerates base + custom characters (see Bug 1 for dedup).
- Push pattern: `AddChildSafely(instance)` then `NSubmenuStack.Push(instance)` (modded submenus can't use `PushSubmenuType<T>()`).
- Per-row controls: visibility (`VisibilityStore`), In-Select hide (`CharacterSelectPatch`).

### M2 — Character info card (base + custom)

- **CharacterInfoScreen** (`Code/UI/CharacterInfoScreen.cs`) — read-only drill-in.
- Shows HP/gold/energy, gender, source mod/version (`character.GetType().Assembly` ↔ `ModManager.GetLoadedMods()`), starting deck/relics/potions, unlock text (`GetUnlockText()`). All `CharacterModel` members public → low risk.

### M3 — Run-history filtering by character

- **RunHistoryPatch** (`Code/Patches/RunHistoryPatch.cs`) — patches `NRunHistory.OnSubmenuOpened` / `OnSubmenuClosed`; **RunHistoryFilter** (`Code/Config/RunHistoryFilter.cs`) static store.
- Runs are JSON files at `{profile}/saves/history/{StartTime}.run`. Postfix on open rebuilds private `_runNames` to this character's runs + `RefreshAndSelectRun(0)`; postfix on close clears the filter.
- Risk: reflection on `_runNames` / `RefreshAndSelectRun`, O(n) disk reads.

### M4 — Read-only analytics per character

- **CharacterAnalyticsScreen** + **CharacterAnalytics** (`Code/Analytics/`).
- Sources: `CharacterStats` (W/L, win rate, ascension, streaks, fastest win, playtime, badges) + run aggregates from `.run` files.
- **Game-mode correctness:** `ProgressSaveManager.UpdateWithRunData` only increments `CharacterStats` for `GameMode.Standard`; Custom/Daily excluded.

### M5 — Read-only stats export

- **StatsExporter** (`Code/Analytics/StatsExporter.cs`) — JSON + CSV to `{user_data}/mod_configs/charactermanager_exports/`. Reads only `CharacterStats` + `.run` files; never touches saves. (Extended in M13.)

### M6 — UI overhaul (v0.2.0 + v0.3.0)

- **UiTheme** (`Code/UI/UiTheme.cs`) — copy of the game `Theme`, `StsColors` palette, bordered warm-dark panels.
- Manager = left list + right detail panel; auto-selected gold-bordered row; large `CharacterSelectIcon` portrait; History/Analytics/Info buttons.
- Data viz is pure `ColorRect` bars (no PCK): deck composition, outcomes, per-ascension W/L, act distribution. Semi-transparent backdrop on all screens.
- Native Compendium button duplicates `NCompendiumBottomButton` (texture/HSV shader/animations), own material, recoloured via HSV hue.

### M7 — Random-character pool toggle (mod's first gameplay patch)

The first gameplay path (`affects_gameplay → true`). Lets the player choose which characters the **Random** select button may draw, multiplayer-synced.

**Pool store (`Code/Config/RandomPoolStore.cs`):** `EnabledStore` clone (`charactermanager_randompool.json`, `Dictionary<string,bool>` keyed by `ModelId`, default in-pool, `OnToggle` event). `BuildLocalPool()` = `ModelDb.AllCharacters` (runtime order preserved) keeping a character only if it's BOTH enabled in-select AND in-pool — so the in-select hide is a hard override on the draw. Empty-pool fallback draws from the in-select-visible roster, never the full roster (so a hidden character can never leak in, even when every visible one is unchecked).

**Gameplay filter (`Code/Patches/RandomPoolPatch.cs`):** Harmony transpiler on `StartRunLobby.BeginRunLocally(string, List<ModifierModel>)`. The live game uses `GetRandomEligibleCharacters()` (not the decompiled `ModelDb.AllCharacters`); the transpiler traces backward from the single `Rng.NextItem<CharacterModel>` call and replaces whatever static parameterless instruction loads the collection with `RandomPoolStore.GetPool()` — stack-neutral, rng-sequencing untouched. Fails closed to vanilla if the target isn't found exactly once. **Most update-fragile patch — re-verify every game update.**

**Multiplayer (`Code/Patches/RandomPoolLobbyPatch.cs` + `Code/Multiplayer/RandomPoolMessage.cs`):** each player broadcasts their own pool; peers store remote pools by net id and re-derive every player's draw locally (`BeginResolution`/`GetPool`/`PoolForPlayer`), so the result matches on all machines.

**UI (`Code/UI/RandomPoolPanel.cs` + `Code/Patches/RandomPoolUiPatch.cs`):** themed floating card via postfix on `NCharacterSelectScreen.SelectCharacter` when the selected button `IsRandom`; lists `CharacterHelper.GetAllCharacters()` minus in-select-hidden chars (dynamic; picks up modded chars at runtime); In/Out toggle per character, All/None; broadcasts the local pool on Random pick.

**Edge cases:** changing the pool changes which character a seed yields (expected); `YummyCookie`'s separate in-run pick left vanilla.

### M8 — Analytics foundations + quick wins

First milestone of the analytics expansion (`ANALYTICS_PLAN.md`); cross-cutting infra for M9–M12 plus three visible wins. Read-only.

- **Async/cached deep-parse (`Code/Analytics/AnalyticsCache.cs`):** process-wide cache of per-character `CharacterAnalytics`; invalidation via a cheap generation token (count of run-history files). `LoadFailed` snapshots are never cached (can't get pinned to zeros at startup). **Threading:** aggregation runs on the main thread (SaveManager is a Godot singleton with unverified off-thread safety); the screen paints "Crunching run history…" and defers one frame.
- **Name resolver (`Code/Analytics/NameResolver.cs`):** `ModelId` → localized name via `LocString.Exists` table-probing, memoised, SCREAMING_SNAKE→Title-Case fallback for modded ids.
- **Ranked-row widget (`UiTheme.MakeRankedRow`):** shared "name — bar — value" row.
- **Quick wins:** floor-reached distribution bars; win-rate moving windows (last 10/50/100/all decisive runs, abandons excluded); expanded filter bar (min-ascension + recent-N cycle buttons) driven by composite `RunFilter`.

**Post-test fixes (verified Ironclad, 32 runs):** act-reached used `RunHistory.Acts.Count` (the *planned* act list, always ~3-4) → switched to `MapPointHistory.Count` (acts actually entered); also corrects the export. Custom/Daily win rate aligned to the decisive rate (`wins/(wins+deaths)`).

**Build fix:** freshly-cloned `references/` stats-mod clones were swept into compilation by the `**/*.cs` glob → added `<Compile Remove="references/**/*.cs" />` to the csproj.

### M9 — Card analytics (headline feature)

Per-card pick / win-rate / avoidance lists from the `.run` files. Built on M8 cache + name resolver + ranked rows; respects every filter.

- **Deep parse:** the cached per-run walk extracts `CardChoices` (offered + `wasPicked`, per-occurrence), `DeckCards` (floor `CardsGained` ∪ final deck), `RemovedCards`, `UpgradedCardIds`. Floor entries matched to the character's player by `RunHistoryPlayer.Id` ↔ `PlayerMapPointHistoryEntry.PlayerId` (single-player fallback). `ComputeCardStats(upgradeAware)` aggregates: Offered/Picks per-occurrence; RunsWith/WinsWith de-duped once per run; RunsWith from the gained∪final-deck union (so starters aren't invisible).
- **UI:** Most Picked, Highest/Lowest Win Rate (≥3 runs), Most Avoided (≥3 offers), top 10 each. Upgrades Off/On toggle (base `ModelId` identity; collapse is the default).
- **Caveats encoded:** 1 (starter cards via deck union), 3 (once-per-run HashSet), 4b (upgrade-aware grouping), 6 (min-sample). Caveat 2 (colored shop buys) out of scope.

### M10 — Encounter & death analytics

Combat-side analytics from the same cached walk. Tier comes from `RoomType` directly (caveat 5 without string-munging).

- **Deep parse:** `ExtractCombatFacts` (encounter `ModelId`, tier, floor `DamageTaken`, `TurnsTaken` → `RunSummary.Combats`). `ResolveDeath` per caveat-4 chain (win→None; abandoned→Abandoned; `KilledByEncounter`→Combat; `KilledByEvent`→Event; deepest combat→Combat; else Unknown). Aggregations: `ComputeEncounterStats`, `ComputeTierStats`, `ComputeDeathCauses`, nearest-rank `Percentile`.
- **UI:** Combat-by-Tier table; Deadliest Encounters (death rate, ≥3 fights); Most Damaging (avg + p80, ≥3 fights); Death Causes (colour-coded). Avg-turns + `DeathInfo.Act` captured but not yet surfaced.

### M11 — Relics, potions, ancients

Pick / win-rate lists, same pattern as M9, same cached walk.

- **Deep parse:** `ExtractInventoryFacts` (relic/potion choices + `wasPicked`, `BoughtRelics`/`BoughtPotions`, ancient options by `Title` + `WasChosen`; owned union completed by final `Relics`/`Potions` snapshots). Generic `ComputeOwnedChoiceStats` → Relic/Potion stats; `ComputeAncientStats` identifies options by `Title.LocEntryKey` (caveat 7).
- **UI:** relics Most Picked + Highest/Lowest Win Rate; potions Highest/Lowest; ancients Most Taken + Highest Win Rate; all via reused `AddPickListSection`, filter-aware.

### M12 — Single-run "autopsy"

A per-run drill-in (loads one `.run` file directly, renders floor by floor) — different shape from M8–M11's cross-run aggregation.

- **Screen (`Code/UI/CharacterRunAutopsyScreen.cs`, new `NSubmenu`):** opened from a Run Autopsy button on the analytics header; ◀ Older / Newer ▶ walk the character's runs (newest-first list handed over from the analytics aggregate; only the selected run's file is loaded). Sections: Summary; HP Over Time; Ancients Taken; Boss Fights (damage bars); per-act floor event log.
- **HP-over-time chart:** built as an `HBoxContainer` of per-floor `VBoxContainer` + `ColorRect` segments — **not** custom `_Draw`/`_Process`/`Line2D`, which render nothing on dynamically-instantiated Control subclasses in the pushed-submenu load path (see CLAUDE.md gotcha; cost three failed attempts).

### M13 — Export extension (all aggregates)

The M5 JSON/CSV exporter now includes every M8–M12 aggregate.
- **JSON:** adds `cards`, `encounters`, `combatTiers`, `deathCauses`, `relics`, `potions`, `ancients`, plus `floorReachedDistribution` + `winRateWindows` under `runHistoryAggregate` (rates null when no samples).
- **CSV:** per-aggregate files (`{stem}_cards.csv`, `_encounters.csv`, `_relics.csv`, `_potions.csv`, `_ancients.csv`, `_deaths.csv`), each only when it has rows; `ExportResult.Files` lists everything; status line reports the count. Cards exported base-collapsed.
- **Still open (advanced follow-ups):** card & ancient Elo (opt-in, relic-dependent grouping — caveat 8); filter-by-individual-card/ancient-pick; surfacing `progress.save` progression (unlocks/achievements).

### M14 — Analytics UI polish (density + bars)

Presentation pass on `CharacterAnalyticsScreen` / `UiTheme`; no analytics math changed.
- **Tab-aware summary:** the "Summary (Standard runs)" card (from `CharacterStats`, Standard-only) now hides on Custom/Daily via a `showSummary` gate (`_currentFilter` is All or Standard), so those tabs lead with run-history-derived sections.
- **Rounded bar fill (`UiTheme.MakeBarTrack`):** segments are now `Panel` + `StyleBoxFlat` rounding only outer corners (Godot `ClipContents` clips the rectangle, not the corner radius), tracking `firstFilled`/`lastFilled`.
- **Aligned ranked rows (`MakeRankedRow`):** fixed name column (`ClipText` + tooltip) + expand-fill bar + wider fixed value column (108) so rows align across sections.
- **Two-column card layout:** sections greedily balanced across two columns inside a vertical-only `ScrollContainer` (`BeginColumns`/`AddSection`/`EstimateSectionWeight`/`CountLeafRows`); stat grids drop to 1 column. Balance is by estimated row count (so column order doesn't track reading order — acceptable for a dashboard).

### M15 — Cross-character source control

Control which characters' card/relic pools the cross-character mechanics (Kaleidoscope, Colorful Philosophers, Splash, Prismatic Gem, Orobas/SeaGlass) may draw from — the same lever-class as M7's random pool. All route through `UnlockState.CharacterCardPools` (= `Characters.Select(c => c.CardPool)`); Orobas reads `Characters` directly.

**Approach.** A `CrossSourceStore` (per-character eligibility, default on, persisted to `charactermanager_crosssource.json`, independent of the In-Select and Random-Pool stores) plus a shared `CrossSourceFilter` applied per-consumer via one transpiler each — never a global choke-point postfix, which would also shrink `CardPools`/`Cards` everywhere. `CrossSourceFilter` maps pools back to characters by `CardPool` identity and **never returns an empty set** (falls back to the vanilla roster), so consumers that assume ≥1 source never crash.

**Five transpiler patches**, each swapping the `UnlockState.CharacterCardPools`/`Characters` getter call inside one method for the filtered equivalent:
- `ColorfulPhilosophers.GenerateInitialOptions` (sync), `PrismaticGem.ModifyCardRewardCreationOptions` (sync), `Orobas.GenerateInitialOptions` (sync — only the single `Characters` pick; the broader `SeaGlassOptions` list iterates `ModelDb.AllCharacters` directly and is left vanilla).
- `Kaleidoscope.AfterObtained` and `Splash.OnPlay` are **`async`** — the getter call lives in the compiler-generated state machine, so these target `AccessTools.AsyncMoveNext(...)`, not the kickoff stub. (Targeting the stub matched 0 getter calls and silently no-op'd — the bug fixed in v0.7.0.)

Appearance/eligibility gates (`Kaleidoscope.IsAllowedAtNeow`, `ColorfulPhilosophers.IsAllowed`) read the **unfiltered** count and are intentionally left alone: filtering changes what's *offered*, never whether the relic/event can *appear*. Multiplayer needs no sync — each peer filters from its own `UnlockState` identically.

**UI.** A "Lend Cards" column in the Character Manager (per-character toggle, hover tooltip). Base characters are eligible by default and toggleable.

**Base-character select management (v0.7.0).** The In-Select toggle was extended to base characters (previously custom-only): `CharacterSelectPatch.IsDisabled` no longer exempts base chars, and the `AllCharacters` getter postfix now hides disabled base chars during button construction. A safety guard keeps the full roster if *every* character would be disabled, so the select screen can never be empty.

Full pre-implementation analysis in **`M15-CROSS-CHARACTER-POOL-PLAN.md`**.

---

## Technical Lessons Learned

### Bug 1 — Duplicate custom characters (FIXED)
**Root cause:** BaseLib, KitLib, RitsuLib (and Ryoshu) all patch `ModelDb.get_AllCharacters`, so a modded character is reachable through *both* `AllCharacters` and `_contentById`. The three earlier dedup attempts (ReferenceEquals, `HashSet<ModelId>`, `HashSet<Type>`) all deduped only *within* the `_contentById` slice and never collapsed an `AllCharacters` entry against a registry entry.
**Fix (`CharacterHelper.cs`):** merge both sources and dedup the **whole** result by `ModelId`. Base chars first in canonical order, customs sorted by title. `GetCustomCharacters()` derives from `GetAllCharacters()` so manager list and Compendium injection see the identical set.

### Bug 2 — "Stats Shown" toggle had no effect (FIXED)
**Root cause:** `VisibilityStore` was toggled/persisted but nothing read it to drive the Compendium display (the base mod's stats-grid patch had never been ported).
**Fix (`Code/Patches/StatsGridPatch.cs`):** postfix on `NGeneralStatsGrid.LoadStats` appends one `NCharacterStats.Create(stats)` per visible custom character with recorded stats.

### In-Select toggle merge bug (FIXED)
**Root cause:** Harmony merges multiple `[HarmonyPatch(typeof(X),"M")]` attributes on one method into a single target (last type wins) — so only `NCustomRunScreen.InitCharacterButtons` was armed; normal `NCharacterSelectScreen` never was.
**Fix:** split into four single-target methods (`Arm/Disarm_Select`, `Arm/Disarm_CustomRun`); `_filtering` is a depth counter. **General rule: one `[HarmonyPatch]` target per method.**

### Character-select cache (v0.4.0)
**Root cause:** the screen is built once eagerly and cached in `NMainMenuSubmenuStack._characterSelectSubmenu`, so a runtime In-Select toggle wasn't reflected until restart. Mutating live buttons doesn't work (a disabled char has no button; RitsuLib's scroller ignores per-button `Visible`).
**Fix (`CharacterSelectPatch`):** on `EnabledStore.OnToggle`, free + null the cached `_characterSelectSubmenu` / `_customRunScreen` so the next open rebuilds fresh under the existing `AllCharacters` filter (guards against freeing a visible screen).

### Library-injected select buttons bypass `AllCharacters` (FIXED v0.5.0)
**Symptom:** in-select hide worked for Ryoshu but not The Cursed / LittleWizard.
**Root cause:** those are BaseLib characters RitsuLib registers with mod-prefixed ids and injects directly as `NCharacterSelectButton`s — **never in `ModelDb.AllCharacters` at button-build time**, so the getter filter couldn't see them. Confirmed by diagnostics (even at `int.MinValue` priority the only custom in `__result` was Ryoshu).
**Fix (`CharacterSelectPatch`):** keep the getter filter for the vanilla path; add a post-build pass on both screens' `InitCharacterButtons` (priority `int.MinValue`) that **frees** any button whose `Character` is a disabled custom. Freeing the node (not `Visible=false`) is what sticks.
**Takeaway:** filtering `AllCharacters` only governs characters that reach the screen through the vanilla roster; library-managed characters must be handled at the built-button level.

### Live animated portraits (v0.4.0)
`CharacterManagerScreen.TryAttachLiveVisuals` + `FitAndAnimate`. **Crash on modded chars:** `CharacterModel.CreateVisuals()` loads the `.tscn` through `AssetCache`, which other mods patch (Ryoshu's `RyoshuAssetCachePatch` fataled casting scene→texture) → fix: resolve private `CharacterModel.VisualsPath` via reflection and `ResourceLoader.Load` the `PackedScene` directly. **Black frame:** `NCreatureVisuals` is a `Node2D` and won't render in a Control → host in a `SubViewport` (shown via `TextureRect`), play `idle_loop`, fit via the creature's `Bounds` marker (~70% fill).

---

## Release History

### v0.7.0 (2026-06-27) — Cross-character source control (M15)
Per-character "Lend Cards" control over which characters' pools the cross-character mechanics (Kaleidoscope, Colorful Philosophers, Splash, Prismatic Gem, Orobas/SeaGlass) may draw from, via five per-consumer transpilers + a never-empty `CrossSourceFilter`.
- **Fix:** the Kaleidoscope and Splash patches targeted the `async` method stub instead of its state machine, so they matched 0 getter calls and silently no-op'd. Both now target `AccessTools.AsyncMoveNext(...)`.
- Base characters gained the In-Select toggle (hide from select/custom-run screens), with a guard that never lets the roster be emptied, plus the new Lend Cards toggle.
- Hover tooltips on the Stats / In Select / Lend Cards columns (hard-wrapped — the native Godot tooltip doesn't word-wrap).
- **Distribution:** GitHub `v0.7.0` (`beta`→`main`), Nexus via CI auto-fire, Steam Workshop `3747550119`. `min_game_version` unchanged.

### v0.6.0 (2026-06-26) — Analytics deep-dive + autopsy + UI polish
The largest content jump since v0.1.0: the entire analytics suite (M8–M13) plus the M14 presentation pass.
- Per-character analytics (M8–M11): win-rate windows; card pick/win-rate/avoidance with Upgrades toggle; relic/potion/ancient lists; encounter analytics, Combat-by-Tier, Death Causes; act/floor distributions; composite mode + min-ascension + recent-N filtering.
- Single-run autopsy (M12): summary, HP-over-time column chart, ancients, boss damage, per-act floor log; ◀/▶ navigation.
- Export (M13): all aggregates in JSON + per-aggregate CSVs.
- UI polish (M14): tab-aware summary, rounded bar fills, aligned ranked rows, two-column density.
- **Distribution:** GitHub `v0.6.0` (`beta`→`main`), Nexus via CI auto-fire, Steam Workshop `3747550119`. `min_game_version` unchanged.

### v0.5.0 (2026-06-25) — Random-character pool + in-select hard override
First public release of the M7 random pool (multiplayer-synced; mod's first gameplay patch), with the fixes that make the in-select hide a hard override across the select strip, the pool panel, and the draw (incl. library-injected characters — see Lessons). Distribution: all three channels.

### v0.4.0 (2026-06-22) — Two stretch features
Live animated character portraits (`SubViewport` host + safe `ResourceLoader` load — see Lessons) and In-Select toggle without restart (cache invalidation — see Lessons). Distribution: all three channels.

### v0.3.1 (2026-06-18) — Game v0.107.1 compatibility
STS2 0.107.0 → 0.107.1 (1481 modified source files, 116 hook-signature changes). All 7 Harmony patch targets + reflected members intact; no gameplay hooks affected (menu-screen patches only); `check_mod_compatibility` = 0 issues. Hook changes: `After*`/`Before*`/`Should*`/`Modify*` gameplay hooks dropped `ICombatState`/`IRunState` params, moving to `PlayerChoiceContext` + entity args (didn't affect us). Distribution: all three channels.

*(v0.1.0–v0.3.0 shipped M1–M6; see milestone details above.)*

---

## Design decisions

- **No save mutation.** Backup/restore, orphan cleanup, ModelId migration are out of scope. Read-only export to a separate file is allowed.
- **English only.** No localization beyond what a feature needs. (M15 will need a loc fallback if it allows modded colors into Colorful Philosophers — see plan.)
- **Run history is read-only navigation/aggregation**, not storage — it's already character-agnostic on disk and rendered safely by the native viewer.

## Future work

1. **M15 — cross-character source control** (planned; see `M15-CROSS-CHARACTER-POOL-PLAN.md`).
2. **Advanced analytics follow-ups (from M13):** card & ancient Elo; filter-by-individual-pick; surface `progress.save` progression.
3. **Compare Characters view** — sourceable today from the existing per-character aggregates.
4. **Win streaks / fastest win for Custom/Daily** — the official `CharacterStats` only tracks these for Standard.
5. **Character conflict detection** — surface duplicate `ModelId` registrations (modded characters silently overwriting each other in `_contentById`).
6. **Reorder / pin characters** in the manager list (reflected in select).
7. **Custom art** — parchment nine-patch for the screens (would require shipping a PCK; mod is currently `has_pck:false`).

## Base mod context

Evolved from **CustomCharacterStats**, which provided the three primitives everything builds on: registry enumeration (`CharacterHelper` reflecting `_contentById`), native UI injection (postfix on `NGeneralStatsGrid.LoadStats`), and per-character JSON config separate from the BaseLib config file.
