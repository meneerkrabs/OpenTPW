using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SavePayloadLazyDelimiterTests
{
	private static byte[] Payload( string tag, int copies, bool listedCoaster = false )
	{
		// Synthetic small World intentionally retains the existing World delimiter fallback.
		var world = SaveTestPayloads.World( SaveTestPayloads.ParkVariables(),
			SaveTestPayloads.Grid( OriginalParkImportTests.CreateMap( 2, 2, ( _, _ ) => 0 ) ), SaveTestPayloads.Things( SaveTestPayloads.ParkThings() ) );
		var data = Enumerable.Repeat( SaveTestPayloads.Stored( tag ), copies ).SelectMany( bytes => bytes ).ToArray();
		var coaster = new byte[SavePayloadLayout.EmptyCoastersBytes];
		if ( listedCoaster )
			coaster[0] = 1; // Layout-only opaque block; not a decoded coaster fixture.
		return SaveTestPayloads.Payload( world, blocks: new Dictionary<SaveBlock, byte[]>
		{
			[SaveBlock.RideSystem] = BitConverter.GetBytes( (uint)data.Length ).Concat( data ).ToArray(),
			[SaveBlock.Coasters] = coaster,
		} );
	}

	[DataTestMethod]
	[DataRow( 256 )]
	[DataRow( 512 )]
	public void ExactEmptyCoasterIgnoresCopiesInsideAnotherFramedBlock( int copies )
	{
		var payload = Payload( "COAS", copies );
		var layout = SavePayloadLayout.Parse( payload );
		Assert.AreEqual( 16, layout[SaveBlock.Coasters].Length );
		Assert.AreEqual( 4 + copies * 4, layout[SaveBlock.RideSystem].Length );
		CollectionAssert.AreEqual( new byte[16], layout.Slice( payload, SaveBlock.Coasters ).ToArray() );
	}

	[DataTestMethod]
	[DataRow( "COAS", true )]
	[DataRow( "WRLD", false )]
	public void FallbackStillCountsGloballyAndKeepsTheExactCap( string tag, bool listedCoaster )
	{
		// The real delimiter adds one to the copies inside RideSystem. Both unresolved
		// blocks retain their global scan, including copies before the coaster block.
		Assert.AreEqual( 19, SavePayloadLayout.Parse( Payload( tag, SavePayloadLayout.MaximumDelimiterOccurrences - 1, listedCoaster ) ).Blocks.Count );
		var error = Assert.ThrowsException<InvalidDataException>( () =>
			SavePayloadLayout.Parse( Payload( tag, SavePayloadLayout.MaximumDelimiterOccurrences, listedCoaster ) ) );
		StringAssert.Contains( error.Message, $"{tag} delimiter bytes more than {SavePayloadLayout.MaximumDelimiterOccurrences} times" );
	}
}
