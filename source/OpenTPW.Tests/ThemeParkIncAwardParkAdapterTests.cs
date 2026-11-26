using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The level's adapter for Theme Park Inc's award checker (<c>Level.ThemeParkIncAwardPark</c>) over the shipped Science catalogue: the keys it reads and the shape size rule.</summary>
[TestClass]
public class ThemeParkIncAwardParkAdapterTests
{
	private BaseFileSystem? previous;
	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize()
	{
		previous = FileSystem;
		previousEdition = GameEditions.Current;
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous!;
		GameEditions.Select( previousEdition );
	}

	[TestMethod]
	public void TheAdapterReadsTheShippedTypesUiTypeTrackTypeAndShapeSize()
	{
		ThemeParkIncObstacleTests.UseTpiData();
		var catalog = ObjectCatalog.Load( "science" );
		// the Science litter bin (facility 1422): one cell, the facilities' WhichUIType 5, no track
		var bin = Level.ThemeParkIncAwardPark.Attraction( catalog.Get( 1422 ).Settings, open: true, 0, 0 );
		Assert.AreEqual( (5, 0, 1, 1), (bin.WhichUIType, bin.TrackType, bin.ShapeWidth, bin.ShapeHeight) );
		// the track rides answer Bumper.WhichTrackType 1 (karts) and 2 (water), the two the award measures look for (0x5B6D20, 0x5B6FA0)
		var tracks = catalog.Entries.Select( entry => Level.ThemeParkIncAwardPark.Attraction( entry.Settings, open: true, 0, 0 ).TrackType ).ToHashSet();
		Assert.IsTrue( tracks.Contains( 1 ) && tracks.Contains( 2 ), $"track types {string.Join( ", ", tracks.Order() )}" );
		// [BIN:TPI-EXE:0x41AF73 shape size] the widest row's cells without spaces and the row count, for every shipped shape
		foreach ( var entry in catalog.Entries.Where( entry => entry.Settings.Blocks.ContainsKey( "Info.Shape" ) ) )
		{
			var rows = entry.Settings.Blocks["Info.Shape"];
			var (width, height) = Level.ThemeParkIncAwardPark.ShapeSize( entry.Settings );
			Assert.AreEqual( rows.Count, height, entry.ArchivePath );
			Assert.IsTrue( rows.Any( row => row.Count( symbol => symbol != ' ' ) == width ) && rows.All( row => row.Count( symbol => symbol != ' ' ) <= width ), entry.ArchivePath );
		}
	}
}
