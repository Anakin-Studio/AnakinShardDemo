namespace AnakinShardBackend;

// Wire envelope (all requests). payload is the JSON of the real request DTO,
// sig is HMAC-SHA256(secret, payload).
public sealed record Envelope( string Payload, string Sig );

// Domain state (persisted).

/// <summary>Authoritative shared player state, keyed by SteamId64.</summary>
public sealed class PlayerRecord
{
	public string SteamId { get; set; } = "";
	public int Money { get; set; }
	public string CarriedItem { get; set; } = "";
	public bool Handcuffed { get; set; }

	/// <summary>Last exit position "x,y,z", carried across shards so you arrive where you left.</summary>
	public string Position { get; set; } = "";

	/// <summary>"A"/"B"/"C" or null if not currently on any shard.</summary>
	public string? CurrentShard { get; set; }

	/// <summary>TransferId the player is mid-transfer under, or null.</summary>
	public string? InTransit { get; set; }

	/// <summary>Ledger transferIds already applied (exact-once money deltas).</summary>
	public HashSet<string> AppliedTransferIds { get; set; } = new();
}

/// <summary>A prepared transfer, single-use token record. Lives server-side only.</summary>
public sealed class TransferToken
{
	public string TransferId { get; set; } = "";
	public string SteamId { get; set; } = "";
	public string SrcShard { get; set; } = "";
	public string DstShard { get; set; } = "";

	// Snapshot taken at prepare time.
	public int Money { get; set; }
	public string CarriedItem { get; set; } = "";
	public bool Handcuffed { get; set; }
	public string Position { get; set; } = "";

	public DateTimeOffset IssuedAt { get; set; }
	public DateTimeOffset ExpiresAt { get; set; }
	public string Nonce { get; set; } = "";
	public bool Consumed { get; set; }
	public bool Expired { get; set; }

	/// <summary>HMAC over the compact token payload, proves the backend issued it.</summary>
	public string Signature { get; set; } = "";
}

/// <summary>Server directory entry, refreshed by heartbeats.</summary>
public sealed class ShardEntry
{
	public string ShardId { get; set; } = "";
	public string ConnectHandle { get; set; } = "";   // Steam lobby id (runtime)
	public int Players { get; set; }
	public int Capacity { get; set; }
	public string Version { get; set; } = "";
	public DateTimeOffset LastHeartbeat { get; set; }
}

// Request DTOs (the decoded `payload`).
public sealed record PlayerGetReq( string SteamId );
public sealed record PlayerJoinReq( string SteamId, string ShardId );
public sealed record ApplyLedgerReq( string SteamId, string TransferId, int Delta, string Reason );
public sealed record HeartbeatReq( string ShardId, string ConnectHandle, int Players, int Capacity, string Version );
public sealed record LookupReq( string ShardId );
// Handcuffed/CarriedItem are the source shard's live values (the shard is authoritative for
// them while the player is on it); the backend snapshots them here. CarriedItem null = leave
// unchanged. Money is not passed; it stays ledger-authoritative in the backend.
public sealed record PrepareReq( string SteamId, string SrcShard, string DstShard, string TransferId,
	bool Handcuffed = false, string? CarriedItem = null, string Position = "" );
// No TransferId: the dst shard only knows the arriving SteamId. The backend resolves the
// transfer via the player's own InTransit record (minted on the source shard at prepare).
public sealed record AcceptReq( string SteamId, string DstShard );
public sealed record CancelReq( string SteamId, string TransferId );

// Response DTOs.
public sealed record PlayerDto( string SteamId, int Money, string CarriedItem, bool Handcuffed,
	string? CurrentShard, string? InTransit, string Position = "" );

public sealed record OkResp( bool Ok, string Reason = "" );
public sealed record LedgerResp( bool Ok, int Balance, string Reason = "" );
public sealed record LookupResp( string ShardId, string ConnectHandle, int Players, int Capacity, bool Up );
public sealed record PrepareResp( bool Ok, string Reason, string TransferId, string DstConnectHandle,
	DateTimeOffset ExpiresAt );
public sealed record AcceptResp( bool Ok, string Reason, PlayerDto? Snapshot );
