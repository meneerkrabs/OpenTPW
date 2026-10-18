using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>
/// Feature switches that combine the edition's capabilities with the active official extensions (docs/MODS.md). Read
/// them instead of the underlying capability, so a disabled extension hides its features everywhere.
/// </summary>
// [EXT:mods] OpenTPW's own extension switches; the original game had none
public static class FeatureGates
{
	public const string OnlineDisabledMessage = "OpenTPW online extension is disabled";

	/// <summary>The <c>opentpw.online</c> extension is on for this run.</summary>
	public static bool Online => OpenTpwExtensions.IsEnabled( OfficialExtensions.Online );

	/// <summary>
	/// The <c>opentpw.cross-edition-objects</c> extension is on for this run: objects from a registered content root of another
	/// original game get their own Info.Ids, sound banks and economy defaults (docs/MODS.md).
	/// </summary>
	public static bool CrossEditionObjects => OpenTpwExtensions.IsEnabled( OfficialExtensions.CrossEditionObjects );

	/// <summary>
	/// [EXT:pools-extension] The <c>opentpw.pools</c> extension is on for this run: the pool paint tool, its buy-list rows and the pool cell type are available (docs/POOLS.md).
	/// It is on by default, but not in a Theme Park Inc run: the pools UI (tool, rows, changing rooms, swimmers) is unavailable there for now, because
	/// its themes, objects and layout were made and checked for the four Theme Park World parks only (docs/POOLS.md, "Theme Park Inc").
	/// </summary>
	public static bool Pools => OpenTpwExtensions.IsEnabled( OfficialExtensions.Pools ) && !GameEditions.IsThemeParkInc;

	/// <summary>
	/// [EXT:umbrellas-extension] The <c>opentpw.umbrellas</c> extension is on for this run: the brolly shops are listed in the theme catalogs and the guests' umbrella rules apply (docs/UMBRELLAS.md). It is on by default, but not in a
	/// Theme Park Inc run, which has its own shop, art and rules.
	/// </summary>
	public static bool Umbrellas => OpenTpwExtensions.IsEnabled( OfficialExtensions.Umbrellas ) && !GameEditions.IsThemeParkInc;

	/// <summary>The <c>opentpw.pools-guest-art</c> extension is on: the land guests use the pools extension's 2x kid and head sets (docs/POOLS.md), when the pools extension is on too.</summary>
	public static bool PoolGuestArt => OpenTpwExtensions.IsEnabled( OfficialExtensions.PoolGuestArt );

	/// <summary>
	/// The <c>opentpw.smarter-guests</c> extension is on for this run (docs/SMARTER-GUESTS.md). The guest simulation reads it once, when it is built, never per tick.
	/// </summary>
	public static bool SmarterGuests => OpenTpwExtensions.IsEnabled( OfficialExtensions.SmarterGuests );

	/// <summary>The <c>opentpw.guest-insights</c> extension is on for this run: refusal reasons and the complaints summary in the park windows (docs/SMARTER-GUESTS.md).</summary>
	public static bool GuestInsights => OpenTpwExtensions.IsEnabled( OfficialExtensions.GuestInsights );

	/// <summary>
	/// [EXT:restored-advice] The <c>opentpw.restored-advice</c> extension is on for this run (on by default): the advisor says the lines tp.exe v2.0 never plays (docs/reverse/ADVISOR-SCAN.md, "Restored advice").
	/// It only reads park state and posts advice, so it is not a simulation extension.
	/// </summary>
	public static bool RestoredAdvice => OpenTpwExtensions.IsEnabled( OfficialExtensions.RestoredAdvice );

	/// <summary>
	/// [EXT:evening] The <c>opentpw.evening</c> extension is on for this run (on by default): the park has a day and night cycle and its lamps (docs/EVENING.md). Presentation only.
	/// </summary>
	public static bool Evening => OpenTpwExtensions.IsEnabled( OfficialExtensions.Evening );

	/// <summary>Exporting, importing, visiting and postcards: the edition supports sharing and the online extension is on.</summary>
	public static bool Sharing => GameEditions.Capabilities.Sharing && Online;

	/// <summary>
	/// Refuses the online command-line options while the online extension is disabled: <c>--export-park</c>,
	/// <c>--import-park</c>, <c>--visit-park</c> and <c>--online-dir</c>.
	/// </summary>
	public static void RefuseOnlineCommandLine( string[] args )
	{
		if ( Online )
			return;
		var option = new[] { "--export-park", "--import-park", "--visit-park", "--online-dir" }.FirstOrDefault( args.Contains );
		if ( option != null )
			throw new ArgumentException( $"{OnlineDisabledMessage}: {option} is unavailable (enable opentpw.online in mods.json, or leave out --disable-extension opentpw.online; docs/MODS.md)." );
	}
}
