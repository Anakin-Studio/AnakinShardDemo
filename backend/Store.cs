using System.Text.Json;

namespace AnakinShardBackend;

/// <summary>
/// In-memory authoritative store with JSON-file durability. All public methods lock a single
/// mutex; traffic is tiny (a few shards) so coarse locking is fine and keeps the transfer
/// state machine obviously correct.
///
/// Invariant this whole class exists to protect: a player's Money and CarriedItem are never
/// duplicated and never lost, across every transfer / failure path. Because shared state is
/// keyed by SteamId here (not on any shard's disk), even a failed transfer cannot lose money:
/// a fresh join on any shard reloads the authoritative record.
/// </summary>
public sealed class Store
{
	public const int StartingMoney = 500;
	public const string StartingItem = "briefcase";
	public const int TokenTtlSeconds = 30;
	public const int HeartbeatStaleSeconds = 15;

	// Same value as config/shard.*.json so the demo runs out-of-the-box. Override with the
	// SHARD_DEMO_SECRET env var (set on both backend and shards) for anything real.
	public const string DefaultSecret = "fb1976e069994f08a3f0725eb5ba9051ecd5159ab74cce1f2cdf6cad56bfa45d";

	public string Secret { get; set; } = DefaultSecret;

	private readonly object _lock = new();
	private readonly Dictionary<string, PlayerRecord> _players = new();
	private readonly Dictionary<string, ShardEntry> _shards = new();
	private readonly Dictionary<string, TransferToken> _tokens = new();

	private readonly string _dataFile;
	private readonly Action<string> _log;

	public Store( string dataFile, Action<string> log )
	{
		_dataFile = dataFile;
		_log = log;
	}

	// Time seam: overridable so tests can force "expired". Defaults to real time.
	public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

	private bool IsUp( ShardEntry e ) => (Now() - e.LastHeartbeat).TotalSeconds < HeartbeatStaleSeconds;
	private static bool IsFull( ShardEntry e ) => e.Players >= e.Capacity;

	private PlayerRecord GetOrCreate( string steamId )
	{
		if ( _players.TryGetValue( steamId, out var p ) )
			return p;

		p = new PlayerRecord
		{
			SteamId = steamId,
			Money = StartingMoney,
			CarriedItem = StartingItem,
			Handcuffed = false,
			CurrentShard = null,
			InTransit = null,
		};
		_players[steamId] = p;
		_log( $"[player] created {steamId} (money={StartingMoney}, item={StartingItem})" );
		return p;
	}

	private static PlayerDto ToDto( PlayerRecord p ) =>
		new( p.SteamId, p.Money, p.CarriedItem, p.Handcuffed, p.CurrentShard, p.InTransit, p.Position );

	// Player.
	public PlayerDto PlayerGet( PlayerGetReq r )
	{
		lock ( _lock )
			return ToDto( GetOrCreate( r.SteamId ) );
	}

	/// <summary>
	/// Normal (non-transfer) arrival on a shard. Sets CurrentShard. Duplicate-login rule:
	/// newest connection wins, so CurrentShard is reassigned to the joining shard. Any dangling
	/// InTransit is cleared (a normal join means the player is not mid-transfer here).
	/// </summary>
	public PlayerDto PlayerJoin( PlayerJoinReq r )
	{
		lock ( _lock )
		{
			var p = GetOrCreate( r.SteamId );
			var prev = p.CurrentShard;
			p.CurrentShard = r.ShardId;
			p.InTransit = null;
			Save();
			if ( prev is not null && prev != r.ShardId )
				_log( $"[player] {r.SteamId} joined {r.ShardId} (was {prev}), newest-wins" );
			else
				_log( $"[player] {r.SteamId} joined {r.ShardId}" );
			return ToDto( p );
		}
	}

	/// <summary>Applies a money delta exactly once (dedupe on transferId). Used for tolls.</summary>
	public LedgerResp ApplyLedger( ApplyLedgerReq r )
	{
		lock ( _lock )
		{
			var p = GetOrCreate( r.SteamId );

			if ( p.AppliedTransferIds.Contains( r.TransferId ) )
			{
				_log( $"[ledger] {r.SteamId} {r.TransferId} already applied, no-op (bal={p.Money})" );
				return new LedgerResp( true, p.Money, "already-applied" );
			}

			if ( r.Delta < 0 && p.Money + r.Delta < 0 )
			{
				_log( $"[ledger] {r.SteamId} {r.TransferId} DENIED insufficient (bal={p.Money}, delta={r.Delta})" );
				return new LedgerResp( false, p.Money, "insufficient-funds" );
			}

			p.Money += r.Delta;
			p.AppliedTransferIds.Add( r.TransferId );
			Save();
			_log( $"[ledger] {r.SteamId} {r.TransferId} delta={r.Delta} ({r.Reason}) -> bal={p.Money}" );
			return new LedgerResp( true, p.Money );
		}
	}

