using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace OpenTPW.Online;

/// <summary>The browser supplies NFKC; identity casing stays in .NET on every platform.</summary>
[SupportedOSPlatform( "browser" )]
internal static partial class BrowserText
{
	[JSImport( "normalizeCompatibility", "opentpw-text" )]
	internal static partial string NormalizeCompatibility( string text );
}
