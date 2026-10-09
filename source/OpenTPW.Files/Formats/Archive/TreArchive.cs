using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>How an entry of a <see cref="TreArchive"/> is stored.</summary>
public enum TreCompressionKind { Stored, Pkware, Refpack }

/// <summary>One descriptor of a TREE archive: where the data is and how it is stored. Names are not stored; see <see cref="TreArchive.Hash"/>.</summary>
public sealed record TreEntry( int Index, uint Offset, uint StoredSize, uint FullSize, uint Flags, TreCompressionKind Compression );

/// <summary>
/// The "TREE" archive of the original CD's autorun launcher (<c>general.tre</c>, <c>English.tre</c>,
/// <c>autorun.tre</c> ...; docs/AUTORUN.md). Entries are found by a 32-bit hash of the upper-cased
/// path as the launcher script writes it (<c>.\autorun\play.bmp</c>); file names are not stored.
/// </summary>
public sealed class TreArchive
{
	public const uint Magic = 0x45455254; // "TREE"
	public const uint SupportedVersion = 1;
	private const int HeaderSize = 32;
	private const int DescriptorSize = 12;
	private const uint EmptyNode = 0xFFFFFFFF;
	private const uint CompressedFlag = 0x40000000;
	private const uint SizeMask = 0x3FFFFFFF;
	private const uint NotFound = 0xFFFFFFFE;

	private readonly byte[] data;
	private readonly uint[] nodeHashes;
	private readonly uint[] nodeIndices;
	private readonly int collisionOffset;
	private readonly int collisionSize;

	public TreArchive( byte[] data )
	{
		this.data = data;
		if ( data.Length < HeaderSize || U32( 0 ) != Magic )
			throw new InvalidDataException( "Not a TREE archive." );
		var version = U32( 4 );
		if ( version > SupportedVersion )
			throw new InvalidDataException( $"Unsupported TREE version {version}." );
		var count = U32( 8 );
		var descriptorOffset = U32( 12 );
		var nodeCount = U32( 16 );
		var hashOffset = U32( 20 );
		var collisionLength = U32( 24 );
		var collisionStart = U32( 28 );
		if ( count > 0xFFFF || nodeCount > 0xFFFF
			|| descriptorOffset + (long)count * DescriptorSize > data.Length
			|| hashOffset + (long)nodeCount * 8 > data.Length
			|| collisionStart + (long)collisionLength > data.Length )
			throw new InvalidDataException( "TREE header points outside the file." );
		nodeHashes = new uint[nodeCount];
		nodeIndices = new uint[nodeCount];
		for ( var node = 0; node < nodeCount; node++ )
		{
			nodeHashes[node] = U32( (int)hashOffset + node * 8 );
			nodeIndices[node] = U32( (int)hashOffset + node * 8 + 4 );
		}
		collisionOffset = (int)collisionStart;
		collisionSize = (int)collisionLength;
		var entries = new List<TreEntry>( (int)count );
		for ( var index = 0; index < count; index++ )
		{
			var at = (int)descriptorOffset + index * DescriptorSize;
			var offset = U32( at );
			var field = U32( at + 4 );
			var full = U32( at + 8 );
			// Equal sizes mean stored; otherwise bit 30 selects RefPack and its absence PKWARE DCL.
			var kind = field == full ? TreCompressionKind.Stored : (field & CompressedFlag) != 0 ? TreCompressionKind.Refpack : TreCompressionKind.Pkware;
			var stored = kind == TreCompressionKind.Stored ? field : field & SizeMask;
			if ( offset + (long)stored > data.Length )
				throw new InvalidDataException( $"TREE entry {index} points outside the file." );
			entries.Add( new TreEntry( index, offset, stored, full, kind == TreCompressionKind.Stored ? 0 : field & ~SizeMask, kind ) );
		}
		Entries = entries;
	}

	public static TreArchive Open( string path ) => new( File.ReadAllBytes( path ) );

	public IReadOnlyList<TreEntry> Entries { get; }

