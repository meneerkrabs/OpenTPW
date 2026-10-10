using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// Which edition of the original data OPENTPW_GAME_PATH holds. Corpus pins are taken from the
/// 1999 release; official Patch 2 (docs/PATCH-2.md) adds four DATE fonts and shortens Jelly.RSE.
/// </summary>
internal static class OriginalEdition
{
	/// <summary>The fonts Patch 2 adds to the English language folder, with their SHA-256 (docs/PATCH-2.md).</summary>
	internal static readonly IReadOnlyDictionary<string, string> Patch2Fonts = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
	{
		["DATEBIG.bf4"] = "1E5BAB6C50447002638AAC6C636D12F9FD49493018DBF214ADB32FC070D0951D",
		["DATEMED.bf4"] = "A9B6EE073567675F9B1799D8CC354E6A5BB65E525AFBADB26F275B634F7F36BC",
		["DATESMALL.bf4"] = "15EB42CD95898A7CD87F7B4E15B4FABDFB0F321C092BE257DF13A23F71DD3FED",
		["DATETINY.bf4"] = "92211A2C337F8AD56FB2132181035C2C77A75E1F425EAA887341A6E7748886B5",
	};

	internal static bool IsPatch2( string dataDirectory ) =>
		GameLanguage.FindEntry( dataDirectory, Path.Combine( "Language", "English", "DATEMED.bf4" ), false ) != null;

	/// <summary>The 1999 release's 33 English BF4 files: every .bf4 except the ones Patch 2 adds.</summary>
	internal static string[] BaseFonts( string englishDirectory ) =>
		Directory.GetFiles( englishDirectory )
			.Where( file => Path.GetExtension( file ).Equals( ".bf4", StringComparison.OrdinalIgnoreCase ) && !Patch2Fonts.ContainsKey( Path.GetFileName( file ) ) )
			.ToArray();

	/// <summary>Asserts that the English folder holds exactly the Patch 2 fonts when <paramref name="patched"/>, else none of them.</summary>
	internal static void AssertPatch2Fonts( string englishDirectory, bool patched )
	{
		var present = Directory.GetFiles( englishDirectory ).Where( file => Patch2Fonts.ContainsKey( Path.GetFileName( file ) ) ).ToArray();
		Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual( patched ? Patch2Fonts.Count : 0, present.Length );
		foreach ( var file in present )
			Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual( Patch2Fonts[Path.GetFileName( file )],
				Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( File.ReadAllBytes( file ) ) ), file );
	}
}
