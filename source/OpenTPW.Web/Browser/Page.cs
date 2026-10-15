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

	/// <summary>Queues a missing Clarity replacement without blocking a render frame.</summary>
	[JSImport( "requestTexture", "opentpw-page" )]
	public static partial void RequestTexture( string relativePath );
}
