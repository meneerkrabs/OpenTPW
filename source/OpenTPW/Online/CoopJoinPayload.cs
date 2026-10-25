using System.Buffers.Binary;
using System.IO.Compression;
using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-snapshot-join] The session state a join snapshot carries beyond the park's save (docs/COOP.md, "Joining a running session"): what the session digest covers that the save
/// leaves out or a load resets. Every player's carried catalogue entry and employee in hand, the pending hire and the stale on-pointer candidate, the coasters' being-edited marks (by their
/// station's attraction id), the session clock, and the cells each coaster's marker binding wants and does not hold (by the station's attraction id; its displaced cells marked), which the
/// save leaves out and a join's adoption of the binding needs; and the guests' seats the host granted building ([EXT:online-coop-permissions], C3). A capture waits while a coaster drag is held,
/// so it carries no drag.
/// </summary>
internal sealed record CoopJoinState(
	IReadOnlyList<(int Player, string Entry)> CarriedEntries,
	IReadOnlyList<(int Player, int Staff, int X, int Y)> StaffHands,
	int PendingHire,
	int StaleOnPointer,
	IReadOnlyList<int> CoastersBeingEdited,
	long? SessionClockTicks,
	IReadOnlyList<(int Station, int X, int Y, bool Displaced)> CoasterUnheldCells,
	IReadOnlyList<int>? BuildGrants = null )
{
	public const ushort Version = 3;
	/// <summary>The most unheld coaster cells a join state carries (no player tool makes one today: it is a defence).</summary>
	public const int MaximumUnheldCells = 4096;
	/// <summary>The most entries of a per-player list: one a player, with room for a replay's other player numbers.</summary>
	public const int MaximumPlayerEntries = 64;
	/// <summary>A carried entry's key at most: the command codec's string bound (<see cref="ParkCommandCodec.MaximumString"/>).</summary>
	public const int MaximumEntryKeyLength = ParkCommandCodec.MaximumString;

	public byte[] Encode()
	{
		var grants = BuildGrants ?? Array.Empty<int>();
		if ( CarriedEntries.Count > MaximumPlayerEntries || StaffHands.Count > MaximumPlayerEntries || CoastersBeingEdited.Count > CoasterSave.MaximumRides
			|| CoasterUnheldCells.Count > MaximumUnheldCells || grants.Count > MaximumPlayerEntries )
			throw new InvalidOperationException( "The join state holds more entries than a join snapshot carries." );
		var writer = new CoopWriter();
		writer.U16( Version );
		writer.Byte( (byte)CarriedEntries.Count );
		foreach ( var (player, entry) in CarriedEntries )
		{
			writer.U32( Unsigned( player ) );
			writer.Text( entry, MaximumEntryKeyLength );
		}
		writer.Byte( (byte)StaffHands.Count );
		foreach ( var (player, staff, x, y) in StaffHands )
		{
			writer.U32( Unsigned( player ) );
			writer.U32( Unsigned( staff ) );
			writer.U32( Unsigned( x ) );
			writer.U32( Unsigned( y ) );
		}
		writer.U32( Unsigned( PendingHire ) );
		writer.U32( Unsigned( StaleOnPointer ) );
		writer.U16( (ushort)CoastersBeingEdited.Count );
		foreach ( var station in CoastersBeingEdited )
			writer.U32( Unsigned( station ) );
		writer.Bool( SessionClockTicks.HasValue );
		if ( SessionClockTicks is { } ticks )
			writer.U64( checked( (ulong)ticks ) );
		writer.U16( (ushort)CoasterUnheldCells.Count );
		foreach ( var (station, x, y, displaced) in CoasterUnheldCells )
		{
			writer.U32( Unsigned( station ) );
			writer.U32( Unsigned( x ) );
			writer.U32( Unsigned( y ) );
			writer.Bool( displaced );
		}
		writer.Byte( (byte)grants.Count );
		foreach ( var seat in grants )
			writer.U32( Unsigned( seat ) );
		return writer.ToArray();
	}

	/// <summary>Reads a join state; anything malformed (another version, a count past its bound, a negative number, a duplicate player, bytes after the fields) throws <see cref="InvalidDataException"/>.</summary>
	public static CoopJoinState Decode( ReadOnlySpan<byte> bytes )
	{
		var reader = new CoopReader( bytes );
		if ( reader.U16() != Version )
			throw new InvalidDataException( "The join state has an unknown version." );
		var carried = new (int, string)[Count( reader.Byte(), MaximumPlayerEntries )];
		for ( var index = 0; index < carried.Length; index++ )
			carried[index] = (NonNegative( reader.U32() ), reader.Text( MaximumEntryKeyLength ));
		var hands = new (int, int, int, int)[Count( reader.Byte(), MaximumPlayerEntries )];
		for ( var index = 0; index < hands.Length; index++ )
			hands[index] = (NonNegative( reader.U32() ), NonNegative( reader.U32() ), Signed( reader.U32() ), Signed( reader.U32() ));
		var pending = NonNegative( reader.U32() );
		var stale = NonNegative( reader.U32() );
		var coasters = new int[Count( reader.U16(), CoasterSave.MaximumRides )];
		for ( var index = 0; index < coasters.Length; index++ )
			coasters[index] = NonNegative( reader.U32() );
		long? clock = reader.Bool() ? reader.U64() is var ticks && ticks <= (ulong)DateTime.MaxValue.Ticks ? (long)ticks
			: throw new InvalidDataException( "The join state's session clock is out of range." ) : null;
		var unheld = new (int, int, int, bool)[Count( reader.U16(), MaximumUnheldCells )];
		for ( var index = 0; index < unheld.Length; index++ )
			unheld[index] = (NonNegative( reader.U32() ), Signed( reader.U32() ), Signed( reader.U32() ), reader.Bool());
		var grants = new int[Count( reader.Byte(), MaximumPlayerEntries )];
		for ( var index = 0; index < grants.Length; index++ )
			grants[index] = NonNegative( reader.U32() ) is var seat and > 0 and < MaximumPlayerEntries ? seat : throw new InvalidDataException( "The join state grants a seat out of range." );
		reader.End();
		if ( carried.Select( item => item.Item1 ).Distinct().Count() != carried.Length || hands.Select( item => item.Item1 ).Distinct().Count() != hands.Length
			|| coasters.Distinct().Count() != coasters.Length || unheld.Select( item => (item.Item1, item.Item2, item.Item3) ).Distinct().Count() != unheld.Length
			|| grants.Distinct().Count() != grants.Length )
			throw new InvalidDataException( "The join state names a player, a coaster, a coaster's cell or a granted seat twice." );
		return new CoopJoinState( carried, hands, pending, stale, coasters, clock, unheld, grants );
	}

	private static uint Unsigned( int value ) => unchecked( (uint)value );
	private static int Signed( uint value ) => unchecked( (int)value );
	private static int NonNegative( uint value ) => value <= int.MaxValue ? (int)value : throw new InvalidDataException( "The join state has a number out of range." );
	private static int Count( int count, int maximum ) => count <= maximum ? count : throw new InvalidDataException( "The join state holds too many entries." );
}

