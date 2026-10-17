using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>The kind of one <see cref="ThemeParkIncBodySegment"/>.</summary>
public enum ThemeParkIncBodySegmentKind
{
	/// <summary>A member of 1, 2, 4, 8 or 12 bytes, copied verbatim.</summary>
	Field,
	/// <summary>A group of segments read <see cref="ThemeParkIncBodySegment.Count"/> times (an array of records).</summary>
	Repeat,
	/// <summary>A group of segments read once (an embedded record).</summary>
	Group,
	/// <summary>A history ring: <c>u32 mCurrentEntry, u32 mNumEntries, u8 mWrappedAround, mTemp</c>, then <c>mNumEntries</c> entries of the element size.</summary>
	Ring
}

/// <summary>One segment of a thing body layout. <see cref="Size"/> is a field's width or a ring's element width; <see cref="Capacity"/> a ring's in-memory capacity.</summary>
public sealed class ThemeParkIncBodySegment
{
	internal ThemeParkIncBodySegment( ThemeParkIncBodySegmentKind kind, string name, int size, int count, int capacity, IReadOnlyList<ThemeParkIncBodySegment> children )
	{
		Kind = kind;
		Name = name;
		Size = size;
		Count = count;
		Capacity = capacity;
		Children = children;
	}

	public ThemeParkIncBodySegmentKind Kind { get; }
	/// <summary>The member name from the original's serializer (its field log); <c>_</c> for the unnamed element of an array.</summary>
	public string Name { get; }
	public int Size { get; }
	public int Count { get; }
	public int Capacity { get; }
	public IReadOnlyList<ThemeParkIncBodySegment> Children { get; }
}

/// <summary>One field of a thing body: its dotted path (<c>mQueue[3].mScore</c>), its offset from the start of the body and its width.</summary>
public readonly record struct ThemeParkIncBodyField( string Path, int Offset, int Size );

/// <summary>
/// The traced layout of a Theme Park Inc thing body, written from the model's load routine in <c>Game.exe</c> (the thing dispatch of the World loader 0x64bc00, then each class's
/// load virtual: the stream read helper 0x7cc930 with a size and a destination per member, in order). A layout is a list of fields, arrays and history rings; the width of a body
/// is the sum of its fixed parts plus <c>mNumEntries</c> entries for every ring, so a body is measured by reading its ring counts, never by searching for the next thing header.
/// The version check of every load routine accepts 0 and 1 (the loader passes 1) and no member read is conditional on stream data apart from the ring counts.
/// </summary>
// [BIN:TPI-EXE:0x64c771 thing dispatch] cmp eax(model - 1), 0x16; jmp [eax*4 + 0x64d7e0]. Per case: allocate, construct, set the thing id, then call the class load with (stream, 0 = load, version 1, id); the same routine also saves.
// [BIN:TPI-EXE:0x637230 thing base load] mX, mY, mMapChild, mMapParent (2 bytes each) start every body.
// [BIN:TPI-EXE:0x62f2b0 history ring] mCurrentEntry 4, mNumEntries 4, mWrappedAround 1, mTemp (the element size) and then mNumEntries elements read one by one into the ring's array in the object; the array holds Capacity elements, and the (Capacity + 1)th entry would overwrite mCurrentEntry in memory, so a count above the capacity is not a ring the original could have written
// [APPROX:SAVE-944] the guest, staff and gardener layouts are read from their load routines and an emulation of them, not from any save; a word is typed by width only; a ring count above the in-memory capacity is refused where the original would overrun the ring
public sealed partial class ThemeParkIncBodyLayout
{
	/// <summary>The bytes before a ring's mTemp: mCurrentEntry, mNumEntries and mWrappedAround.</summary>
	public const int RingHeaderBytes = 4 + 4 + 1;

	private static readonly Lazy<IReadOnlyDictionary<ThemeParkIncThingModel, ThemeParkIncBodyLayout>> byModel = new( Build );

