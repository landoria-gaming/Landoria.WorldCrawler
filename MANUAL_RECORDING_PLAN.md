# Passive recording and restoration

## Current behavior

- F8 records detached copies of persistent objects received by the client, including distant records.
- Also sample existing client objects and local-owner changes; copy final state before native pooling clears it.
- Ignore players, creatures, animals, birds and fish.
- Cache by source object ID and sector; flush every 10 seconds and on stop.
- Write a recoverable batch checkpoint, merge into canonical 64 × 64 metre zone files, then update the manifest.
- New arrivals use a separate cache during disk writes. Failed writes retain their batch and retry.
- Record each source ID once across all zone files. Keep its first saved state and position on every later visit.
- Every 5 seconds, show session zones saved, distinct object IDs saved, and objects still cached (including writes in progress).
- Count saved objects only after confirmed disk writes. New objects count only as cached until committed.
- Map rectangles distinguish cached updates from durable received data.
- F10 restores nearby recorded data without moving or immobilizing the player.
- Incomplete restoration is resumed on a later visit; native saves remain every 60 seconds and on stop.
- F9 selects the latest export and finds a local world with the exact source name and seed. If absent, show the values for the user to create it in Valheim.
- Only change the matching local world's UID after an atomic metadata backup. Keep every other byte and database file unchanged. Refuse another local world with the same target UID; ignore cloud worlds.
- F10 requires exactly the same name, seed and UID. Native generation versions may differ between source and target.
- Imported source IDs remain tagged on native world objects. Never overwrite an existing tagged object; recreate it only if missing.
- Persist only world UID and zones restored at least once in the export folder's `restore.json`. No `_restorations`, `_characters`, character metadata, object ledger or target sidecar.

## Player behavior

- No forced flight, input interception, physics takeover, movement locks or automatic return.
- Keep ordinary walking, sprinting, jumping, swimming, sailing and manual map teleports.
- God, ghost, cold protection, unlimited stamina and map teleportation require `EnableCheats=true`.
- Apply local time of day 0.4 on world arrival; do not change synchronized server time.
- Retain the existing character-marker handling without invoking console cheat commands.

## Data boundaries

- Payload version 3 means received-object cache, not complete-zone coverage.
- Preserve legacy files and read support; merging new receipts keeps prior unseen records.
- At F10 startup, index distinct prefab names and hashes across every validated zone file and keep them in memory.
- Treat source files as authoritative for those prefab types inside exported sectors, including passive recordings. Restore source objects and remove extra destination copies before checkpointing.
- Preserve sectors with no source file, prefab types never exported, players, creatures and the native zone controller.
- Merchant exception: after restoring a source merchant site, remove same-merchant duplicates anywhere in the world while preserving source sites.
- Capture static layout opportunistically; an unloaded hierarchy must not block or discard network records.
- Log capture errors, cache counts, actual arrivals, flush failures and successful commits.
- Orderly stop drains the cache. Crashes or power loss can lose the last unflushed in-memory data.
- The client cannot save objects the server never sent. A green rectangle means saved observations, not full server coverage.

## Verification

- Compile against both saved game versions with Harmony Validator.
- No unit-test or audit projects.
- In-game confirmation remains required: leave zones during reception, teleport, repeat F8 after restart, interrupt a flush, then restore with F10.
