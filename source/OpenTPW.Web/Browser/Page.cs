using System.Runtime.InteropServices.JavaScript;

namespace OpenTPW;

/// <summary>Calls into the page (main.js, registered as module "opentpw-page").</summary>
internal static partial class Page
{
	/// <summary>Asks the page to copy a level's folder from the player's files; it calls <c>Program.LevelLoaded</c> when done.</summary>
	[JSImport( "requestLevel", "opentpw-page" )]
	public static partial void RequestLevel( string level );

	/// <summary>Asks the page to copy a language's speech files from the player's files; it calls <c>Program.LanguageLoaded</c> when done.</summary>
	[JSImport( "requestLanguage", "opentpw-page" )]
	public static partial void RequestLanguage( string language );

	/// <summary>
	/// Opens the browser's folder dialog for the player's own files of an original game (<c>theme-park-inc</c>) that a mod reads; the page copies
	/// them and calls <c>Program.InputChanged</c> when done.
	/// </summary>
	[JSImport( "chooseInput", "opentpw-page" )]
	public static partial void ChooseInput( string edition );

	/// <summary>
	/// [EXT:extensions-apply-at-load] Asks the page to fetch the pools extension's sprite sets now (docs/POOLS.md, "Content", the browser's sprite sets): the extension was switched on after the start, at a park load or on the
	/// Extensions and mods screen. A fetch already running is followed by one more; files already stored are not fetched again.
	/// </summary>
	[JSImport( "requestPoolSprites", "opentpw-page" )]
	public static partial void RequestPoolSprites();
}