	private ThemeParkIncBodyLayout( string name, IReadOnlyList<ThemeParkIncBodySegment> segments )
	{
		Name = name;
		Segments = segments;
		MinimumBytes = Bound( segments, full: false );
		MaximumBytes = Bound( segments, full: true );
	}

	public string Name { get; }
	public IReadOnlyList<ThemeParkIncBodySegment> Segments { get; }
	/// <summary>The width when every ring is empty.</summary>
	public int MinimumBytes { get; }
	/// <summary>The width when every ring is full.</summary>
	public int MaximumBytes { get; }

	/// <summary>The layouts of the models with a traced variable or fixed record body (the guests, staff, gardener, staff HQ, advisor controller, park analyser, research lab, weather and challenge system).</summary>
	public static IReadOnlyDictionary<ThemeParkIncThingModel, ThemeParkIncBodyLayout> All => byModel.Value;

	public static ThemeParkIncBodyLayout? For( ThemeParkIncThingModel model ) => byModel.Value.GetValueOrDefault( model );

	/// <summary>
	/// The width of the body that starts at <paramref name="offset"/>. Throws <see cref="InvalidDataException"/> when the body crosses the end of the data or a ring holds more
	/// entries than its capacity.
	/// </summary>
	public int Measure( ReadOnlySpan<byte> payload, int offset )
	{
		if ( offset < 0 || offset > payload.Length )
			throw new InvalidDataException( $"Save Theme Park Inc {Name} body starts outside the data." );
		return Skip( payload, offset, Segments ) - offset;
	}

	/// <summary>Every field of the body (relative offsets), rings expanded to their header, <c>mTemp</c> and entries. <paramref name="body"/> must be exactly one body.</summary>
	public IReadOnlyList<ThemeParkIncBodyField> Fields( ReadOnlySpan<byte> body )
	{
		var fields = new List<ThemeParkIncBodyField>();
		var end = Collect( body, 0, Segments, string.Empty, fields );
		if ( end != body.Length )
			throw new InvalidDataException( $"Save Theme Park Inc {Name} body is {body.Length} bytes, but its layout reads {end}." );
		return fields.AsReadOnly();
	}

	private int Skip( ReadOnlySpan<byte> payload, int position, IReadOnlyList<ThemeParkIncBodySegment> segments )
	{
		foreach ( var segment in segments )
		{
			switch ( segment.Kind )
			{
				case ThemeParkIncBodySegmentKind.Field:
					position = Advance( payload, position, segment.Size );
					break;
				case ThemeParkIncBodySegmentKind.Repeat:
					for ( var index = 0; index < segment.Count; index++ )
						position = Skip( payload, position, segment.Children );
					break;
				case ThemeParkIncBodySegmentKind.Group:
					position = Skip( payload, position, segment.Children );
					break;
				default:
					position = Advance( payload, position, RingHeaderBytes + segment.Size, segment, out var count );
					position = Advance( payload, position, checked( (int)count * segment.Size ) );
					break;
			}
		}
		return position;
	}

	private int Advance( ReadOnlySpan<byte> payload, int position, int count )
	{
		if ( payload.Length - position < count )
			throw new InvalidDataException( $"Save Theme Park Inc {Name} body crosses the end of the data at offset {position} (needs {count} more bytes, {payload.Length - position} remain)." );
		return position + count;
	}

	/// <summary>Advances over a ring's header and mTemp and returns its mNumEntries, checked against the ring's capacity.</summary>
	private int Advance( ReadOnlySpan<byte> payload, int position, int count, ThemeParkIncBodySegment ring, out uint entries )
	{
		var end = Advance( payload, position, count );
		entries = BinaryPrimitives.ReadUInt32LittleEndian( payload[(position + 4)..] );
		if ( entries > ring.Capacity )
			throw new InvalidDataException( $"Save Theme Park Inc {Name} body has a history ring of {entries} entries at offset {position}, but its capacity is {ring.Capacity}." );
		return end;
	}

