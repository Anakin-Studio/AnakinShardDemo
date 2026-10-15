using Sandbox;

namespace AnakinShardDemo;

/// <summary>
/// Mutable per-player test state that drives the transfer gate rules. Host-authoritative
/// (SyncFlags.FromHost); clients read for the HUD.
///
/// Two kinds of fields:
///  - Shared / transferred: <see cref="Handcuffed"/> and <see cref="CarriedItem"/> travel
///    with the player across shards (stored in the backend snapshot, applied on arrival).
///  - Local / transient gate timers: <see cref="LastCombatTime"/> and
///    <see cref="LastTransferTime"/> gate transfers on the current shard only and are not
///    sent to the backend; they reset naturally when the player arrives fresh on a shard.
///
/// Times are <see cref="Time.Now"/> (seconds, this shard's scene clock). Gate windows
/// compare <c>Time.Now - LastX</c> against the rule threshold (combat 5s, cooldown 3s).
/// </summary>
public sealed class PlayerTestState : Component
{
	// Shared (transferred with the player).

	[Sync( SyncFlags.FromHost )] public bool Handcuffed { get; set; }

	/// <summary>Id of the single carried test item, or empty string for none.</summary>
	[Sync( SyncFlags.FromHost )] public string CarriedItem { get; set; } = "";

	// Local gate timers (not transferred).

	[Sync( SyncFlags.FromHost )] public float LastCombatTime { get; set; } = -9999f;
	[Sync( SyncFlags.FromHost )] public float LastTransferTime { get; set; } = -9999f;

	public bool HasCarriedItem => !string.IsNullOrEmpty( CarriedItem );

	/// <summary>Seconds since the player was last in combat on this shard.</summary>
	public float TimeSinceCombat => Time.Now - LastCombatTime;

	/// <summary>Seconds since the player's last transfer attempt handled on this shard.</summary>
	public float TimeSinceTransfer => Time.Now - LastTransferTime;

	/// <summary>Host-only: mark the player as having just taken combat damage.</summary>
	public void MarkCombat()
	{
		if ( !Networking.IsHost )
			return;

		LastCombatTime = Time.Now;
	}

	/// <summary>Host-only: stamp a transfer attempt for the spam cooldown.</summary>
	public void MarkTransferAttempt()
	{
		if ( !Networking.IsHost )
			return;

		LastTransferTime = Time.Now;
	}

	/// <summary>Host-only: apply the transferred fields from a backend snapshot on arrival.</summary>
	public void ApplyFromSnapshot( bool handcuffed, string carriedItem )
	{
		if ( !Networking.IsHost )
			return;

		Handcuffed = handcuffed;
		CarriedItem = carriedItem ?? "";
	}
}
