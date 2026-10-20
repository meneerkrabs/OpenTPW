using System.Globalization;
using System.Text;
using System.Text.Json;
using Point = OpenTPW.ContentKit.HandAnchorMeasure.Point;
using Sample = OpenTPW.ContentKit.HandAnchorMeasure.Sample;

namespace OpenTPW.ContentKit;

/// <summary>
/// The measurements behind the head anchors of the original sprite sets (docs/WINTER.md, "Anchors for the original sets"): for each frame the point where a hat's hotspot goes (the top of the head) and where earmuffs' go (the
/// head's centre), in pixels from the frame's hotspot, numbers only. Same method as the umbrellas' hand anchors (<see cref="HandAnchorMeasure"/>): an original kid set has a 2x redraw of the same pose, so its anchor is the redraw's
/// at half the pixels moved sideways by the silhouettes' mean x; where the head is the top of the redraw's silhouette (a figure whose arms are down) the anchor is also moved up or down by the difference of the two silhouettes' top edges.
/// A costume set has no redraw: a rule fitted on the kids (per slot and direction, the point's height above the silhouette's bottom edge and its distance from the box's centre) gives it.
/// </summary>
public static class HeadAnchorMeasure
{
	/// <summary>The animation slots a guest in the park is drawn with (standing, walking, waiting in a queue; the 2x kid sets of the pools extension have art for these only), with the names the 2x table gives them.</summary>
	public static readonly IReadOnlyList<(int Slot, string Name)> Slots = new[] { (0, "stand"), (1, "walk"), (14, "hands-on-hips") };
	/// <summary>How far, in 2x pixels, the redraw's head top may be below its silhouette's top edge for the head to count as the top of the figure.</summary>
	public const double HeadIsTopWithin = 3;

	/// <summary>The two points of a frame.</summary>
	public readonly record struct Head( Point Top, Point Centre );

	/// <summary>The head anchor of an original frame from the 2x redraw's: half the pixels, moved sideways to the original's silhouette and, when the head is the top of the redraw, vertically to the original's top edge.</summary>
	public static Head Transfer( FrameSilhouette original, FrameSilhouette redraw, Head redrawHead, double scale = 2 )
	{
		var top = HandAnchorMeasure.Transfer( original, redraw, redrawHead.Top, scale );
		var centre = HandAnchorMeasure.Transfer( original, redraw, redrawHead.Centre, scale );
		if ( redrawHead.Top.Y - redraw.Top <= HeadIsTopWithin )
		{
			var shift = Math.Clamp( original.Top - redraw.Top / scale, -HandAnchorMeasure.MaximumShift, HandAnchorMeasure.MaximumShift );
			top = new Point( top.X, top.Y + shift );
			centre = new Point( centre.X, centre.Y + shift );
		}
		return new Head( top, centre );
	}

	/// <summary>One set's table: per slot, directions, steps.</summary>
	public sealed record SetTable( string Name, string Source, IReadOnlyDictionary<int, Head[][]> Frames );

	private static string Number( double value ) => Math.Round( value, 1 ).ToString( "0.0", CultureInfo.InvariantCulture );

	/// <summary>Writes the table: the method, the fitted rule's error and every set. Numbers only.</summary>
	public static byte[] Write( IReadOnlyList<SetTable> tables, (double Mean, double Max, int Frames) error, int kidSets )
	{
		var text = new StringBuilder();
		text.Append( "{\n \"schema\": 2,\n \"generator\": \"opentpw-kit measure-head-anchors\",\n \"edition\": \"TPW\",\n \"pixelsPerUnit\": 128,\n \"directions\": 5,\n" );
		text.Append( " \"fields\": [\"headTopX\", \"headTopY\", \"headX\", \"headY\"],\n" );
		text.Append( " \"units\": \"pixels at 128 per unit from the feet hotspot of the game's own frames, x right and y down; directions 5 to 7 mirror 3 to 1 with x negated; one point per step; head only (an original frame has no reliable hand point, so mittens are not drawn on these sets)\",\n" );
		text.Append( " \"method\": \"kid sets: the 2x rig's head anchors at half the pixels, moved sideways by the difference of the silhouettes' mean x and, where the head is the top of the figure, vertically by the difference of the top edges (source transfer); costume sets: a rule fitted on the kids, the point's height as a share of the silhouette's height above its bottom edge and its x from the box's centre as a share of half its width (source rule); numbers only, no pixels\",\n" );
		text.Append( $" \"rule\": {{ \"fittedOnSets\": {kidSets.ToString( CultureInfo.InvariantCulture )}, \"leaveOneSetOut\": {{ \"point\": \"headTop\", \"frames\": {error.Frames.ToString( CultureInfo.InvariantCulture )}, \"meanPixels\": {Math.Round( error.Mean, 2 ).ToString( CultureInfo.InvariantCulture )}, \"maxPixels\": {Math.Round( error.Max, 2 ).ToString( CultureInfo.InvariantCulture )} }} }},\n \"sets\": {{\n" );
		var setTexts = new List<string>();
		foreach ( var table in tables )
		{
			var slotTexts = new List<string>();
			foreach ( var (slot, name) in Slots )
			{
				if ( !table.Frames.TryGetValue( slot, out var directions ) )
					continue;
				var directionTexts = directions.Select( steps => "[" + string.Join( ",", steps.Select( head => $"[{Number( head.Top.X )},{Number( head.Top.Y )},{Number( head.Centre.X )},{Number( head.Centre.Y )}]" ) ) + "]" );
				slotTexts.Add( $"    \"{name}\": {{ \"slot\": {slot}, \"points\": [{string.Join( ",", directionTexts )}] }}" );
			}
			setTexts.Add( $"  \"{table.Name}\": {{\n   \"source\": \"{table.Source}\",\n   \"slots\": {{\n{string.Join( ",\n", slotTexts )}\n   }}\n  }}" );
		}
		text.Append( string.Join( ",\n", setTexts ) ).Append( "\n }\n}\n" );
		var bytes = Encoding.UTF8.GetBytes( text.ToString() );
		using ( JsonDocument.Parse( bytes ) )
		{
		}
		return bytes;
	}
}

