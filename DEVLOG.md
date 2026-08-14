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
| **M16** | **Manager list polish: per-row win-rate sparkline, W/L scope, Yes/No Lend Cards, ? Help screen** | ✅ Shipped | v0.8.0 |
| **M17** | **Compendium stats crash fix: exclude non-playable meta-characters (RandomCharacter/Deprived)** | ✅ Shipped | v0.9.0 |

**Current released version: v0.9.2** (GitHub + Nexus shipped; Steam staged, awaiting the manual `ModUploader` run). `min_game_version 0.111.0`.

### Game update: v0.110.1 → v0.111.0 (2026-08-14)

**The game install moved libraries.** Steam reinstalled STS2 (app `2868840`) from `/run/media/nazar/Gaaaymes/SteamLibrary` to `/home/nazar/.local/share/Steam`, leaving the old directory as an empty husk that still contains `mods/`, `mods_disabled/` and `steam_appid.txt` — so `get_setup_status` just reported `game_found: false` with no hint as to why. Repointed `CharacterManager.csproj`'s `<Sts2Dir>` and `sts2-modding-mcp/sts2mcp_config.json`. The MCP server caches `GAME_DIR` at import, so `install_mod` still targets the stale path until the desktop app restarts; this build was copied into the mods dir by hand. Decompile + Roslyn index were driven directly through `sts2mcp.setup.run_decompile` / `build_roslyn_index` for the same reason.

Re-decompiled (`decompiled_v0.110.1_backup` kept): **23 added / 14 removed / 174 modified** source files (3528 → 3537 `.cs`), **0 changed hooks, 0 changed public-method signatures**. Most churn is VFX namespace reshuffling (orb VFX moved into `MegaCrit.Sts2.Core.Nodes.Orbs`) and a multiplayer handshake rework (`ClientConnectionFailedMessage` deleted, replaced by `NetErrorInfo` + `HandshakeManager`).

**No code changes needed.** Of the 20 game types we patch or reflect into, 13 were byte-identical and the 7 that changed changed only in methods we don't touch:

- `StartRunLobby` — the handshake rework removed the public `MaxPlayers` property (now a private `_maxPlayers` field) and retyped `PlayerFailedToConnect`. We use neither. The 4-arg ctor, `CleanUp(bool, NetError)` and **`BeginRunLocally(string, List<ModifierModel>)` are byte-identical** — still exactly one `rng.NextItem(ModelDb.AllCharacters)` call preceded by the static parameterless `AllCharacters` getter, so the M7 transpiler's guards still match.
- `NCharacterSelectScreen` / `NCustomRunScreen` — only `RemoteClientFailedToConnectToLocalHost` changed (handshake rework). `InitCharacterButtons` and `SelectCharacter` intact.
- `NRunHistory` — prev/next arrow enable-state handling added, plus a decompiler-naming churn in the player-icon loop. `_runNames`, `RefreshAndSelectRun` and `OnSubmenuOpened` intact.
- `CharacterModel` — `GenerateAnimator` gained a `Creature` parameter and a low-health idle state. We never call it; the M6 live portraits go through the private `VisualsPath` getter, which is unchanged.
- `Rng` — `NextUnsignedLong`'s default argument was removed. `NextItem<T>(IEnumerable<T>)` unchanged.
- `Splash` — rarity Uncommon → Rare. `OnPlay` (our M15 async-`MoveNext` transpiler target) unchanged.

`ColorfulPhilosophers`, `Orobas`, `Kaleidoscope`, `PrismaticGem`, `ModelDb`, `NSubmenu`, `NSubmenuStack`, `NGeneralStatsGrid`, `NCompendiumSubmenu`, `NCompendiumBottomButton`, `NCharacterSelectButton`, `NMainMenuSubmenuStack` and `NCharacterStats` were all byte-identical.

**Save schema:** `RunHistory.cs`, `RunHistoryPlayer.cs` and `SerializableRun.cs` are byte-identical to v0.110.1, and the only migration added this version is `SettingsSaveV7ToV8` (settings, which the analytics never read). No mod-side migration needed and no analytics field changes.

