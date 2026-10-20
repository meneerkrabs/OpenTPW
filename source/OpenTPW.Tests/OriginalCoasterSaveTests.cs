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
/// "Coasters block"): the traced 32-byte header, the auxiliary block (station node S2's collision list), 34-byte sections with their collision lists and the raw
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
		ushort controller244 = 3, ushort controller240 = 2, uint intersections = 0 )
	{
		return body.U32( flags ).U32( controller8 ).U16( controller12 ).U16( controller16 ).U16( controller20 )
			.U16( controller28 ).U16( controller32 ).F32( power ).U16( sections ).U16( controller244 ).U16( controller240 ).U32( intersections );
	}

	/// <summary>One section record plus its collision ordinals (34 bytes, then each ordinal's own u16).</summary>
	private static Body Section( Body body, uint flags = 0x280, uint field112 = 37, int cellX = 65, int cellY = 67, byte stack = 0,
		float height = 0.25f, float bank = 0.5f, float field220 = -0.068084791f, float field224 = 1.5707964f, float field248 = 0.75f,
		params ushort[] collisions )
	{
		body.U32( flags ).U32( field112 ).U16( (ushort)cellX ).U16( (ushort)cellY ).U8( stack )
			.F32( height ).F32( bank ).F32( field220 ).F32( field224 ).F32( field248 ).U8( (byte)collisions.Length );
		foreach ( var collision in collisions )
			body.U16( collision );
		return body;
	}

	private static byte[] Payload( byte[] coasters, byte[]? world = null ) =>
		SaveTestPayloads.Payload( world ?? new byte[8],
			blocks: new Dictionary<SaveBlock, byte[]> { [SaveBlock.Coasters] = coasters } );

	private static SaveCoasterBlock Parse( byte[] coasters, int countX = 128, int countY = 128, Func<ushort, SaveCoasterTrainLayout?>? trainLayouts = null )
	{
		var payload = Payload( coasters );
		return SaveCoasterBlock.Parse( payload, SavePayloadLayout.Parse( payload ), countX, countY, trainLayouts );
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
		var body = Header( new Body().U32( 1 ).U32( 1 ).U32( 0 ).U32( 2 ), sections: 2, intersections: 5, power: 1f )
			.U32( 2 ).U16( 5 ).U16( 7 );
		Section( body, collisions: 1 );
		Section( body, flags: 0, field112: 0, cellX: 66, stack: 1, height: 0.5f, bank: 0f, field248: 0f, collisions: new ushort[] { 0, 2 } );
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
		Assert.AreEqual( 5u, coaster.IntersectionCount );
		CollectionAssert.AreEqual( new ushort[] { 5, 7 }, coaster.AuxiliaryOrdinals );

		var first = coaster.Sections[0];
		Assert.AreEqual( layout[SaveBlock.Coasters].Offset + 16 + 32 + 4 + 4, first.Offset );
		Assert.AreEqual( 0x280u, first.Flags );
		Assert.AreEqual( 37u, first.MeshInstanceId );
		Assert.AreEqual( (65, 67), (first.CellX, first.CellY) );
		Assert.AreEqual( 0, first.StackOrdinal );
		Assert.AreEqual( 0.25f, first.HeightParameter );
		Assert.AreEqual( 0.5f, first.Bank );
		Assert.AreEqual( -0.068084791f, first.Incline );
		Assert.AreEqual( 1.5707964f, first.Yaw );
		Assert.AreEqual( 0.75f, first.LoopRotate );
		CollectionAssert.AreEqual( new ushort[] { 1 }, first.CollisionOrdinals );
		var second = coaster.Sections[1];
		Assert.AreEqual( (66, 67), (second.CellX, second.CellY) );
		Assert.AreEqual( 1, second.StackOrdinal );
		CollectionAssert.AreEqual( new ushort[] { 0, 2 }, second.CollisionOrdinals );

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
		Refused( Header( Listed() ).ToArray(), "auxiliary collision list" );
		Refused( Header( Listed() ).U32( 2 ).U16( 5 ).ToArray(), "auxiliary collision list" );
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
			Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ) ).U32( SaveCoasterBlock.CollisionListSlots + 1 ).ToArray() ) );
		StringAssert.Contains( error.Message, $"more than the bound of {SaveCoasterBlock.CollisionListSlots}" );
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

	/// <summary>A 3×3 synthetic park (Belly Bounce 1100 at (1, 1), the gates 1601 at (0, 0)) with the given coaster body.</summary>
	private static OriginalParkImport Park( byte[]? coasters = null, bool matchingAttractionId = true, bool duplicateMatches = false,
		Func<int, SaveCoasterTrainLayout?>? coasterTrains = null, uint rideMeshInstance = 110 )
	{
		var map = OriginalParkImportTests.CreateMap( 3, 3, ( _, _ ) => 0 );
		var things = SaveTestPayloads.ParkThings();
		things.Insert( 2, (11, (uint)SaveThingModel.Attraction, SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields
		{
			PositionX = 128, PositionY = 128, Angle = 0, InfoId = 1601, MeshInstance = 99, Price = 0, Level = 0, Repair = 100, LifeGauge = 100
		} ) ) );
		if ( !matchingAttractionId )
		{
			var ride = things.FindIndex( thing => thing.Item1 == 12 );
			things[ride] = (12, (uint)SaveThingModel.Attraction, SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields
			{
				PositionX = 384, PositionY = 384, InfoId = 1101, Repair = 100, LifeGauge = 100
			} ));
		}
		if ( rideMeshInstance != 110 )
		{
			var ride = things.FindIndex( thing => thing.Item1 == 12 );
			things[ride] = (12, (uint)SaveThingModel.Attraction, SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields
			{
				PositionX = 384, PositionY = 384, InfoId = (ushort)(matchingAttractionId ? 1100 : 1101), MeshInstance = rideMeshInstance, Price = 30, Level = 0, Repair = 80, LifeGauge = 90, IngredientAmount = 80, Quality = 100, Chance = 30
			} ));
		}
		if ( duplicateMatches )
			things.Add( (13, (uint)SaveThingModel.Attraction, SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields
			{
				PositionX = 384, PositionY = 384, InfoId = 1100, Repair = 100, LifeGauge = 100
			} )) );
		var grid = SaveTestPayloads.Grid( map, ( x, y ) => OriginalParkImportTests.CreateCellRecord( 0, occupied: (x, y) == (1, 1) ) );
		var slots = new List<byte[]>
		{
			SaveTestPayloads.RideSystemRecord( 1601, 0, 0, 1, 1, SaveObject.FixedItemKind, 1 ),
			SaveTestPayloads.RideSystemRecord( 1100, 1, 1, 1, 1, SaveObject.PlacedObjectKind, 2 ),
		};
		if ( duplicateMatches )
			slots.Add( SaveTestPayloads.RideSystemRecord( 1100, 1, 1, 1, 1, SaveObject.PlacedObjectKind, 3 ) );
		var rideSystem = SaveTestPayloads.RideSystem( slots );
		var payload = SaveTestPayloads.Payload( SaveTestPayloads.World( SaveTestPayloads.ParkVariables(), grid, SaveTestPayloads.Things( things ) ), rideSystem,
			coasters == null ? null : new Dictionary<SaveBlock, byte[]> { [SaveBlock.Coasters] = coasters } );
		return OriginalParkImport.Import( payload, map, coasterTrains );
	}

	[TestMethod]
	public void ParkImportExposesTheCoasterBlockAndJoinsSectionsToSavedRecords()
	{
		var body = Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ), sections: 2 ).U32( 0 );
		Section( body, cellX: 1, cellY: 1 );
		Section( body, flags: 0, field112: 0, cellX: 2, cellY: 0, collisions: 0 );
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
	public void StackedSectionsShareBoundedCellJoinsWithoutDiscardingAmbiguousObjects()
	{
		const ushort sectionCount = 4096;
		var body = Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ), sections: sectionCount ).U32( 0 );
		for ( var index = 0; index < sectionCount; index++ )
			Section( body, cellX: 1, cellY: 1, height: index / (float)sectionCount );
		var park = Park( body.ToArray(), duplicateMatches: true );
		Assert.AreEqual( sectionCount, park.CoasterSections.Count );
		var first = park.CoasterSections[0];
		CollectionAssert.AreEqual( new[] { 2, 3 }, first.Objects.Select( record => record.Index ).ToArray() );
		CollectionAssert.AreEqual( new[] { 12, 13 }, first.Attractions.Select( record => record.Id ).ToArray() );
		foreach ( var section in park.CoasterSections )
		{
			// A section-count × scene-count copy would amplify a bounded input into billions of references.
			Assert.AreSame( first.Objects, section.Objects, "one shared scene match collection per cell" );
			Assert.AreSame( first.Attractions, section.Attractions, "one shared attraction match collection per cell" );
		}
		Assert.AreNotEqual( first.Section.HeightParameter, park.CoasterSections[^1].Section.HeightParameter,
			"sharing cell joins must preserve each section's independent raw geometry" );
		Assert.ThrowsException<InvalidDataException>( park.RequireSupportedCoasterRuntime );
	}

	[TestMethod]
	public void RuntimeGuardRefusesOnlyCoastersThatBindToNoStation()
	{
		Park().RequireSupportedCoasterRuntime();
		var body = Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ), sections: 0 ).U32( 0 );
		var park = Park( body.ToArray() );
		Assert.AreEqual( 1u, park.CoasterBlock!.Count );
		var error = Assert.ThrowsException<InvalidDataException>( park.RequireSupportedCoasterRuntime );
		StringAssert.Contains( error.Message, "binds to station mesh 4, which no saved attraction carries" );
		// a block that binds to its station's attraction thing passes the guard; the level restores it
		Park( body.ToArray(), rideMeshInstance: 4 ).RequireSupportedCoasterRuntime();
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

	// [DATA:PreBuilt/prebuilt.TPWS:COAS] the sibling Theme Park Inc prebuilt save's coaster body follows the traced Theme Park World contract (power factor 1.0, one section, no collisions); a structural cross-check on real nonempty bytes, not a Theme Park World proof
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
		Assert.AreEqual( 0u, coaster.IntersectionCount );
		Assert.AreEqual( 0, coaster.AuxiliaryOrdinals.Length );
		var section = coaster.Sections.Single();
		Assert.AreEqual( 0x280u, section.Flags );
		Assert.AreEqual( 37u, section.MeshInstanceId );
		Assert.AreEqual( (65, 67), (section.CellX, section.CellY) );
		Assert.AreEqual( 0, section.StackOrdinal );
		Assert.AreEqual( 0.05555984750390053f, section.HeightParameter );
		Assert.AreEqual( 0f, section.Bank );
		Assert.AreEqual( -0.06808479130268097f, section.Incline );
		Assert.AreEqual( 1.5707963705062866f, section.Yaw );
		Assert.AreEqual( 0f, section.LoopRotate );
		Assert.AreEqual( 0, section.CollisionOrdinals.Length );
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

	/// <summary>One train slot record (27 bytes) in the saver's order (0x43783B).</summary>
	private static Body Train( Body body, uint flags = 0x20, byte cars = 2, float position = 12.5f, float speed = 5f, uint word38 = 7, uint word64 = 8, uint word68 = 9, ushort riders = 3 )
	{
		return body.U32( flags ).U8( cars ).U32( BitConverter.SingleToUInt32Bits( position ) ).U32( BitConverter.SingleToUInt32Bits( speed ) )
			.U32( word38 ).U32( word64 ).U32( word68 ).U16( riders );
	}

	/// <summary>One car record (11 bytes, 0x4378A5) and its two counted lists.</summary>
	private static Body Car( Body body, uint flags, uint mesh, byte byte44, uint[] lap, uint[] station )
	{
		body.U32( flags ).U8( (byte)lap.Length ).U8( (byte)station.Length ).U8( byte44 ).U32( mesh );
		foreach ( var guest in lap.Concat( station ) )
			body.U32( guest );
		return body;
	}

	/// <summary>A coaster of <paramref name="station"/> with no section and the given train slots of two cars.</summary>
	private static Body Coaster( Body body, ushort station, int trains, uint firstGuest )
	{
		Header( body, controller16: station, sections: 0 ).U32( 0 );
		for ( var slot = 0; slot < trains; slot++ )
		{
			Train( body, cars: 2, position: 10f + slot, riders: (ushort)(slot + 1) );
			Car( body, 1, (uint)(200 + slot * 2), 4, new[] { firstGuest + (uint)slot }, new uint[] { 50, 51 } );
			Car( body, 0, (uint)(201 + slot * 2), 0, Array.Empty<uint>(), Array.Empty<uint>() );
		}
		return body;
	}

	[TestMethod]
	public void TheTrainPayloadIsDecodedWithTheTypesTrainAndCarCounts()
	{
		var body = Coaster( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ), 4, trains: 2, firstGuest: 700 );
		var block = Parse( body.ToArray(), trainLayouts: station => station == 4 ? new SaveCoasterTrainLayout( 2, 2 ) : null );
		var coaster = block.Coasters.Single();
		Assert.AreEqual( 2, coaster.TrainSlots!.Count );
		var first = coaster.TrainSlots[0];
		Assert.AreEqual( (0x20u, (byte)2, 10f, 5f, (ushort)1), (first.Flags, first.CarCount, first.Position, first.Speed, first.Riders) );
		Assert.AreEqual( (7u, 8u, 9u), (first.Word38, first.Word64, first.Word68) );
		Assert.AreEqual( 2, first.Cars.Count );
		Assert.AreEqual( (1u, (byte)4, 200u), (first.Cars[0].Flags, first.Cars[0].Car44, first.Cars[0].MeshInstanceId) );
		CollectionAssert.AreEqual( new uint[] { 700 }, first.Cars[0].HeadsA );
		CollectionAssert.AreEqual( new uint[] { 50, 51 }, first.Cars[0].HeadsB );
		Assert.AreEqual( 0, first.Cars[1].HeadsA.Length );
		Assert.AreEqual( 11f, coaster.TrainSlots[1].Position );
		Assert.AreEqual( (ushort)2, coaster.TrainSlots[1].Riders );
		CollectionAssert.AreEqual( new uint[] { 701 }, coaster.TrainSlots[1].Cars[0].HeadsA );
		// The decoded slots take exactly the payload bytes and the offsets point at their records.
		Assert.AreEqual( coaster.Trains.Length, coaster.TrainSlots.Sum( slot => slot.Bytes ) );
		Assert.AreEqual( coaster.TrainsOffset, first.Offset );
		Assert.AreEqual( first.Offset + SaveCoasterTrain.RecordBytes + first.Cars.Sum( car => car.Bytes ), coaster.TrainSlots[1].Offset );
		Assert.AreEqual( SaveCoasterCar.RecordBytes + 4 * 3, first.Cars[0].Bytes );
	}

	[TestMethod]
	public void SeveralCoastersReadWhenEveryTypeLayoutIsKnown()
	{
		var body = new Body().U32( 2 ).U32( 3 ).U32( 4 ).U32( 5 );
		Coaster( body, 4, trains: 2, firstGuest: 700 );
		Coaster( body, 9, trains: 1, firstGuest: 800 );
		var layouts = new Dictionary<ushort, SaveCoasterTrainLayout> { [4] = new( 2, 2 ), [9] = new( 1, 2 ) };
		var block = Parse( body.ToArray(), trainLayouts: station => layouts.TryGetValue( station, out var layout ) ? layout : null );
		Assert.AreEqual( 2u, block.Count );
		Assert.AreEqual( (4, 9), (block.Coasters[0].Controller16 + 0, block.Coasters[1].Controller16 + 0) );
		Assert.AreEqual( 2, block.Coasters[0].TrainSlots!.Count );
		Assert.AreEqual( 1, block.Coasters[1].TrainSlots!.Count );
		Assert.AreEqual( 800u, block.Coasters[1].TrainSlots![0].Cars[0].HeadsA.Single() );
		Assert.AreEqual( block.Coasters[0].TrainsOffset + block.Coasters[0].Trains.Length + SaveCoaster.HeaderBytes + 4, block.Coasters[1].TrainsOffset );
		// Without the layouts only a single coaster reads.
		var error = Assert.ThrowsException<InvalidDataException>( () => Parse( body.ToArray() ) );
		StringAssert.Contains( error.Message, "needs the coaster type's train and car counts" );
		// A coaster whose type is unknown stops the read at it, unless it is the last one, which keeps the rest of the block raw.
		Assert.ThrowsException<InvalidDataException>( () => Parse( body.ToArray(), trainLayouts: station => station == 9 ? new SaveCoasterTrainLayout( 1, 2 ) : null ) );
		var lastRaw = Parse( body.ToArray(), trainLayouts: station => station == 4 ? new SaveCoasterTrainLayout( 2, 2 ) : null );
		Assert.IsNotNull( lastRaw.Coasters[0].TrainSlots );
		Assert.IsNull( lastRaw.Coasters[1].TrainSlots );
		Assert.AreEqual( 27 + (11 + 4 * 3) + 11, lastRaw.Coasters[1].Trains.Length );
	}

	[TestMethod]
	public void ATrainLayoutThatDoesNotMatchTheSaveIsRefused()
	{
		var body = Coaster( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ), 4, trains: 2, firstGuest: 700 ).ToArray();
		var shorter = Assert.ThrowsException<InvalidDataException>( () => Parse( body, trainLayouts: _ => new SaveCoasterTrainLayout( 1, 2 ) ) );
		StringAssert.Contains( shorter.Message, "do not match the save" );
		var longer = Assert.ThrowsException<InvalidDataException>( () => Parse( body, trainLayouts: _ => new SaveCoasterTrainLayout( 3, 2 ) ) );
		StringAssert.Contains( longer.Message, "truncated" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( body, trainLayouts: _ => new SaveCoasterTrainLayout( 2, SaveCoasterTrainLayout.MaximumCarsPerTrain + 1 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Parse( body, trainLayouts: _ => new SaveCoasterTrainLayout( -1, 1 ) ) );
		// A layout of no train slots leaves everything after the sections for the next header.
		var none = Assert.ThrowsException<InvalidDataException>( () => Parse( body, trainLayouts: _ => new SaveCoasterTrainLayout( 0, 0 ) ) );
		StringAssert.Contains( none.Message, "do not match the save" );
	}

	[TestMethod]
	public void TheParkImportFindsACoastersTypeThroughItsStationsMeshInstance()
	{
		var body = Coaster( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ), 321, trains: 2, firstGuest: 700 ).ToArray();
		var asked = new List<int>();
		var park = Park( body, coasterTrains: infoId => { asked.Add( infoId ); return new SaveCoasterTrainLayout( 2, 2 ); }, rideMeshInstance: 321 );
		CollectionAssert.AreEqual( new[] { 1100 }, asked, "the station is the attraction whose mesh instance equals controller +0x10, and its Info.Id names the type" );
		Assert.AreEqual( 2, park.CoasterBlock!.Coasters.Single().TrainSlots!.Count );
		// No attraction with that mesh instance (or two) leaves the layout unknown: the single coaster's trains stay raw.
		var unknown = Park( body, coasterTrains: _ => new SaveCoasterTrainLayout( 2, 2 ), rideMeshInstance: 322 );
		Assert.IsNull( unknown.CoasterBlock!.Coasters.Single().TrainSlots );
		Assert.AreEqual( body.Length - 16 - SaveCoaster.HeaderBytes - 4, unknown.CoasterBlock.Coasters.Single().Trains.Length );
	}

	[TestMethod]
	public void SectionFlagsFollowTheSaversAndTheLoadersMaps()
	{
		// Saver 0x437615..0x4376E0: node flag -> wire bit.
		var saver = new (uint Node, uint Wire)[]
		{
			(0x1, 0x1), (0x2, 0x2), (0x4, 0x4), (0x80, 0x8), (0x40, 0x10), (0x100, 0x20),
			(0x10000, 0x40), (0x20000, 0x80), (0x40000, 0x100), (0x80000, 0x200), (0x200000, 0x400), (0x400000, 0x800),
		};
		foreach ( var (node, wire) in saver )
			Assert.AreEqual( wire, SaveCoasterSectionFlags.ToWire( node ), $"0x{node:X}" );
		// Loader 0x437EA6..0x437F58: wire bit -> node flag, except the three the node constructor derives again.
		foreach ( var (node, wire) in saver )
			Assert.AreEqual( (wire & (uint)SaveCoasterSectionFlags.NotRestored) != 0 ? 0u : node, SaveCoasterSectionFlags.ToNodeFlags( wire ), $"wire 0x{wire:X}" );
		Assert.AreEqual( 0x8u | 0x10u | 0x20u, (uint)SaveCoasterSectionFlags.NotRestored );
		Assert.AreEqual( 0x1u | 0x2 | 0x4 | 0x10000 | 0x20000 | 0x40000 | 0x80000 | 0x200000 | 0x400000, SaveCoasterSectionFlags.ToNodeFlags( 0xFFF ) );
		// The section of a loop node says so, and a 34-byte record names its floats.
		var loop = new SaveCoasterSection( 0, 0x10, 5, 1, 2, 0, 0.25f, 0f, 0f, 1f, 0.5f, Array.Empty<ushort>() );
		Assert.IsTrue( loop.IsLoop );
		Assert.AreEqual( SaveCoasterSectionWire.Loop, loop.Wire );
	}

	[TestMethod]
	public void TheHeaderNamesTheStationTheStateAndTheTrackBits()
	{
		var body = Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ), flags: 0x3, controller8: 0x24, controller16: 77, sections: 0 ).U32( 0 );
		var coaster = Parse( body.ToArray() ).Coasters.Single();
		Assert.AreEqual( (ushort)77, coaster.StationMeshInstanceId );
		Assert.AreEqual( 4u, coaster.State, "the controller state's low nibble (4: open)" );
		Assert.IsTrue( coaster.TrackComplete );
		Assert.IsTrue( coaster.CircuitOpen );
		var closed = Parse( Header( new Body().U32( 1 ).U32( 0 ).U32( 0 ).U32( 1 ), flags: 0x1, sections: 0 ).U32( 0 ).ToArray() ).Coasters.Single();
		Assert.IsTrue( closed.TrackComplete );
		Assert.IsFalse( closed.CircuitOpen );
	}

	[TestMethod]
	public void TheHeaderWordsNameWhatTheLoaderRestores()
	{
		// Synthetic: a breakdown coaster (state word 0x42, power 0) in slot 3 of type 21, two trains in service, four seats, one self-intersection,
		// after the allocator globals of a park whose slots 1..3 are used (used 3, no hole, next 4).
		var body = Header( new Body().U32( 1 ).U32( 3 ).U32( 0 ).U32( 4 ), flags: 0x1, controller8: 0x42, controller12: 21, controller16: 9,
			controller20: 3, controller28: 70, controller32: 66, power: 0f, sections: 0, controller244: 2, controller240: 4, intersections: 1 ).U32( 0 );
		var block = Parse( body.ToArray() );
		Assert.AreEqual( (3u, 0u, 4u), (block.Global1, block.Global2, block.Global3), "the globals are the slot allocator's used slots, holes and cursor" );
		var coaster = block.Coasters.Single();
		Assert.AreEqual( ((ushort)21, (ushort)9, (ushort)3), (coaster.TypeId, coaster.StationMeshInstanceId, coaster.Slot) );
		Assert.AreEqual( ((ushort)2, (ushort)4, 1u), (coaster.TrainsInService, coaster.SeatsPerTrain, coaster.IntersectionCount) );
		Assert.AreEqual( ((ushort)70, (ushort)66), (coaster.Controller28, coaster.Controller32), "the placement values stay raw (not restored)" );
		Assert.AreEqual( (2u, 0f), (coaster.State, coaster.PowerFactor) );
		Assert.AreEqual( 0x40u, coaster.Controller8 & 0x70, "mode bit 0x40: the breakdown" );
		Assert.IsTrue( coaster.TrackComplete );
		Assert.IsFalse( coaster.CircuitOpen );
	}

	[TestMethod]
	public void CollisionListsAreBoundedByTheNativeEightSlots()
	{
		// Synthetic: station node S2's collision list (the auxiliary block) and a pylon's list each fill the native eight slots exactly.
		var full = Enumerable.Range( 3, SaveCoasterBlock.CollisionListSlots ).Select( value => (ushort)value ).ToArray();
		var body = Header( new Body().U32( 1 ).U32( 1 ).U32( 0 ).U32( 2 ), sections: 1 ).U32( (uint)full.Length );
		foreach ( var ordinal in full )
			body.U16( ordinal );
		Section( body, collisions: full );
		var coaster = Parse( body.ToArray() ).Coasters.Single();
		CollectionAssert.AreEqual( full, coaster.AuxiliaryOrdinals );
		CollectionAssert.AreEqual( full, coaster.Sections.Single().CollisionOrdinals );
		Assert.AreEqual( 0, coaster.Trains.Length );

		// A ninth entry would overrun the list (+0xB4..+0xD4) in the original, whose loader stores the count unbounded: refused.
		var over = Enumerable.Range( 3, SaveCoasterBlock.CollisionListSlots + 1 ).Select( value => (ushort)value ).ToArray();
		var sectionOver = Section( Header( new Body().U32( 1 ).U32( 1 ).U32( 0 ).U32( 2 ), sections: 1 ).U32( 0 ), collisions: over );
		var error = Assert.ThrowsException<InvalidDataException>( () => Parse( sectionOver.ToArray() ) );
		StringAssert.Contains( error.Message, "section 0 has 9 collision ordinals" );
		var auxiliaryOver = Header( new Body().U32( 1 ).U32( 1 ).U32( 0 ).U32( 2 ), sections: 0 ).U32( (uint)over.Length );
		foreach ( var ordinal in over )
			auxiliaryOver.U16( ordinal );
		error = Assert.ThrowsException<InvalidDataException>( () => Parse( auxiliaryOver.ToArray() ) );
		StringAssert.Contains( error.Message, "9 auxiliary collision ordinals" );
	}

	[TestMethod]
	public void SectionsFollowTheStationNodesInChainOrder()
	{
		// Synthetic: a complete circuit of three pylons; collision ordinals are chain positions (S0 = 0, S1 = 1, S2 = 2, the pylons 3..5, the end node 6),
		// so a pylon may name a station node or the end node, which are never sections themselves.
		var body = Header( new Body().U32( 1 ).U32( 1 ).U32( 0 ).U32( 2 ), flags: 0x1, sections: 3 ).U32( 1 ).U16( 5 );
		Section( body, flags: (uint)SaveCoasterSectionWire.ManualYaw, field112: 40, cellX: 10, cellY: 20, collisions: new ushort[] { 6 } );
		Section( body, flags: (uint)(SaveCoasterSectionWire.LoopFirst | SaveCoasterSectionWire.Loop), field112: 41, cellX: 11, cellY: 20, field248: 0f );
		Section( body, flags: (uint)SaveCoasterSectionWire.Loop, field112: 42, cellX: 12, cellY: 20, bank: 0f, field220: 0f, field248: 0.5f, collisions: new ushort[] { 2 } );
		var coaster = Parse( body.ToArray() ).Coasters.Single();
		CollectionAssert.AreEqual( new ushort[] { 5 }, coaster.AuxiliaryOrdinals, "S2 collides with the third pylon" );
		Assert.AreEqual( 3, coaster.Sections.Count );
		CollectionAssert.AreEqual( new[] { 40u, 41u, 42u }, coaster.Sections.Select( section => section.MeshInstanceId ).ToArray() );
		CollectionAssert.AreEqual( new ushort[] { 6 }, coaster.Sections[0].CollisionOrdinals );
		CollectionAssert.AreEqual( new ushort[] { 2 }, coaster.Sections[2].CollisionOrdinals );
		Assert.IsTrue( coaster.Sections[1].IsLoop && coaster.Sections[2].IsLoop );
		Assert.AreEqual( 0.5f, coaster.Sections[2].LoopRotate, "a loop node carries its loop rotation in the last float" );
		Assert.AreEqual( 0x1u, SaveCoasterSectionFlags.ToNodeFlags( coaster.Sections[0].Flags ) );
		Assert.AreEqual( 0u, SaveCoasterSectionFlags.ToNodeFlags( coaster.Sections[1].Flags ), "the loop flags come from the node constructor, not the record" );
	}
}
