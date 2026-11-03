using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>RIDES-031: a Theme Park Inc world types its placed footprints like tp.exe's twin commit; only a restored checkpoint layout stays untyped.</summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncTypedFootprintTests
{
	private static bool Types( Level level ) =>
		(bool)typeof( Level ).GetProperty( "TypesFootprints", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( level )!;

	[TestMethod]
	public void APlacedThemeParkIncWorldTypesItsFootprintsAndARestoredLayoutDoesNot()
	{
		var previous = GameEditions.Current;
		try
		{
			GameEditions.Select( GameEdition.ThemeParkInc );
			var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			// [BIN:TPI-EXE:0x668552 footprint commit] the twin of tp.exe's commit stamps typed cells, so the slice reads them too.
			Assert.IsTrue( Types( level ) );
			var cellsField = typeof( Level ).GetField( "incLayoutCells", BindingFlags.Instance | BindingFlags.NonPublic )!;
			cellsField.SetValue( level, new ParkCellMap( 4, 4 ) );
			Assert.IsFalse( Types( level ), "a layout restored from a checkpoint keeps the cells it was given (RIDES-031 residual)" );
		}
		finally
		{
			GameEditions.Select( previous );
		}
	}
}
