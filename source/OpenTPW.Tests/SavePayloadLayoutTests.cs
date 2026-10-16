using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SavePayloadLayoutTests
{
	private static byte[] World() => SaveTestPayloads.World( SaveTestPayloads.ParkVariables(),
		SaveTestPayloads.Grid( OriginalParkImportTests.CreateMap( 2, 2, ( _, _ ) => 0 ) ), SaveTestPayloads.Things( SaveTestPayloads.ParkThings() ) );

	[TestMethod]
	public void SplitsTheBlocksInWriterOrderWithTrailingDelimiters()
	{
		var world = World();
		var payload = SaveTestPayloads.Payload( world, published: 1 );
		var layout = SavePayloadLayout.Parse( payload );
		Assert.AreEqual( 19, layout.Blocks.Count );
		Assert.IsTrue( layout.LoadedPublishedPark );
		CollectionAssert.AreEqual( Enum.GetValues<SaveBlock>(), layout.Blocks.Select( block => block.Block ).ToArray() );
		Assert.AreEqual( new SavePayloadBlock( SaveBlock.ActionRecording, "", 0, 11, -1 ), layout[SaveBlock.ActionRecording] );
		Assert.AreEqual( 3, layout.ActionRecordingBytes );
		Assert.AreEqual( new SavePayloadBlock( SaveBlock.World, "WRLD", 11, world.Length, 11 + world.Length ), layout[SaveBlock.World] );
		CollectionAssert.AreEqual( world, layout.Slice( payload, SaveBlock.World ).ToArray() );
		Assert.AreEqual( 8, layout[SaveBlock.Clock].Length );
		Assert.AreEqual( 16, layout[SaveBlock.Coasters].Length );
		Assert.AreEqual( new SavePayloadBlock( SaveBlock.UI, "", payload.Length - 4, 4, -1 ), layout[SaveBlock.UI] );
		// Blocks and delimiters tile the payload without gaps.
		var position = 0;
		foreach ( var block in layout.Blocks )
		{
			Assert.AreEqual( position, block.Offset, block.Block.ToString() );
			position = block.End + (block.DelimiterOffset < 0 ? 0 : SavePayloadLayout.DelimiterBytes);
			if ( block.DelimiterOffset >= 0 )
				CollectionAssert.AreEqual( SaveTestPayloads.Stored( block.Tag ), payload.AsSpan( block.DelimiterOffset, 4 ).ToArray() );
		}
		Assert.AreEqual( payload.Length, position );
	}

	[TestMethod]
	public void RejectsMissingDuplicateAndOutOfOrderDelimiters()
	{
		var world = World();
		var missing = SaveTestPayloads.Payload( world, tags: SavePayloadLayout.Delimiters.Where( item => item.Tag != "RSYS" ) );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( missing ) ).Message, "missing the RSYS delimiter" );
		var swapped = SavePayloadLayout.Delimiters.ToList();
		(swapped[1], swapped[2]) = (swapped[2], swapped[1]);
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( SaveTestPayloads.Payload( world, tags: swapped ) ) ).Message, "SAD_Scripts" );
		// A Coasters block with a coaster is located by its delimiter, so a copy of the COAS bytes that leaves a valid rest is a second split.
		var baseline = SaveTestPayloads.Payload( world );
		var tail = baseline[SavePayloadLayout.Parse( baseline )[SaveBlock.Advisor].Offset..];
		var coaster = new byte[16];
		coaster[0] = 1;
		var duplicate = SaveTestPayloads.Payload( world, blocks: new Dictionary<SaveBlock, byte[]> { [SaveBlock.Coasters] = coaster.Concat( SaveTestPayloads.Stored( "COAS" ) ).Concat( tail ).ToArray() } );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( duplicate ) ).Message, "splits into its blocks in more than one way: the COAS delimiter" );
	}

	[TestMethod]
	public void TreatsDelimiterBytesInsideTracedBlocksAsData()
	{
		var world = World();
		// A traced block ends where its reader stops, so any delimiter's bytes inside it, even its own, are data (SAVE-011).
		var rideSystem = BitConverter.GetBytes( 12u ).Concat( SaveTestPayloads.Stored( "RSYS" ) ).Concat( SaveTestPayloads.Stored( "SOUN" ) ).Concat( SaveTestPayloads.Stored( "WRLD" ) ).ToArray();
		var advisor = BitConverter.GetBytes( SaveBlockWidths.AdvisorMagic ).Concat( BitConverter.GetBytes( 4u ) ).Concat( SaveTestPayloads.Stored( "ADVS" ) )
			.Concat( BitConverter.GetBytes( 4u ) ).Concat( SaveTestPayloads.Stored( "SPSC" ) ).ToArray();
		var clock = SaveTestPayloads.Stored( "CLOK" ).Concat( new byte[4] ).ToArray();
		var payload = SaveTestPayloads.Payload( world, blocks: new Dictionary<SaveBlock, byte[]> { [SaveBlock.RideSystem] = rideSystem, [SaveBlock.Advisor] = advisor, [SaveBlock.Clock] = clock } );
		var layout = SavePayloadLayout.Parse( payload );
		CollectionAssert.AreEqual( rideSystem, layout.Slice( payload, SaveBlock.RideSystem ).ToArray() );
		CollectionAssert.AreEqual( advisor, layout.Slice( payload, SaveBlock.Advisor ).ToArray() );
		CollectionAssert.AreEqual( clock, layout.Slice( payload, SaveBlock.Clock ).ToArray() );
		Assert.AreEqual( world.Length, layout[SaveBlock.World].Length );

		// Only the delimiter-located blocks (World, a Coasters block with a coaster) bound their delimiter's occurrences.
		var coaster = new byte[16];
		coaster[0] = 1;
		var flooded = SaveTestPayloads.Payload( world, blocks: new Dictionary<SaveBlock, byte[]>
		{
			[SaveBlock.Coasters] = coaster.Concat( Enumerable.Repeat( SaveTestPayloads.Stored( "COAS" ), SavePayloadLayout.MaximumDelimiterOccurrences ).SelectMany( bytes => bytes ) ).ToArray(),
		} );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( flooded ) ).Message, $"more than {SavePayloadLayout.MaximumDelimiterOccurrences} times" );
		var many = SaveTestPayloads.Payload( world, blocks: new Dictionary<SaveBlock, byte[]>
		{
			[SaveBlock.RideSystem] = BitConverter.GetBytes( (uint)(4 * SavePayloadLayout.MaximumDelimiterOccurrences * 2) )
				.Concat( Enumerable.Repeat( SaveTestPayloads.Stored( "RSYS" ), SavePayloadLayout.MaximumDelimiterOccurrences * 2 ).SelectMany( bytes => bytes ) ).ToArray(),
		} );
		Assert.AreEqual( 4 + 4 * SavePayloadLayout.MaximumDelimiterOccurrences * 2, SavePayloadLayout.Parse( many )[SaveBlock.RideSystem].Length );
	}

	[TestMethod]
	public void ChecksTracedBlockWidthsLikeTheOriginalLoader()
	{
		var world = World();
		foreach ( var (block, size) in SavePayloadLayout.FixedBlockBytes )
		{
			var payload = SaveTestPayloads.Payload( world, blocks: new Dictionary<SaveBlock, byte[]> { [block] = new byte[size + 1] } );
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( payload ) ).Message, $"SAD_{block}" );
		}
		var shortCoasters = SaveTestPayloads.Payload( world, blocks: new Dictionary<SaveBlock, byte[]> { [SaveBlock.Coasters] = new byte[12] } );
		Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( shortCoasters ) );
		var paddedEmpty = SaveTestPayloads.Payload( world, blocks: new Dictionary<SaveBlock, byte[]> { [SaveBlock.Coasters] = new byte[20] } );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( paddedEmpty ) ).Message, "no coaster" );
		var coaster = new byte[40];
		coaster[0] = 1;
		Assert.AreEqual( 40, SavePayloadLayout.Parse( SaveTestPayloads.Payload( world, blocks: new Dictionary<SaveBlock, byte[]> { [SaveBlock.Coasters] = coaster } ) )[SaveBlock.Coasters].Length );
	}

	[TestMethod]
	public void RejectsTruncatedAndCorruptActionRecordings()
	{
		var payload = SaveTestPayloads.Payload( World() );
		for ( var length = 0; length < SavePayloadLayout.ActionRecordingHeaderBytes; length++ )
			Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( payload.AsSpan( 0, length ) ), $"length {length}" );
		var oversized = payload.ToArray();
		BitConverter.GetBytes( (uint)payload.Length ).CopyTo( oversized, 4 );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( oversized ) ).Message, "action recording claims" );
		var flag = payload.ToArray();
		flag[0] = 2;
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( flag ) ).Message, "published-park flag" );
		// Cutting the payload anywhere loses a delimiter or a traced width.
		foreach ( var cut in new[] { 20, payload.Length / 2, payload.Length - 9 } )
			Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( payload.AsSpan( 0, cut ) ), $"cut {cut}" );
		Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( new byte[SavePayloadLayout.MaximumPayloadBytes + 1] ) );
	}

	[TestMethod]
	public void ReadsThroughANonSeekableSaveStream()
	{
		var payload = SaveTestPayloads.Payload( World() );
		var container = SaveReaderTests.CreateContainer( payload );
		using var reader = new SaveReader( new ForwardOnlyStream( container ) );
		var layout = SavePayloadLayout.Parse( reader );
		Assert.AreEqual( payload.Length, layout.PayloadLength );
	}

	private sealed class ForwardOnlyStream( byte[] data ) : MemoryStream( data )
	{
		public override bool CanSeek => false;
		public override long Seek( long offset, SeekOrigin loc ) => throw new NotSupportedException();
		public override long Position { get => base.Position; set => throw new NotSupportedException(); }
	}

	[TestMethod]
	public void OriginalJungleEasymodeSplitsIntoAllBlocks()
	{
		var payload = OriginalParkImportTests.ReadEasymodePayload();
		Assert.AreEqual( "A3C9A28252C37AD49A8EB78E4A0C5E1D5229D01548FA35801DB67015D2589173", Convert.ToHexString( SHA256.HashData( payload ) ) );
		var layout = SavePayloadLayout.Parse( payload );
		Assert.AreEqual( 19, layout.Blocks.Count );
		Assert.IsFalse( layout.LoadedPublishedPark );
		Assert.AreEqual( payload.Length, layout.Blocks.Sum( block => block.Length ) + SavePayloadLayout.Delimiters.Count * SavePayloadLayout.DelimiterBytes );
		Assert.IsTrue( layout.Blocks.All( block => block.Length > 0 ), "every Easymode block has content" );
		// The leading-tag reading took the bytes after a tag; they belong to the next block. The RideSystem block holds
		// the placed-object records, the TrackRides block the build stamp and FlyingRides starts with its own tag.
		Assert.IsTrue( layout.Slice( payload, SaveBlock.TrackRides ).EndsWith( "20:25:29Oct 21 1999\0"u8 ) );
		Assert.IsTrue( layout.Slice( payload, SaveBlock.FlyingRides ).StartsWith( "FLY_"u8 ) );
		Assert.IsTrue( layout.Slice( payload, SaveBlock.Advisor ).StartsWith( "ADV_"u8 ) );
		Assert.IsTrue( layout.Slice( payload, SaveBlock.Scripts ).StartsWith( "TPCS"u8 ) );
		Assert.IsTrue( layout.Slice( payload, SaveBlock.UI ).ToArray().All( value => value == 0 ) );
		// The small runtime blocks of the shipped Jungle save (the camera looks at the gates at cell 47.5, 17.5 with zoom 110 and no yaw).
		var runtime = SaveRuntimeBlocks.Read( payload, layout );
		Assert.AreEqual( (110f, 0f, 256u, (475f, 0f, 175.00726f), (0f, 0f, 0f), 0f), (runtime.Camera.Zoom, runtime.Camera.YRotation, runtime.Camera.Flags, runtime.Camera.RequiredPoi, runtime.Camera.SavedPoi, runtime.Camera.SavedYRotation) );
		CollectionAssert.AreEqual( new uint[] { 114374804, 114876286 }, runtime.ClockWords );
		Assert.AreEqual( 114938044u, runtime.VanillaTime );
		CollectionAssert.AreEqual( new uint[] { 114374806, 114374775, 114374589, 6055, 0, 7723976, 2, 4294967267, 4294967081 }, runtime.GameSystemWords );
		CollectionAssert.AreEqual( new byte[] { 1, 1 }, runtime.Cheats );
		Assert.AreEqual( (277u, 0u), (runtime.AdvisorScoring, runtime.UiRecords) );
		// Every traced block's reader (SaveBlockWidths) stops exactly where the original save has its delimiter.
		var widths = new Dictionary<SaveBlock, int>
		{
			[SaveBlock.Scripts] = 5452, [SaveBlock.Particles] = 76252, [SaveBlock.MessageCentre] = 266, [SaveBlock.Clock] = 8, [SaveBlock.VanillaTime] = 4,
			[SaveBlock.GameSystem] = 36, [SaveBlock.RideSystem] = 17526, [SaveBlock.TrackRides] = 44, [SaveBlock.FlyingRides] = 452, [SaveBlock.Rsse] = 10856,
			[SaveBlock.Camera] = 40, [SaveBlock.Coasters] = 16, [SaveBlock.Advisor] = 372, [SaveBlock.Sound] = 1445, [SaveBlock.Cheat] = 2,
			[SaveBlock.AdvisorScoring] = 4, [SaveBlock.UI] = 4,
		};
		foreach ( var (block, width) in widths )
		{
			Assert.AreEqual( width, layout[block].Length, block.ToString() );
			if ( SaveBlockWidths.IsTraced( block ) )
			{
				Assert.IsTrue( SaveBlockWidths.TryMeasure( payload, block, layout[block].Offset, out var measured ), block.ToString() );
				Assert.AreEqual( width, measured, block.ToString() );
			}
		}
	}

	[TestMethod]
	public void ReadsTheCameraAndTheSmallRuntimeBlocks()
	{
		var camera = new[] { 110f, 0.5f, BitConverter.UInt32BitsToSingle( 256 ), 475f, 0f, 175f, 1f, 2f, 3f, 0.25f }.SelectMany( BitConverter.GetBytes ).ToArray();
		var words = Enumerable.Range( 1, 9 ).SelectMany( value => BitConverter.GetBytes( (uint)value ) ).ToArray();
		var payload = SaveTestPayloads.Payload( World(), blocks: new Dictionary<SaveBlock, byte[]>
		{
			[SaveBlock.Camera] = camera, [SaveBlock.GameSystem] = words, [SaveBlock.Clock] = BitConverter.GetBytes( 7u ).Concat( BitConverter.GetBytes( 8u ) ).ToArray(),
			[SaveBlock.VanillaTime] = BitConverter.GetBytes( 9u ), [SaveBlock.Cheat] = new byte[] { 1, 0 }, [SaveBlock.AdvisorScoring] = BitConverter.GetBytes( 277u ),
		} );
		var layout = SavePayloadLayout.Parse( payload );
		var blocks = SaveRuntimeBlocks.Read( payload, layout );
		Assert.AreEqual( (110f, 0.5f, 256u, (475f, 0f, 175f), (1f, 2f, 3f), 0.25f), (blocks.Camera.Zoom, blocks.Camera.YRotation, blocks.Camera.Flags, blocks.Camera.RequiredPoi, blocks.Camera.SavedPoi, blocks.Camera.SavedYRotation) );
		CollectionAssert.AreEqual( new uint[] { 7, 8 }, blocks.ClockWords );
		Assert.AreEqual( 9u, blocks.VanillaTime );
		CollectionAssert.AreEqual( Enumerable.Range( 1, 9 ).Select( value => (uint)value ).ToArray(), blocks.GameSystemWords );
		CollectionAssert.AreEqual( new byte[] { 1, 0 }, blocks.Cheats );
		Assert.AreEqual( (277u, 0u), (blocks.AdvisorScoring, blocks.UiRecords) );
		Assert.ThrowsException<ArgumentException>( () => SaveRuntimeBlocks.Read( payload.AsSpan( 1 ), layout ) );
	}
}
