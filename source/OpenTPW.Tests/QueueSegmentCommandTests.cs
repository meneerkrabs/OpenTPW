using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class QueueSegmentCommandTests
{
	[TestMethod]
	public void LegacyAndNativeCommandsKeepTheirOwnSemanticsInMixedLog()
	{
		ParkCommand legacy = new LayLineCommand( CellToolMode.Queue, 7, 2, 3, 5, 3 );
		ParkCommand native = new QueueSegmentCommand( 7, 2, 3, 5, 3 );
		Assert.AreEqual( (byte)2, ParkCommandCodec.Encode( legacy )[0], "old kinds keep wire1" );
		Assert.AreEqual( (byte)4, ParkCommandCodec.Encode( native )[0], "new kind has wire2" );
		var log = new ParkCommandLogFile( "jungle", 1, 0, 0,
			new[] { new ParkCommandLogEntry( 0, legacy ), new ParkCommandLogEntry( 1, native ), new ParkCommandLogEntry( 2, legacy ) }, Array.Empty<ParkCommandHashCheckpoint>() );
		var loaded = ParkCommandLogFile.Deserialize( log.Serialize() );
		Assert.IsInstanceOfType( loaded.Entries[0].Command, typeof(LayLineCommand) );
		Assert.IsInstanceOfType( loaded.Entries[1].Command, typeof(QueueSegmentCommand) );
		Assert.IsInstanceOfType( loaded.Entries[2].Command, typeof(LayLineCommand) );
		var alias = ParkCommandCodec.Encode( legacy ); alias[0] = 4;
		Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( alias ) );
		var forged = ParkCommandCodec.Encode( native ); forged[0] = 2;
		Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( forged ) );
		var oldLog = log.Serialize(); oldLog[ParkCommandLogFile.Magic.Length] = 2;
		Assert.ThrowsException<InvalidDataException>( () => ParkCommandLogFile.Deserialize( oldLog ) );
		var onlyOld = log with { Entries = new[] { new ParkCommandLogEntry( 0, legacy ) } };
		var bytes = onlyOld.Serialize(); bytes[ParkCommandLogFile.Magic.Length] = 2;
		Assert.AreEqual( legacy, ParkCommandLogFile.Deserialize( bytes ).Entries[0].Command );
	}

	[TestMethod]
	public void PartialFailureReplayPreservesEveryRawByteAndActiveReceipt()
	{
		static ParkCellMap Map()
		{
			var map = new ParkCellMap( 8, 8 ); map.SetType( 2, 3, ParkCellType.Queue ); map.SetOwner( 2, 3, 17 );
			map.SetType( 3, 3, ParkCellType.Path ); map.SetType( 5, 3, ParkCellType.Path ); return map;
		}
		static QueueSegmentReducer Reducer( ParkCellMap map ) => new( map, 17, (_, _) => false, () => false, _ => true, () => 10, _ => { } );
		var source = Map(); var replay = Map();
		var command = new QueueSegmentCommand( 7, 2, 3, 5, 3 );
		var first = Reducer( source ).Commit( (command.StartX, command.StartY), (command.EndX, command.EndY) );
		var decoded = (QueueSegmentCommand)ParkCommandCodec.Decode( ParkCommandCodec.Encode( command ) );
		var second = Reducer( replay ).Commit( (decoded.StartX, decoded.StartY), (decoded.EndX, decoded.EndY) );
		Assert.AreEqual( first.QueueReceipt, second.QueueReceipt );
		for ( var y = 0; y < 8; y++ ) for ( var x = 0; x < 8; x++ )
		{
			Assert.AreEqual( source.RawTypeAt( x, y ), replay.RawTypeAt( x, y ) );
			Assert.AreEqual( source.LinksAt( x, y ), replay.LinksAt( x, y ) );
			Assert.AreEqual( source.QueueLinkAt( x, y ), replay.QueueLinkAt( x, y ) );
			Assert.AreEqual( source.OwnerAt( x, y ), replay.OwnerAt( x, y ) );
			Assert.AreEqual( source.PlacementCountAt( x, y ), replay.PlacementCountAt( x, y ) );
			Assert.AreEqual( source.FlagsAt( x, y ), replay.FlagsAt( x, y ) );
		}
	}
	[TestMethod]
	public void ActiveToolRetainsFailedEndpointButReopenedWriterDoesNotResurrectReceipt()
	{
		var grid = new GuestPathGrid( 8, 8 ); grid.Cells.SetType( 2, 2, ParkCellType.Queue ); grid.Cells.SetOwner( 2, 2, 17 );
		var ride = new RideVisitorBridge( 7, "Synthetic queue", RideVisitorKind.Ride, 5, 55, 30 ) { OwnerWord = 17 };
		QueueLineWriter Writer() => new QueueLineWriter( grid, ride, (_, _, _) => QueueBuildResult.Ok, (_, _) => QueueBuildResult.Ok, (_, _) => 0, () => 10 )
			.WithNativeReducer( new QueueSegmentReducer( grid.Cells, 17, (_, _) => false, () => false, _ => true, () => 10, _ => { } ) );
		var writer = Writer(); var tool = new CellBuildTool(); tool.Enter( writer, (2, 2) );
		var result = tool.Click( (5, 2) )!;
		Assert.IsTrue( tool.IsActive ); Assert.AreEqual( (5, 2), tool.Start );
		Assert.IsNotNull( result.QueueReceipt );
		Assert.IsTrue( writer.CanUseNative( (5, 2), (5, 3) ), "active continuation can start on failed empty endpoint" );
		tool.Cancel();
		Assert.IsFalse( writer.CanUseNative( (5, 2), (5, 3) ), "closing clears even the original writer session" );
		var reopened = Writer();
		Assert.IsFalse( reopened.CanUseNative( (5, 2), (5, 3) ), "a reopened writer has no prior active receipt" );
	}

}
