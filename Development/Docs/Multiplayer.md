# Multiplayer

Networking model for SubZeroShardDemo. Read before UI, networking, or scene flow work.

## Model

Dedicated servers only, with a separate backend as the source of truth. There is no listen-server
or self-hosting: `ShardContext` and `ShardNetwork` only act when `Application.IsDedicatedServer`
is true, so a client (or the editor) never hosts a shard and never talks to the backend.

- Shard: one `sbox-server.exe` per shard (`A`, `B`, `C`), same project and same scene, ports
  27015/27017/27019 (query 27016/27018/27020). Launched by `Development/Scripts/run-shard.bat`
  with `+subzero_shard <id>`.
- Backend: `backend/` on `http://localhost:8443`. It holds authoritative player state (money,
  item, cuffs, position, current shard, in-transit marker), the server directory, and the
  transfer state machine. Every request is an HMAC-SHA256 signed envelope
  `{ payload, sig }`; the game signs with the managed `Code/Shared/Hmac.cs`.
- Scene that owns the session: `scenes/subzerosharddemo.scene`, object `Shard` (ShardContext,
  ShardNetwork, ServerDirectory, TransferService).
- Authority: the host (dedicated server) runs gate rules, ledger calls, prepare/accept and state
  application. Clients read `[Sync( SyncFlags.FromHost )]` values for the HUD and may only
  request actions (RPC to host) and reconnect when told.

### Lobby flow and finding a shard

1. Each shard creates a lobby on boot (`Networking.CreateLobby`, required or clients get "Not
   Connected") and tags it `Networking.SetData( "shardid", id )`.
2. A dedicated server cannot read its own lobby id, so the heartbeat `connectHandle` stays empty.
3. To move a player, the source host calls the owner-only RPC `PlayerTransfer.BeginReconnect`.
   The owning client runs `Networking.QueryLobbies`, picks the lobby whose `shardid` tag equals the
   target, and calls `Networking.Connect( lobbyId )`. QueryLobbies is client-side only, which is
   why the client does it.
4. On the destination, `INetworkListener.OnActive` spawns the player and `HandleArrival` asks the
   backend (`/transfer/accept`, keyed by SteamId) whether this is a transfer or a fresh join.

### Heartbeat

`ServerDirectory` POSTs `/directory/heartbeat` every 5 s with shard id, player count, capacity and
version. The backend marks a shard down after 15 s of silence; `prepare` refuses a down or full
destination. Heartbeats are not persisted.

### Transfer (prepare / accept)

Source host: gate rules, optional toll via `/player/apply-ledger`, then `/transfer/prepare`, which
snapshots money, item, cuffs and position into a single-use token with a 30 s TTL and marks the
player `InTransit`. Client reconnects. Destination host: `/transfer/accept` consumes the token
exactly once, flips `CurrentShard`, clears `InTransit`, and returns the snapshot. Expired or
abandoned transfers revert on the backend (sweep every 5 s) and leave money intact. The game never
calls `/transfer/cancel`. Full detail in `Architecture.md`.

### Duplicate login

Rule: newest connection wins. `/player/join` reassigns `CurrentShard`; every 10 s each shard asks
the backend who owns its players and kicks any player now owned elsewhere (not in transit). The
kick call is unverified in game.

## RPCs and sync used

| Member | Direction | Purpose |
| --- | --- | --- |
| `PlayerTransfer.RequestTransferHost( target, rule, toll )` | client to host (`[Rpc.Host]`) | Button press requests a transfer. Rule and toll are client-supplied today (security risk, see TODO). |
| `PlayerTransfer.BeginReconnect( target, reserved )` | host to owner (`[Rpc.Owner]`) | Tell the client to find and connect to the target lobby. |
| `PlayerTransfer.ShowDenied( reason )` | host to owner (`[Rpc.Owner]`) | Show the denial on the HUD. |
| `PlayerWallet.Money`, `PlayerTestState` fields, `ShardContext.ShardId/DisplayName/BackendOk` | `[Sync( FromHost )]` | Read-only on clients. |

## Canonical Gotchas

The shared networking gotchas doc lives at
`C:\Users\Jordan\Documents\s&box projects\1AI DEEP RESEARCH\SBox-Packages\Multiplayer\README.md`.
Key ones that bite every project:

- `[Sync]`, `[Sync(SyncFlags.FromHost)]`, `[Rpc.Broadcast]`, `[Rpc.Host]`, `[Rpc.Owner]` are
  current. `[Broadcast]` and `[Authority]` are obsolete.
- `Networking.IsHost` is true offline too. "Am I in a real session" is `Networking.IsActive`.
- After `NetworkSpawn()`, added components, children, and enable-toggles do not replicate until
  `Network.Refresh()`. Build the whole rig, then spawn once.

Project-specific gotchas (lobby id unreadable headless, blocked `HMACSHA256`, convar timing,
port 8443 allowlist, editor shows `$0`) are in `FieldNotes.md`.

## Required Scene Objects

Each shard's scene must contain: `Shard` (ShardContext, ShardNetwork, ServerDirectory,
TransferService), a disabled `PlayerTemplate` (PlayerWallet, PlayerTestState, PlayerTransfer; `ShardNetwork` finds
it by name when `PlayerPrefab` is unset). Note: `DebugCommands.FindPlayer` looks players up by
`PlayerController`, but the scene file at HEAD contains no PlayerController component, so confirm
the template has one (a debug command finding no player silently does nothing), `SpawnPoint`,
`ArrivalPoint`, and the transfer pads. `ShardNetwork` clones the template per connection and
`NetworkSpawn`s it owned by the connecting client.

## Smoke Test

Never ship UI or networking changes without a client smoke test: host plus one joined client,
exercise the changed flow on both.

For this project the real test is on dedicated shards, not the editor (the editor has no backend
client). Backend only: `powershell -ExecutionPolicy Bypass -File backend\smoke-test.ps1`
(23 checks). Cross-shard in game: start the backend and shards A and B, join A with a client, walk
a pad, confirm arrival on B with money and item intact, and watch the `[transfer]` log lines on
both shards. None of the in-game rows has been recorded as passed yet.
