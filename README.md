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
| **F8** | Start or stop manual recording. |
| **F9** | At the main menu, prepare a local world from the latest recording. |
| **F10** | Start or stop manual restoration. |
| **Movement keys / Jump / Crouch** | Fly horizontally / up / down at sprint ×4. |
| **Alt + map click** | Teleport when data work is idle. |

## How to use

### 1. Record the server world

- Join the source world and press **F8**. Fly wherever you want to copy.
- Nearby loaded zones are recorded, including their buildings, terrain and inventories. No pins or route planning are needed.
- Movement pauses for useful data and validation, then resumes after two quiet seconds. Your character is protected from damage, enemies and cold.
- **Green rectangles** show saved zones. Incomplete observations are never exported.
- Press **F8** to stop in place. Start again later to record more or update visited zones without duplicates.
- Keep the whole recording folder in `BepInEx/config/WorldCrawler/worlds`.
- Recording supports **0.221.12 and 1.0.x**. The next two steps require **1.0.x**.

### 2. Create the local world

At the main menu, press **F9** and confirm **Yes**. The latest recording supplies the world's name, seed and UID. Unrelated worlds are never overwritten.

### 3. Restore the recorded areas

- Enter the new local world and press **F10**. Fly where you want to restore it.
- Nearby recorded zones are restored, cleaned and checked. Revisiting a zone safely reapplies it without duplicates.
- **Amber** means a recorded zone still needs restoration or saving; **green** means restored and saved.
- The world saves every **two minutes** when changes exist, at the next safe boundary. Press **F10** to stop and wait for the final save.
- Movement pauses during actual work, not while waiting for the next scheduled save. No automatic travel or return to the start.
- Safety backups are kept. Cleanup protects creatures and unrelated player builds, and never removes types absent from the recording.

## Screenshot

<img src="https://raw.githubusercontent.com/landoria-gaming/Landoria.WorldCrawler/refs/heads/main/assets/world-crawler-map.png" alt="World Crawler map progress" width="500">

[Report a bug](https://github.com/landoria-gaming/Landoria.WorldCrawler/issues).