	private int Collect( ReadOnlySpan<byte> body, int position, IReadOnlyList<ThemeParkIncBodySegment> segments, string prefix, List<ThemeParkIncBodyField> fields )
	{
		foreach ( var segment in segments )
		{
			switch ( segment.Kind )
			{
				case ThemeParkIncBodySegmentKind.Field:
					fields.Add( new ThemeParkIncBodyField( segment.Name == "_" ? prefix.TrimEnd( '.' ) : prefix + segment.Name, position, segment.Size ) );
					position = Advance( body, position, segment.Size );
					break;
				case ThemeParkIncBodySegmentKind.Repeat:
					for ( var index = 0; index < segment.Count; index++ )
						position = Collect( body, position, segment.Children, $"{prefix}{segment.Name}[{index}].", fields );
					break;
				case ThemeParkIncBodySegmentKind.Group:
					position = Collect( body, position, segment.Children, $"{prefix}{segment.Name}.", fields );
					break;
				default:
				{
					var head = segment.Name == "_" ? prefix : prefix + segment.Name + ".";
					var start = position;
					position = Advance( body, position, RingHeaderBytes + segment.Size, segment, out var count );
					fields.Add( new ThemeParkIncBodyField( head + "mCurrentEntry", start, 4 ) );
					fields.Add( new ThemeParkIncBodyField( head + "mNumEntries", start + 4, 4 ) );
					fields.Add( new ThemeParkIncBodyField( head + "mWrappedAround", start + 8, 1 ) );
					fields.Add( new ThemeParkIncBodyField( head + "mTemp", start + RingHeaderBytes, segment.Size ) );
					for ( var entry = 0; entry < count; entry++ )
						fields.Add( new ThemeParkIncBodyField( $"{head}mData[{entry}]", position + entry * segment.Size, segment.Size ) );
					position = Advance( body, position, (int)count * segment.Size );
					break;
				}
			}
		}
		return position;
	}

	private static int Bound( IReadOnlyList<ThemeParkIncBodySegment> segments, bool full )
	{
		var total = 0;
		foreach ( var segment in segments )
		{
			total += segment.Kind switch
			{
				ThemeParkIncBodySegmentKind.Field => segment.Size,
				ThemeParkIncBodySegmentKind.Repeat => segment.Count * Bound( segment.Children, full ),
				ThemeParkIncBodySegmentKind.Group => Bound( segment.Children, full ),
				_ => RingHeaderBytes + segment.Size + (full ? segment.Capacity * segment.Size : 0),
			};
		}
		return total;
	}

	private static IReadOnlyDictionary<ThemeParkIncThingModel, ThemeParkIncBodyLayout> Build()
	{
		var fragments = new Dictionary<string, string>
		{
			["person"] = PersonFragment,
			["staff"] = StaffFragment,
		};
		var layouts = new Dictionary<ThemeParkIncThingModel, ThemeParkIncBodyLayout>();
		void Add( ThemeParkIncThingModel model, string name, string text ) => layouts.Add( model, new ThemeParkIncBodyLayout( name, Parse( text, fragments ) ) );
		Add( ThemeParkIncThingModel.Guest, "guest", GuestBody );
		Add( ThemeParkIncThingModel.Mechanic, "mechanic", MechanicBody );
		Add( ThemeParkIncThingModel.Handyman, "handyman", HandymanBody );
		Add( ThemeParkIncThingModel.Entertainer, "entertainer", EntertainerBody );
		Add( ThemeParkIncThingModel.Guard, "guard", GuardBody );
		Add( ThemeParkIncThingModel.Researcher, "researcher", ResearcherBody );
		Add( ThemeParkIncThingModel.StaffHq, "staff HQ", StaffHqBody );
		Add( ThemeParkIncThingModel.AdvisorController, "advisor controller", AdvisorBody );
		Add( ThemeParkIncThingModel.ParkAnalyser, "park analyser", AnalyserBody );
		Add( ThemeParkIncThingModel.ResearchLab, "research lab", ResearchLabBody );
		Add( ThemeParkIncThingModel.Weather, "weather", WeatherBody );
		Add( ThemeParkIncThingModel.ChallengeSystem, "challenge system", ChallengeBody );
		Add( ThemeParkIncThingModel.Gardener, "gardener", GardenerBody );
		return layouts;
	}

