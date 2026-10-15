#nullable enable

using System;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Network;

namespace AnakinShardDemo;

/// <summary>
/// Core host-side networking for a shard: creates the Steam lobby (required, or clients get
/// "Not Connected"), spawns a player object per connection, and routes each arrival through
/// <see cref="TransferService"/> (transfer-accept vs normal join).
/// </summary>
public sealed class ShardNetwork : Component, Component.INetworkListener
{
	public static ShardNetwork? Current { get; private set; }

	/// <summary>Player prefab (PlayerController + PlayerWallet + PlayerTestState + PlayerTransfer).</summary>
	[Property] public GameObject? PlayerPrefab { get; set; }

	/// <summary>Default spawn point for fresh joins.</summary>
	[Property] public GameObject? SpawnPoint { get; set; }

	/// <summary>Where players materialise after arriving via a transfer (fallback only).</summary>
	[Property] public GameObject? ArrivalPoint { get; set; }

	/// <summary>Current Steam lobby id as a string, heartbeated as the shard's connect handle.</summary>
	public string ConnectHandle { get; private set; } = "";

	private readonly System.Collections.Generic.Dictionary<Guid, GameObject> _players = new();

	protected override void OnAwake() => Current = this;

	protected override void OnStart()
	{
		// Only a dedicated server hosts a shard lobby. A client never self-hosts.
		if ( !Application.IsDedicatedServer )
			return;

		ResolveSceneRefs();

		if ( !Networking.IsActive )
		{
			var cap = ShardContext.Current?.Config?.Capacity ?? 8;
			// Create the lobby so clients can connect. Privacy defaults to public.
			Networking.CreateLobby( new LobbyConfig
			{
				MaxPlayers = Math.Max( cap, 1 ),
				Name = ShardContext.Current?.DisplayName ?? "Anakin Shard",
			} );
			// Tag the lobby with the shard id so clients and peers can find it via QueryLobbies.
			Networking.SetData( "shardid", ShardContext.Current?.ShardId ?? "A" );
			Log.Info( "[net] created lobby (shardid tagged)" );
		}
	}


	// A dedicated server cannot read its own lobby id (QueryLobbies is client-only, CreateLobby
	// returns void). Instead the shard tags its lobby (SetData "shardid" above) and the client
	// discovers the target shard's lobby by that tag at reconnect time. See
	// PlayerTransfer.ConnectToShard.

	// INetworkListener

	void Component.INetworkListener.OnActive( Connection channel )
	{
		if ( !Networking.IsHost )
			return;

		Log.Info( $"[net] OnActive {channel.DisplayName} ({channel.SteamId})" );
		var go = SpawnPlayer( channel );
		if ( go is null )
			return;

		_players[channel.Id] = go;
		_ = TransferService.Current?.HandleArrival( channel, go );
	}

	void Component.INetworkListener.OnDisconnected( Connection channel )
	{
		if ( _players.Remove( channel.Id, out var go ) )
			go?.Destroy();
		Log.Info( $"[net] OnDisconnected {channel.DisplayName}" );
	}

	// Fallback wiring: if the GameObject refs were not set in the Inspector, resolve them by
	// name from the scene. Lets the demo run without manual drag-drop.
	private void ResolveSceneRefs()
	{
		PlayerPrefab ??= FindInScene( "PlayerTemplate" );
		SpawnPoint ??= FindInScene( "SpawnPoint" );
		ArrivalPoint ??= FindInScene( "ArrivalPoint" );

		if ( PlayerPrefab is null )
			Log.Warning( "[net] no PlayerPrefab and no 'PlayerTemplate' object found in scene" );
	}

	private GameObject? FindInScene( string name )
	{
		foreach ( var go in Scene.GetAllObjects( false ) )   // false = include disabled objects
		{
			if ( go.Name == name )
				return go;
		}
		return null;
	}

	private GameObject? SpawnPlayer( Connection owner )
	{
		if ( PlayerPrefab is null )
		{
			Log.Warning( "[net] PlayerPrefab not assigned, cannot spawn player" );
			return null;
		}

		var start = (SpawnPoint ?? GameObject).WorldPosition;
		var go = PlayerPrefab.Clone( start );
		go.Enabled = true;   // PlayerPrefab is a disabled scene template; clones must be enabled
		go.Name = $"Player {owner.DisplayName}";

		// Spawn networked and owned by the connecting client.
		go.NetworkSpawn( owner );
		return go;
	}

	/// <summary>Move a spawned player to a named point (host authoritative).</summary>
	public void PlaceAt( GameObject player, bool arrival )
	{
		var point = arrival ? ArrivalPoint : SpawnPoint;
		if ( point is not null )
			player.WorldPosition = point.WorldPosition;
	}
}
