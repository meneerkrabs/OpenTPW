using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>One point of a cross section (<c>asCrossSectionPoints*[i]</c>): the offset across (X) and up (Y) from the track curve, the texture U and the outward normal.</summary>
public readonly record struct CoasterSectionPoint( float X, float Y, float U, float Nx, float Ny );

/// <summary>One edge of a cross section (<c>asCrossSectionEdges*[i]</c>): a strip between two points, drawn with a track texture.</summary>
public readonly record struct CoasterSectionEdge( int Texture, int From, int To );

/// <summary>A track texture (<c>asTextureData[i]</c>): the file, its scroll rate in texture repeats per second and whether it ignores the light.</summary>
public sealed record CoasterTrackTexture( string FileName, float ScrollRate, bool SelfIlluminated );

/// <summary>One cross section (<c>asCrossSections[i]</c>): a point block and an edge block (1-based), the track edge and an optional blend target.</summary>
public sealed record CoasterCrossSection( int PointBlock, int EdgeBlock, int TrackEdge, int BlendToPointBlock, float BlendLength );

/// <summary>One cross-section select (<c>asCrossSectionSelects[i]</c>): the section it picks (1-based), the pitch window in degrees it applies to and, for a blend select, its two sections.</summary>
public sealed record CoasterSectionSelect( int CrossSection, float? PitchMin, float? PitchMax, int BlendFrom, int BlendTo, IReadOnlyList<float>? Windows = null )
{
	/// <summary>
	/// The eight window singles of the original's table entry, in radians: minimum and maximum of the speed, direction, pitch and bank ranges, -FLT_MAX and FLT_MAX where the sam names none
	/// (all zero counts as none). Null for a select built without them.
	/// </summary>
	public bool IsBlend => BlendFrom != 0 || BlendTo != 0;

	public bool Matches( float pitchDegrees ) => (PitchMin == null || pitchDegrees >= PitchMin) && (PitchMax == null || pitchDegrees <= PitchMax);
}

