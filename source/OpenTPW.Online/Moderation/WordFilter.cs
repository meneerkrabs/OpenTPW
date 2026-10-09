using System.Text;

namespace OpenTPW.Online.Moderation;

/// <summary>
/// Chat/postcard word filter driven by the original <c>swears.txt</c> and <c>alloweds.txt</c> lists
/// of the operator's own game installation. The lists are read at runtime and never shipped.
/// </summary>
public sealed class WordFilter
{
	public const string SwearsFileName = "swears.txt";
	public const string AllowedsFileName = "alloweds.txt";
	public const int MaximumListBytes = 256 * 1024;
	public const int MaximumEntries = 4096;

	private readonly string[] swears;
	private readonly string[] alloweds;

	public WordFilter( IEnumerable<string> swears, IEnumerable<string> alloweds )
	{
		this.swears = Clean( swears );
		this.alloweds = Clean( alloweds );
	}

	public static WordFilter Empty { get; } = new( Array.Empty<string>(), Array.Empty<string>() );

	public int SwearCount => swears.Length;
	public int AllowedCount => alloweds.Length;
	public bool IsEmpty => swears.Length == 0;

	/// <summary>
	/// Decodes an original list. [DATA:Language/English/swears.txt,alloweds.txt] Observed encoding:
	/// UTF-16LE, every code unit except CR (0x000D) and LF (0x000A) stored bitwise inverted, one entry
	/// per CRLF line. Entries may start or end with a space (word boundary) and contain punctuation.
	/// </summary>
	public static IReadOnlyList<string> DecodeList( ReadOnlySpan<byte> data )
	{
		if ( data.Length > MaximumListBytes || data.Length % 2 != 0 )
			throw new InvalidDataException( "Word list has an invalid size." );
		var builder = new StringBuilder( data.Length / 2 );
		for ( var offset = 0; offset < data.Length; offset += 2 )
		{
			var unit = (ushort)(data[offset] | data[offset + 1] << 8);
			builder.Append( unit is 0x000D or 0x000A ? (char)unit : (char)(ushort)~unit );
		}
		var lines = builder.ToString().Split( "\r\n" ).Where( line => line.Length > 0 ).ToArray();
		if ( lines.Length > MaximumEntries || lines.Any( line => line.Any( char.IsControl ) ) )
			throw new InvalidDataException( "Word list is not an original filter list." );
		return lines;
	}

	/// <summary>Encodes entries the way the original lists are stored (used by tests and tools).</summary>
	public static byte[] EncodeList( IEnumerable<string> entries )
	{
		var text = string.Join( "\r\n", entries ) + "\r\n";
		var output = new byte[text.Length * 2];
		for ( var index = 0; index < text.Length; index++ )
		{
			var unit = text[index] is '\r' or '\n' ? text[index] : (char)(ushort)~text[index];
			output[index * 2] = (byte)unit;
			output[index * 2 + 1] = (byte)(unit >> 8);
		}
		return output;
	}

	/// <summary>
	/// Loads the lists from a language directory, e.g. <c>&lt;game&gt;/Data/Language/English</c> or the
	/// Benelux CD's <c>Dutch/Filter/Dutch</c>. Missing lists give an empty filter.
	/// </summary>
	public static WordFilter LoadDirectory( string directory )
	{
		string? Find( string name ) => Directory.Exists( directory )
			? Directory.EnumerateFiles( directory ).FirstOrDefault( file => string.Equals( Path.GetFileName( file ), name, StringComparison.OrdinalIgnoreCase ) )
			: null;
		IReadOnlyList<string> Load( string? file )
		{
			if ( file == null )
				return Array.Empty<string>();
			using var stream = File.OpenRead( file );
			return DecodeList( Packages.BoundedZip.ReadBounded( stream, MaximumListBytes, Path.GetFileName( file ) ) );
		}
		return new WordFilter( Load( Find( SwearsFileName ) ), Load( Find( AllowedsFileName ) ) );
	}

	/// <summary>
	/// [APPROX:ONLINE-001] Matching rule — evidence needed: the original filter code is not
	/// readable (TP.ICD is encrypted). Assumed: case-insensitive substring match of each entry
	/// against the text padded with one space on each side (so leading/trailing spaces in entries
	/// act as word boundaries); a hit is ignored when it lies inside an occurrence of an
	/// <c>alloweds.txt</c> entry; hit characters (except spaces) are replaced by '*'.
	/// </summary>
	public string Apply( string text, out bool changed )
	{
		changed = false;
		if ( swears.Length == 0 || string.IsNullOrEmpty( text ) )
			return text;
		var padded = " " + text.ToLowerInvariant() + " ";
		var allowedRanges = new List<(int Start, int End)>();
		foreach ( var allowed in alloweds )
		{
			for ( var index = padded.IndexOf( allowed, StringComparison.Ordinal ); index >= 0; index = padded.IndexOf( allowed, index + 1, StringComparison.Ordinal ) )
				allowedRanges.Add( (index, index + allowed.Length) );
		}
		var masked = text.ToCharArray();
		foreach ( var swear in swears )
		{
			for ( var index = padded.IndexOf( swear, StringComparison.Ordinal ); index >= 0; index = padded.IndexOf( swear, index + 1, StringComparison.Ordinal ) )
			{
				var end = index + swear.Length;
				if ( allowedRanges.Any( range => range.Start <= index && range.End >= end ) )
					continue;
				for ( var position = Math.Max( index, 1 ); position < Math.Min( end, padded.Length - 1 ); position++ )
				{
					if ( masked[position - 1] != ' ' )
					{
						masked[position - 1] = '*';
						changed = true;
					}
				}
			}
		}
		return changed ? new string( masked ) : text;
	}

	public string Apply( string text ) => Apply( text, out _ );

	private static string[] Clean( IEnumerable<string> entries ) => entries
		.Where( entry => !string.IsNullOrWhiteSpace( entry ) )
		.Select( entry => entry.ToLowerInvariant() )
		.Distinct( StringComparer.Ordinal )
		.ToArray();
}