/// <summary>
/// [EXT:online-coop-snapshot-join] A join snapshot (docs/COOP.md, "Joining a running session"): the host's park at a cut of its command stream. Every record up to <see cref="Records"/> is
/// applied and published, tick <see cref="Ticks"/> runs next and frame <see cref="Frame"/> is the last published, whose horizon is <see cref="Ticks"/>; the session digest and the world hash at
/// the cut, the session state beyond the save, and the park's save. On the wire: "OTPWJOIN", the version, the capture's generation and the body's length, then the body compressed with zlib
/// (the browser runtime has no Brotli).
/// </summary>
internal sealed record CoopJoinPayload( uint Generation, long Records, long Ticks, ulong Frame, ulong Digest, ulong WorldHash, CoopJoinState State, byte[] Save )
{
	public static ReadOnlySpan<byte> Magic => "OTPWJOIN"u8;
	public const ushort Version = 1;
	/// <summary>The bytes before the compressed body: the magic, the version, the generation and the body's length.</summary>
	public const int HeaderBytes = 8 + 2 + 4 + 4;
	/// <summary>The body's bytes before the join state: the cut, the digest and the world hash.</summary>
	public const int CutBytes = 8 + 8 + 8 + 8 + 8;
	/// <summary>The most bytes a join state takes in a body.</summary>
	public const int MaximumStateBytes = 1 << 20;
	/// <summary>The most bytes a body may declare: the save's own cap, a join state and the cut.</summary>
	public const int MaximumBodyBytes = ParkResumeSaveFile.MaximumFileSize + MaximumStateBytes + CutBytes + 8;

