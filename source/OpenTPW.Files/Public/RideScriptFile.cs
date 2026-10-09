using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>
/// High 16 bits of an RSE code word. Values are the only five observed in the corpus.
/// </summary>
public enum RideScriptOperandKind : ushort
{
	Literal = 0x0000,
	String = 0x1000,
	Branch = 0x2000,
	Variable = 0x4000
}

/// <summary>
/// One RSE operand. <see cref="Value"/> is the raw low 16 bits; literal signedness is not verified.
/// String values are byte offsets into the string blob, branch values are code-word indices and
/// variable values index <see cref="RideScriptFile.VariableNames"/>.
/// </summary>
public readonly record struct RideScriptOperand( RideScriptOperandKind Kind, ushort Value );

/// <summary>
/// An opcode word plus the operand words that follow it until the next opcode word.
/// </summary>
public sealed record RideScriptInstruction( int WordOffset, ushort Opcode, IReadOnlyList<RideScriptOperand> Operands );

/// <summary>
/// Bounded strict reader for compiled RSSEQ ride/object scripts (.RSE).
/// Parses container structure and cross-references only; it assigns no opcode semantics.
/// </summary>
public sealed class RideScriptFile : BaseFormat
{
	public const int MaximumFileBytes = 1024 * 1024;
	public const int MaximumCodeWords = 65536;
	public const int MaximumVariables = 65536;
	public const int HeaderBytes = 48;
	private const ushort OpcodeFlag = 0x8000;
	private static ReadOnlySpan<byte> Magic => new byte[] { (byte)'R', (byte)'S', (byte)'S', (byte)'E', (byte)'Q', 0x0F, 0x01, 0x00 };
	private static ReadOnlySpan<byte> Padding => "Pad Pad Pad Pad "u8;

	public int VariableCount { get; private set; }
	public int StackSize { get; private set; }
	public int TimeSlice { get; private set; }
	public int LimboSize { get; private set; }
	public int BounceSize { get; private set; }
	public int WalkSize { get; private set; }
	public int CodeWordCount { get; private set; }
	public int StringBlobLength { get; private set; }
	public IReadOnlyList<RideScriptInstruction> Instructions { get; private set; } = Array.Empty<RideScriptInstruction>();

	/// <summary>Key: byte offset within the string blob. Value: string without its terminator.</summary>
	public IReadOnlyDictionary<int, string> Strings { get; private set; } = new Dictionary<int, string>();
	public IReadOnlyList<string> VariableNames { get; private set; } = Array.Empty<string>();

	private Dictionary<int, int> instructionIndexByWord = new();

	public RideScriptFile( string path ) => ReadFromFile( path );
	public RideScriptFile( Stream stream ) => ReadFromStream( stream );

	/// <summary>Returns the instruction index whose opcode word is at <paramref name="wordOffset"/>, or -1.</summary>
	public int GetInstructionIndexAtWord( int wordOffset ) => instructionIndexByWord.TryGetValue( wordOffset, out var index ) ? index : -1;

