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
| **F8** | Start, stop, or resume automatic export. |
| **Left Ctrl+F8** | Start or stop manual export; recapture visited selected zones. |
| **F9** | At the main menu, prepare a local world from the latest export. |
| **F10** | Start, stop, or resume automatic restoration of unfinished zones. |
| **Left Ctrl+F10** | Start or stop manual restoration; redo visited exported zones. |
| **Movement keys** | Fly horizontally in manual mode. |
| **Jump / Crouch** | Fly up / down in manual mode. |
| **Alt + map click** | Teleport when idle or in manual mode. |

## How to use

### 1. Export the server world

Join the source world, then choose:

- **Automatic — F8:** your character flies between the selected areas to capture their data.
- **Manual — Left Ctrl+F8:** you fly or teleport to selected areas to capture them again, even if already saved.
- Areas within **80 m** of discovered portals and landmarks are selected. Add a personal **Home** pin to include a missing revealed area; plain dots and death pins are ignored.
- Press the same shortcut to stop or resume. Zone files are replaced safely, without duplicates.

### 2. Create the local world

At the main menu, press **F9**. Confirm **Yes** to create or reuse a world from the latest export, with the same name, seed, and UID.

### 3. Restore the exported areas

Enter the new local world, then choose:

- **Automatic — F10:** your character flies through unfinished zones to restore their data.
- **Manual — Left Ctrl+F10:** you fly or teleport to exported zones to restore them, even if already restored.
- Press the same shortcut to stop or resume.
- Both modes share progress and reuse objects. Each zone is imported, cleaned, checked, and saved before being marked complete.
- Cleanup keeps creatures, player-owned objects, and types never found anywhere in the export. A safety backup is kept.

## Screenshot

<img src="https://raw.githubusercontent.com/landoria-gaming/Landoria.WorldCrawler/refs/heads/main/assets/world-crawler-map.png" alt="World Crawler map progress" width="500">

[Report a bug](https://github.com/landoria-gaming/Landoria.WorldCrawler/issues).
