using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>One outline point in font units; <see cref="OnCurve"/> false marks a quadratic control point.</summary>
public readonly record struct TrueTypePoint( float X, float Y, bool OnCurve );

/// <summary>Horizontal metrics and bounding box of one glyph, in font units.</summary>
public readonly record struct TrueTypeGlyphMetrics( int AdvanceWidth, int LeftSideBearing, int XMin, int YMin, int XMax, int YMax, int ContourCount );

/// <summary>
/// Minimal read-only TrueType (glyf outline) parser for the 17 fonts in <c>Data/fonts.wad</c>
/// (see docs/COMPATIBILITY.md). Reads <c>head</c>, <c>hhea</c>, <c>maxp</c>, <c>hmtx</c>, <c>loca</c>,
/// <c>glyf</c> (simple and composite glyphs), <c>cmap</c> (formats 0, 4, 6 and 12), <c>name</c>,
/// <c>OS/2</c> (Windows ascent/descent) and <c>kern</c> format 0 when present. Hinting
/// instructions are ignored. All offsets are bounds-checked; malformed data throws
/// <see cref="InvalidDataException"/>.
/// </summary>
public sealed class TrueTypeFont
{
	public const int MaximumFileBytes = 16 * 1024 * 1024;
	private const int MaximumCompositeDepth = 8;

	private readonly byte[] data;
	private readonly Dictionary<string, (int Offset, int Length)> tables = new( StringComparer.Ordinal );
	private readonly int glyfOffset;
	private readonly int glyfLength;
	private readonly int[] glyphOffsets;
	private readonly ushort[] advances;
	private readonly short[] leftSideBearings;
	private readonly Dictionary<int, int> characterMap = new();
	private readonly Dictionary<(int Left, int Right), short> kerning = new();

	public string SourceName { get; }
	public string FamilyName { get; private set; } = "";
	public string SubfamilyName { get; private set; } = "";
	public string FullName { get; private set; } = "";
	public int UnitsPerEm { get; }
	public int XMin { get; }
	public int YMin { get; }
	public int XMax { get; }
	public int YMax { get; }
	public int Ascender { get; }
	public int Descender { get; }
	public int LineGap { get; }
	/// <summary>OS/2 usWinAscent (falls back to <see cref="Ascender"/>), used by GDI for cell height.</summary>
	public int WindowsAscent { get; }
	/// <summary>OS/2 usWinDescent as a positive value (falls back to -<see cref="Descender"/>).</summary>
	public int WindowsDescent { get; }
	public int WeightClass { get; }
	public int GlyphCount { get; }
	/// <summary>The (platform, encoding, format) of the cmap subtable in use.</summary>
	public (int Platform, int Encoding, int Format) CharacterMapSource { get; private set; }
	/// <summary>True when the cmap is a Windows symbol table (codes live at U+F000..U+F0FF).</summary>
	public bool IsSymbolFont { get; private set; }
	public IReadOnlyCollection<int> CodePoints => characterMap.Keys;
	public int KerningPairCount => kerning.Count;

