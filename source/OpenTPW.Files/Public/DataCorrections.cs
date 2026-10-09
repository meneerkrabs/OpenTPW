using System.Security.Cryptography;
using System.Text;

namespace OpenTPW;

/// <summary>
/// An in-memory correction of one original file, applied only when the file's SHA-256 matches
/// <see cref="OriginalSha256"/> (so other editions/patches are never altered) and its fix is enabled.
/// Nothing is ever written to the game directory.
/// </summary>
public sealed record DataCorrection( string FixId, string RelativePath, string OriginalSha256, string Description, Func<byte[], byte[]> Apply );

/// <summary>The registry of data corrections keyed by asset path and hash (docs/COMPATIBILITY.md).</summary>
public static class DataCorrections
{
	public static readonly IReadOnlyList<DataCorrection> All = new[]
	{
		// [EXT:COMPAT-FIX sign-font-substitution] [DATA:levels/space/rides/megacost.wad/megacost.sgn:slot 1 = "EggIt Italic"/EGGII___.TTF, not in fonts.wad]
		new DataCorrection( "sign-font-substitution", "/levels/space/rides/megacost/megacost.sgn",
			"E1CCFB4581D49226729D22C081ECA6E3B7494B9BF346FF7F619263741014E5F4",
			"Text slot 1 names EGGII___.TTF ('EggIt Italic'), which fonts.wad does not ship; use the shipped EGGITAOE.TTF ('EggIt AOE').",
			data => ReplaceSlotFont( data, 1, "EggIt AOE", "EGGITAOE.TTF" ) ),
	};

	/// <summary>Normalizes a data-relative path for matching ('/' separators, leading '/', '.wad' archive suffixes removed, case-insensitive).</summary>
	public static string NormalizePath( string relativePath )
	{
		var parts = relativePath.Replace( '\\', '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries )
			.Select( part => part.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ? part[..^4] : part );
		return "/" + string.Join( "/", parts ).ToLowerInvariant();
	}

	/// <summary>
	/// Returns the corrected bytes when an enabled correction matches the path and hash, else the
	/// input unchanged. <paramref name="log"/> receives one line per applied or hash-mismatched correction.
	/// </summary>
	public static byte[] Apply( string relativePath, byte[] data, Func<string, bool> isEnabled, Action<string>? log = null )
	{
		ArgumentNullException.ThrowIfNull( data );
		var path = NormalizePath( relativePath );
		foreach ( var correction in All )
		{
			if ( NormalizePath( correction.RelativePath ) != path || !isEnabled( correction.FixId ) )
				continue;
			var hash = Convert.ToHexString( SHA256.HashData( data ) );
			if ( !string.Equals( hash, correction.OriginalSha256, StringComparison.OrdinalIgnoreCase ) )
			{
				log?.Invoke( $"Data correction {correction.FixId} skipped for {correction.RelativePath}: content hash {hash} differs from the known original." );
				continue;
			}
			data = correction.Apply( (byte[])data.Clone() );
			log?.Invoke( $"Data correction {correction.FixId} applied in memory to {correction.RelativePath}: {correction.Description}" );
		}
		return data;
	}

	/// <summary>Rewrites the face name, font file name and LOGFONT face of one <see cref="SignFile"/> text slot.</summary>
	public static byte[] ReplaceSlotFont( byte[] data, int slot, string faceName, string fileName )
	{
		var offset = SignFile.HeaderBytes + slot * SignFile.SlotBytes;
		if ( offset + SignFile.SlotBytes > data.Length )
			throw new InvalidDataException( "Sign file is too short for the corrected slot." );
		WriteFixed( data, offset + 4, 64, faceName );
		WriteFixed( data, offset + 68, 260, fileName );
		WriteFixed( data, offset + 364, 32, faceName );
		return data;
	}

	private static void WriteFixed( byte[] data, int offset, int length, string value )
	{
		var bytes = Encoding.Latin1.GetBytes( value );
		if ( bytes.Length >= length )
			throw new ArgumentException( "Value does not fit the field.", nameof( value ) );
		Array.Clear( data, offset, length );
		bytes.CopyTo( data, offset );
	}
}
