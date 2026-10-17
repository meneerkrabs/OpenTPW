using System.Globalization;
using System.Text.RegularExpressions;
using StbImageSharp;

namespace OpenTPW.ContentKit;

/// <summary>
/// Wraps rendered swimmer frames (tools/blender/opentpw_swim_sprites.py, branch content/swim-sprites) into a sprite set with <see cref="SpriteSetBuilder"/>.
/// The frames are named <c>&lt;slot&gt;_d&lt;direction&gt;_f&lt;frame&gt;.png</c>: slot A is the front crawl (8 frames), B the breaststroke (8) and C treading water (4), five directions
/// as in the original kid sprites (0 away from the camera, 2 screen right, 4 towards the camera; the game mirrors 5 to 7 itself).
/// </summary>
public static class SpriteBuilderCommand
{
	/// <summary>
	/// The animation slot each frame letter is written to unless <c>--slot</c> says otherwise. The original kid sets (docs/GUESTS.md, "Kid slots") have 8 x 5 frame slots at 1 (walking) and
	/// 2 (running) and 4 x 5 slots at 4, 6, 11 and 12: the crawl takes the walking slot, the breaststroke the running slot and treading water the first 4-frame slot (4, a
	/// short gesture). This is the kit's own choice: nothing in the game plays these slots as swimming, an extension picks them up by number.
	/// </summary>
	public static readonly IReadOnlyDictionary<char, int> DefaultSlots = new Dictionary<char, int> { ['A'] = 1, ['B'] = 2, ['C'] = 4 };

	/// <summary>The hotspot (pixel inside the frame that is the sprite's position) of the two canvases the swim script renders: 64 x 48 for the Theme Park World sets, 80 x 64 for Theme Park Inc.</summary>
	public static readonly IReadOnlyDictionary<(int Width, int Height), (int X, int Y)> DefaultHotspots = new Dictionary<(int, int), (int, int)> { [(64, 48)] = (32, 27), [(80, 64)] = (40, 36) };

	private static readonly Regex FramePattern = new( @"^([A-Za-z])_d(\d+)_f(\d+)\.png$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );

	public static CommandResult Build( string framesDirectory, string name, string outDirectory, (int X, int Y)? hotspot, IReadOnlyDictionary<char, int>? slotMap, bool force )
	{
		if ( !Directory.Exists( framesDirectory ) )
			throw new DirectoryNotFoundException( $"The frames folder '{framesDirectory}' does not exist." );
		var slots = new Dictionary<char, int>( DefaultSlots );
		foreach ( var (letter, slot) in slotMap ?? new Dictionary<char, int>() )
			slots[char.ToUpperInvariant( letter )] = slot;

		// slot letter -> direction -> frame -> file
		var found = new SortedDictionary<char, SortedDictionary<int, SortedDictionary<int, string>>>();
		foreach ( var file in Directory.EnumerateFiles( framesDirectory, "*.png" ) )
		{
			var match = FramePattern.Match( Path.GetFileName( file ) );
			if ( !match.Success )
				continue;
			var letter = char.ToUpperInvariant( match.Groups[1].Value[0] );
			var direction = int.Parse( match.Groups[2].Value, CultureInfo.InvariantCulture );
			var frame = int.Parse( match.Groups[3].Value, CultureInfo.InvariantCulture );
			if ( !found.TryGetValue( letter, out var directions ) )
				found[letter] = directions = new SortedDictionary<int, SortedDictionary<int, string>>();
			if ( !directions.TryGetValue( direction, out var frames ) )
				directions[direction] = frames = new SortedDictionary<int, string>();
			if ( !frames.TryAdd( frame, file ) )
				throw new InvalidDataException( $"Slot {letter} direction {direction} frame {frame} exists twice." );
		}
		if ( found.Count == 0 )
			throw new InvalidDataException( $"'{framesDirectory}' holds no frames named <slot>_d<direction>_f<frame>.png." );

		var builder = new SpriteSetBuilder( name );
		foreach ( var (letter, directions) in found )
		{
			if ( !slots.TryGetValue( letter, out var slot ) )
				throw new InvalidDataException( $"The frames of slot {letter} have no sprite slot; pass --slot {letter}=<0..15>." );
			if ( !directions.Keys.SequenceEqual( Enumerable.Range( 0, directions.Count ) ) )
				throw new InvalidDataException( $"Slot {letter} directions must run 0..{directions.Count - 1} without a gap; found {string.Join( ", ", directions.Keys )}." );
			var perDirection = directions[0].Count;
			foreach ( var (direction, frames) in directions )
			{
				if ( !frames.Keys.SequenceEqual( Enumerable.Range( 0, perDirection ) ) )
					throw new InvalidDataException( $"Slot {letter} direction {direction} must have frames 0..{perDirection - 1}; found {string.Join( ", ", frames.Keys )}." );
			}
			var decoded = directions.Values.Select( frames => (IReadOnlyList<SpriteRgbaFrame>)frames.Values.Select( file => Decode( file, hotspot ) ).ToList() ).ToList();
			builder.AddAnimation( slot, decoded );
		}
		var set = builder.Build();
		var files = set.Files.Select( pair => ($"{set.Name}{pair.Key}", pair.Value) ).ToList();
		var written = new List<string>();
		Output.Write( outDirectory, files, force, written );
		return new CommandResult( outDirectory, written, Array.Empty<string>() );
	}

	private static SpriteRgbaFrame Decode( string file, (int X, int Y)? hotspot )
	{
		ImageResult image;
		try
		{
			image = ImageResult.FromMemory( File.ReadAllBytes( file ), ColorComponents.RedGreenBlueAlpha );
		}
		catch ( Exception exception ) when ( exception is InvalidOperationException or IndexOutOfRangeException or ArgumentException )
		{
			throw new InvalidDataException( $"{Path.GetFileName( file )} cannot be decoded as PNG: {exception.Message}" );
		}
		(int X, int Y) point;
		if ( hotspot is { } given )
			point = given;
		else if ( !DefaultHotspots.TryGetValue( (image.Width, image.Height), out point ) )
			throw new InvalidDataException( $"{Path.GetFileName( file )} is {image.Width} x {image.Height}; pass --hotspot x,y (the pixel that is the sprite's position; the swim script's canvases are 64 x 48 and 80 x 64)." );
		// the frame's left/top edge relative to the hotspot (SpriteFrame.OriginX/OriginY)
		return new SpriteRgbaFrame( image.Width, image.Height, image.Data, -point.X, -point.Y );
	}
}

public static class SpritesCommand
{
	public static CommandResult Run( CommandOptions options )
	{
		(int, int)? hotspot = null;
		if ( options.Optional( "hotspot" ) is { } text )
		{
			var parts = text.Split( ',' );
			if ( parts.Length != 2 || !int.TryParse( parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x ) || !int.TryParse( parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y ) )
				throw new UsageException( "--hotspot is x,y in pixels." );
			hotspot = (x, y);
		}
		var slots = new Dictionary<char, int>();
		foreach ( var item in options.Many( "slot" ) )
		{
			var parts = item.Split( '=' );
			if ( parts.Length != 2 || parts[0].Length != 1 || !char.IsAsciiLetter( parts[0][0] ) || !int.TryParse( parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var slot ) )
				throw new UsageException( "--slot is <letter>=<slot number>, for example A=1." );
			slots[char.ToUpperInvariant( parts[0][0] )] = slot;
		}
		var result = SpriteBuilderCommand.Build( options.Require( "frames" ), options.Require( "name" ), options.Require( "out" ), hotspot, slots, options.Has( "force" ) );
		options.RejectUnknown();
		return result;
	}
}
