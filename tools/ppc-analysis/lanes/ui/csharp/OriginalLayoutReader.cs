using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenTPW.Reverse.Ui;

public enum OriginalLayoutDiagnostic
{
	InputLimit, TruncatedWord, WordLimit, ControlLimit, DepthLimit, ArrayLimit,
	UnknownCommand, UnsupportedControlType, UnknownGeometrySubtype,
	IncompatibleParent, UnknownExternalParentType, DuplicateImpliedChild,
	InvalidLightIndex, InvalidOrigin, TrailingData
}

public sealed class OriginalLayoutException : FormatException
{
	public OriginalLayoutDiagnostic Diagnostic { get; }
	public int DataOffset { get; }
	public ushort? Command { get; }

	internal OriginalLayoutException( OriginalLayoutDiagnostic diagnostic, int offset, ushort? command, string detail )
		: base( $"{diagnostic} at data:0x{offset:x}" + (command.HasValue ? $", command {command}" : "") + $": {detail}" )
	{
		Diagnostic = diagnostic;
		DataOffset = offset;
		Command = command;
	}
}

public sealed record OriginalLayoutLimits( int MaximumInputBytes = 32 * 1024 * 1024,
	int MaximumWords = 4096, int MaximumControls = 512, int MaximumDepth = 64, int MaximumArrayEntries = 512 );

public sealed record OriginalLayoutOrigin( string? ExecutableSha256, int DataOffset = 0, int SectionIndex = 1 );

public readonly record struct OriginalRect( short Left, short Top, short Right, short Bottom );
public readonly record struct OriginalPoint( short X, short Y );
public readonly record struct OriginalPair( short First, short Second );

/// <summary>Only polygon semantics are established; scalar shape parameters retain their original order.</summary>
public sealed record OriginalGeometry( ushort Subtype, IReadOnlyList<short> Scalars, IReadOnlyList<OriginalPoint> Points );

/// <summary>Ordered metadata AST. No command is executed against a GUI or renderer.</summary>
public sealed record OriginalCommand( ushort Code, int DataOffset,
	OriginalControl? Control = null, int? Scalar = null, OriginalRect? Rectangle = null,
	OriginalGeometry? Geometry = null, IReadOnlyList<OriginalPair>? Pairs = null );

public sealed record OriginalScope( IReadOnlyList<OriginalCommand> Commands );

public sealed class OriginalControl
{
	public int Index { get; }
	public int DataOffset { get; }
	public ushort Type { get; }
	public uint Attributes { get; }
	public int Id { get; }
	public int? ParentIndex { get; }
	public ushort OriginCommand { get; }
	public OriginalRect Bounds { get; }
	public OriginalScope Body { get; internal set; } = new( Array.Empty<OriginalCommand>() );

	internal OriginalControl( int index, int offset, ushort type, uint attributes, int id,
		int? parent, ushort originCommand, OriginalRect bounds )
	{
		Index = index;
		DataOffset = offset;
		Type = type;
		Attributes = attributes;
		Id = id;
		ParentIndex = parent;
		OriginCommand = originCommand;
		Bounds = bounds;
	}
}

public sealed record OriginalLayoutDocument( OriginalLayoutOrigin Origin, int BytesConsumed,
	string TableSha256, OriginalScope OuterScope, IReadOnlyList<OriginalControl> Controls );

/// <summary>
/// Reads the identified Mac embedded halfword grammar. Explicit control types 1–13
/// and fresh-allocation implied children are supported. Type 0 returns null in the
/// original factory and is rejected here; this reader does not invent an allocation.
/// </summary>
public static class OriginalLayoutReader
{
	public static OriginalLayoutDocument Read( ReadOnlyMemory<byte> data, OriginalLayoutOrigin? origin = null,
		OriginalLayoutLimits? limits = null, bool requireWholeInput = true )
	{
		origin ??= new OriginalLayoutOrigin( null );
		limits ??= new OriginalLayoutLimits();
		ValidateLimits( limits );
		if ( origin.DataOffset < 0 || origin.SectionIndex < 0 || origin.DataOffset > int.MaxValue - data.Length )
			throw new OriginalLayoutException( OriginalLayoutDiagnostic.InvalidOrigin, origin.DataOffset, null, "offset cannot address the complete input" );
		if ( data.Length > limits.MaximumInputBytes )
			throw new OriginalLayoutException( OriginalLayoutDiagnostic.InputLimit, origin.DataOffset, null, "input exceeds the byte limit" );
		return new Parser( data, origin, limits ).Read( requireWholeInput );
	}