	/// <summary>The uncompressed body: the cut, the digest, the world hash, the join state and the save, each a length before it.</summary>
	internal byte[] Body()
	{
		var state = State.Encode();
		var body = new byte[CutBytes + 4 + state.Length + 4 + Save.Length];
		var span = body.AsSpan();
		BinaryPrimitives.WriteInt64LittleEndian( span, Records );
		BinaryPrimitives.WriteInt64LittleEndian( span[8..], Ticks );
		BinaryPrimitives.WriteUInt64LittleEndian( span[16..], Frame );
		BinaryPrimitives.WriteUInt64LittleEndian( span[24..], Digest );
		BinaryPrimitives.WriteUInt64LittleEndian( span[32..], WorldHash );
		BinaryPrimitives.WriteUInt32LittleEndian( span[CutBytes..], (uint)state.Length );
		state.CopyTo( span[(CutBytes + 4)..] );
		BinaryPrimitives.WriteUInt32LittleEndian( span[(CutBytes + 4 + state.Length)..], (uint)Save.Length );
		Save.CopyTo( span[(CutBytes + 8 + state.Length)..] );
		return body;
	}

	/// <summary>The whole payload in one go (the tests; the host compresses in slices, <see cref="CoopJoinPayloadWriter"/>).</summary>
	public byte[] Encode()
	{
		var writer = new CoopJoinPayloadWriter( this );
		while ( !writer.Run( TimeSpan.MaxValue ) )
		{
		}
		return writer.Bytes;
	}

	/// <summary>
	/// Reads a payload: the header first, the declared body length bounded before anything is decompressed, then exactly that many bytes, whose Adler-32 must be the zlib stream's last four bytes
	/// (a stream cut inside its trailer still inflates the whole body); anything else (another magic or version, a body that ends early or goes on, bytes after the stream, a malformed join
	/// state, a save past its cap) throws <see cref="InvalidDataException"/>. The save's own contents are checked by its loader.
	/// </summary>
	public static CoopJoinPayload Decode( ReadOnlySpan<byte> bytes )
	{
		if ( bytes.Length < HeaderBytes || !bytes[..8].SequenceEqual( Magic ) )
			throw new InvalidDataException( "This is no join snapshot." );
		if ( BinaryPrimitives.ReadUInt16LittleEndian( bytes[8..] ) != Version )
			throw new InvalidDataException( "The join snapshot has an unknown version." );
		var generation = BinaryPrimitives.ReadUInt32LittleEndian( bytes[10..] );
		var declared = BinaryPrimitives.ReadUInt32LittleEndian( bytes[14..] );
		if ( declared < CutBytes + 8 || declared > MaximumBodyBytes )
			throw new InvalidDataException( "The join snapshot declares a body out of range." );
		var body = new byte[declared];
		using ( var input = new ZLibStream( new MemoryStream( bytes[HeaderBytes..].ToArray(), false ), CompressionMode.Decompress ) )
		{
			try
			{
				input.ReadExactly( body );
				if ( input.ReadByte() != -1 )
					throw new InvalidDataException( "The join snapshot's body is longer than it says." );
			}
			catch ( EndOfStreamException )
			{
				throw new InvalidDataException( "The join snapshot's body ends early." );
			}
		}
		if ( bytes.Length < HeaderBytes + 4 || BinaryPrimitives.ReadUInt32BigEndian( bytes[^4..] ) != Adler32( body ) )
			throw new InvalidDataException( "The join snapshot's body does not end as its checksum says." );
		var span = body.AsSpan();
		var records = BinaryPrimitives.ReadInt64LittleEndian( span );
		var ticks = BinaryPrimitives.ReadInt64LittleEndian( span[8..] );
		var frame = BinaryPrimitives.ReadUInt64LittleEndian( span[16..] );
		if ( records < 0 || ticks < 0 || frame > long.MaxValue )
			throw new InvalidDataException( "The join snapshot's cut is out of range." );
		var stateLength = BinaryPrimitives.ReadUInt32LittleEndian( span[CutBytes..] );
		if ( stateLength > MaximumStateBytes || CutBytes + 8 + stateLength > (ulong)body.Length )
			throw new InvalidDataException( "The join snapshot's session state is out of range." );
		var state = CoopJoinState.Decode( span.Slice( CutBytes + 4, (int)stateLength ) );
		var saveAt = CutBytes + 4 + (int)stateLength;
		var saveLength = BinaryPrimitives.ReadUInt32LittleEndian( span[saveAt..] );
		if ( saveLength > ParkResumeSaveFile.MaximumFileSize || saveAt + 4 + (long)saveLength != body.Length )
			throw new InvalidDataException( "The join snapshot's save is out of range." );
		return new CoopJoinPayload( generation, records, ticks, frame, BinaryPrimitives.ReadUInt64LittleEndian( span[24..] ), BinaryPrimitives.ReadUInt64LittleEndian( span[32..] ), state,
			span.Slice( saveAt + 4 ).ToArray() );
	}

