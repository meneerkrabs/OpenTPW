namespace OpenTPW;

/// <summary>The record kinds of a Theme Park Inc SAM schema table (each record 0x3C bytes: <c>+0</c> the kind, <c>+4</c> the name, <c>+0x24</c>/<c>+0x28</c> a range, <c>+0x34</c> an array's count).</summary>
public enum ThemeParkIncSamKind
{
	StructBegin = 0,
	StructEnd = 1,
	ArrayBegin = 2,
	ArrayEnd = 3,
	/// <summary>A signed integer.</summary>
	Int = 4,
	/// <summary>An integer that must not be negative.</summary>
	NonNegativeInt = 5,
	/// <summary>An integer in [<see cref="ThemeParkIncSamRecord.Minimum"/>, <see cref="ThemeParkIncSamRecord.Maximum"/>), whose default is the minimum.</summary>
	IntRange = 6,
	Float = 7,
	NonNegativeFloat = 8,
	FloatRange = 9,
	String = 10,
	MapVariable = 11,
	End = 12
}

/// <summary>One record of a SAM schema table: its kind, its name (a struct's or an array's name sits on its end record) and, for an integer range, its bounds; an array end carries the element count.</summary>
public readonly record struct ThemeParkIncSamRecord( ThemeParkIncSamKind Kind, string Name, int Minimum = 0, int Maximum = 0, int Count = 0 );

/// <summary>
/// Theme Park Inc's generic SAM-bound object (docs/reverse/TPI-CAREER.md §1): the dword slots a schema table binds, the reset to defaults and the text parser that fills them. Only the integer kinds
/// (4, 5 and 6) are ported; the schemas OpenTPW binds this way (the mission file) have no other leaf. A word index is the original object's byte offset minus 8, divided by 4.
/// </summary>
// [BIN:TPI-EXE:0x418F60 SAM binding] the object's data base is this + 8 (vtable slot 1); each leaf record gets the next dword slot, counted from slot 1 (0x418FA9), with the stride of its enclosing
// array's leaves (0 outside an array) and the address of that array's count dword; an array begin counts its leaves up to the next struct/array record (0x41901B..0x419049) and puts the count
// dword at slot + count × leaves (0x419066..0x419075); its end moves the slot to slot + (count - 1) × leaves + 1 past the leaves (0x419095..0x4190A2); the constructor then resets (0x419108 -> 0x419830)
public sealed class ThemeParkIncSamObject
{
	private readonly ThemeParkIncSamRecord[] schema;
	private readonly Binding[] bindings;
	private readonly int[] words;

	private readonly record struct Binding( int Slot, int Stride, int CountSlot );

	public ThemeParkIncSamObject( IReadOnlyList<ThemeParkIncSamRecord> schema )
	{
		ArgumentNullException.ThrowIfNull( schema );
		this.schema = schema.ToArray();
		var end = Array.FindIndex( this.schema, record => record.Kind == ThemeParkIncSamKind.End );
		if ( end < 0 )
			throw new ArgumentException( "A SAM schema ends with an end record.", nameof( schema ) );
		if ( this.schema.Take( end ).Any( record => record.Kind is not (ThemeParkIncSamKind.StructBegin or ThemeParkIncSamKind.StructEnd or ThemeParkIncSamKind.ArrayBegin
			or ThemeParkIncSamKind.ArrayEnd or ThemeParkIncSamKind.Int or ThemeParkIncSamKind.NonNegativeInt or ThemeParkIncSamKind.IntRange) ) )
			throw new NotSupportedException( "Only SAM schemas with integer leaves are ported." );
		bindings = new Binding[end];
		var slot = 1;
		var stride = 0;
		var countSlot = -1;
		for ( var index = 0; index < end; index++ )
		{
			switch ( this.schema[index].Kind )
			{
				case ThemeParkIncSamKind.ArrayBegin:
					var close = index + 1;
					while ( close < end && this.schema[close].Kind is not (ThemeParkIncSamKind.ArrayEnd or ThemeParkIncSamKind.StructBegin or ThemeParkIncSamKind.StructEnd or ThemeParkIncSamKind.ArrayBegin) )
						close++;
					stride = close - index - 1;
					countSlot = slot + this.schema[close].Count * stride;
					bindings[index] = new Binding( 0, 0, -1 );
					break;
				case ThemeParkIncSamKind.ArrayEnd:
					slot = slot + (this.schema[index].Count - 1) * stride + 1;
					stride = 0;
					countSlot = -1;
					bindings[index] = new Binding( 0, 0, -1 );
					break;
				case ThemeParkIncSamKind.StructBegin or ThemeParkIncSamKind.StructEnd:
					bindings[index] = new Binding( 0, 0, -1 );
					break;
				default:
					bindings[index] = new Binding( slot, stride, countSlot );
					slot++;
					break;
			}
		}
		words = new int[slot];
		Reset();
	}

