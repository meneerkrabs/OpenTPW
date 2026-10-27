using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The queue cells' models: the chooser's class-2 result (tp.exe <c>0x535DD0</c>, table <c>0x7630B0</c>) and the theme's queue archives.</summary>
[TestClass]
public class QueueModelTests
{
	private static (GuestPathGrid Grid, RideVisitorBridge Ride) Queue( int cells, bool connect )
	{
		var grid = new GuestPathGrid( 12, 12 );
		for ( var x = 0; x < 10; x++ )
			grid.SetPath( x, 0, true );
		var ride = new RideVisitorBridge( 1, "Queue ride", RideVisitorKind.Ride, 5, 55, 30 )
		{
			EntranceCell = (2, 0),
			ExitCell = (2, 0),
			QueueFrontCell = (2, 5),
			QueueEntranceDirection = 2,
			Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 )
		};
		Assert.AreEqual( cells, QueuePaths.TryExtend( grid, ride, Enumerable.Range( 0, cells ).Select( index => (2, 5 - index) ) ) );
		if ( connect )
			Assert.IsTrue( QueuePaths.TryConnect( grid, ride ) );
		return (grid, ride);
	}

	[TestMethod]
	public void AStraightQueueHasStraightsAndAnEndAtThePath()
	{
		var (grid, _) = Queue( 5, connect: true );
		Assert.AreEqual( new QueueModelTile( 2, 0 ), QueueModels.Choose( grid.Cells, 2, 5 ), "the front links to the entrance and the next cell: questra" );
		Assert.AreEqual( new QueueModelTile( 2, 0 ), QueueModels.Choose( grid.Cells, 2, 3 ) );
		Assert.AreEqual( new QueueModelTile( 5, 0 ), QueueModels.Choose( grid.Cells, 2, 1 ), "the back linked to a path linking back: questra + 3, queend" );
		Assert.AreEqual( "queend", QueueModels.Names[5] );
	}

	[TestMethod]
	public void AnUnconnectedBackIsADeadEnd()
	{
		var (grid, _) = Queue( 4, connect: false );
		Assert.AreEqual( new QueueModelTile( 1, 0 ), QueueModels.Choose( grid.Cells, 2, 2 ), "one link, 0x10: row (1, 0)" );
		Assert.AreEqual( "quedead", QueueModels.Names[1] );
	}

	[TestMethod]
	public void AStraightTurnsHalfWayRoundUnlessItsQueueLinkIsPlusXOrPlusY()
	{
		var cells = new ParkCellMap( 8, 8 );
		cells.SetType( 3, 3, ParkCellType.Queue );
		cells.SetLinks( 3, 3, 0x11 );
		cells.SetQueueLink( 3, 3, 0x01 );
		Assert.AreEqual( new QueueModelTile( 2, 180 ), QueueModels.Choose( cells, 3, 3 ), "rotation 0 + 180, yaw 360 − 180" );
		cells.SetLinks( 3, 3, 0x44 );
		cells.SetQueueLink( 3, 3, 0x04 );
		Assert.AreEqual( new QueueModelTile( 2, 90 ), QueueModels.Choose( cells, 3, 3 ), "rotation 90 + 180 = 270, yaw 90" );
		cells.SetQueueLink( 3, 3, 0x40 );
		Assert.AreEqual( new QueueModelTile( 2, 270 ), QueueModels.Choose( cells, 3, 3 ), "rotation 90, yaw 270" );
	}

	[TestMethod]
	public void ACornerTakesTheBendOfItsQueueLink()
	{
		var cells = new ParkCellMap( 8, 8 );
		cells.SetType( 3, 3, ParkCellType.Queue );
		cells.SetLinks( 3, 3, 0x14 );
		cells.SetQueueLink( 3, 3, 0x10 );
		Assert.AreEqual( new QueueModelTile( 4, 0 ), QueueModels.Choose( cells, 3, 3 ), "0x10 with 0x14: the next model, quebnd1" );
		cells.SetQueueLink( 3, 3, 0x04 );
		Assert.AreEqual( new QueueModelTile( 3, 0 ), QueueModels.Choose( cells, 3, 3 ), "quebnd2" );
		cells.SetLinks( 3, 3, 0x41 );
		cells.SetQueueLink( 3, 3, 0x01 );
		Assert.AreEqual( new QueueModelTile( 4, 180 ), QueueModels.Choose( cells, 3, 3 ), "row (3, 180), the next model" );
		// A corner at the path: + 3.
		cells.SetType( 3, 2, ParkCellType.Path );
		cells.SetLinks( 3, 2, 0x10 );
		Assert.AreEqual( new QueueModelTile( 7, 180 ), QueueModels.Choose( cells, 3, 3 ), "quebin2" );
		cells.SetLinks( 3, 2, 0x00 );
		Assert.AreEqual( new QueueModelTile( 4, 180 ), QueueModels.Choose( cells, 3, 3 ), "a path that does not link back adds nothing" );
	}

	[TestMethod]
	public void QueueTextureLookupUsesTheQueueArchive()
	{
		string[] Files( string directory ) => directory switch
		{
			"/levels/jungle/queue/textures" => new[] { "/levels/jungle/queue/textures/jpa_que1.wct" },
			"/levels/jungle/terrain/pathtex" => new[] { "/levels/jungle/terrain/pathtex/jpa_str1.wct" },
			_ => Array.Empty<string>()
		};
		Assert.AreEqual( "/levels/jungle/queue/textures/jpa_que1.wct", OriginalTerrain.FindTexture( "/levels/jungle/queue", "jungle", "jpa_que1", "textures", Files ) );
		Assert.IsNull( OriginalTerrain.FindTexture( "/levels/jungle/terrain", "jungle", "jpa_que1", "pathtex", Files ) );
	}

	[TestMethod]
	public void TheDrawnGroundUnderAPointFollowsTheCornersAndTheEdgeOfTheMap()
	{
		var heights = new float[9];
		for ( var index = 0; index < heights.Length; index++ )
			heights[index] = index * 4f; // a ramp: every corner differs
		var field = new ModelHeightfield( 2, 2, 10, 10, 0, 0, Array.Empty<uint>(), heights, new uint[4] );
		for ( var corner = 0; corner < 4; corner++ )
		{
			var (cx, cy) = (corner % 2, corner / 2);
			Assert.AreEqual( field.GetCornerHeight( cx, cy ), OriginalTerrain.GroundUnder( field, cx * 10, cy * 10 ), 1e-4f, $"corner {(cx, cy)}" );
		}
		var mean = (field.GetCornerHeight( 0, 0 ) + field.GetCornerHeight( 1, 0 ) + field.GetCornerHeight( 0, 1 ) + field.GetCornerHeight( 1, 1 )) / 4;
		Assert.AreNotEqual( mean, OriginalTerrain.GroundUnder( field, 2.2f, 9 ), "a post near the cell's high edge stands above the cell's mean height" );
		Assert.AreEqual( OriginalTerrain.GroundUnder( field, 20 - 0.001f, 5 ), OriginalTerrain.GroundUnder( field, 20.7f, 5 ), 1e-4f, "a vertex past the map's edge takes the edge's ground" );
	}

	[DataTestMethod]
	[DataRow( "jungle" )]
	[DataRow( "hallow" )]
	[DataRow( "fantasy" )]
	[DataRow( "space" )]
	public void EveryThemeShipsTheQueueModelsOnOneCell( string theme )
	{
		var path = Path.Combine( OriginalParkImportTests.OriginalDataPath(), "levels", theme, "queue.wad" );
		if ( !File.Exists( path ) ) Assert.Inconclusive( "Original game data is not installed." );
		using var archive = new WadArchive( path );
		var files = archive.GetFiles( "" ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		foreach ( var name in QueueModels.Names.Distinct() )
		{
			Assert.IsTrue( files.Contains( name + ".md2" ), $"{theme}: {name}.md2" );
			var model = new ModelFile( new MemoryStream( archive.GetFile( name + ".md2" ).GetData() ) );
			var points = model.Meshes.SelectMany( mesh => mesh.Vertices.Select( vertex =>
				System.Numerics.Vector3.Transform( vertex.Position.GetSystemVector3(), OriginalTerrain.GetWorldMatrix( model, mesh.NodeIndex ) ) ) ).ToArray();
			// Authored on the cell (MD2 X and Z 0..10, Y up), within a cell's reach beyond its edge.
			Assert.IsTrue( points.All( point => point.X > -1.5f && point.X < 11.5f && point.Z > -1.5f && point.Z < 12.5f && point.Y > -1f ), $"{theme}: {name}" );
		}
	}
}
