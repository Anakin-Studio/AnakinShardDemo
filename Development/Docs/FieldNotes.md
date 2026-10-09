# SubZeroShardDemo Field Notes

What actually held up in this project: engine API corrections, confirmed gotchas, and traps
discovered while building. Modeled on the ZeroLoop field notes
(`SBox-Packages/00-ZEROLOOP-FIELD-NOTES.md`).

Why this doc exists: per-project learnings die with the project unless captured. At milestones (or
when the project ships), fold corrections back into the matching package README or SBox-Skills
topic doc so the next project inherits them.

Last reviewed against the code: 2026-10-09. "Confirmed" means observed on a real run (see
`SPIKE.md`, local-only). "Unverified" means the code assumes it but no live run has shown it.

## Engine API Verification

| API | Status | Notes / source |
| --- | --- | --- |
| `Networking.CreateLobby( LobbyConfig )` headless | Confirmed | Works on a dedicated server; returns `void` (`Code/Net/ShardNetwork.cs`). `SPIKE.md` 2026-07-14. |
| Reading the server's own lobby id | Not possible | `Connection.Local` is null headless and there is no `Networking.Lobby`. See gotcha 1. |
| `Networking.SetData( "shardid", id )` as a lobby tag | Written, unverified live | `Code/Net/ShardNetwork.cs` line ~53. |
| `Networking.QueryLobbies( default )` + `LobbyInformation.Get( "shardid", "" )` | Written, unverified live | Client-side only. `Code/Player/PlayerTransfer.cs` `ConnectToShard`. |
| `Networking.Connect( ulong lobbyId )` | Written, unverified live | Called from the owning client after the host RPC `BeginReconnect`. |
| `System.Security.Cryptography.HMACSHA256` in game code | Blocked (SB1000) | Sandbox whitelist rejects it. See gotcha 2. |
| `Convert.ToHexString` | Allowed | Used by `Code/Shared/Hmac.cs`. |
| `Http.RequestAsync` to `http://localhost:8443` from a headless server | Confirmed | Heartbeats reached the backend (`SPIKE.md`). 8443 is on the localhost allowlist. |
| `[ConVar( "subzero_shard" )]` filled from `+subzero_shard A` before `OnStart` | Unverified | See gotcha 3. |
| `FileSystem.Mounted` can read `config/shard.<id>.json` | Unverified, assume no | See gotcha 5. |
| `Connection.Kick( reason )` | Unverified | Duplicate-login path in `TransferService.ValidateOwnership`. |
| `Application.IsDedicatedServer` | Used as the shard switch | Editor and clients return false, so they never build a backend client. |

## Gotchas

Items 1, 2, 4 and 8 were observed. Items 3 and 5 are flagged because the code assumes behavior no
live run has confirmed. Items 6, 7 and 9 are repo facts verified by reading the code.

1. A dedicated server cannot read its own lobby id. `Networking.CreateLobby` returns `void`,
   `Connection.Local` is null with no local client, and there is no `Networking.Lobby`. So the
   heartbeat `connectHandle` is always an empty string (`ShardNetwork.ConnectHandle` is declared at
   `Code/Net/ShardNetwork.cs:29` but never assigned; `Code/Net/ServerDirectory.cs:48` sends it).
   The working approach is the lobby tag: the shard calls `Networking.SetData( "shardid", id )`,
   and the client discovers the lobby with `QueryLobbies` (client-side only) and connects. The
   backend's `DstConnectHandle` and `Lookup.ConnectHandle` are therefore dead fields. Source:
   `SPIKE.md` "KEY BLOCKER", `Code/Net/ShardNetwork.cs:59`, `Code/Player/PlayerTransfer.cs:33`.

