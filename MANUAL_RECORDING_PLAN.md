# Manual recording and restoration

Status: manual-only refactor implemented in source. Compilation and Harmony target validation pass against both supplied game versions. In-game validation is still required; no unit-test or audit project was created or run.

## Compatibility decisions

- New recordings use manifest and snapshot version 2 in the world's unscoped UID/seed folder. Existing landmark folders, captures, prepared worlds and journals are not rewritten or converted.
- Legacy complete snapshots remain readable for F9/F10. Their old flags are not upgraded into new coverage evidence; absence-based cleanup is disabled in those sectors. Missing terrain, stability or dungeon evidence rejects a source file before destination mutation.
- The remaining landmark DTOs exist only to read legacy manifests. No landmark selection, portal discovery, route, automatic flight or Ctrl mode remains.
- Native explicit-destruction events are retained in validated zone snapshots. Export reading resolves source IDs globally and filters older observations superseded by explicit removals or observed sector departures. Departures never fabricate a complete destination-zone file.
- Object fingerprints ignore ownership/revision counters. Fire clocks and fractional fuel consumption do not repeatedly stop movement; each full snapshot still saves their unmodified current values.
- The server has no complete-zone acknowledgment. Version 2 means validated local near coverage, instances, scene and stable observations, not an authoritative server snapshot.
- During F10, only validated zones without unresolved captured links enter a save batch. Unresolved links remain amber and are retried as more source objects become available.
- Available restore files are frozen for each F10 session under an export lock. Stop/restart F10 to load new captures or changed revisions safely.
- No native final save can be guaranteed after a crash or disconnection. F10's ordinary stop waits for its native-save checkpoint.

## Target behavior

| Key | Behavior |
| --- | --- |
| **F8** | Start or stop continuous recording while the player moves manually. |
| **F9** | Keep current behavior: prepare a local world from the latest export at the main menu. |
| **F10** | Start or stop continuous restoration while the player moves manually. |
| **Movement / Jump / Crouch** | Steer manual flight, rise, and descend. |
| **Alt + map click** | Player-requested teleport when data work is idle; the session stays active. Block the click while movement is locked. |

- Remove automatic piloting, route planning, and the separate Ctrl+F8 / Ctrl+F10 modes.
- Remove landmark inventories, portal searches, the 80 m selection radius, and explored-map or pin filters.
- The player chooses where to go. Never choose a destination, teleport automatically, or return to the start.
- Keep manual flight, sprint ×4, god, ghost, cold protection, and suppressed cold notifications.
- Lock manual movement only for actual export or restoration work and the loading it needs. Release when that work is finished and 2 quiet seconds have passed since the last accepted change. Network traffic alone is not a pause trigger.
- Keep export and restoration mutually exclusive. No admin access or server installation is required for recording; restoration stays restricted to the prepared local world.
- During F10, save the native world every **120 seconds** when changes exist, and once more when restoration stops. This does not change F8's per-zone export-file commits.

## 1. F8: record received world data

- Read the connected world's name, seed, UID, and generation version. Open its recording folder without creating a route or planned zone list.
- Include eligible server state already loaded when F8 starts; do not wait for unchanged objects to be sent again.
- Observe newly received objects and updates after the game's deserialization. Group them by their actual **64 × 64 m** zone, including neighboring zones, not just the player's current zone.
- Use source object IDs and revisions to update existing records. Keep one canonical file per zone; revisits update it without duplicating objects.
- Record confirmed server deletions separately. An object leaving view, losing its local instance, or unloading is **not** proof of deletion. Moving objects must not remain duplicated in two zone files.
- Preserve buildings, inventories, signs, displays, terrain changes, resource state, and received dungeon data. Keep excluding players, creatures, animals, birds, and fish.
- Network packets alone do not contain every static scene detail. Reuse the existing recorder to supplement received state with loaded terrain and generated-location layout when available.
- Collect changed zones incrementally in memory, but commit only validated complete snapshots, atomically. On stop or disconnect, preserve committed files and report unfinished zones; never flush an incomplete zone as an export.
- Do not scan the whole world or request extra remote zones. Coverage follows what the client actually receives and loads during manual travel.

### Movement lock while data is pending

- Check already loaded data at startup and hold only while there is real capture or validation work. Once idle, let the player steer; freeze at the current position when an accepted object actually needs to be recorded, or an active capture requires loading or validation.
- Decide whether to hold **after filtering and deduplication**: the object must be included by the export rules and be new or have changed exported state not yet committed. A received packet or a transport-only revision change is not enough.
- Do not hold or reset the timer for excluded players/creatures/animals, duplicate packets, unchanged exported state, or repeated scans of the same queued object. Routine updates count only if they change data the recorder will actually save.
- Block horizontal and vertical movement, and mod-controlled manual teleport requests, throughout loading, capture, completeness checks, and atomic writes. Do not move the player to a different position or height.
- Enqueuing a new or changed record to save resets the 2-second quiet timer; merely seeing it again does not. An expired timer does not unlock movement while loading needed by the active capture, completeness checks, or its write is still pending.
- Watch the actual near-zone loading scope. Distant-only records cannot produce a complete export and must not cause an endless wait for terrain the game is not loading.
- Show the reason for the hold and pending work in the HUD. If a load stalls, warn and keep protection; never declare completion just to release movement. F8 remains available to explicitly stop recording without exporting incomplete observations.
- After a permitted teleport, lock again at the destination while its data loads. Do not queue a rejected teleport for a surprise jump after loading.
- Apply the same gate to F10 during active restoration and cleanup, and when a native save/checkpoint is actually running. Unsaved completed zones awaiting the 120-second timer must not keep movement locked. Keep stop controls available and leave unloaded cross-zone link targets pending for later manual travel.
- Forced movement from the server or another mod, disconnection, and explicit user cancellation remain interruption cases. Handle them without partial exports or false completion; never treat them as normal early departure.