	private TrueTypeFont( byte[] data, string sourceName )
	{
		this.data = data;
		SourceName = sourceName;
		if ( data.Length < 12 )
			throw new InvalidDataException( "TrueType data is shorter than its table directory." );
		var version = U32( 0 );
		if ( version != 0x00010000 && version != 0x74727565 )
			throw new InvalidDataException( $"Not a TrueType outline font (sfnt version 0x{version:X8})." );
		var tableCount = U16( 4 );
		Require( 12, tableCount * 16 );
		for ( var i = 0; i < tableCount; i++ )
		{
			var record = 12 + i * 16;
			var tag = Encoding.ASCII.GetString( data, record, 4 );
			var offset = (int)Math.Min( U32( record + 8 ), int.MaxValue );
			var length = (int)Math.Min( U32( record + 12 ), int.MaxValue );
			Require( offset, length );
			tables.TryAdd( tag, (offset, length) );
		}

		var head = Table( "head", 54 );
		UnitsPerEm = U16( head + 18 );
		if ( UnitsPerEm is < 16 or > 16384 )
			throw new InvalidDataException( $"unitsPerEm {UnitsPerEm} is out of range." );
		XMin = I16( head + 36 );
		YMin = I16( head + 38 );
		XMax = I16( head + 40 );
		YMax = I16( head + 42 );
		var longOffsets = I16( head + 50 ) != 0;

		var maxp = Table( "maxp", 6 );
		GlyphCount = U16( maxp + 4 );
		if ( GlyphCount == 0 )
			throw new InvalidDataException( "Font declares no glyphs." );

		var hhea = Table( "hhea", 36 );
		Ascender = I16( hhea + 4 );
		Descender = I16( hhea + 6 );
		LineGap = I16( hhea + 8 );
		var metricCount = U16( hhea + 34 );
		if ( metricCount == 0 || metricCount > GlyphCount )
			throw new InvalidDataException( $"hhea declares {metricCount} metrics for {GlyphCount} glyphs." );

		var hmtx = Table( "hmtx", metricCount * 4 + (GlyphCount - metricCount) * 2 );
		advances = new ushort[GlyphCount];
		leftSideBearings = new short[GlyphCount];
		for ( var i = 0; i < GlyphCount; i++ )
		{
			if ( i < metricCount )
			{
				advances[i] = U16( hmtx + i * 4 );
				leftSideBearings[i] = I16( hmtx + i * 4 + 2 );
			}
			else
			{
				advances[i] = advances[metricCount - 1];
				leftSideBearings[i] = I16( hmtx + metricCount * 4 + (i - metricCount) * 2 );
			}
		}

		(glyfOffset, glyfLength) = tables.TryGetValue( "glyf", out var glyf ) ? glyf : throw new InvalidDataException( "Font has no glyf table (only TrueType outlines are supported)." );
		var loca = Table( "loca", (GlyphCount + 1) * (longOffsets ? 4 : 2) );
		glyphOffsets = new int[GlyphCount + 1];
		for ( var i = 0; i <= GlyphCount; i++ )
		{
			var offset = longOffsets ? (long)U32( loca + i * 4 ) : U16( loca + i * 2 ) * 2L;
			if ( offset > glyfLength || (i > 0 && offset < glyphOffsets[i - 1]) )
				throw new InvalidDataException( $"loca entry {i} ({offset}) is outside the glyf table or out of order." );
			glyphOffsets[i] = (int)offset;
		}

		if ( tables.TryGetValue( "OS/2", out var os2 ) && os2.Length >= 78 )
		{
			WeightClass = U16( os2.Offset + 4 );
			WindowsAscent = U16( os2.Offset + 74 );
			WindowsDescent = U16( os2.Offset + 76 );
		}
		if ( WindowsAscent == 0 && WindowsDescent == 0 )
		{
			WindowsAscent = Ascender;
			WindowsDescent = -Descender;
		}

		ReadCharacterMap();
		ReadNames();
		ReadKerning();
	}

	/// <summary>Parses a complete TrueType file held in memory (the array is kept, not copied).</summary>
	public static TrueTypeFont Parse( byte[] data, string sourceName = "" )
	{
		ArgumentNullException.ThrowIfNull( data );
		if ( data.Length > MaximumFileBytes )
			throw new InvalidDataException( "TrueType data exceeds the size limit." );
		return new TrueTypeFont( data, sourceName );
	}

	public bool HasTable( string tag ) => tables.ContainsKey( tag );

	/// <summary>Glyph index for a Unicode code point; 0 (.notdef) when unmapped.</summary>
	public int GetGlyphIndex( int codePoint )
	{
		if ( characterMap.TryGetValue( codePoint, out var glyph ) )
			return glyph;
		// Windows symbol fonts map their 8-bit codes to U+F000..U+F0FF.
		if ( IsSymbolFont && codePoint is >= 0 and <= 0xFF && characterMap.TryGetValue( 0xF000 + codePoint, out glyph ) )
			return glyph;
		return 0;
	}

	public bool HasGlyph( int codePoint ) => GetGlyphIndex( codePoint ) != 0;

