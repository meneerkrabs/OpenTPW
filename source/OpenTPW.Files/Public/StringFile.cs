using System.Collections.Concurrent;

namespace OpenTPW;

/// <summary>
/// A BFST string table decoded with the BFMU character table (<c>MBToUni.dat</c>) of its own
/// language folder.
/// </summary>
public sealed class StringFile : BaseFormat
{
	public const string CharacterTableName = "MBToUni.dat";
	private static readonly ConcurrentDictionary<string, BFMUReader> tableCache = new();
	private BFMUReader table;

	public string[] Entries { get; private set; }

	/// <summary>
	/// Reads a string table from the game file system, using <c>MBToUni.dat</c> from the same
	/// directory.
	/// </summary>
	public StringFile( string path )
	{
		var directory = Path.GetDirectoryName( path.Replace( '\\', '/' ) ) ?? "";
		var tablePath = Path.Combine( directory, CharacterTableName );
		table = tableCache.GetOrAdd( FileSystem.GetAbsolutePath( tablePath ), _ => new BFMUReader( tablePath ) );
		ReadFromFile( path );
	}

	public StringFile( Stream stream, BFMUReader table )
	{
		this.table = table ?? throw new ArgumentNullException( nameof( table ) );
		ReadFromStream( stream );
	}

	public string this[int val] => Entries[val];

	protected override void ReadFromStream( Stream stream )
	{
		var reader = new BFSTReader( stream, table );
		Entries = reader.ReadFile();
	}
}