/// <summary>
/// <c>measure-head-anchors --data &lt;game Data folder&gt; --anchors &lt;winter anchors.json&gt; --pools &lt;folder of the 2x kid sets&gt; --out &lt;file&gt; [--force]</c>: reads the player's own kid and costume sprite sets and writes the head anchors
/// of the original sets as numbers only (<see cref="HeadAnchorMeasure"/>). A maintainers' tool of the winter extension; the table it writes is committed, so players never run it.
/// </summary>
public static class MeasureHeadAnchorsCommand
{
	private static readonly string[] KidNames = { "BE", "BI", "CH", "FR", "KI", "SA", "SU", "TA" };
	private static readonly (string Theme, string Name)[] CostumeSets = { ("Jungle", "SPR_TI"), ("Fantasy", "SPR_BK"), ("Hallow", "SPR_HK"), ("Space", "SPR_SK") };
	private const int Directions = HandAnchorMeasure.Directions;

	public static CommandResult Run( CommandOptions options )
	{
		var data = options.Require( "data" );
		var anchorsPath = options.Require( "anchors" );
		var pools = options.Require( "pools" );
		var outPath = options.Require( "out" );
		var force = options.Has( "force" );
		options.RejectUnknown();
		var wadPath = Directory.EnumerateFiles( data ).FirstOrDefault( file => string.Equals( Path.GetFileName( file ), "esprites.wad", StringComparison.OrdinalIgnoreCase ) )
			?? throw new FileNotFoundException( $"There is no esprites.wad in '{data}' (give the game's Data folder)." );
		if ( File.Exists( outPath ) && !force )
			throw new IOException( $"{outPath} already exists; pass --force to replace it." );
		using var wad = new WadArchive( wadPath );
		using var anchors = JsonDocument.Parse( File.ReadAllBytes( anchorsPath ) );
		var warnings = new List<string>();

		var topSamples = new List<IReadOnlyList<Sample>>();
		var centreSamples = new List<Sample>();
		var tables = new List<HeadAnchorMeasure.SetTable>();
		foreach ( var kid in KidNames )
		{
			var (animations, bank) = Open( wad, $"Generic/Kids/SPR_{kid}" );
			using var animationStream = File.OpenRead( Path.Combine( pools, $"SPR_{kid}.ESP" ) );
			var redrawAnimations = new SpriteAnimationFile( animationStream );
			using var bankStream = File.OpenRead( Path.Combine( pools, $"SPR_{kid}.TPC" ) );
			var redrawBank = new SpriteBankFile( bankStream );
			var slots = anchors.RootElement.GetProperty( "sets" ).GetProperty( kid ).GetProperty( "slots" );
			var frames = new Dictionary<int, HeadAnchorMeasure.Head[][]>();
			var samples = new List<Sample>();
			foreach ( var (slot, name) in HeadAnchorMeasure.Slots )
			{
				var original = animations.Animations[slot];
				var redraw = redrawAnimations.Animations[slot];
				if ( original.IsEmpty || redraw.IsEmpty || original.Directions != Directions || redraw.FramesPerDirection != original.FramesPerDirection || !slots.TryGetProperty( name, out var slotTable ) )
				{
					warnings.Add( $"SPR_{kid} slot {slot} ({name}) does not line up with its 2x redraw (original {( original.IsEmpty ? "empty" : $"{original.Directions} directions x {original.FramesPerDirection} frames" )}, redraw {( redraw.IsEmpty ? "empty" : $"{redraw.Directions} x {redraw.FramesPerDirection}" )}); the head follows walking there." );
					continue;
				}
				var table = slotTable.GetProperty( "points" );
				var perDirection = new HeadAnchorMeasure.Head[Directions][];
				for ( var direction = 0; direction < Directions; direction++ )
				{
					perDirection[direction] = new HeadAnchorMeasure.Head[original.FramesPerDirection];
					for ( var step = 0; step < original.FramesPerDirection; step++ )
					{
						var point = table[direction][step];
						var redrawHead = new HeadAnchorMeasure.Head( new Point( point[0].GetDouble(), point[1].GetDouble() ), new Point( point[2].GetDouble(), point[3].GetDouble() ) );
						var originalOutline = FrameSilhouette.Of( bank.Frames[original.GetFrame( direction, step )] );
						var redrawOutline = FrameSilhouette.Of( redrawBank.Frames[redraw.GetFrame( direction, step )] );
						if ( originalOutline == null || redrawOutline == null )
							throw new InvalidDataException( $"SPR_{kid} slot {slot} direction {direction} step {step} has an empty frame." );
						var head = HeadAnchorMeasure.Transfer( originalOutline.Value, redrawOutline.Value, redrawHead );
						perDirection[direction][step] = head;
						samples.Add( new Sample( slot, direction, originalOutline.Value, head.Top ) );
						centreSamples.Add( new Sample( slot, direction, originalOutline.Value, head.Centre ) );
					}
				}
				frames[slot] = perDirection;
			}
			topSamples.Add( samples );
			tables.Add( new HeadAnchorMeasure.SetTable( $"SPR_{kid}", "transfer", frames ) );
		}
		var topRule = HandAnchorMeasure.FitRule( topSamples.SelectMany( set => set ) );
		var centreRule = HandAnchorMeasure.FitRule( centreSamples );
		var error = HandAnchorMeasure.LeaveOneSetOut( topSamples );

		foreach ( var (theme, name) in CostumeSets )
		{
			var (animations, bank) = Open( wad, $"{theme}/Costumes/{name}" );
			var frames = new Dictionary<int, HeadAnchorMeasure.Head[][]>();
			foreach ( var (slot, _) in HeadAnchorMeasure.Slots )
			{
				var animation = animations.Animations[slot];
				if ( animation.IsEmpty || animation.Directions != Directions || Enumerable.Range( 0, Directions ).Any( direction => !topRule.ContainsKey( (slot, direction) ) ) )
				{
					warnings.Add( $"{name} has no usable slot {slot}; the head follows walking or standing there." );
					continue;
				}
				var perDirection = new HeadAnchorMeasure.Head[Directions][];
				for ( var direction = 0; direction < Directions; direction++ )
				{
					perDirection[direction] = new HeadAnchorMeasure.Head[animation.FramesPerDirection];
					for ( var step = 0; step < animation.FramesPerDirection; step++ )
					{
						var outline = FrameSilhouette.Of( bank.Frames[animation.GetFrame( direction, step )] ) ?? throw new InvalidDataException( $"{name} slot {slot} direction {direction} step {step} has an empty frame." );
						perDirection[direction][step] = new HeadAnchorMeasure.Head( HandAnchorMeasure.Apply( topRule[(slot, direction)], outline ), HandAnchorMeasure.Apply( centreRule[(slot, direction)], outline ) );
					}
				}
				frames[slot] = perDirection;
			}
			tables.Add( new HeadAnchorMeasure.SetTable( name, "rule", frames ) );
		}
		var bytes = HeadAnchorMeasure.Write( tables, error, KidNames.Length );
		Directory.CreateDirectory( Path.GetDirectoryName( Path.GetFullPath( outPath ) )! );
		File.WriteAllBytes( outPath, bytes );
		warnings.Add( $"the rule's leave-one-set-out error of the head top over {error.Frames} frames: mean {error.Mean:0.00} px, largest {error.Max:0.00} px." );
		return new CommandResult( Path.GetDirectoryName( Path.GetFullPath( outPath ) )!, new[] { Path.GetFileName( outPath ) }, warnings );
	}

	private static (SpriteAnimationFile Animations, SpriteBankFile Bank) Open( WadArchive wad, string path )
	{
		byte[] Get( string file )
		{
			var parts = file.Split( '/' );
			ArchiveDirectory directory = wad.Root;
			for ( var index = 0; index < parts.Length - 1; index++ )
				directory = (ArchiveDirectory)directory.Children.First( child => string.Equals( child.Name, parts[index], StringComparison.OrdinalIgnoreCase ) );
			return ((ArchiveFile)directory.Children.First( child => string.Equals( child.Name, parts[^1], StringComparison.OrdinalIgnoreCase ) )).GetData();
		}
		return (new SpriteAnimationFile( new MemoryStream( Get( path + ".ESP" ) ) ), new SpriteBankFile( new MemoryStream( Get( path + ".TPC" ) ) ));
	}
}