### No partial zone exports

- Incoming packets are observations in memory, not exported zone files. A first object, a distant object, or 2 quiet seconds must not mark a whole zone as complete.
- Accept a snapshot only after the whole zone is inside actual near-zone coverage, terrain and instances are ready, generated scenes and expected dungeon data are available, and repeated observations are stable with no relevant pending work.
- Read the game's real loading parameters; do not hardcode a 320 m square or use the current 128 m receive filter as an export boundary.
- Normal movement and mod-controlled teleports cannot leave an active capture before validation. If forced movement, disconnection, or cancellation interrupts it, invalidate its completeness and revalidate during a later visit. Do not save it or draw an exported rectangle. Already validated independent snapshots may finish their atomic write.
- During a recapture, retain the previous valid file and its saved rectangle until a fully validated replacement is committed. Never replace it with fewer objects merely because the zone unloaded.
- If a zone is empty, require the same full-zone loading and validation evidence before committing an empty snapshot.
- No partial files, partial restoration mode, or partial map rectangles. Report unfinished zones in the HUD/log instead.
- Client readiness is not a server-side end-of-transfer acknowledgment. Investigate whether the protocol exposes such evidence; if it does not, state that local validation cannot mathematically guarantee every server record was delivered. Never claim that a quiet timer alone proves completeness or absence.

## 2. F10: restore around the manually moved player

- Read available zone files and intersect them with genuinely loaded, ready destination zones around the player. This is a work queue, not a movement route.
- Restore eligible zones in small batches without moving the player. No source file means no import and no cleanup there.
- Keep idempotent object identities, old-to-current item conversion, terrain restoration, and links between objects. Resolve cross-zone links when their other endpoint becomes available.
- Allow a new visit to restore a zone again, including an already restored zone. Avoid repeatedly rewriting the same unchanged zone while the player stands still.
- Block user travel during restoration work. If an external teleport or forced unload still occurs, pause mutations, preserve unfinished progress, and resume safely when the zone becomes available again.
- Process each zone as **restore → permitted cleanup → validate → applied, awaiting save**. Batch native world saves instead of saving after every zone.
- Reject incomplete source captures without changing the destination zone. Keep unfinished terrain, scene, dungeon, or link restoration work visible as pending until its valid source data can be fully applied.
- Retain the global cleanup allowlist: never delete a type absent from every file in the selected export. Also retain actor, player-owned, source-match, and coverage protections.
- Tie restored status to the zone file's committed revision. A newer capture makes that zone pending again, without changing stable object identities.
- On F10 stop, finish or safely suspend the current zone, flush outstanding world changes, and wait for confirmed save/checkpoint completion before reporting the session stopped. Never return the player to another location. Keep world and character backups.

### F10 save schedule and recovery

- Request a native world save every **120 seconds** while F10 is active, including when waiting for the next visited zone. Skip writes when restoration made no changes since the last successful save.
- At a due save, briefly suspend new mutations at a safe boundary. Complete the save, then atomically checkpoint only validated zones included in that save. Never run overlapping saves.
- Movement may resume after each zone's work and the quiet pause; do not hold the player merely because the next scheduled save is not due yet.
- F10 stop requests a final save immediately, without waiting for the timer. Coordinate with an already running native/autosave rather than launching a competing write.
- Keep in-memory applied state separate from durable completion. Preserve recoverable identity/journal information, but never mark a zone durably restored before its native save succeeds.
- After a crash, work since the last successful save may need replay. A crash between world save and journal checkpoint must also recover without duplicates, using existing source identity tags.
- If a save fails, warn, keep the changes pending, and do not claim completion. A forced process exit or disconnect cannot guarantee the final save; show the last confirmed save rather than promising a strict maximum loss of two minutes.

## Map and progress

| Context | Rectangle meaning |
| --- | --- |
| F8 recording | Green: a validated complete zone snapshot has been committed. No rectangle for incomplete observations, planned zones, or unseen zones. |
| F10 restoration | Amber: an available file still needs restoration or its applied changes await a save. Green: its current revision was restored, validated, and included in a successful native save/checkpoint. |
| Incomplete restoration | Keep amber with a warning; do not mark unfinished work as restored. |

