#nullable enable

using System.Threading.Tasks;
using Sandbox;

namespace AnakinShardDemo;

/// <summary>
/// Host-side transfer coordinator. Two flows:
///  - RequestTransfer (source): gate rules, toll ledger, prepare, then tell the client to
///    reconnect to the destination lobby.
///  - HandleArrival (destination): ask the backend whether this arrival is a transfer (accept)
///    or a fresh join, then apply the authoritative snapshot.
///
/// Everything here is host-authoritative; the client only reconnects and shows denials.
/// The money/item invariant is protected by the backend: the snapshot is taken at prepare
/// (after any toll), applied once at accept, and a failed transfer falls back to a join that
/// reloads the same authoritative record, so state is never duplicated and never lost.
/// </summary>
public sealed class TransferService : Component
{
	public const float CombatLockSeconds = 5f;
	public const float CooldownSeconds = 3f;

	public static TransferService? Current { get; private set; }

	private float _nextOwnershipCheck;

	protected override void OnAwake() => Current = this;

	// Destination side: a player just became active on this shard.
	public async Task HandleArrival( Connection conn, GameObject player )
	{
		var ctx = ShardContext.Current;
		if ( ctx?.Backend is null )
			return;

		var steamId = conn.SteamId.ToString();
		var wallet = player.Components.Get<PlayerWallet>();
		var state = player.Components.Get<PlayerTestState>();

		var accept = await ctx.Backend.Accept( steamId );
		if ( accept is { Ok: true, Snapshot: not null } )
		{
			Apply( wallet, state, accept.Snapshot );
			PlaceArrived( player, accept.Snapshot.Position );
			Log.Info( $"[transfer] {steamId} ARRIVED via transfer on {ctx.ShardId} " +
				$"(money={accept.Snapshot.Money}, item={accept.Snapshot.CarriedItem}, pos={accept.Snapshot.Position})" );
			return;
		}

		// Not a transfer (or it was abandoned): normal join.
		var rec = await ctx.Backend.PlayerJoin( steamId );
		if ( rec is not null )
			Apply( wallet, state, rec );
		ShardNetwork.Current?.PlaceAt( player, arrival: false );
		Log.Info( $"[transfer] {steamId} joined {ctx.ShardId} normally (money={rec?.Money})" );
	}

	private static void Apply( PlayerWallet? wallet, PlayerTestState? state, PlayerDto dto )
	{
		wallet?.SetAuthoritative( dto.Money );
		state?.ApplyFromSnapshot( dto.Handcuffed, dto.CarriedItem );
	}

	// Arrive where you left off: place the player at the position carried in the snapshot
	// (all shards share the scene, so the same coords map to the same place). Falls back to
	// the arrival point if there is no carried position.
	private static void PlaceArrived( GameObject player, string position )
	{
		if ( !string.IsNullOrEmpty( position ) )
		{
			try
			{
				player.WorldPosition = Vector3.Parse( position );
				return;
			}
			catch ( System.Exception ) { }
		}

		ShardNetwork.Current?.PlaceAt( player, arrival: true );
	}

