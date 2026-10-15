#nullable enable

using System.Text.Json;
using System.Threading.Tasks;
using Sandbox;

namespace AnakinShardDemo;

/// <summary>
/// Typed, async wrapper over the backend HTTP API. Every call is an HMAC-signed envelope.
/// Never throws into gameplay: a failed/unreachable call returns null (or a typed failure),
/// and <see cref="Reachable"/> / <see cref="LastError"/> feed the debug HUD.
///
/// Host-side only, constructed by <see cref="ShardContext"/> on the host. Clients never touch
/// the backend (host authority).
/// </summary>
public sealed class BackendClient
{
	private readonly string _baseUrl;
	private readonly string _secret;
	private readonly string _shardId;

	public bool Reachable { get; private set; } = true;
	public string LastError { get; private set; } = "";

	public BackendClient( string baseUrl, string secret, string shardId )
	{
		_baseUrl = baseUrl.TrimEnd( '/' );
		_secret = secret;
		_shardId = shardId;
	}

	// Endpoints.
	public Task<PlayerDto?> PlayerGet( string steamId ) =>
		Post<PlayerDto>( "/player/get", new PlayerGetReq( steamId ) );

	public Task<PlayerDto?> PlayerJoin( string steamId ) =>
		Post<PlayerDto>( "/player/join", new PlayerJoinReq( steamId, _shardId ) );

	public Task<LedgerResp?> ApplyLedger( string steamId, string transferId, int delta, string reason ) =>
		Post<LedgerResp>( "/player/apply-ledger", new ApplyLedgerReq( steamId, transferId, delta, reason ) );

	public Task<OkResp?> Heartbeat( string connectHandle, int players, int capacity ) =>
		Post<OkResp>( "/directory/heartbeat",
			new HeartbeatReq( _shardId, connectHandle, players, capacity, ShardContext.Version ) );

	public Task<LookupResp?> Lookup( string shardId ) =>
		Post<LookupResp>( "/directory/lookup", new LookupReq( shardId ) );

	public Task<PrepareResp?> Prepare( string steamId, string dstShard, string transferId,
		bool handcuffed, string carriedItem, string position ) =>
		Post<PrepareResp>( "/transfer/prepare",
			new PrepareReq( steamId, _shardId, dstShard, transferId, handcuffed, carriedItem, position ) );

	public Task<AcceptResp?> Accept( string steamId ) =>
		Post<AcceptResp>( "/transfer/accept", new AcceptReq( steamId, _shardId ) );

	public Task<OkResp?> Cancel( string steamId, string transferId ) =>
		Post<OkResp>( "/transfer/cancel", new CancelReq( steamId, transferId ) );

	/// <summary>Test-only: force the player's in-flight token to expire.</summary>
	public Task<OkResp?> ForceExpire( string steamId ) =>
		Post<OkResp>( "/debug/force-expire", new PlayerGetReq( steamId ) );

	// Transport.
	private async Task<T?> Post<T>( string path, object dto ) where T : class
	{
		// Serialize the DTO, sign that exact string, then wrap. The backend verifies the HMAC
		// over the received payload string (it does not re-serialize), so casing here is not
		// load-bearing for auth.
		var payload = JsonSerializer.Serialize( dto, dto.GetType() );
		var sig = Hmac.Hex( _secret, payload );
		var envelope = new Envelope { Payload = payload, Sig = sig };

		try
		{
			var resp = await Http.RequestAsync( _baseUrl + path, "POST", Http.CreateJsonContent( envelope ) );
			var body = await resp.Content.ReadAsStringAsync();
			Reachable = true;
			LastError = "";
			return JsonSerializer.Deserialize<T>( body, ReadOpts );
		}
		catch ( System.Exception e )
		{
			Reachable = false;
			LastError = e.Message;
			Log.Warning( $"[backend] POST {path} failed: {e.Message}" );
			return null;
		}
	}

	private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };
}
