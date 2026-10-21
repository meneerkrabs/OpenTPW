using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

// Uses the existing asset-free Level fixture and its isolated filesystem, entities and logger.
public partial class TrackAddOnLevelTests
{
	private (Level Level, ObjectCatalogEntry Entry) CoasterMarkerLevel()
	{
		var sam = "Info.Id 1170\nInfo.Name SyntheticCoaster\nInfo.WhichUIType 0\nInfo.IsChoosable 1\nBumper.WhichTrackType 3\n";
		sam += string.Join( "\n", CoasterTests.PcSettings().Select( pair => $"{pair.Key} {pair.Value}" ) );
		var entry = Entry( ObjectCategory.Ride, sam, "<***>" );
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), Private, null, new object[] { "jungle", new[] { entry } }, null )!;
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "<Objects>k__BackingField", new ParkObjects( catalog, new SandboxParkGrid() ) );
		Set( level, "<Guests>k__BackingField", new GuestSimulation( new GuestPathGrid( 48, 32 ), GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false } );
		Set( level, "coasterBuilders", new Dictionary<OriginalObject, CoasterBuilder>() );
		Set( level, "demolishedQueues", new HashSet<OriginalObject>() );
		typeof( Level ).GetMethod( "SetupCoasters", Private )!.Invoke( level, null );
		// Exercise the real registration order, not copies of its footprint/binding lambdas.
		typeof( Level ).GetMethod( "ConnectObjectsToGuests", Private )!.Invoke( level, null );
		return (level, entry);
	}

	[TestMethod]
	public void LevelWithoutCoastersDoesNotRequireCoasterSetupForCellBinding()
	{
		var (level, _) = CoasterMarkerLevel();
		Set( level, "coasterBuilders", null );
		Set( level, "coasterCellsReady", false );
		// Existing asset-free non-coaster fixtures skip the constructor and SetupCoasters.
		typeof( Level ).GetMethod( "ConnectObjectsToGuests", Private )!.Invoke( level, null );
		Assert.AreEqual( true, typeof( Level ).GetField( "coasterCellsReady", Private )!.GetValue( level ) );
		Assert.AreEqual( 0, level.Coasters.Count );
	}

	[TestMethod]
	public void LiveCoasterWithMissingBuilderStateIsNotSilentlyIgnored()
	{
		var (level, entry) = CoasterMarkerLevel();
		var station = level.Objects.TryPlace( entry, 20, 16, 0, out _ );
		Assert.IsNotNull( station?.Runtime.Coaster );
		Set( level, "coasterBuilders", null );
		var failure = Assert.ThrowsException<System.Reflection.TargetInvocationException>( () =>
			typeof( Level ).GetMethod( "BindCoasterCellsAfterLoad", Private )!.Invoke( level, null ) );
		Assert.IsInstanceOfType( failure.InnerException, typeof( InvalidOperationException ) );
	}

	[TestMethod]
	public void LevelPlacementBindsAfterFootprintsAndRemovalClearsBeyondEnds()
	{
		var (level, entry) = CoasterMarkerLevel();
		var station = level.Objects.TryPlace( entry, 20, 16, 0, out _ );
		Assert.IsNotNull( station?.Runtime.Coaster );
		var builder = level.GetCoasterBuilder( station! )!;
		Assert.IsNotNull( builder.Track.CellMarkers );
		var cells = level.Guests!.Grid.Cells;
		var beyond = station!.Cells.Where( cell => cell.Cell.CellType == ObjectShapeSymbols.TypeTrackEnd )
			.Select( cell => (cell.X, cell.Y, D: GuestPathGrid.LinkDirection( cell.Cell.Direction )) )
			.Select( cell => (X: cell.X + GuestPathGrid.Directions[cell.D].DX, Y: cell.Y + GuestPathGrid.Directions[cell.D].DY) ).ToArray();
		Assert.AreEqual( 2, beyond.Length );
		foreach ( var cell in beyond ) Assert.AreEqual( (byte)21, cells.RawTypeAt( cell.X, cell.Y ) );
		builder.Delete();
		foreach ( var cell in beyond ) Assert.AreEqual( (byte)21, cells.RawTypeAt( cell.X, cell.Y ), "ordinary deletion retains station ends" );
		level.Objects.Remove( station );
		foreach ( var cell in beyond ) Assert.AreEqual( (byte)0, cells.RawTypeAt( cell.X, cell.Y ), "runtime removal releases the lease after footprint writes" );
	}

	[TestMethod]
	public void LevelRestoreHelpersRebindDerivedMarkersAfterEmptyTrackRestore()
	{
		var (level, entry) = CoasterMarkerLevel();
		var station = level.Objects.TryPlace( entry, 20, 16, 0, out _ )!;
		var builder = level.GetCoasterBuilder( station )!;
		builder.Place( 28, 20 );
		Assert.IsTrue( builder.Track.Nodes.Any( node => node.X == 28 && node.Y == 20 ) );
		var saved = level.CaptureCoastersSnapshot();
		var cells = level.Guests!.Grid.Cells;
		var markers = Enumerable.Range( 0, cells.Width * cells.Height )
			.Select( index => (X: index % cells.Width, Y: index / cells.Width) )
			.Where( cell => cells.RawTypeAt( cell.X, cell.Y ) == 21 )
			.Select( cell => (cell.X, cell.Y, Owner: cells.OwnerAt( cell.X, cell.Y ), Flags: cells.FlagsAt( cell.X, cell.Y )) ).ToArray();
		builder.Track.DetachCells();
		Set( level, "coasterCellsReady", false );
		level.RestoreCoastersSnapshot( saved );
		Assert.IsNull( builder.Track.CellMarkers, "coaster restore cannot bind ahead of the secondary layer" );
		level.RestoreTracksSnapshot( null );
		foreach ( var cell in markers )
		{
			cells.SetType( cell.X, cell.Y, (byte)21 ); cells.SetOwner( cell.X, cell.Y, cell.Owner ); cells.SetFlags( cell.X, cell.Y, cell.Flags );
		}
		var stamp = cells.StampCounter;
		typeof( Level ).GetMethod( "BindCoasterCellsAfterLoad", Private )!.Invoke( level, null );
		Assert.IsNotNull( builder.Track.CellMarkers!.TokenAt( 28, 20 ) );
		Assert.AreEqual( stamp, cells.StampCounter, "restored markers only acquire derived tokens" );
		builder.Delete();
		Assert.AreEqual( (byte)0, cells.RawTypeAt( 28, 20 ) );
	}
}