	/// <summary>The object's data words by slot; slot 0 (object <c>+8</c>) is never bound.</summary>
	public IReadOnlyList<int> Words => words;

	/// <summary>The word at the original object's byte offset <paramref name="offset"/> (<c>+0xC</c> is slot 1).</summary>
	public int this[int offset] => words[SlotOf( offset )];

	internal void Set( int offset, int value ) => words[SlotOf( offset )] = value;

	private int SlotOf( int offset )
	{
		if ( offset < 8 || offset % 4 != 0 || (offset - 8) / 4 >= words.Length )
			throw new ArgumentOutOfRangeException( nameof( offset ), $"Offset {offset:X} is outside the object's words." );
		return (offset - 8) / 4;
	}

	/// <summary>
	/// The reset to defaults (<c>0x419830</c>): every element of every leaf becomes 0, a range leaf its minimum, and every array's count dword 0.
	/// </summary>
	// [BIN:TPI-EXE:0x419830 SAM reset] per struct or array (an array's element count from its end record's +0x34, else 1) every following leaf until a struct or array end: kinds 4, 5, 7 and 8
	// write 0 to each element, kind 6 the record's +0x24, kind 9 its +0x2C, and then the count dword (binding +8, when there is one) becomes 0
	public void Reset()
	{
		for ( var index = 0; index < bindings.Length; index++ )
		{
			var record = schema[index];
			if ( record.Kind is not (ThemeParkIncSamKind.Int or ThemeParkIncSamKind.NonNegativeInt or ThemeParkIncSamKind.IntRange) )
				continue;
			var binding = bindings[index];
			var elements = binding.CountSlot < 0 ? 1 : CountOf( index );
			for ( var element = 0; element < elements; element++ )
				words[binding.Slot + binding.Stride * element] = record.Kind == ThemeParkIncSamKind.IntRange ? record.Minimum : 0;
			if ( binding.CountSlot >= 0 )
				words[binding.CountSlot] = 0;
		}
	}

	private int CountOf( int leaf )
	{
		var close = leaf;
		while ( schema[close].Kind != ThemeParkIncSamKind.ArrayEnd )
			close++;
		return schema[close].Count;
	}

	/// <summary>
	/// Reads a SAM file into the object as <c>0x419B20</c> does: a missing file changes nothing and fails, an empty file changes nothing and succeeds, any other is parsed (with the reset first
	/// unless <paramref name="overlay"/>). False when the file is missing or a line is malformed; the lines before a malformed one keep what they set.
	/// </summary>
	// [BIN:TPI-EXE:0x419B20 SAM file load] the size 0x7CC5F0 is -1 for a missing file (fails, after logging only when the overlay flag is clear), 0 for an empty file (returns 1 before the parser,
	// so nothing is reset); otherwise the file is read whole, NUL-terminated and parsed by 0x419CD0 with the flag
	public bool Load( byte[]? file, bool overlay )
	{
		if ( file == null )
			return false;
		if ( file.Length == 0 )
			return true;
		return Parse( file, overlay );
	}

