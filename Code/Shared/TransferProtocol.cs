#nullable enable

namespace AnakinShardDemo;

// Game-side mirror of the backend's wire contract (backend/Models.cs). Kept as a parallel
// definition because the game and backend compile into separate assemblies. JSON shapes must
// match; the backend reads case-insensitively and returns camelCase, and the BackendClient
// deserializes case-insensitively, so property casing is not load-bearing.

/// <summary>Signed envelope wrapping every request body.</summary>
public sealed class Envelope
{
	public string Payload { get; set; } = "";
	public string Sig { get; set; } = "";
}

// --- Requests ---
public sealed record PlayerGetReq( string SteamId );
public sealed record PlayerJoinReq( string SteamId, string ShardId );
public sealed record ApplyLedgerReq( string SteamId, string TransferId, int Delta, string Reason );
public sealed record HeartbeatReq( string ShardId, string ConnectHandle, int Players, int Capacity, string Version );
public sealed record LookupReq( string ShardId );
public sealed record PrepareReq( string SteamId, string SrcShard, string DstShard, string TransferId,
	bool Handcuffed = false, string? CarriedItem = null, string Position = "" );
public sealed record AcceptReq( string SteamId, string DstShard );
public sealed record CancelReq( string SteamId, string TransferId );

// --- Responses (classes with settable props for System.Text.Json) ---
public sealed class PlayerDto
{
	public string SteamId { get; set; } = "";
	public int Money { get; set; }
	public string CarriedItem { get; set; } = "";
	public bool Handcuffed { get; set; }
	public string? CurrentShard { get; set; }
	public string? InTransit { get; set; }
	public string Position { get; set; } = "";
}

public sealed class OkResp
{
	public bool Ok { get; set; }
	public string Reason { get; set; } = "";
}

public sealed class LedgerResp
{
	public bool Ok { get; set; }
	public int Balance { get; set; }
	public string Reason { get; set; } = "";
}

public sealed class LookupResp
{
	public string ShardId { get; set; } = "";
	public string ConnectHandle { get; set; } = "";
	public int Players { get; set; }
	public int Capacity { get; set; }
	public bool Up { get; set; }
}

public sealed class PrepareResp
{
	public bool Ok { get; set; }
	public string Reason { get; set; } = "";
	public string TransferId { get; set; } = "";
	public string DstConnectHandle { get; set; } = "";
	public System.DateTimeOffset ExpiresAt { get; set; }
}

public sealed class AcceptResp
{
	public bool Ok { get; set; }
	public string Reason { get; set; } = "";
	public PlayerDto? Snapshot { get; set; }
}