	/// <summary>
	/// Parses the layout text: one segment per line, <c>size name</c> for a field, <c>rep count name</c> (closed by <c>end</c>) for an array, <c>group name</c> (closed by <c>end</c>) for an
	/// embedded record, <c>ring elementSize capacity name</c> for a history ring and <c>use fragment</c> to include a shared part. The name <c>_</c> is the unnamed element of an array of one field.
	/// </summary>
	internal static IReadOnlyList<ThemeParkIncBodySegment> Parse( string text, IReadOnlyDictionary<string, string> fragments )
	{
		var root = new List<ThemeParkIncBodySegment>();
		var open = new Stack<(List<ThemeParkIncBodySegment> Items, ThemeParkIncBodySegmentKind Kind, string Name, int Count)>();
		var current = root;
		foreach ( var raw in text.Split( '\n', StringSplitOptions.RemoveEmptyEntries ) )
		{
			var line = raw.Trim();
			if ( line.Length == 0 )
				continue;
			var token = line.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
			switch ( token[0] )
			{
				case "use":
					current.AddRange( Parse( fragments[token[1]], fragments ) );
					break;
				case "end":
				{
					var (parent, kind, name, count) = open.Pop();
					parent.Add( new ThemeParkIncBodySegment( kind, name, 0, count, 0, current.AsReadOnly() ) );
					current = parent;
					break;
				}
				case "rep":
				case "group":
					open.Push( (current, token[0] == "rep" ? ThemeParkIncBodySegmentKind.Repeat : ThemeParkIncBodySegmentKind.Group, token[^1], token[0] == "rep" ? int.Parse( token[1] ) : 1) );
					current = new List<ThemeParkIncBodySegment>();
					break;
				case "ring":
					current.Add( new ThemeParkIncBodySegment( ThemeParkIncBodySegmentKind.Ring, token[3], int.Parse( token[1] ), 0, int.Parse( token[2] ), Array.Empty<ThemeParkIncBodySegment>() ) );
					break;
				default:
					current.Add( new ThemeParkIncBodySegment( ThemeParkIncBodySegmentKind.Field, token[1], int.Parse( token[0] ), 0, 0, Array.Empty<ThemeParkIncBodySegment>() ) );
					break;
			}
		}
		if ( open.Count != 0 )
			throw new InvalidOperationException( "A Theme Park Inc body layout has an unclosed group." );
		return root.AsReadOnly();
	}
}

/// <summary>
/// One thing body read through its <see cref="ThemeParkIncBodyLayout"/>: the bytes plus every field by dotted path. Fields are looked up lazily; the typed records of the models
/// read from here, and any member the records do not name is still reachable by its path and offset.
/// </summary>
public sealed class ThemeParkIncBody
{
	private readonly ReadOnlyMemory<byte> bytes;
	private IReadOnlyList<ThemeParkIncBodyField>? fields;
	private Dictionary<string, ThemeParkIncBodyField>? index;

	public ThemeParkIncBody( ThemeParkIncThingModel model, ReadOnlyMemory<byte> bytes )
	{
		Layout = ThemeParkIncBodyLayout.For( model ) ?? throw new ArgumentException( $"Theme Park Inc model {model} has no traced body layout.", nameof( model ) );
		Model = model;
		this.bytes = bytes;
		if ( Layout.Measure( bytes.Span, 0 ) != bytes.Length )
			throw new InvalidDataException( $"Save Theme Park Inc {Layout.Name} body is {bytes.Length} bytes, but its layout reads {Layout.Measure( bytes.Span, 0 )}." );
	}

	public ThemeParkIncThingModel Model { get; }
	public ThemeParkIncBodyLayout Layout { get; }
	public ReadOnlyMemory<byte> Bytes => bytes;
	public int Length => bytes.Length;

