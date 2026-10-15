#nullable enable

using Sandbox;

namespace AnakinShardDemo;

/// <summary>
/// One source of truth for "which shard am I". Authored on a stable-GUID scene object and
/// NetworkSpawned with the scene, so <see cref="ShardId"/> / <see cref="DisplayName"/> can
/// replicate (SyncFlags.FromHost) to clients for the HUD.
///
/// The host resolves its identity at boot (launch convar, then hostname, then editor default),
/// loads the matching <see cref="ShardConfig"/>, and builds the <see cref="BackendClient"/>.
/// Config, Backend and Secret stay host-side only and are never synced; clients only ever
/// learn the shard's id and display name.
/// </summary>
public sealed class ShardContext : Component
{
	public const string Version = "0.1.0";

	public static ShardContext? Current { get; private set; }

	// Set by the launch arg `+anakin_shard A|B|C` (launch params are convars applied at boot).
	// Read late (OnStart), with hostname and default fallbacks.
	[ConVar( "anakin_shard" )] public static string ShardArg { get; set; } = "";

	// Replicated to clients (safe to show). Never put the secret here.
	[Sync( SyncFlags.FromHost )] public string ShardId { get; set; } = "A";
	[Sync( SyncFlags.FromHost )] public string DisplayName { get; set; } = "Downtown";

	/// <summary>Host's last-known backend reachability, mirrored to clients for the HUD.</summary>
	[Sync( SyncFlags.FromHost )] public bool BackendOk { get; set; } = true;

	// Host-only. Null on clients.
	public ShardConfig? Config { get; private set; }
	public BackendClient? Backend { get; private set; }

	protected override void OnAwake()
	{
		Current = this;
	}

	protected override void OnStart()
	{
		// Only a dedicated server is a shard. Clients (and the editor) connect to one and get
		// its id/name via sync; they never host a shard themselves.
		if ( !Application.IsDedicatedServer )
		{
			Log.Info( $"[shard] client sees shard {ShardId} \"{DisplayName}\"" );
			return;
		}

		var id = ResolveShardId();
		Config = ShardConfig.Load( id );

		ShardId = Config.ShardId;
		DisplayName = Config.DisplayName;
		Backend = new BackendClient( Config.BackendUrl, Config.Secret, Config.ShardId );

		Log.Info( $"[shard] DEDICATED shard {ShardId} \"{DisplayName}\" -> backend {Config.BackendUrl}" );
	}

	private static string ResolveShardId()
	{
		// 1. launch convar +anakin_shard A
		if ( !string.IsNullOrWhiteSpace( ShardArg ) )
			return Normalize( ShardArg );

		// 2. parse from the server hostname, e.g. "Anakin Shard B - Industrial"
		try
		{
			var name = LaunchArguments.ServerName ?? "";
			var idx = name.IndexOf( "Shard ", System.StringComparison.OrdinalIgnoreCase );
			if ( idx >= 0 && idx + 6 < name.Length )
			{
				var c = char.ToUpperInvariant( name[idx + 6] );
				if ( c is 'A' or 'B' or 'C' )
					return c.ToString();
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"[shard] hostname parse failed: {e.Message}" );
		}

		// 3. editor / single-instance default
		return "A";
	}

	private static string Normalize( string s )
	{
		var t = s.Trim().ToUpperInvariant();
		return t is "A" or "B" or "C" ? t : "A";
	}
}
