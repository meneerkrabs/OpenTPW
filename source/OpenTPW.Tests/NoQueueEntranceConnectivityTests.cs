using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class NoQueueEntranceConnectivityTests
{
	private GameEdition edition;
	[TestInitialize]
	public void Setup() { edition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkWorld ); Log ??= new(); }
	[TestCleanup]
	public void Cleanup() => GameEditions.Select( edition );

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 90 )]
	[DataRow( 180 )]
	[DataRow( 270 )]
	public void PlacedNoQueueEntranceNeedsBothReciprocalLinksToReopen( int rotation )
	{
		using var w = new Fixture( rotation );
		var cells = w.Guests.Grid.Cells;
		var outside = w.Outside;
		var stand = w.Stand;
		Assert.AreEqual( ParkCellTypes.Entrance, cells.RawTypeAt( stand.X, stand.Y ) );
		Assert.AreEqual( (byte)ParkCellType.Path, cells.RawTypeAt( outside.X, outside.Y ) );
		Assert.IsTrue( w.Gate.MayOpen( w.Runtime ) );
		var outLinks = cells.LinksAt( outside.X, outside.Y );
		var standLinks = cells.LinksAt( stand.X, stand.Y );
		var toward = GuestPathGrid.LinkValue( w.Runtime.Visitors.QueueEntranceDirection );
		cells.SetLinks( outside.X, outside.Y, (byte)(outLinks & ~toward) );
		Assert.IsFalse( w.Gate.MayOpen( w.Runtime ), "The placed path no longer links toward the entrance." );
		cells.SetLinks( outside.X, outside.Y, outLinks );
		cells.SetLinks( stand.X, stand.Y, (byte)(standLinks & ~PathLinkWriter.Opposite( toward )) );
		Assert.IsFalse( w.Gate.MayOpen( w.Runtime ), "The entrance no longer links toward its actual outside path." );
		cells.SetLinks( stand.X, stand.Y, standLinks );
		Assert.IsTrue( w.Gate.MayOpen( w.Runtime ) );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 3 )]
	[DataRow( 4 )]
	[DataRow( 10 )]
	public void OnlyRawPathOutsideTheTypedEntranceIsConnected( int type )
	{
		using var w = new Fixture();
		w.Guests.Grid.Cells.SetType( w.Outside.X, w.Outside.Y, (byte)type );
		Assert.IsFalse( w.Gate.MayOpen( w.Runtime ) );
	}

	// [BIN:TPW-EXE:0x004DE4FC no-queue entrance] a typed world's answer is the cells': the owner word, the entrance cell's type, the queue front and its direction are not read
	[DataTestMethod]
	[DataRow( "no-owner" )]
	[DataRow( "wrong-owner" )]
	[DataRow( "no-front" )]
	[DataRow( "direction-negative" )]
	[DataRow( "direction-large" )]
	[DataRow( "orientation" )]
	[DataRow( "outside-bounds" )]
	[DataRow( "stand-bounds" )]
	public void ATypedWorldReadsOnlyTheCellsTheOriginalReads( string kind )
	{
		using var w = new Fixture();
		var ride = w.Runtime.Visitors;
		var cells = w.Guests.Grid.Cells;
		Assert.IsTrue( w.Gate.MayOpen( w.Runtime ) );
		if ( kind == "no-owner" ) ride.OwnerWord = 0;
		if ( kind == "wrong-owner" ) cells.SetOwner( w.Stand.X, w.Stand.Y, (ushort)(ride.OwnerWord + 1) );
		if ( kind == "no-front" ) ride.QueueFrontCell = null;
		if ( kind == "direction-negative" ) ride.QueueEntranceDirection = -1;
		if ( kind == "direction-large" ) ride.QueueEntranceDirection = 4;
		if ( kind == "orientation" ) ride.QueueEntranceDirection = 0;
		if ( kind == "outside-bounds" ) ride.QueueFrontCell = (-1, -1);
		if ( kind == "stand-bounds" ) ride.QueueFrontCell = (0, w.Guests.Grid.CountY - 1);
		Assert.IsTrue( w.Gate.MayOpen( w.Runtime ), "the answer still holds: the links decide, nothing the bridge adapted" );
		cells.SetLinks( w.Outside.X, w.Outside.Y, 0 );
		Assert.IsFalse( w.Gate.MayOpen( w.Runtime ), "and a missing link is unconnected whatever the bridge adapted" );
	}

	[TestMethod]
	public void AnEntranceCellOfAnotherTypeIsTestedOnItsLinksAlone()
	{
		// [BIN:TPW-EXE:0x004DE57F entrance links] only the entrance cell's link byte is read, not its type
		using var w = new Fixture();
		var links = w.Guests.Grid.Cells.LinksAt( w.Stand.X, w.Stand.Y );
		w.Guests.Grid.Cells.SetType( w.Stand.X, w.Stand.Y, ParkCellType.Empty );
		w.Guests.Grid.Cells.SetLinks( w.Stand.X, w.Stand.Y, links );
		Assert.IsTrue( w.Gate.MayOpen( w.Runtime ) );
		w.Guests.Grid.Cells.SetLinks( w.Stand.X, w.Stand.Y, 0 );
		Assert.IsFalse( w.Gate.MayOpen( w.Runtime ) );
	}

	[DataTestMethod]
	[DataRow( "untyped" )]
	[DataRow( "rotation" )]
	[DataRow( "tpi" )]
	public void UntypedUndefinedAndThemeParkIncGeometryKeepThePermissivePolicy( string kind )
	{
		using var w = new Fixture( untyped: kind == "untyped" );
		var ride = w.Runtime.Visitors;
		var cells = w.Guests.Grid.Cells;
		cells.SetLinks( w.Outside.X, w.Outside.Y, 0 );
		Assert.AreEqual( kind == "untyped", w.Runtime.Visitors.AccessCell == null );
		if ( kind != "untyped" )
			Assert.IsFalse( w.Gate.MayOpen( w.Runtime ) );
		if ( kind == "rotation" ) ride.Rotation = 45;
		if ( kind == "tpi" ) GameEditions.Select( GameEdition.ThemeParkInc );
		Assert.IsTrue( w.Gate.MayOpen( w.Runtime ) );
	}

	[TestMethod]
	public void ConnectivityQueriesDoNotCloseAnOpenRideOrMutateSimulationState()
	{
		using var w = new Fixture();
		w.Runtime.Open();
		w.Guests.Grid.Cells.SetLinks( w.Outside.X, w.Outside.Y, 0 );
		var before = WorldStateHash.Compute( new WorldStateSources { Economy = w.Economy, Guests = w.Guests } );
		var script = ParkResumeJson.Serialize( w.Runtime.Script!.CaptureSnapshot() );
		var version = w.Guests.Grid.Version;
		for ( var query = 0; query < 3; query++ )
			Assert.IsFalse( w.Gate.MayOpen( w.Runtime ) );
		Assert.IsTrue( w.Runtime.IsOpen, "The reopening guard does not close an already-open ride." );
		Assert.AreEqual( before, WorldStateHash.Compute( new WorldStateSources { Economy = w.Economy, Guests = w.Guests } ) );
		Assert.AreEqual( version, w.Guests.Grid.Version );
		CollectionAssert.AreEqual( script, ParkResumeJson.Serialize( w.Runtime.Script.CaptureSnapshot() ) );
		w.Runtime.Close();
		Assert.IsFalse( w.Gate.MayOpen( w.Runtime ) );
		w.Runtime.Open();
		Assert.IsTrue( w.Runtime.IsOpen, "An unconditional reopen remains unconditional; the query is not a new close path." );
	}

	[TestMethod]
	public void RealQueueBackLinkAndUntypedQueueFallbackAreUnchanged()
	{
		using var w = new Fixture( hasQueue: true );
		var ride = w.Runtime.Visitors;
		Assert.IsFalse( ParkGate.QueueConnected( w.Guests.Grid, ride ) );
		var back = ride.QueueBackCell;
		var cells = w.Guests.Grid.Cells;
		cells.SetLinks( back.X, back.Y, (byte)(cells.QueueLinkAt( back.X, back.Y ) | 4) );
		Assert.IsTrue( ParkGate.QueueConnected( w.Guests.Grid, ride ) );
		cells.SetType( w.Outside.X, w.Outside.Y, ParkCellType.Path );
		Assert.IsTrue( ParkGate.QueueConnected( w.Guests.Grid, ride ) );
	}

	private sealed class Fixture : IDisposable
	{
		private readonly string root = Path.Combine( Path.GetTempPath(), "opentpw-noqueue-links-" + Guid.NewGuid().ToString( "N" ) );
		public OriginalObjectRuntime Runtime { get; }
		public GuestSimulation Guests { get; }
		public ParkEconomy Economy { get; } = EconomyTestData.Park();
		public ParkGate Gate { get; }
		public (int X, int Y) Outside { get; }
		public (int X, int Y) Stand { get; }
		public Fixture( int rotation = 0, bool hasQueue = false, bool untyped = false )
		{
			Directory.CreateDirectory( root );
			var bytes = Md2ModelFileTests.CreateGeometry();
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x42 ), 1 ); BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x44 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x268 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x78 ), 0x264 );
			foreach ( var at in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( at ), 0 );
			File.WriteAllBytes( Path.Combine( root, "model.md2" ), bytes );
			File.WriteAllBytes( Path.Combine( root, "script.rse" ), new RideVMTests.Asm().Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( Enum.GetNames<RideVariables>() ) );
			var settings = new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id 1203\nInfo.Name Shop\nInfo.IsChoosable 1\nInfo.HasQueue {(hasQueue ? 1 : 0)}\nUpgrades[0].InitCapacity 2" ) } );
			var shape = new ObjectShape( new[] { "*2S" } );
			var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Shop, "/shop.wad", settings, shape, "/model.md2", null, "/script.rse", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
			Runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 1, open: false );
			Runtime.Script!.Advance( FixedStepClock.TickDuration );
			var cells = new ParkCellMap( 16, 16 );
			var owner = ParkCellMap.CellWord( 6, 6 );
			var access = ObjectFootprint.GetAccessPoints( shape, 6, 6, rotation ).ToArray();
			var entrance = access.Single( point => point.Kind == ObjectCellKind.Entrance );
			Outside = (entrance.OutsideX, entrance.OutsideY); Stand = (entrance.X, entrance.Y);
			cells.WriteFootprint( ObjectFootprint.GetCells( shape, 6, 6, rotation ), owner, rotation );
			ObjectAccessCells.Lay( cells, access, owner, hasQueue );
			Guests = new GuestSimulation( new GuestPathGrid( cells ), GuestTests.CreateSettings(), 1 ) { ArrivalsEnabled = false };
			Assert.IsTrue( Level.RegisterWithGuests( Runtime, access, Guests, rotation, owner, ownExitCell: !untyped ) );
			Gate = new ParkGate( Economy, () => null, () => new[] { Runtime }, Guests );
		}
		public void Dispose() { Runtime.Stop(); Directory.Delete( root, true ); }
	}
}