	public IReadOnlyList<ThemeParkIncBodyField> Fields => fields ??= Layout.Fields( bytes.Span );

	public bool Has( string path ) => Index.ContainsKey( path );

	public ThemeParkIncBodyField Field( string path ) =>
		Index.TryGetValue( path, out var field ) ? field : throw new KeyNotFoundException( $"The Theme Park Inc {Layout.Name} body has no field {path}." );

	public ReadOnlySpan<byte> Raw( string path )
	{
		var field = Field( path );
		return bytes.Span.Slice( field.Offset, field.Size );
	}

	public byte U8( string path ) => Sized( path, 1 )[0];
	public bool Flag( string path ) => U8( path ) != 0;
	public ushort U16( string path ) => BinaryPrimitives.ReadUInt16LittleEndian( Sized( path, 2 ) );
	public uint U32( string path ) => BinaryPrimitives.ReadUInt32LittleEndian( Sized( path, 4 ) );
	public long I64( string path ) => BinaryPrimitives.ReadInt64LittleEndian( Sized( path, 8 ) );
	public float F32( string path ) => BinaryPrimitives.ReadSingleLittleEndian( Sized( path, 4 ) );
	public byte[] Bytes12( string path ) => Sized( path, 12 ).ToArray();

	/// <summary>The 33 UTF-16 units of an inline name array <c>name[i]</c> up to the first NUL.</summary>
	public string Utf16( string path, int units )
	{
		var characters = new char[units];
		for ( var unit = 0; unit < units; unit++ )
			characters[unit] = (char)U16( $"{path}[{unit}]" );
		var end = Array.IndexOf( characters, '\0' );
		return new string( characters, 0, end < 0 ? units : end );
	}

	/// <summary>A history ring named <paramref name="path"/> (its fields are <c>path.mCurrentEntry</c> and so on; an empty path is a ring that is an array element).</summary>
	public ThemeParkIncHistoryRing Ring( string path )
	{
		var head = path.Length == 0 ? string.Empty : path + ".";
		var count = (int)U32( head + "mNumEntries" );
		var entries = new uint[count];
		for ( var entry = 0; entry < count; entry++ )
			entries[entry] = Word( head + $"mData[{entry}]" );
		return new ThemeParkIncHistoryRing( U32( head + "mCurrentEntry" ), Flag( head + "mWrappedAround" ), Word( head + "mTemp" ), entries );
	}

	/// <summary>A 1, 2 or 4 byte field as an unsigned word.</summary>
	public uint Word( string path ) => Field( path ).Size switch
	{
		1 => U8( path ),
		2 => U16( path ),
		4 => U32( path ),
		_ => throw new InvalidDataException( $"The Theme Park Inc {Layout.Name} field {path} is not a 1, 2 or 4 byte word." ),
	};

	private Dictionary<string, ThemeParkIncBodyField> Index
	{
		get
		{
			if ( index != null )
				return index;
			var built = new Dictionary<string, ThemeParkIncBodyField>( Fields.Count );
			foreach ( var item in Fields )
				built[item.Path] = item;
			return index = built;
		}
	}

	private ReadOnlySpan<byte> Sized( string path, int size )
	{
		var field = Field( path );
		if ( field.Size != size )
			throw new InvalidDataException( $"The Theme Park Inc {Layout.Name} field {path} is {field.Size} bytes, not {size}." );
		return bytes.Span.Slice( field.Offset, size );
	}

	public override string ToString() => new StringBuilder().Append( Layout.Name ).Append( " body of " ).Append( Length ).Append( " bytes" ).ToString();
}

/// <summary>A history ring of a Theme Park Inc body: the entry last written, whether it wrapped, the running value (<c>mTemp</c>) and the saved entries (at most the ring's capacity).</summary>
public sealed record ThemeParkIncHistoryRing( uint CurrentEntry, bool WrappedAround, uint Current, IReadOnlyList<uint> Entries );
