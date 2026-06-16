# STS2 Character Management Mod — Project Guide

A Slay the Spire 2 mod that grows the existing **CustomCharacterStats** mod into a
full custom-character management suite (roster control, info cards, run-history
filtering, analytics, export). This folder currently holds the design spec; see
**`ROADMAP.md`** for the milestone-by-milestone plan with verified class/method
names, patch targets, and risks. Treat `ROADMAP.md` as the source of truth for
*what* to build; this file covers *how to work* in this project.

## Status

Planning / spec stage. No code in this folder yet. The implementation reference is
the working base mod (below). Build the new features as an evolution of that
codebase or as a fresh project that reuses its proven patterns.

## Reference codebase (the base mod)

The base mod lives in a separate folder: **`/home/nazar/Projects/STS2-CustomCharacterStats`**.
It already implements the three primitives everything here builds on:

- Registry enumeration of installed custom characters (`Code/CharacterHelper.cs`,
  reflecting `ModelDb._contentById`).
- Native UI injection via Harmony (`Code/Patches/NGeneralStatsGridPatch.cs`,
  postfix on `NGeneralStatsGrid.LoadStats`).
- Per-character config persisted to JSON (`Code/Config/CharacterVisibilityStore.cs`)
  plus a BaseLib `SimpleModConfig` (`Code/Config/CustomStatsConfig.cs`).
- Entry: `Code/ModEntry.cs` (`[ModInitializer("Init")]`).

Read its `OPENCODE.md` for hard-won gotchas (see "STS2 modding conventions" below).

## Installed tooling

### sts2-modding MCP — primary tool for this project
Repo: https://github.com/elliotttate/sts2-modding-mcp — an MCP server exposing
**~153 tools** for STS2 modding: game-data querying, mod code generation,
building, deployment, live game control, and automated playtesting. Tools are
prefixed `mcp__sts2-modding__*` in this environment.

What it gives you:
- **Code intelligence (game source).** The game's C# is decompiled and Roslyn-
  indexed, and its Godot PCK (15k+ assets, 3k+ entities, ~144 hooks) is extracted.
  Use `get_entity_source`, `search_game_code`, `browse_namespace`, `list_entities`,
  `list_hooks`, `get_hook_signature`, `suggest_hooks`, `suggest_patches`,
  `analyze_method_callers`, `get_entity_relationships`, `check_mod_compatibility`.
  **Do NOT run `decompile_game` first** — the index is prebuilt; only re-decompile
  after a game update (`get_setup_status` tells you if it's needed).
- **Generators & docs.** `generate_*` scaffolds (cards, relics, powers, harmony
  patches, overlays, UI, config, reflection accessors, save-data, etc.; default to
  BaseLib base classes — `use_baselib:false` for raw API), `get_modding_guide
  <topic>`, `get_baselib_reference <topic>`. `create_mod_project` scaffolds a full
  project tree (Code/ subfolders + localization/images).
- **Project-aware workflow.** Higher-level than raw build steps:
  `inspect_mod_project` (infer namespace/assembly/PCK/loc layout) →
  `apply_generated_output` (write generator blobs into the project, merge
  localization, transactional with `dry_run`) → `validate_mod_project`
  (localization + asset checks; also `validate_mod` / `validate_mod_assets`) →
  `deploy_mod` (validate+build+pack+install in one call). Lower-level equivalents:
  `build_mod`, `build_project_pck`, `install_mod`, `hot_reload_project`,
  `watch_project`, `analyze_build_output`.
- **Live game (the "bridge", TCP 21337).** `bridge_*` — start seeded runs with
  fixtures, drive every screen, read combat/run/map state, snapshots
  (`bridge_save_snapshot`/`restore`), breakpoint debugging
  (`bridge_debug_*`), AutoSlay multi-run stress testing (`bridge_autoslay_*`),
  exception/log polling, screenshots, `bridge_wait_for_screen` /
  `bridge_get_diagnostics` for sync/triage.
- **Live scene inspection (GodotExplorer, companion mod, TCP 27020).** `explorer_*`
  — walk the live scene tree, find/inspect nodes, read/write properties, tween,
  call methods, list loaded assemblies/types. Useful when building the M1 submenu
  UI (inspect real screen node layouts at runtime). Note: OPENCODE.md flagged the
  explorer port as sometimes unresponsive — verify before relying on it.

MCP prerequisites (server-side; the server auto-detects the game install and can
run `python -m sts2mcp.setup` to fetch tools): Python 3.11+, .NET SDK 9.0+,
`ilspycmd` (`dotnet tool install -g ilspycmd`) for C# decompilation, and GDRE Tools
(https://github.com/GDRETools/gdsdecomp) for PCK extraction. Server lives at
`/home/nazar/sts2-modding-mcp` (decompiled source + Roslyn index under
`decompiled/`). Configured in the Claude MCP config as a `sts2-modding` server
pointing at `run.py` (Claude Code uses `.mcp.json` / `~/.claude/mcp.json`, not
`settings.json`).

**Local docs:** `/home/nazar/sts2-modding-mcp/docs/` — `tools-reference.md` (all
~151 tools by category), `workflows.md` (multi-artifact apply/validate/deploy and
bridge playtest sequences), `project-structure.md` (generated layout + BaseLib),
`modding-guides.md` (the 28 `get_modding_guide` topics), `advanced-generators.md`,
`detailed-setup.md`.

**Most relevant `get_modding_guide` topics for this project:** `harmony_patches`,
`hooks`, `godot_ui_construction` (the M1 submenu UI), `reflection_patterns`
(`AccessTools`/`Traverse`, used throughout), `mod_config_integration` (BaseLib
`SimpleModConfig`, the M1 fallback UI), `save_file_format` (run-history files for
M3/M4), `project_structure`, `building`, `debugging`.

### ast-grep — structural search & refactor for our own code
Repo: https://github.com/ast-grep/ast-grep. **Installed: v0.43.0** (invoke as
`ast-grep`; avoid the `sg` alias, which collides with other tools). A tree-sitter
based CLI for AST-level search, lint, and rewrite — patterns look like code, with
`$VAR` / `$$$` metavariables.

Use it for structural edits across **our** C# mod code (Roslyn-indexed game source
is better searched with the MCP's `search_game_code`):
```bash
# find every Harmony postfix in the project
ast-grep -p '[HarmonyPatch($$$)] public static void Postfix($$$) { $$$ }' -l csharp

# find reflection field lookups we may want to centralize
ast-grep -p 'AccessTools.Field($$$)' -l csharp

# structural rewrite example (preview without -U, apply with -U)
ast-grep -p 'Traverse.Create($X).Field($F).GetValue<$T>()' -l csharp
```
YAML rules (`ast-grep scan` with `sgconfig.yml`) can enforce project conventions
(e.g. "config properties must be static" — a real bug class in the base mod).

### Build toolchain
- **.NET SDK installed: 10.0.108** (`dotnet build` works). The MCP targets .NET 9+;
  10 is backward compatible for these builds.
- **Python: 3.14.5** present.
- **`ilspycmd`: not currently on PATH.** Only needed if you must re-decompile the
  game after an update; install with `dotnet tool install -g ilspycmd` if so.
- Prefer the MCP's `build_mod` / `install_mod` / `hot_reload_project` over raw
  `dotnet` so deployment paths and PCK conversion are handled for you.

## STS2 modding conventions (carry-overs from the base mod)

These cost real debugging time; honor them in new code:

- **Assembly name must equal the manifest `id` (lowercase).** The loader looks for
  `Path.Combine(mod.path, manifest.id + ".dll")`. On Linux this is case-sensitive —
  set `<AssemblyName>` to the lowercase id (RootNamespace can stay PascalCase).
- **Don't bundle BaseLib.** It's a separate installed mod; reference it
  compile-only (`ExcludeAssets="runtime"`) and declare it in the manifest
  `dependencies` so it loads first. Two BaseLib copies = two static registries =
  silent config breakage.
- **BaseLib `SimpleModConfig` only renders STATIC properties.** Config props,
  backing fields, and their mutators must be `static`.
- **`[ModInitializer("MethodName")]` goes on the class**, method must be `static`.
- **`ModelDb.AllCharacters` is only the 5 base characters.** Enumerate
  `ModelDb._contentById` (reflection) for the full base+modded roster. Duplicate
  `ModelId` registration silently overwrites — relevant to conflict detection
  (see ROADMAP M1).
- **Modded submenus can't use `PushSubmenuType<T>()`** (it throws for unknown
  types); construct the instance and call the public `NSubmenuStack.Push` after
  `AddChildSafely`. Full contract in ROADMAP M1.