/// <summary>
/// The cross sections of a coaster's track (<c>coaster.sam</c>: <c>asTextureData</c>, <c>asCrossSectionPoints1..20</c>,
/// <c>asCrossSectionEdges1..20</c>, <c>asCrossSections</c>, <c>asCrossSectionSelects</c> and <c>sCoasterType.uiWinchCrossSection</c>).
/// The track mesh is the extrusion of one section along the curve slices (<see cref="CoasterTrackMesh"/>).
/// </summary>
// [BIN:TPW-EXE:0x42d5a0 cross section setup] the loader copies the point blocks as five floats (X, Y, U, Nx, Ny) and adds the definition's fTrackHeightAdjust to every Y (0x42d6d0), then builds the edge blocks (0x42d130), the sections and the selects (0x42e200); the block counts are read from the settings record
// [BIN:TPW-EXE:0x43e9c0 cross section select] the selects are tested in order against the slice's three angles (yaw, pitch, roll windows); the last select is the fallback when none matches. A flume's dry section applies to the climbing pitch window of -90..-1 degrees
public sealed class CoasterCrossSections
{
	public const int MaxBlocks = 20;
	private static readonly Regex PointKey = new( @"^asCrossSectionPoints(\d+)\[(\d+)\]\.(\w+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );
	private static readonly Regex EdgeKey = new( @"^asCrossSectionEdges(\d+)\[(\d+)\]\.(\w+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );
	private static readonly Regex TextureKey = new( @"^asTextureData\[(\d+)\]\.(\w+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );
	private static readonly Regex SectionKey = new( @"^asCrossSections\[(\d+)\]\.(\w+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );
	private static readonly Regex SelectKey = new( @"^asCrossSectionSelects\[(\d+)\]\.(\w+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );

	public IReadOnlyList<CoasterTrackTexture> Textures { get; private set; } = Array.Empty<CoasterTrackTexture>();
	/// <summary>The point blocks, index 0 = block 1.</summary>
	public IReadOnlyList<IReadOnlyList<CoasterSectionPoint>> PointBlocks { get; private set; } = Array.Empty<IReadOnlyList<CoasterSectionPoint>>();
	/// <summary>The edge blocks, index 0 = block 1.</summary>
	public IReadOnlyList<IReadOnlyList<CoasterSectionEdge>> EdgeBlocks { get; private set; } = Array.Empty<IReadOnlyList<CoasterSectionEdge>>();
	/// <summary>The sections, index 0 = <c>asCrossSections[0]</c> (referenced 1-based).</summary>
	public IReadOnlyList<CoasterCrossSection> Sections { get; private set; } = Array.Empty<CoasterCrossSection>();
	public IReadOnlyList<CoasterSectionSelect> Selects { get; private set; } = Array.Empty<CoasterSectionSelect>();
	/// <summary>
	/// <c>sCoasterType.uiInvalidCrossSection</c> (1-based; no PC <c>coaster.sam</c> sets it, so 0): the section the loader copies to an extra last section for the
	/// links drawn in a marker texture (<c>0x42d5a0</c>), the first section when 0.
	/// </summary>
	// [BIN:TPW-EXE:0x42d5a0 invalid cross section] the section array holds the sections plus one: the last entry is a copy of section (uiInvalidCrossSection − 1, or 0 when it is 0) with its own copy of the edges, whose textures the slice emitter overwrites (definition +0x80)
	public int InvalidSection { get; private set; }
	/// <summary>The index (0-based into <see cref="Sections"/>) of the section the marker links copy.</summary>
	public int InvalidSectionIndex => InvalidSection >= 1 && InvalidSection <= Sections.Count ? InvalidSection - 1 : 0;
	/// <summary><c>sCoasterType.uiWinchCrossSection</c> (1-based), 0 for none: the section a chain-lift segment uses.</summary>
	public int WinchSection { get; private set; }
	/// <summary>The value added to every point Y (<c>fTrackHeightAdjust</c>).</summary>
	public float HeightAdjust { get; private set; }

	public bool IsEmpty => Sections.Count == 0 || PointBlocks.Count == 0 || EdgeBlocks.Count == 0;

	public static CoasterCrossSections FromSettings( ObjectSettings settings ) => FromValues( settings.Values );

	public static CoasterCrossSections FromValues( IReadOnlyDictionary<string, string> values )
	{
		ArgumentNullException.ThrowIfNull( values );
		var textures = new SortedDictionary<int, (string File, float Scroll, bool Lit)>();
		var points = new SortedDictionary<int, SortedDictionary<int, float[]>>();
		var edges = new SortedDictionary<int, SortedDictionary<int, int[]>>();
		var sections = new SortedDictionary<int, int[]>();
		var sectionBlend = new Dictionary<int, float>();
		var selects = new SortedDictionary<int, (int Section, float? Min, float? Max, int From, int To, float[]? Ranges)>();
		var winch = 0;
		var invalid = 0;
		var adjust = 0f;
		foreach ( var (key, value) in values )
		{
			if ( TextureKey.Match( key ) is { Success: true } texture )
			{
				var index = int.Parse( texture.Groups[1].Value, CultureInfo.InvariantCulture );
				textures.TryGetValue( index, out var entry );
				switch ( texture.Groups[2].Value.ToLowerInvariant() )
				{
					case "pctexturefilename": entry.File = value.Trim().Trim( '"' ); break;
					case "fscrollrate": entry.Scroll = Float( value ); break;
					case "bisselfilluminating": entry.Lit = Float( value ) != 0; break;
				}
				textures[index] = entry;
			}
			else if ( PointKey.Match( key ) is { Success: true } point )
			{
				var block = int.Parse( point.Groups[1].Value, CultureInfo.InvariantCulture );
				var index = int.Parse( point.Groups[2].Value, CultureInfo.InvariantCulture );
				if ( block < 1 || block > MaxBlocks )
					continue;
				if ( !points.TryGetValue( block, out var list ) )
					points[block] = list = new SortedDictionary<int, float[]>();
				if ( !list.TryGetValue( index, out var fields ) )
					list[index] = fields = new float[5];
				var slot = point.Groups[3].Value.ToLowerInvariant() switch { "fx" => 0, "fy" => 1, "fu" => 2, "fnx" => 3, "fny" => 4, _ => -1 };
				if ( slot >= 0 )
					fields[slot] = Float( value );
			}
			else if ( EdgeKey.Match( key ) is { Success: true } edge )
			{
				var block = int.Parse( edge.Groups[1].Value, CultureInfo.InvariantCulture );
				var index = int.Parse( edge.Groups[2].Value, CultureInfo.InvariantCulture );
				if ( block < 1 || block > MaxBlocks )
					continue;
				if ( !edges.TryGetValue( block, out var list ) )
					edges[block] = list = new SortedDictionary<int, int[]>();
				if ( !list.TryGetValue( index, out var fields ) )
					list[index] = fields = new int[3];
				var slot = edge.Groups[3].Value.ToLowerInvariant() switch { "ustexture" => 0, "uscspoint1" => 1, "uscspoint2" => 2, _ => -1 };
				if ( slot >= 0 )
					fields[slot] = (int)Float( value );
			}
			else if ( SectionKey.Match( key ) is { Success: true } section )
			{
				var index = int.Parse( section.Groups[1].Value, CultureInfo.InvariantCulture );
				if ( !sections.TryGetValue( index, out var fields ) )
					sections[index] = fields = new int[4];
				switch ( section.Groups[2].Value.ToLowerInvariant() )
				{
					case "uscspointblockindex": fields[0] = (int)Float( value ); break;
					case "uscsedgeblockindex": fields[1] = (int)Float( value ); break;
					case "ustrackedge": fields[2] = (int)Float( value ); break;
					case "usblendtocspointblockindex": fields[3] = (int)Float( value ); break;
					case "fblendlength": sectionBlend[index] = Float( value ); break;
				}
			}
			else if ( SelectKey.Match( key ) is { Success: true } select )
			{
				var index = int.Parse( select.Groups[1].Value, CultureInfo.InvariantCulture );
				selects.TryGetValue( index, out var entry );
				switch ( select.Groups[2].Value.ToLowerInvariant() )
				{
					case "uscrosssectionindex": entry.Section = (int)Float( value ); break;
					case "fpitchrangemin": entry.Min = Float( value ); entry.Ranges = Range( entry.Ranges, 4, Float( value ) ); break;
					case "fpitchrangemax": entry.Max = Float( value ); entry.Ranges = Range( entry.Ranges, 5, Float( value ) ); break;
					case "fspeedrangemin": entry.Ranges = Range( entry.Ranges, 0, Float( value ) ); break;
					case "fspeedrangemax": entry.Ranges = Range( entry.Ranges, 1, Float( value ) ); break;
					case "fdirectionrangemin": entry.Ranges = Range( entry.Ranges, 2, Float( value ) ); break;
					case "fdirectionrangemax": entry.Ranges = Range( entry.Ranges, 3, Float( value ) ); break;
					case "fbankrangemin": entry.Ranges = Range( entry.Ranges, 6, Float( value ) ); break;
					case "fbankrangemax": entry.Ranges = Range( entry.Ranges, 7, Float( value ) ); break;
					case "usblendfromcrosssectionindex": entry.From = (int)Float( value ); break;
					case "usblendtocrosssectionindex": entry.To = (int)Float( value ); break;
				}
				selects[index] = entry;
			}
			else if ( string.Equals( key, "sCoasterType.uiWinchCrossSection", StringComparison.OrdinalIgnoreCase ) )
				winch = (int)Float( value );
			else if ( string.Equals( key, "sCoasterType.uiInvalidCrossSection", StringComparison.OrdinalIgnoreCase ) )
				invalid = (int)Float( value );
			else if ( string.Equals( key, "sCoasterType.fTrackHeightAdjust", StringComparison.OrdinalIgnoreCase ) )
				adjust = Float( value );
		}
		var result = new CoasterCrossSections { HeightAdjust = adjust, WinchSection = winch, InvalidSection = invalid };
		result.Textures = Dense( textures, entry => new CoasterTrackTexture( entry.File ?? "", entry.Scroll, entry.Lit ), new CoasterTrackTexture( "", 0, false ) );
		result.PointBlocks = DenseBlocks( points, fields => new CoasterSectionPoint( fields[0], fields[1] + adjust, fields[2], fields[3], fields[4] ) );
		result.EdgeBlocks = DenseBlocks( edges, fields => new CoasterSectionEdge( fields[0], fields[1], fields[2] ) );
		result.Sections = Dense( sections, fields => new CoasterCrossSection( fields[0], fields[1], fields[2], fields[3], 0 ), new CoasterCrossSection( 0, 0, 0, 0, 0 ) )
			.Select( ( section, index ) => sectionBlend.TryGetValue( index, out var length ) ? section with { BlendLength = length } : section ).ToArray();
		result.Selects = Dense( selects, entry => new CoasterSectionSelect( entry.Section, entry.Min, entry.Max, entry.From, entry.To, Windows( entry.Ranges ) ), new CoasterSectionSelect( 0, null, null, 0, 0, Windows( null ) ) );
		return result;
	}

	private static float[] Range( float[]? ranges, int slot, float value )
	{
		ranges ??= new float[8];
		ranges[slot] = value;
		return ranges;
	}

	/// <summary>The table entry's eight window singles from the sam's degrees: a pair is taken only when its minimum or maximum is not zero, each value times the degree-to-radian single.</summary>
	// [BIN:TPW-EXE:0x42d130 select windows] every entry starts with -FLT_MAX and FLT_MAX in its four pairs (0x42d1b8..0x42d1d5); a pair of the sam (speed, direction, pitch, bank, eight words from the select's +0x08) replaces them only when its minimum or its maximum is not 0.0 (0x42d20f..0x42d227) and then holds each value times the single 0.017453292 (0x6fde78, 0x42d22d..0x42d245)
	private static float[] Windows( float[]? ranges )
	{
		var windows = new float[8];
		for ( var pair = 0; pair < 4; pair++ )
		{
			windows[2 * pair] = -float.MaxValue;
			windows[2 * pair + 1] = float.MaxValue;
			if ( ranges != null && (ranges[2 * pair] != 0 || ranges[2 * pair + 1] != 0) )
			{
				windows[2 * pair] = ranges[2 * pair] * DegreesToRadians;
				windows[2 * pair + 1] = ranges[2 * pair + 1] * DegreesToRadians;
			}
		}
		return windows;
	}

	private const float DegreesToRadians = 0.017453292f;

	private static float Float( string text ) => float.TryParse( text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) && float.IsFinite( value ) ? value : 0;

	private static IReadOnlyList<TOut> Dense<TIn, TOut>( SortedDictionary<int, TIn> source, Func<TIn, TOut> convert, TOut missing )
	{
		if ( source.Count == 0 )
			return Array.Empty<TOut>();
		var result = new TOut[Math.Min( source.Keys.Max() + 1, 256 )];
		Array.Fill( result, missing );
		foreach ( var (index, item) in source )
		{
			if ( index < result.Length )
				result[index] = convert( item );
		}
		return result;
	}

	private static IReadOnlyList<IReadOnlyList<TOut>> DenseBlocks<TIn, TOut>( SortedDictionary<int, SortedDictionary<int, TIn>> source, Func<TIn, TOut> convert )
	{
		if ( source.Count == 0 )
			return Array.Empty<IReadOnlyList<TOut>>();
		var blocks = new IReadOnlyList<TOut>[source.Keys.Max()];
		Array.Fill( blocks, Array.Empty<TOut>() );
		foreach ( var (block, items) in source )
			blocks[block - 1] = Dense( items, convert, default! );
		return blocks;
	}

	/// <summary>
	/// The section index (0-based into <see cref="Sections"/>) a slice of the given pitch uses: the winch section for a chain-lift
	/// segment, else the first non-blend select whose pitch window holds the pitch (negative pitch climbs), the last select otherwise. -1 without sections.
	/// </summary>
	public int SelectSection( float pitchRadians, bool winch )
	{
		if ( Sections.Count == 0 )
			return -1;
		if ( winch && WinchSection >= 1 && WinchSection <= Sections.Count )
			return WinchSection - 1;
		var degrees = pitchRadians * (180f / MathF.PI);
		CoasterSectionSelect? last = null;
		foreach ( var select in Selects )
		{
			if ( select.IsBlend || select.CrossSection < 1 )
				continue;
			last = select;
			if ( select.Matches( degrees ) )
				return Math.Clamp( select.CrossSection - 1, 0, Sections.Count - 1 );
		}
		return last == null ? 0 : Math.Clamp( last.CrossSection - 1, 0, Sections.Count - 1 );
	}

	/// <summary>Whether a section blends to a second point row (the type's flag <c>+0x74</c> bit 0): the run builder then looks for transition sections.</summary>
	// [BIN:TPW-EXE:0x42e19d transition flag] the section loader sets the flag word it hands to 0x42d130 (type +0x74 bit 0) when any section names a blend-to point block (0x42e19d); the block must have as many points as the section's own (0x42e1a7) and the section keeps its pointer at +0x10 and its blend length at +0x14
	public bool HasTransitions => Sections.Any( section => section.BlendToPointBlock != 0 );

	/// <summary>
	/// The section the select table gives for a track frame (<c>0x43e9c0</c>): the first of the entries but the last whose four windows hold the angle (the speed window against 0 in the
	/// mesh builder), the yaw, the pitch and the roll, its section; the last entry's section when none holds or the one found is none. -1 without selects.
	/// </summary>
	// [BIN:TPW-EXE:0x43e9c0 select table] the entries of 0x2c bytes (windows at +0x00..+0x1c, section at +0x20, transition from and to at +0x24 and +0x28) are walked up to count − 1, each tested with both ends inclusive (a value below the minimum or above the maximum fails, 0x43e9eb..0x43ea44); the last entry is only the fallback (0x43ea66..0x43ea76), also when the section found is null; the speed window is compared with the fourth argument, which the mesh builder passes as 0.0
	public int SelectAnswer( float angle, float yaw, float pitch, float roll )
	{
		var count = Selects.Count;
		if ( count == 0 )
			return -1;
		var found = -1;
		for ( var index = 0; index < count - 1; index++ )
		{
			var select = Selects[index];
			if ( select.Windows is not { Count: 8 } windows )
				continue;
			if ( !(angle >= windows[0]) || !(angle <= windows[1]) || !(yaw >= windows[2]) || !(yaw <= windows[3]) || !(pitch >= windows[4]) || !(pitch <= windows[5]) || !(roll >= windows[6]) || !(roll <= windows[7]) )
				continue;
			found = select.CrossSection - 1;
			break;
		}
		return found >= 0 ? found : Selects[count - 1].CrossSection - 1;
	}

	/// <summary>The section of the transition entry that goes from section <paramref name="from"/> to <paramref name="to"/> (0-based), -1 when no entry names the pair.</summary>
	public int SelectTransition( int from, int to )
	{
		foreach ( var select in Selects )
		{
			if ( select.BlendFrom - 1 == from && select.BlendTo - 1 == to && select.BlendFrom != 0 && select.CrossSection >= 1 )
				return select.CrossSection - 1;
		}
		return -1;
	}

	/// <summary>The points and edges of a section (empty when its blocks are missing).</summary>
	public (IReadOnlyList<CoasterSectionPoint> Points, IReadOnlyList<CoasterSectionEdge> Edges) Resolve( int section )
	{
		if ( section < 0 || section >= Sections.Count )
			return (Array.Empty<CoasterSectionPoint>(), Array.Empty<CoasterSectionEdge>());
		var entry = Sections[section];
		var points = entry.PointBlock >= 1 && entry.PointBlock <= PointBlocks.Count ? PointBlocks[entry.PointBlock - 1] : Array.Empty<CoasterSectionPoint>();
		var edges = entry.EdgeBlock >= 1 && entry.EdgeBlock <= EdgeBlocks.Count ? EdgeBlocks[entry.EdgeBlock - 1] : Array.Empty<CoasterSectionEdge>();
		return (points, edges);
	}
}