	public TrueTypeGlyphMetrics GetMetrics( int glyph )
	{
		CheckGlyph( glyph );
		var (offset, length) = GlyphRange( glyph );
		if ( length == 0 )
			return new TrueTypeGlyphMetrics( advances[glyph], leftSideBearings[glyph], 0, 0, 0, 0, 0 );
		Require( offset, 10 );
		return new TrueTypeGlyphMetrics( advances[glyph], leftSideBearings[glyph], I16( offset + 2 ), I16( offset + 4 ), I16( offset + 6 ), I16( offset + 8 ), I16( offset ) );
	}

	/// <summary>Pair kerning from a format-0 <c>kern</c> table, in font units (0 when absent).</summary>
	public int GetKerning( int leftGlyph, int rightGlyph ) => kerning.TryGetValue( (leftGlyph, rightGlyph), out var value ) ? value : 0;

	/// <summary>Scale for a GDI negative <c>lfHeight</c>: pixels per em.</summary>
	public float ScaleForEmHeight( float pixels ) => pixels / UnitsPerEm;

	/// <summary>Scale for a GDI positive <c>lfHeight</c>: cell height = Windows ascent + descent.</summary>
	public float ScaleForCellHeight( float pixels ) => pixels / Math.Max( 1, WindowsAscent + WindowsDescent );

	/// <summary>Scale for a GDI-style height: negative = em height, positive = cell height.</summary>
	public float ScaleForGdiHeight( int lfHeight ) => lfHeight < 0 ? ScaleForEmHeight( -lfHeight ) : ScaleForCellHeight( Math.Max( 1, lfHeight ) );

	/// <summary>
	/// Glyph outline as closed contours in font units (Y up). Composite glyphs are flattened with
	/// their component offsets and 2x2 transforms; point-matched component placement (rare, and
	/// unused by the shipped fonts) is placed at offset zero.
	/// </summary>
	public IReadOnlyList<IReadOnlyList<TrueTypePoint>> GetOutline( int glyph )
	{
		CheckGlyph( glyph );
		var contours = new List<IReadOnlyList<TrueTypePoint>>();
		AppendOutline( glyph, System.Numerics.Matrix3x2.Identity, contours, 0 );
		return contours;
	}

	private void AppendOutline( int glyph, System.Numerics.Matrix3x2 transform, List<IReadOnlyList<TrueTypePoint>> contours, int depth )
	{
		if ( depth > MaximumCompositeDepth )
			throw new InvalidDataException( "Composite glyph nesting is too deep." );
		var (offset, length) = GlyphRange( glyph );
		if ( length == 0 )
			return;
		Require( offset, 10 );
		var end = offset + length;
		var contourCount = I16( offset );
		if ( contourCount >= 0 )
		{
			AppendSimple( offset, end, contourCount, transform, contours );
			return;
		}

		var position = offset + 10;
		while ( true )
		{
			RequireWithin( position, 4, end );
			var flags = U16( position );
			var component = U16( position + 2 );
			position += 4;
			CheckGlyph( component );
			float dx, dy;
			var wordArguments = (flags & 0x0001) != 0;
			var xyValues = (flags & 0x0002) != 0;
			if ( wordArguments )
			{
				RequireWithin( position, 4, end );
				dx = I16( position );
				dy = I16( position + 2 );
				position += 4;
			}
			else
			{
				RequireWithin( position, 2, end );
				dx = (sbyte)data[position];
				dy = (sbyte)data[position + 1];
				position += 2;
			}
			if ( !xyValues )
				dx = dy = 0;
			float a = 1, b = 0, c = 0, d = 1;
			if ( (flags & 0x0008) != 0 )
			{
				RequireWithin( position, 2, end );
				a = d = F2Dot14( position );
				position += 2;
			}
			else if ( (flags & 0x0040) != 0 )
			{
				RequireWithin( position, 4, end );
				a = F2Dot14( position );
				d = F2Dot14( position + 2 );
				position += 4;
			}
			else if ( (flags & 0x0080) != 0 )
			{
				RequireWithin( position, 8, end );
				a = F2Dot14( position );
				b = F2Dot14( position + 2 );
				c = F2Dot14( position + 4 );
				d = F2Dot14( position + 6 );
				position += 8;
			}
			var local = new System.Numerics.Matrix3x2( a, b, c, d, dx, dy );
			AppendOutline( component, local * transform, contours, depth + 1 );
			if ( (flags & 0x0020) == 0 )
				break;
		}
	}

