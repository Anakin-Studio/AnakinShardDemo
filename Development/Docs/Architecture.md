# Architecture

High-level game flow and system ownership for SubZeroShardDemo.

SubZeroShardDemo moves a connected player between separate dedicated s&box servers ("shards"),
carrying money, one carried item, a handcuff flag and a position. Last reviewed against the code
on 2026-10-09. Items marked "unverified" have no live in-game run behind them yet (see
`TEST_RESULTS.md`, local-only).

## Game Flow

Processes: one backend (`backend/`, .NET 10, `http://localhost:8443`) and N shards. Each shard is
its own `sbox-server.exe` running this same project and the same scene
(`scenes/subzerosharddemo.scene`). Shard ids today are exactly `A`, `B`, `C`
(`Code/Shard/ShardContext.cs`).

Shard process (dedicated server only, gated on `Application.IsDedicatedServer`):

1. Boot: the launch arg `+subzero_shard X` selects the shard id (convar, then hostname parse, then
   `A`). `ShardContext.OnStart` loads `ShardConfig` (baked defaults; the JSON file is probably not
   read) and builds a `BackendClient` with the shared HMAC secret.
2. `ShardNetwork.OnStart` creates the Steam lobby and tags it with `shardid` via
   `Networking.SetData`. A dedicated server cannot read its own lobby id, so the tag is how a
   client later finds it.
3. Heartbeat: `ServerDirectory` POSTs `/directory/heartbeat` every 5 s (shard id, player count,
   capacity, version, and a connect handle that is always empty today). The backend treats a
   shard as up for 15 s after its last heartbeat.
4. A client joins. `ShardNetwork.OnActive` spawns a player (clone of the disabled `PlayerTemplate`
   object, `NetworkSpawn( owner )`) and calls `TransferService.HandleArrival`.
5. `HandleArrival` calls `/transfer/accept` by SteamId. If the backend says the player was
   mid-transfer to this shard, the snapshot (money, item, cuffs, position) is applied and the
   player is placed at the carried position. Otherwise it calls `/player/join` and applies the
   authoritative record at the spawn point.
6. Every 10 s `TransferService.ValidateOwnership` asks the backend who owns each local player; a
   player now owned by another shard (and not in transit) is kicked ("newest wins").

Transfer (source shard, then destination shard), see "Transfer flow" below.

## Transfer flow (prepare / accept)

1. The player walks onto a `TransitionZone` (host side trigger) or presses E on a
   `TransitionButton` (client press, routed to the host by `PlayerTransfer.RequestTransferHost`).
2. Source host, `TransferService.RequestTransfer`: gate rules (`EvaluateGate`: handcuffs, 5 s
   combat lock, 3 s cooldown, toll needs $100), then stamps the cooldown.
3. Toll pads: the host charges the toll through `/player/apply-ledger` with the transfer id as the
   idempotency key, before prepare, so the snapshot is post-toll. A failed prepare refunds it under
   `<transferId>:refund`.
4. `/transfer/prepare`: the backend checks the player is on the source shard, not already in
   transit, and that the destination is up (fresh heartbeat) and not full. It snapshots money,
   item, cuffs and position into a single-use signed token (30 s TTL), sets `InTransit`, and
   returns ok. The shard's live cuffs, item and position are written into the authoritative
   record only here.
5. The source host RPCs the owning client `BeginReconnect( targetShard )`. The client runs
   `Networking.QueryLobbies`, finds the lobby whose `shardid` tag matches, and calls
   `Networking.Connect( lobbyId )`. No token or secret travels with the client.
6. Destination host: `OnActive` then `/transfer/accept { steamId, dstShard }`. The backend resolves
   the transfer from the player's `InTransit` record, checks destination and expiry, then in one
   step marks the token consumed, sets `CurrentShard`, clears `InTransit`, and restores the
   snapshot. It returns the authoritative record, which the shard applies.
7. Failure paths: an expired token or a TTL sweep (every 5 s) clears `InTransit` and the player
   stays on the source with money intact; accept on a shard that is not the destination abandons
   the transfer and falls back to a normal join; a repeated accept after consume returns the same
   record without re-applying. `/transfer/cancel` exists but the game never calls it.

Invariant: money and the carried item are never duplicated and never lost. Money changes only
through the backend ledger (idempotent per transfer id); the snapshot is applied once.

## System Ownership

