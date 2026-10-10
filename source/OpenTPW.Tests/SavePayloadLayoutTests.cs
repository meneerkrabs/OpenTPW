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
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( SaveTestPayloads.Payload( world, tags: swapped ) ) ).Message, "PART (Particles) is out of writer order" );
		var duplicate = SaveTestPayloads.Payload( world, blocks: new Dictionary<SaveBlock, byte[]> { [SaveBlock.Particles] = SaveTestPayloads.Stored( "SPSC" ) } );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( duplicate ) ).Message, "more than once" );
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
	}
}
