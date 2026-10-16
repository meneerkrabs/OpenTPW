namespace OpenTPW.Mods;

/// <summary>
/// An official extension: OpenTPW engine code that lives in this repository, carries its own fidelity labels and
/// can be switched off in <c>mods.json</c> or with <c>--disable-extension &lt;id&gt;</c> (docs/MODS.md). Extensions ship
/// no files; files-based mods may depend on them like on any other mod.
/// </summary>
public interface IOpenTpwExtension
{
	/// <summary>The extension's id, starting with <c>opentpw.</c>.</summary>
	string Id { get; }
	string Name { get; }
	ModVersion Version { get; }
	/// <summary>Whether the extension is on when <c>mods.json</c> has no choice for it.</summary>
	bool DefaultEnabled { get; }
	/// <summary>Whether the extension changes the park simulation; such extensions are recorded in park saves (a later phase).</summary>
	bool AffectsSimulation { get; }
}

/// <summary>A compiled-in extension described by its fields.</summary>
public sealed record OpenTpwExtension( string Id, string Name, ModVersion Version, bool DefaultEnabled, bool AffectsSimulation ) : IOpenTpwExtension;

/// <summary>Every official extension compiled into this build (docs/MODS.md).</summary>
// [EXT:mods] OpenTPW's own extension registry; the original game had none
public static class OfficialExtensions
{
	/// <summary>
	/// The online extension (docs/ONLINE.md): Go Online, publishing, postcards, visiting and importing parks, the online
	/// folder and its command-line tools. On by default; it makes no network request until the player acts.
	/// </summary>
	public static IOpenTpwExtension Online { get; } = new OpenTpwExtension( "opentpw.online", "OpenTPW online", new ModVersion( 1, 0, 0 ), DefaultEnabled: true, AffectsSimulation: false );

	/// <summary>All official extensions, in declaration order.</summary>
	public static IReadOnlyList<IOpenTpwExtension> All { get; } = new[] { Online };

	public static IOpenTpwExtension? Find( string id ) => All.FirstOrDefault( extension => extension.Id == id );
}

/// <summary>
/// The extensions and mods active in this process: resolved once at startup (<see cref="ModStartup"/>) and unchanged
/// until the next start. Before startup, and in tests that do not set it, every official extension has its default.
/// </summary>
public static class OpenTpwExtensions
{
	public static ModSet Active { get; private set; } = ModSet.Defaults( OfficialExtensions.All );

	public static bool IsEnabled( IOpenTpwExtension extension ) => Active.IsEnabled( extension.Id );

	internal static void Activate( ModSet set ) => Active = set;

	/// <summary>Makes <paramref name="set"/> active until the returned scope is disposed (tests).</summary>
	internal static IDisposable Use( ModSet set )
	{
		var previous = Active;
		Active = set;
		return new Restore( () => Active = previous );
	}

	private sealed class Restore( Action restore ) : IDisposable
	{
		public void Dispose() => restore();
	}
}
