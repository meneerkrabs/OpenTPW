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
	/// auto-joined edges, cost money per cell and are saved. On by default (a player can switch it off); it adds a cell type to the park simulation, but a park save records
	/// the extension only while the park has pool cells or a changing room, so a park without either saves byte-identically whether the extension is on or off
	/// (<c>ParkResumeMods.UsedOnlyExtensionIds</c>).
	/// </summary>
	// [EXT:pools-extension] OpenTPW's own extension; the original game has no pools
	public static IOpenTpwExtension Pools { get; } = new OpenTpwExtension( "opentpw.pools", "Pools", new ModVersion( 1, 0, 0 ), DefaultEnabled: true, AffectsSimulation: true );

	/// <summary>
	/// The pool guest art (docs/POOLS.md): while it and the pools extension are on, the land guests of a Theme Park World park are drawn with OpenTPW's own 2x Blender kid and head sets instead of the original sprites
	/// (the swimmers always use the swim sets while pools is on). Presentation only: <c>AffectsSimulation</c> is false, so it is not recorded in saves and changes no hash. On by default; switch it off for the original guests on land.
	/// </summary>
	// [EXT:pools-guest-art] OpenTPW's own guest art; the original game's guests are the faithful look
	public static IOpenTpwExtension PoolGuestArt { get; } = new OpenTpwExtension( "opentpw.pools-guest-art", "Pool guest art", new ModVersion( 1, 0, 0 ), DefaultEnabled: true, AffectsSimulation: false );

	/// <summary>
	/// The umbrellas extension (docs/UMBRELLAS.md): a brolly shop per theme and guests who carry an open umbrella in the rain, from a static trace of Theme Park Inc's rules (the shop's score in bad weather, a balloon traded for an umbrella,
	/// the five umbrella states). On by default (a player can switch it off) and not in a Theme Park Inc run; it changes the simulation, but a park save records the extension only while the park has a brolly shop or a guest with an
	/// umbrella, so any other park saves byte-identically whether the extension is on or off (<c>ParkResumeMods.UsedOnlyExtensionIds</c>). Phase U2 is the simulation; the drawing (U3) and the rain mood rule
	/// (<c>opentpw.rain-mood</c>, U4) come later, and the version is the rule identity: every behaviour-changing release bumps it.
	/// </summary>
	// [EXT:umbrellas-extension] OpenTPW's own extension; Theme Park World has no umbrellas
	public static IOpenTpwExtension Umbrellas { get; } = new OpenTpwExtension( "opentpw.umbrellas", "Umbrellas", new ModVersion( 1, 0, 0 ), DefaultEnabled: true, AffectsSimulation: true );

	/// <summary>
	/// The guest insights extension (docs/SMARTER-GUESTS.md): the refusal-reason counters (too expensive, queue too long, too scary, too boring, ride broken or closed) in the park information window (phase 1).
	/// It only reads the simulation (<see cref="GuestRefusalCounters"/>, a passive observer), so it is not recorded in park saves and a player may switch it on freely
	/// (<see cref="IOpenTpwExtension.PlayerSelectable"/>).
	/// </summary>
	// [EXT:guest-insights] OpenTPW's own extension; the original game has no such counters
	public static IOpenTpwExtension GuestInsights { get; } = new OpenTpwExtension( "opentpw.guest-insights", "Guest insights", new ModVersion( 1, 0, 0 ), DefaultEnabled: false, AffectsSimulation: false, PlayerSelectable: true );

	/// <summary>
	/// The smarter guests extension (docs/SMARTER-GUESTS.md): guests that decide better, through the pure decision layer (<see cref="IGuestDecisions"/>). Off by default; it changes the simulation,
	/// so park saves record it, and its version is the algorithm identity: every behaviour-changing release bumps it, and a park of another version is refused. Version 1.0.0 was the seam and the guests' identity only; 1.1.0 adds value for money (phase 2: price in the shop and sideshow choice, the booth weighing the fee against the park's worth, a price sensitivity trait); 1.2.0 adds spreading (phase 3: an expected-wait queue term for every attraction from a persisted throughput estimate, a stable per-guest preference per attraction instance and hysteresis between rides); 1.3.0 adds queue patience (phase 4: a guest does not join a queue whose expected wait exceeds its patience and gives up after waiting too long, each only when another ride it could choose is clearly quicker); 1.4.0 adds planning needs (phase 5: a service whose need will cross its level before the guest is done with its ride is scored as if it had, a service on the guest's way gets a small detour step, and shortly after a meal a ride that would make the guest sick scores lower, from a persisted meal time); 1.5.0 adds clean and quiet paths (phase 6: a wander goal on or beside litter or in a crowd is swapped for a cleaner or quieter cell of the same walk, and a guest standing in litter or a crowd complains with a small, rate-limited happiness cost from a persisted complaint time). Players cannot switch it on yet: it becomes selectable once the rule set (phases 2 to 6) is complete, so a save does not break with every phase (<see cref="IOpenTpwExtension.PlayerSelectable"/>).
	/// </summary>
	// [EXT:smarter-guests] OpenTPW's own extension; the original game's guests are the faithful default
	public static IOpenTpwExtension SmarterGuests { get; } = new OpenTpwExtension( "opentpw.smarter-guests", "Smarter guests", new ModVersion( 1, 6, 0 ), DefaultEnabled: false, AffectsSimulation: true, PlayerSelectable: true );

	/// <summary>
	/// The restored advice extension (docs/MODS.md, docs/reverse/ADVISOR-SCAN.md "Restored advice"): triggers for advisor messages that exist in the shipped data (speech recorded, often captioned)
	/// but that tp.exe v2.0 never plays, because their score callback is the constant-zero stub or nothing posts them. On by default and it never changes the park simulation: the advisor is
	/// outside the world state hash (<see cref="WorldStateHash"/>), the extension only reads park state and posts advice, and a save made with or without it loads with or without it
	/// (<see cref="AdvisorController.AdvisorControllerSnapshot"/> has the same shape and every descriptor exists in both cases), so it is not recorded in park saves.
	/// </summary>
	// [EXT:restored-advice] OpenTPW's own extension; the original game never says these lines
	public static IOpenTpwExtension RestoredAdvice { get; } = new OpenTpwExtension( "opentpw.restored-advice", "Restored advice", new ModVersion( 1, 0, 0 ), DefaultEnabled: true, AffectsSimulation: false, PlayerSelectable: true );

	/// <summary>All official extensions, in declaration order.</summary>
	public static IReadOnlyList<IOpenTpwExtension> All { get; } = new[] { Online, CrossEditionObjects, SmarterGuests, GuestInsights, Pools, PoolGuestArt, Umbrellas, RestoredAdvice };

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