2. `HMACSHA256` is blocked in the s&box code sandbox (analyzer SB1000), so the game signs with a
   pure-managed SHA-256 and HMAC in `Code/Shared/Hmac.cs`. It must stay byte-identical to the
   backend's `HMACSHA256` (`backend/Crypto.cs`), and output must be lowercase hex. The backend
   verifies the signature over the received `payload` string without re-serializing, so JSON
   casing differences do not break auth. If you touch `Hmac.cs`, re-verify a signature against
   the backend before trusting it.

3. Convar timing is unverified. `ShardContext.ShardArg` (`[ConVar( "subzero_shard" )]`) is read in
   `OnStart` (`Code/Shard/ShardContext.cs`), on the assumption that launch params are applied as
   convars before the scene starts. The code falls back to parsing the hostname ("SubZero Shard
   B - Industrial") and then to `A`. A silent fallback to `A` on a mis-timed convar would put a
   shard in the wrong identity, so check each shard's `[shard] DEDICATED shard X` log line on boot.
   `Normalize` also maps any id other than `A`, `B`, `C` to `A`.

4. Port 8443 is chosen because it is on s&box's localhost HTTP allowlist (80, 443, 8080, 8443),
   and because 8080 is commonly occupied (NVIDIA Broadcast here). The backend default and the
   game's baked `BackendUrl` both use 8443 (`backend/Program.cs:13`, `Code/Shard/ShardConfig.cs`).
   `run-shard.bat` also passes `-allowlocalhttp`, but the 2026-07-14 spike showed heartbeats work
   without it. Do not move the backend to a port outside the allowlist without that flag.

5. Config JSON files are probably not read at runtime. `ShardConfig.Load` calls
   `FileSystem.Mounted` for `config/shard.<id>.json`, but the folder is at the project root, not
   under `Assets/`. The log lines to look for are `[shard] loaded config from ...` versus
   `[shard] using baked default config`. The baked `ShardConfig.Default` values duplicate the
   JSON, so behavior is the same either way. The README "Configuration" section has the details.

6. In the editor the HUD shows `$0`. `ShardContext.OnStart` and `ShardNetwork.OnStart` return
   early unless `Application.IsDedicatedServer`, so the editor builds no `BackendClient`,
   `TransferService.HandleArrival` returns at its null-backend check, and `PlayerWallet.Money`
   keeps its default of 0. This is expected, not a bug. Test money and transfers against dedicated
   shards. (The header of the local `TODO.md` still says the editor session shows `$500`; that
   note predates the dedicated-only gating.)

7. The launch scripts moved from `scripts/` to `Development/Scripts/`, but `run-shard.bat` and
   `run-backend.bat` still use `%~dp0..` for the project and backend paths, which now resolves to
   `Development/` instead of the repo root. They need `..\..`. The script headers,
   `setup-steamclient.ps1` and older docs still say `scripts\...`.

8. The server is `sbox-server.exe` from the s&box install itself
   (`...\steamapps\common\sbox\sbox-server.exe`), no SteamCMD needed. Three anonymous servers plus
   one client on one machine and Steam account is the unproven part; `+net_allow_local 1` is the
   fallback. Port 27015 may be held by other software over TCP, but the game server uses UDP so it
   coexists (`SPIKE.md`).

9. Four working docs are gitignored and therefore untracked: `SPIKE.md`, `EDITOR_NOTES.md`,
   `TODO.md`, `TEST_RESULTS.md` (`.gitignore` lines 28 to 31, unanchored basenames, so they match
   anywhere in the tree). Findings written there are not in version control. Move anything that
   must be shared into a tracked doc such as this one.

## Corrections To Fold Back

- SBox-Skills networking topic: "a dedicated server cannot read its own lobby id; tag the lobby
  with `Networking.SetData` and let the client find it with `QueryLobbies`" (gotcha 1).
- SBox-Skills / package sandbox notes: `HMACSHA256` is blocked, use a managed implementation
  (gotcha 2).
- Package README for the shard pattern: only `Application.IsDedicatedServer` builds backend
  clients; the editor is never a shard (gotcha 6).
- Keep the unverified rows above visible until a live dedicated run confirms them.
