using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The Theme Park Inc World block and the reader-rule split of a Theme Park Inc payload, on the shipped prebuilt.TPWS (the only Theme Park Inc park save) and on
/// synthetic damage made to it. The prebuilt tests need OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncSaveWorldTests
{
	private static byte[]? prebuilt;

	private static byte[] Prebuilt()
	{
		if ( prebuilt != null )
			return (byte[])prebuilt.Clone();
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH for the Theme Park Inc prebuilt-save fixture." );
		var dataPath = Directory.Exists( Path.Combine( path!, "Data" ) ) ? Path.Combine( path!, "Data" ) : path!;
		using var reader = new SaveReader( Path.Combine( dataPath, "PreBuilt", "prebuilt.TPWS" ) );
		prebuilt = reader.ReadFile();
		return (byte[])prebuilt.Clone();
	}

	private static ThemeParkIncSaveWorld World( byte[] payload ) =>
		ThemeParkIncSaveWorld.Parse( payload, SavePayloadLayout.ParseThemeParkInc( payload ), 128, 128 );

	[TestMethod]
	public void PrebuiltContainerHasTheVersionBlockAndStamp()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH for the Theme Park Inc prebuilt-save fixture." );
		var dataPath = Directory.Exists( Path.Combine( path!, "Data" ) ) ? Path.Combine( path!, "Data" ) : path!;
		using var reader = new SaveReader( Path.Combine( dataPath, "PreBuilt", "prebuilt.TPWS" ) );
		var info = reader.Inspect();
		Assert.IsTrue( info.IsThemeParkInc );
		Assert.AreEqual( "Version: Beta_21\r\n", info.VersionText );
		Assert.AreEqual( 24, info.VersionBlockBytes );
		Assert.AreEqual( 500u, info.Magic );
		Assert.AreEqual( 0x01221985u, info.Stamp );
		Assert.AreEqual( 1692641, info.DecodedLength );
		Assert.AreEqual( 0u, reader.InspectHeader().OnlineFlag );
	}

	/// <summary>The widths of the prebuilt's 19 blocks, each from its reader's rules (Theme Park Inc profile), not from a delimiter search.</summary>
	[TestMethod]
	public void PrebuiltSplitsOnReaderRulesWithTheObservedWidths()
	{
		var payload = Prebuilt();
		Assert.AreEqual( 1692641, payload.Length );
		var layout = SavePayloadLayout.ParseThemeParkInc( payload );
		Assert.AreEqual( SavePayloadProfile.ThemeParkInc, layout.Profile );
		var expected = new (SaveBlock Block, int Length)[]
		{
			(SaveBlock.ActionRecording, 563), (SaveBlock.World, 1_571_114), (SaveBlock.Scripts, 412), (SaveBlock.Particles, 76_252), (SaveBlock.MessageCentre, 476),
			(SaveBlock.Clock, 8), (SaveBlock.VanillaTime, 4), (SaveBlock.GameSystem, 36), (SaveBlock.RideSystem, 13_154), (SaveBlock.TrackRides, 180),
			(SaveBlock.FlyingRides, 452), (SaveBlock.Rsse, 26_446), (SaveBlock.Camera, 48), (SaveBlock.Coasters, 1_226), (SaveBlock.Advisor, 392),
			(SaveBlock.Sound, 1_804), (SaveBlock.Cheat, 2), (SaveBlock.AdvisorScoring, 4), (SaveBlock.UI, 0),
		};
		foreach ( var (block, length) in expected )
			Assert.AreEqual( length, layout[block].Length, block.ToString() );
		// every block's own reader stops exactly at its delimiter (the UI block reads nothing)
		foreach ( var item in layout.Blocks )
		{
			if ( item.Block == SaveBlock.ActionRecording || item.Block == SaveBlock.World || item.Block == SaveBlock.Coasters )
				continue;
			Assert.IsTrue( SaveBlockWidths.TryMeasure( payload, item.Block, item.Offset, out var width, SavePayloadProfile.ThemeParkInc ), item.Block.ToString() );
			Assert.AreEqual( item.Length, width, item.Block.ToString() );
		}
		Assert.IsTrue( ThemeParkIncSaveWorld.TryMeasure( payload, layout[SaveBlock.World].Offset, out var worldWidth ) );
		Assert.AreEqual( layout[SaveBlock.World].Length, worldWidth );
		// the Theme Park World reader rules do not read this payload
		Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( payload ) );
		// the Coasters block is the one block the rules cannot size: the coaster's train slots (30 of one car) are the type's, not the file's
		var coasters = layout.Slice( payload, SaveBlock.Coasters );
		var parsed = SaveCoasterBlock.ParseBlock( coasters, 128, 128, 0, _ => new SaveCoasterTrainLayout( 30, 1 ) );
		Assert.AreEqual( 1u, parsed.Count );
		Assert.AreEqual( 1, parsed.Coasters[0].Sections.Count );
		Assert.AreEqual( 30, parsed.Coasters[0].TrainSlots!.Count );
	}

	[TestMethod]
	public void DelimiterBytesInsideTracedBlocksAreData()
	{
		var payload = Prebuilt();
		var before = SavePayloadLayout.ParseThemeParkInc( payload );
		var camera = before[SaveBlock.Camera];
		// the stored SPSC / MESS / WRLD delimiter bytes inside the 48-byte camera block: reader rules size it, the delimiter search would have split here
		Encoding.ASCII.GetBytes( "CSPS" ).CopyTo( payload, camera.Offset );
		Encoding.ASCII.GetBytes( "SSEM" ).CopyTo( payload, camera.Offset + 4 );
		Encoding.ASCII.GetBytes( "DLRW" ).CopyTo( payload, camera.Offset + 8 );
		var after = SavePayloadLayout.ParseThemeParkInc( payload );
		foreach ( var block in Enum.GetValues<SaveBlock>() )
			Assert.AreEqual( before[block], after[block], block.ToString() );
	}

	[TestMethod]
	public void LayoutRejectsBlocksThatDoNotReadAsThemeParkInc()
	{
		var payload = Prebuilt();
		var layout = SavePayloadLayout.ParseThemeParkInc( payload );
		// the UI block is empty: a trailing byte is not accepted
		Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.ParseThemeParkInc( payload.Append( (byte)0 ).ToArray() ) );
		// a 29-list message centre or a 40-byte camera does not follow the Theme Park Inc delimiters
		var message = layout[SaveBlock.MessageCentre];
		var damaged = (byte[])payload.Clone();
		damaged[message.DelimiterOffset] ^= 0xFF;
		Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.ParseThemeParkInc( damaged ) );
		// World delimiter missing
		damaged = (byte[])payload.Clone();
		damaged[layout[SaveBlock.World].DelimiterOffset] ^= 0xFF;
		Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.ParseThemeParkInc( damaged ) );
		Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.ParseThemeParkInc( payload.AsSpan( 0, layout[SaveBlock.Rsse].Offset + 100 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.ParseThemeParkInc( Array.Empty<byte>() ) );
	}

	[TestMethod]
	public void WorldBlockIsConsumedExactlyAndTyped()
	{
		var payload = Prebuilt();
		var layout = SavePayloadLayout.ParseThemeParkInc( payload );
		var world = ThemeParkIncSaveWorld.Parse( payload, layout, 128, 128 );
		var block = layout[SaveBlock.World];
		Assert.AreEqual( 563, block.Offset );
		Assert.AreEqual( 1_571_114, block.Length );
		// the special rides' tail ends exactly at the World delimiter
		Assert.AreEqual( 4, world.SpecialRides.Count );
		Assert.AreEqual( block.End, world.SpecialRidesOffset + 4 + 4 * ThemeParkIncSpecialRide.Bytes );
		Assert.AreEqual( 812, block.End - world.SpecialRidesOffset );
		// variables (70 bytes), control manager, staff pool, calendar, arrivals, grid
		Assert.AreEqual( ThemeParkIncWorldVariables.SupportedVersion, world.Variables.Version );
		Assert.AreEqual( 563 + 70, world.ObjectControls.Offset );
		Assert.AreEqual( 563 + 70 + ThemeParkIncObjectControls.Bytes, world.StaffPool.Offset );
		Assert.AreEqual( 7066, world.Calendar.Offset );
		Assert.AreEqual( 7098, world.ArrivalOffset );
		Assert.AreEqual( ThemeParkIncSaveWorld.ArrivalBytes, world.Arrivals.Length );
		Assert.AreEqual( 7116, world.Cells.StartOffset );
		Assert.AreEqual( 1449038, world.Cells.EndOffset );
		Assert.AreEqual( 1449038, world.ThingListOffset );
		Assert.AreEqual( 13, Enumerable.Range( 0, 128 * 128 ).Count( index => world.Cells[index % 128, index / 128].HasExtension ) );
		Assert.AreEqual( 128 * 128, Enumerable.Range( 0, 128 * 128 ).Count( index => (world.Cells[index % 128, index / 128].Record.Span[0] & 3) == 3 ) );
		// the first thing's header follows the head and EATC; the things tile the list up to ENOD
		Assert.AreEqual( 37u, world.UsedThingHead );
		Assert.AreEqual( 37, world.Things.Count );
		Assert.AreEqual( 37, world.Things[0].Id );
		var offset = world.ThingListOffset + 8;
		foreach ( var thing in world.Things )
		{
			Assert.AreEqual( offset, thing.Offset, $"thing {thing.Id}" );
			Assert.AreEqual( offset + 16, thing.BodyOffset, $"thing {thing.Id}" );
			offset = thing.End;
		}
		Assert.AreEqual( world.SpecialRidesOffset - 4, offset );
		CollectionAssert.AreEqual( new[] { 'E', 'N', 'O', 'D' }, payload.Skip( offset ).Take( 4 ).Select( value => (char)value ).ToArray() );
		Assert.AreEqual( 0, world.Things[^1].NextId );
		// variables name things of the right model
		Assert.AreEqual( ThemeParkIncThingModel.Bank, world.Find( world.Variables.BankAccount )!.Value.Model );
		Assert.AreEqual( (ushort)37, world.Variables.FirstObject );
		Assert.AreEqual( (ushort)0, world.Variables.FirstGardener );
		// calendar: 2000-01-04 with a rate of 15000
		Assert.AreEqual( 2000, world.Calendar.YearAtLastUpdate );
		Assert.AreEqual( 0, world.Calendar.MonthAtLastUpdate );
		Assert.AreEqual( 4, world.Calendar.DayAtLastUpdate );
		Assert.AreEqual( 15000, world.Calendar.FunnySecsPerRealSec );
		// control manager: 87 used controls, the first is a researched one
		Assert.AreEqual( 87, world.ObjectControls.Controls.Count );
		Assert.AreEqual( 1414, world.ObjectControls.PreviousSearchKey );
		Assert.AreEqual( 0x44c, world.ObjectControls.Controls[0].Id );
		Assert.IsTrue( world.ObjectControls.Controls[0].Researched );
		// staff pool: 29 valid candidates of roles 0-5, three free slots
		Assert.AreEqual( 29, world.StaffPool.Candidates.Count( candidate => candidate.Valid ) );
		Assert.IsTrue( world.StaffPool.Candidates.Where( candidate => candidate.Valid ).All( candidate => candidate.Type < 6 ) );
		CollectionAssert.AreEqual( new[] { 5, 5, 5, 5, 4, 5 }, Enumerable.Range( 0, 6 ).Select( role => world.StaffPool.Candidates.Count( candidate => candidate.Valid && candidate.Type == role ) ).ToArray() );
		Assert.AreEqual( ThemeParkIncStaffPool.Roles, world.StaffPool.StopProducing.Count );
		Assert.IsTrue( world.StaffPool.OpeningPoolGenerated );
	}

	[TestMethod]
	public void ThingChainTypesWhatIsTracedAndBoundsTheRest()
	{
		var payload = Prebuilt();
		var world = World( payload );
		var widths = world.Things.GroupBy( thing => thing.Model ).ToDictionary( group => group.Key, group => group.Select( thing => thing.BodyLength ).Distinct().OrderBy( value => value ).ToArray() );
		CollectionAssert.AreEqual( new[] { 312, 5575 }, widths[ThemeParkIncThingModel.Attraction] );
		Assert.AreEqual( 24, world.Things.Count( thing => thing.Model == ThemeParkIncThingModel.Attraction ) );
		// exact: typed or fixed width
		foreach ( var (model, width) in new[] { (ThemeParkIncThingModel.MechanicHq, 10), (ThemeParkIncThingModel.TagSystem, 12), (ThemeParkIncThingModel.UIMsgReceiver, 8),
			(ThemeParkIncThingModel.Objh, 14), (ThemeParkIncThingModel.Cuts, 10), (ThemeParkIncThingModel.Trnr, 8), (ThemeParkIncThingModel.Bank, 292) } )
		{
			var thing = world.OfModel( model ).Single();
			Assert.IsFalse( thing.MarkerBounded, model.ToString() );
			Assert.AreEqual( width, thing.BodyLength, model.ToString() );
		}
		// marker-bounded raw bodies
		foreach ( var (model, width) in new[] { (ThemeParkIncThingModel.StaffHq, 411), (ThemeParkIncThingModel.AdvisorController, 233), (ThemeParkIncThingModel.ParkAnalyser, 76_617),
			(ThemeParkIncThingModel.ResearchLab, 8_414), (ThemeParkIncThingModel.Weather, 104), (ThemeParkIncThingModel.ChallengeSystem, 1_287) } )
		{
			var thing = world.OfModel( model ).Single();
			Assert.IsTrue( thing.MarkerBounded, model.ToString() );
			Assert.AreEqual( width, thing.BodyLength, model.ToString() );
			Assert.AreEqual( width, world.BodyOf( thing ).Length, model.ToString() );
		}
		Assert.AreEqual( 6, world.SmallThings.Count );
		Assert.AreEqual( (int)world.Variables.BankAccount, world.Bank!.Id );
		Assert.AreEqual( SaveBank.LoanRecords, world.Bank.Loans.Count );
		Assert.IsFalse( world.OfModel( ThemeParkIncThingModel.Guest ).Any() );
		Assert.IsFalse( world.OfModel( ThemeParkIncThingModel.Gardener ).Any() );
	}

	[TestMethod]
	public void AttractionsCarryTheirInfoIdsNamesMeshIdsAndHistories()
	{
		var world = World( Prebuilt() );
		CollectionAssert.AreEqual( new[] { 1414, 1413, 1413, 1403, 1101, 1798, 1750, 1703, 1700, 1701, 1702, 1710, 1707, 3708, 1709, 1706, 1705, 1110, 1170, 1160, 1180, 104, 103, 1601 },
			world.Attractions.Select( item => item.InfoId ).ToArray() );
		CollectionAssert.AreEqual( new[] { 37, 36, 35, 34, 33, 32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14 }, world.Attractions.Select( item => item.Id ).ToArray() );
		CollectionAssert.AreEqual( new uint[] { 62, 61, 60, 59, 53, 52, 51, 50, 49, 48, 47, 46, 45, 44, 43, 42, 41, 40, 39, 38, 4, 3, 2, 1 }, world.Attractions.Select( item => item.MeshInstanceId ).ToArray() );
		CollectionAssert.AreEqual( new[] { "Medium Rock", "End Of Gas", "End Of Gas", "Electric", "Giant Robot", "", "Gate to", "Lake", "Gates", "Fallen Trees", "Bridge", "", "", "", "", "", "", "Changing Hut", "Mechanical", "Waterwheel", "Log Mill", "Miniature", "Miniature", "LAND OF" },
			world.Attractions.Select( item => item.NameLine1 ).ToArray() );
		var withHistory = world.Attractions.Where( item => item.HasHistory ).Select( item => item.InfoId ).ToArray();
		CollectionAssert.AreEqual( new[] { 1101, 1110, 1170, 1160, 1180 }, withHistory );
		foreach ( var item in world.Attractions )
		{
			Assert.AreEqual( item.HasHistory ? 5575 : 312, item.Length, item.NameLine1 );
			Assert.AreEqual( item.HasHistory ? 19 : 0, item.Histories.Count, item.NameLine1 );
			Assert.AreEqual( 2000, item.Timestamp.Year );
			Assert.AreEqual( item.Id, world.Things.Single( thing => thing.Id == item.Id ).Id );
		}
		var robot = world.Attractions.Single( item => item.InfoId == 1101 );
		Assert.AreEqual( 42, robot.CellX );
		Assert.AreEqual( 26, robot.CellY );
		Assert.AreEqual( 13, robot.Histories.Take( ThemeParkIncAttraction.ShortRings ).Count( ring => ring.Entries.Count == 30 ) );
		Assert.AreEqual( 6, robot.Histories.Skip( ThemeParkIncAttraction.ShortRings ).Count( ring => ring.Entries.Count == 144 ) );
		Assert.AreEqual( 5263, ThemeParkIncAttraction.HistoryBytes );
		// the gate, lobby and obstacle variables name attractions
		Assert.AreEqual( 1601, world.FindAttraction( world.Variables.ParkGates )!.InfoId );
		Assert.AreEqual( 103, world.FindAttraction( world.Variables.Lobby )!.InfoId );
		Assert.AreEqual( 1700, world.FindAttraction( world.Variables.CurrentObstacle )!.InfoId );
	}

	[TestMethod]
	public void WorldRejectsMalformedBytes()
	{
		var payload = Prebuilt();
		var layout = SavePayloadLayout.ParseThemeParkInc( payload );
		var world = World( payload );
		var block = layout[SaveBlock.World];

		void Damaged( string what, Action<byte[]> damage )
		{
			var copy = (byte[])payload.Clone();
			damage( copy );
			Assert.ThrowsException<InvalidDataException>( () => World( copy ), what );
		}

		Damaged( "version", copy => copy[block.Offset] = 2 );
		Damaged( "thing marker", copy => copy[world.Things[3].Offset + 9] ^= 0xFF );
		Damaged( "model tag", copy => copy[world.Things[5].Offset + 12] ^= 0xFF );
		Damaged( "unsupported model", copy => copy[world.Things[5].Offset + 4] = 18 );
		Damaged( "list marker", copy => copy[world.ThingListOffset + 4] ^= 0xFF );
		Damaged( "end marker", copy => copy[world.SpecialRidesOffset - 1] ^= 0xFF );
		Damaged( "special rides count", copy => copy[world.SpecialRidesOffset + 2] = 1 );
		Damaged( "has history flag", copy => copy[world.Attractions[0].Offset + ThemeParkIncAttraction.BodyBytes - 1] = 2 );
		var historyAttraction = world.Attractions.First( item => item.HasHistory );
		Damaged( "ring count", copy => copy[historyAttraction.Offset + ThemeParkIncAttraction.BodyBytes + 4] = 29 );
		Damaged( "ring head", copy => copy[historyAttraction.Offset + ThemeParkIncAttraction.BodyBytes] = 77 );
		Damaged( "attraction angle", copy => copy[world.Attractions[0].Offset + 8] = 45 );
		Damaged( "attraction stamp month", copy => copy[world.Attractions[0].Offset + 8 + 4 + 2 + 4] = 13 );
		Damaged( "bank variable names an attraction", copy => copy[block.Offset + 4 + 6] = (byte)world.Attractions[0].Id );
		Damaged( "calendar year", copy => { copy[world.Calendar.Offset + 16] = 0; copy[world.Calendar.Offset + 17] = 0; } );
		Damaged( "calendar rate high bytes", copy => { copy[world.Calendar.Offset + 28] = 0; copy[world.Calendar.Offset + 29] = 0; copy[world.Calendar.Offset + 30] = 0; copy[world.Calendar.Offset + 31] = 0; } );
		Damaged( "object control count", copy => copy[world.ObjectControls.Offset + ThemeParkIncObjectControls.Slots * ThemeParkIncObjectControl.Bytes + 1] = 1 );
		Damaged( "staff candidate flag", copy => copy[world.StaffPool.Offset + 17] = 2 );
		Damaged( "staff candidate role", copy => copy[world.StaffPool.Offset] = 9 );

		// truncation anywhere in the World block
		foreach ( var length in new[] { block.Offset + 10, block.Offset + 6000, world.Cells.StartOffset + 5000, world.Cells.EndOffset - 1, world.ThingListOffset + 4, world.Things[1].Offset + 20,
			world.Things[^1].End, world.SpecialRidesOffset + 2, block.End - 1 } )
		{
			var truncated = payload.AsSpan( 0, length ).ToArray();
			Assert.IsFalse( ThemeParkIncSaveWorld.TryMeasure( truncated, block.Offset, out _ ), $"truncated at {length}" );
		}
		Assert.IsFalse( ThemeParkIncSaveWorld.TryMeasure( payload, -1, out _ ) );
		Assert.IsFalse( ThemeParkIncSaveWorld.TryMeasure( payload, payload.Length, out _ ) );
		// a map that is not 128 x 128 is not the map the loader's fixed 0x4000 cells belong to
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncSaveWorld.Parse( payload, layout, 64, 64 ) );
		Assert.ThrowsException<ArgumentException>( () => ThemeParkIncSaveWorld.Parse( payload.AsMemory( 1 ), layout, 128, 128 ) );
	}

	[TestMethod]
	public void MarkerBoundedBodyMayHoldMarkerLookalikes()
	{
		// a raw body that holds the bytes of a thing header whose model has no tag, or the bytes LDMT alone, is still bounded by the real next header
		var payload = Prebuilt();
		var world = World( payload );
		var analyser = world.OfModel( ThemeParkIncThingModel.ParkAnalyser ).Single();
		var body = analyser.BodyOffset + 100;
		BitConverter.GetBytes( 7u ).CopyTo( payload, body );
		BitConverter.GetBytes( 99u ).CopyTo( payload, body + 4 );
		Encoding.ASCII.GetBytes( "LDMT" ).CopyTo( payload, body + 8 );
		Encoding.ASCII.GetBytes( "XXXX" ).CopyTo( payload, body + 12 );
		Encoding.ASCII.GetBytes( "LDMT" ).CopyTo( payload, body + 300 );
		var damaged = World( payload );
		Assert.AreEqual( analyser.BodyLength, damaged.OfModel( ThemeParkIncThingModel.ParkAnalyser ).Single().BodyLength );
		Assert.AreEqual( world.Things.Count, damaged.Things.Count );
	}

	[TestMethod]
	public void StatusByteGridFollowsTheBitRule()
	{
		// status 1: map cell only; 2: track cell only; 3: both; 4: effects only; 7: all; other bits are not read
		var statuses = new byte[] { 1, 2, 3, 4, 7, 0, 0x81 };
		var payload = new List<byte> { 9, 9 };
		foreach ( var status in statuses )
		{
			payload.Add( status );
			payload.AddRange( Enumerable.Repeat( (byte)(status | 0x10), SaveCellGrid.ThemeParkIncCellBytes( status ) - 1 ) );
		}
		var bytes = payload.ToArray();
		var grid = SaveCellGrid.ParseThemeParkIncAt( bytes, 2, bytes.Length, statuses.Length, 1 );
		Assert.AreEqual( 2, grid.StartOffset );
		Assert.AreEqual( bytes.Length, grid.EndOffset );
		Assert.AreEqual( 1 + 56, SaveCellGrid.ThemeParkIncCellBytes( 1 ) );
		Assert.AreEqual( 1 + 31, SaveCellGrid.ThemeParkIncCellBytes( 2 ) );
		Assert.AreEqual( 1 + 10, SaveCellGrid.ThemeParkIncCellBytes( 4 ) );
		Assert.AreEqual( 1 + 56 + 31 + 10, SaveCellGrid.ThemeParkIncCellBytes( 7 ) );
		Assert.AreEqual( 1, SaveCellGrid.ThemeParkIncCellBytes( 0 ) );
		Assert.AreEqual( 1 + 56, SaveCellGrid.ThemeParkIncCellBytes( 0x81 ) );
		// every cell exposes the 88-byte record (98 with effects); a missing record reads as zeros
		for ( var index = 0; index < statuses.Length; index++ )
		{
			var record = grid[index, 0].Record.Span;
			Assert.AreEqual( 88 + ((statuses[index] & 4) != 0 ? 10 : 0), record.Length, $"cell {index}" );
			Assert.AreEqual( statuses[index], record[0], $"cell {index}" );
			Assert.AreEqual( (statuses[index] & 1) != 0, record[1] != 0 && record[56] != 0, $"map cell {index}" );
			Assert.AreEqual( (statuses[index] & 2) != 0, record[57] != 0 && record[87] != 0, $"track cell {index}" );
			Assert.AreEqual( (statuses[index] & 4) != 0, record.Length == 98 && record[88] != 0 && record[97] != 0, $"effects {index}" );
		}
		Assert.IsFalse( grid[5, 0].IsOccupied );
		Assert.ThrowsException<InvalidDataException>( () => SaveCellGrid.ParseThemeParkIncAt( bytes, 2, bytes.Length - 1, statuses.Length, 1 ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveCellGrid.ParseThemeParkIncAt( bytes, 2, bytes.Length, statuses.Length - 1, 1 ) );
	}

	[TestMethod]
	public void ImportJoinsSceneRecordsToAttractionsByMeshAndInfoId()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH for the Theme Park Inc prebuilt-save fixture." );
		var dataPath = Directory.Exists( Path.Combine( path!, "Data" ) ) ? Path.Combine( path!, "Data" ) : path!;
		using var archive = new WadArchive( Path.Combine( dataPath, "levels/science/terrain.wad" ) );
		var map = new MapFile( new MemoryStream( archive.GetFile( "base.map" )!.GetData() ) );
		var payload = Prebuilt();
		var park = OriginalParkImport.ImportThemeParkInc( payload, map );
		Assert.IsNotNull( park.ThemeParkIncWorld );
		Assert.AreEqual( 23, park.ThemeParkIncObjects.Count );
		foreach ( var item in park.ThemeParkIncObjects )
		{
			Assert.AreEqual( item.Record.InfoId, item.Attraction.InfoId );
			Assert.AreEqual( item.Attraction.Id, park.FindThemeParkIncAttraction( item.Record )!.Id );
			Assert.AreEqual( BitConverter.ToUInt32( payload, item.Record.Offset + 1 ), item.Attraction.MeshInstanceId );
		}
		// the lake (Info.Id 1703) is the one attraction without a scene record
		CollectionAssert.AreEqual( new[] { 1703 }, park.UnrecordedThemeParkIncAttractions.Select( item => item.InfoId ).ToArray() );
		Assert.AreEqual( 1101, park.UnresolvedObjects.Single().InfoId );
	}
}
