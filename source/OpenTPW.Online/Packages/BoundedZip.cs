using System.IO.Compression;

namespace OpenTPW.Online.Packages;

/// <summary>Limits for one named entry of a bounded ZIP container.</summary>
public readonly record struct ZipEntryRule( string Name, int MaximumBytes, bool Required );

/// <summary>
/// Strict reader/writer for the small ZIP containers used by <c>.tpwpark</c> and <c>.tpwcard</c>.
/// [EXT:ONLINE-001] OpenTPW container design (the original online service used EA's servers; no
/// original file format is reproduced). Defences: the whole input is bounded before parsing, only
/// an allow-list of flat entry names is accepted (no directories, traversal, drive or absolute
/// paths, no duplicates), declared sizes and compression ratios are checked and every entry is
/// decompressed through a hard byte cap, so a lying header cannot inflate past the limit.
/// </summary>
public static class BoundedZip
{
	/// <summary>[EXT:ONLINE-002] Highest accepted uncompressed/compressed ratio for entries above 4 KiB.</summary>
	public const int MaximumCompressionRatio = 100;
	private const int RatioCheckThreshold = 4096;

	public static IReadOnlyDictionary<string, byte[]> Read( Stream input, int maximumContainerBytes, IReadOnlyList<ZipEntryRule> rules )
	{
		ArgumentNullException.ThrowIfNull( input );
		var buffer = ReadBounded( input, maximumContainerBytes, "Container" );
		return Read( buffer, rules );
	}

	public static IReadOnlyDictionary<string, byte[]> Read( byte[] container, IReadOnlyList<ZipEntryRule> rules )
	{
		ArgumentNullException.ThrowIfNull( container );
		var byName = rules.ToDictionary( rule => rule.Name, StringComparer.Ordinal );
		var result = new Dictionary<string, byte[]>( StringComparer.Ordinal );
		ZipArchive archive;
		try
		{
			archive = new ZipArchive( new MemoryStream( container, writable: false ), ZipArchiveMode.Read );
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or ArgumentException or IOException or NotSupportedException )
		{
			throw new InvalidDataException( "The file is not a valid OpenTPW container.", exception );
		}
		using ( archive )
		{
			if ( archive.Entries.Count > rules.Count )
				throw new InvalidDataException( $"Container has {archive.Entries.Count} entries; at most {rules.Count} are allowed." );
			foreach ( var entry in archive.Entries )
			{
				var name = entry.FullName;
				ValidateEntryName( name );
				if ( !byName.TryGetValue( name, out var rule ) )
					throw new InvalidDataException( $"Container entry '{Sanitize( name )}' is not allowed." );
				if ( result.ContainsKey( name ) )
					throw new InvalidDataException( $"Container entry '{name}' appears more than once." );
				if ( entry.Length < 0 || entry.Length > rule.MaximumBytes )
					throw new InvalidDataException( $"Container entry '{name}' exceeds its {rule.MaximumBytes}-byte limit." );
				if ( entry.Length > RatioCheckThreshold && entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaximumCompressionRatio )
					throw new InvalidDataException( $"Container entry '{name}' has a suspicious compression ratio." );
				try
				{
					using var stream = entry.Open();
					var data = ReadBounded( stream, rule.MaximumBytes, $"Container entry '{name}'" );
					if ( data.Length != entry.Length )
						throw new InvalidDataException( $"Container entry '{name}' does not match its declared size." );
					result.Add( name, data );
				}
				catch ( Exception exception ) when ( exception is IOException or NotSupportedException )
				{
					throw new InvalidDataException( $"Container entry '{name}' cannot be decompressed.", exception );
				}
			}
		}
		foreach ( var rule in rules.Where( rule => rule.Required && !result.ContainsKey( rule.Name ) ) )
			throw new InvalidDataException( $"Container entry '{rule.Name}' is missing." );
		return result;
	}

	public static byte[] Write( IEnumerable<KeyValuePair<string, byte[]>> entries )
	{
		using var output = new MemoryStream();
		using ( var archive = new ZipArchive( output, ZipArchiveMode.Create, leaveOpen: true ) )
		{
			foreach ( var (name, data) in entries )
			{
				ValidateEntryName( name );
				// PNG data is already compressed; store it.
				var level = name.EndsWith( ".png", StringComparison.Ordinal ) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
				var entry = archive.CreateEntry( name, level );
				// [EXT:ONLINE-003] Fixed timestamp so identical content gives identical bytes.
				entry.LastWriteTime = new DateTimeOffset( 2000, 1, 1, 0, 0, 0, TimeSpan.Zero );
				using var stream = entry.Open();
				stream.Write( data );
			}
		}
		return output.ToArray();
	}

	/// <summary>Reads at most <paramref name="maximumBytes"/>; one byte more is an error.</summary>
	public static byte[] ReadBounded( Stream stream, int maximumBytes, string what )
	{
		var output = new MemoryStream();
		var chunk = new byte[81920];
		long total = 0;
		while ( true )
		{
			var count = stream.Read( chunk, 0, (int)Math.Min( chunk.Length, maximumBytes + 1L - total ) );
			if ( count == 0 )
				break;
			total += count;
			if ( total > maximumBytes )
				throw new InvalidDataException( $"{what} exceeds the {maximumBytes}-byte limit." );
			output.Write( chunk, 0, count );
		}
		return output.ToArray();
	}

	public static void ValidateEntryName( string name )
	{
		if ( string.IsNullOrEmpty( name ) || name.Length > 64 )
			throw new InvalidDataException( "Container entry name is empty or too long." );
		foreach ( var c in name )
		{
			if ( !(char.IsAsciiLetterOrDigit( c ) || c is '.' or '-' or '_') )
				throw new InvalidDataException( $"Container entry name '{Sanitize( name )}' contains a path separator or unsupported character." );
		}
		if ( name.StartsWith( '.' ) || name.Contains( "..", StringComparison.Ordinal ) )
			throw new InvalidDataException( $"Container entry name '{Sanitize( name )}' is not a plain file name." );
	}

	private static string Sanitize( string name )
	{
		var cleaned = new string( name.Take( 64 ).Select( c => char.IsControl( c ) ? '?' : c ).ToArray() );
		return name.Length > 64 ? cleaned + "…" : cleaned;
	}
}
