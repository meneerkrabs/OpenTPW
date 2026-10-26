using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-commands] The stable, versioned byte form of a <see cref="ParkCommand"/> (docs/COOP.md, "Serialization"). A command is an envelope (its
/// <see cref="Version"/>, its <see cref="ParkCommandKind"/> tag, the player) and its fields in their declared order: integers as zigzag LEB128 varints, strings as a
/// varint byte length and UTF-8, floats by their bit pattern, cell lists as a varint count and pairs. The same command always has the same bytes, on every platform.
/// A change of any command's fields takes a new generation, written by the kinds that changed (<see cref="VersionOf"/>); the schema tests pin the bytes of every kind in each.
/// </summary>
public static class ParkCommandCodec
{
	/// <summary>
	/// The newest command format. Each kind writes its own generation (<see cref="VersionOf"/>): version 1 for every legacy kind, <see cref="QueueVersion"/> for the native queue kinds,
	/// and [EXT:online-coop-commands] <see cref="ExpectationVersion"/> for a stale-checked command that carries its expectation (C4b), so every command an older build wrote keeps its bytes.
	/// </summary>
	public const int Version = 3;
	/// <summary>The native queue kinds' generation (<see cref="QueueSegmentCommand"/>, <see cref="UndoQueueSegmentCommand"/>).</summary>
	public const int QueueVersion = 2;
	/// <summary>
	/// [EXT:online-coop-commands] The generation of a stale-checked command with its expectation (<see cref="IStaleChecked"/>: a guest's level upgrade, its cancellation and its research
	/// move, C4b), whose expectation fields follow its own; the same kinds without one keep version 1.
	/// </summary>
	public const int ExpectationVersion = 3;
	internal static bool IsNativeQueue( ParkCommand command ) => command is QueueSegmentCommand or UndoQueueSegmentCommand;
	/// <summary>The generation <paramref name="command"/> is written in.</summary>
	internal static int VersionOf( ParkCommand command ) => IsNativeQueue( command ) ? QueueVersion : command is IStaleChecked { HasExpectation: true } ? ExpectationVersion : 1;
	private static bool IsStaleCheckedKind( ParkCommandKind kind ) => kind is ParkCommandKind.BuyUpgrade or ParkCommandKind.CancelUpgrade or ParkCommandKind.MoveResearch;
	/// <summary>The bound of a cell coordinate a command may carry (the largest park map is far smaller).</summary>
	public const int MaximumCoordinate = 4096;
	/// <summary>The most cells one command's cell list may hold.</summary>
	public const int MaximumCells = 4096;
	/// <summary>The longest string (in UTF-8 bytes) one command may hold.</summary>
	public const int MaximumString = 512;
	/// <summary>The most bytes one encoded command may take.</summary>
	public const int MaximumBytes = 64 * 1024;

	/// <summary>The key a command names a catalogue entry by: its content root's key prefix and its archive path (the asset manifest's identity, <c>ContentRoot.KeyPrefix</c>).</summary>
	public static string EntryKey( ObjectCatalogEntry entry )
	{
		ArgumentNullException.ThrowIfNull( entry );
		return entry.Root.KeyPrefix + entry.ArchivePath;
	}

	/// <summary>Whether a line command's mode and ride go together: a path line names no ride, a queue line its ride's attraction id.</summary>
	internal static bool IsLineMode( CellToolMode mode, int ride ) => mode switch
	{
		CellToolMode.Path => ride == -1,
		CellToolMode.Queue => ride >= 0,
		_ => false
	};

	/// <summary>The command's bytes.</summary>
	public static byte[] Encode( ParkCommand command )
	{
		ArgumentNullException.ThrowIfNull( command );
		var writer = new ParkCommandWriter();
		writer.Int( VersionOf( command ) );
		writer.Byte( (byte)command.Kind );
		writer.Int( command.Player );
		command.WriteFields( writer );
		var bytes = writer.ToArray();
		if ( bytes.Length > MaximumBytes )
			throw new InvalidOperationException( $"A {command.Kind} command takes {bytes.Length} bytes, more than {MaximumBytes}." );
		return bytes;
	}

	/// <summary>Reads one command; throws <see cref="InvalidDataException"/> for bytes that are not exactly one well-formed command of a generation its kind has.</summary>
	public static ParkCommand Decode( ReadOnlySpan<byte> bytes )
	{
		if ( bytes.Length > MaximumBytes )
			throw new InvalidDataException( $"A command of {bytes.Length} bytes is longer than {MaximumBytes}." );
		var reader = new ParkCommandReader( bytes.ToArray() );
		var version = reader.Int();
		if ( version is not (1 or QueueVersion or ExpectationVersion) )
			throw new InvalidDataException( $"Park command version {version} is none of 1 to {Version}." );
		var kind = (ParkCommandKind)reader.Byte();
		if ( version == 1 && kind is ParkCommandKind.QueueSegment or ParkCommandKind.UndoQueueSegment )
			throw new InvalidDataException( "Native queue commands require version 2." );
		if ( version == QueueVersion && kind is not (ParkCommandKind.QueueSegment or ParkCommandKind.UndoQueueSegment) )
			throw new InvalidDataException( "Legacy command kinds retain their canonical version 1 encoding." );
		// [EXT:online-coop-commands] version 3 is a stale-checked command's with its expectation, which the kind's reader then reads (C4b)
		if ( version == ExpectationVersion && !IsStaleCheckedKind( kind ) )
			throw new InvalidDataException( $"A {kind} command has no version {ExpectationVersion} form." );
		reader.Version = version;
		var player = reader.Int();
		if ( player < 0 )
			throw new InvalidDataException( $"Player {player} is negative." );
		ParkCommand command = kind switch
		{
			ParkCommandKind.PlaceObject => new PlaceObjectCommand( reader.String(), reader.Int(), reader.Int(), reader.Int(), reader.Bool(), reader.Int() ),
			ParkCommandKind.RemoveAt => new RemoveAtCommand( reader.Int(), reader.Int() ),
			ParkCommandKind.RemoveObjectAt => new RemoveObjectAtCommand( reader.Int(), reader.Int() ),
			ParkCommandKind.ClearArea => new ClearAreaCommand( reader.Int(), reader.Int(), reader.Int(), reader.Int() ),
			ParkCommandKind.RemoveAttraction => new RemoveAttractionCommand( reader.Int() ),
			ParkCommandKind.PickUpObject => new PickUpObjectCommand( reader.Int() ),
			ParkCommandKind.EndMove => new EndMoveCommand(),
			ParkCommandKind.QueueSegment => new QueueSegmentCommand( reader.Int(), reader.Int(), reader.Int(), reader.Int(), reader.Int() ),
			ParkCommandKind.UndoQueueSegment => new UndoQueueSegmentCommand( reader.Int(), reader.Int(), reader.Int(), reader.Int(), reader.Int() ),
			ParkCommandKind.LayLine => new LayLineCommand( (CellToolMode)reader.Byte(), reader.Int(), reader.Int(), reader.Int(), reader.Int(), reader.Int() ),
			ParkCommandKind.UndoLine => new UndoLineCommand( (CellToolMode)reader.Byte(), reader.Int(), reader.Int(), reader.Int(), reader.Cells(), reader.Cells() ),
			ParkCommandKind.RemovePathCell => new RemovePathCellCommand( reader.Int(), reader.Int() ),
			ParkCommandKind.RemoveQueueCell => new RemoveQueueCellCommand( reader.Int(), reader.Int() ),
			ParkCommandKind.Backtrack => new BacktrackCommand( reader.Int(), reader.Int() ),
			ParkCommandKind.EditQueue => new EditQueueCommand( reader.Int(), reader.Int() ),
			ParkCommandKind.PlacePrototype => new PlacePrototypeCommand( reader.Float(), reader.Float(), reader.Float() ),
			ParkCommandKind.RemovePrototype => new RemovePrototypeCommand(),
			ParkCommandKind.DismissFinale => new DismissFinaleCommand(),
			ParkCommandKind.SetPrototypeRunning => new SetPrototypeRunningCommand( reader.Bool() ),
			_ => (ParkCommand?)ParkSettingCommand.Read( kind, reader ) ?? ParkToolCommand.Read( kind, reader ) ?? throw new InvalidDataException( $"Unknown park command kind {(int)kind}." )
		};
		if ( !reader.AtEnd )
			throw new InvalidDataException( $"A {kind} command has {reader.Remaining} bytes after its fields." );
		command = command with { Player = player };
		if ( !command.IsWellFormed )
			throw new InvalidDataException( $"A {kind} command has fields out of range." );
		return command;
	}

	/// <summary>The command's bytes as lower-case hex (the schema test's golden form).</summary>
	public static string ToHex( ParkCommand command ) => Convert.ToHexString( Encode( command ) ).ToLowerInvariant();
}

/// <summary>Writes the codec's primitive values (<see cref="ParkCommandCodec"/>).</summary>
public sealed class ParkCommandWriter
{
	private readonly MemoryStream stream = new();

	public void Byte( byte value ) => stream.WriteByte( value );

	public void Bool( bool value ) => stream.WriteByte( value ? (byte)1 : (byte)0 );

	public void Int( int value ) => Long( value );

	/// <summary>A zigzag LEB128 varint.</summary>
	public void Long( long value )
	{
		var zigzag = unchecked( (ulong)((value << 1) ^ (value >> 63)) );
		do
		{
			var low = (byte)(zigzag & 0x7F);
			zigzag >>= 7;
			stream.WriteByte( zigzag != 0 ? (byte)(low | 0x80) : low );
		}
		while ( zigzag != 0 );
	}

	/// <summary>A float by its bit pattern, little-endian.</summary>
	public void Float( float value )
	{
		Span<byte> bytes = stackalloc byte[4];
		BinaryPrimitives.WriteInt32LittleEndian( bytes, BitConverter.SingleToInt32Bits( value ) );
		stream.Write( bytes );
	}

	public void UInt64( ulong value )
	{
		Span<byte> bytes = stackalloc byte[8];
		BinaryPrimitives.WriteUInt64LittleEndian( bytes, value );
		stream.Write( bytes );
	}

	public void String( string value )
	{
		ArgumentNullException.ThrowIfNull( value );
		var bytes = Encoding.UTF8.GetBytes( value );
		if ( bytes.Length > ParkCommandCodec.MaximumString )
			throw new ArgumentException( $"A command string is at most {ParkCommandCodec.MaximumString} bytes.", nameof( value ) );
		Int( bytes.Length );
		stream.Write( bytes );
	}

	public void Bytes( ReadOnlySpan<byte> value )
	{
		Int( value.Length );
		stream.Write( value );
	}

	public void Cells( IReadOnlyList<(int X, int Y)> cells )
	{
		ArgumentNullException.ThrowIfNull( cells );
		Int( cells.Count );
		foreach ( var (x, y) in cells )
		{
			Int( x );
			Int( y );
		}
	}

	public byte[] ToArray() => stream.ToArray();
}

/// <summary>Reads the codec's primitive values; every read past the end or out of range throws <see cref="InvalidDataException"/>.</summary>
public sealed class ParkCommandReader
{
	private readonly byte[] bytes;
	private int position;

	public ParkCommandReader( byte[] bytes ) => this.bytes = bytes ?? throw new ArgumentNullException( nameof( bytes ) );

	/// <summary>The generation of the command being read (<see cref="ParkCommandCodec.VersionOf"/>): a stale-checked kind reads its expectation in <see cref="ParkCommandCodec.ExpectationVersion"/>.</summary>
	public int Version { get; internal set; } = 1;

	/// <summary>[EXT:online-coop-commands] Whether the command being read carries a stale-checked command's expectation (C4b).</summary>
	internal bool HasExpectation => Version == ParkCommandCodec.ExpectationVersion;

	public bool AtEnd => position == bytes.Length;
	public int Remaining => bytes.Length - position;

	public byte Byte()
	{
		if ( position >= bytes.Length )
			throw new InvalidDataException( "The command ends inside a field." );
		return bytes[position++];
	}

	public bool Bool() => Byte() switch
	{
		0 => false,
		1 => true,
		var other => throw new InvalidDataException( $"A boolean byte is {other}." )
	};

	public int Int()
	{
		var value = Long();
		if ( value is < int.MinValue or > int.MaxValue )
			throw new InvalidDataException( $"The integer {value} does not fit 32 bits." );
		return (int)value;
	}

	public long Long()
	{
		ulong zigzag = 0;
		for ( var shift = 0; ; shift += 7 )
		{
			if ( shift > 63 )
				throw new InvalidDataException( "A varint is longer than 64 bits." );
			var next = Byte();
			// the tenth byte holds only bit 63: anything above it would be shifted out, a second byte form of the same value
			if ( shift == 63 && next > 1 )
				throw new InvalidDataException( "A varint is longer than 64 bits." );
			zigzag |= (ulong)(next & 0x7F) << shift;
			if ( (next & 0x80) == 0 )
			{
				// the shortest form only: a last byte of zero after the first is a padded varint
				if ( next == 0 && shift > 0 )
					throw new InvalidDataException( "A varint is not in its shortest form." );
				break;
			}
		}
		return (long)(zigzag >> 1) ^ -(long)(zigzag & 1);
	}

	public float Float() => BitConverter.Int32BitsToSingle( BinaryPrimitives.ReadInt32LittleEndian( Take( 4 ) ) );

	public ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian( Take( 8 ) );

	public string String()
	{
		var length = Int();
		if ( length < 0 || length > ParkCommandCodec.MaximumString )
			throw new InvalidDataException( $"A command string of {length} bytes is out of range." );
		try
		{
			return new UTF8Encoding( false, true ).GetString( Take( length ) );
		}
		catch ( DecoderFallbackException error )
		{
			throw new InvalidDataException( "A command string is not UTF-8.", error );
		}
	}

	public byte[] Bytes( int maximum )
	{
		var length = Int();
		if ( length < 0 || length > maximum )
			throw new InvalidDataException( $"A byte field of {length} bytes is out of range." );
		return Take( length ).ToArray();
	}

	public IReadOnlyList<(int X, int Y)> Cells()
	{
		var count = Int();
		if ( count < 0 || count > ParkCommandCodec.MaximumCells )
			throw new InvalidDataException( $"A cell list of {count} cells is out of range." );
		var cells = new (int X, int Y)[count];
		for ( var index = 0; index < count; index++ )
			cells[index] = (Int(), Int());
		return cells;
	}

	private ReadOnlySpan<byte> Take( int count )
	{
		if ( count > Remaining )
			throw new InvalidDataException( "The command ends inside a field." );
		var span = bytes.AsSpan( position, count );
		position += count;
		return span;
	}
}
