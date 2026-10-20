using System.Globalization;
using System.Text;
using System.Text.Json;
using Sample = OpenTPW.ContentKit.HandAnchorMeasure.Sample;

namespace OpenTPW.ContentKit;

/// <summary>
/// The outline of one sprite frame as numbers, in pixels from the frame's hotspot (x right, y down): the box around the opaque pixels and the mean x of the opaque pixels. Nothing of the picture is kept.
/// </summary>
public readonly record struct FrameSilhouette( double Left, double Top, double Right, double Bottom, double CentroidX )
{
	public double Width => Right - Left;
	public double Height => Bottom - Top;
	public double CenterX => (Left + Right) / 2;

	/// <summary>The silhouette of a frame (palette index 0 is transparent), or null for a frame with no opaque pixel.</summary>
	public static FrameSilhouette? Of( SpriteFrame frame )
	{
		int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
		double sumX = 0;
		long count = 0;
		for ( var y = 0; y < frame.Height; y++ )
			for ( var x = 0; x < frame.Width; x++ )
			{
				if ( frame.Indices[y * frame.Width + x] == 0 )
					continue;
				minX = Math.Min( minX, x );
				maxX = Math.Max( maxX, x );
				minY = Math.Min( minY, y );
				maxY = Math.Max( maxY, y );
				sumX += frame.OriginX + x + 0.5;
				count++;
			}
		return count == 0 ? null : new FrameSilhouette( frame.OriginX + minX, frame.OriginY + minY, frame.OriginX + maxX + 1, frame.OriginY + maxY + 1, sumX / count );
	}
}

/// <summary>
/// The measurements behind the hand anchors of the original sprite sets (docs/UMBRELLAS.md, "Anchors for the original sets"). An anchor is the point, in pixels from the frame's hotspot (the feet), where a guest's right
/// hand is in that frame, so an umbrella's handle can be put there. The extension's own 2x kid sets have exact anchors from the rig that drew them; the game's frames carry none, so they are derived from the frames
/// themselves, as numbers only:
/// <list type="bullet">
/// <item>An original kid set has a 2x redraw of the same pose in the same slot, direction and step (the pools extension's sets), so its anchor is the 2x anchor at half the pixels (the redraw is
/// twice the resolution), moved sideways by the difference between the two silhouettes' mean x. <see cref="Transfer"/></item>
/// <item>A costume set has no redraw. Its anchors come from a rule fitted on the kids: for each slot and direction, the hand's height as a fraction of the silhouette's height above its bottom edge and its distance
/// from the box's centre as a fraction of half the box's width, averaged over the kids' frames. <see cref="FitRule"/>, <see cref="Apply"/></item>
/// </list>
/// The fit's error is measured by leaving each kid set out in turn and predicting its transferred anchors from the other seven (<see cref="LeaveOneSetOut"/>).
/// </summary>
public static class HandAnchorMeasure
{
	/// <summary>The animation slots the umbrella follows (standing, walking, waiting in a queue) and the names the table gives them.</summary>
	public static readonly IReadOnlyList<(int Slot, string Name)> Slots = new[] { (0, "stand"), (1, "walk"), (14, "wait") };
	/// <summary>The directions of a land slot (the rest are mirrors).</summary>
	public const int Directions = 5;
	/// <summary>The most a silhouette registration may move an anchor sideways, in pixels: a redraw's mean x differs by up to 2.6 pixels from the original's, more means the frames are not the same pose.</summary>
	public const double MaximumShift = 4;

	/// <summary>A hand point, in pixels from the hotspot.</summary>
	public readonly record struct Point( double X, double Y );

	/// <summary>The hand's place in a silhouette: how far above its bottom edge (as a share of its height) and how far from its box's centre (as a share of half its width, to the right).</summary>
	public readonly record struct Ratios( double Height, double Side );

	/// <summary>The anchor of an original frame from the 2x redraw's anchor: half the pixels, shifted sideways to the original's silhouette.</summary>
	public static Point Transfer( FrameSilhouette original, FrameSilhouette redraw, Point redrawAnchor, double scale = 2 )
	{
		var shift = Math.Clamp( original.CentroidX - redraw.CentroidX / scale, -MaximumShift, MaximumShift );
		return new Point( redrawAnchor.X / scale + shift, redrawAnchor.Y / scale );
	}