	// Directory.
	public OkResp Heartbeat( HeartbeatReq r )
	{
		lock ( _lock )
		{
			if ( !_shards.TryGetValue( r.ShardId, out var e ) )
			{
				e = new ShardEntry { ShardId = r.ShardId };
				_shards[r.ShardId] = e;
			}
			e.ConnectHandle = r.ConnectHandle;
			e.Players = r.Players;
			e.Capacity = r.Capacity;
			e.Version = r.Version;
			e.LastHeartbeat = Now();
			// heartbeats are transient, not persisted.
			return new OkResp( true );
		}
	}

	public LookupResp Lookup( LookupReq r )
	{
		lock ( _lock )
		{
			if ( _shards.TryGetValue( r.ShardId, out var e ) )
				return new LookupResp( e.ShardId, e.ConnectHandle, e.Players, e.Capacity, IsUp( e ) );

			return new LookupResp( r.ShardId, "", 0, 0, false );
		}
	}

	// Transfer state machine.
	public PrepareResp Prepare( PrepareReq r )
	{
		lock ( _lock )
		{
			var p = GetOrCreate( r.SteamId );

			if ( p.InTransit is not null )
				return Deny( $"already in transit ({p.InTransit})" );

			if ( p.CurrentShard != r.SrcShard )
				return Deny( $"not on source shard (backend says {p.CurrentShard ?? "none"})" );

			if ( !_shards.TryGetValue( r.DstShard, out var dst ) || !IsUp( dst ) )
				return Deny( "destination down" );

			if ( IsFull( dst ) )
				return Deny( "destination full" );

			// Snapshot the source shard's live cuffs/item into the authoritative record; money
			// stays ledger-authoritative (already correct here).
			p.Handcuffed = r.Handcuffed;
			if ( r.CarriedItem is not null )
				p.CarriedItem = r.CarriedItem;
			if ( !string.IsNullOrEmpty( r.Position ) )
				p.Position = r.Position;

			var now = Now();
			var token = new TransferToken
			{
				TransferId = r.TransferId,
				SteamId = r.SteamId,
				SrcShard = r.SrcShard,
				DstShard = r.DstShard,
				Money = p.Money,
				CarriedItem = p.CarriedItem,
				Handcuffed = p.Handcuffed,
				Position = p.Position,
				IssuedAt = now,
				ExpiresAt = now.AddSeconds( TokenTtlSeconds ),
				Nonce = Guid.NewGuid().ToString( "N" ),
			};
			token.Signature = SignToken( token );
			_tokens[r.TransferId] = token;
			p.InTransit = r.TransferId;
			Save();

			_log( $"[transfer] PREPARE {r.TransferId} {r.SteamId} {r.SrcShard}->{r.DstShard} " +
				$"snapshot(money={token.Money},item={token.CarriedItem},cuffs={token.Handcuffed}) ttl={TokenTtlSeconds}s" );
			return new PrepareResp( true, "", r.TransferId, dst.ConnectHandle, token.ExpiresAt );

			PrepareResp Deny( string reason )
			{
				_log( $"[transfer] PREPARE {r.TransferId} {r.SteamId} {r.SrcShard}->{r.DstShard} DENIED: {reason}" );
				return new PrepareResp( false, reason, r.TransferId, "", default );
			}
		}
	}

	/// <summary>
	/// Destination shard confirms an arriving player. Resolves the transfer via the player's
	/// InTransit record (dst never sees the TransferId). Idempotent: a second accept after
	/// consume returns the same authoritative snapshot without re-applying anything.
	/// </summary>
	public AcceptResp Accept( AcceptReq r )
	{
		lock ( _lock )
		{
			var p = GetOrCreate( r.SteamId );
			var transferId = p.InTransit;

			if ( transferId is null || !_tokens.TryGetValue( transferId, out var token ) )
			{
				// Not mid-transfer here. If already sitting on dst, that's an idempotent
				// success (accept was already processed and InTransit cleared). Otherwise
				// this arrival is a normal join, so tell the caller and it falls back.
				if ( p.CurrentShard == r.DstShard )
					return new AcceptResp( true, "already-accepted", ToDto( p ) );

				_log( $"[transfer] ACCEPT {r.SteamId}->{r.DstShard} not-in-transit (fall back to join)" );
				return new AcceptResp( false, "not-in-transit", null );
			}

			if ( token.DstShard != r.DstShard )
			{
				// Player surfaced on a shard that isn't the transfer's destination, e.g. the
				// client failed to reach dst and reconnected to src. Abandon the transfer and
				// let the caller fall back to a normal join. Money stays intact (authoritative
				// here), so nothing is lost.
				token.Expired = true;
				if ( p.InTransit == transferId )
					p.InTransit = null;
				Save();
				_log( $"[transfer] ACCEPT {transferId} {r.SteamId} arrived {r.DstShard} != dst " +
					$"{token.DstShard}, abandoned, caller should join" );
				return new AcceptResp( false, "not-in-transit", null );
			}

			if ( token.Consumed )
			{
				_log( $"[transfer] ACCEPT {transferId} {r.SteamId} REPLAY, no-op" );
				return new AcceptResp( true, "replay", ToDto( p ) );
			}

			if ( token.Expired || Now() > token.ExpiresAt )
			{
				token.Expired = true;
				if ( p.InTransit == transferId )
					p.InTransit = null;   // revert
				Save();
				return Fail( "token expired" );
			}

			// Consume: the one and only mutation point.
			token.Consumed = true;
			p.CurrentShard = token.DstShard;
			p.InTransit = null;
			p.Money = token.Money;
			p.CarriedItem = token.CarriedItem;
			p.Handcuffed = token.Handcuffed;
			p.Position = token.Position;
			Save();

			_log( $"[transfer] ACCEPT {transferId} {r.SteamId} -> {r.DstShard} OK " +
				$"(money={p.Money},item={p.CarriedItem},cuffs={p.Handcuffed})" );
			return new AcceptResp( true, "", ToDto( p ) );

			AcceptResp Fail( string reason )
			{
				_log( $"[transfer] ACCEPT {transferId} {r.SteamId}->{r.DstShard} FAIL: {reason}" );
				return new AcceptResp( false, reason, null );
			}
		}
	}