	/// <summary>The Adler-32 of <paramref name="data"/>, the checksum a zlib stream ends with (RFC 1950).</summary>
	internal static uint Adler32( ReadOnlySpan<byte> data )
	{
		const uint Modulus = 65521;
		// the most bytes before the sums must be reduced so they cannot overflow (zlib's NMAX)
		const int Run = 5552;
		uint a = 1, b = 0;
		while ( data.Length > 0 )
		{
			var run = data[..Math.Min( Run, data.Length )];
			foreach ( var value in run )
			{
				a += value;
				b += a;
			}
			a %= Modulus;
			b %= Modulus;
			data = data[run.Length..];
		}
		return (b << 16) | a;
	}
}

/// <summary>
/// [EXT:online-coop-snapshot-join] Writes a join payload in time-boxed slices on the game thread (the browser has no worker): the header, then the body through zlib a chunk at a time, so a large
/// park's compression spreads over frames as the periodic autosave's serialization does.
/// </summary>
internal sealed class CoopJoinPayloadWriter
{
	/// <summary>The body's bytes compressed in one step of a slice.</summary>
	public const int ChunkBytes = 256 * 1024;

	private readonly byte[] body;
	private readonly MemoryStream output = new();
	private ZLibStream? zlib;
	private int written;
	private byte[]? bytes;

	public CoopJoinPayloadWriter( CoopJoinPayload payload )
	{
		ArgumentNullException.ThrowIfNull( payload );
		body = payload.Body();
		if ( body.Length > CoopJoinPayload.MaximumBodyBytes )
			throw new CoopSnapshotTooLargeException( "The park is too large for a join snapshot." );
		Span<byte> header = stackalloc byte[CoopJoinPayload.HeaderBytes];
		CoopJoinPayload.Magic.CopyTo( header );
		BinaryPrimitives.WriteUInt16LittleEndian( header[8..], CoopJoinPayload.Version );
		BinaryPrimitives.WriteUInt32LittleEndian( header[10..], payload.Generation );
		BinaryPrimitives.WriteUInt32LittleEndian( header[14..], (uint)body.Length );
		output.Write( header );
		zlib = new ZLibStream( output, CompressionLevel.Fastest, leaveOpen: true );
	}

	/// <summary>The slices so far.</summary>
	public int Slices { get; private set; }

	/// <summary>The uncompressed body's bytes.</summary>
	public int BodyBytes => body.Length;

	/// <summary>[EXT:online-coop-snapshot-join] Of the last slice: the milliseconds zlib's end and the payload's copy took (docs/COOP.md, "Measurements").</summary>
	public double FinishMilliseconds { get; private set; }

	/// <summary>The payload once <see cref="Run"/> returned true.</summary>
	public byte[] Bytes => bytes ?? throw new InvalidOperationException( "The join payload is not written yet." );

	/// <summary>Compresses chunks of the body until <paramref name="budget"/> is spent (at least one); true once the payload is complete.</summary>
	public bool Run( TimeSpan budget )
	{
		if ( bytes != null )
			return true;
		Slices++;
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		do
		{
			var count = Math.Min( ChunkBytes, body.Length - written );
			zlib!.Write( body, written, count );
			written += count;
		}
		while ( written < body.Length && System.Diagnostics.Stopwatch.GetElapsedTime( start ) < budget );
		if ( written < body.Length )
			return false;
		var finishing = System.Diagnostics.Stopwatch.GetTimestamp();
		zlib!.Dispose();
		zlib = null;
		bytes = output.ToArray();
		FinishMilliseconds = CoopClock.Since( finishing );
		return true;
	}
}

/// <summary>[EXT:online-coop-snapshot-join] A park too large for a join snapshot (its save past the save's cap, or its body past the payload's): the capture fails for good, as too large.</summary>
internal sealed class CoopSnapshotTooLargeException( string message ) : Exception( message );
