using System.Reflection;

namespace OpenTPW.Mods;

/// <summary>
/// Resolves the run's extensions and mods once at startup (docs/MODS.md) and makes them active for the whole process.
/// Changes to <c>mods.json</c> apply at the next start. Phase 1 mods are data only: nothing is loaded or executed, and
/// nothing consumes their content yet.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
public static class ModStartup
{
	public const string ModDirOption = "--mod-dir";
	public const string DisableExtensionOption = "--disable-extension";

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
		var set = Resolve( discovered, settings, Values( args, DisableExtensionOption, "an extension or mod id such as opentpw.online" ), diagnostics );
		Report( set, diagnostics, trace, warning );
		OpenTpwExtensions.Activate( set );
		Discovered = discovered;
		return set;
	}

	/// <summary>The browser startup: official extensions only, with the choices saved in <c>/config/mods.json</c>.</summary>
	public static ModSet InitializeOfficial( Action<string> trace, Action<string> warning )
	{
		var diagnostics = new List<string>();
		var settings = ModSettings.Load( ModSettings.GetDefaultPath(), diagnostics );
		var set = Resolve( Array.Empty<DiscoveredMod>(), settings, Array.Empty<string>(), diagnostics );
		Report( set, diagnostics, trace, warning );
		OpenTpwExtensions.Activate( set );
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
