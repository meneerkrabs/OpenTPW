namespace OpenTPW.ContentKit;

/// <summary>A command line the kit cannot use (an unknown option, a missing value). Reported with the usage text.</summary>
public sealed class UsageException : Exception
{
	public UsageException( string message ) : base( message )
	{
	}
}

/// <summary>The parsed <c>--option value</c> pairs of one command; options may repeat. <c>--force</c> is the only flag without a value.</summary>
public sealed class CommandOptions
{
	private readonly Dictionary<string, List<string>> values = new( StringComparer.Ordinal );
	private readonly HashSet<string> read = new( StringComparer.Ordinal );

	public CommandOptions( IReadOnlyList<string> args )
	{
		for ( var index = 0; index < args.Count; index++ )
		{
			var option = args[index];
			if ( !option.StartsWith( "--", StringComparison.Ordinal ) || option.Length < 3 )
				throw new UsageException( $"Unexpected argument '{option}'; options look like --name value." );
			var key = option[2..];
			if ( key == "force" )
			{
				values[key] = new List<string> { "true" };
				continue;
			}
			if ( index + 1 >= args.Count )
				throw new UsageException( $"The option {option} needs a value." );
			if ( !values.TryGetValue( key, out var list ) )
				values[key] = list = new List<string>();
			list.Add( args[++index] );
		}
	}

	public string Require( string key )
	{
		read.Add( key );
		return values.TryGetValue( key, out var list ) ? list[^1] : throw new UsageException( $"The option --{key} is required." );
	}

	public string? Optional( string key )
	{
		read.Add( key );
		return values.TryGetValue( key, out var list ) ? list[^1] : null;
	}

	public IReadOnlyList<string> Many( string key )
	{
		read.Add( key );
		return values.TryGetValue( key, out var list ) ? list : Array.Empty<string>();
	}

	public bool Has( string key )
	{
		read.Add( key );
		return values.ContainsKey( key );
	}

	/// <summary>Refuses options the command never asked for (a typo would otherwise be silently ignored).</summary>
	public void RejectUnknown()
	{
		var unknown = values.Keys.Where( key => !read.Contains( key ) ).ToArray();
		if ( unknown.Length > 0 )
			throw new UsageException( $"Unknown option --{unknown[0]}." );
	}
}

/// <summary>The content-kit command line (docs/CONTENT-KIT.md): <c>object</c>, <c>mod</c> and <c>sprites</c>.</summary>
public static class ContentKitCli
{
	public const string Usage = """
		opentpw-kit: turns original art into files the game loads (docs/CONTENT-KIT.md)

		  object  --glb <file> --name <archiveName> --out <dir> [--sam <file|template>] [--script <text .rse>]
		          [--display-name <text>] [--force]
		          Writes <name>.MD2, textures/*.tga, <Name>.sam and <Name>.RSE into <dir> (an object archive folder).
		          --sam is a .sam file or the word 'template' (default), the built-in sideshow-like settings.
		          --script is an RSE text source; without it the built-in placeholder pool script is assembled.

		  mod     --out <dir> (--manifest <file> | --id <id> --name <text> [--version 1.0.0] [--author <text>] [--description <text>])
		          [--overrides <folder>] [--objects <file> | --id-first <n> --id-last <n> --object <spec> [--object <spec> ...]] [--force]
		          Lays out a mod folder: opentpw-mod.json, objects.json (when there are objects) and, with src=<dir> in an object
		          spec, a copy of the built object folder; --overrides names the folder of replacement game files. A spec is
		          comma-separated key=value pairs:
		          archive=objects/pool,theme=jungle,category=rides,id=20001,name=Pool[,name.German=Becken][,src=<dir>]

		  sprites --frames <set folder> [--frames <set folder> ...] [--name SPR_XX]
		          (--out <dir> | --mod <mod dir> --folder <folder below esprites/, e.g. Generic/Kids> [--overrides data]) [--force]
		          Packs each set folder (slot<NN>_d<d>_f<f>.png frames and a set.json with the name, the detail and kid flags, the slots'
		          directions and frames and every frame's origin) into NAME.ESP, .TPC and, at full detail, .FPC. With --mod the files go to
		          <mod>/<overrides>/esprites/<folder>/, where the mod's content.overrides makes the game read them instead of its own.

		  measure-anchors --data <game Data folder> --anchors <anchors.json> --pools <folder of the 2x kid sets> --out <file> [--force]
		          A maintainers' tool of the umbrellas extension (docs/UMBRELLAS.md): reads the game's own kid and costume sprite sets and writes
		          the guests' hand anchors as numbers only. Players never need it; its table is committed.

		  measure-head-anchors --data <game Data folder> --anchors <winter anchors.json> --pools <folder of the 2x kid sets> --out <file> [--force]
		          A maintainers' tool of the winter extension (docs/WINTER.md): the same for the guests' head (a hat's and earmuffs' attach points), every land slot the
		          original sets share with the 2x redraws, as numbers only.
		""";

	public static int Run( string[] args, TextWriter output, TextWriter error )
	{
		try
		{
			if ( args.Length == 0 || args[0] is "-h" or "--help" or "help" )
			{
				output.WriteLine( Usage );
				return args.Length == 0 ? 2 : 0;
			}
			var options = new CommandOptions( args.Skip( 1 ).ToArray() );
			switch ( args[0] )
			{
				case "object":
					PrintResult( output, ObjectCommand.Run( options ) );
					return 0;
				case "mod":
					PrintResult( output, ModCommand.Run( options ) );
					return 0;
				case "sprites":
					PrintResult( output, SpritesCommand.Run( options ) );
					return 0;
				case "measure-head-anchors":
					PrintResult( output, MeasureHeadAnchorsCommand.Run( options ) );
					return 0;
				case "measure-anchors":
					PrintResult( output, MeasureAnchorsCommand.Run( options ) );
					return 0;
				default:
					throw new UsageException( $"Unknown command '{args[0]}'." );
			}
		}
		catch ( UsageException exception )
		{
			error.WriteLine( $"error: {exception.Message}" );
			error.WriteLine();
			error.WriteLine( Usage );
			return 2;
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or InvalidOperationException or IOException or ArgumentException or RideScriptAssemblyException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException or KeyNotFoundException )
		{
			error.WriteLine( $"error: {exception.Message}" );
			return 1;
		}
	}

	private static void PrintResult( TextWriter output, CommandResult result )
	{
		foreach ( var file in result.Files )
			output.WriteLine( $"wrote {file}" );
		foreach ( var warning in result.Warnings )
			output.WriteLine( $"warning: {warning}" );
	}
}

/// <summary>What a command wrote (paths below its output folder, '/' form) and what it noticed on the way.</summary>
public sealed record CommandResult( string Directory, IReadOnlyList<string> Files, IReadOnlyList<string> Warnings );