	/// <summary>Where a hand is in a silhouette.</summary>
	public static Ratios RatiosOf( FrameSilhouette silhouette, Point hand ) =>
		new( (silhouette.Bottom - hand.Y) / Math.Max( 1, silhouette.Height ), (hand.X - silhouette.CenterX) / Math.Max( 1, silhouette.Width / 2 ) );

	/// <summary>The hand point a rule puts in a silhouette.</summary>
	public static Point Apply( Ratios ratios, FrameSilhouette silhouette ) =>
		new( silhouette.CenterX + ratios.Side * silhouette.Width / 2, silhouette.Bottom - ratios.Height * silhouette.Height );

	/// <summary>One measured frame of the fit: its slot and direction, its silhouette and where its hand is.</summary>
	public readonly record struct Sample( int Slot, int Direction, FrameSilhouette Silhouette, Point Hand );

	/// <summary>The rule: the mean <see cref="Ratios"/> of every slot and direction of the samples (a combination without samples is not in it).</summary>
	public static Dictionary<(int Slot, int Direction), Ratios> FitRule( IEnumerable<Sample> samples ) =>
		samples.GroupBy( sample => (sample.Slot, sample.Direction ) ).ToDictionary( group => group.Key, group =>
		{
			var all = group.Select( sample => RatiosOf( sample.Silhouette, sample.Hand ) ).ToArray();
			return new Ratios( all.Average( ratio => ratio.Height ), all.Average( ratio => ratio.Side ) );
		} );

	/// <summary>
	/// The leave-one-set-out error of the rule, in pixels: for each set in turn the rule is fitted on the other sets and applied to the left-out set's silhouettes, and the distance to its own hands is taken.
	/// Returns the mean and the largest distance over every frame of every set.
	/// </summary>
	public static (double Mean, double Max, int Frames) LeaveOneSetOut( IReadOnlyList<IReadOnlyList<Sample>> sets )
	{
		double sum = 0, max = 0;
		var frames = 0;
		for ( var left = 0; left < sets.Count; left++ )
		{
			var rule = FitRule( sets.Where( ( _, index ) => index != left ).SelectMany( set => set ) );
			foreach ( var sample in sets[left] )
			{
				if ( !rule.TryGetValue( (sample.Slot, sample.Direction), out var ratios ) )
					continue;
				var predicted = Apply( ratios, sample.Silhouette );
				var distance = Math.Sqrt( Math.Pow( predicted.X - sample.Hand.X, 2 ) + Math.Pow( predicted.Y - sample.Hand.Y, 2 ) );
				sum += distance;
				max = Math.Max( max, distance );
				frames++;
			}
		}
		return (frames == 0 ? 0 : sum / frames, max, frames);
	}

	/// <summary>The table of one set: per slot name, the slot and every direction's list of hand points, one per step.</summary>
	public sealed record SetTable( string Name, string Source, IReadOnlyDictionary<int, Point[][]> Frames );

	private static string Number( double value ) => Math.Round( value, 1 ).ToString( "0.0", CultureInfo.InvariantCulture );

