using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The Coasters block reader (the body between the stored EMAK and SAOC delimiters; docs/TPWS-PAYLOAD.md
/// "Coasters block"): the traced 32-byte header, auxiliary link block, 34-byte sections with links and the raw
/// train payload. Synthetic bodies prove every field and refusal; the original Jungle Easymode fixture pins the
/// empty boundary; the Theme Park Inc prebuilt save (a sibling game whose coaster body follows the same contract)
/// is a structural cross-check only, not a Theme Park World proof.
/// </summary>
[TestClass]
public class OriginalCoasterSaveTests
{
	private sealed class Body
	{
		private readonly MemoryStream stream = new();
		private readonly BinaryWriter writer;

		public Body() => writer = new BinaryWriter( stream );

		public Body U8( byte value ) { writer.Write( value ); return this; }
		public Body U16( ushort value ) { writer.Write( value ); return this; }
		public Body U32( uint value ) { writer.Write( value ); return this; }
		public Body F32( float value ) { writer.Write( value ); return this; }
		public Body Bytes( params byte[] values ) { writer.Write( values ); return this; }
		public byte[] ToArray() { writer.Flush(); return stream.ToArray(); }
	}

	/// <summary>The header of one coaster in the traced field order (loader 0x39f98).</summary>
	private static Body Header( Body body, uint flags = 3, uint controller8 = 17, ushort controller12 = 19, ushort controller16 = 4,
		ushort controller20 = 1, ushort controller28 = 69, ushort controller32 = 65, float power = 1f, ushort sections = 1,
		ushort controller244 = 3, ushort controller240 = 2, uint riders = 0 )
	{
		return body.U32( flags ).U32( controller8 ).U16( controller12 ).U16( controller16 ).U16( controller20 )
			.U16( controller28 ).U16( controller32 ).F32( power ).U16( sections ).U16( controller244 ).U16( controller240 ).U32( riders );
	}

	/// <summary>One section record plus its link values (34 bytes, then each link's own u16).</summary>
	private static Body Section( Body body, uint flags = 0x280, uint field112 = 37, int cellX = 65, int cellY = 67, byte stack = 0,
		float height = 0.25f, float bank = 0.5f, float field220 = -0.068084791f, float field224 = 1.5707964f, float field248 = 0.75f,
		params ushort[] links )
	{
		body.U32( flags ).U32( field112 ).U16( (ushort)cellX ).U16( (ushort)cellY ).U8( stack )
			.F32( height ).F32( bank ).F32( field220 ).F32( field224 ).F32( field248 ).U8( (byte)links.Length );
		foreach ( var link in links )
			body.U16( link );
		return body;
	}

	private static byte[] Payload( byte[] coasters, byte[]? world = null ) =>
		SaveTestPayloads.Payload( world ?? new byte[8], rideSystem: new byte[6],
			blocks: new Dictionary<SaveBlock, byte[]> { [SaveBlock.Coasters] = coasters } );

	private static SaveCoasterBlock Parse( byte[] coasters, int countX = 128, int countY = 128 )
	{
		var payload = Payload( coasters );
		return SaveCoasterBlock.Parse( payload, SavePayloadLayout.Parse( payload ), countX, countY );
	}

	[TestMethod]
	public void EmptyBlockParsesWithNoCoasters()
	{
		var block = Parse( new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0 } );
		Assert.AreEqual( 0u, block.Count );
		Assert.AreEqual( (0u, 0u, 1u), (block.Global1, block.Global2, block.Global3) );
		Assert.AreEqual( 0, block.Coasters.Count );
	}

	[TestMethod]
	public void OneCoasterHeaderAuxiliarySectionsLinksAndTrainsParseExactly()
	{
		var body = Header( new Body().U32( 1 ).U32( 1 ).U32( 0 ).U32( 2 ), sections: 2, riders: 5, power: 1f )
			.U32( 2 ).U16( 5 ).U16( 7 );
		Section( body, links: 1 );
		Section( body, flags: 0, field112: 0, cellX: 66, stack: 1, height: 0.5f, bank: 0f, field248: 0f, links: new ushort[] { 0, 2 } );
		var bytes = body.U32( 9 ).Bytes( 1, 2, 3, 4, 5 ).ToArray();
		var payload = Payload( bytes );
		var layout = SavePayloadLayout.Parse( payload );
		var block = SaveCoasterBlock.Parse( payload, layout, 128, 128 );

		Assert.AreEqual( 1u, block.Count );
		Assert.AreEqual( (1u, 0u, 2u), (block.Global1, block.Global2, block.Global3) );
		var coaster = block.Coasters.Single();
		Assert.AreEqual( layout[SaveBlock.Coasters].Offset + 16, coaster.Offset );
		Assert.AreEqual( 3u, coaster.Flags );
		Assert.AreEqual( 17u, coaster.Controller8 );
		Assert.AreEqual( 19, coaster.Controller12 );
		Assert.AreEqual( 4, coaster.Controller16 );
		Assert.AreEqual( 1, coaster.Controller20 );
		Assert.AreEqual( 69, coaster.Controller28 );
		Assert.AreEqual( 65, coaster.Controller32 );
		Assert.AreEqual( 1f, coaster.PowerFactor );
		Assert.AreEqual( 2, coaster.SectionCount );
		Assert.AreEqual( 3, coaster.Controller244 );
		Assert.AreEqual( 2, coaster.Controller240 );
		Assert.AreEqual( 5u, coaster.RidersOnBoard );
		CollectionAssert.AreEqual( new ushort[] { 5, 7 }, coaster.AuxiliaryOrdinals );

		var first = coaster.Sections[0];
		Assert.AreEqual( layout[SaveBlock.Coasters].Offset + 16 + 32 + 4 + 4, first.Offset );
		Assert.AreEqual( 0x280u, first.Flags );
		Assert.AreEqual( 37u, first.Field112 );
		Assert.AreEqual( (65, 67), (first.CellX, first.CellY) );
		Assert.AreEqual( 0, first.StackOrdinal );
		Assert.AreEqual( 0.25f, first.HeightParameter );
		Assert.AreEqual( 0.5f, first.Bank );
		Assert.AreEqual( -0.068084791f, first.Field220 );
		Assert.AreEqual( 1.5707964f, first.Field224 );
		Assert.AreEqual( 0.75f, first.Field248 );
		CollectionAssert.AreEqual( new ushort[] { 1 }, first.Links );
		var second = coaster.Sections[1];
		Assert.AreEqual( (66, 67), (second.CellX, second.CellY) );
		Assert.AreEqual( 1, second.StackOrdinal );
		CollectionAssert.AreEqual( new ushort[] { 0, 2 }, second.Links );

		// The train payload follows the last link value and stays raw.
		Assert.AreEqual( first.Offset + SaveCoasterSection.Bytes + 2 + SaveCoasterSection.Bytes + 4, coaster.TrainsOffset );
		CollectionAssert.AreEqual( new byte[] { 9, 0, 0, 0, 1, 2, 3, 4, 5 }, coaster.Trains );
	}

	[TestMethod]
	public void TruncatedBodiesAreRefusedWhereTheyBreak()
	{
		Body Listed() => new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 );
		void Refused( byte[] bytes, string part )
		{
			var error = Assert.ThrowsException<InvalidDataException>( () => Parse( bytes ) );
			StringAssert.Contains( error.Message, "truncated" );
			StringAssert.Contains( error.Message, part );
		}
		Refused( Listed().Bytes( new byte[10] ).ToArray(), "coaster 0 header" );
		Refused( Header( Listed() ).ToArray(), "auxiliary links" );
		Refused( Header( Listed() ).U32( 2 ).U16( 5 ).ToArray(), "auxiliary links" );
		Refused( Header( Listed() ).U32( 0 ).Bytes( new byte[20] ).ToArray(), "section 0" );
		Refused( Header( Listed() ).U32( 0 ).Bytes( new byte[33] ).U8( 2 ).U16( 1 ).ToArray(), "section 0" );
		Refused( Section( Header( Listed(), sections: 2 ).U32( 0 ) ).ToArray(), "section 1" );
	}

	[TestMethod]
	public void TwoCoastersAreRefusedBecauseTheTrainPayloadExtentIsUnknown()
	{
		var error = Assert.ThrowsException<InvalidDataException>( () => Parse(
			Header( new Body().U32( 2 ).U32( 0 ).U32( 0 ).U32( 1 ) ).U32( 0 ).U32( 9 ).Bytes( 1, 2, 3, 4 )
				.Bytes( new byte[32] ).ToArray() ) );
		StringAssert.Contains( error.Message, "train payload that follows coaster 0" );
		StringAssert.Contains( error.Message, "coaster 1's header cannot be located" );
	}

	[TestMethod]
	public void TwoCoastersWithoutRoomForTheSecondHeaderAreRefusedAsMissing()
	{
		var error = Assert.ThrowsException<InvalidDataException>( () => Parse(
			Header( new Body().U32( 2 ).U32( 0 ).U32( 0 ).U32( 1 ), sections: 0 ).U32( 0 ).ToArray() ) );
		StringAssert.Contains( error.Message, "ends after coaster 0" );
		StringAssert.Contains( error.Message, "coaster 1's header is missing" );
	}

	[TestMethod]
	public void CountsOutsideTheBoundsAreRefused()
	{
		var error = Assert.ThrowsException<InvalidDataException>( () => Parse(
			new Body().U32( (uint)(SaveCoasterBlock.MaximumCoasters + 1) ).U32( 0 ).U32( 0 ).U32( 1 ).ToArray() ) );
		StringAssert.Contains( error.Message, $"more than the bound of {SaveCoasterBlock.MaximumCoasters}" );
		error = Assert.ThrowsException<InvalidDataException>( () => Parse(
			Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ), sections: 3000 ).U32( 0 ).ToArray() ) );
		StringAssert.Contains( error.Message, "truncated" );
		error = Assert.ThrowsException<InvalidDataException>( () => Parse(
			Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ) ).U32( SaveCoasterBlock.MaximumAuxiliaryLinks + 1 ).ToArray() ) );
		StringAssert.Contains( error.Message, $"more than the bound of {SaveCoasterBlock.MaximumAuxiliaryLinks}" );
	}

	[TestMethod]
	public void NonFiniteFloatsAndOutsideCellsAreRefused()
	{
		var nan = BitConverter.UInt32BitsToSingle( 0x7FC00000 );
		var infinity = BitConverter.UInt32BitsToSingle( 0x7F800000 );
		var error = Assert.ThrowsException<InvalidDataException>( () => Parse(
			Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ), power: nan ).U32( 0 ).ToArray() ) );
		StringAssert.Contains( error.Message, "non-finite power factor" );
		error = Assert.ThrowsException<InvalidDataException>( () => Parse(
			Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ) ).U32( 0 )
				.U32( 0 ).U32( 0 ).U16( 65 ).U16( 67 ).U8( 0 ).F32( 0.25f ).F32( 0 ).F32( infinity ).F32( 0 ).F32( 0 ).U8( 0 ).ToArray() ) );
		StringAssert.Contains( error.Message, "non-finite" );
		var outside = Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ) ).U32( 0 );
		Section( outside, cellX: 65, cellY: 67 );
		error = Assert.ThrowsException<InvalidDataException>( () => Parse( outside.ToArray(), 128, 64 ) );
		StringAssert.Contains( error.Message, "outside the 128x64 grid" );
	}

	[TestMethod]
	public void AnEmptyListedBlockWithExtraBytesNeverReachesTheReader()
	{
		var payload = Payload( new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0 } );
		var error = Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( payload ) );
		StringAssert.Contains( error.Message, "no coaster but 20 bytes" );
	}

	private static byte[] RideSystemRecord( int infoId, int x, int y, int kind, int index )
	{
		var record = new byte[60];
		record[0] = 1;
		var values = new[] { infoId, x, y, 1, 1, kind, index };
		for ( var field = 0; field < values.Length; field++ )
			BinaryPrimitives.WriteInt32LittleEndian( record.AsSpan( 1 + field * 4 ), values[field] );
		return record;
	}

	/// <summary>A 3×3 synthetic park (Belly Bounce 1100 at (1, 1), the gates 1601 at (0, 0)) with the given coaster body.</summary>
	private static OriginalParkImport Park( byte[]? coasters = null, bool matchingAttractionId = true )
	{
		var map = OriginalParkImportTests.CreateMap( 3, 3, ( _, _ ) => 0 );
		var things = SaveTestPayloads.ParkThings();
		things.Insert( 2, (11, (uint)SaveThingModel.Attraction, SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields
		{
			PositionX = 128, PositionY = 128, Angle = 0, InfoId = 1601, Price = 0, Level = 0, Repair = 100, LifeGauge = 100
		} ) ) );
		if ( !matchingAttractionId )
		{
			var ride = things.FindIndex( thing => thing.Item1 == 12 );
			things[ride] = (12, (uint)SaveThingModel.Attraction, SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields
			{
				PositionX = 384, PositionY = 384, InfoId = 1101, Repair = 100, LifeGauge = 100
			} ));
		}
		var grid = SaveTestPayloads.Grid( map, ( x, y ) => OriginalParkImportTests.CreateCellRecord( 0, occupied: (x, y) == (1, 1) ) );
		var rideSystem = RideSystemRecord( 1601, 0, 0, SaveObject.FixedItemKind, 1 ).Concat( RideSystemRecord( 1100, 1, 1, SaveObject.PlacedObjectKind, 2 ) ).ToArray();
		var payload = SaveTestPayloads.Payload( SaveTestPayloads.World( SaveTestPayloads.ParkVariables(), grid, SaveTestPayloads.Things( things ) ), rideSystem,
			coasters == null ? null : new Dictionary<SaveBlock, byte[]> { [SaveBlock.Coasters] = coasters } );
		return OriginalParkImport.Import( payload, map );
	}

	[TestMethod]
	public void ParkImportExposesTheCoasterBlockAndJoinsSectionsToSavedRecords()
	{
		var body = Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ), sections: 2 ).U32( 0 );
		Section( body, cellX: 1, cellY: 1 );
		Section( body, flags: 0, field112: 0, cellX: 2, cellY: 0, links: 0 );
		var park = Park( body.ToArray() );

		Assert.IsNotNull( park.CoasterBlock );
		Assert.AreEqual( 1u, park.CoasterBlock.Count );
		var coaster = park.CoasterBlock.Coasters.Single();
		Assert.AreEqual( 2, coaster.Sections.Count );
		Assert.AreEqual( (1, 1), (coaster.Sections[0].CellX, coaster.Sections[0].CellY) );
		var record = park.ObjectsAt( 1, 1 ).Single();
		Assert.AreEqual( 1100, record.InfoId );
		Assert.AreEqual( 12, park.AttractionsAt( 1, 1 ).Single().Id );
		Assert.AreEqual( 1601, park.ObjectsAt( 0, 0 ).Single().InfoId );
		Assert.AreEqual( 0, park.ObjectsAt( 2, 2 ).Count );
		Assert.AreEqual( 0, park.AttractionsAt( 2, 2 ).Count );
		Assert.AreEqual( 2, park.CoasterSections.Count );
		Assert.AreEqual( record, park.CoasterSections[0].Objects.Single() );
		Assert.AreEqual( 12, park.CoasterSections[0].Attractions.Single().Id );
		Assert.AreEqual( 0, park.CoasterSections[1].Objects.Count );
		Assert.AreEqual( 0, park.CoasterSections[1].Attractions.Count );
	}

	[TestMethod]
	public void StructuralJoinDoesNotUseACollocatedDifferentAttractionId()
	{
		var body = Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ) ).U32( 0 );
		Section( body, cellX: 1, cellY: 1 );
		var park = Park( body.ToArray(), matchingAttractionId: false );
		Assert.AreEqual( 1100, park.CoasterSections.Single().Objects.Single().InfoId );
		Assert.AreEqual( 1101, park.AttractionsAt( 1, 1 ).Single().InfoId );
		Assert.AreEqual( 0, park.CoasterSections.Single().Attractions.Count );
	}

	[TestMethod]
	public void RuntimeGuardAllowsEmptyParksButRefusesOpaqueOriginalControllers()
	{
		Park().RequireSupportedCoasterRuntime();
		var body = Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ), sections: 0 ).U32( 0 );
		var park = Park( body.ToArray() );
		Assert.AreEqual( 1u, park.CoasterBlock!.Count );
		var error = Assert.ThrowsException<InvalidDataException>( park.RequireSupportedCoasterRuntime );
		StringAssert.Contains( error.Message, "controller-to-station identity" );
		// No catalog lookup or object mutation may precede the unsupported-data refusal.
		var objects = new ParkObjects( null!, new SandboxParkGrid() );
		Assert.ThrowsException<InvalidDataException>( () => objects.ImportOriginal( park ) );
		Assert.AreEqual( 0, objects.Objects.Count );
	}

	[TestMethod]
	public void BlockReaderRefusesInvalidDimensionsAndOverflowingOffsets()
	{
		var empty = new byte[16];
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => SaveCoasterBlock.ParseBlock( empty, 0, 128 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => SaveCoasterBlock.ParseBlock( empty, 128, -1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => SaveCoasterBlock.ParseBlock( empty, 128, 128, -1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => SaveCoasterBlock.ParseBlock( empty, 128, 128, int.MaxValue ) );
		var payload = Payload( empty );
		var layout = SavePayloadLayout.Parse( payload );
		Assert.ThrowsException<ArgumentException>( () => SaveCoasterBlock.Parse( payload.AsSpan( 1 ), layout, 128, 128 ) );
	}

	[TestMethod]
	public void ParkImportRefusesASectionOutsideItsMap()
	{
		var body = Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ) ).U32( 0 );
		Section( body, cellX: 5, cellY: 5 );
		var error = Assert.ThrowsException<InvalidDataException>( () => Park( body.ToArray() ) );
		StringAssert.Contains( error.Message, "outside the 3x3 grid" );
	}

	// [DATA:levels/jungle/Easymode.TPWI:COAS] the only original Theme Park World save fixture holds the empty coaster boundary, pinned by its SHA-256 from the rides lane
	[TestMethod]
	public void OriginalEasymodeHoldsThePinnedEmptyCoasterBoundary()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Jungle Easymode.TPWI fixture." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var path = Path.Combine( dataPath, "levels", "jungle", "Easymode.TPWI" );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( "The selected original Jungle Easymode.TPWI fixture is missing." );
		using var reader = new SaveReader( path );
		var payload = reader.ReadFile();
		var layout = SavePayloadLayout.Parse( payload );
		var block = layout[SaveBlock.Coasters];
		Assert.AreEqual( 16, block.Length );
		var bytes = payload.AsSpan().Slice( block.Offset, block.Length ).ToArray();
		Assert.AreEqual( "741939CCB979DF0DBF391A6548F88FD9D1873AA3FB996F1DF39ABC6A66DF7025", Convert.ToHexString( SHA256.HashData( bytes ) ) );
		var coasters = SaveCoasterBlock.Parse( payload, layout, 128, 128 );
		Assert.AreEqual( 0u, coasters.Count );
		Assert.AreEqual( (0u, 0u, 1u), (coasters.Global1, coasters.Global2, coasters.Global3) );

		using var mapStream = new MemoryStream( OriginalParkImportTests.ReadArchiveMember( "levels/jungle/terrain.wad", "base.map" ) );
		var park = OriginalParkImport.Import( payload, new MapFile( mapStream ) );
		Assert.IsNotNull( park.CoasterBlock );
		Assert.AreEqual( 0u, park.CoasterBlock.Count );
	}

	// [DATA:PreBuilt/prebuilt.TPWS:COAS] the sibling Theme Park Inc prebuilt save's coaster body follows the traced Theme Park World contract (power factor 1.0, one section, no links); a structural cross-check on real nonempty bytes, not a Theme Park World proof
	[TestMethod]
	public void ThemeParkIncPrebuiltCoasterBodyParsesUnderTheSharedContract()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH for the Theme Park Inc prebuilt-save fixture." );
		var dataPath = Directory.Exists( Path.Combine( path!, "Data" ) ) ? Path.Combine( path!, "Data" ) : path!;
		var file = Path.Combine( dataPath, "PreBuilt", "prebuilt.TPWS" );
		if ( !File.Exists( file ) )
			Assert.Inconclusive( "The Theme Park Inc prebuilt.TPWS fixture is missing." );
		using var reader = new SaveReader( file );
		var payload = reader.ReadFile();
		var layout = SavePayloadLayout.ParseThemeParkInc( payload );
		var block = layout[SaveBlock.Coasters];
		Assert.AreEqual( 1226, block.Length );
		var bytes = payload.AsSpan().Slice( block.Offset, block.Length ).ToArray();
		Assert.AreEqual( "A6C50E9D8FE9CCEBFACADC3BB87A2973D15105B5DECD00F31D38BE0C874E3440", Convert.ToHexString( SHA256.HashData( bytes ) ) );

		var coasters = SaveCoasterBlock.Parse( payload, layout, 128, 128 );
		Assert.AreEqual( 1u, coasters.Count );
		Assert.AreEqual( (1u, 0u, 2u), (coasters.Global1, coasters.Global2, coasters.Global3) );
		var coaster = coasters.Coasters.Single();
		Assert.AreEqual( 3u, coaster.Flags );
		Assert.AreEqual( 17u, coaster.Controller8 );
		Assert.AreEqual( 19, coaster.Controller12 );
		Assert.AreEqual( 4, coaster.Controller16 );
		Assert.AreEqual( 1, coaster.Controller20 );
		Assert.AreEqual( 69, coaster.Controller28 );
		Assert.AreEqual( 65, coaster.Controller32 );
		Assert.AreEqual( 1f, coaster.PowerFactor );
		Assert.AreEqual( 1, coaster.SectionCount );
		Assert.AreEqual( 3, coaster.Controller244 );
		Assert.AreEqual( 2, coaster.Controller240 );
		Assert.AreEqual( 0u, coaster.RidersOnBoard );
		Assert.AreEqual( 0, coaster.AuxiliaryOrdinals.Length );
		var section = coaster.Sections.Single();
		Assert.AreEqual( 0x280u, section.Flags );
		Assert.AreEqual( 37u, section.Field112 );
		Assert.AreEqual( (65, 67), (section.CellX, section.CellY) );
		Assert.AreEqual( 0, section.StackOrdinal );
		Assert.AreEqual( 0.05555984750390053f, section.HeightParameter );
		Assert.AreEqual( 0f, section.Bank );
		Assert.AreEqual( -0.06808479130268097f, section.Field220 );
		Assert.AreEqual( 1.5707963705062866f, section.Field224 );
		Assert.AreEqual( 0f, section.Field248 );
		Assert.AreEqual( 0, section.Links.Length );
		// The pylon's cell carries its own 1×1 record in the save's object list (Info.Id 1181 in this park).
		Assert.AreEqual( 1140, coaster.Trains.Length );
		Assert.AreEqual( block.Offset + 16 + SaveCoaster.HeaderBytes + 4 + SaveCoasterSection.Bytes, coaster.TrainsOffset );
		Assert.AreEqual( 1u, BinaryPrimitives.ReadUInt32LittleEndian( coaster.Trains ) );
		var objects = ThemeParkIncSaveObjectList.Parse( payload, layout, 128, 128 );
		// Pylons have kind 1018, outside the existing scene parser's accepted kinds. Pin the observed
		// raw header separately rather than widening the TPI production parser from sibling-game evidence.
		Assert.IsFalse( objects.Any( record => record.X == section.CellX && record.Y == section.CellY ) );
		var rideBlock = layout[SaveBlock.RideSystem];
		var rideBytes = payload.AsSpan( rideBlock.Offset, rideBlock.Length );
		var pylonHeaders = 0;
		for ( var offset = 0; offset <= rideBytes.Length - ThemeParkIncSaveObjectList.RecordHeaderBytes; offset++ )
			if ( rideBytes[offset] == 1
				&& BinaryPrimitives.ReadUInt32LittleEndian( rideBytes[(offset + 5)..] ) == 1181
				&& BinaryPrimitives.ReadUInt32LittleEndian( rideBytes[(offset + 9)..] ) == section.CellX
				&& BinaryPrimitives.ReadUInt32LittleEndian( rideBytes[(offset + 13)..] ) == section.CellY
				&& BinaryPrimitives.ReadUInt32LittleEndian( rideBytes[(offset + 25)..] ) == 1018 )
				pylonHeaders++;
		Assert.AreEqual( 1, pylonHeaders );
		Assert.IsTrue( objects.Any( record => record.InfoId == 1180 && record.Width == 6 && record.Height == 6 ) );
	}
}
