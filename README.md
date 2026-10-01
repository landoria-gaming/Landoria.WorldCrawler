# World Crawler

Rebuild a server world locally, even without admin access or access to its save files.

## How it works

- Your character automatically flies between landmarks and records what the server sends.
- Captures whole zones within **80 metres** of known portals, discovered places, and personal map pins.
- Saves buildings, terrain changes, chest contents, signs, and displayed items when received.
- Skips players, creatures, animals, death markers, shared pins, round pins, and undiscovered map locations.
- Client-only: no server installation needed. Get the server owner's permission first.
- Install the DLL in your client's `BepInEx/plugins` folder.
- This is a partial reconstruction, not a complete server backup. Unseen data and changed game assets may be missing.

## Controls and versions

| Key | Action | Valheim version |
|---|---|---|
| `F8` | Start, pause, or resume export | `0.221.12` and `1.0.x` |
| `F9` | Select an export and prepare a local world from the main menu | `1.0.x` only |
| `F10` | Start, pause, or resume restoration in the prepared local world | `1.0.x` only |

- Fixed controls and production defaults; no configuration needed.
- Disable ValheimTomrer before use: it uses the same keys.

## 1. Export

- Join the source world with your usual character, then press `F8`.
- Let the mod move your character and wait for nearby data to load.
- Open the map: green zones are saved; amber zones are still pending.
- To include a missing area, add a personal **Home** pin on a revealed part of the map. Its 80-metre surroundings join the route, even during export.
- Press `F8` again to pause and return to your starting point. Press it later to resume unfinished zones.
- Files stay in `BepInEx/config/WorldCrawler/worlds`, in a folder identified by the world's UID and seed. Keep the whole folder.

## 2. Prepare the local world

- Start Valheim `1.0.x`, stay at the main menu, and press `F9`.
- Select your export, then confirm world preparation.
- Creates a local world with the same name, seed, and UID, using the current save format.
- Existing unrelated worlds are never overwritten. A partial export is enough to begin.

## 3. Restore

- Back up your character. The same world UID shares its map and saved positions.
- Join the prepared world alone with a **different character** from the one used for export.
- Press `F10`. The mod flies about 1 m above the terrain, stops while data arrives, restores each saved zone, and saves progress automatically.
- Long empty trips use twice the character's sprint speed. Travel between zones does not use portals or coordinate jumps.
- Press `F10` again to pause; press it later to resume without duplicating restored objects.
- After restarting the game, `F10` finds the prepared world's export automatically if its folder is still available.
- Review warnings in the BepInEx log and inspect the restored world after reloading before returning with your usual character.

## Screenshot

<img src="https://raw.githubusercontent.com/landoria-gaming/Landoria.WorldCrawler/refs/heads/main/assets/world-crawler-map.png" alt="World Crawler exporting landmark zones on the Valheim map" width="500">

## Contact

Report bugs through [GitHub Issues](https://github.com/landoria-gaming/Landoria.WorldCrawler/issues).
