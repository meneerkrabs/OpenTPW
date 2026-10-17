using System.Reflection;

namespace OpenTPW.Mods;

/// <summary>
/// Resolves the run's extensions and mods once at startup (docs/MODS.md) and makes them active for the whole process.
/// Changes to <c>mods.json</c> apply at the next start. Mods are data only: nothing is loaded or executed; their overrides,
/// strings and objects are installed by <see cref="ModContentRoots"/>.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
public static class ModStartup
{
	public const string ModDirOption = "--mod-dir";
	public const string DisableExtensionOption = "--disable-extension";
	/// <summary><c>--enable-mod &lt;id&gt;</c> (repeatable): switches an extension or mod on for this run.</summary>
	public const string EnableModOption = "--enable-mod";
	/// <summary><c>--mod-input &lt;mod id&gt;:&lt;input id&gt;=&lt;folder&gt;</c> (repeatable): the folder of a mod input for this run.</summary>
	public const string ModInputOption = "--mod-input";

	/// <summary>
	/// The mod folders found at startup, valid or not: the Extensions and mods screen lists them ([EXT:mods-ui]). Like the
	/// resolved set it does not change until the next start, and the browser build has none.
	/// </summary>
	public static IReadOnlyList<DiscoveredMod> Discovered { get; private set; } = Array.Empty<DiscoveredMod>();

	/// <summary>
	/// This build's OpenTPW version for <c>openTpw</c> ranges: the assembly's informational version, or null for a
	/// development build without one (the SDK default 1.0.0), in which case the ranges are not checked.
	/// </summary>
	public static ModVersion? OpenTpwVersion( Assembly? assembly = null )
	{
		var text = (assembly ?? typeof( ModStartup ).Assembly).GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		if ( text == null )
			return null;
		var plus = text.IndexOf( '+' );
		if ( plus >= 0 )
			text = text[..plus];
		return ModVersion.TryParse( text.TrimStart( 'v' ), out var version ) && version != new ModVersion( 1, 0, 0 ) ? version : null;
	}

	/// <summary>
	/// The desktop startup: <c>--mod-dir</c>, <c>OPENTPW_MOD_DIRS</c>, <c>&lt;config&gt;/mods</c> and
	/// <c>&lt;program&gt;/content/mods</c>, the saved choices, then <c>--disable-extension</c>. Smoke tests ignore the
	/// saved choices and the configuration folder's mods, like the compatibility settings.
	/// </summary>
	public static ModSet Initialize( string[] args, Action<string> trace, Action<string> warning ) =>
		Initialize( args, trace, warning, Environment.GetEnvironmentVariable( ModDiscovery.EnvironmentVariable ), AppContext.BaseDirectory );

	/// <summary><see cref="Initialize(string[], Action{string}, Action{string})"/> with explicit <c>OPENTPW_MOD_DIRS</c> and program folder (tests).</summary>
	internal static ModSet Initialize( string[] args, Action<string> trace, Action<string> warning, string? modDirsEnvironment, string? programDirectory )
	{
		var smokeTest = args.Contains( "--smoke-test" );
		var configDirectory = Path.GetDirectoryName( DisplaySettings.GetDefaultPath() )!;
		var diagnostics = new List<string>();
		var settings = smokeTest ? ModSettings.Default : ModSettings.Load( ModSettings.GetDefaultPath(), diagnostics );
		var discovered = ModDiscovery.Discover( Values( args, ModDirOption, "a mod folder containing " + ModManifest.FileName ),
			ModDiscovery.SplitEnvironment( modDirsEnvironment ),
			smokeTest ? null : configDirectory, programDirectory );
		settings = WithCommandLine( settings, discovered, Values( args, EnableModOption, "an extension or mod id such as opentpw.cross-edition-objects" ),
			Values( args, ModInputOption, "<mod id>:<input id>=<folder>" ) );
		var set = Resolve( discovered, settings, Values( args, DisableExtensionOption, "an extension or mod id such as opentpw.online" ), diagnostics );
		Report( set, diagnostics, trace, warning );
		OpenTpwExtensions.Activate( set );
		Discovered = discovered;
		// [EXT:mods] the mods' overrides, strings and objects become visible to the game (docs/MODS.md)
		ModContentRoots.Activate( set, trace );
		return set;
	}

