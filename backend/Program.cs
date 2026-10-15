using System.Text;
using System.Text.Json;
using AnakinShardBackend;

// AnakinShardDemo backend: shared player store, server directory and transfer coordinator.
// Runs on http://localhost:8443. Every request is an HMAC-signed envelope { payload, sig }.
// See Store.cs for the state machine.

var secret = Environment.GetEnvironmentVariable( "SHARD_DEMO_SECRET" ) ?? Store.DefaultSecret;
// Port 8443 (not 8080): 8080 is commonly taken (e.g. NVIDIA Broadcast), and 8443 is the other
// port on s&box's localhost HTTP allowlist (80/443/8080/8443), so the editor can reach the
// backend without needing -allowlocalhttp, and dedicated servers work too.
var url = Environment.GetEnvironmentVariable( "SHARD_DEMO_BACKEND_URL" ) ?? "http://localhost:8443";
var dataFile = Path.Combine( Directory.GetCurrentDirectory(), "data", "state.json" );

void Log( string m ) => Console.WriteLine( $"{DateTimeOffset.Now:HH:mm:ss} {m}" );

var store = Store.LoadOrCreate( dataFile, Log );
store.Secret = secret;

var builder = WebApplication.CreateBuilder( args );
builder.Logging.ClearProviders();               // concise custom logging only
var app = builder.Build();

var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

// Read the raw body, verify the HMAC envelope, deserialize the inner payload to T.
async Task<(T? val, IResult? err)> Read<T>( HttpContext ctx )
{
	using var reader = new StreamReader( ctx.Request.Body, Encoding.UTF8 );
	var body = await reader.ReadToEndAsync();

	Envelope? env;
	try { env = JsonSerializer.Deserialize<Envelope>( body, jsonOpts ); }
	catch { return (default, Results.BadRequest( new OkResp( false, "bad envelope" ) )); }

	if ( env is null || env.Payload is null || env.Sig is null )
		return (default, Results.BadRequest( new OkResp( false, "bad envelope" ) ));

	if ( !Crypto.Verify( secret, env.Payload, env.Sig ) )
	{
		Log( "[auth] rejected request with bad signature" );
		return (default, Results.Json( new OkResp( false, "bad signature" ), statusCode: 401 ));
	}

	try
	{
		var val = JsonSerializer.Deserialize<T>( env.Payload, jsonOpts );
		if ( val is null )
			return (default, Results.BadRequest( new OkResp( false, "bad payload" ) ));
		return (val, null);
	}
	catch { return (default, Results.BadRequest( new OkResp( false, "bad payload" ) )); }
}

app.MapGet( "/health", () => Results.Json( new { ok = true, service = "anakin-shard-backend" } ) );

app.MapPost( "/player/get", async ( HttpContext c ) =>
{
	var (r, err) = await Read<PlayerGetReq>( c ); if ( err != null ) return err;
	return Results.Json( store.PlayerGet( r! ) );
} );

app.MapPost( "/player/join", async ( HttpContext c ) =>
{
	var (r, err) = await Read<PlayerJoinReq>( c ); if ( err != null ) return err;
	return Results.Json( store.PlayerJoin( r! ) );
} );

app.MapPost( "/player/apply-ledger", async ( HttpContext c ) =>
{
	var (r, err) = await Read<ApplyLedgerReq>( c ); if ( err != null ) return err;
	return Results.Json( store.ApplyLedger( r! ) );
} );

app.MapPost( "/directory/heartbeat", async ( HttpContext c ) =>
{
	var (r, err) = await Read<HeartbeatReq>( c ); if ( err != null ) return err;
	return Results.Json( store.Heartbeat( r! ) );
} );

app.MapPost( "/directory/lookup", async ( HttpContext c ) =>
{
	var (r, err) = await Read<LookupReq>( c ); if ( err != null ) return err;
	return Results.Json( store.Lookup( r! ) );
} );

app.MapPost( "/transfer/prepare", async ( HttpContext c ) =>
{
	var (r, err) = await Read<PrepareReq>( c ); if ( err != null ) return err;
	return Results.Json( store.Prepare( r! ) );
} );

app.MapPost( "/transfer/accept", async ( HttpContext c ) =>
{
	var (r, err) = await Read<AcceptReq>( c ); if ( err != null ) return err;
	return Results.Json( store.Accept( r! ) );
} );

app.MapPost( "/transfer/cancel", async ( HttpContext c ) =>
{
	var (r, err) = await Read<CancelReq>( c ); if ( err != null ) return err;
	return Results.Json( store.Cancel( r! ) );
} );

// Test-only: force the player's in-flight token to be expired, so the token-expiry test is
// instant instead of a 30s wait. Still HMAC-gated.
app.MapPost( "/debug/force-expire", async ( HttpContext c ) =>
{
	var (r, err) = await Read<PlayerGetReq>( c ); if ( err != null ) return err;
	return Results.Json( new OkResp( store.ForceExpireInTransit( r!.SteamId ), "forced-expire" ) );
} );

// Expire stale tokens + auto-revert their InTransit locks every 5s.
using var sweeper = new Timer( _ => store.SweepExpired(), null,
	TimeSpan.FromSeconds( 5 ), TimeSpan.FromSeconds( 5 ) );

Log( $"[boot] AnakinShardDemo backend on {url}" );
Log( $"[boot] secret: {(secret == Store.DefaultSecret ? "default demo secret (config/shard.*.json)" : "from SHARD_DEMO_SECRET env")}" );
Log( $"[boot] data file: {dataFile}" );

app.Run( url );
