using System.Globalization;

namespace OpenTPW.FrontEnd;

/// <summary>A theme island of the original lobby (lobby.wad <c>&lt;theme&gt;.txt</c>).</summary>
public sealed record LobbyIslandInfo( int Index, string Level, string Directory, string IslandModel, string GateModel, string Name, float Angle, float Height,
	(byte R, byte G, byte B) SkyColour, IReadOnlyList<string> FlyingMeshes, bool Rainy, int Lightning )
{
	/// <summary>THEMENAMES.str entry of the island's theme (Lost Kingdom, Halloween World, Wonder Land, Space Zone).</summary>
	public int ThemeNameIndex => Level.ToLowerInvariant() switch
	{
		"jungle" => 0,
		"hallow" => 1,
		"fantasy" => 2,
		"space" => 3,
		_ => -1
	};
}

/// <summary>
/// The original front-end lobby as described by <c>lobby.wad</c>: <c>lobby.txt</c> holds the camera
/// (ISLANDFOV, SPINSPEED, SPINRADIUS, VERTICALOFFSET) and island positions (ISLANDCAMERAPOSITION
/// index, x, z), and one file per theme holds ISLAND(index, directory, island model, gate model,
/// name, angle, height), FLYINGMESH, SKYCOLOUR, RAINY and LIGHTNING. The meaning of the angle (island
/// yaw in degrees) and height (camera target height) is inferred, not proven (docs/UI.md).
/// </summary>
public sealed class LobbyDefinition
{
	public static readonly string[] ThemeFiles = { "jungle", "fantasy", "hallow", "space" };

	public float FieldOfView { get; private set; } = 100;
	public float SpinSpeed { get; private set; } = 0.02f;
	public float SpinRadius { get; private set; } = 70;
	public float VerticalOffset { get; private set; } = 20;
	public Dictionary<int, (float X, float Z)> IslandPositions { get; } = new();
	public List<LobbyIslandInfo> Islands { get; } = new();

	public static LobbyDefinition Load()
	{
		var definition = new LobbyDefinition();
		definition.ParseLobby( FileSystem.ReadAllText( "/lobby/lobby.txt" ) );
		foreach ( var theme in ThemeFiles )
		{
			try { definition.ParseTheme( theme, FileSystem.ReadAllText( $"/lobby/{theme}.txt" ) ); }
			catch ( Exception exception ) when ( exception is IOException or InvalidOperationException ) { Log?.Warning( $"Lobby island {theme}: {exception.Message}" ); }
		}
		return definition;
	}

	public void ParseLobby( string text )
	{
		foreach ( var (name, arguments) in Commands( text ) )
		{
			switch ( name )
			{
				case "ISLANDFOV": FieldOfView = Number( arguments, 0 ); break;
				case "SPINSPEED": SpinSpeed = Number( arguments, 0 ); break;
				case "SPINRADIUS": SpinRadius = Number( arguments, 0 ); break;
				case "VERTICALOFFSET": VerticalOffset = Number( arguments, 0 ); break;
				case "ISLANDCAMERAPOSITION" when arguments.Count >= 3:
					IslandPositions[(int)Number( arguments, 0 )] = (Number( arguments, 1 ), Number( arguments, 2 ));
					break;
			}
		}
	}

	public LobbyIslandInfo? ParseTheme( string level, string text )
	{
		var commands = Commands( text ).ToList();
		var island = commands.FirstOrDefault( command => command.Name == "ISLAND" && command.Arguments.Count >= 7 );
		if ( island.Name == null )
			return null;
		var sky = commands.FirstOrDefault( command => command.Name == "SKYCOLOUR" && command.Arguments.Count >= 3 );
		var colour = sky.Name == null ? ((byte)0, (byte)0, (byte)0)
			: ((byte)Math.Clamp( Number( sky.Arguments, 0 ), 0, 255 ), (byte)Math.Clamp( Number( sky.Arguments, 1 ), 0, 255 ), (byte)Math.Clamp( Number( sky.Arguments, 2 ), 0, 255 ));
		var lightning = commands.FirstOrDefault( command => command.Name == "LIGHTNING" );
		var rainy = commands.FirstOrDefault( command => command.Name == "RAINY" );
		var result = new LobbyIslandInfo( (int)Number( island.Arguments, 0 ), level, island.Arguments[1], island.Arguments[2], island.Arguments[3], island.Arguments[4],
			Number( island.Arguments, 5 ), Number( island.Arguments, 6 ), colour,
			commands.Where( command => command.Name == "FLYINGMESH" && command.Arguments.Count >= 2 ).Select( command => command.Arguments[1] ).ToArray(),
			rainy.Name != null && Number( rainy.Arguments, 0 ) != 0, lightning.Name == null ? 0 : (int)Number( lightning.Arguments, 0 ) );
		Islands.RemoveAll( existing => existing.Index == result.Index );
		Islands.Add( result );
		Islands.Sort( ( a, b ) => a.Index.CompareTo( b.Index ) );
		return result;
	}

	/// <summary>Island centre in MD2 lobby coordinates (x, z), from ISLANDCAMERAPOSITION.</summary>
	public (float X, float Z) PositionOf( LobbyIslandInfo island ) =>
		IslandPositions.TryGetValue( island.Index, out var position ) ? position : (400 + island.Index * 200, 400);

	private static float Number( IReadOnlyList<string> arguments, int index ) =>
		index < arguments.Count && float.TryParse( arguments[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) ? value : 0;

	/// <summary>Parses <c>NAME(arg, "string", …)</c> commands; anything else is ignored.</summary>
	public static IEnumerable<(string Name, IReadOnlyList<string> Arguments)> Commands( string text )
	{
		foreach ( var rawLine in text.Replace( "\r", "\n" ).Split( '\n' ) )
		{
			var line = rawLine.Trim();
			var open = line.IndexOf( '(' );
			var close = line.LastIndexOf( ')' );
			if ( open <= 0 || close < open )
				continue;
			var name = line[..open].Trim().ToUpperInvariant();
			var arguments = new List<string>();
			var current = new System.Text.StringBuilder();
			var quoted = false;
			foreach ( var character in line[(open + 1)..close] )
			{
				if ( character == '"' )
					quoted = !quoted;
				else if ( character == ',' && !quoted )
				{
					arguments.Add( current.ToString().Trim() );
					current.Clear();
				}
				else
					current.Append( character );
			}
			arguments.Add( current.ToString().Trim() );
			yield return (name, arguments);
		}
	}
}
