using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The shipped sideshows and toilets whose <c>UsageInfo.RideHandlesSprite</c> is 1 (inconclusive without OPENTPW_GAME_PATH), run with their original scripts: tp.exe's state-16 entry keeps the body of
/// every such object (0x0050212B tests no object kind), and the guest update leaves it where it is while +0x28 is set (0x004FA087). Their scripts do with the kept body only what the walk table does
/// (WALKON, WALKOFF, WALKGET); the others leave it standing where it stood. Every use keeps the guest in view through the whole use, except while a hidden walk action (1) has the body deleted on board.
/// </summary>
[TestClass]
[DoNotParallelize]
public class KeptBodyAssetTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private BaseFileSystem? previousFiles;

	[TestInitialize]
	public void Init()
	{
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original sideshows and toilets." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		previousFiles = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( previousFiles != null )
			FileSystem = previousFiles;
	}

	/// <summary>How a kept body is drawn on one tick of its use.</summary>
	private enum View { InPlace, Walked, Held, HiddenOnBoard, Lost }

	/// <summary>
	/// Each object's kept body over its uses: stands in place (no WALKON), walked by the walk table, or held where the walk left it; the walk action 1 (Arcade, Super Toilet) deletes it on board
	/// (0x004F9ED0) and the walk-off draws it again.
	/// </summary>
	[DataTestMethod]
	[DataRow( "jungle", 1402, "Small Toilet", false, false )]
	[DataRow( "jungle", 1307, "Giant Puzzle", false, false )]
	[DataRow( "jungle", 1304, "Laughing Hyenas", true, false )]
	[DataRow( "jungle", 1303, "Jungle Spray", true, false )]
	[DataRow( "jungle", 1305, "Strength Bird", true, false )]
	[DataRow( "fantasy", 4405, "Small Toilet", false, false )]
	[DataRow( "fantasy", 4406, "Super Toilet", true, true )]
	[DataRow( "fantasy", 4303, "Strength Flower", false, false )]
	[DataRow( "fantasy", 4304, "Fruit Shy", true, false )]
	[DataRow( "fantasy", 4301, "Aqua Spray", true, false )]
	[DataRow( "hallow", 2305, "Giant Puzzle", false, false )]
	[DataRow( "hallow", 2303, "Strength Skull", true, false )]
	[DataRow( "hallow", 2306, "Arcade", true, true )]
	[DataRow( "space", 3402, "Small Toilet", false, false )]
	[DataRow( "space", 3303, "Ufo Blaster", false, false )]
	[DataRow( "space", 3301, "Giant Puzzle", false, false )]
	[DataRow( "space", 3300, "Martian Mooners", true, false )]
	public void AKeptBodyStaysInViewThroughTheWholeUse( string theme, int infoId, string name, bool walks, bool hidesOnBoard )
	{
		var park = OriginalPark.Load( theme, readShippedSave: false );
		var entry = ObjectCatalog.Load( theme ).Get( infoId );
		Assert.AreEqual( name, entry.SettingsName );
		var simulation = new GuestSimulation( GuestPathGrid.FromOriginal( park.Map, null ), GuestSettings.Load( theme ), 7 ) { ArrivalsEnabled = false };
		var grid = simulation.Grid;
		var cells = grid.Cells;
		var site = Enumerable.Range( 0, grid.CountX * grid.CountY ).Select( index => (X: index % grid.CountX, Y: index / grid.CountX) ).First( anchor =>
			ObjectFootprint.GetCells( entry.Shape, anchor.X, anchor.Y, 0 ).All( cell => cells.InBounds( cell.X, cell.Y ) && cells.RawTypeAt( cell.X, cell.Y ) == 0 && !grid.IsWalkable( cell.X, cell.Y ) )
			&& ObjectFootprint.GetAccessPoints( entry.Shape, anchor.X, anchor.Y, 0 ).All( point => grid.IsWalkable( point.OutsideX, point.OutsideY ) ) );
		var owner = ParkCellMap.CellWord( site.X, site.Y );
		var points = ObjectFootprint.GetAccessPoints( entry.Shape, site.X, site.Y, 0 ).ToArray();
		cells.WriteFootprint( ObjectFootprint.GetCells( entry.Shape, site.X, site.Y, 0 ), owner, 0 );
		ObjectAccessCells.Lay( cells, points, owner, entry.HasQueue );
		var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: null, open: true );
		Assert.IsTrue( Level.RegisterWithGuests( runtime, points, simulation, 0, owner, ownExitCell: true ) );
		var bridge = runtime.Visitors;
		Assert.IsTrue( bridge.RideHandlesSprite, $"{name}: UsageInfo.RideHandlesSprite 1" );
		var guests = Enumerable.Range( 0, 6 ).Select( _ => simulation.SpawnInPark( bridge.EntranceCell.X, bridge.EntranceCell.Y ) ).ToList();
		foreach ( var guest in guests )
		{
			guest.AttractionId = bridge.AttractionId;
			guest.State = GuestState.GoingToRide;
			guest.Money = 1000;
		}
		var poses = new Dictionary<int, BounceRiderPose>();
		var placed = new Dictionary<int, BounceRiderPose>();
		var scratch = new List<RideVisitorBridge.WalkerView>();
		var counts = new Dictionary<View, int>();
		var walkers = new List<RideVisitorBridge.WalkerView>();
		for ( var tick = 0; tick < 6 * 60 * 60; tick++ )
		{
			simulation.Tick( Tick );
			runtime.Simulate( Tick );
			// the host's body-sprite phase (Level.Simulation): the players run on the fixed host clock, so a requested body has its output
			simulation.SetBodySpriteClock( GuestSimulation.BodySpriteSampleAtTick( tick + 1 ) );
			simulation.ScheduleBodySprites();
			// the host's pose phase, as FlatRideBounceRiders.Prepare runs it after the substeps
			poses.Clear();
			FlatRideBounceRiders.CollectWalkers( poses, runtime, node => ObjectPlacement.SwapAxes( runtime.Animator.NodeTransforms[node] ), scratch,
				( guest, template ) => simulation.RequestRideBody( guest, bridge.AttractionId, (VisitorSpriteTemplate)template ),
				guest => simulation.HideRideBody( guest, bridge.AttractionId ) );
			FlatRideBounceRiders.HoldKeptBodies( poses, placed, simulation );
			walkers.Clear();
			bridge.CollectWalkers( walkers );
			foreach ( var guest in guests )
			{
				if ( guest.State != GuestState.Using || guest.AttractionId != bridge.AttractionId )
					continue;
				var walker = walkers.FirstOrDefault( item => item.Guest == guest.Id );
				View view;
				if ( poses.TryGetValue( guest.Id, out var pose ) && pose.Visible )
					view = walker.Guest == guest.Id ? View.Walked : View.Held;
				else if ( simulation.ShowsKeptBody( guest ) )
					view = View.InPlace;
				else
					view = walker.Guest == guest.Id && walker.State == 2 && WalkerBodies.HidesOnBoard( walker.Mode ) ? View.HiddenOnBoard : View.Lost;
				counts[view] = counts.GetValueOrDefault( view ) + 1;
			}
		}
		Assert.AreNotEqual( RideVMState.Faulted, runtime.Script!.State, runtime.Script.FaultMessage );
		var summary = string.Join( ", ", counts.OrderBy( pair => pair.Key ).Select( pair => $"{pair.Key} {pair.Value}" ) );
		Assert.IsTrue( bridge.BoardedTotal >= 3, $"{name}: {bridge.BoardedTotal} used it ({summary})" );
		Assert.IsTrue( bridge.ReleasedTotal >= bridge.BoardedTotal - 1, $"{name}: {bridge.ReleasedTotal} of {bridge.BoardedTotal} let off" );
		Assert.AreEqual( 0, counts.GetValueOrDefault( View.Lost ), $"{name}: the kept body is in view through every use ({summary})" );
		Assert.AreEqual( walks, counts.GetValueOrDefault( View.Walked ) > 0, $"{name}: walked by the walk table ({summary})" );
		Assert.AreEqual( !walks, counts.GetValueOrDefault( View.InPlace ) > 0 && counts.GetValueOrDefault( View.Walked ) == 0, $"{name}: stands where it stood ({summary})" );
		Assert.AreEqual( hidesOnBoard, counts.GetValueOrDefault( View.HiddenOnBoard ) > 0, $"{name}: the hidden walk action 1 deletes the body on board ({summary})" );
	}
}
