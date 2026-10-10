using System.Runtime.InteropServices.JavaScript;

namespace OpenTPW;

/// <summary>Calls into the page (main.js, registered as module "opentpw-page").</summary>
internal static partial class Page
{
	/// <summary>Asks the page to copy a level's folder from the player's files; it calls <c>Program.LevelLoaded</c> when done.</summary>
	[JSImport( "requestLevel", "opentpw-page" )]
	public static partial void RequestLevel( string level );
}