	public OkResp Cancel( CancelReq r )
	{
		lock ( _lock )
		{
			var p = GetOrCreate( r.SteamId );
			if ( p.InTransit == r.TransferId )
			{
				p.InTransit = null;
				if ( _tokens.TryGetValue( r.TransferId, out var t ) )
					t.Expired = true;
				Save();
				_log( $"[transfer] CANCEL {r.TransferId} {r.SteamId}, reverted" );
				return new OkResp( true );
			}
			return new OkResp( false, "not in transit under that id" );
		}
	}

	/// <summary>Test hook: mark the player's in-flight token expired.</summary>
	public bool ForceExpireInTransit( string steamId )
	{
		lock ( _lock )
		{
			if ( _players.TryGetValue( steamId, out var p ) && p.InTransit is not null
				&& _tokens.TryGetValue( p.InTransit, out var t ) )
			{
				t.ExpiresAt = Now().AddSeconds( -1 );
				_log( $"[debug] force-expired token {t.TransferId} for {steamId}" );
				return true;
			}
			return false;
		}
	}

	/// <summary>Timer-driven: expire stale tokens and auto-revert their InTransit locks.</summary>
	public void SweepExpired()
	{
		lock ( _lock )
		{
			var now = Now();
			foreach ( var t in _tokens.Values )
			{
				if ( t.Consumed || t.Expired ) continue;
				if ( now <= t.ExpiresAt ) continue;

				t.Expired = true;
				if ( _players.TryGetValue( t.SteamId, out var p ) && p.InTransit == t.TransferId )
				{
					p.InTransit = null;
					_log( $"[sweep] token {t.TransferId} expired, reverted InTransit for {t.SteamId}" );
				}
			}
		}
	}

	private string SignToken( TransferToken t )
	{
		var payload = $"{t.TransferId}|{t.SteamId}|{t.SrcShard}|{t.DstShard}|{t.Money}|" +
			$"{t.CarriedItem}|{t.Handcuffed}|{t.IssuedAt:o}|{t.ExpiresAt:o}|{t.Nonce}";
		return Crypto.HmacHex( Secret, payload );
	}

	// Persistence.
	private sealed record Snapshot( List<PlayerRecord> Players, List<TransferToken> Tokens );

	private void Save()
	{
		try
		{
			var snap = new Snapshot( _players.Values.ToList(), _tokens.Values.ToList() );
			var dir = Path.GetDirectoryName( _dataFile );
			if ( !string.IsNullOrEmpty( dir ) )
				Directory.CreateDirectory( dir );
			var tmp = _dataFile + ".tmp";
			File.WriteAllText( tmp, JsonSerializer.Serialize( snap, JsonOpts ) );
			File.Move( tmp, _dataFile, overwrite: true );   // atomic-ish
		}
		catch ( Exception e )
		{
			_log( $"[persist] save failed: {e.Message}" );
		}
	}

	public static Store LoadOrCreate( string dataFile, Action<string> log )
	{
		var store = new Store( dataFile, log );
		try
		{
			if ( File.Exists( dataFile ) )
			{
				var snap = JsonSerializer.Deserialize<Snapshot>( File.ReadAllText( dataFile ), JsonOpts );
				if ( snap is not null )
				{
					foreach ( var p in snap.Players )
						store._players[p.SteamId] = p;
					foreach ( var t in snap.Tokens )
						store._tokens[t.TransferId] = t;
					log( $"[persist] loaded {store._players.Count} players, {store._tokens.Count} tokens" );
				}
			}
		}
		catch ( Exception e )
		{
			log( $"[persist] load failed ({e.Message}), starting empty" );
		}
		return store;
	}

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true,
	};
}