Bumped `version` 0.9.1 → 0.9.2 and `min_game_version` 0.110.0 → 0.111.0, built Release clean (0 errors, 3 pre-existing CS8602 nullable warnings), installed. **Not yet verified in-game** — needs a manual launch to confirm the Manager submenu, random pool and Lend Cards still behave.

> `check_mod_compatibility` again reports 22 issues, all inside `references/STS2mod-Stats_the_Spire/` (`CombatHistoryPatch.cs`, `RunLifecyclePatch.cs`, `CareerStatsSection.cs`, `FilterPanel.cs`). Our own `Code/` tree is clean.

### Game update: v0.108.0 → v0.110.1 (2026-08-01)
Re-decompiled (`decompiled_v0.108_backup` kept) and diffed: **54 added / 7 removed / 419 modified** source files, 2 hook changes (`ModifyCardPlayResultLocation` added; `AfterBlockBroken` now takes `PlayerChoiceContext` + two `Creature`s), and `AbstractModel.AfterModifyingCardPlayResultPileOrPosition` → `ModifyCardPlayResultLocation` / `AfterModifyingCardPlayResultLocation`. None of the changed hooks are ones we use.

**One break in our code:** `MegaCrit.Sts2.Core.Entities.Multiplayer.LobbyPlayer` was deleted and split into `StartRunLobbyPlayer` / `LoadRunLobbyPlayer` / `RunLobbyPlayer`. `StartRunLobby.PlayerConnected` is now `Action<StartRunLobbyPlayer>`, so `RandomPoolNet.OnPlayerConnected(LobbyPlayer)` no longer compiled — retyped to `StartRunLobbyPlayer`. Behaviour unchanged (the arg is discarded; it only triggers a re-broadcast).

**Everything else verified intact** against the new source: `StartRunLobby`'s 4-arg ctor + `CleanUp` + `BeginRunLocally(string, List<ModifierModel>)` (still a single `rng.NextItem(ModelDb.AllCharacters)` call, so the M7 transpiler still matches); `NRunHistory._runNames`/`RefreshAndSelectRun`/`OnSubmenuOpened`; `NSubmenu.OnSubmenuClosed`; `NGeneralStatsGrid.LoadStats` + `_characterStatContainer`; `NCompendiumSubmenu._Ready`/`_statisticsButton`/`_stack`; `NCompendiumBottomButton._locKeyPrefix`/`_label`/`_icon`/`_bgPanel`/`_hsv`; `NCharacterSelectScreen.SelectCharacter`/`InitCharacterButtons`; `NCustomRunScreen.InitCharacterButtons`; `NCharacterSelectButton.Character`/`IsRandom`; `NMainMenuSubmenuStack._characterSelectSubmenu`/`_customRunScreen`; `NCharacterStats._Ready`/`_characterStats`; `CharacterModel.VisualsPath`; `ModelDb._contentById`/`AllCharacters` (still 5 base chars); and all five M15 cross-source targets (`ColorfulPhilosophers`/`Orobas.GenerateInitialOptions`, `Kaleidoscope.AfterObtained`, `Splash.OnPlay`, `PrismaticGem.ModifyCardRewardCreationOptions`, `UnlockState.CharacterCardPools`/`Characters`).

**Save schema:** RunHistory v9→v10, SerializableRun v18→v20, ProgressSave v22→v24, Settings v6→v7. The only ModelId rename is `CARD.SCARE` → `CARD.SIDESTEP`, applied by the game's own migration before we ever read it; we hardcode no model IDs, and every `RunHistory`/`RunHistoryPlayer` field the analytics read still exists. No mod-side migration needed.

> **Note:** `check_mod_compatibility` reports 16 errors for this project — all of them are in `references/STS2mod-Stats_the_Spire/`, a third-party mod vendored for reference. Our own `Code/` tree is clean.

### Game update: v0.107.1 → v0.108.0 (2026-07-05)

Re-decompiled and diffed against v0.107.1 — the beta bump changed nothing in the game's C# surface (`diff_game_versions` found 0 added/removed/modified files), so all Harmony patch targets, hooks, and reflection fields (`NRunHistory._runNames`, `ModelDb.AllCharacters`, `NMainMenuSubmenuStack._characterSelectSubmenu`, the M15 async-`MoveNext` transpiler targets, `StartRunLobby.BeginRunLocally`, etc.) were unaffected.

