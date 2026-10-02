# Passive recording and restoration

## Current behavior

- F8 records detached copies of persistent objects received by the client, including distant records.
- Also sample existing client objects and local-owner changes; copy final state before native pooling clears it.
- Ignore players, creatures, animals, birds and fish.
- Cache by source object ID and sector; flush every 10 seconds and on stop.
- Write a recoverable batch checkpoint, merge into canonical 64 × 64 metre zone files, then update the manifest.
- New arrivals use a separate cache during disk writes. Failed writes retain their batch and retry.
- Repeated recordings merge with existing files by source ID; missing observations are not deletions.
- Every 5 seconds, show session zones saved, distinct object IDs saved, and objects still cached (including writes in progress).
- Count saved objects only after confirmed disk writes. An object with a pending update counts only as cached until that update is committed.
- Map rectangles distinguish cached updates from durable received data.
- F10 restores nearby recorded data without moving or immobilizing the player.
- Incomplete restoration is resumed on a later visit; native saves remain every 120 seconds and on stop.
- F9 still prepares the local world using its native save format.

## Player behavior

- No forced flight, input interception, physics takeover, movement locks or automatic return.
- Keep ordinary walking, sprinting, jumping, swimming, sailing and manual map teleports.
- God, ghost, cold protection and unlimited stamina apply to the local player while the mod is loaded.
- Apply local time of day 0.4 on world arrival; do not change synchronized server time.
- Retain the existing character-marker handling without invoking console cheat commands.

## Data boundaries

- Payload version 3 means received-object cache, not complete-zone coverage.
- Preserve legacy files and read support; merging new receipts keeps prior unseen records.
- Restore received objects idempotently, but disable absence-based cleanup for passive captures.
- Capture static layout opportunistically; an unloaded hierarchy must not block or discard network records.
- Log capture errors, cache counts, actual arrivals, flush failures and successful commits.
- Orderly stop drains the cache. Crashes or power loss can lose the last unflushed in-memory data.
- The client cannot save objects the server never sent. A green rectangle means saved observations, not full server coverage.

## Verification

- Compile against both saved game versions with Harmony Validator.
- No unit-test or audit projects.
- In-game confirmation remains required: leave zones during reception, teleport, repeat F8 after restart, interrupt a flush, then restore with F10.