- **Hot-reload of code changes can fail** (`BadImageFormatException` — assembly
  already loaded non-collectible); a full game restart may be required for some
  changes. PCK/localization reloads are fine.
- **min_game_version** in the manifest (base mod targets `0.107.0`). Bump as needed.

## Deployment

Game mods directory:
`/run/media/nazar/Gaaaymes/SteamLibrary/steamapps/common/Slay the Spire 2/mods/<modid>/`.
A deployed mod folder contains `<modid>.dll`, `<modid>.pck` (if `has_pck`),
`mod_manifest.json`, plus the .NET sidecar files. Use `install_mod` to place them.

## Typical workflow

1. **Research with the MCP** (`get_entity_source` / `search_game_code` /
   `get_modding_guide`) to confirm the exact game classes/hooks a feature touches —
   verify ROADMAP names still exist for the current game version.
2. **Scaffold/write** code in the project, reusing the base mod's patterns. Use
   `ast-grep` for structural edits across our own files.
3. **Build & deploy** with `build_mod` / `install_mod` (or `hot_reload_project`).
4. **Test — mostly manual.** The in-game bridge / hook MCP tools (`bridge_*`,
   `explorer_*`) are **unreliable** — screen navigation and hooks often don't fire
   or stall. Give them one quick attempt (e.g. `bridge_ping`, `bridge_start_run`,
   drive to the screen, screenshot) and if they don't behave, **stop and hand off
   to manual testing** rather than burning time/tokens retrying. The dependable
   path is: deploy, launch the game yourself, and verify the feature by hand.
   Restart the game if a code hot-reload didn't take (hot-reload of code changes
   frequently fails — see conventions). Use `bridge_get_game_log` /
   `bridge_get_exceptions` for log/exception reads when the bridge *is* responding,
   since those are lower-risk than driving the UI.
5. **Validate** statically with `validate_mod` / `validate_mod_project` before
   considering a milestone done — this catches manifest/localization/asset issues
   without needing the unreliable live bridge.

## Pointers
- Spec & milestones: `./ROADMAP.md`
- Base mod + detailed dev notes: `/home/nazar/Projects/STS2-CustomCharacterStats/`
  (`OPENCODE.md`, `Code/`)
- MCP: https://github.com/elliotttate/sts2-modding-mcp (local: `/home/nazar/sts2-modding-mcp`, docs in `docs/`)
- ast-grep: https://github.com/ast-grep/ast-grep
