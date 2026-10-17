using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using StbImageSharp;

namespace OpenTPW.ContentKit;

/// <summary>
/// Packs a folder of RGBA frames and a <c>set.json</c> into one sprite set (<c>.ESP</c> table plus <c>.TPC</c> and, at full detail, <c>.FPC</c> bank) with <see cref="SpriteSetBuilder"/>:
/// one shared palette, the slots in their own numbers, the origins from the file. Used for complete guest sprite sets (kids, heads, costumes, ...) and any other set the game's sprite readers load.
/// <para>
/// The folder holds <c>slot&lt;NN&gt;_d&lt;direction&gt;_f&lt;frame&gt;.png</c> (NN the animation slot, 0 to 15; direction and frame count from 0) and <c>set.json</c>:
/// </para>
/// <code>
/// { "name": "SPR_BE", "detailFlag": 1, "kidFlag": 0,
///   "slots": { "0": { "directions": 5, "framesPerDirection": 1 }, "1": { "directions": 5, "framesPerDirection": 8 } },
///   "defaultOrigin": { "originX": -24, "originY": -52 },
///   "frames": { "slot01_d0_f0": { "originX": -25, "originY": -53 } },
///   "performances": [ { "program": 0, "slot": 4, "intro": 0, "hold": 0 } ] }
/// </code>
/// <c>pixelsPerUnit</c> (default 128, 1 to 65535) is the pixels-per-unit field written to every frame (<see cref="SpriteFrame.PixelsPerUnitX"/>/<see cref="SpriteFrame.PixelsPerUnitY"/>): the quad of a frame spans
/// <c>width / pixelsPerUnit</c> units, so a set drawn at twice the pixel size gets 256 and appears at the same world size with twice the detail (docs/CONTENT-KIT.md, "Resolution").
/// <c>detailFlag</c> is ESP byte 0x10C (1: <c>.FPC</c> and <c>.TPC</c> are written, 0: only the <c>.TPC</c>; default 1), <c>kidFlag</c> byte 0x10D (default 0). A slot with
/// <c>directions</c> 0 has no directions (balloons, thoughts: files <c>d0</c> only). <c>originX</c>/<c>originY</c> are the offset of the frame's top-left corner from the hotspot (the
/// reader's <see cref="SpriteFrame.OriginX"/>/<see cref="SpriteFrame.OriginY"/>); <c>defaultOrigin</c> serves every frame without an entry.
/// </summary>
public static class SpriteSetFolder
{
	public const string DescriptionFileName = "set.json";
	private static readonly Regex FramePattern = new( @"\Aslot(\d{1,2})_d(\d{1,3})_f(\d{1,3})\.png\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );
	private static readonly string[] SetFields = { "name", "detailFlag", "kidFlag", "pixelsPerUnit", "slots", "frames", "defaultOrigin", "performances" };

	/// <summary>The folders of <c>esprites.wad</c> (below <c>esprites/</c> in the data), with the casing the game's own archive uses.</summary>
	public static readonly IReadOnlyList<string> KnownFolders = new[]
	{
		"Generic/Kids", "Generic/Kidsheads", "Generic/Balloons", "Generic/Thoughts", "Generic/Mechanics", "Generic/Handymen", "Generic/Guards", "Generic/Researchers", "Generic/Litter",
		"Generic/Particles", "Generic/SpecialFX",
		"Jungle/Costumes", "Hallow/Costumes", "Space/Costumes", "Fantasy/Costumes", "Jungle/Costumeheads", "Space/Costumeheads",
		"Jungle/Entertainers", "Hallow/Entertainers", "Space/Entertainers", "Fantasy/Entertainers"
	};

	/// <summary>The built set and the notes made while reading the folder.</summary>
	public sealed record Packed( SpriteSet Set, IReadOnlyList<string> Warnings );

	public static Packed Pack( string folder, string? nameOverride = null )
	{
		if ( !Directory.Exists( folder ) )
			throw new DirectoryNotFoundException( $"The set folder '{folder}' does not exist." );
		var descriptionPath = Path.Combine( folder, DescriptionFileName );
		if ( !File.Exists( descriptionPath ) )
			throw new FileNotFoundException( $"The set folder '{folder}' has no {DescriptionFileName}." );
		using var document = JsonDocument.Parse( File.ReadAllBytes( descriptionPath ), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow } );
		var root = document.RootElement;
		if ( root.ValueKind != JsonValueKind.Object )
			throw new InvalidDataException( $"{descriptionPath} must be a JSON object." );
		foreach ( var property in root.EnumerateObject() )
		{
			if ( !SetFields.Contains( property.Name ) )
				throw new InvalidDataException( $"{DescriptionFileName}: unknown field '{property.Name}'." );
		}

		var warnings = new List<string>();
		var name = root.TryGetProperty( "name", out var nameElement ) ? nameElement.GetString() : null;
		if ( nameOverride != null && name != null && !string.Equals( nameOverride, name, StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidDataException( $"--name {nameOverride} does not match the name {name} in {DescriptionFileName}." );
		name ??= nameOverride ?? throw new InvalidDataException( $"{DescriptionFileName} has no \"name\" (and --name was not given)." );
		var builder = new SpriteSetBuilder( name )
		{
			FullDetail = Flag( root, "detailFlag", true ),
			KidFlag = (byte)(Flag( root, "kidFlag", false ) ? 1 : 0)
		};
		var pixelsPerUnit = 128;
		if ( root.TryGetProperty( "pixelsPerUnit", out var unitElement ) && (unitElement.ValueKind != JsonValueKind.Number || !unitElement.TryGetInt32( out pixelsPerUnit ) || pixelsPerUnit is < 1 or > ushort.MaxValue) )
			throw new InvalidDataException( $"{DescriptionFileName}: pixelsPerUnit must be a whole number from 1 to {ushort.MaxValue}." );

		(int X, int Y)? defaultOrigin = root.TryGetProperty( "defaultOrigin", out var fallback ) ? Origin( fallback, "defaultOrigin" ) : null;
		var origins = new Dictionary<string, (int X, int Y)>( StringComparer.OrdinalIgnoreCase );
		if ( root.TryGetProperty( "frames", out var frames ) )
		{
			foreach ( var property in frames.EnumerateObject() )
			{
				var key = property.Name.EndsWith( ".png", StringComparison.OrdinalIgnoreCase ) ? property.Name[..^4] : property.Name;
				origins[key] = Origin( property.Value, $"frames.{property.Name}" );
			}
		}

		var files = new Dictionary<(int Slot, int Direction, int Frame), string>();
		foreach ( var file in Directory.EnumerateFiles( folder, "*.png" ) )
		{
			var match = FramePattern.Match( Path.GetFileName( file ) );
			if ( !match.Success )
			{
				warnings.Add( $"{Path.GetFileName( file )} is not named slot<NN>_d<direction>_f<frame>.png and is ignored." );
				continue;
			}
			var key = (int.Parse( match.Groups[1].Value, CultureInfo.InvariantCulture ), int.Parse( match.Groups[2].Value, CultureInfo.InvariantCulture ), int.Parse( match.Groups[3].Value, CultureInfo.InvariantCulture ));
			if ( !files.TryAdd( key, file ) )
				throw new InvalidDataException( $"Slot {key.Item1} direction {key.Item2} frame {key.Item3} exists twice ({Path.GetFileName( files[key] )} and {Path.GetFileName( file )})." );
		}

		var slotsElement = root.TryGetProperty( "slots", out var slots ) && slots.ValueKind == JsonValueKind.Object ? slots : throw new InvalidDataException( $"{DescriptionFileName} needs a \"slots\" object." );
		var declared = new SortedDictionary<int, (int Directions, int FramesPerDirection)>();
		foreach ( var slotProperty in slotsElement.EnumerateObject() )
		{
			if ( !int.TryParse( slotProperty.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var slot ) || slot >= SpriteAnimationFile.SlotCount )
				throw new InvalidDataException( $"Slot '{slotProperty.Name}' must be a number from 0 to {SpriteAnimationFile.SlotCount - 1}." );
			foreach ( var property in slotProperty.Value.EnumerateObject() )
			{
				if ( property.Name is not ("directions" or "framesPerDirection") )
					throw new InvalidDataException( $"Slot {slot}: unknown field '{property.Name}'." );
			}
			declared[slot] = (Integer( slotProperty.Value, "directions", slot ), Integer( slotProperty.Value, "framesPerDirection", slot ));
			if ( declared[slot].FramesPerDirection < 1 )
				throw new InvalidDataException( $"Slot {slot}: framesPerDirection must be at least 1." );
		}
		if ( declared.Count == 0 )
			throw new InvalidDataException( $"{DescriptionFileName} declares no slots." );
		foreach ( var key in files.Keys.Where( key => !declared.ContainsKey( key.Slot ) ) )
			throw new InvalidDataException( $"{Path.GetFileName( files[key] )} belongs to slot {key.Slot}, which {DescriptionFileName} does not declare." );

		foreach ( var (slot, (directionCount, perDirection)) in declared )
		{
			var directional = directionCount > 0;
			var directionList = new List<IReadOnlyList<SpriteRgbaFrame>>();
			for ( var direction = 0; direction < Math.Max( directionCount, 1 ); direction++ )
			{
				var list = new List<SpriteRgbaFrame>();
				for ( var frame = 0; frame < perDirection; frame++ )
				{
					var stem = $"slot{slot:00}_d{direction}_f{frame}";
					if ( !files.TryGetValue( (slot, direction, frame), out var file ) )
						throw new InvalidDataException( $"Slot {slot} needs {stem}.png ({(directional ? directionCount : 1)} direction(s) of {perDirection} frame(s)); it is missing." );
					var origin = origins.TryGetValue( stem, out var own ) ? own : defaultOrigin ?? throw new InvalidDataException( $"{stem} has no origin in {DescriptionFileName} and there is no defaultOrigin." );
					list.Add( Decode( file, origin ) with { PixelsPerUnit = (ushort)pixelsPerUnit } );
				}
				directionList.Add( list );
			}
			foreach ( var key in files.Keys.Where( key => key.Slot == slot && (key.Direction >= Math.Max( directionCount, 1 ) || key.Frame >= perDirection) ) )
				throw new InvalidDataException( $"{Path.GetFileName( files[key] )} lies outside slot {slot}'s {Math.Max( directionCount, 1 )} direction(s) of {perDirection} frame(s)." );
			builder.AddAnimation( slot, directionList, directional );
		}
		var unused = origins.Keys.FirstOrDefault( key => !files.Keys.Any( file => $"slot{file.Slot:00}_d{file.Direction}_f{file.Frame}".Equals( key, StringComparison.OrdinalIgnoreCase ) ) );
		if ( unused != null )
			warnings.Add( $"{DescriptionFileName} gives an origin for {unused}, which is not a frame of the set." );

		if ( root.TryGetProperty( "performances", out var performances ) )
		{
			var entry = 0;
			foreach ( var item in performances.EnumerateArray() )
			{
				foreach ( var property in item.EnumerateObject() )
				{
					if ( property.Name is not ("program" or "slot" or "intro" or "hold") )
						throw new InvalidDataException( $"Performance {entry}: unknown field '{property.Name}'." );
				}
				builder.SetPerformance( entry++, Integer( item, "program", entry ), Integer( item, "slot", entry ), item.TryGetProperty( "intro", out _ ) ? Integer( item, "intro", entry ) : 0, item.TryGetProperty( "hold", out _ ) ? Integer( item, "hold", entry ) : 0 );
			}
		}
		return new Packed( builder.Build(), warnings );
	}

	private static bool Flag( JsonElement root, string field, bool fallback )
	{
		if ( !root.TryGetProperty( field, out var value ) )
			return fallback;
		return value.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Number when value.TryGetInt32( out var number ) && number is 0 or 1 => number == 1,
			_ => throw new InvalidDataException( $"{DescriptionFileName}: {field} must be 0, 1, true or false." )
		};
	}

	private static int Integer( JsonElement element, string field, int context )
	{
		if ( !element.TryGetProperty( field, out var value ) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32( out var number ) )
			throw new InvalidDataException( $"{DescriptionFileName}: '{field}' of slot or entry {context} must be a whole number." );
		return number;
	}

	private static (int X, int Y) Origin( JsonElement element, string what )
	{
		foreach ( var property in element.EnumerateObject() )
		{
			if ( property.Name is not ("originX" or "originY") )
				throw new InvalidDataException( $"{DescriptionFileName}: {what} has the unknown field '{property.Name}'." );
		}
		if ( !element.TryGetProperty( "originX", out var x ) || !element.TryGetProperty( "originY", out var y ) || !x.TryGetInt32( out var originX ) || !y.TryGetInt32( out var originY ) )
			throw new InvalidDataException( $"{DescriptionFileName}: {what} needs whole-number originX and originY." );
		return (originX, originY);
	}

	private static SpriteRgbaFrame Decode( string file, (int X, int Y) origin )
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
		return new SpriteRgbaFrame( image.Width, image.Height, image.Data, origin.X, origin.Y );
	}

