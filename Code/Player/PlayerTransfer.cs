using Sandbox;

namespace AnakinShardDemo;

/// <summary>
/// Lives on the player prefab. Carries the two host-to-owner RPCs the transfer needs: telling
/// this client to reconnect to another shard, and showing a denial reason on the HUD. The
/// client only ever reconnects; it never talks to the backend (host authority).
/// </summary>
public sealed class PlayerTransfer : Component
{
	// Read by DebugHud on the owning client. Set by the ShowDenied RPC.
	public string LastDenial { get; private set; } = "";
	public float LastDenialAt { get; private set; } = -9999f;

	/// <summary>
	/// Host to owning client: disconnect and connect to the destination shard's lobby. On
	/// failure, fall back to reconnecting to the source shard (the backend auto-reverts the
	/// InTransit lock when the player resurfaces on the source, so money stays intact).
	/// </summary>
	[Rpc.Owner]
	public void BeginReconnect( string targetShardId, string reserved )
	{
		if ( string.IsNullOrEmpty( targetShardId ) )
			return;

		_ = ConnectToShard( targetShardId );
	}

	// Runs on the owning client. QueryLobbies works client-side (not on a dedicated server),
	// so the client discovers the target shard's lobby by its "shardid" tag and connects.
	// Disconnecting from the source and joining the destination is the whole cross-shard hop.
	private async System.Threading.Tasks.Task ConnectToShard( string shardId )
	{
		try
		{
			var lobbies = await Networking.QueryLobbies( default );
			foreach ( var l in lobbies )
			{
				if ( l.Get( "shardid", "" ) == shardId )
				{
					Log.Info( $"[transfer] client connecting to shard {shardId} (lobby {l.LobbyId})" );
					Networking.Connect( l.LobbyId );
					return;
				}
			}
			Log.Warning( $"[transfer] no lobby found for shard {shardId} among {lobbies.Count}" );
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"[transfer] reconnect failed: {e.Message}" );
		}
	}

	/// <summary>
	/// Client to host: request a transfer for this player (fired by a TransitionButton press).
	/// Runs host-side so the gate rules and prepare stay authoritative. Rule is passed as int
	/// to keep the RPC signature simple.
	/// </summary>
	[Rpc.Host]
	public void RequestTransferHost( string targetShard, int rule, int tollAmount )
	{
		_ = TransferService.Current?.RequestTransfer( GameObject, targetShard, (TransferRule)rule, tollAmount );
	}

	/// <summary>Host to owning client: a transfer request was denied; show the reason.</summary>
	[Rpc.Owner]
	public void ShowDenied( string reason )
	{
		LastDenial = reason;
		LastDenialAt = Time.Now;
		Log.Info( $"[transfer] denied: {reason}" );
	}
}