	// Source side: player walked into a transition zone (or pressed a button).
	public async Task RequestTransfer( GameObject player, string targetShard, TransferRule rule, int tollAmount )
	{
		if ( !Networking.IsHost )
			return;

		var ctx = ShardContext.Current;
		var transfer = player.Components.Get<PlayerTransfer>();
		if ( ctx?.Backend is null || ctx.Config is null )
		{
			transfer?.ShowDenied( "backend unavailable" );
			return;
		}

		var owner = player.Network.Owner;
		if ( owner is null )
			return;
		var steamId = owner.SteamId.ToString();
		var wallet = player.Components.Get<PlayerWallet>();
		var state = player.Components.Get<PlayerTestState>();

		// Gate rules.
		var (ok, reason) = EvaluateGate( state, wallet, rule, targetShard, ctx.ShardId );
		if ( !ok )
		{
			Log.Info( $"[transfer] {steamId} {ctx.ShardId}->{targetShard} DENIED: {reason}" );
			transfer?.ShowDenied( reason );
			return;
		}

		state?.MarkTransferAttempt();   // cooldown stamp, even on later failure (anti-spam)

		var transferId = $"{ctx.ShardId}:{System.Guid.NewGuid():N}";

		// Toll: deduct exactly once before prepare so the snapshot reflects post-toll money.
		if ( rule == TransferRule.Toll )
		{
			var led = await ctx.Backend.ApplyLedger( steamId, transferId, -tollAmount, "toll" );
			if ( led is null || !led.Ok )
			{
				transfer?.ShowDenied( led?.Reason ?? "toll failed" );
				return;
			}
			wallet?.SetAuthoritative( led.Balance );
		}

		// Prepare (backend validates on-src, not-in-transit, dst up and not full).
		// Report the shard's live cuffs/item so they travel with the player.
		var prep = await ctx.Backend.Prepare( steamId, targetShard, transferId,
			state?.Handcuffed ?? false, state?.CarriedItem ?? "", player.WorldPosition.ToString() );
		if ( prep is null || !prep.Ok )
		{
			// refund the toll (distinct ledger id, so it isn't deduped against the charge)
			if ( rule == TransferRule.Toll )
			{
				var refund = await ctx.Backend.ApplyLedger( steamId, transferId + ":refund", tollAmount, "toll-refund" );
				if ( refund is not null )
					wallet?.SetAuthoritative( refund.Balance );
			}
			transfer?.ShowDenied( prep?.Reason ?? "backend unreachable" );
			return;
		}

		// Tell the client to reconnect; the client resolves the target shard's lobby.
		Log.Info( $"[transfer] {steamId} {ctx.ShardId}->{targetShard} prepared {transferId}; reconnecting client" );
		transfer?.BeginReconnect( targetShard, "" );
	}

	/// <summary>Evaluate the transfer gate rules. Returns (allowed, denialReason).</summary>
	public static (bool ok, string reason) EvaluateGate(
		PlayerTestState? state, PlayerWallet? wallet, TransferRule rule, string targetShard, string srcShard )
	{
		if ( state is null || wallet is null )
			return (false, "player state missing");

		// Handcuffed players may only use a Prisoner pad.
		if ( state.Handcuffed && rule != TransferRule.Prisoner )
			return (false, "handcuffed - jail only");

		if ( rule == TransferRule.Prisoner )
		{
			// Prisoner pad requires being handcuffed; routes to Jail (C).
			return state.Handcuffed
				? (true, "")
				: (false, "not handcuffed");
		}

		// Normal / Toll: combat lock + cooldown.
		if ( state.TimeSinceCombat < CombatLockSeconds )
			return (false, $"in combat ({CombatLockSeconds - state.TimeSinceCombat:0.0}s)");

		if ( state.TimeSinceTransfer < CooldownSeconds )
			return (false, $"cooldown ({CooldownSeconds - state.TimeSinceTransfer:0.0}s)");

		if ( rule == TransferRule.Toll && wallet.Money < 100 )
			return (false, "need $100");

		return (true, "");
	}

	// Duplicate-login guard (rule: newest connection wins). Periodically the shard asks the
	// backend who owns each local player; if the backend says a player now belongs elsewhere
	// (and is not mid-transfer), this stale shard kicks them.
	protected override void OnUpdate()
	{
		if ( !Application.IsDedicatedServer )
			return;
		if ( Time.Now < _nextOwnershipCheck )
			return;
		_nextOwnershipCheck = Time.Now + 10f;
		_ = ValidateOwnership();
	}

	private async Task ValidateOwnership()
	{
		var ctx = ShardContext.Current;
		if ( ctx?.Backend is null )
			return;

		foreach ( var conn in Connection.All )
		{
			var rec = await ctx.Backend.PlayerGet( conn.SteamId.ToString() );
			if ( rec is null )
				continue;

			if ( rec.CurrentShard is not null && rec.CurrentShard != ctx.ShardId
				&& string.IsNullOrEmpty( rec.InTransit ) )
			{
				Log.Info( $"[transfer] {conn.SteamId} now owned by {rec.CurrentShard}, kicking (newest wins)" );
				try { conn.Kick( "signed in on another shard" ); } catch { }
			}
		}
	}
}
