using System.Security.Cryptography;

namespace OpenTPW;

/// <summary>
/// SHA-256 manifest helpers for <see cref="ParkResumeSnapshot.AssetHashes"/>. The manifest is the save's asset
/// content identity: root computes it from verified catalog/game-data paths and passes the same manifest to
/// <see cref="ParkResumeSaveFile.ValidateSnapshot"/>, which requires exact key and value agreement. Nothing here
/// reads a path that came out of a saved snapshot, executes anything or mutates an asset — every input is a
/// caller-provided stream or absolute path, and the digests are read-only computations over bytes.
/// </summary>
internal static class ParkResumeAssets
{
	/// <summary>Upper bound for manifest entries: one per canonical expected asset, never per file on disk.</summary>
	public const int MaximumManifestEntries = 8192;
	/// <summary>Upper bound for one canonical asset key (an identifier, not a path or a document).</summary>
	public const int MaximumKeyLength = 512;

	/// <summary>A SHA-256 digest as the envelope stores it: exactly 64 lowercase hexadecimal characters.</summary>
	public static bool IsValidSha256( string value ) =>
		value.Length == 64 && value.All( character => character is >= '0' and <= '9' or >= 'a' and <= 'f' );

	/// <summary>Digests everything the stream still holds, without rewinding or disposing it.</summary>
	public static string Sha256( Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		return Convert.ToHexString( SHA256.HashData( stream ) ).ToLowerInvariant();
	}

	/// <summary>Digests one file, opened read-only and shared (an original asset is never written through this).</summary>
	public static string Sha256( string path )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		using var stream = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read );
		return Sha256( stream );
	}

	/// <summary>
	/// Builds a manifest from caller-provided (key, path) pairs — root's helper for computing the current
	/// manifest at load. Keys are taken verbatim (canonicalization is root's choice); duplicates, empty or
	/// oversized keys and an oversized manifest are rejected before anything is hashed.
	/// </summary>
	public static IReadOnlyDictionary<string, string> HashFiles( IEnumerable<(string Key, string Path)> files )
	{
		ArgumentNullException.ThrowIfNull( files );
		var manifest = new Dictionary<string, string>( StringComparer.Ordinal );
		foreach ( var (key, path) in files )
		{
			if ( manifest.Count >= MaximumManifestEntries )
				throw new InvalidDataException( $"The asset manifest exceeds {MaximumManifestEntries} entries." );
			if ( string.IsNullOrWhiteSpace( key ) )
				throw new InvalidDataException( "An asset key is empty." );
			if ( key!.Length > MaximumKeyLength )
				throw new InvalidDataException( $"The asset key '{key}' exceeds {MaximumKeyLength} characters." );
			if ( !manifest.TryAdd( key, Sha256( path ) ) )
				throw new InvalidDataException( $"The asset key '{key}' is listed twice." );
		}
		return manifest;
	}
}