	private static void ValidateLimits( OriginalLayoutLimits limits )
	{
		if ( limits.MaximumInputBytes is < 1 or > 32 * 1024 * 1024
			|| limits.MaximumWords is < 1 or > 4096 || limits.MaximumControls is < 1 or > 512
			|| limits.MaximumDepth is < 0 or > 64 || limits.MaximumArrayEntries is < 0 or > 512 )
			throw new ArgumentOutOfRangeException( nameof( limits ), "Limits may tighten, but cannot exceed, the reader's hard caps." );
	}

	private sealed class Parser
	{
		private readonly ReadOnlyMemory<byte> data;
		private readonly OriginalLayoutOrigin origin;
		private readonly OriginalLayoutLimits limits;
		private readonly List<OriginalControl> controls = new();
		private int position;
		private int words;
		private ushort? command;

		public Parser( ReadOnlyMemory<byte> data, OriginalLayoutOrigin origin, OriginalLayoutLimits limits )
		{
			this.data = data;
			this.origin = origin;
			this.limits = limits;
		}

		public OriginalLayoutDocument Read( bool requireWholeInput )
		{
			var scope = ReadScope( null, 0 );
			if ( requireWholeInput && position != data.Length )
				Fail( OriginalLayoutDiagnostic.TrailingData, "bytes remain after the outer end command" );
			return new OriginalLayoutDocument( origin, position,
				Convert.ToHexString( SHA256.HashData( data.Span[..position] ) ).ToLowerInvariant(),
				scope, controls.AsReadOnly() );
		}

		private ushort Word()
		{
			if ( words >= limits.MaximumWords )
				Fail( OriginalLayoutDiagnostic.WordLimit, "halfword budget exhausted" );
			if ( position > data.Length - 2 )
				Fail( OriginalLayoutDiagnostic.TruncatedWord, "complete big-endian halfword required" );
			var value = BinaryPrimitives.ReadUInt16BigEndian( data.Span.Slice( position, 2 ) );
			position += 2;
			words++;
			return value;
		}

		private short SignedWord() => unchecked((short)Word());

		private int Scalar()
		{
			uint low = Word(), high = Word();
			return unchecked((int)(low | high << 16));
		}

		private OriginalRect Rectangle() => new( SignedWord(), SignedWord(), SignedWord(), SignedWord() );

		private int Count( string description )
		{
			int count = SignedWord();
			if ( count < 0 || count > limits.MaximumArrayEntries )
				Fail( OriginalLayoutDiagnostic.ArrayLimit, $"{description} count {count} is outside the array budget" );
			return count;
		}

		private OriginalControl Create( int at, ushort type, uint attributes, int id, OriginalRect bounds,
			OriginalControl? parent, ushort code, int depth )
		{
			if ( type is < 1 or > 13 )
				Fail( OriginalLayoutDiagnostic.UnsupportedControlType, $"type {type} has no supported allocation", at );
			if ( controls.Count >= limits.MaximumControls )
				Fail( OriginalLayoutDiagnostic.ControlLimit, "control budget exhausted", at );
			var control = new OriginalControl( controls.Count, origin.DataOffset + at, type, attributes,
				id, parent?.Index, code, bounds );
			controls.Add( control );
			control.Body = ReadScope( control, depth + 1 );
			return control;
		}

		private OriginalControl RequireParent( OriginalControl? parent, params ushort[] types )
		{
			if ( parent is null )
				Fail( OriginalLayoutDiagnostic.UnknownExternalParentType, "typed command cannot assume an existing caller's control type" );
			if ( !types.Contains( parent!.Type ) )
				Fail( OriginalLayoutDiagnostic.IncompatibleParent, $"parent type {parent.Type} is incompatible" );
			return parent;
		}

		private OriginalGeometry Geometry()
		{
			var subtype = Word();
			if ( subtype is 1 or 2 or 3 )
			{
				var values = new short[subtype == 2 ? 3 : 4];
				for ( var i = 0; i < values.Length; i++ ) values[i] = SignedWord();
				return new OriginalGeometry( subtype, Array.AsReadOnly( values ), Array.Empty<OriginalPoint>() );
			}
			if ( subtype != 4 )
				Fail( OriginalLayoutDiagnostic.UnknownGeometrySubtype, $"geometry subtype {subtype} is unproved" );
			var points = new OriginalPoint[Count( "polygon" )];
			for ( var i = 0; i < points.Length; i++ ) points[i] = new OriginalPoint( SignedWord(), SignedWord() );
			return new OriginalGeometry( subtype, Array.Empty<short>(), Array.AsReadOnly( points ) );
		}

