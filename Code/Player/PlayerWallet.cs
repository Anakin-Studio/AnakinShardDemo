using Sandbox;

namespace AnakinShardDemo;

/// <summary>
/// A player's money. Host-authoritative: only the host may write <see cref="Money"/>
/// (SyncFlags.FromHost), clients read it for the HUD.
///
/// Money is shared state, and the backend is the source of truth. Gameplay paths that spend
/// or grant money must go through the backend ledger (stamped with a TransferId, applied
/// exactly once); see BackendClient.ApplyLedger. This component only mirrors the current
/// authoritative value locally so the HUD can show it; it is set from the backend snapshot
/// on arrival and after each ledger call. Never bump Money directly on a gameplay path.
/// </summary>
public sealed class PlayerWallet : Component
{
	[Sync( SyncFlags.FromHost )] public int Money { get; set; }

	/// <summary>
	/// Host-only: overwrite the local mirror from an authoritative value (backend snapshot
	/// on arrival, or the returned balance after a ledger call). No-op on clients.
	/// </summary>
	public void SetAuthoritative( int money )
	{
		if ( !Networking.IsHost )
			return;

		Money = money;
	}
}
