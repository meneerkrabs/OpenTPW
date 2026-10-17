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
	/// <summary>
	/// Whether a player can switch the extension on (the Extensions and mods screen lists it; <c>mods.json</c> and <c>--enable-mod</c> are honoured). An extension that is registered but not selectable
	/// exists for tests and development (<see cref="OpenTpwExtensions.Use"/>) until its first real feature ships, so no save gets locked to a version that does nothing.
	/// </summary>
	bool PlayerSelectable => true;
}

/// <summary>A compiled-in extension described by its fields.</summary>
public sealed record OpenTpwExtension( string Id, string Name, ModVersion Version, bool DefaultEnabled, bool AffectsSimulation, bool PlayerSelectable = true ) : IOpenTpwExtension;

/// <summary>Every official extension compiled into this build (docs/MODS.md).</summary>
// [EXT:mods] OpenTPW's own extension registry; the original game had none
public static class OfficialExtensions
{
	/// <summary>
	/// The online extension (docs/ONLINE.md): Go Online, publishing, postcards, visiting and importing parks, the online
	/// folder and its command-line tools. On by default; it makes no network request until the player acts.
	/// </summary>
	public static IOpenTpwExtension Online { get; } = new OpenTpwExtension( "opentpw.online", "OpenTPW online", new ModVersion( 1, 0, 0 ), DefaultEnabled: true, AffectsSimulation: false );

	/// <summary>
	/// The cross-edition objects extension (docs/MODS.md, Cross-edition objects): the engine support a data mod needs to
	/// bring attractions of another original game (Theme Park Inc) into Theme Park World. It ships nothing of Theme Park Inc: the
	/// player supplies that install as a mod input. Off by default because it changes the simulation as soon as such a mod
	/// places an object.
	/// </summary>
	// [EXT:cross-edition-objects] OpenTPW's own extension; neither original game loads another game's objects
	public static IOpenTpwExtension CrossEditionObjects { get; } = new OpenTpwExtension( "opentpw.cross-edition-objects", "Cross-edition objects", new ModVersion( 1, 0, 0 ), DefaultEnabled: false, AffectsSimulation: true );

	/// <summary>
	/// The pools extension (docs/POOLS.md): the player paints pool water cells of any size and shape, which are drawn with an animated surface and
	/// auto-joined edges, cost money per cell and are saved. Off by default because it adds a cell type to the park simulation. A park save records
	/// the extension only while the park has pool cells, so a park without any saves byte-identically whether the extension is on or off
	/// (<c>ParkResumeMods.UsedOnlyExtensionIds</c>).
	/// </summary>
	// [EXT:pools-extension] OpenTPW's own extension; the original game has no pools
	public static IOpenTpwExtension Pools { get; } = new OpenTpwExtension( "opentpw.pools", "Pools", new ModVersion( 1, 0, 0 ), DefaultEnabled: false, AffectsSimulation: true );

	/// <summary>
	/// The guest insights extension (docs/SMARTER-GUESTS.md): reserved for phase 1's guest counters in the park windows. It only reads the simulation, so it is not recorded in park saves. It has no feature
	/// yet, so players cannot switch it on until phase 1 ships (<see cref="IOpenTpwExtension.PlayerSelectable"/>).
	/// </summary>
	// [EXT:guest-insights] OpenTPW's own extension; the original game has no such counters
	public static IOpenTpwExtension GuestInsights { get; } = new OpenTpwExtension( "opentpw.guest-insights", "Guest insights", new ModVersion( 1, 0, 0 ), DefaultEnabled: false, AffectsSimulation: false, PlayerSelectable: false );

	/// <summary>
	/// The smarter guests extension (docs/SMARTER-GUESTS.md): guests that decide better, through the pure decision layer (<see cref="IGuestDecisions"/>). Off by default; it changes the simulation,
	/// so park saves record it, and its version is the algorithm identity: every behaviour-changing release bumps it, and a park of another version is refused. Version 1.0.0 has the seam and
	/// the guests' identity only: the guests still behave as the original, so players cannot switch it on until the first real rule ships in phase 2 (<see cref="IOpenTpwExtension.PlayerSelectable"/>).
	/// </summary>
	// [EXT:smarter-guests] OpenTPW's own extension; the original game's guests are the faithful default
	public static IOpenTpwExtension SmarterGuests { get; } = new OpenTpwExtension( "opentpw.smarter-guests", "Smarter guests", new ModVersion( 1, 0, 0 ), DefaultEnabled: false, AffectsSimulation: true, PlayerSelectable: false );

	/// <summary>All official extensions, in declaration order.</summary>
	public static IReadOnlyList<IOpenTpwExtension> All { get; } = new[] { Online, CrossEditionObjects, SmarterGuests, GuestInsights, Pools };

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
