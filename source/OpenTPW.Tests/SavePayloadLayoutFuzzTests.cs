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
	private static readonly SaveBlock[] Variable = SavePayloadLayout.Delimiters.Select( item => item.Block )
		.Where( block => block is not (SaveBlock.World or SaveBlock.Coasters) && !SavePayloadLayout.FixedBlockBytes.ContainsKey( block ) ).ToArray();

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
			// One to three variable blocks get random bytes with one to three injected delimiters.
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
				blocks[Variable[random.Next( Variable.Length )]] = content.ToArray();
			}
			var payload = SaveTestPayloads.Payload( world, blocks: blocks );
			try
			{
				var layout = SavePayloadLayout.Parse( payload );
				accepted++;
				Assert.AreEqual( world.Length, layout[SaveBlock.World].Length, $"iteration {iteration}" );
				foreach ( var (block, content) in blocks )
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
		// Only the variable blocks flooded: the fixed widths cannot prune the search.
		var flooded = SavePayloadLayout.Delimiters.Where( item => Variable.Contains( item.Block ) ).ToDictionary( item => item.Block,
			item => Enumerable.Range( 0, SavePayloadLayout.MaximumDelimiterOccurrences - 1 ).SelectMany( _ => SaveTestPayloads.Stored( item.Tag ) ).ToArray() );
		Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( SaveTestPayloads.Payload( World(), blocks: flooded ) ) );
		Assert.IsTrue( stopwatch.Elapsed < TimeSpan.FromSeconds( 5 ), $"hostile payloads took {stopwatch.Elapsed}" );
	}
}