	private void AppendSimple( int offset, int end, int contourCount, System.Numerics.Matrix3x2 transform, List<IReadOnlyList<TrueTypePoint>> contours )
	{
		if ( contourCount == 0 )
			return;
		var position = offset + 10;
		RequireWithin( position, contourCount * 2 + 2, end );
		var endPoints = new int[contourCount];
		for ( var i = 0; i < contourCount; i++ )
		{
			endPoints[i] = U16( position + i * 2 );
			if ( i > 0 && endPoints[i] < endPoints[i - 1] )
				throw new InvalidDataException( "Contour end points are not increasing." );
		}
		position += contourCount * 2;
		var pointCount = endPoints[^1] + 1;
		var instructionLength = U16( position );
		position += 2 + instructionLength;

		var flags = new byte[pointCount];
		for ( var i = 0; i < pointCount; )
		{
			RequireWithin( position, 1, end );
			var flag = data[position++];
			flags[i++] = flag;
			if ( (flag & 0x08) != 0 )
			{
				RequireWithin( position, 1, end );
				var repeat = data[position++];
				for ( var r = 0; r < repeat && i < pointCount; r++ )
					flags[i++] = flag;
			}
		}

		var xs = new int[pointCount];
		var ys = new int[pointCount];
		position = ReadCoordinates( flags, xs, position, end, 0x02, 0x10 );
		ReadCoordinates( flags, ys, position, end, 0x04, 0x20 );

		var start = 0;
		foreach ( var last in endPoints )
		{
			var contour = new List<TrueTypePoint>( last - start + 1 );
			for ( var i = start; i <= last; i++ )
			{
				var point = System.Numerics.Vector2.Transform( new System.Numerics.Vector2( xs[i], ys[i] ), transform );
				contour.Add( new TrueTypePoint( point.X, point.Y, (flags[i] & 0x01) != 0 ) );
			}
			if ( contour.Count > 0 )
				contours.Add( contour );
			start = last + 1;
		}
	}

	private int ReadCoordinates( byte[] flags, int[] values, int position, int end, byte shortFlag, byte sameOrPositiveFlag )
	{
		var value = 0;
		for ( var i = 0; i < flags.Length; i++ )
		{
			var flag = flags[i];
			if ( (flag & shortFlag) != 0 )
			{
				RequireWithin( position, 1, end );
				var delta = data[position++];
				value += (flag & sameOrPositiveFlag) != 0 ? delta : -delta;
			}
			else if ( (flag & sameOrPositiveFlag) == 0 )
			{
				RequireWithin( position, 2, end );
				value += I16( position );
				position += 2;
			}
			values[i] = value;
		}
		return position;
	}