	/// <summary>The canonical casing of a folder below <c>esprites/</c> when it is one of <see cref="KnownFolders"/>, else the path as given (after a safety check) and a flag.</summary>
	public static (string Folder, bool Known) ResolveFolder( string folder )
	{
		var normalised = folder.Replace( '\\', '/' ).Trim( '/' );
		if ( normalised.Length == 0 || normalised.Contains( ':' ) || normalised.Split( '/' ).Any( segment => segment.Length == 0 || segment is "." or ".." || segment.EndsWith( '.' ) || segment.EndsWith( ' ' ) ) )
			throw new ArgumentException( $"The folder '{folder}' must be a relative path below esprites/ without '..' or empty segments." );
		var known = KnownFolders.FirstOrDefault( candidate => string.Equals( candidate, normalised, StringComparison.OrdinalIgnoreCase ) );
		return (known ?? normalised, known != null);
	}
}

public static class SpritesCommand
{
	public static CommandResult Run( CommandOptions options )
	{
		var folders = options.Many( "frames" );
		if ( folders.Count == 0 )
			throw new UsageException( "The option --frames is required (a set folder; it may be given several times)." );
		var name = options.Optional( "name" );
		if ( name != null && folders.Count > 1 )
			throw new UsageException( "--name only applies to a single --frames folder; the other sets take their names from set.json." );
		var outDirectory = options.Optional( "out" );
		var modDirectory = options.Optional( "mod" );
		var spriteFolder = options.Optional( "folder" );
		var overrides = options.Optional( "overrides" ) ?? "data";
		var force = options.Has( "force" );
		options.RejectUnknown();
		if ( (outDirectory == null) == (modDirectory == null) )
			throw new UsageException( "Give --out <dir> (the files are written there) or --mod <dir> --folder <esprites folder> (they go into the mod's overrides tree)." );
		if ( modDirectory != null && spriteFolder == null )
			throw new UsageException( "--mod needs --folder, the folder below esprites/ the game reads the set from (for example Generic/Kids)." );
		if ( modDirectory == null && spriteFolder != null )
			throw new UsageException( "--folder only goes with --mod." );

		var files = new List<(string Path, byte[] Bytes)>();
		var warnings = new List<string>();
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		string? prefix = null;
		string root = outDirectory ?? modDirectory!;
		if ( modDirectory != null )
		{
			var (folder, known) = SpriteSetFolder.ResolveFolder( spriteFolder! );
			if ( !known )
				warnings.Add( $"'{folder}' is not a folder of the game's esprites.wad; its sprite readers will not look there." );
			var overridesPath = ModBuilder.NormalizeOverridesPath( overrides );
			prefix = $"{overridesPath}/esprites/{folder}/";
			CheckManifest( modDirectory, overridesPath, warnings );
		}
		foreach ( var folder in folders )
		{
			var packed = SpriteSetFolder.Pack( folder, folders.Count == 1 ? name : null );
			warnings.AddRange( packed.Warnings );
			if ( !seen.Add( packed.Set.Name ) )
				throw new InvalidDataException( $"The set {packed.Set.Name} is given twice." );
			foreach ( var (extension, bytes) in packed.Set.Files.OrderBy( pair => pair.Key, StringComparer.Ordinal ) )
				files.Add( ($"{prefix}{packed.Set.Name}{extension}", bytes) );
		}
		var written = new List<string>();
		Output.Write( root, files, force, written );
		return new CommandResult( root, written, warnings );
	}

	/// <summary>Warns when the mod's manifest does not name the overrides folder the files went into (the game would never read them).</summary>
	private static void CheckManifest( string modDirectory, string overridesPath, List<string> warnings )
	{
		var path = Path.Combine( modDirectory, ModBuilder.ManifestFileName );
		if ( !File.Exists( path ) )
		{
			warnings.Add( $"{modDirectory} has no {ModBuilder.ManifestFileName} yet; create it with 'mod --overrides {overridesPath}/'." );
			return;
		}
		using var document = JsonDocument.Parse( File.ReadAllBytes( path ) );
		var named = document.RootElement.TryGetProperty( "content", out var content ) && content.TryGetProperty( "overrides", out var value ) ? value.GetString() : null;
		if ( named == null || !string.Equals( ModBuilder.NormalizeOverridesPath( named ), overridesPath, StringComparison.OrdinalIgnoreCase ) )
			warnings.Add( $"The manifest's content.overrides is {(named == null ? "not set" : $"'{named}'")}, not '{overridesPath}/': the game will not read these files until it is." );
	}
}
