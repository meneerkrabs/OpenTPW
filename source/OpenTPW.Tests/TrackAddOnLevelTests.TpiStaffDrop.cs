using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

// Uses the asset-free Level fixture (synthetic objects and their model) with a Theme Park Inc Science economy for the hire (OPENTPW_TPI_PATH).
public partial class TrackAddOnLevelTests
{
	/// <summary>A level with the Science economy's staff, the staff world, a path row along y = 5 and the synthetic objects' catalogue, the placed objects typing their footprints.</summary>
	private Level TpiDropLevel( params ObjectCatalogEntry[] entries )
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		var synthetic = FileSystem;
		ParkEconomy economy;
		try
		{
			FileSystem = new BaseFileSystem( data );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			GameEditions.Select( GameEdition.ThemeParkInc );
			economy = ParkEconomyRuntime.ForThemeParkIncLevel( OriginalPark.Load( "science" ) ).Economy;
		}
		finally
		{
			FileSystem = synthetic;
			GameEditions.Select( GameEdition.ThemeParkWorld );
		}
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), Private, null, new object[] { "jungle", entries }, null )!;
		var objects = new ParkObjects( catalog, new SandboxParkGrid() );
		var grid = new GuestPathGrid( 32, 32 );
		for ( var x = 0; x < 20; x++ )
			grid.SetPath( x, 5, true );
		var guests = new GuestSimulation( grid, GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		economy.Litter.Cells = grid.Cells;
		var runtime = (ParkEconomyRuntime)RuntimeHelpers.GetUninitializedObject( typeof( ParkEconomyRuntime ) );
		typeof( ParkEconomyRuntime ).GetField( "<Economy>k__BackingField", Private )!.SetValue( runtime, economy );
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "<Objects>k__BackingField", objects );
		Set( level, "<Guests>k__BackingField", guests );
		Set( level, "<Park>k__BackingField", runtime );
		Set( level, "<CellTool>k__BackingField", new CellBuildTool() );
		Set( level, "<TpiStaffWorld>k__BackingField", new ThemeParkIncStaffWorld( grid ) { Guests = guests } );
		Set( level, "<TpiPlacingSlot>k__BackingField", -1 );
		objects.ObjectPlaced += item => grid.Cells.WriteFootprint( item.Cells, Level.OwnerWord( item ), item.Placement.Rotation );
		return level;
	}

	private static int TpiHandymanSlot( Level level ) =>
		Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).First( index => level.Park!.Economy.TpiStaff!.Slots[index]?.Role == ThemeParkIncStaffRole.Handyman );

	[TestMethod]
	public void ATpiDropOnAShopsCellMakesTheCleanerOnItsEntranceAndOnAnUnlinkedFeaturesCellEndsSilently()
	{
		// the shop: entrance '2' at (U 1, V 2), its own cell the anchor (0, 0), typed 4
		var shop = Entry( ObjectCategory.Shop, "Info.Id 1160\nInfo.Name SyntheticStall\nInfo.WhichUIType 1\nInfo.IsChoosable 1\n", "*2*", "***", "***" );
		var tree = Entry( ObjectCategory.Feature, "Info.Id 1161\nInfo.Name SyntheticTree\nInfo.WhichUIType 3\nInfo.IsChoosable 1\n", "*" );
		var level = TpiDropLevel( shop, tree );
		var staff = level.Park!.Economy.TpiStaff!;
		var cells = level.Guests!.Grid.Cells;
		var stall = level.Objects.TryPlace( shop, 8, 9, 90, out _ );
		Assert.IsNotNull( stall );
		Assert.AreEqual( 4, cells.RawTypeAt( 8, 9 ), "the shop's own cell is a footprint cell" );
		var slot = TpiHandymanSlot( level );
		Assert.IsNull( level.BeginTpiHire( slot ) );
		// [BIN:TPI-EXE:0x4D2180 drop redirect] a shop (Info.WhichUIType 1, M 2) gives its entrance word +0x46: the anchor plus (1, 2) turned by 90, (2, -1)
		var member = level.DropTpiStaff( 8, 9 );
		Assert.IsNotNull( member, "the shop's cell redirects the drop" );
		Assert.AreEqual( (10, 8), member!.Navigation!.Cell, "he is made on the shop's entrance cell" );
		Assert.AreEqual( -1, level.TpiPlacingSlot );
		// any other cell of the footprint holds no thing: the ladder's type test refuses it with the message
		Assert.IsNull( level.BeginTpiHire( TpiHandymanSlot( level ) ) );
		Assert.IsNull( level.DropTpiStaff( 9, 9 ) );
		Assert.IsFalse( level.LastTpiDropSilent );

		// [BIN:TPI-EXE:0x5E9320 drop fallback] a feature (M 4) with no path beside it: the word is 0 and the drop ends without the message, the candidate in the hand
		var feature = level.Objects.TryPlace( tree, 4, 12, 0, out _ );
		Assert.IsNotNull( feature );
		Assert.IsNull( level.DropTpiStaff( 4, 12 ) );
		Assert.IsTrue( level.LastTpiDropSilent );
		Assert.IsTrue( level.TpiPlacingSlot >= 0 && staff.IsPlacing( level.TpiPlacingSlot ) );
		// one beside it on the path row: he is made there, and that cell's queue link points from the tree to it
		var bush = level.Objects.TryPlace( tree, 6, 6, 0, out _ );
		Assert.IsNotNull( bush );
		var cleaner = level.DropTpiStaff( 6, 6 );
		Assert.IsNotNull( cleaner );
		Assert.AreEqual( (6, 5), cleaner!.Navigation!.Cell );
		Assert.AreEqual( 0x10, cells.QueueLinkAt( 6, 5 ) );
	}
}