- Use exact zone boundaries: center `(64*x, 64*z)`, edges ±32 m. Overlay state comes from committed files and the restoration journal, not travel distance.
- Keep the overlay readable while running and after stopping or restarting. Ignore missing, corrupt, or temporary files and report them.
- F8 shows saved-zone count, pending writes, and warnings; no percentage because there is no planned total.
- F10 may show saved / available files and a percentage, plus applied zones awaiting save and the last successful save. Reload available files and their revisions when a new F10 session starts.

## Code impacts

| Area | Work |
| --- | --- |
| `Plugin`, `ShortcutInput`, `RestoreShortcutPolicy` | Route F8/F10 directly to their single manual mode; remove automatic and Ctrl variants. |
| `CrawlController.*`, `CrawlPhase`, `ExportPreparation` | Replace route-driven capture with recording, flushing, and stopped states; start immediately without landmark discovery. |
| `Inventory/*`, `Landmark*`, `ExportSelection`, `SelectionReport`, `SelectionRecovery`, `PortalRouteMetric` | Remove obsolete planning code and its callers after checking remaining dependencies. |
| `ContinuousCaptureFlight`, `RestoreFlightNavigator`, automatic landing and movement checks | Remove automatic travel, test moves, route recovery, and automatic height control. Retain only manual motion and safety handling. |
| `CaptureReceivePatch`, `CaptureReceiveWatch`, `CaptureReceivePolicy` | Replace the single active-zone observer with multi-zone received-state tracking; include useful revisions and confirmed deletion events. |
| `ZoneCaptureSession`, `ObjectCapture`, `SceneCaptureCursor` | Reuse serialization and scene capture; separate incremental observations from completeness validation. Remove unconditional completeness claims. |
| `WorldStore`, `WorldManifest`, `ZoneEntry`, `StoreValidation` | Register committed zones dynamically; require full-snapshot validation before writes, store revisions and deletion evidence, and remove mandatory landmark inventory and radius scope. |
| `SessionCheckpoint`, `RestoreState` | Remove obsolete route and return-position state; retain crash-safe operation and write progress. |
| `RestorationController.*`, `RestoreSession`, `RestoreJournal` | Use a manual loaded-zone work queue, separate applied and saved progress, and add 120-second native saves plus a final stop save with revision-aware checkpoints. |
| `ExportArchive`, `PreparedWorld`, `ExportSeries`, `LatestExport`, `WorldPreparation` | Accept incremental recordings without a completed inventory; preserve identity checks and safe F9 create/reuse behavior. |
| `GeneratedCleanup`, `CleanupSourceIndex` | Require trustworthy coverage or explicit deletion evidence; reject incomplete source captures before any cleanup. |
| `FlightController.Manual`, `ReceiveMotionGate`, `MapTeleportPatch`, protection patches | Trigger holds from actual accepted work, not raw receive notifications. Keep protections and stop controls. In F10, pause for active work/saves, never for the idle wait until the next scheduled save. |
| `ExportMapOverlay*`, `ProgressMapSnapshot`, `RestorationMapSource` | Build overlays from actual valid files and saved import revisions; remove landmark, character-map, and radius assumptions. |
| `README.md`, HUD, logs | Describe only F8 recording, F9 preparation, and F10 restoration. Remove automatic-route wording and planned-export percentages. |

## Storage and compatibility

- Keep the world-identity folder under `BepInEx/config/WorldCrawler/worlds`, one manifest, and one canonical file per zone. Do not create an archive or files in `C:\worlds`.
- Version any changed snapshot semantics, especially completeness evidence and deletions. Do not silently treat older completeness flags as stronger proof than they provide; reject captures that cannot meet the new acceptance rules.
- Preserve existing exports and journals. Decide the minimal safe old-format read support before implementation; no automatic conversion, deletion, or forced restart is authorized by this plan.
- Keep export support for **0.221.12 and 1.0.x**; F9 and restoration remain **1.0.x** only. Do not resolve restoration-only APIs during legacy recording startup.
- Keep English UI and production constants. Do not recreate the deleted test or audit projects; retain Harmony Validator at compilation.

## Implementation order

1. Define recording revisions, strict completeness checks, deletion evidence, and format compatibility.
2. Implement multi-zone observation and complete-snapshot-only atomic commits; remove export selection and navigation.
3. Update export rectangles and recording counters.
4. Simplify F10 to continuous manual restoration with safe cleanup, 120-second/final native saves, and revision-aware map status.
5. Remove obsolete controls, planners, recovery state, and UI; update the README.
6. Compile for the supported games and check manually in game: F8 after objects are already loaded, neighboring zones, revisits, holds for actual new/changed records, no holds for excluded or unchanged records, movement/teleport blocking until pending work finishes, release after quiet time, forced interruptions, stop/restart, F9, repeated F10, and missing or corrupt files. Confirm excluded actors and unknown scenery types remain untouched.
7. Check F10 save timing, movement between scheduled saves, final save on stop, save failure reporting, and idempotent recovery before/after native save and journal checkpoint.

Implemented in source; game saves and existing exports were not modified. In-game validation remains to be done after deployment.
