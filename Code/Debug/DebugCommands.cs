#nullable enable

using System.Linq;
using System.Threading.Tasks;
using Sandbox;

namespace AnakinShardDemo;

/// <summary>
/// Debug console commands to drive the tests. All run host-side (ConVarFlags.Server) and act
/// on the caller's player, so state changes stay host-authoritative.
///
/// Usage from the client console (or dedicated-server console for the host):
///   anakin_setmoney 500      set the caller's money (routed through the backend ledger)
///   anakin_cuffs 1           handcuff (1) / uncuff (0)
///   anakin_item briefcase    set the carried item ("" for none)
///   anakin_combat            mark just-took-damage (starts the 5s combat lock)
///   anakin_transfer B        force a Normal transfer to shard B/C/A
///   anakin_info              print shard and player state to the console
/// </summary>
public static class DebugCommands
{
	[ConCmd( "anakin_setmoney", ConVarFlags.Server )]
	public static void SetMoney( Connection caller, int amount )
	{
		var go = FindPlayer( caller );
		if ( go is null ) return;
		_ = SetMoneyAsync( caller.SteamId.ToString(), go, amount );
	}

	private static async Task SetMoneyAsync( string steamId, GameObject go, int amount )
	{
		var backend = ShardContext.Current?.Backend;
		var wallet = go.Components.Get<PlayerWallet>();
		if ( backend is null || wallet is null ) return;

		var rec = await backend.PlayerGet( steamId );
		var current = rec?.Money ?? wallet.Money;
		var delta = amount - current;
		var led = await backend.ApplyLedger( steamId, $"debug:setmoney:{System.Guid.NewGuid():N}", delta, "debug-setmoney" );
		if ( led is not null && led.Ok )
			wallet.SetAuthoritative( led.Balance );
		Log.Info( $"[debug] setmoney {steamId} -> {led?.Balance}" );
	}

	[ConCmd( "anakin_cuffs", ConVarFlags.Server )]
	public static void Cuffs( Connection caller, int on )
	{
		var state = FindPlayer( caller )?.Components.Get<PlayerTestState>();
		if ( state is null ) return;
		state.Handcuffed = on != 0;
		Log.Info( $"[debug] cuffs {caller.SteamId} = {state.Handcuffed}" );
	}

	[ConCmd( "anakin_item", ConVarFlags.Server )]
	public static void SetItem( Connection caller, string item )
	{
		var state = FindPlayer( caller )?.Components.Get<PlayerTestState>();
		if ( state is null ) return;
		state.CarriedItem = item ?? "";
		Log.Info( $"[debug] item {caller.SteamId} = '{state.CarriedItem}'" );
	}

	[ConCmd( "anakin_combat", ConVarFlags.Server )]
	public static void Combat( Connection caller )
	{
		var state = FindPlayer( caller )?.Components.Get<PlayerTestState>();
		if ( state is null ) return;
		state.MarkCombat();
		Log.Info( $"[debug] combat {caller.SteamId} at {Time.Now:0.0}" );
	}

	[ConCmd( "anakin_transfer", ConVarFlags.Server )]
	public static void ForceTransfer( Connection caller, string target )
	{
		var go = FindPlayer( caller );
		if ( go is null ) return;
		_ = TransferService.Current?.RequestTransfer( go, target.Trim().ToUpperInvariant(), TransferRule.Normal, 0 );
	}

	[ConCmd( "anakin_info", ConVarFlags.Server )]
	public static void Info( Connection caller )
	{
		var ctx = ShardContext.Current;
		var go = FindPlayer( caller );
		var wallet = go?.Components.Get<PlayerWallet>();
		var state = go?.Components.Get<PlayerTestState>();
		Log.Info( $"[debug] shard={ctx?.ShardId} \"{ctx?.DisplayName}\" backend={(ctx?.Backend?.Reachable ?? false)} " +
			$"| money={wallet?.Money} cuffs={state?.Handcuffed} item='{state?.CarriedItem}' " +
			$"combat={state?.TimeSinceCombat:0.0}s ago cooldown={state?.TimeSinceTransfer:0.0}s ago" );
	}

	private static GameObject? FindPlayer( Connection caller )
	{
		var scene = Game.ActiveScene;
		if ( scene is null ) return null;

		foreach ( var pc in scene.GetAllComponents<PlayerController>() )
		{
			if ( pc.Network.Owner == caller )
				return pc.GameObject;
		}
		return null;
	}
}
