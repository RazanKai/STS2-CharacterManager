# Character Manager

A **Slay the Spire 2** mod that turns the Compendium into a full character-management
suite for your whole roster — base game and modded characters alike. Control which
custom characters show up in stats and in character select, browse per-character info
cards, filter run history by character, and dig into per-character analytics with
built-in charts.

> Read-only by design: the mod never mutates your save. It only reads the game's
> progress, stats, and run-history files.

## Features

- **Character Manager screen** (Compendium → *Manage Characters*): a roster list with
  each character's icon, name, and source mod, paired with a detail panel showing a
  large portrait, win/loss, and quick actions.
- **Stats visibility toggle** — show/hide each custom character in the Compendium stats.
- **In-select enable/disable** — keep any character (base **or** modded) out of the
  character-select roster without uninstalling it. Applies immediately (no restart),
  works for characters added by character libraries (e.g. BaseLib/RitsuLib mods) too,
  and a safety guard never lets you empty the roster.
- **Configurable Random pool** — when the **Random** option is selected, a pool panel
  lets you pick exactly which characters Random may draw, with per-character In/Out
  toggles and All/None shortcuts. Multiplayer-synced. Characters you've hidden in-select
  are never drawn at random.
- **Lend Cards (cross-character source control)** — choose which characters' card/relic
  pools the cross-character mechanics — **Kaleidoscope, Colorful Philosophers, Splash,
  Prismatic Gem, and Orobas/SeaGlass** — may draw from. Per-character toggle with a hover
  tooltip; works for base and modded characters and never empties a pool. It changes only
  what those relics/events *offer*, not whether they can appear.
- **Per-character info card** — starting HP/gold/energy, gender, starting deck (with a
  card-type composition chart), starting relics/potions, and unlock requirement.
- **Run-history filtering** — jump straight to a character's runs in the game's own
  Run History screen.
- **Per-character analytics** — outcomes, win rate, win-rate moving windows, per-ascension
  W/L, act/floor-reached distributions, and run-length stats; plus deep deck-derived
  lists — card / relic / potion / ancient pick & win-rate & avoidance, deadliest and
  most-damaging encounters, combat-by-tier, and death causes — with All/Standard/Custom/Daily
  + ascension + recent-N filters. Correctly separates the game's **official (Standard-run)**
  stats from **Custom/Daily** runs, which the game excludes from official tallies.
- **Single-run autopsy** — drill into one run floor-by-floor: HP over time, boss damage,
  ancients taken, and a per-act event log, with ◀/▶ navigation across the character's runs.
- **Read-only export** — write a character's stats and every analytics aggregate to JSON
  and per-aggregate CSV files from the analytics screen.
- Native look: the UI inherits the game's theme and fonts, and the *Manage Characters*
  button matches the game's own Compendium buttons.

## Requirements

- Slay the Spire 2 (min game version **0.107.1**)
- [BaseLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127)
  (`Alchyr.Sts2.BaseLib`) — a separate installed mod, declared as a dependency so it
  loads first.

## Installation

1. Install **BaseLib** if you don't have it.
2. Download the latest `charactermanager` release.
3. Copy the mod folder into your game's `mods/` directory:
   `…/Slay the Spire 2/mods/charactermanager/`
   (it should contain `charactermanager.dll` and `mod_manifest.json`).
4. Launch the game and open **Compendium → Manage Characters**.

## Usage

Open the **Compendium** from the main menu and click **Manage Characters** (next to
Character Stats). Select any character in the list to populate the detail panel, then
use **History**, **Analytics**, or **Info**. Toggle the **In Select** and **Lend Cards**
columns directly in the list (for base and modded characters); **Stats** visibility is a
custom-character toggle (base stats always show). Hover any column header or toggle for a
tooltip explaining it.

## Notes & limitations

- **In-Select applies immediately.** Toggling a character on/off updates the
  character-select roster without a restart (the cached select screen is rebuilt on
  change), including characters injected by character libraries.
- **Standard vs Custom/Daily stats.** The game only counts **Standard** runs toward its
  official win/loss totals; Custom and Daily runs are excluded. The analytics screen
  shows both, clearly separated, so the numbers won't appear to contradict each other.
- **No custom art.** The mod ships no PCK; all visuals reuse the game's own theme,
  textures, and character art.

## Building from source

Requires the .NET SDK (9.0+). The assembly name must equal the manifest `id`
(lowercase) — `charactermanager`. BaseLib is referenced compile-only (not bundled).

```bash
dotnet build CharacterManager.csproj -c Debug
```

Then copy the build output + `mod_manifest.json` into the game's
`mods/charactermanager/` folder.

## Credits

- Author: **RazanKai**
- Built on the patterns from the base *Custom Character Stats* mod and the STS2 modding
  tooling/community.

## License

See repository for license details.
