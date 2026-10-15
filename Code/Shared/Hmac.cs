using System;
using System.Text;

namespace AnakinShardDemo;

/// <summary>
/// HMAC-SHA256 for backend request signing. The s&box sandbox whitelist blocks
/// System.Security.Cryptography.HMACSHA256 (SB1000), so this is a pure-managed
/// implementation. It is byte-identical to the backend's BCL HMACSHA256 (verified against it),
/// so signatures produced here validate server-side. Output is lowercase hex, matching
/// backend/Crypto.cs (Convert.ToHexString is allowed by the whitelist).
/// </summary>
public static class Hmac
{
	public static string Hex( string secret, string message )
	{
		var hash = HmacSha256( Encoding.UTF8.GetBytes( secret ), Encoding.UTF8.GetBytes( message ) );
		return Convert.ToHexString( hash ).ToLowerInvariant();
	}

	private const int BlockSize = 64;

	private static byte[] HmacSha256( byte[] key, byte[] message )
	{
		if ( key.Length > BlockSize )
			key = Sha256( key );

		var block = new byte[BlockSize];
		Array.Copy( key, block, key.Length );   // remaining bytes stay zero

		var inner = new byte[BlockSize + message.Length];
		var outerPrefix = new byte[BlockSize];
		for ( int i = 0; i < BlockSize; i++ )
		{
			inner[i] = (byte)(block[i] ^ 0x36);
			outerPrefix[i] = (byte)(block[i] ^ 0x5c);
		}
		Array.Copy( message, 0, inner, BlockSize, message.Length );

		var innerHash = Sha256( inner );
		var outer = new byte[BlockSize + innerHash.Length];
		Array.Copy( outerPrefix, 0, outer, 0, BlockSize );
		Array.Copy( innerHash, 0, outer, BlockSize, innerHash.Length );

		return Sha256( outer );
	}

	private static readonly uint[] K =
	{
		0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
		0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
		0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
		0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
		0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
		0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
		0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
		0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
	};

	private static byte[] Sha256( byte[] message )
	{
		uint h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a;
		uint h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19;

		int len = message.Length;
		int totalLen = ((len + 9 + 63) / 64) * 64;   // message + 0x80 + 8-byte length, padded to 64
		var data = new byte[totalLen];
		Array.Copy( message, data, len );
		data[len] = 0x80;
		long bitLen = (long)len * 8;
		for ( int i = 0; i < 8; i++ )
			data[totalLen - 1 - i] = (byte)(bitLen >> (8 * i));

		var w = new uint[64];
		for ( int chunk = 0; chunk < totalLen; chunk += 64 )
		{
			for ( int i = 0; i < 16; i++ )
			{
				int j = chunk + i * 4;
				w[i] = ((uint)data[j] << 24) | ((uint)data[j + 1] << 16) | ((uint)data[j + 2] << 8) | data[j + 3];
			}
			for ( int i = 16; i < 64; i++ )
			{
				uint s0 = Ror( w[i - 15], 7 ) ^ Ror( w[i - 15], 18 ) ^ (w[i - 15] >> 3);
				uint s1 = Ror( w[i - 2], 17 ) ^ Ror( w[i - 2], 19 ) ^ (w[i - 2] >> 10);
				w[i] = w[i - 16] + s0 + w[i - 7] + s1;
			}

			uint a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;
			for ( int i = 0; i < 64; i++ )
			{
				uint bigS1 = Ror( e, 6 ) ^ Ror( e, 11 ) ^ Ror( e, 25 );
				uint ch = (e & f) ^ (~e & g);
				uint t1 = h + bigS1 + ch + K[i] + w[i];
				uint bigS0 = Ror( a, 2 ) ^ Ror( a, 13 ) ^ Ror( a, 22 );
				uint maj = (a & b) ^ (a & c) ^ (b & c);
				uint t2 = bigS0 + maj;
				h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
			}

			h0 += a; h1 += b; h2 += c; h3 += d; h4 += e; h5 += f; h6 += g; h7 += h;
		}

		var hash = new byte[32];
		WriteBE( hash, 0, h0 ); WriteBE( hash, 4, h1 ); WriteBE( hash, 8, h2 ); WriteBE( hash, 12, h3 );
		WriteBE( hash, 16, h4 ); WriteBE( hash, 20, h5 ); WriteBE( hash, 24, h6 ); WriteBE( hash, 28, h7 );
		return hash;
	}

	private static uint Ror( uint x, int n ) => (x >> n) | (x << (32 - n));

	private static void WriteBE( byte[] b, int o, uint v )
	{
		b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
	}
}
