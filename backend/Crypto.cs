using System.Security.Cryptography;
using System.Text;

namespace AnakinShardBackend;

/// <summary>
/// HMAC-SHA256 request authentication. Every request body is a signed envelope
/// { "payload": "&lt;json&gt;", "sig": "&lt;hex&gt;" } where sig = HMAC-SHA256(secret, payload).
/// The body is signed (not a header) so the game side never needs custom HTTP headers.
/// </summary>
public static class Crypto
{
	public static string HmacHex( string secret, string message )
	{
		using var h = new HMACSHA256( Encoding.UTF8.GetBytes( secret ) );
		var hash = h.ComputeHash( Encoding.UTF8.GetBytes( message ) );
		return Convert.ToHexString( hash ).ToLowerInvariant();
	}

	/// <summary>Constant-time comparison so a bad signature can't be timed.</summary>
	public static bool Verify( string secret, string message, string? sigHex )
	{
		var expected = HmacHex( secret, message );
		return CryptographicOperations.FixedTimeEquals(
			Encoding.UTF8.GetBytes( expected ),
			Encoding.UTF8.GetBytes( sigHex ?? "" ) );
	}
}