	/// <summary>
	/// The SAM text parser (<c>0x419CD0</c>) over a file's bytes. True when every line parsed; false at the first malformed line, which stops the parse with what came before kept.
	/// </summary>
	// [BIN:TPI-EXE:0x419CD0 SAM parser] flag 0 resets first (0x419CE2 -> 0x419830); every '\r' is removed; a line is skipped from its first byte to its '\n' after its parse, so leading blank
	// lines make the next key parse once per blank line (idempotent); after the leading white space (isspace, which crosses newlines) a NUL ends the file, '#' or '\n' skips the line; the group is
	// up to 64 isalnum bytes followed by '.' or '[digits].'; then up to 16 fields of isalnum bytes, each starting isalpha, separated by '.' and ended by white space; each field is resolved
	// (0x4192C0) and takes the next value: white space, an optional '-', digits, then white space or NUL (else "Value read error"), converted by atol (0x86F490); an array element raises the
	// count dword to index + 1 before the kind check; kind 5 refuses a negative value and kind 6 a value outside [+0x24, +0x28); every failure returns 0 at once
	public bool Parse( ReadOnlySpan<byte> file, bool overlay )
	{
		if ( !overlay )
			Reset();
		var text = new List<byte>( file.Length + 1 );
		foreach ( var value in file )
		{
			if ( value == 0 )
				break;
			if ( value != (byte)'\r' )
				text.Add( value );
		}
		text.Add( 0 );
		var buffer = text.ToArray();
		var position = 0;
		while ( buffer[position] != 0 )
		{
			var lineStart = position;
			while ( IsSpace( buffer[position] ) )
				position++;
			var first = buffer[position];
			if ( first != 0 && first != (byte)'#' && first != (byte)'\n' && !ParseLine( buffer, ref position ) )
				return false;
			position = lineStart;
			while ( buffer[position] != 0 && buffer[position] != (byte)'\n' )
				position++;
			if ( buffer[position] == (byte)'\n' )
				position++;
		}
		return true;
	}

	private bool ParseLine( byte[] buffer, ref int position )
	{
		if ( !IsAlnum( buffer[position] ) )
			return false;
		var group = new List<byte>();
		do
		{
			if ( group.Count > 63 )
				break;
			group.Add( buffer[position++] );
		}
		while ( IsAlnum( buffer[position] ) );
		var separator = buffer[position];
		if ( separator != (byte)'.' && separator != (byte)'[' )
			return false;
		var isArray = false;
		var arrayIndex = 0;
		if ( separator == (byte)'[' )
		{
			position++;
			if ( !IsDigit( buffer[position] ) )
				return false;
			arrayIndex = Atol( buffer, position );
			while ( IsDigit( buffer[position] ) )
				position++;
			if ( buffer[position] != (byte)']' || buffer[position + 1] != (byte)'.' )
				return false;
			position += 2;
			isArray = true;
		}
		else
			position++;
		var fields = new List<byte[]>();
		if ( !IsAlpha( buffer[position] ) )
			return false;
		while ( true )
		{
			var field = new List<byte>();
			do
			{
				if ( !IsAlnum( buffer[position] ) )
					break;
				field.Add( buffer[position++] );
			}
			while ( field.Count < 64 );
			fields.Add( field.ToArray() );
			if ( buffer[position] != (byte)'.' && !IsSpace( buffer[position] ) )
				return false;
			var after = buffer[position++];
			if ( after != (byte)'.' || fields.Count > 15 )
			{
				if ( fields.Count == 16 && buffer[position] == (byte)'.' )
					return false;
				break;
			}
			if ( !IsAlpha( buffer[position] ) )
				return false;
		}
		var groupName = group.ToArray();
		foreach ( var field in fields )
		{
			if ( Resolve( groupName, field, isArray, arrayIndex ) is not { } leaf )
				return false;
			while ( IsSpace( buffer[position] ) )
				position++;
			var start = position;
			if ( buffer[position] == (byte)'-' )
				position++;
			while ( IsDigit( buffer[position] ) )
				position++;
			if ( !IsSpace( buffer[position] ) && buffer[position] != 0 )
				return false;
			var value = Atol( buffer, start );
			var binding = bindings[leaf];
			var slot = binding.Slot;
			if ( isArray )
			{
				if ( words[binding.CountSlot] < unchecked( arrayIndex + 1 ) )
					words[binding.CountSlot] = unchecked( arrayIndex + 1 );
				var target = (long)binding.Slot + (long)binding.Stride * arrayIndex;
				// [BIN:TPI-EXE:0x4192C0 SAM resolver] the resolver refuses an index at or past the count but not a negative one, which atol's wrap of a long digit run can give; the original
				// would write before the array, outside the object: such a file is refused here
				if ( target < 0 || target >= words.Length )
					throw new InvalidDataException( $"SAM array index {arrayIndex} writes outside the object." );
				slot = (int)target;
			}
			var kind = schema[leaf].Kind;
			if ( kind == ThemeParkIncSamKind.NonNegativeInt && value < 0 || kind == ThemeParkIncSamKind.IntRange && (value < schema[leaf].Minimum || value >= schema[leaf].Maximum) )
				return false;
			words[slot] = value;
		}
		return true;
	}