	/// <summary>
	/// The name hash of the original <c>trees.c</c>: the name is upper-cased; <c>h = c0 &lt;&lt; 8</c>, then for
	/// every further character <c>h += (h &gt;&gt; 4) * c; h += i++</c> (32-bit, <c>i</c> counting from 0).
	/// </summary>
	public static uint Hash( string name )
	{
		var bytes = Encoding.Latin1.GetBytes( name.ToUpperInvariant() );
		if ( bytes.Length == 0 )
			return 0;
		uint hash = (uint)bytes[0] << 8;
		uint position = 0;
		for ( var index = 1; index < bytes.Length; index++ )
		{
			hash += (hash >> 4) * bytes[index];
			hash += position++;
		}
		return hash;
	}

	/// <summary>Finds an entry by script path such as <c>.\autorun\play.bmp</c> (case-insensitive).</summary>
	public bool TryFind( string name, out TreEntry entry )
	{
		var hash = Hash( name );
		var index = FindIndex( hash );
		if ( index == EmptyNode )
			index = FindCollision( name );
		if ( index < Entries.Count )
		{
			entry = Entries[(int)index];
			return true;
		}
		entry = null!;
		return false;
	}

	/// <summary>The entry whose name hash is <paramref name="hash"/> (no collision-list lookup).</summary>
	public bool TryFindHash( uint hash, out TreEntry entry )
	{
		var index = FindIndex( hash );
		if ( index < Entries.Count )
		{
			entry = Entries[(int)index];
			return true;
		}
		entry = null!;
		return false;
	}

	/// <summary>Every (hash, entry index) pair of the lookup tree, in storage order.</summary>
	public IEnumerable<(uint Hash, int Index)> HashNodes()
	{
		for ( var node = 0; node < nodeHashes.Length; node++ )
			if ( nodeHashes[node] != EmptyNode && nodeIndices[node] < Entries.Count )
				yield return (nodeHashes[node], (int)nodeIndices[node]);
	}

	/// <summary>The decoded bytes of an entry.</summary>
	public byte[] Read( TreEntry entry )
	{
		var stored = data.AsSpan( (int)entry.Offset, (int)entry.StoredSize ).ToArray();
		if ( entry.FullSize > 0x10000000 )
			throw new InvalidDataException( "TREE entry is too large." );
		return entry.Compression switch
		{
			TreCompressionKind.Stored => stored,
			TreCompressionKind.Refpack => TreCompression.Refpack( stored, (int)entry.FullSize ),
			_ => TreCompression.Explode( stored, (int)entry.FullSize ),
		};
	}

	/// <summary>The decoded bytes of the named entry, or null when absent.</summary>
	public byte[]? Read( string name ) => TryFind( name, out var entry ) ? Read( entry ) : null;

	// The launcher walks an implicit binary search tree in heap order: a node with a larger hash than the key
	// continues at p + step, a smaller one at p + step + 1, doubling the step each level.
	private uint FindIndex( uint hash )
	{
		var node = 0;
		var step = 1;
		while ( node < nodeHashes.Length )
		{
			if ( nodeHashes[node] == hash )
				return nodeIndices[node];
			if ( nodeHashes[node] > hash )
			{
				node += step;
				step *= 2;
			}
			else
			{
				node += step + 1;
				step = step * 2 + 1;
			}
		}
		return NotFound;
	}

	// Colliding names: the node holds EmptyNode as its index and the collision list repeats [length byte][name][u32 index].
	// No shipped archive has one (collision list size 0), so this follows the launcher's code without a sample.
	private uint FindCollision( string name )
	{
		var wanted = name.ToUpperInvariant();
		var position = collisionOffset;
		var end = collisionOffset + collisionSize;
		while ( position < end )
		{
			var length = data[position++];
			if ( position + length + 4 > data.Length )
				break;
			if ( Encoding.Latin1.GetString( data, position, length ).ToUpperInvariant() == wanted )
				return U32( position + length );
			position += length + 4;
		}
		return NotFound;
	}

	private uint U32( int at ) => BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( at ) );
}
