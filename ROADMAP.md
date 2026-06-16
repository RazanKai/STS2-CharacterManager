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
