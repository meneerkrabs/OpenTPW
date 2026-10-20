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
	/// The rain mood extension (docs/UMBRELLAS.md, "The rain mood rule (phase U4)"): Theme Park Inc's weather rule for the guests' meters, which Theme Park World does not have. In rain, storm and snow a guest's happiness, thirst and hunger change
	/// by the shipped kid row, once per qualifying day, and an open umbrella softens a happiness loss. Off by default; it changes every park with weather, so a save always records it while it is on
	/// (it is not in <c>ParkResumeMods.UsedOnlyExtensionIds</c>) and its version is the rule identity. It does not need the umbrellas extension, and the umbrellas work without it.
	/// </summary>
	// [EXT:rain-mood-extension] OpenTPW's own extension; Theme Park World's guests do not mind the weather
	public static IOpenTpwExtension RainMood { get; } = new OpenTpwExtension( "opentpw.rain-mood", "Rain mood", new ModVersion( 1, 0, 0 ), DefaultEnabled: false, AffectsSimulation: true );

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

	/// <summary>
	/// The evening and night extension (docs/EVENING.md): a day and night cycle on the play time (not the park speed), with a darker sky, ground and water at night and lamps that light up at
	/// dusk (the pools extension's lamps and underwater lights, lamp-like original features and a few ride lights, from <c>content/data/evening-lights.json</c>). The cycle and the lamps are presentation only and change no hash.
	/// Since version 1.1.0 the extension also brings a buildable torch per Theme Park World theme (<c>content/evening</c>, <see cref="EveningContent"/>): an object with a price is a simulation change, so <c>AffectsSimulation</c> is true,
	/// but a park save records the extension only while the park holds a torch (<c>ParkResumeMods.UsedOnlyExtensionIds</c>), so any other park saves byte-identically whether the extension is on or off. On by default; the pace is set on the OpenTPW options page.
	/// </summary>
	// [EXT:evening] OpenTPW's own extension; the original game's parks are always in daylight
	// [EXT:evening-torches] user-approved 2026-10-19 ("Onderdeel van de avondextensie"): the torches belong to this extension, which therefore changes the simulation while a park holds one
	public static IOpenTpwExtension Evening { get; } = new OpenTpwExtension( "opentpw.evening", "Evening and night", new ModVersion( 1, 1, 0 ), DefaultEnabled: true, AffectsSimulation: true, PlayerSelectable: true );

	/// <summary>
	/// The winter extension (docs/WINTER.md): a winter kiosk and a glühwein stall per Theme Park World theme. The kiosk sells a warm item (a hat or earmuffs first, then mittens) all year, its demand following the season and the snow; the stall
	/// is a drinks shop that does not make guests drunk and sells best in snow and in the winter months. On by default (a player can switch it off) and not in a Theme Park Inc run; it changes the simulation, but a park save records the
	/// extension only while the park has one of its shops or a guest with a warm item, so any other park saves byte-identically whether the extension is on or off (<c>ParkResumeMods.UsedOnlyExtensionIds</c>). This is phase W3, the simulation;
	/// the drawing of the items (W4) and the cold rule (W5) come later, and the version is the rule identity: every behaviour-changing release bumps it.
	/// </summary>
	// [EXT:winter-extension] OpenTPW's own extension; Theme Park World has neither these shops nor warm items
	public static IOpenTpwExtension Winter { get; } = new OpenTpwExtension( "opentpw.winter", "Winter", new ModVersion( 1, 0, 0 ), DefaultEnabled: true, AffectsSimulation: true );

	/// <summary>
	/// Wintry Decembers (docs/WINTER.md, "Wintry Decembers"): the rain and thunderstorms of a December weather roll become snow in the jungle and fantasy parks (the Halloween park already snows; space has no precipitation). Snow instead of rain and
	/// lightning is what the umbrella rules and the rain mood read, so it changes every park of those themes: a save always records the extension while it is on (it is not in <c>ParkResumeMods.UsedOnlyExtensionIds</c>), and its version is the rule
	/// identity. Off by default, because it is an option and not a repair (the original's Decembers are storms), and not in a Theme Park Inc run, which has no such weather.
	/// </summary>
	// [EXT:winter-weather] OpenTPW's own option; Theme Park World's jungle and fantasy parks never snow (their snow band is -1..-1)
	public static IOpenTpwExtension WinterWeather { get; } = new OpenTpwExtension( "opentpw.winter-weather", "Wintry Decembers", new ModVersion( 1, 0, 0 ), DefaultEnabled: false, AffectsSimulation: true );

	/// <summary>
	/// The cold rule (docs/WINTER.md, "Cold mood"): in snow a guest loses happiness once per qualifying day (the rain mood's gate), softened by a head item, mittens and a recent glühwein, never above 0. With it on it replaces the rain mood's
	/// snow happiness (+1 in Theme Park Inc's kid row). It changes the simulation, so a save always records it while it is on and its version is the rule identity (the numbers are code defaults). Off by default, because it is an option,
	/// and not in a Theme Park Inc run, which has no such weather.
	/// </summary>
	// [EXT:winter-cold] OpenTPW's own rule; Theme Park World has no cold and Theme Park Inc's snow cheers kids
	public static IOpenTpwExtension WinterCold { get; } = new OpenTpwExtension( "opentpw.winter-cold", "Cold mood", new ModVersion( 1, 0, 0 ), DefaultEnabled: false, AffectsSimulation: true );

	/// <summary>
	/// Always winter (docs/WINTER.md, "Always winter"): every weather roll of the year uses December's row of the season roll, and its rain and thunderstorms become snow as in a wintry December, in the jungle, fantasy and
	/// Halloween parks (space has no rain or snow band, so its weather is left alone); the winter kiosk and the glühwein stall sell at their winter demand all year. It implies Wintry Decembers, with no double effect when both are on.
	/// It changes the simulation, so a save always records it while it is on (it is not in <c>ParkResumeMods.UsedOnlyExtensionIds</c>), and its version is the rule identity. Off by default, because it is an option, and not in a
	/// Theme Park Inc run, which has no such weather.
	/// </summary>
	// [EXT:winter-always] OpenTPW's own option; user-approved 2026-10-19 ("Winterweer hele jaar"); Theme Park World's seasons change the weather through the year
	public static IOpenTpwExtension WinterAlways { get; } = new OpenTpwExtension( "opentpw.winter-always", "Always winter", new ModVersion( 1, 0, 0 ), DefaultEnabled: false, AffectsSimulation: true );

	/// <summary>
	/// The settling snow extension (docs/WINTER.md, phase W1): snow that settles on the park's ground, roofs and objects while it snows and melts afterwards, with the paths turning to slush. Presentation only:
	/// <c>AffectsSimulation</c> is false, so it is not recorded in park saves and changes no hash (a save keeps the cover as an optional unhashed field). On by default; it shows wherever the weather snows (the Halloween world).
	/// </summary>
	// [EXT:winter-snow] OpenTPW's own extension; the original game has no ground snow
	public static IOpenTpwExtension WinterSnow { get; } = new OpenTpwExtension( "opentpw.winter-snow", "Settling snow", new ModVersion( 1, 0, 0 ), DefaultEnabled: true, AffectsSimulation: false, PlayerSelectable: true );

	/// <summary>
	/// The cheats extension (docs/CHEATS.md): the cheats window (Alt+C) with OpenTPW's own cheats: visitors arriving by a chosen vehicle, the weather, a chosen golden ticket and a ride's vehicle limit. Off by default and only in
	/// Theme Park World parks (not in a Theme Park Inc run, a read-only visit or an online session). The original game's own cheat mode (the secret park name and the Alt keys) is not part of it and works without it. A park save records the
	/// extension only once the park has used one of its own cheats, and such a park needs it to load (<c>ParkResumeMods.UsedOnlyExtensionIds</c>); every other park, cheated through the original's cheats or not, saves the same bytes
	/// whether it is on or off. Selectable from phase K3, the window and its first cheats.
	/// </summary>
	// [EXT:cheats-extension] OpenTPW's own cheats; the original game has the cheat mode only (docs/CHEATS.md)
	public static IOpenTpwExtension Cheats { get; } = new OpenTpwExtension( "opentpw.cheats", "Cheats", new ModVersion( 1, 0, 0 ), DefaultEnabled: false, AffectsSimulation: true );

	/// <summary>All official extensions, in declaration order.</summary>
	public static IReadOnlyList<IOpenTpwExtension> All { get; } = new[] { Online, CrossEditionObjects, SmarterGuests, GuestInsights, Pools, PoolGuestArt, Umbrellas, RainMood, Winter, WinterWeather, WinterCold, WinterAlways, RestoredAdvice, Evening, WinterSnow, Cheats };

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
