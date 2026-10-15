#nullable enable

using System.Linq;
using System.Threading.Tasks;
using Sandbox;

namespace AnakinShardDemo;

/// <summary>
/// Host-side heartbeat + lookup against the backend server directory. On a timer it POSTs
/// this shard's connect handle (Steam lobby id), player count and capacity so peer shards can
/// find it; the backend expires an entry after ~15s of silence.
/// </summary>
public sealed class ServerDirectory : Component
{
	public static ServerDirectory? Current { get; private set; }

	[Property] public float HeartbeatInterval { get; set; } = 5f;

	private float _nextHeartbeat;

	protected override void OnAwake() => Current = this;

	protected override void OnStart()
	{
		// send an immediate first heartbeat so the shard registers quickly
		_nextHeartbeat = 0f;
	}

	protected override void OnUpdate()
	{
		if ( !Application.IsDedicatedServer )
			return;

		if ( Time.Now < _nextHeartbeat )
			return;

		_nextHeartbeat = Time.Now + HeartbeatInterval;
		_ = SendHeartbeat();
	}

	private async Task SendHeartbeat()
	{
		var ctx = ShardContext.Current;
		if ( ctx?.Backend is null || ctx.Config is null )
			return;

		var handle = ShardNetwork.Current?.ConnectHandle ?? "";
		var players = PlayerCount();

		await ctx.Backend.Heartbeat( handle, players, ctx.Config.Capacity );
		ctx.BackendOk = ctx.Backend.Reachable;   // synced to clients for the HUD
	}

	/// <summary>Number of connected players (dedicated-server host is not a player).</summary>
	public static int PlayerCount() => Connection.All.Count();

	/// <summary>Look up a peer shard's directory entry (used before a transfer).</summary>
	public async Task<LookupResp?> Lookup( string shardId )
	{
		var ctx = ShardContext.Current;
		if ( ctx?.Backend is null )
			return null;

		return await ctx.Backend.Lookup( shardId );
	}
}