`check_mod_compatibility`, however, caught a real (pre-existing, unrelated to this bump) break in our own code: `CharacterHelper.GetSourceMod` referenced `Mod.assembly` (singular), but the game's `Mod` class exposes `List<Assembly> assemblies` — this field was already `assemblies` back in 107.1 too, so the mod's `Code/` tree had been failing to build even before the update. Fixed to `mod.assemblies.Contains(charAssembly)`. Bumped `min_game_version` to `0.108.0`, rebuilt, installed, and re-ran `check_mod_compatibility` scoped to `Code/` clean (0 issues). No release cut yet — version still 0.8.0 pending user decision to publish.

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
- **UI:** Most Picked, Highest/Lowest Win Rate (≥3 runs), top 10 each. Upgrades Off/On toggle (base `ModelId` identity; collapse is the default).
- **Caveats encoded:** 1 (starter cards via deck union), 3 (once-per-run HashSet), 4b (upgrade-aware grouping), 6 (min-sample). Caveat 2 (colored shop buys) out of scope.
- **"Most Avoided" removed (2026-07-06), root cause confirmed:** a player reported it "not working." `Offered`/`Picks` come from `PlayerMapPointHistoryEntry.CardChoices`, but the game appends `wasPicked:false` to that *same* list for every card left unsold in a merchant's stock at `MerchantRoom.Exit` — plus a handful of relic/event reveals (`HeftyTablet`, `LeadPaperweight`, `MassiveScroll`, `FakeMerchant`, generic `EventModel` declines). None of these carry a flag distinguishing them from a genuine reward-screen skip, and purchased shop cards never get a matching `wasPicked:true` entry from that path — so `Offered` is one-sidedly inflated by ordinary unsold shop stock, swamping the real skip signal. `Most Picked` and the win-rate lists are unaffected (they key off `Picks`/`RunsWith`, not this ratio), but `Most Picked`'s secondary "{pct}% taken" display uses the same tainted `Offered` and should be treated with the same caution if it resurfaces. A real fix needs reward decisions captured live (e.g. a Harmony hook on `CardReward`) rather than reconstructed from saved history.

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

---

### M16 — Manager list polish (sparkline · Yes/No · Help)

Three usability tweaks to the manager list itself, no new gameplay patches.

**Per-row win-rate sparkline.** A new "Win Rate" column surfaces each character's win rate (%) over a strip of recent-result ticks (green win / red loss, oldest left → newest right), so the list reads as informative at a glance instead of leaving the row half-empty. Colour-graded %: green ≥50, gold ≥30, red below; a dash when there are no decisive runs. Hovering the % shows exact W/L and run count.
- **Data: `RosterWinHistory` (`Code/Analytics/`).** The analytics screen's `CharacterAnalytics.Compute` does a deep per-floor parse for *one* character — far too heavy to call per row. `RosterWinHistory` instead does a single O(files) pass over all `.run` files reading only top-level fields (`Win`, `WasAbandoned`, `StartTime`, `Players[].Character`), bucketing each run's outcome under every character that played it. One shared pass fills the whole roster. Cached by the same run-file-count generation token as `AnalyticsCache`; a failed file-list read is never cached (no startup-empty poison).
- **Scope:** all game modes; the % counts decisive runs only (wins + deaths). `RosterWinHistory` retains a 3-state outcome per run (win/loss/abandoned) so abandons can be shown without affecting the rate. Intentionally a richer set than the detail panel's Standard-only official W/L (M16 decision).
- **Abandoned cycle:** the "Win Rate" column header is a button that cycles three modes — Hidden (default; abandons excluded from strip and %), Shown (grey ticks in the strip, not in the %), Counted (grey ticks AND counted as losses in the %, since outside of testing an abandon is usually a lost run). Header colour signals the mode (muted → gold → orange) with a tooltip; strips rebuild in place, preserving selection. The header (and the row toggles) use `FocusMode = None` so Godot doesn't leave the button "selected" after a click — which both stuck until another control was clicked and overrode the state colour with the theme's focus-white.
- **Rendering:** `ColorRect` ticks laid out at integer offsets inside one fixed-size `Control` (not an `HBoxContainer` — container per-child rounding at a fractional centre origin made the spacing look uneven; a single control shifts all ticks together). Custom `_Draw` is still avoided per the standing rule (see CLAUDE.md). No PCK art.

