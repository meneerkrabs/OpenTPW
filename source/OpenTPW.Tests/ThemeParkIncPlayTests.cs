using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// The experimental Theme Park Inc slice (docs/THEME-PARK-INC.md): the edition capability policy on synthetic data,
/// and the supported gameplay route over the original retail corpus when <c>OPENTPW_TPI_PATH</c> names it. The route is
/// the headless composition the slice drives — the build rules of <see cref="ParkObjects.Check"/>, an
/// <see cref="OriginalObjectRuntime"/> with its original script, a free <see cref="ParkPathBuilder"/> (no economy) and
/// <see cref="GuestSimulation"/> — composed like the M3 gate and the determinism tests, because placed
/// <see cref="OriginalObject"/>s build GPU models. None of this compares with the original game; it checks that the
/// slice's own systems keep working on Theme Park Inc data.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncPlayTests
{
	private BaseFileSystem? previous;

	[TestCleanup]
	public void Cleanup()
	{
		if ( previous != null )
			FileSystem = previous;
		previous = null;
		GameEditions.Select( GameEdition.ThemeParkWorld );
	}

	// ---- Capability policy (synthetic; no corpus needed) ---------------------------------

	[TestMethod]
	public void ThemeParkWorldRunsTheFullGame()
	{
		GameEditions.Select( GameEdition.ThemeParkWorld );
		var capabilities = GameEditions.Capabilities;
		Assert.IsTrue( capabilities.Economy && capabilities.OriginalHud && capabilities.Building && capabilities.Guests
			&& capabilities.Staff && capabilities.Advisor && capabilities.FrontEnd && capabilities.ParkSaves && capabilities.Sharing,
			"every implemented system runs for Theme Park World" );
		Assert.IsFalse( capabilities.ExperimentalHud, "the experimental HUD is the Theme Park Inc slice's" );
		Assert.IsNull( capabilities.ExperimentalNote, "the full game needs no experimental caveat" );
	}

	[TestMethod]
	public void TheSliceGivesBuildingAndGuestsAndRefusesTheUntracedSystems()
	{
		GameEditions.Select( GameEdition.ThemeParkInc );
		var capabilities = GameEditions.Capabilities;
		Assert.IsTrue( capabilities.ExperimentalHud && capabilities.Building && capabilities.Guests, "the slice builds and routes guests" );
		Assert.IsFalse( capabilities.Economy, "Theme Park Inc Standard.sam lacks 89 of the balance keys the economy reads" );
		Assert.IsFalse( capabilities.OriginalHud && capabilities.Staff && capabilities.Advisor && capabilities.FrontEnd
			&& capabilities.ParkSaves && capabilities.Sharing, "the untraced systems stay refused" );
		var note = capabilities.ExperimentalNote;
		Assert.IsNotNull( note );
		StringAssert.Contains( note, "experimental" );
		foreach ( var refused in new[] { "economy", "staff", "advisor", "missions", "Global Master Save", "COS", "saves", "sharing" } )
			StringAssert.Contains( note, refused, $"the note names the refused '{refused}'" );
		StringAssert.Contains( GameEditions.SliceNotice, "not verified" );
	}

	[TestMethod]
	public void StartupRefusesSaveAndSharingCommandsOnThemeParkIncData()
	{
		GameEditions.Select( GameEdition.ThemeParkInc );
		Assert.IsNull( GameEditions.ViewerRefusal( new[] { "--load-original-level", "science", "--capture-world", "a.png" } ),
			"the slice itself starts (captures included)" );
		foreach ( var refused in new[] { "--park-resume-smoke", "--export-park", "--import-park", "--visit-park", "--sandbox", "--smoke-test", "--front-end", "--setup" } )
		{
			var args = new[] { "--load-original-level", "science", refused, "value" };
			var message = GameEditions.ViewerRefusal( args );
			Assert.IsNotNull( message, $"{refused} is refused on Theme Park Inc data" );
			StringAssert.Contains( message, refused, $"the refusal names {refused}" );
			StringAssert.Contains( message, "experimental slice" );
		}
	}

	// ---- Supported gameplay route over the corpus ----------------------------------------

	private string UseTpiData()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		previous = FileSystem;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		ObjectCatalog.BonusDataRoot = null;
		return data;
	}

	/// <summary>
	/// The composition the slice drives, without the economy and without GPU models: the world's paths (its prebuilt
	/// save in science, its MAP initial path elsewhere), its catalog under the build rules, a free path builder and the
	/// guest simulation over one shared script world.
	/// </summary>
	private sealed class SlicePark
	{
		public readonly OriginalPark Park;
		public readonly ObjectCatalog Catalog;
		public readonly OriginalParkGrid BuildGrid;
		public readonly GuestPathGrid Grid;
		public readonly GuestSimulation Guests;
		public readonly ParkPathBuilder Paths;
		public readonly RideScriptWorld Scripts = new();
		/// <summary>Cells the world's own records hold (placed objects and fixed items of a prebuilt save).</summary>
		public readonly HashSet<(int X, int Y)> Occupied = new();

		public SlicePark( string world, int? arrivalTimeBetween = null )
		{
			Park = OriginalPark.Load( world );
			Catalog = ObjectCatalog.Load( world );
			BuildGrid = new OriginalParkGrid( Park );
			Grid = GuestPathGrid.FromOriginal( Park.Map, Park.Save );
			var settings = GuestSettings.Load( world );
			if ( arrivalTimeBetween is int interval )
				settings.ArrivalTimeBetween = interval;
			Guests = new GuestSimulation( Grid, settings, Level.GuestSeed );
			Paths = new ParkPathBuilder( Grid.Cells, Grid, () => null, BuildGrid, ( x, y ) => Occupied.Contains( (x, y) ) );
			if ( Park.Save != null )
				foreach ( var record in Park.Save.PlacedObjects )
					for ( var y = record.MinY; y <= record.MaxY; y++ )
						for ( var x = record.MinX; x <= record.MaxX; x++ )
							Occupied.Add( (x, y) );
		}

		public bool IsBlocked( int x, int y ) => Occupied.Contains( (x, y) ) || Grid.IsWalkable( x, y ) || Grid.IsQueue( x, y );

		/// <summary>One simulation step of the slice's frame (Level.AdvanceSimulation without the economy systems).</summary>
		public void Step( double deltaSeconds, params OriginalObjectRuntime[] runtimes )
		{
			Guests.Tick( (float)deltaSeconds );
			foreach ( var runtime in runtimes )
				runtime.Simulate( deltaSeconds );
		}
	}

	[TestMethod]
	public void ScienceBuildsPlacesAndRemovesACatalogObjectWithoutAnEconomy()
	{
		UseTpiData();
		var park = new SlicePark( "science" );
		Assert.IsTrue( park.Grid.WalkableCount > 0, "the prebuilt save's paths are walkable" );
		// A buildable ride with a queue, placed through the HUD build flow's rules beside an existing path.
		var entry = park.Catalog.Buildable.First( candidate => candidate.HasQueue && candidate.ScriptPath != null );
		var placed = PlaceBesidePath( park, entry, out var anchor, out var rotation );
		Assert.IsNotNull( placed, $"{entry} has no buildable site beside the imported paths" );
		Assert.AreNotEqual( 0, placed!.Visitors.AttractionId );
		Assert.IsTrue( park.Guests.Attractions.Contains( placed.Visitors ), "the placed ride registers with the guests" );
		// The placed object and the guests advance together for ten simulated seconds; the script must not fault.
		for ( var step = 0; step < 10 * FixedStepClock.TicksPerSecond; step++ )
			park.Step( FixedStepClock.TickDuration, placed );
		Assert.AreNotEqual( RideVMState.Faulted, placed.Script?.State, placed.Script?.FaultMessage );
		// The remove flow's guest side (Level wires this to Objects.ObjectRemoved): the attraction unregisters.
		park.Guests.Unregister( placed.Visitors );
		Assert.IsFalse( park.Guests.Attractions.Contains( placed.Visitors ), "a removed object unregisters with the guests" );
		// Its cells are buildable again once the footprint is freed.
		park.Occupied.ExceptWith( ObjectFootprint.GetCells( entry.Shape, anchor.X, anchor.Y, rotation ).Select( cell => (cell.X, cell.Y) ) );
		Assert.AreEqual( OriginalPlacementResult.Allowed, ParkObjects.Check( park.BuildGrid, park.IsBlocked, entry, anchor.X, anchor.Y, rotation ),
			"the freed site builds again" );
	}

	[TestMethod]
	public void EveryWorldHasABuildableQueueRideBesideItsPaths()
	{
		UseTpiData();
		foreach ( var world in new[] { "arabian", "science", "water" } )
		{
			var park = new SlicePark( world );
			var entry = park.Catalog.Buildable.FirstOrDefault( candidate => candidate.ScriptPath != null && candidate.HasQueue );
			Assert.IsNotNull( entry, $"{world}: the catalog offers a buildable ride with a queue" );
			var placed = PlaceBesidePath( park, entry!, out _, out _ );
			Assert.IsNotNull( placed, $"{world}: {entry} builds beside a path under the world's own terrain rules" );
		}
	}

	[TestMethod]
	public void PathsLayFreeWithoutAnEconomy()
	{
		UseTpiData();
		var park = new SlicePark( "science" );
		// Extend the imported paths by a straight line somewhere: without an economy nothing is charged.
		var line = StraightExtension( park );
		Assert.IsNotNull( line, "a buildable line exists beside the imported paths" );
		var (start, end) = line!.Value;
		var before = park.Grid.WalkableCount;
		var result = park.Paths.BuildSegment( start, end );
		Assert.IsTrue( result.Completed, $"the free builder lays from ({start.X},{start.Y}) to ({end.X},{end.Y}): {result.StoppedBy}" );
		Assert.AreEqual( 0, result.Charged, "without an economy nothing is charged" );
		Assert.AreEqual( before + result.Built.Count, park.Grid.WalkableCount, "the built cells are walkable" );
		// Removal through the remove tool's route clears a built cell again.
		var built = result.Built[0];
		Assert.AreEqual( CellBuildResult.Ok, park.Paths.Remove( built.X, built.Y ) );
		Assert.AreNotEqual( ParkCellType.Path, park.Grid.Cells.TypeAt( built.X, built.Y ) );
	}

	/// <summary>A straight path extension of at least two cells off an imported path cell, in any direction.</summary>
	private static ((int X, int Y) Start, (int X, int Y) End)? StraightExtension( SlicePark park )
	{
		for ( var y = 0; y < park.Grid.CountY; y++ )
		{
			for ( var x = 0; x < park.Grid.CountX; x++ )
			{
				if ( park.Grid.Cells.TypeAt( x, y ) != ParkCellType.Path )
					continue;
				foreach ( var (dx, dy) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) } )
				{
					var end = (X: x + dx, Y: y + dy);
					var length = 0;
					while ( park.Grid.Cells.InBounds( end.X, end.Y ) && park.Grid.Cells.TypeAt( end.X, end.Y ) == ParkCellType.Empty
						&& park.Paths.CheckCell( end.X, end.Y ) == CellBuildResult.Ok )
					{
						length++;
						end = (end.X + dx, end.Y + dy);
					}
					if ( length >= 2 )
						return ((x, y), (end.X - dx, end.Y - dy));
				}
			}
		}
		return null;
	}

	[TestMethod]
	public void GuestsArriveAndWalkInTheThemeParkIncSciencePark()
	{
		UseTpiData();
		// Arrivals sped up as in the M3 gate's ArrivalTimeBetween option; the slice runs the same guest systems.
		var park = new SlicePark( "science", arrivalTimeBetween: 2 );
		Assert.IsTrue( park.Grid.WalkableCount > 0, "the prebuilt save's paths are walkable" );
		for ( var step = 0; step < 90 * FixedStepClock.TicksPerSecond && park.Guests.GetStatistics().InPark == 0; step++ )
			park.Step( FixedStepClock.TickDuration );
		var statistics = park.Guests.GetStatistics();
		Assert.IsTrue( statistics.InPark > 0, "guests arrive from the bus stop through the ticket booth" );
		for ( var step = 0; step < 10 * FixedStepClock.TicksPerSecond; step++ )
			park.Step( FixedStepClock.TickDuration );
		statistics = park.Guests.GetStatistics();
		Assert.IsTrue( statistics.Walking + statistics.Queueing + statistics.OnRides > 0,
			$"the arrived guests move through the park ({statistics.Walking} walking, {statistics.Queueing} queueing, {statistics.OnRides} on rides)" );
	}

	/// <summary>Places <paramref name="entry"/> through the HUD build flow's rules (Level.PlaceObject without the purchase) beside a walkable path cell.</summary>
	private static OriginalObjectRuntime? PlaceBesidePath( SlicePark park, ObjectCatalogEntry entry, out (int X, int Y) anchor, out int rotation )
	{
		// The HUD centres the footprint on the clicked cell, so the click sits on free ground beside a path (the
		// object's own entrance cell must still open onto one, which RegisterWithGuests enforces).
		var clicks = CellsNearPaths( park );
		foreach ( var (x, y) in clicks )
		{
			foreach ( var turn in new[] { 0, 90, 180, 270 } )
			{
				(anchor, rotation) = (Level.GetCentredAnchor( entry, x, y, turn ), turn);
				if ( ParkObjects.Check( park.BuildGrid, park.IsBlocked, entry, anchor.X, anchor.Y, rotation ) != OriginalPlacementResult.Allowed )
					continue;
				var runtime = new OriginalObjectRuntime( entry, park.Scripts, open: true );
				var points = ObjectFootprint.GetAccessPoints( entry.Shape, anchor.X, anchor.Y, rotation );
				if ( Level.RegisterWithGuests( runtime, points, park.Guests, rotation ) || !runtime.IsAttraction )
				{
					foreach ( var cell in ObjectFootprint.GetCells( entry.Shape, anchor.X, anchor.Y, rotation ) )
						park.Occupied.Add( (cell.X, cell.Y) );
					return runtime;
				}
				runtime.Stop();
			}
		}
		anchor = default;
		rotation = 0;
		return null;
	}

	/// <summary>Free cells whose Chebyshev distance to a walkable path cell is 1 to 4 (a click beside the paths).</summary>
	private static IEnumerable<(int X, int Y)> CellsNearPaths( SlicePark park )
	{
		var paths = new List<(int X, int Y)>();
		for ( var y = 0; y < park.Grid.CountY; y++ )
			for ( var x = 0; x < park.Grid.CountX; x++ )
				if ( park.Grid.IsWalkable( x, y ) )
					paths.Add( (x, y) );
		for ( var y = 0; y < park.Grid.CountY; y++ )
			for ( var x = 0; x < park.Grid.CountX; x++ )
		{
			if ( park.Grid.IsWalkable( x, y ) || park.Grid.IsQueue( x, y ) || park.Occupied.Contains( (x, y) ) )
				continue;
			if ( paths.Any( path => Math.Max( Math.Abs( path.X - x ), Math.Abs( path.Y - y ) ) is >= 1 and <= 4 ) )
				yield return (x, y);
		}
	}
}
