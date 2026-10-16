using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// REVIEW-SAVE-2 lane: seeded fuzz of the SAVE-011 delimiter split. Every mutated payload either splits into
/// exactly its true blocks or is rejected with <see cref="InvalidDataException"/> (reject, never guess), and the
/// hostile payloads finish quickly.
/// </summary>
[TestClass]
public class SavePayloadLayoutFuzzTests
{
	/// <summary>Blocks whose reader accepts arbitrary bytes inside a counted body: RideSystem, Advisor, Sound and Particles.</summary>
	private static readonly SaveBlock[] Variable = { SaveBlock.RideSystem, SaveBlock.Advisor, SaveBlock.Sound, SaveBlock.Particles };

	private static byte[] Counted( SaveBlock block, byte[] body ) => block switch
	{
		SaveBlock.RideSystem => BitConverter.GetBytes( (uint)body.Length ).Concat( body ).ToArray(),
		SaveBlock.Advisor => BitConverter.GetBytes( SaveBlockWidths.AdvisorMagic ).Concat( BitConverter.GetBytes( (uint)body.Length ) ).Concat( body ).Concat( BitConverter.GetBytes( 0u ) ).ToArray(),
		SaveBlock.Sound => new byte[8].Concat( BitConverter.GetBytes( (uint)body.Length ) ).Concat( body ).ToArray(),
		_ => BitConverter.GetBytes( SaveBlockWidths.ParticlesMagic ).Concat( BitConverter.GetBytes( 1u ) ).Concat( BitConverter.GetBytes( (uint)SaveBlockWidths.ParticleHeaderBytes ) )
			.Concat( new byte[SaveBlockWidths.ParticleHeaderBytes] ).Concat( BitConverter.GetBytes( (uint)SaveBlockWidths.ParticleFieldBytes ) ).Concat( new byte[SaveBlockWidths.ParticleFieldBytes] )
			.Concat( BitConverter.GetBytes( (uint)body.Length ) ).Concat( body ).ToArray(),
	};

	private static byte[] World() => SaveTestPayloads.World( SaveTestPayloads.ParkVariables(),
		SaveTestPayloads.Grid( OriginalParkImportTests.CreateMap( 2, 2, ( _, _ ) => 0 ) ), SaveTestPayloads.Things( SaveTestPayloads.ParkThings() ) );

	[TestMethod]
	public void InjectedDelimiterBytesAreSplitCorrectlyOrRejected()
	{
		var world = World();
		var random = new Random( 20261010 );
		var (accepted, rejected) = (0, 0);
		for ( var iteration = 0; iteration < 2000; iteration++ )
		{
			// One to three counted blocks get random bytes with one to three injected delimiters; one iteration in three then
			// corrupts a length word, which the block's reader follows to a position without its delimiter.
			var blocks = new Dictionary<SaveBlock, byte[]>();
			for ( var count = 1 + random.Next( 3 ); count > 0; count-- )
			{
				var content = new List<byte>();
				for ( var piece = 1 + random.Next( 3 ); piece > 0; piece-- )
				{
					var filler = new byte[random.Next( 6 )];
					random.NextBytes( filler );
					content.AddRange( filler );
					content.AddRange( SaveTestPayloads.Stored( SavePayloadLayout.Delimiters[random.Next( SavePayloadLayout.Delimiters.Count )].Tag ) );
				}
				var block = Variable[random.Next( Variable.Length )];
				blocks[block] = Counted( block, content.ToArray() );
			}
			var expected = blocks.ToDictionary( item => item.Key, item => item.Value.ToArray() );
			var corrupt = random.Next( 3 ) == 0;
			if ( corrupt )
			{
				var victim = blocks.Keys.ElementAt( random.Next( blocks.Count ) );
				var bytes = blocks[victim];
				var at = victim == SaveBlock.RideSystem ? 0 : victim == SaveBlock.Advisor ? 4 : victim == SaveBlock.Sound ? 8 : SaveBlockWidths.ParticleHeaderBytes + SaveBlockWidths.ParticleFieldBytes + 16;
				bytes[at] = (byte)(bytes[at] + 1 + random.Next( 200 ));
			}
			var payload = SaveTestPayloads.Payload( world, blocks: blocks );
			try
			{
				var layout = SavePayloadLayout.Parse( payload );
				accepted++;
				Assert.IsFalse( corrupt, $"iteration {iteration}: a corrupted length was accepted" );
				Assert.AreEqual( world.Length, layout[SaveBlock.World].Length, $"iteration {iteration}" );
				foreach ( var (block, content) in expected )
					CollectionAssert.AreEqual( content, layout.Slice( payload, block ).ToArray(), $"iteration {iteration}: {block}" );
			}
			catch ( InvalidDataException )
			{
				rejected++;
			}
		}
		Assert.IsTrue( accepted > 100 && rejected > 100, $"both outcomes are exercised ({accepted} accepted, {rejected} rejected)" );
	}

	[TestMethod]
	public void TruncatedAndReorderedPayloadsAreRejected()
	{
		var payload = SaveTestPayloads.Payload( World() );
		var layout = SavePayloadLayout.Parse( payload );
		var uiStart = layout[SaveBlock.UI].Offset;
		for ( var length = 0; length < uiStart; length++ )
			Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( payload.AsSpan( 0, length ) ), $"truncated to {length}" );
		var tags = SavePayloadLayout.Delimiters.ToArray();
		for ( var first = 0; first < tags.Length; first++ )
			for ( var second = first + 1; second < tags.Length; second++ )
			{
				var swapped = tags.ToArray();
				(swapped[first].Tag, swapped[second].Tag) = (swapped[second].Tag, swapped[first].Tag);
				Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( SaveTestPayloads.Payload( World(), tags: swapped ) ), $"{tags[first].Tag} <-> {tags[second].Tag}" );
			}
	}

	[TestMethod]
	public void HostileDelimiterFloodsFinishQuickly()
	{
		var stopwatch = Stopwatch.StartNew();
		foreach ( var count in new[] { 16, 17 } )
		{
			// Every delimiter MaximumDelimiterOccurrences times, interleaved, with 16 filler bytes between them.
			var output = new MemoryStream();
			output.Write( new byte[8] );
			for ( var repeat = 0; repeat < SavePayloadLayout.MaximumDelimiterOccurrences; repeat++ )
				foreach ( var (_, tag) in SavePayloadLayout.Delimiters.Take( count ) )
				{
					output.Write( SaveTestPayloads.Stored( tag ) );
					output.Write( new byte[16] );
				}
			Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( output.ToArray() ) );
			output.Write( SaveTestPayloads.Stored( "WRLD" ) );
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( output.ToArray() ) ).Message, "more than" );
		}
		// Every counted block flooded with its own delimiter: the traced widths ignore the copies, and the Coasters block's copies are bounded.
		var flooded = SavePayloadLayout.Delimiters.Where( item => Variable.Contains( item.Block ) ).ToDictionary( item => item.Block,
			item => Counted( item.Block, Enumerable.Range( 0, SavePayloadLayout.MaximumDelimiterOccurrences * 4 ).SelectMany( _ => SaveTestPayloads.Stored( item.Tag ) ).ToArray() ) );
		Assert.AreEqual( 17, SavePayloadLayout.Parse( SaveTestPayloads.Payload( World(), blocks: flooded ) ).Blocks.Count - 2 );
		Assert.IsTrue( stopwatch.Elapsed < TimeSpan.FromSeconds( 5 ), $"hostile payloads took {stopwatch.Elapsed}" );
	}
}
