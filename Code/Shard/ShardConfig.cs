using System.Text.Json;
using Sandbox;

namespace AnakinShardDemo;

/// <summary>
/// Per-shard configuration, loaded at boot from <c>config/shard.&lt;id&gt;.json</c> (mounted
/// project content). Falls back to baked defaults if the file cannot be read, so the demo
/// still runs. Held host-side only; the Secret must never sync to clients.
/// </summary>
public sealed class ShardConfig
{
	public string ShardId { get; set; } = "A";
	public string DisplayName { get; set; } = "Downtown";
	public int Port { get; set; } = 27015;
	public int QueryPort { get; set; } = 27016;
	public string BackendUrl { get; set; } = "http://localhost:8443";
	public string Secret { get; set; } = "";
	public int Capacity { get; set; } = 2;
	public string[] Peers { get; set; } = System.Array.Empty<string>();

	private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

	public static ShardConfig Load( string shardId )
	{
		// Try a couple of path shapes; fall back to baked defaults on any failure.
		foreach ( var path in new[] { $"config/shard.{shardId}.json", $"/config/shard.{shardId}.json" } )
		{
			try
			{
				if ( FileSystem.Mounted.FileExists( path ) )
				{
					var json = FileSystem.Mounted.ReadAllText( path );
					var cfg = JsonSerializer.Deserialize<ShardConfig>( json, JsonOpts );
					if ( cfg is not null )
					{
						Log.Info( $"[shard] loaded config from {path}: {cfg.ShardId} \"{cfg.DisplayName}\"" );
						return cfg;
					}
				}
			}
			catch ( System.Exception e )
			{
				Log.Warning( $"[shard] failed reading {path}: {e.Message}" );
			}
		}

		Log.Warning( $"[shard] using baked default config for shard {shardId}" );
		return Default( shardId );
	}

	public static ShardConfig Default( string shardId ) => shardId.ToUpperInvariant() switch
	{
		"B" => new ShardConfig { ShardId = "B", DisplayName = "Industrial", Port = 27017, QueryPort = 27018,
			Capacity = 2, Peers = new[] { "A", "C" }, Secret = DemoSecret },
		"C" => new ShardConfig { ShardId = "C", DisplayName = "Jail", Port = 27019, QueryPort = 27020,
			Capacity = 2, Peers = new[] { "A", "B" }, Secret = DemoSecret },
		_ => new ShardConfig { ShardId = "A", DisplayName = "Downtown", Port = 27015, QueryPort = 27016,
			Capacity = 2, Peers = new[] { "B", "C" }, Secret = DemoSecret },
	};

	// Matches config/shard.*.json and the backend's Store.DefaultSecret. Demo-only: a real
	// deployment would inject this via launch/env and never bake or commit it.
	private const string DemoSecret = "fb1976e069994f08a3f0725eb5ba9051ecd5159ab74cce1f2cdf6cad56bfa45d";
}