**Detail-panel W/L scope.** The W/L line under the portrait is clickable, cycling three scopes: Official (the game's Standard-only ranked tally, via `Progress.GetStatsForCharacter` — default), All decisive (wins + losses across every mode, from `RosterWinHistory`), and All runs (adds a separate grey `A:` abandoned count). A caption names the current scope. The History button's enabled-gate now considers all-mode history too, so a character with only Custom/Daily runs is no longer wrongly greyed out.

**Lend Cards → Yes/No.** `MakeToggle` gained optional on/off label params; the Lend Cards column now reads Yes/No (an eligibility flag) while Stats and In Select keep Shown/Hidden (visibility).

**Tooltip rework + `?` Help screen.** The per-button hover tooltips (they fired on every In-Select / Lend-Cards toggle and felt noisy) were removed; the column *headers* keep concise tooltips. A new `?` button in the header opens `CharacterHelpScreen` — a code-built `NSubmenu` reference documenting every column, the sparkline, the detail-panel buttons, the Lend-Cards mechanic, and the random pool. Same single-instance reuse + scroll-of-section-panels pattern as the Info/Analytics drill-ins; static content built once on first open.

### M17 — Compendium stats crash fix (non-playable meta-characters)

**Symptom.** Opening Compendium → Statistics crashed to desktop (reported by a user; reproduced from the local `godot.log`). Not related to Downfall (the trigger mod in the report) or to any of our own features.

**Root cause.** `CharacterModel.IconPath` resolves to `res://scenes/ui/character_icons/<id>_icon.tscn`. The game ships icon scenes only for the five playable characters. Non-playable *meta*-characters — `RandomCharacter` (the "?" random-pick placeholder, id `random_character`) and `Deprived` (a test character), both `IsPlayable == false` — have no icon scene, and vanilla never renders them, so vanilla never calls `.Icon` on them. RitsuLib's stats injection (`StatsScreenCharacterStatsPatch`), however, builds an `NCharacterStats` per character **without an `IsPlayable` guard**, so it renders `RandomCharacter`; its `_Ready` reads `.Icon`, the missing scene load fails (`Cannot open file`), and the game hard-crashes. The stack contained zero CharacterManager frames — our own `StatsGridPatch` already filters `!IsPlayable` via `CharacterHelper.GetAllCharacters`.

**Fix — `NonPlayableStatsGuardPatch` (`Code/Patches/`).** A single prefix at the game's own choke point, `NCharacterStats._Ready`, which every stats card (base game, RitsuLib, or ours) funnels through. It resolves the card's character from the private `_characterStats.Id` and, if that character is `!IsPlayable`, `QueueFree()`s the just-added node and returns `false` — so the section is never built and `.Icon` is never reached. The parent container re-lays-out on removal, leaving no gap. This is independent of which mod injected the card, so it also suppresses any stray `Deprived` section.

**Design note.** The first pass shipped an extra defensive guard on `CharacterModel.Icon` (return an empty `Control` on a missing/failed scene) to stop the crash. Once the `_Ready` prefix removed the non-playable card at the source, that guard only defended against a state that can no longer occur, so it was deleted rather than left as dead surface area — the `_Ready` prefix is the sole fix. Root cause is upstream in RitsuLib (missing `IsPlayable` guard); this is a local, single-choke-point defense.

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

### Bug 3 — Detail-panel "official" W/L looked inconsistent with the row/all-runs W/L (FIXED, 2026-07-06)
**Symptom:** a user reported the win/loss numbers "don't match" between a character row's win-rate tooltip and the detail panel underneath the portrait — and specifically, the "Standard · official" scope could show *more* losses than the all-modes decisive count, which looks like a subset violation (Standard ⊆ all modes, so official losses should never exceed all-mode losses).
**Root cause:** it isn't a subset relationship at all. `ProgressSaveManager.UpdateWithRunData` — the game's own writer for `CharacterStats.TotalWins`/`TotalLosses` — is "called whenever the player wins, loses, **or abandons** a run," and an abandon reaches it as `victory: false`, landing in the same branch as a real death and incrementing `TotalLosses`. So the official tally's Losses = deaths + abandons (Standard mode only). `RosterWinHistory` (the row/all-runs source), by contrast, tracks `Abandoned` as its own third outcome bucket, explicitly excluded from the decisive win rate. Two legitimately different definitions of "loss," not a data bug — but our own tooltip/help text only mentioned the game-mode difference (Standard vs. all modes) and never mentioned the abandon-as-loss folding, which is what actually produces the counterintuitive "official > all-modes" numbers.
**Fix:** no data/logic change — `RosterWinHistory`'s doc comment, the detail panel's W/L tooltip, and `CharacterHelpScreen`'s detail-panel section now all state plainly that official Losses include abandoned Standard runs, so the discrepancy reads as expected behaviour instead of a bug.
**Takeaway:** when two data sources disagree, check whether they're actually built from the same event stream before assuming a subset/superset relationship — `WasAbandoned` vs. `victory` are not the same axis, and the game folds one into the other in exactly one of our two code paths.

### Bug 4 — `RosterWinHistory` credited a co-op partner's own runs to your stats (FIXED, 2026-07-06)
**Symptom:** follow-up to Bug 3 — "2 abandons but only 1 official loss" for the same character didn't fit the abandon-as-loss explanation alone (that predicts official ≥ all-modes losses, not fewer). Pushed on directly: a user pointed out there's no good reason a co-op partner's runs should ever land in *your own* character stats in the first place.
**Root cause:** they were right — this was a genuine bug, not a documented design difference. `ProgressSaveManager.UpdateWithRunData` resolves exactly ONE player per run for the official tally — the one matching the local platform-user id in multiplayer (sole player in singleplayer) — and only that player's `CharacterStats` gets touched. `RosterWinHistory.Load`, by contrast, looped `foreach (var p in h.Players)` and credited **every** player's own character unconditionally. So a shared/co-op run where a partner played character X (win, loss, or abandon) showed up in X's row for every profile in the party, not just the one who actually played X.
**Fix (`RosterWinHistory.Load`):** resolve "our own" `RunHistoryPlayer` the same way the game does — sole player in singleplayer, otherwise match `RunHistoryPlayer.Id` against `PlatformUtil.GetLocalPlayerId(h.PlatformType)` — and only credit that one player's character; skip the run entirely if the local player can't be resolved (mirrors the official path's silent no-op). (First cut also added a tooltip/help-screen line spelling out "only reflects runs you played" — cut on user feedback that it's an obvious assumption not worth the extra text; the tooltip/help still cover the two non-obvious divergences below.)
**Remaining, legitimate divergence:** with this fixed, "official" and "all-modes" can still disagree for two documented reasons only — game-mode scope (Standard-only vs. every mode) and abandon handling (official folds an abandoned Standard run into Losses; the other scopes keep it in a separate bucket) — both from Bug 3, both intentional.
**Takeaway:** don't stop at "these are two different definitions" when a user says the difference still doesn't make sense — a plausible-sounding explanation can be incomplete. This one initially covered only the abandon-folding axis; the whodunit (whose run counts) needed a second look because a real bug matched the same symptom shape as the intentional design choice sitting right next to it.

### Bug 5 — Same "whose run is it" mistake in Analytics and Run Autopsy (FIXED, 2026-07-06)
**Symptom:** raised directly by a user — card picks (and, by the same code path, deck/relic/potion facts and per-floor HP/ancients/boss-damage data) from a co-op partner's run were showing up in the local profile's own Analytics and Run Autopsy screens whenever the partner played the same character being analyzed.
**Root cause:** the exact same mistake as Bug 4, independently present in two more places that predate it. `CharacterAnalytics.Compute(characterId)` resolved "my player" for a run via `foreach (var p in h.Players) if (p.Character == characterId) { me = p; break; }` — matching on the CHARACTER being analyzed, not on player identity, so if a partner (not this profile) played `characterId` in a shared run, their `PlayerMapPointHistoryEntry` (cards, combat, deck/relics/potions) got attributed to this profile's analytics for that character. `CharacterRunAutopsyScreen` had a byte-for-byte copy of the same bug when loading a single run's floor-by-floor detail. Since `CharacterAnalytics.Compute` also builds the `RunSummary` list the Autopsy screen pages through, this one root cause reached both the aggregate analytics (win-rate/card/relic/potion stats, per-ascension breakdown) and the single-run drill-in.
**Fix:** extracted the correct resolution (sole player in singleplayer; else match `RunHistoryPlayer.Id` against `PlatformUtil.GetLocalPlayerId`) into a shared `Code/Analytics/LocalPlayerResolver.cs`, used by all three call sites now (`RosterWinHistory`, `CharacterAnalytics.Compute`, `CharacterRunAutopsyScreen`) instead of each re-deriving it. `CharacterAnalytics.Compute` now also explicitly requires `me.Character == characterId` — resolving to "us" isn't enough; the run only counts for this character's analytics if WE were the one who played it that run. `CharacterRunAutopsyScreen` defensively re-checks the same thing and logs + shows no per-floor data rather than another player's, should the (now correctly filtered) run list ever get out of sync.
**Takeaway:** the same wrong-assumption ("match by character, not by player") apparently got copy-pasted or independently reinvented three times across the M9–M12 build-out. Now that it's centralized in one resolver, any future per-run "which player is me" need should call it rather than re-deriving the match inline.

### Live animated portraits (v0.4.0)
`CharacterManagerScreen.TryAttachLiveVisuals` + `FitAndAnimate`. **Crash on modded chars:** `CharacterModel.CreateVisuals()` loads the `.tscn` through `AssetCache`, which other mods patch (Ryoshu's `RyoshuAssetCachePatch` fataled casting scene→texture) → fix: resolve private `CharacterModel.VisualsPath` via reflection and `ResourceLoader.Load` the `PackedScene` directly. **Black frame:** `NCreatureVisuals` is a `Node2D` and won't render in a Control → host in a `SubViewport` (shown via `TextureRect`), play `idle_loop`, fit via the creature's `Bounds` marker (~70% fill).

---

## Release History

### v0.9.2 (2026-08-14) — Game v0.111.0 compatibility
Game update only; **no mod code changes at all** — the manifest bump was the whole release. Re-decompiled and diffed v0.110.1 → v0.111.0: 0 changed hooks, 0 changed public-method signatures, and every patch target and reflected member verified intact (`BeginRunLocally`, `RunHistory`, `RunHistoryPlayer` and `SerializableRun` all byte-identical). `min_game_version` 0.110.0 → 0.111.0. Verified in game before release. See "Game update: v0.110.1 → v0.111.0" above.
- **Also this release:** Steam moved the game install off the external SteamLibrary to `~/.local/share/Steam`; `CharacterManager.csproj`'s `<Sts2Dir>` and the MCP's `sts2mcp_config.json` were repointed.
- **Distribution:** GitHub `v0.9.2` (`main`, `beta` fast-forwarded to match), Nexus via CI auto-fire (run 31765792042, success), Steam Workshop `3747550119` staged.

### v0.9.1 (2026-08-01) — Game v0.110.1 compatibility
Game update only; no feature or behaviour changes. `min_game_version` 0.108.0 → 0.110.0. Single code change: `LobbyPlayer` → `StartRunLobbyPlayer` in `RandomPoolNet.OnPlayerConnected` (the game split the lobby-player type three ways). All other patch targets and reflected members verified intact — see "Game update: v0.108.0 → v0.110.1" above.
- **Distribution:** GitHub `v0.9.1` (`main`, `beta` fast-forwarded to match), Nexus via CI auto-fire (run 30706181043, success), Steam Workshop `3747550119`.

### v0.8.1 (2026-07-06) — Game v0.108.0 compatibility + co-op stat/analytics bugfixes
Game update + a cluster of bugs found via user/playtest feedback. No new features; `min_game_version` bumped.
- **Game v0.107.1 → v0.108.0:** re-decompiled and diffed — 0 changed files in the game's C# surface, so no patch/hook targets moved. `check_mod_compatibility` did catch one pre-existing (unrelated to this bump) break in our own code: `CharacterHelper.GetSourceMod` referenced `Mod.assembly` (singular), but the game's `Mod` class only ever exposed `List<Assembly> assemblies` — fixed to `mod.assemblies.Contains(...)`.
- **Removed "Most Avoided Cards"** from the analytics Cards section (reported broken by a playtester). Root cause: `Offered`/`Picks` come from `PlayerMapPointHistoryEntry.CardChoices`, which the game also uses for unsold merchant stock and a few relic/event card reveals — none flagged apart from genuine reward-screen skips, and purchased shop cards never get a matching "picked" entry, so `Offered` was one-sidedly inflated by ordinary unsold shop stock. Most Picked and the win-rate lists are unaffected. A correct fix needs reward decisions captured live (Harmony hook on `CardReward`) rather than reconstructed from saved history — left for a future pass.
- **Fixed a real bug (not just a documentation gap) in `RosterWinHistory`, `CharacterAnalytics.Compute`, and `CharacterRunAutopsyScreen`:** all three resolved "which player is me" in a run by matching on the character being looked at, not on player identity, so a co-op partner's picks/deck/relics/potions/HP/boss-fight data could show up in this profile's own manager-row stats, per-character analytics, and run autopsy whenever the partner played the same character. Fixed via a new shared `LocalPlayerResolver` (sole player in singleplayer; else match `RunHistoryPlayer.Id` against `PlatformUtil.GetLocalPlayerId`), used by all three call sites now instead of each re-deriving the match.
- Clarified (didn't change) the detail panel's official-vs-all-runs W/L tooltip and Help screen: official's Losses fold in abandoned Standard runs (the game itself treats an abandon as a loss there); the other two scopes always break abandons out separately — a legitimate, intentional divergence, not a bug.
- **Distribution:** GitHub `v0.8.1` (`main`), Nexus via CI auto-fire, Steam Workshop `3747550119`.

### v0.8.0 (2026-06-27) — Manager list polish (M16)
Usability pass on the manager list itself; no new gameplay patches, `min_game_version` unchanged.
- **Win Rate column:** per-row win-rate % + recent-results tick strip (green win / red loss / grey abandoned), from a new lightweight all-modes `RosterWinHistory` (single shared pass, cached on the run-file generation token). Ticks laid out at integer offsets in one fixed-size Control for even spacing.
- **Abandoned cycle:** the Win Rate column header cycles Hidden → Shown-in-strip → Counted-as-losses-in-%, colour-coded (muted → gold → orange).
- **W/L scope:** the detail-panel win/loss line is clickable, cycling Standard-official → all runs (every mode) → all runs incl. abandoned (separate `A:` count); History button gate now considers all-mode history.
- **Lend Cards → Yes/No**; per-button tooltips removed in favour of header tooltips + a new **?** Help screen documenting every feature.
- **Fix:** toggle buttons use `FocusMode.None` so they never stay "selected" after a click (Godot focus-white was sticking and overriding the state colour).
- **Distribution:** GitHub `v0.8.0` (`beta`→`main`), Nexus via CI auto-fire, Steam Workshop `3747550119`.

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

1. **Advanced analytics follow-ups (from M13):** card & ancient Elo; filter-by-individual-pick; surface `progress.save` progression.
3. **Compare Characters view** — sourceable today from the existing per-character aggregates.
4. **Win streaks / fastest win for Custom/Daily** — the official `CharacterStats` only tracks these for Standard.
5. **Character conflict detection** — surface duplicate `ModelId` registrations (modded characters silently overwriting each other in `_contentById`).
6. **Reorder / pin characters** in the manager list (reflected in select).
7. **Custom art** — parchment nine-patch for the screens (would require shipping a PCK; mod is currently `has_pck:false`).

## Base mod context

Evolved from **CustomCharacterStats**, which provided the three primitives everything builds on: registry enumeration (`CharacterHelper` reflecting `_contentById`), native UI injection (postfix on `NGeneralStatsGrid.LoadStats`), and per-character JSON config separate from the BaseLib config file.