	/// <summary>Writes the anchors file: the method, the fitted rule with its error, and every set's table. Numbers only.</summary>
	public static byte[] Write( IReadOnlyList<SetTable> tables, Dictionary<(int Slot, int Direction), Ratios> rule, (double Mean, double Max, int Frames) error, int kidSets )
	{
		var text = new StringBuilder();
		text.Append( "{\n \"schema\": 1,\n \"generator\": \"opentpw-kit measure-anchors\",\n \"edition\": \"TPW\",\n \"pixelsPerUnit\": 128,\n \"directions\": 5,\n \"hand\": \"right\",\n" );
		text.Append( " \"units\": \"pixels at 128 per unit from the feet hotspot of the game's own frames, x right and y down; directions 5 to 7 mirror 3 to 1 with x negated; one point per step\",\n" );
		text.Append( " \"method\": \"kid sets: the pools 2x anchor at half the pixels, moved sideways by the difference of the silhouettes' mean x (source transfer); costume sets: the fitted rule (source rule), the hand's height as a share of the silhouette's height above its bottom edge and its x from the box's centre as a share of half its width; numbers only, no pixels\",\n" );
		text.Append( " \"rule\": { \"fittedOnSets\": " ).Append( kidSets.ToString( CultureInfo.InvariantCulture ) ).Append( ", \"leaveOneSetOut\": { \"frames\": " ).Append( error.Frames.ToString( CultureInfo.InvariantCulture ) )
			.Append( ", \"meanPixels\": " ).Append( Math.Round( error.Mean, 2 ).ToString( CultureInfo.InvariantCulture ) ).Append( ", \"maxPixels\": " ).Append( Math.Round( error.Max, 2 ).ToString( CultureInfo.InvariantCulture ) ).Append( " }, \"ratios\": {\n" );
		var slotLines = new List<string>();
		foreach ( var (slot, name) in Slots )
		{
			var row = Enumerable.Range( 0, Directions ).Select( direction => rule.TryGetValue( (slot, direction), out var ratios )
				? $"[{ratios.Height.ToString( "0.000", CultureInfo.InvariantCulture )}, {ratios.Side.ToString( "0.000", CultureInfo.InvariantCulture )}]" : "null" );
			slotLines.Add( $"  \"{name}\": [{string.Join( ", ", row )}]" );
		}
		text.Append( string.Join( ",\n", slotLines ) ).Append( "\n } },\n \"sets\": {\n" );
		var setTexts = new List<string>();
		foreach ( var table in tables )
		{
			var slotTexts = new List<string>();
			foreach ( var (slot, name) in Slots )
			{
				if ( !table.Frames.TryGetValue( slot, out var directions ) )
					continue;
				var directionTexts = directions.Select( steps => "[" + string.Join( ",", steps.Select( point => $"[{Number( point.X )},{Number( point.Y )}]" ) ) + "]" );
				slotTexts.Add( $"   \"{name}\": {{ \"slot\": {slot}, \"frames\": [{string.Join( ",", directionTexts )}] }}" );
			}
			setTexts.Add( $"  \"{table.Name}\": {{\n   \"source\": \"{table.Source}\",\n{string.Join( ",\n", slotTexts )}\n  }}" );
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
/// <c>measure-anchors --data &lt;game Data folder&gt; --anchors &lt;anchors.json&gt; --pools &lt;folder of the 2x kid sets&gt; --out &lt;file&gt; [--force]</c>: reads the player's own sprite sets (kids and the four theme costumes from
/// <c>esprites.wad</c>) and writes the hand anchors of the original sets as numbers only (<see cref="HandAnchorMeasure"/>). A maintainers' tool for the umbrellas extension; the table it writes is committed, so
/// players never run it.
/// </summary>
public static class MeasureAnchorsCommand
{
	private static readonly string[] KidNames = { "BE", "BI", "CH", "FR", "KI", "SA", "SU", "TA" };
	private static readonly (string Theme, string Name)[] CostumeSets = { ("Jungle", "SPR_TI"), ("Fantasy", "SPR_BK"), ("Hallow", "SPR_HK"), ("Space", "SPR_SK") };

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

		var kidSamples = new List<IReadOnlyList<Sample>>();
		var tables = new List<HandAnchorMeasure.SetTable>();
		foreach ( var kid in KidNames )
		{
			var (animations, bank) = Open( wad, $"Generic/Kids/SPR_{kid}" );
			using var animationStream = File.OpenRead( Path.Combine( pools, $"SPR_{kid}.ESP" ) );
			var redrawAnimations = new SpriteAnimationFile( animationStream );
			using var bankStream = File.OpenRead( Path.Combine( pools, $"SPR_{kid}.TPC" ) );
			var redrawBank = new SpriteBankFile( bankStream );
			var set = anchors.RootElement.GetProperty( "sets" ).GetProperty( kid );
			var frames = new Dictionary<int, HandAnchorMeasure.Point[][]>();
			var samples = new List<Sample>();
			foreach ( var (slot, name) in HandAnchorMeasure.Slots )
			{
				var original = animations.Animations[slot];
				var redraw = redrawAnimations.Animations[slot];
				if ( original.IsEmpty || original.Directions != HandAnchorMeasure.Directions || redraw.FramesPerDirection != original.FramesPerDirection )
					throw new InvalidDataException( $"SPR_{kid} slot {slot} does not line up with its 2x redraw." );
				var table = set.GetProperty( name ).GetProperty( "frames" );
				var perDirection = new HandAnchorMeasure.Point[HandAnchorMeasure.Directions][];
				for ( var direction = 0; direction < HandAnchorMeasure.Directions; direction++ )
				{
					perDirection[direction] = new HandAnchorMeasure.Point[original.FramesPerDirection];
					for ( var step = 0; step < original.FramesPerDirection; step++ )
					{
						var point = table[direction][step];
						var redrawHand = new HandAnchorMeasure.Point( point[0].GetDouble(), point[1].GetDouble() );
						var originalOutline = FrameSilhouette.Of( bank.Frames[original.GetFrame( direction, step )] );
						var redrawOutline = FrameSilhouette.Of( redrawBank.Frames[redraw.GetFrame( direction, step )] );
						if ( originalOutline == null || redrawOutline == null )
							throw new InvalidDataException( $"SPR_{kid} slot {slot} direction {direction} step {step} has an empty frame." );
						var hand = HandAnchorMeasure.Transfer( originalOutline.Value, redrawOutline.Value, redrawHand );
						perDirection[direction][step] = hand;
						samples.Add( new Sample( slot, direction, originalOutline.Value, hand ) );
					}
				}
				frames[slot] = perDirection;
			}
			kidSamples.Add( samples );
			tables.Add( new HandAnchorMeasure.SetTable( $"SPR_{kid}", "transfer", frames ) );
		}
		var rule = HandAnchorMeasure.FitRule( kidSamples.SelectMany( set => set ) );
		var error = HandAnchorMeasure.LeaveOneSetOut( kidSamples );

		foreach ( var (theme, name) in CostumeSets )
		{
			var (animations, bank) = Open( wad, $"{theme}/Costumes/{name}" );
			var frames = new Dictionary<int, HandAnchorMeasure.Point[][]>();
			foreach ( var (slot, _) in HandAnchorMeasure.Slots )
			{
				var animation = animations.Animations[slot];
				if ( animation.IsEmpty )
				{
					warnings.Add( $"{name} has no slot {slot}; the umbrella follows its standing slot there." );
					continue;
				}
				if ( animation.Directions != HandAnchorMeasure.Directions )
					throw new InvalidDataException( $"{name} slot {slot} has {animation.Directions} directions, not {HandAnchorMeasure.Directions}." );
				var perDirection = new HandAnchorMeasure.Point[HandAnchorMeasure.Directions][];
				for ( var direction = 0; direction < HandAnchorMeasure.Directions; direction++ )
				{
					perDirection[direction] = new HandAnchorMeasure.Point[animation.FramesPerDirection];
					for ( var step = 0; step < animation.FramesPerDirection; step++ )
					{
						var outline = FrameSilhouette.Of( bank.Frames[animation.GetFrame( direction, step )] ) ?? throw new InvalidDataException( $"{name} slot {slot} direction {direction} step {step} has an empty frame." );
						perDirection[direction][step] = HandAnchorMeasure.Apply( rule[(slot, direction)], outline );
					}
				}
				frames[slot] = perDirection;
			}
			tables.Add( new HandAnchorMeasure.SetTable( name, "rule", frames ) );
		}
		var bytes = HandAnchorMeasure.Write( tables, rule, error, KidNames.Length );
		Directory.CreateDirectory( Path.GetDirectoryName( Path.GetFullPath( outPath ) )! );
		File.WriteAllBytes( outPath, bytes );
		warnings.Add( $"the rule's leave-one-set-out error over {error.Frames} frames: mean {error.Mean:0.00} px, largest {error.Max:0.00} px." );
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