	/// <summary>
	/// The resolver (<c>0x4192C0</c>): the last struct or array end named <paramref name="group"/>; a struct for a plain key, an array for an indexed key whose index is below the count; then
	/// the nearest leaf before it named <paramref name="field"/>, not past the struct's or array's begin. Names compare byte for byte (strcmp).
	/// </summary>
	// [BIN:TPI-EXE:0x4192C0 SAM resolver] scans down from the end record for kind 1 or 3 with an equal name; the first match decides: a plain key needs kind 1, an indexed key kind 3 and
	// index < +0x34 (signed), else 0; then scans down for an equal leaf name and fails at a kind 0 or 2
	private int? Resolve( byte[] group, byte[] field, bool isArray, int arrayIndex )
	{
		for ( var index = bindings.Length - 1; index >= 0; index-- )
		{
			var record = schema[index];
			if ( record.Kind is not (ThemeParkIncSamKind.StructEnd or ThemeParkIncSamKind.ArrayEnd) || !NameIs( record.Name, group ) )
				continue;
			if ( !isArray && record.Kind == ThemeParkIncSamKind.ArrayEnd || isArray && (record.Kind == ThemeParkIncSamKind.StructEnd || record.Count <= arrayIndex) )
				return null;
			for ( var leaf = index - 1; leaf >= 0; leaf-- )
			{
				if ( schema[leaf].Kind is ThemeParkIncSamKind.StructBegin or ThemeParkIncSamKind.ArrayBegin )
					return null;
				if ( NameIs( schema[leaf].Name, field ) )
					return leaf;
			}
			return null;
		}
		return null;
	}

	private static bool NameIs( string name, byte[] bytes )
	{
		if ( name.Length != bytes.Length )
			return false;
		for ( var index = 0; index < bytes.Length; index++ )
		{
			if ( name[index] != bytes[index] )
				return false;
		}
		return true;
	}

	// [BIN:TPI-EXE:0x86F490 atol] white space, an optional sign, then decimal digits accumulated in a 32-bit long that wraps
	private static int Atol( byte[] buffer, int position )
	{
		while ( IsSpace( buffer[position] ) )
			position++;
		var negative = buffer[position] == (byte)'-';
		if ( buffer[position] is (byte)'-' or (byte)'+' )
			position++;
		var total = 0;
		while ( IsDigit( buffer[position] ) )
			total = unchecked( total * 10 + (buffer[position++] - '0') );
		return negative ? unchecked( -total ) : total;
	}

	// [BIN:TPI-EXE:0x86F6B3 ctype] the C locale's classes through _pctype: isspace (mask 8), isdigit (4), isalpha (0x103), isalnum (0x107); the parser passes a signed char, so a byte above
	// 0x7F would index the table below its start: no shipped SAM file has one, and a file that puts one where the parser classifies a character is refused
	private static byte Ascii( byte value ) => value <= 0x7F ? value : throw new InvalidDataException( $"SAM text has the byte 0x{value:X2} where the parser classifies a character." );
	private static bool IsSpace( byte value ) => Ascii( value ) is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r';
	private static bool IsDigit( byte value ) => Ascii( value ) is >= (byte)'0' and <= (byte)'9';
	private static bool IsAlpha( byte value ) => Ascii( value ) is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z';
	private static bool IsAlnum( byte value ) => IsAlpha( value ) || IsDigit( value );
}