		private OriginalScope ReadScope( OriginalControl? parent, int depth )
		{
			if ( depth > limits.MaximumDepth )
				Fail( OriginalLayoutDiagnostic.DepthLimit, "scope nesting budget exhausted" );
			var commands = new List<OriginalCommand>();
			while ( true )
			{
				var at = position;
				command = null;
				var code = Word();
				command = code;
				OriginalCommand item;
				switch ( code )
				{
					case 0:
						var type = Word();
						uint attributes = unchecked((uint)Scalar());
						var id = Scalar();
						item = new( code, origin.DataOffset + at, Control: Create( at, type, attributes, id, Rectangle(), parent, code, depth ) );
						break;
					case 1:
					case 2:
					case 17:
					case 18:
						item = new( code, origin.DataOffset + at, Scalar: Scalar() );
						break;
					case 3:
						item = new( code, origin.DataOffset + at, Rectangle: Rectangle() );
						break;
					case 4:
						item = new( code, origin.DataOffset + at, Geometry: Geometry() );
						break;
					case 5:
						commands.Add( new OriginalCommand( code, origin.DataOffset + at ) );
						return new OriginalScope( commands.AsReadOnly() );
					case 6:
					case 7:
					case 8:
					case 9:
					case 12:
					case 14:
					case 15:
					case 16:
						var owner = code switch
						{
							6 or 7 or 8 => RequireParent( parent, 3 ),
							9 => RequireParent( parent, 4, 7 ),
							12 => RequireParent( parent, 7 ),
							14 or 15 => RequireParent( parent, 12 ),
							_ => RequireParent( parent, 13 )
						};
						int childId;
						if ( code is 12 or 16 )
						{
							var number = SignedWord();
							if ( code == 16 && number is < 0 or >= 32 )
								Fail( OriginalLayoutDiagnostic.InvalidLightIndex, $"light index {number} is outside 0..31" );
							childId = code == 12 ? number + 16 : 0x11100000 | (ushort)number;
						}
						else childId = code switch { 6 or 9 or 14 => 1, 7 or 15 => 2, _ => 3 };
						if ( controls.Any( child => child.ParentIndex == owner.Index && child.Id == childId ) )
							Fail( OriginalLayoutDiagnostic.DuplicateImpliedChild, $"implied child {childId} already exists under this parent", at );
						ushort childType = code == 9 ? (ushort)3 : code == 16 ? (ushort)6 : (ushort)2;
						uint childAttributes = code == 9 ? 16u : code is 8 or 12 or 16 ? 1u : 33u;
						item = new( code, origin.DataOffset + at,
							Control: Create( at, childType, childAttributes, childId, Rectangle(), owner, code, depth ) );
						break;
					case 10:
						RequireParent( parent, 4, 7 );
						item = new( code, origin.DataOffset + at, Rectangle: Rectangle() );
						break;
					case 11:
						RequireParent( parent, 7 );
						var pairs = new OriginalPair[Count( "column" )];
						for ( var i = 0; i < pairs.Length; i++ ) pairs[i] = new OriginalPair( SignedWord(), SignedWord() );
						item = new( code, origin.DataOffset + at, Pairs: Array.AsReadOnly( pairs ) );
						break;
					case 13:
						RequireParent( parent, 11 );
						item = new( code, origin.DataOffset + at,
							Pairs: Array.AsReadOnly( new[] { new OriginalPair( SignedWord(), SignedWord() ) } ) );
						break;
					default:
						Fail( OriginalLayoutDiagnostic.UnknownCommand, $"command {code} is outside the proved 0..18 grammar", at );
						throw new InvalidOperationException();
				}
				commands.Add( item );
			}
		}

		private void Fail( OriginalLayoutDiagnostic diagnostic, string detail, int? relative = null )
		{
			throw new OriginalLayoutException( diagnostic, origin.DataOffset + (relative ?? position), command, detail );
		}
	}
}

public static class OriginalNodeNameHash
{
	public const int MaximumNameBytes = 255;

	/// <summary>Raw nonzero name bytes; original arithmetic sign-extends each byte before XOR.</summary>
	public static int Compute( ReadOnlySpan<byte> name )
	{
		if ( name.Length > MaximumNameBytes ) throw new ArgumentOutOfRangeException( nameof( name ) );
		uint hash = 0;
		foreach ( var value in name )
		{
			if ( value == 0 ) throw new ArgumentException( "Pass the name without its C-string terminator.", nameof( name ) );
			hash = unchecked((hash ^ (uint)(int)(sbyte)value) * 47u);
		}
		return unchecked((int)hash);
	}
}
