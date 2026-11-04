using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>RIDES-031: a Theme Park Inc world types its placed footprints like tp.exe's twin commit.</summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncTypedFootprintTests
{
	private static bool Types( Level level ) =>
		(bool)typeof( Level ).GetProperty( "TypesFootprints", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( level )!;

	[TestMethod]
	public void AThemeParkIncWorldTypesItsFootprintsAndOnlyAReadOnlyVisitDoesNot()
	{
		var previous = GameEditions.Current;
		try
		{
			GameEditions.Select( GameEdition.ThemeParkInc );
			var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			// [BIN:TPI-EXE:0x668552 footprint commit] the twin of tp.exe's commit stamps typed cells, so the slice reads them too; a restored checkpoint layout is stamped again at connect
			Assert.IsTrue( Types( level ) );
			level.TypesFootprintsOverride = false;
			Assert.IsFalse( Types( level ), "the test hook forces untyped cells" );
		}
		finally
		{
			GameEditions.Select( previous );
		}
	}
}
