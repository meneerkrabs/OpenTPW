using OpenTPW.FrontEnd;

namespace OpenTPW;

/// <summary>
/// The signs of the front-end lobby's island and gate models. A lobby model whose materials use <c>sign1</c>/<c>sign2</c> (shipped: <c>Jun_isle</c>, <c>Hal_isle</c>,
/// <c>Fan_gate</c> and <c>Spa_gate</c>) shows its theme's park gate lines, drawn with the model's own <c>&lt;model&gt;.sgn</c> beside it in <c>lobby/terrain</c>; the shipped
/// sign1/sign2 art is a magenta "SIGN1 SIGN2" placeholder. The composed halves are kept for the session (re-entering the lobby does not compose again); the scenes
/// make and release their own GPU textures from them.
/// </summary>
// [BIN:TPW-EXE:0x005E1A44 lobby island lines] the lobby builds each island (0x5E3210) with two lines that 0x409B10 fills for the island's theme: the first (0x413020) and second (0x4130B0) OBJECT_NAMES text of the theme's gate row, the lines a new park's gate starts with; no saved park name is read
// [BIN:TPW-EXE:0x005DFA60 island models] the island constructor passes the same two lines to both its island and its gate model (0x5D8870 -> 0x5D7FD0 -> 0x4629D0 arguments 8 and 9), which draws them as explicit lines, without the middle-space split (0x462EA6), through the sign generator 0x467A80
// [BIN:TPW-EXE:0x00467A80 sign generator] the sign replaces the model's sign1/sign2 textures and reads its .sgn as "%s\%s_%s.sgn", else "%s\%s.sgn" (the model's directory and name; strings 0x74D698, 0x74D68C)
// [DATA:lobby.wad:terrain] the .sgn files are Jun_isle.sgn, Hal_isle.sgn, Fan_gate.sgn and Spa_gate.sgn, exactly the four models with sign1/sign2 materials (no language variant)
public static class LobbySigns
{
	/// <summary>
	/// The gate whose OBJECT_NAMES row a theme's lobby island shows, as 0x409B10 picks it per theme. They are the levels' own gates (<c>global.sam</c>
	/// <c>ParkName.GateObjectId</c>); a theme outside the four has none.
	/// </summary>
	// [BIN:TPW-EXE:0x00409B10 theme gate lines] the theme name is compared with four names in turn and the lines come from info ids 0x11F9 (4601), 0xA29 (2601), 0x641 (1601) and 0xE11 (3601); the levels' global.sam give fantasy 4601, hallow 2601, jungle 1601 and space 3601
	public static int? GateInfoId( string level ) => level.ToLowerInvariant() switch
	{
		"fantasy" => 4601,
		"hallow" => 2601,
		"jungle" => 1601,
		"space" => 3601,
		_ => null
	};

	/// <summary>The OBJECT_NAMES texts in the current language, read once per lobby build for <see cref="Lines"/>; null when they cannot be read (or the edition is not Theme Park World).</summary>
	public static IReadOnlyList<string>? LoadObjectNames()
	{
		if ( GameEditions.Current != GameEdition.ThemeParkWorld )
			return null;
		try
		{
			return GameLanguage.Current.LoadStrings( ObjectNames.FileName ).Entries;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or InvalidOperationException )
		{
			Log?.Warning( $"Lobby signs: {ObjectNames.FileName} unavailable ({exception.Message}); the theme names stand in." );
			return null;
		}
	}

	/// <summary>
	/// The two lines a theme's lobby island shows: its gate's OBJECT_NAMES row in <paramref name="objectNames"/> (<see cref="ParkNameLines.Default"/>, the lines
	/// <see cref="OriginalGateSign.DefaultLines"/> gives a new park's gate), else the theme name.
	/// </summary>
	public static ParkNameLines Lines( LobbyIslandInfo island, IReadOnlyList<string>? objectNames ) =>
		GateInfoId( island.Level ) is { } gate && objectNames != null && ParkNameLines.Default( gate, objectNames ) is { } lines
			? lines
			: OriginalGateSign.ThemeNameLines( island.Level );

	private static readonly Dictionary<string, ObjectSignImages?> cache = new( StringComparer.Ordinal );
	private static readonly Dictionary<string, int> failedReads = new( StringComparer.Ordinal );

	private static string Key( string signPath, ParkNameLines lines ) => $"{signPath.ToLowerInvariant()}\0{lines.Line1}\0{lines.Line2}";

	/// <summary>Whether the sign of <paramref name="signPath"/> with <paramref name="lines"/> is known (composed or given up), so <see cref="Compose"/> costs nothing.</summary>
	public static bool TryGetCached( string signPath, ParkNameLines lines, out ObjectSignImages? images )
	{
		lock ( cache )
			return cache.TryGetValue( Key( signPath, lines ), out images );
	}

	/// <summary>
	/// Composes (or takes from the session cache) the sign of the <c>.sgn</c> at <paramref name="signPath"/> with <paramref name="lines"/>, or null when it cannot be drawn:
	/// the model then keeps the plain board. A failure that may pass (an I/O or access error) is tried again up to <see cref="ObjectSigns.ReadAttempts"/> times.
	/// </summary>
	public static ObjectSignImages? Compose( string signPath, ParkNameLines lines, BaseFileSystem? fileSystem = null )
	{
		fileSystem ??= FileSystem;
		var key = Key( signPath, lines );
		lock ( cache )
		{
			if ( cache.TryGetValue( key, out var cached ) )
				return cached;
			ObjectSignImages? images = null;
			Exception? failure = null;
			try
			{
				var sign = new SignFile( CompatibilityRuntime.Correct( signPath, fileSystem.ReadAllBytes( signPath ) ) );
				var composed = SignTextRenderer.Shared.ComposeSign( sign, new[] { lines.Line1, lines.Line2 }, default );
				foreach ( var diagnostic in composed.Diagnostics.Where( diagnostic => !diagnostic.StartsWith( "COMPAT-003", StringComparison.Ordinal ) ).Distinct() )
					Log?.Warning( $"Lobby sign {Path.GetFileName( signPath )}: {diagnostic}" );
				images = new ObjectSignImages( lines.Line1, lines.Line2, composed.Left, composed.Right, composed.TextPixelCount, composed.Diagnostics );
				Log?.Trace( $"Lobby sign {Path.GetFileName( signPath )}: \"{lines.DisplayName}\" ({composed.TextPixelCount} text pixels)." );
			}
			catch ( Exception exception ) when ( exception is not ArgumentNullException
				&& exception is IOException or InvalidDataException or NotSupportedException or ArgumentException or KeyNotFoundException or UnauthorizedAccessException )
			{
				failure = exception;
			}
			var failures = 0;
			if ( failure != null )
			{
				failedReads.TryGetValue( key, out failures );
				failedReads[key] = ++failures;
			}
			var final = ObjectSigns.CanCache( failure, images, archiveWithoutSign: true, failures );
			if ( failure != null && (failures == 1 || final) )
				Log?.Warning( $"Lobby sign {Path.GetFileName( signPath )} unavailable: {failure.Message}{(final ? "; the board stays plain" : "; it is tried again")}" );
			if ( final )
			{
				cache[key] = images;
				failedReads.Remove( key );
			}
			return images;
		}
	}
}