	/// <summary>
	/// The saved choices with this run's <c>--enable-mod</c> and <c>--mod-input</c> on top (they change nothing on disk). An id that names no
	/// extension or installed mod, or an input that is not <c>&lt;mod id&gt;:&lt;input id&gt;=&lt;folder&gt;</c>, is an error, like <c>--disable-extension</c>.
	/// </summary>
	internal static ModSettings WithCommandLine( ModSettings settings, IReadOnlyList<DiscoveredMod> discovered, IReadOnlyList<string> enabled, IReadOnlyList<string> inputs )
	{
		foreach ( var id in enabled )
		{
			if ( OfficialExtensions.Find( id ) == null && !discovered.Any( mod => mod.Id == id ) )
				throw new ArgumentException( $"{EnableModOption} '{id}' names no OpenTPW extension or installed mod (extensions: {string.Join( ", ", OfficialExtensions.All.Select( extension => extension.Id ) )})." );
			settings = settings.WithEnabled( id, true );
		}
		foreach ( var text in inputs )
		{
			var colon = text.IndexOf( ':' );
			var equals = text.IndexOf( '=' );
			var (mod, input, folder) = colon > 0 && equals > colon ? (text[..colon], text[(colon + 1)..equals], text[(equals + 1)..]) : ("", "", "");
			if ( !ModManifest.IsValidId( mod ) || !ModManifest.IsValidInputId( input ) || string.IsNullOrWhiteSpace( folder ) || folder.Length > ModSettings.MaximumInputPathLength || folder.Any( char.IsControl ) )
				throw new ArgumentException( $"{ModInputOption} '{text}' is not <mod id>:<input id>=<folder> (for example community.tpi-rides:tpi=/games/Theme Park Inc)." );
			settings = settings.WithInput( mod, input, folder );
		}
		return settings;
	}

	/// <summary>
	/// The browser startup: the mods shipped with the page (<c>content/mods</c>, written to the runtime's memory like the other content), the
	/// choices saved in <c>/config/mods.json</c> and the inputs the page copied from the player's own game files
	/// (<see cref="BrowserModInputs"/>). The browser has no mod folders of its own and loads no code: the mods are data only, as on the desktop.
	/// </summary>
	public static ModSet InitializeBrowser( Action<string> trace, Action<string> warning ) =>
		InitializeBrowser( trace, warning, AppContext.BaseDirectory, ModSettings.GetDefaultPath() );

	/// <summary><see cref="InitializeBrowser(Action{string}, Action{string})"/> with an explicit program folder and settings file (tests).</summary>
	internal static ModSet InitializeBrowser( Action<string> trace, Action<string> warning, string programDirectory, string settingsPath )
	{
		var diagnostics = new List<string>();
		var discovered = ModDiscovery.Discover( Array.Empty<string>(), Array.Empty<string>(), null, programDirectory );
		var settings = BrowserModInputs.WithDefaults( ModSettings.Load( settingsPath, diagnostics ), discovered );
		var set = Resolve( discovered, settings, Array.Empty<string>(), diagnostics );
		Report( set, diagnostics, trace, warning );
		OpenTpwExtensions.Activate( set );
		Discovered = discovered;
		BrowserModInputs.LockInUse( set );
		// [EXT:mods] the mods' objects become visible to the game (docs/MODS.md); files-based overrides work the same way
		ModContentRoots.Activate( set, trace );
		return set;
	}

	/// <summary>Resolves against this build's extensions; an unknown <c>--disable-extension</c> id is an error.</summary>
	public static ModSet Resolve( IReadOnlyList<DiscoveredMod> discovered, ModSettings settings, IReadOnlyList<string> disabled, ICollection<string> diagnostics )
	{
		foreach ( var id in disabled )
			if ( OfficialExtensions.Find( id ) == null && !discovered.Any( mod => mod.Id == id ) )
				throw new ArgumentException( $"{DisableExtensionOption} '{id}' names no OpenTPW extension or installed mod (extensions: {string.Join( ", ", OfficialExtensions.All.Select( extension => extension.Id ) )})." );
		foreach ( var id in settings.Enabled.Where( pair => pair.Value ).Select( pair => pair.Key ) )
			if ( OfficialExtensions.Find( id ) == null && !discovered.Any( mod => mod.Id == id ) )
				diagnostics.Add( $"Mod settings enable '{id}', which is not installed." );
		return ModSet.Resolve( discovered, settings, OpenTpwVersion(), GameEditions.Current, OfficialExtensions.All, disabled );
	}

	private static void Report( ModSet set, IEnumerable<string> diagnostics, Action<string> trace, Action<string> warning )
	{
		foreach ( var diagnostic in diagnostics )
			warning( diagnostic );
		foreach ( var refusal in set.Refused )
			warning( $"Mod {refusal.Id} refused: {refusal.Reason}" );
		var disabled = set.Extensions.Where( extension => !extension.Enabled ).Select( extension => extension.Extension.Id ).ToList();
		if ( disabled.Count > 0 )
			trace( $"Extensions disabled: {string.Join( ", ", disabled )}." );
		if ( set.Mods.Count > 0 )
			trace( $"Mods: {string.Join( ", ", set.Mods.Select( mod => $"{mod.Id} {mod.Manifest.Version} ({mod.ContentSha256[..12]})" ) )}." );
	}

	/// <summary>Every value of a repeatable option.</summary>
	public static IReadOnlyList<string> Values( string[] args, string name, string description )
	{
		var values = new List<string>();
		for ( var index = 0; index < args.Length; index++ )
		{
			if ( args[index] != name )
				continue;
			if ( index + 1 >= args.Length || args[index + 1].StartsWith( "--" ) )
				throw new ArgumentException( $"{name} requires {description}." );
			values.Add( args[++index] );
		}
		return values;
	}
}
