using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The serialized widths of the framed save blocks (SAVE-011), one test per block reader of tp.exe's loader 0x415270:
/// each block is built by the field order its reader consumes and measured, including the counted parts the Jungle
/// Easymode save leaves empty.
/// </summary>
[TestClass]
public class SaveBlockWidthsTests
{
	private static byte[] Words( params uint[] words ) => words.SelectMany( BitConverter.GetBytes ).ToArray();

	private static byte[] Join( params byte[][] parts ) => parts.SelectMany( part => part ).ToArray();

	private static int Measure( SaveBlock block, byte[] bytes, int prefix = 0 )
	{
		// A marker after the block shows the width stops at the reader's end, not at the end of the data.
		var payload = Join( new byte[prefix], bytes, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD } );
		Assert.IsTrue( SaveBlockWidths.TryMeasure( payload, block, prefix, out var width ) );
		return width;
	}

	private static void Rejects( SaveBlock block, byte[] bytes, string text )
	{
		var error = Assert.ThrowsException<InvalidDataException>( () => SaveBlockWidths.TryMeasure( bytes, block, 0, out _ ) );
		StringAssert.Contains( error.Message, text );
	}

	[TestMethod]
	public void ScriptsTakeOneRecordPerNonzeroSlotAfterTheFirst()
	{
		// Slot 0 is cleared without being read; slots 1 and 3 hold a record each.
		var block = Join( Words( SaveBlockWidths.ScriptsMagic, 0x118, 5, 7, 1, 0, 1, 0 ), new byte[2 * 0x118] );
		Assert.AreEqual( 12 + 5 * 4 + 2 * 0x118, Measure( SaveBlock.Scripts, block ) );
		Assert.AreEqual( 12 + 5 * 4, Measure( SaveBlock.Scripts, Words( SaveBlockWidths.ScriptsMagic, 0x118, 5, 7, 0, 0, 0, 0 ) ), "slot 0 alone has no record" );
		// Without the magic or the 0x118 record size the loader reads two words and keeps its defaults.
		Assert.AreEqual( 8, Measure( SaveBlock.Scripts, Words( 1, 0x118, 5 ) ) );
		Assert.AreEqual( 8, Measure( SaveBlock.Scripts, Words( SaveBlockWidths.ScriptsMagic, 0x117, 5 ) ) );
		Rejects( SaveBlock.Scripts, Words( SaveBlockWidths.ScriptsMagic, 0x118, 2, 0, 1 ), "truncated" );
	}

	[TestMethod]
	public void ParticlesFollowTheirTwoFixedTablesAndTheCountedTail()
	{
		var tables = Join( Words( SaveBlockWidths.ParticleHeaderBytes ), new byte[SaveBlockWidths.ParticleHeaderBytes ], Words( SaveBlockWidths.ParticleFieldBytes ), new byte[SaveBlockWidths.ParticleFieldBytes] );
		Assert.AreEqual( 8 + tables.Length + 4 + 6, Measure( SaveBlock.Particles, Join( Words( SaveBlockWidths.ParticlesMagic, 1 ), tables, Words( 6 ), new byte[6] ) ) );
		Assert.AreEqual( 8, Measure( SaveBlock.Particles, Words( SaveBlockWidths.ParticlesMagic, 0 ) ), "switched off" );
		Assert.AreEqual( 4, Measure( SaveBlock.Particles, Words( 1, 1, 0x9e68 ) ), "no particle magic" );
		Assert.AreEqual( 12, Measure( SaveBlock.Particles, Words( SaveBlockWidths.ParticlesMagic, 1, 0x9e67 ) ), "wrong table size" );
		Rejects( SaveBlock.Particles, Join( Words( SaveBlockWidths.ParticlesMagic, 1 ), tables, Words( 9 ), new byte[3] ), "truncated" );
	}

	[TestMethod]
	public void TheMessageCentreIsTwentyNineCountedU16Lists()
	{
		var block = Words( 5 ).Concat( Enumerable.Range( 0, 29 ).SelectMany( list => Words( (uint)(list % 3) ).Concat( new byte[(list % 3) * 2] ) ) ).ToArray();
		Assert.AreEqual( block.Length, Measure( SaveBlock.MessageCentre, block ) );
		Rejects( SaveBlock.MessageCentre, block[..^1], "truncated" );
	}

	[TestMethod]
	public void TheSmallBlocksHaveTheirFixedWidths()
	{
		foreach ( var (block, width) in SavePayloadLayout.FixedBlockBytes )
			Assert.AreEqual( width, Measure( block, new byte[width] ), block.ToString() );
		Assert.AreEqual( 36, SaveBlockWidths.GameSystemBytes );
		Assert.AreEqual( 17, SavePayloadLayout.Delimiters.Count );
	}

	[TestMethod]
	public void TheRideSystemAndAdvisorAreLengthPrefixed()
	{
		Assert.AreEqual( 4 + 9, Measure( SaveBlock.RideSystem, Join( Words( 9 ), new byte[9] ) ) );
		Assert.AreEqual( 8 + 3 + 4 + 5, Measure( SaveBlock.Advisor, Join( Words( SaveBlockWidths.AdvisorMagic, 3 ), new byte[3], Words( 5 ), new byte[5] ) ) );
		Assert.AreEqual( 4, Measure( SaveBlock.Advisor, Words( 1, 3, 9 ) ), "no advisor magic" );
		Rejects( SaveBlock.RideSystem, Join( Words( 9 ), new byte[8] ), "truncated" );
	}

	[TestMethod]
	public void TrackRidesAreAChunkWhoseSizeCoversItsSubChunks()
	{
		var sub = Join( Words( 2, 32, 24 ), new byte[12] );
		var block = Join( Words( 1, 12, (uint)(12 + 2 * sub.Length) ), sub, sub );
		Assert.AreEqual( 12 + 2 * sub.Length, Measure( SaveBlock.TrackRides, block ) );
		Assert.AreEqual( 12, Measure( SaveBlock.TrackRides, Words( 1, 12, 12 ) ), "no sub-chunk" );
		// The loader consumes sub-chunks until the container's bytes are used up; sizes that do not add up never finish.
		Rejects( SaveBlock.TrackRides, Join( Words( 1, 12, 30 ), sub ), "sub-chunks end at" );
		Rejects( SaveBlock.TrackRides, Join( Words( 1, 12, 20 ), Words( 2, 32, 4 ) ), "its header alone takes" );
		Rejects( SaveBlock.TrackRides, Words( 1, 12, 8 ), "its header alone takes" );
		// A container that is not type 1 is skipped whole by the close.
		Assert.AreEqual( 20, Measure( SaveBlock.TrackRides, Join( Words( 7, 12, 20 ), new byte[8] ) ) );
	}

	[TestMethod]
	public void FlyingRidesReadTheirSlotTableAndOneBlockPerNonzeroSlot()
	{
		var slots = new uint[100];
		slots[1] = 1;
		slots[40] = 1;
		slots[99] = 1;
		slots[0] = 1; // the first slot is not iterated
		var tail = Words( 1, 2, 3, 4, 0, 0, 0, 0, 0, 6 );
		var block = Join( Words( SaveBlockWidths.FlyingMagic, 1, 400 ), Words( slots ), tail, new byte[3 * 6] );
		Assert.AreEqual( 12 + 400 + 40 + 18, Measure( SaveBlock.FlyingRides, block ) );
		Assert.AreEqual( 8, Measure( SaveBlock.FlyingRides, Words( SaveBlockWidths.FlyingMagic, 0 ) ) );
		Assert.AreEqual( 4, Measure( SaveBlock.FlyingRides, Words( 0x1234, 1 ) ) );
		Rejects( SaveBlock.FlyingRides, block[..^1], "truncated" );
	}

	[TestMethod]
	public void RsseReadsItsRecordsBuffersAndObjectLists()
	{
		static byte[] Buffer( uint length, int unit = 1 ) => Join( Words( length ), new byte[length * unit] );
		var record = Join( new byte[8], Buffer( 4 ), Buffer( 0 ), Buffer( 8 ), Buffer( 2 ), Buffer( 12 ), Buffer( 16 ), Buffer( 2, 32 ), Buffer( 0 ), Buffer( 4 ),
			Words( SaveBlockWidths.RsseObjectsMagic, 3, 5 ), new byte[15] );
		var block = Join( Words( SaveBlockWidths.RsseMagic, 6 ), new byte[6], Words( 0, 0, 0, 0, 0, 2, 8 ), record, record );
		Assert.AreEqual( block.Length, Measure( SaveBlock.Rsse, block ) );
		Assert.AreEqual( 36, Measure( SaveBlock.Rsse, Words( SaveBlockWidths.RsseMagic, 0, 0, 0, 0, 0, 0, 0, 0 ) ) );
		Assert.AreEqual( 4, Measure( SaveBlock.Rsse, Words( 1, 0 ) ), "no RSSE magic" );
		var badList = block.ToArray();
		badList[block.AsSpan().IndexOf( Words( SaveBlockWidths.RsseObjectsMagic ) )] ^= 0xFF;
		Rejects( SaveBlock.Rsse, badList, "'OBJ '" );
	}

	[TestMethod]
	public void SoundSkipsItsChunkWhenSwitchedOn()
	{
		Assert.AreEqual( 12 + 5, Measure( SaveBlock.Sound, Join( Words( 0, 1024, 5 ), new byte[5], new byte[40] ) ), "off: only the settings" );
		var on = Join( Words( 1, 1024, 5 ), new byte[5], Words( 11, 22, 33, 20 ), new byte[16] );
		Assert.AreEqual( 12 + 5 + 16 + 16, Measure( SaveBlock.Sound, on ) );
		Rejects( SaveBlock.Sound, Join( Words( 1, 1024, 0 ), Words( 1, 2, 3, 3 ) ), "below the 4 bytes" );
	}

	[TestMethod]
	public void TheUiBlockIsCountedRecords()
	{
		Assert.AreEqual( 4, Measure( SaveBlock.UI, Words( 0 ) ) );
		Assert.AreEqual( 4 + 3 * 0x21c, Measure( SaveBlock.UI, Join( Words( 3 ), new byte[3 * 0x21c] ) ) );
		Rejects( SaveBlock.UI, Join( Words( 3 ), new byte[2 * 0x21c] ), "truncated" );
	}

	[TestMethod]
	public void OnlyTheWorldAndCoasterBlocksHaveNoTracedWidth()
	{
		Assert.IsFalse( SaveBlockWidths.IsTraced( SaveBlock.World ) );
		Assert.IsFalse( SaveBlockWidths.IsTraced( SaveBlock.Coasters ) );
		Assert.IsFalse( SaveBlockWidths.TryMeasure( new byte[64], SaveBlock.World, 0, out _ ) );
		foreach ( var block in Enum.GetValues<SaveBlock>().Where( block => block is not (SaveBlock.World or SaveBlock.Coasters or SaveBlock.ActionRecording) ) )
			Assert.IsTrue( SaveBlockWidths.IsTraced( block ), block.ToString() );
		Assert.ThrowsException<InvalidDataException>( () => SaveBlockWidths.TryMeasure( new byte[4], SaveBlock.Rsse, 5, out _ ) );
	}

	[TestMethod]
	public void HugeSubChunkLengthsAreRejectedAsInvalidDataNotAsOverflow()
	{
		// The first sub-chunk claims 2 GiB; the container's size (4 GiB - 1) says more follows, so the next sub-chunk offset leaves the int range.
		var block = Join( Words( 1, 12, 0xFFFFFFFF ), Words( 2, 32, 0x80000000 ), new byte[64] );
		Rejects( SaveBlock.TrackRides, block, "truncated" );
	}

	[TestMethod]
	public void RandomBytesOnlyEverFailAsInvalidData()
	{
		var random = new Random( 249 );
		var blocks = Enum.GetValues<SaveBlock>().Where( SaveBlockWidths.IsTraced ).ToArray();
		for ( var iteration = 0; iteration < 20000; iteration++ )
		{
			var bytes = new byte[random.Next( 0, 96 )];
			random.NextBytes( bytes );
			// Bias some words towards the magics and large counts so the readers get past their first checks.
			if ( bytes.Length >= 12 && random.Next( 2 ) == 0 )
			{
				var magics = new[] { SaveBlockWidths.ScriptsMagic, SaveBlockWidths.ParticlesMagic, SaveBlockWidths.FlyingMagic, SaveBlockWidths.RsseMagic, SaveBlockWidths.AdvisorMagic, 1u };
				BitConverter.TryWriteBytes( bytes, magics[random.Next( magics.Length )] );
				BitConverter.TryWriteBytes( bytes.AsSpan( 4 + 4 * random.Next( 2 ) ), random.Next( 2 ) == 0 ? 0xFFFFFFFFu : 0x80000000u | (uint)random.Next( 64 ) );
			}
			var block = blocks[random.Next( blocks.Length )];
			try
			{
				SaveBlockWidths.TryMeasure( bytes, block, random.Next( 0, bytes.Length + 1 ), out var width );
				Assert.IsTrue( width >= 0 && width <= bytes.Length );
			}
			catch ( InvalidDataException )
			{
			}
		}
	}
}