	private void ReadCharacterMap()
	{
		var cmap = Table( "cmap", 4 );
		var count = U16( cmap + 2 );
		Require( cmap + 4, count * 8 );
		var candidates = new List<(int Platform, int Encoding, int Offset, int Format, int Priority)>();
		for ( var i = 0; i < count; i++ )
		{
			var record = cmap + 4 + i * 8;
			var platform = U16( record );
			var encoding = U16( record + 2 );
			var offset = cmap + (int)Math.Min( U32( record + 4 ), int.MaxValue / 2 );
			Require( offset, 2 );
			var format = U16( offset );
			var priority = (platform, encoding) switch
			{
				(3, 10) => 0,
				(3, 1) => 1,
				(0, _) => 2,
				(3, 0) => 3,
				(1, 0) => 4,
				_ => -1
			};
			if ( priority >= 0 && format is 0 or 4 or 6 or 12 )
				candidates.Add( (platform, encoding, offset, format, priority) );
		}
		if ( candidates.Count == 0 )
			throw new InvalidDataException( "Font has no supported cmap subtable." );
		var best = candidates.OrderBy( candidate => candidate.Priority ).First();
		CharacterMapSource = (best.Platform, best.Encoding, best.Format);
		IsSymbolFont = best.Platform == 3 && best.Encoding == 0;
		var macRoman = best.Platform == 1;
		switch ( best.Format )
		{
			case 0:
				Require( best.Offset, 262 );
				for ( var code = 0; code < 256; code++ )
					AddMapping( macRoman ? MacRomanToUnicode( code ) : code, data[best.Offset + 6 + code] );
				break;
			case 4:
				ReadFormat4( best.Offset );
				break;
			case 6:
			{
				Require( best.Offset, 10 );
				var first = U16( best.Offset + 6 );
				var entryCount = U16( best.Offset + 8 );
				Require( best.Offset + 10, entryCount * 2 );
				for ( var i = 0; i < entryCount; i++ )
					AddMapping( macRoman ? MacRomanToUnicode( first + i ) : first + i, U16( best.Offset + 10 + i * 2 ) );
				break;
			}
			case 12:
			{
				Require( best.Offset, 16 );
				var groups = U32( best.Offset + 12 );
				if ( groups > 65536 )
					throw new InvalidDataException( "cmap format 12 declares too many groups." );
				Require( best.Offset + 16, (int)groups * 12 );
				for ( var i = 0; i < groups; i++ )
				{
					var group = best.Offset + 16 + i * 12;
					var startCode = U32( group );
					var endCode = U32( group + 4 );
					var startGlyph = U32( group + 8 );
					if ( endCode < startCode || endCode - startCode > 0x10000 || endCode > 0x10FFFF )
						continue;
					for ( var code = startCode; code <= endCode; code++ )
						AddMapping( (int)code, (int)(startGlyph + code - startCode) );
				}
				break;
			}
		}
	}

	private void ReadFormat4( int offset )
	{
		Require( offset, 14 );
		var segmentCount = U16( offset + 6 ) / 2;
		var endCodes = offset + 14;
		var startCodes = endCodes + segmentCount * 2 + 2;
		var deltas = startCodes + segmentCount * 2;
		var rangeOffsets = deltas + segmentCount * 2;
		Require( endCodes, segmentCount * 8 + 2 );
		for ( var segment = 0; segment < segmentCount; segment++ )
		{
			var endCode = U16( endCodes + segment * 2 );
			var startCode = U16( startCodes + segment * 2 );
			var delta = I16( deltas + segment * 2 );
			var rangeOffsetPosition = rangeOffsets + segment * 2;
			var rangeOffset = U16( rangeOffsetPosition );
			if ( startCode > endCode )
				continue;
			for ( var code = startCode; code <= endCode && code != 0xFFFF; code++ )
			{
				int glyph;
				if ( rangeOffset == 0 )
					glyph = (code + delta) & 0xFFFF;
				else
				{
					var glyphPosition = rangeOffsetPosition + rangeOffset + (code - startCode) * 2;
					if ( glyphPosition < 0 || glyphPosition + 2 > data.Length )
						continue;
					glyph = U16( glyphPosition );
					if ( glyph != 0 )
						glyph = (glyph + delta) & 0xFFFF;
				}
				AddMapping( code, glyph );
			}
		}
	}

	private void AddMapping( int code, int glyph )
	{
		if ( glyph > 0 && glyph < GlyphCount )
			characterMap.TryAdd( code, glyph );
	}

	private void ReadNames()
	{
		if ( !tables.TryGetValue( "name", out var name ) || name.Length < 6 )
			return;
		var count = U16( name.Offset + 2 );
		var storage = name.Offset + U16( name.Offset + 4 );
		var found = new Dictionary<int, (int Priority, string Value)>();
		for ( var i = 0; i < count; i++ )
		{
			var record = name.Offset + 6 + i * 12;
			if ( record + 12 > name.Offset + name.Length )
				break;
			var platform = U16( record );
			var encoding = U16( record + 2 );
			var language = U16( record + 4 );
			var nameId = U16( record + 6 );
			var length = U16( record + 8 );
			var offset = storage + U16( record + 10 );
			if ( nameId is not (1 or 2 or 4) || offset + length > data.Length )
				continue;
			string? value = null;
			var priority = 9;
			if ( platform == 3 && encoding is 0 or 1 )
			{
				value = Encoding.BigEndianUnicode.GetString( data, offset, length & ~1 );
				priority = language == 0x0409 ? 0 : 1;
			}
			else if ( platform == 1 && encoding == 0 )
			{
				value = Encoding.Latin1.GetString( data, offset, length );
				priority = 2;
			}
			if ( value != null && (!found.TryGetValue( nameId, out var existing ) || priority < existing.Priority) )
				found[nameId] = (priority, value.TrimEnd( '\0' ));
		}
		FamilyName = found.TryGetValue( 1, out var family ) ? family.Value : "";
		SubfamilyName = found.TryGetValue( 2, out var subfamily ) ? subfamily.Value : "";
		FullName = found.TryGetValue( 4, out var full ) ? full.Value : FamilyName;
	}

