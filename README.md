# Valheim World Crawler ![Experimental](https://img.shields.io/badge/status-experimental-orange)

World Crawler lets you pull a world's data from a server and rebuild it locally. It starts with the world's seed and unique ID, then captures its buildings, terrain modifications, chests, and chest contents. You do not need to be a server admin or have access to the server's save files.

> ⚠️
> This process can take quite a while when the explored world is large. It may also be detected as cheating by the server, potentially resulting in a ban. Use it responsibly and get the server owner's permission first.

## Why?

Sometimes, a world is simply too good to lose:

- A dedicated server admin disappears overnight or decides to move on from the project.
- A public or community server is about to shut down or be wiped, and players want to **keep a local copy of their bases and builds**.
- Players want to continue the adventure in Singleplayer, without having to rely on the server being online.

## Controls

| Key | Action |
| --- | --- |
| **F8** | Start or stop recording received world data. |
| **F9** | At the main menu, prepare a local world from the latest recording. |
| **F10** | Start or stop restoration around you. |
| **Alt + map click** | Teleport to the clicked location when `EnableCheats` is enabled. |

Local daylight is set to **0.4** when entering a world. Movement stays normal: no forced flight or movement locks.

## Settings

| Setting | Default | Effect |
| --- | --- | --- |
| `EnableCheats` | `false` | Enables god mode, ghost mode, cold immunity, unlimited stamina, and **Alt + map click** teleportation. |

## How to use

### 1. Record the server world

- Join the source world and press **F8**. Walk, run, sail or teleport; only objects in your current 64 m sector are recorded.
- Received objects are copied into a cache immediately, then saved every **10 seconds**. Leaving a zone does not discard its cached data.
- **Recording...** appears below the minimap. Three counters at the top show saved zones, saved objects, and cached objects.
- A scrollable journal on the left lists newly saved prefabs with time, English name when available, category, and count. It updates only after the zone file is saved.
- On the map and minimap, **green rectangles** show saved received data; **amber** shows unsaved cache updates. They do not prove that the server sent every object in a zone.
- Press **F8** again for the final flush. Wait for the saved confirmation before closing the game.
- Each object is recorded once by its source ID. Later visits keep its first saved state. Players and creatures are excluded.
- Keep the whole readable JSON folder in `BepInEx/config/WorldCrawler/worlds/<world name>_<UID>`. A sudden crash can lose data still in memory since the last successful flush.
- Recording supports **0.221.12 and 1.0.x**. The next two steps require **1.0.x**.

### 2. Prepare your local world

- Press **F9** at the main menu. If no local world matches the latest recording, it shows the exact **name and seed** to use.
- Create that world yourself in Valheim, using local saves, then press **F9** again. The mod backs up its metadata and changes only its UID.
- Another **local** world with that UID blocks the change. Cloud worlds do not. Pressing F9 again on the prepared world changes nothing.

### 3. Restore the recorded areas

- Enter the local world and press **F10**. Its **name, seed and UID** must match the recording. Move freely to recorded areas.
- Existing imported objects are left untouched, even if edited. Missing objects are restored; a destroyed imported object can therefore be recreated on a later visit.
- **Amber** means never restored and saved; **green** means restored and saved at least once. Only this zone history is tracked in `restore.json`, beside the exports; no character history or separate object ledger.
- The world saves every **minute** at a safe boundary. Press **F10** again for the final save.
- F10 indexes all prefab types in the recording. Inside recorded zones, those types come from the source files: extra local objects are removed, including trees and rocks.
- Unrecorded zones, prefab types absent from the recording, players, and creatures stay untouched. Merchant exception: restoring a source merchant removes extra copies of that same merchant elsewhere in the world.

## Screenshot

<img src="https://raw.githubusercontent.com/landoria-gaming/Landoria.WorldCrawler/refs/heads/main/assets/world-crawler-map.png" alt="World Crawler map progress" width="500">

[Report a bug](https://github.com/landoria-gaming/Landoria.WorldCrawler/issues).
