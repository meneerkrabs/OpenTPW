using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>Asset-free tests for sprites, path grid, queues, needs, determinism and performance of the guest simulation.</summary>
[TestClass]
public class GuestTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	// ---- Sprite formats -------------------------------------------------------------------------

	private static byte[] CreateBank( params (int Width, int Height, int OriginX, int OriginY, byte[][] Rows)[] frames )
	{
		using var output = new MemoryStream();
		var writer = new BinaryWriter( output );
		writer.Write( (ushort)3 );
		writer.Write( (ushort)3 );
		writer.Write( frames.Length );
		writer.Write( 0 );
		for ( var entry = 0; entry < SpriteBankFile.PaletteEntries; entry++ )
			writer.Write( new[] { (byte)entry, (byte)(entry + 1), (byte)(entry + 2), (byte)0xFF } ); // B, G, R, A
		foreach ( var frame in frames )
		{
			var data = frame.Rows.SelectMany( row => new[] { (byte)row.Length }.Concat( row ) ).ToArray();
			writer.Write( data.Length );
			writer.Write( (ushort)frame.Width );
			writer.Write( (ushort)frame.Height );
			writer.Write( (ushort)128 );
			writer.Write( (ushort)128 );
			writer.Write( frame.OriginX );
			writer.Write( frame.OriginY );
			writer.Write( data );
		}
		return output.ToArray();
	}

	[TestMethod]
	public void SpriteBankDecodesSignedRunLengthRowsAndOneBasedPalette()
	{
		// Row 0: 2 transparent, literal [5, 6], 1 transparent. Row 1: run of 5 × index 1.
		var bank = new SpriteBankFile( new MemoryStream( CreateBank( (5, 2, -2, -1, new[] { new byte[] { 0xFE, 0, 2, 5, 6, 0xFF, 0 }, new byte[] { 0xFB, 1 } }) ) ) );
		var frame = bank.Frames.Single();
		CollectionAssert.AreEqual( new byte[] { 0, 0, 5, 6, 0, 1, 1, 1, 1, 1 }, frame.Indices );
		Assert.AreEqual( -2, frame.OriginX );
		Assert.AreEqual( -1, frame.OriginY );
		var rgba = new byte[5 * 2 * 4];
		bank.DecodeRgba( frame, rgba, 5 * 4, 0, 0 );
		CollectionAssert.AreEqual( new byte[] { 0, 0, 0, 0 }, rgba[0..4], "index 0 is transparent" );
		// index 5 → palette entry 4 stored B=4 G=5 R=6 → RGBA 6,5,4.
		CollectionAssert.AreEqual( new byte[] { 6, 5, 4, 255 }, rgba[8..12] );
	}

	[TestMethod]
	public void SpriteBankRejectsRowsThatOverflowOrUnderfill()
	{
		Assert.ThrowsException<InvalidDataException>( () => new SpriteBankFile( new MemoryStream( CreateBank( (2, 1, 0, 0, new[] { new byte[] { 0xFD, 1 } }) ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => new SpriteBankFile( new MemoryStream( CreateBank( (3, 1, 0, 0, new[] { new byte[] { 0xFE, 1 } }) ) ) ) );
		var truncated = CreateBank( (2, 1, 0, 0, new[] { new byte[] { 0xFE, 1 } }) );
		Assert.ThrowsException<InvalidDataException>( () => new SpriteBankFile( new MemoryStream( truncated[..^1] ) ) );
	}

	[TestMethod]
	public void SpriteAnimationTableReadsDirectionMajorSlots()
	{
		var data = new byte[SpriteAnimationFile.FileBytes];
		"ESP_FILE2.00SPR_XX.TPS"u8.CopyTo( data );
		data[0x10C] = 1;
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( SpriteAnimationFile.SlotOffset + 4 ), 135 );
		data[SpriteAnimationFile.SlotOffset + 6] = 8;
		data[SpriteAnimationFile.SlotOffset + 7] = 5;
		var file = new SpriteAnimationFile( new MemoryStream( data ) );
		Assert.AreEqual( "SPR_XX.TPS", file.BankName );
		Assert.AreEqual( 1, file.FlagA );
		var walk = file.Animations[1];
		Assert.AreEqual( 40, walk.FrameCount );
		Assert.AreEqual( 135 + 2 * 8 + 3, walk.GetFrame( 2, 3 ) );
		Assert.IsTrue( file.Animations[3].IsEmpty );
	}

	[DataTestMethod]
	[DataRow( 0f, 1f, 0, false )]   // walking away from the camera
	[DataRow( 1f, 0f, 2, false )]   // walking right
	[DataRow( -1f, 0f, 2, true )]   // walking left: mirrored right-facing frames
	[DataRow( 0f, -1f, 4, false )]  // walking towards the camera
	[DataRow( -0.7f, -0.7f, 3, true )]
	public void SpriteDirectionMirrorsTheLeftHalf( float right, float away, int direction, bool mirror )
	{
		Assert.AreEqual( (direction, mirror), GuestSpriteAtlas.SelectDirection( right, away ) );
	}

	// ---- Path grid ------------------------------------------------------------------------------

	/// <summary>Row y = 5 from x = 0..19 and column x = 10 from y = 0..9.</summary>
	private static GuestPathGrid CreateCross()
	{
		var grid = new GuestPathGrid( 20, 10 );
		for ( var x = 0; x < 20; x++ )
			grid.SetPath( x, 5, true );
		for ( var y = 0; y < 10; y++ )
			grid.SetPath( 10, y, true );
		return grid;
	}

	[TestMethod]
	public void FlowFieldGivesShortestPathsAlongConnectedCells()
	{
		var grid = CreateCross();
		Assert.AreEqual( 29, grid.WalkableCount );
		Assert.AreEqual( 10 + 4, grid.Distance( 0, 5, 10, 9 ) );
		var path = grid.FindPath( 0, 5, 10, 0 );
		Assert.AreEqual( 16, path.Count );
		Assert.AreEqual( (10, 5), path[10] );
		Assert.AreEqual( (10, 0), path[^1] );
		Assert.AreEqual( -1, grid.Distance( 0, 0, 10, 0 ), "non-path cells are unreachable" );
	}

	[TestMethod]
	public void ConnectionBitsSplitAdjacentPathCells()
	{
		var grid = new GuestPathGrid( 3, 1 );
		grid.SetPath( 0, 0, true, 0b0010 );  // links +X
		grid.SetPath( 1, 0, true, 0b1000 );  // links −X only
		grid.SetPath( 2, 0, true, 0 );       // links nothing
		Assert.AreEqual( 1, grid.Distance( 0, 0, 1, 0 ) );
		Assert.AreEqual( -1, grid.Distance( 0, 0, 2, 0 ), "neither (1,0) nor (2,0) links the shared edge" );
		var version = grid.Version;
		grid.SetPath( 2, 0, true, 0b1000 );
		Assert.AreNotEqual( version, grid.Version, "editing the grid invalidates cached flow fields" );
		Assert.AreEqual( 2, grid.Distance( 0, 0, 2, 0 ) );
	}

	[TestMethod]
	public void SaveConnectionBitsMapToGridDirections()
	{
		Assert.AreEqual( 0b0101, GuestPathGrid.ToLinks( SavePathConnections.NegativeY | SavePathConnections.PositiveY | (SavePathConnections)0x80 ) );
		Assert.AreEqual( 0b1010, GuestPathGrid.ToLinks( SavePathConnections.PositiveX | SavePathConnections.NegativeX ) );
	}

	// ---- Synthetic park -------------------------------------------------------------------------

	internal static GuestSettings CreateSettings() => new()
	{
		Types = new() { new GuestType( 70, 300, 30 ), new GuestType( 40, 600, 40 ) },
		ArrivalLaneA = new[] { (0, 0), (2, 0), (2, 2), (2, 4), (0, 5) },
		ArrivalLaneB = new[] { (19, 0), (17, 0), (17, 2), (17, 4), (19, 5) },
		ArrivalTimeBetween = 50,
		ArrivalFixedRate = 4,
		AdmissionFee = 20
	};

	internal static readonly string[] ShopVariables = Enum.GetNames<RideVariables>().Append( "VAR_PEEPID" ).ToArray();
	private const int PeepId = 12;

	/// <summary>The corpus shop protocol (Coconut.RSE shape): take LETMEON, hold the guest 1 s, hand it back via LETMEOFF.</summary>
	internal static RideVM CreateShopScript( IRideScriptEffects effects )
	{
		var on = V( (int)RideVariables.VAR_LETMEON );
		var off = V( (int)RideVariables.VAR_LETMEOFF );
		var onRide = V( (int)RideVariables.VAR_ONRIDE );
		var asm = new Asm()
			.Label( "loop" )
			.I( Opcode.CRIT_LOCK )
			.I( Opcode.TEST, on )
			.I( Opcode.BRANCH_Z, "idle" )
			.I( Opcode.COPY, V( PeepId ), on )
			.I( Opcode.COPY, on, 0 )
			.I( Opcode.ADD, onRide, 1 )
			.I( Opcode.CRIT_UNLOCK )
			.I( Opcode.WAIT, 1000 )
			.Label( "wait" )
			.I( Opcode.TEST, off )
			.I( Opcode.BRANCH_NZ, "wait" )
			.I( Opcode.COPY, off, V( PeepId ) )
			.I( Opcode.ADD, onRide, 0xFFFF )
			.I( Opcode.BRANCH, "loop" )
			.Label( "idle" )
			.I( Opcode.CRIT_UNLOCK )
			.I( Opcode.ENDSLICE )
			.I( Opcode.BRANCH, "loop" );
		return new RideVM( new RideScriptFile( new MemoryStream( asm.Build( ShopVariables ) ) ), new RideVMOptions { Effects = effects, Seed = 3 } );
	}

	private sealed class ScriptedAttraction
	{
		public readonly RideVisitorBridge Bridge;
		public readonly RideVM Script;
		public bool Open = true;

		public ScriptedAttraction( int id, int capacity, int excitement, (int X, int Y) cell, Func<RideVisitorBridge, RideVM> script, GuestNeeds satisfies = GuestNeeds.None, int value = 30 )
		{
			Bridge = new RideVisitorBridge( id, $"Attraction {id}", satisfies == GuestNeeds.None ? RideVisitorKind.Ride : RideVisitorKind.Shop, capacity, excitement, value, satisfies )
			{
				EntranceCell = cell,
				ExitCell = cell
			};
			Script = script( Bridge );
			Bridge.Attach( Script, () => Open );
		}

		public void Step()
		{
			Bridge.HostStep();
			Script.Advance( Tick );
		}
	}

	private static ScriptedAttraction CreateShop( int id, (int X, int Y) cell, int excitement = 70, GuestNeeds satisfies = GuestNeeds.None ) =>
		new( id, 1, excitement, cell, bridge => CreateShopScript( new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ) ), satisfies );

	private static void Run( GuestSimulation simulation, IReadOnlyList<ScriptedAttraction> attractions, int ticks )
	{
		for ( var tick = 0; tick < ticks; tick++ )
		{
			simulation.Tick( Tick );
			foreach ( var attraction in attractions )
				attraction.Step();
		}
	}

	private sealed class RecordingHost : IRideVisitorHost
	{
		public readonly List<string> Events = new();
		public void OnVisitorOffered( IRideVisitorBridge ride, int guestId ) => Events.Add( $"offer {guestId}" );
		public void OnVisitorBoarded( IRideVisitorBridge ride, int guestId ) => Events.Add( $"board {guestId}" );
		public void OnVisitorReleased( IRideVisitorBridge ride, int guestId ) => Events.Add( $"release {guestId}" );
		public void OnVisitorTurnedAway( IRideVisitorBridge ride, int guestId ) => Events.Add( $"away {guestId}" );
	}

	// ---- Queue / visitor protocol ---------------------------------------------------------------

	[TestMethod]
	public void QueueFeedsLetMeOnInOrderAndReleasesThroughLetMeOff()
	{
		var shop = CreateShop( 1, (0, 0) );
		var host = new RecordingHost();
		shop.Bridge.Host = host;
		foreach ( var guest in new[] { 7, 8, 9 } )
			Assert.IsTrue( shop.Bridge.TryJoinQueue( guest ) );
		Assert.IsFalse( shop.Bridge.TryJoinQueue( 8 ), "a guest queues once" );
		Assert.AreEqual( 1, shop.Bridge.GetQueuePosition( 8 ) );
		for ( var tick = 0; tick < 4 * 60; tick++ )
			shop.Step();
		// Capacity 1: the next guest is called only once VAR_ONRIDE < VAR_CAPACITY again, i.e. after the release (0xe1404).
		CollectionAssert.AreEqual( new[] { "offer 7", "board 7", "release 7", "offer 8", "board 8", "release 8", "offer 9", "board 9", "release 9" }, host.Events, string.Join( ", ", host.Events ) );
		Assert.AreEqual( 0, shop.Script[RideVariables.VAR_ONRIDE] );
		Assert.AreEqual( 3, shop.Bridge.ReleasedTotal );
	}

	[TestMethod]
	public void QueueRespectsItsLimitAndClosingTurnsGuestsAway()
	{
		var shop = CreateShop( 1, (0, 0) );
		var host = new RecordingHost();
		shop.Bridge.Host = host;
		// A queue-less shop without a queue path: one front cell gives room for 4 (count < 4 × cells), the data limit is the floor of 4.
		Assert.AreEqual( 4, shop.Bridge.MaximumQueueLength );
		Assert.IsTrue( new[] { 1, 2, 3, 4 }.All( shop.Bridge.TryJoinQueue ) );
		Assert.AreEqual( QueueJoinResult.NoRoom, shop.Bridge.JoinQueue( 5, 0 ), "queue full" );
		shop.Step(); // calls 1, script takes it
		shop.Step();
		shop.Open = false;
		shop.Step();
		CollectionAssert.Contains( host.Events, "away 2" );
		Assert.AreEqual( 0, shop.Bridge.QueueLength );
		Assert.IsFalse( shop.Bridge.TryJoinQueue( 4 ), "closed rides take nobody" );
		shop.Bridge.ReleaseAll();
		CollectionAssert.Contains( host.Events, "release 1" );
	}

	[TestMethod]
	public void LimboOpcodesHoldGuestsForTheirDurationInSeconds()
	{
		var on = V( (int)RideVariables.VAR_LETMEON );
		var off = V( (int)RideVariables.VAR_LETMEOFF );
		var temp = V( PeepId );
		var asm = new Asm()
			.Label( "loop" )
			.I( Opcode.TEST, on )
			.I( Opcode.BRANCH_Z, "release" )
			.I( Opcode.LIMBOSPACE, 0 )
			.I( Opcode.BRANCH_Z, "release" )
			.I( Opcode.LIMBO, on, 2 )
			.I( Opcode.COPY, on, 0 )
			.Label( "release" )
			.I( Opcode.TEST, off )
			.I( Opcode.BRANCH_NZ, "idle" )
			.I( Opcode.UNLIMBO, temp )
			.I( Opcode.BRANCH_Z, "idle" )
			.I( Opcode.WALKOFF, temp )
			.I( Opcode.WALKGET, off )
			.Label( "idle" )
			.I( Opcode.ENDSLICE )
			.I( Opcode.BRANCH, "loop" );
		var bridge = new RideVisitorBridge( 5, "Limbo", RideVisitorKind.Shop, 2, 0, 10 );
		var vm = new RideVM( new RideScriptFile( new MemoryStream( asm.Build( ShopVariables ) ) ), new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1 } );
		bridge.Attach( vm, () => true );
		var host = new RecordingHost();
		bridge.Host = host;
		foreach ( var guest in new[] { 1, 2, 3 } )
			bridge.TryJoinQueue( guest );
		var releaseTimes = new Dictionary<int, double>();
		for ( var tick = 0; tick < 8 * 60; tick++ )
		{
			bridge.HostStep();
			vm.Advance( Tick );
			foreach ( var item in host.Events.Where( x => x.StartsWith( "release" ) ) )
				releaseTimes.TryAdd( int.Parse( item[8..] ), vm.TimeMilliseconds );
			if ( tick == 30 )
				Assert.AreEqual( 2, bridge.Riders.Count, "header limbo size 2 limits LIMBO; the third guest waits" );
		}
		CollectionAssert.AreEquivalent( new[] { 1, 2, 3 }, releaseTimes.Keys.ToArray() );
		Assert.IsTrue( releaseTimes[1] >= 2000 && releaseTimes[1] < 2200, $"LIMBO 2 holds about 2 s, got {releaseTimes[1]}" );
		Assert.IsTrue( releaseTimes[3] >= 4000, "the third guest boards after a limbo slot frees" );
	}

	// ---- Simulation -----------------------------------------------------------------------------

	[TestMethod]
	public void ArrivingGuestsPayAdmissionAndEnterThePark()
	{
		var simulation = new GuestSimulation( CreateCross(), CreateSettings(), 11 );
		var spent = 0;
		simulation.MoneySpent += ( _, amount, attraction ) => spent += attraction == 0 ? amount : 0;
		Run( simulation, Array.Empty<ScriptedAttraction>(), 20 * 60 );
		Assert.IsTrue( simulation.Admissions > 0 );
		Assert.AreEqual( simulation.Admissions * 20, spent );
		Assert.IsTrue( simulation.Guests.Any( guest => guest.State == GuestState.WalkingAround && simulation.Grid.IsWalkable( guest.Cell.X, guest.Cell.Y ) ) );
	}

	[TestMethod]
	public void GuestsPayThroughTheParkEconomy()
	{
		var economy = EconomyTestData.Park();
		var simulation = new GuestSimulation( CreateCross(), CreateSettings(), 11 );
		var bridge = new GuestEconomyBridge( () => economy, simulation );
		simulation.Payments = bridge;
		var spent = 0L;
		simulation.MoneySpent += ( _, amount, _ ) => spent += amount;
		Run( simulation, Array.Empty<ScriptedAttraction>(), 20 * 60 );
		Assert.AreEqual( 0, simulation.Admissions, "the economy's park is closed" );
		Assert.IsTrue( simulation.Departed > 0 || simulation.Guests.All( guest => !simulation.IsInPark( guest ) ) );

		economy.OpenPark();
		economy.SetEntranceFee( 35 );
		Assert.AreEqual( 35, simulation.AdmissionFee, "single source: the economy's entrance fee" );
		var drinks = CreateShop( 2, (0, 5), satisfies: GuestNeeds.Thirst );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1203, out var shop ) );
		bridge.Link( drinks.Bridge.AttractionId, shop!.Id );
		simulation.Register( drinks.Bridge );
		var thirsty = simulation.SpawnInPark( 19, 5 );
		thirsty.Thirst = 120;
		Run( simulation, new[] { drinks }, 60 * 60 );
		Assert.IsTrue( simulation.Admissions > 0 );
		Assert.AreEqual( simulation.Admissions * 35, economy.Ledger.CurrentTotals[LedgerCategory.GateTakings] );
		Assert.AreEqual( simulation.Admissions, economy.Counters[ParkCounters.Admissions] );
		Assert.IsTrue( shop.CustomersThisMonth > 0 );
		Assert.AreEqual( shop.CustomersThisMonth * 30, economy.Ledger.CurrentTotals[LedgerCategory.ShopTakings] );
		Assert.AreEqual( economy.Ledger.CurrentTotals[LedgerCategory.GateTakings] + economy.Ledger.CurrentTotals[LedgerCategory.ShopTakings], spent );
		Assert.AreEqual( simulation.GetStatistics().InPark, bridge.PeopleInPark );
		Assert.IsTrue( bridge.CountHappierThan( 0 ) == bridge.PeopleInPark );
	}

	[TestMethod]
	public void GuestsWithoutAdmissionMoneyTurnBackAtTheTicketBooth()
	{
		var settings = CreateSettings();
		settings.Types = new() { new GuestType( 50, 10, 30 ) };
		settings.StartingCashVarPercent = 0;
		var simulation = new GuestSimulation( CreateCross(), settings, 1 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnArrival()!;
		Run( simulation, Array.Empty<ScriptedAttraction>(), 30 * 60 );
		Assert.AreEqual( 0, simulation.Admissions );
		Assert.AreEqual( GuestThought.Angry, guest.Thought );
		Assert.IsNull( simulation.Find( guest.Id ), "the guest went home" );
		Assert.AreEqual( 1, simulation.Departed );
	}

	[TestMethod]
	public void GuestsChooseQueueRideAndGetHappierFromAMatchingRide()
	{
		var ride = CreateShop( 1, (10, 9), excitement: 70 );
		var settings = CreateSettings();
		settings.Types = new() { new GuestType( 70, 300, 1000 ), new GuestType( 40, 600, 1000 ) };
		var simulation = new GuestSimulation( CreateCross(), settings, 5 ) { ArrivalsEnabled = false };
		simulation.Register( ride.Bridge );
		var guests = Enumerable.Range( 0, 4 ).Select( index => simulation.SpawnInPark( 6, 5 ) ).ToList();
		var previous = guests.ToDictionary( guest => guest.Id, guest => guest.Happiness );
		var firstRideChange = new Dictionary<int, float>();
		var sawQueue = false;
		for ( var tick = 0; tick < 40 * 60; tick++ )
		{
			Run( simulation, new[] { ride }, 1 );
			sawQueue |= ride.Bridge.QueueLength > 0 && guests.Any( guest => guest.State == GuestState.Queueing );
			foreach ( var guest in guests )
			{
				if ( guest.RidesTaken == 1 && !firstRideChange.ContainsKey( guest.Id ) )
					firstRideChange[guest.Id] = guest.Happiness - previous[guest.Id];
				previous[guest.Id] = guest.Happiness;
			}
		}
		Assert.IsTrue( sawQueue, "guests queued behind each other" );
		Assert.IsTrue( ride.Bridge.ReleasedTotal >= 4 );
		foreach ( var guest in guests )
		{
			Assert.IsTrue( guest.RidesTaken >= 1 );
			var type = simulation.Settings.Types[guest.Type];
			var expected = Math.Abs( type.PreferredExcitement - 70 ) <= 5 ? simulation.Settings.PerfectRide : simulation.Settings.OkRide;
			Assert.AreEqual( expected, firstRideChange[guest.Id], 0.001, $"guest {guest.Id} type {guest.Type}" );
			Assert.IsTrue( guest.Nausea > 0, "nausea from the ride excitement" );
		}
	}

	[TestMethod]
	public void ShopsSatisfyNeedsAndNeedsGrowOverTime()
	{
		var drinks = CreateShop( 2, (0, 5), satisfies: GuestNeeds.Thirst );
		var settings = CreateSettings();
		var simulation = new GuestSimulation( CreateCross(), settings, 9 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 19, 5 );
		Run( simulation, Array.Empty<ScriptedAttraction>(), 10 * 60 );
		Assert.AreEqual( 10 * settings.ThirstPerSecond, guest.Thirst, 0.05 );
		Assert.AreEqual( 10 * settings.HungerPerSecond, guest.Hunger, 0.05 );
		Assert.IsTrue( guest.Energy < 100 );
		guest.Thirst = 120;
		simulation.Register( drinks.Bridge );
		var thirsty = true;
		for ( var tick = 0; tick < 60 * 60 && thirsty; tick++ )
		{
			Run( simulation, new[] { drinks }, 1 );
			thirsty = guest.Thirst > 1;
		}
		Assert.IsFalse( thirsty, "the guest walked to the drinks shop and drank" );
	}

	[TestMethod]
	public void GuestsLeaveWhenTheirExitLevelRunsOut()
	{
		var settings = CreateSettings();
		settings.ExitLevelSeconds = 5;
		settings.ExitLevelVarSeconds = 0;
		var simulation = new GuestSimulation( CreateCross(), settings, 2 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 10, 0 );
		var states = new HashSet<GuestState>();
		for ( var tick = 0; tick < 90 * 60 && simulation.Find( guest.Id ) != null; tick++ )
		{
			simulation.Tick( Tick );
			states.Add( guest.State );
		}
		Assert.IsNull( simulation.Find( guest.Id ) );
		Assert.IsTrue( states.Contains( GuestState.LeavingPark ) && states.Contains( GuestState.CrossingRoadHome ) && states.Contains( GuestState.WaitingToGoHome ) );
	}

	[TestMethod]
	public void SameSeedGivesTheSameStateHash()
	{
		ulong RunPark( ulong seed )
		{
			var ride = CreateShop( 1, (10, 9) );
			var simulation = new GuestSimulation( CreateCross(), CreateSettings(), seed );
			simulation.Register( ride.Bridge );
			Run( simulation, new[] { ride }, 60 * 60 );
			Assert.IsTrue( simulation.Guests.Count > 10 );
			return simulation.ComputeStateHash();
		}
		Assert.AreEqual( RunPark( 42 ), RunPark( 42 ) );
		Assert.AreNotEqual( RunPark( 42 ), RunPark( 43 ) );
	}

	[TestMethod]
	public void SixHundredGuestsSimulateFasterThanRealTime()
	{
		var grid = new GuestPathGrid( 64, 64 );
		for ( var y = 0; y < 64; y += 4 )
			for ( var x = 0; x < 64; x++ )
				grid.SetPath( x, y, true );
		for ( var x = 0; x < 64; x += 4 )
			for ( var y = 0; y < 64; y++ )
				grid.SetPath( x, y, true );
		var settings = CreateSettings();
		settings.ArrivalLaneA = null;
		settings.ArrivalLaneB = null;
		settings.ExitLevelSeconds = 10_000;
		var simulation = new GuestSimulation( grid, settings, 7 ) { ArrivalsEnabled = false };
		var rides = Enumerable.Range( 1, 6 ).Select( index => CreateShop( index, (index * 8, 32) ) ).ToArray();
		foreach ( var ride in rides )
			simulation.Register( ride.Bridge );
		var random = new GuestRandom( 1 );
		while ( simulation.Guests.Count < 600 )
		{
			var (x, y) = (random.Next( 64 ), random.Next( 16 ) * 4);
			simulation.SpawnInPark( x, y );
		}
		Run( simulation, rides, 60 ); // warm-up: flow fields
		var watch = Stopwatch.StartNew();
		const int ticks = 10 * FixedStepClock.TicksPerSecond;
		Run( simulation, rides, ticks );
		watch.Stop();
		var perTick = watch.Elapsed.TotalMilliseconds / ticks;
		Console.WriteLine( $"600 guests: {perTick:F3} ms per 60 Hz tick" );
		Assert.AreEqual( 600, simulation.Guests.Count );
		Assert.IsTrue( perTick < 8, $"{perTick:F3} ms per tick leaves no headroom for 60 Hz" );
	}
}