| System | Owner (folder / class) | Notes |
| --- | --- | --- |
| Shard identity and config | `Code/Shard/ShardContext`, `ShardConfig` | Host-only config and `BackendClient`; only id, name, backend status sync to clients. |
| Lobby, player spawn, arrival routing | `Code/Net/ShardNetwork` | `INetworkListener`; creates and tags the lobby; spawns the player; calls `HandleArrival`. |
| Heartbeat and directory lookup | `Code/Net/ServerDirectory` | Every 5 s; `connectHandle` is empty (lobby id unreadable). |
| Backend HTTP client | `Code/Net/BackendClient` | HMAC envelope on every call; returns null on failure, never throws into gameplay. |
| Signing | `Code/Shared/Hmac` | Pure-managed HMAC-SHA256 (sandbox blocks `HMACSHA256`). Must match `backend/Crypto.cs`. |
| Wire DTOs | `Code/Shared/TransferProtocol`, `backend/Models.cs` | Parallel definitions; keep in sync by hand. |
| Gate rules, toll, prepare, arrival, ownership check | `Code/Transfer/TransferService` | Host-authoritative. |
| Reconnect and denial RPCs | `Code/Player/PlayerTransfer` | `[Rpc.Owner]` reconnect and denial, `[Rpc.Host]` button request. |
| Money mirror | `Code/Player/PlayerWallet` | `[Sync( FromHost )]`; set only from backend values. |
| Cuffs, item, combat and cooldown timers | `Code/Player/PlayerTestState` | Cuffs and item travel; timers are shard-local `Time.Now` and do not. |
| Transfer triggers | `Code/World/TransitionZone`, `TransitionButton` | Pad volume or look-and-press; rule and toll are per-object properties. |
| HUD | `Code/UI/DebugHud.razor` | Shows shard banner, money, flags, backend status, denial, look prompt. |
| Debug commands | `Code/Debug/DebugCommands` | Server convars; currently open to all clients (see TODO risks). |
| Shared player state, transfer coordinator, directory | `backend/Store.cs`, `Program.cs` | Single mutex, in-memory plus `backend/data/state.json`; the source of truth. |
| Launch and test tooling | `Development/Scripts/`, `backend/smoke-test.ps1` | Scripts have broken relative paths since the move (see FieldNotes). |

## Scene Structure

One scene, `scenes/subzerosharddemo.scene`, used by every shard (`DedicatedServerStartupScene` in
the `.sbproj`). Objects it must contain: `Shard` (ShardContext, ShardNetwork, ServerDirectory,
TransferService), a disabled `PlayerTemplate` object (found by name; `PlayerPrefab` is left null),
`SpawnPoint`, `ArrivalPoint`, `Floor`, a camera and light, `HUD` (ScreenPanel plus DebugHud), and the
transfer pads (`Pad_Normal_A`, `Pad_Normal_B`, `Pad_Toll_B`, `Pad_Prisoner_C`, all `TransitionZone`
at HEAD; no `TransitionButton` is authored in the scene file). Shards differ only by launch arg, so
all shards share one map and coordinates.

## Rules

- One system owns each behavior. Extend the owner; do not add parallel paths.
- Update this doc when ownership changes.
- The backend is the only authority for shared player state. Shards hold a working copy.
- The client only requests and reconnects. Gate checks, ledger calls, tokens and state
  application run on the host.
- Only a dedicated server is a shard. The editor and clients never build a backend client.

## Guidance for games adopting shards

1. Per-player data authority lives in the backend, keyed by SteamId, never on a shard's disk.
2. One writer per player record: exactly one shard owns a player at a time, and a transfer is the
   hand-off. Reject writes from non-owners (not yet enforced on `/player/apply-ledger`).
3. Per-shard local data goes under a shard-prefixed path such as `shards/<shardId>/` in
   `FileSystem.Data`. The demo writes nothing there; cross-shard state belongs in the backend.
4. Use shard-prefixed ids for anything a shard mints (transfer ids already are `<shard>:<guid>`),
   so two shards can never collide. Today only `A`, `B`, `C` are valid shard ids.
5. Use the backend clock for any time that crosses shards. Shard `Time.Now` values are local.
6. Debug and admin commands are host or staff only; never trust client-supplied amounts, rules or
   targets.
7. Per-shard secrets passed at launch, not committed. The demo shares one committed secret.
8. Persist on change, not only at hand-off, and make every backend mutation idempotent.

See the README for the same list with notes on what the demo implements and what is only a rule.