	protected override void ReadFromStream( Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		var data = ReadBounded( stream );
		if ( data.Length < HeaderBytes + 4 || !data.AsSpan( 0, 8 ).SequenceEqual( Magic ) )
			throw new InvalidDataException( "RSE header is missing or truncated." );
		if ( !data.AsSpan( 32, 16 ).SequenceEqual( Padding ) )
			throw new InvalidDataException( "RSE header padding is invalid." );

		var variableCount = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 8 ) );
		if ( variableCount < 0 || variableCount > MaximumVariables )
			throw new InvalidDataException( "RSE variable count is negative or exceeds its limit." );
		VariableCount = variableCount;
		StackSize = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 12 ) );
		TimeSlice = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 16 ) );
		LimboSize = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 20 ) );
		BounceSize = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 24 ) );
		WalkSize = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 28 ) );

		var wordCount = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( HeaderBytes ) );
		var codeStart = HeaderBytes + 4;
		if ( wordCount > MaximumCodeWords || wordCount * 4L > data.Length - codeStart - 4L )
			throw new InvalidDataException( "RSE code word count is truncated or exceeds its limit." );
		CodeWordCount = (int)wordCount;

		var position = codeStart + CodeWordCount * 4;
		var blobLength = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( position ) );
		position += 4;
		if ( blobLength > data.Length - position )
			throw new InvalidDataException( "RSE string blob is truncated." );
		StringBlobLength = (int)blobLength;
		var strings = ReadStringBlob( data.AsSpan( position, StringBlobLength ) );
		position += StringBlobLength;

		var names = new List<string>( Math.Min( VariableCount, (data.Length - position) / 5 + 1 ) );
		while ( position < data.Length )
		{
			if ( names.Count == VariableCount )
				throw new InvalidDataException( "RSE has more variable names than its header declares." );
			if ( data.Length - position < 4 )
				throw new InvalidDataException( "RSE variable name length is truncated." );
			var length = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( position ) );
			position += 4;
			if ( length == 0 || length > data.Length - position )
				throw new InvalidDataException( $"RSE variable name {names.Count} has an invalid length." );
			var name = data.AsSpan( position, (int)length );
			if ( name[^1] != 0 || name[..^1].IndexOf( (byte)0 ) >= 0 )
				throw new InvalidDataException( $"RSE variable name {names.Count} is not a single terminated string." );
			names.Add( Encoding.Latin1.GetString( name[..^1] ) );
			position += (int)length;
		}
		if ( names.Count != VariableCount )
			throw new InvalidDataException( "RSE has fewer variable names than its header declares." );

		var instructions = new List<RideScriptInstruction>();
		var indexByWord = new Dictionary<int, int>();
		var opcodeWord = -1;
		ushort opcode = 0;
		var operands = new List<RideScriptOperand>();
		for ( var word = 0; word < CodeWordCount; word++ )
		{
			var raw = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( codeStart + word * 4 ) );
			var flags = (ushort)(raw >> 16);
			var value = (ushort)raw;
			if ( flags == OpcodeFlag )
			{
				if ( opcodeWord >= 0 )
					instructions.Add( new RideScriptInstruction( opcodeWord, opcode, operands.ToArray() ) );
				indexByWord[word] = instructions.Count;
				opcodeWord = word;
				opcode = value;
				operands.Clear();
				continue;
			}
			if ( flags is not ((ushort)RideScriptOperandKind.Literal or (ushort)RideScriptOperandKind.String or (ushort)RideScriptOperandKind.Branch or (ushort)RideScriptOperandKind.Variable) )
				throw new InvalidDataException( $"RSE code word {word} has unknown flags 0x{flags:X4}." );
			if ( opcodeWord < 0 )
				throw new InvalidDataException( "RSE code begins with an operand instead of an opcode." );
			var kind = (RideScriptOperandKind)flags;
			if ( kind == RideScriptOperandKind.String && !strings.ContainsKey( value ) )
				throw new InvalidDataException( $"RSE code word {word} references string offset {value} that does not start a string." );
			if ( kind == RideScriptOperandKind.Variable && value >= VariableCount )
				throw new InvalidDataException( $"RSE code word {word} references undeclared variable {value}." );
			operands.Add( new RideScriptOperand( kind, value ) );
		}
		if ( opcodeWord >= 0 )
			instructions.Add( new RideScriptInstruction( opcodeWord, opcode, operands.ToArray() ) );

		foreach ( var instruction in instructions )
			foreach ( var operand in instruction.Operands )
				if ( operand.Kind == RideScriptOperandKind.Branch && !indexByWord.ContainsKey( operand.Value ) )
					throw new InvalidDataException( $"RSE instruction at word {instruction.WordOffset} branches to word {operand.Value}, which is not an opcode." );

		Instructions = instructions.AsReadOnly();
		instructionIndexByWord = indexByWord;
		Strings = strings;
		VariableNames = names.AsReadOnly();
	}

	private static Dictionary<int, string> ReadStringBlob( ReadOnlySpan<byte> blob )
	{
		var strings = new Dictionary<int, string>();
		if ( blob.IsEmpty )
			return strings;
		if ( blob[^1] != 0 )
			throw new InvalidDataException( "RSE string blob is not terminated." );
		var start = 0;
		while ( start < blob.Length )
		{
			var length = blob[start..].IndexOf( (byte)0 );
			strings[start] = Encoding.Latin1.GetString( blob.Slice( start, length ) );
			start += length + 1;
		}
		return strings;
	}

	private static byte[] ReadBounded( Stream stream )
	{
		using var input = new MemoryStream();
		var readBuffer = new byte[8192];
		while ( true )
		{
			var count = stream.Read( readBuffer, 0, (int)Math.Min( readBuffer.Length, MaximumFileBytes - input.Length + 1 ) );
			if ( count == 0 )
				break;
			if ( count > MaximumFileBytes - input.Length )
				throw new InvalidDataException( "RSE exceeds the input byte limit." );
			input.Write( readBuffer, 0, count );
		}
		return input.ToArray();
	}
}