	private void ReadKerning()
	{
		if ( !tables.TryGetValue( "kern", out var kern ) || kern.Length < 4 || U16( kern.Offset ) != 0 )
			return;
		var subtableCount = U16( kern.Offset + 2 );
		var position = kern.Offset + 4;
		var end = kern.Offset + kern.Length;
		for ( var i = 0; i < subtableCount && position + 6 <= end; i++ )
		{
			var length = U16( position + 2 );
			var coverage = U16( position + 4 );
			// Horizontal (bit 0), not minimum (bit 1), not cross-stream (bit 2), format 0 (high byte).
			if ( (coverage & 0x07) == 0x01 && (coverage >> 8) == 0 && position + 14 <= end )
			{
				var pairs = U16( position + 6 );
				for ( var p = 0; p < pairs; p++ )
				{
					var pair = position + 14 + p * 6;
					if ( pair + 6 > end )
						break;
					kerning.TryAdd( (U16( pair ), U16( pair + 2 )), (short)I16( pair + 4 ) );
				}
			}
			if ( length < 6 )
				break;
			position += length;
		}
	}

	private (int Offset, int Length) GlyphRange( int glyph ) =>
		(glyfOffset + glyphOffsets[glyph], glyphOffsets[glyph + 1] - glyphOffsets[glyph]);

	private void CheckGlyph( int glyph )
	{
		if ( glyph < 0 || glyph >= GlyphCount )
			throw new ArgumentOutOfRangeException( nameof( glyph ), $"Glyph {glyph} is outside 0..{GlyphCount - 1}." );
	}

	private int Table( string tag, int minimumLength )
	{
		if ( !tables.TryGetValue( tag, out var table ) )
			throw new InvalidDataException( $"Font has no {tag} table." );
		if ( table.Length < minimumLength )
			throw new InvalidDataException( $"The {tag} table is {table.Length} bytes; at least {minimumLength} are required." );
		return table.Offset;
	}

	private void Require( int offset, int length )
	{
		if ( offset < 0 || length < 0 || offset > data.Length - length )
			throw new InvalidDataException( $"TrueType data range {offset}+{length} is outside the {data.Length}-byte file." );
	}

	private void RequireWithin( int offset, int length, int end )
	{
		if ( offset < 0 || offset + length > end || end > data.Length )
			throw new InvalidDataException( "Glyph data is truncated." );
	}

	private ushort U16( int offset ) => BinaryPrimitives.ReadUInt16BigEndian( data.AsSpan( offset, 2 ) );
	private short I16( int offset ) => BinaryPrimitives.ReadInt16BigEndian( data.AsSpan( offset, 2 ) );
	private uint U32( int offset ) => BinaryPrimitives.ReadUInt32BigEndian( data.AsSpan( offset, 4 ) );
	private float F2Dot14( int offset ) => I16( offset ) / 16384f;

	private static readonly char[] MacRomanHigh =
		"ÄÅÇÉÑÖÜáàâäãåçéèêëíìîïñóòôöõúùûü†°¢£§•¶ß®©™´¨≠ÆØ∞±≤≥¥µ∂∑∏π∫ªºΩæø¿¡¬√ƒ≈∆«»… ÀÃÕŒœ–—“”‘’÷◊ÿŸ⁄€‹›ﬁﬂ‡·‚„‰ÂÊÁËÈÍÎÏÌÓÔÒÚÛÙıˆ˜¯˘˙˚¸˝˛ˇ".ToCharArray();

	private static int MacRomanToUnicode( int code ) => code < 128 ? code : MacRomanHigh[code - 128];
}
