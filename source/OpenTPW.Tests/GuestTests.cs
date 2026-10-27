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
public partial class GuestTests
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
		Assert.AreEqual( 0, file.PerformanceCount, "no performance entries" );
	}

	[TestMethod]
	public void SpriteAnimationTableReadsThePerformanceEntries()
	{
		// Hallow SPR_DR's entry (0x14E): 01 05 02 28, program 0 on slot 4 with registers 2 and 40; a second entry with a zero first byte is not counted.
		var data = new byte[SpriteAnimationFile.FileBytes];
		"ESP_FILE2.00SPR_DR.TPS"u8.CopyTo( data );
		new byte[] { 0x01, 0x05, 0x02, 0x28, 0x00, 0x03 }.CopyTo( data, 0x14E );
		var file = new SpriteAnimationFile( new MemoryStream( data ) );
		Assert.AreEqual( 1, file.PerformanceCount );
		Assert.AreEqual( new SpritePerformance( 0, 4, 2, 40 ), file.Performances[0] );
		Assert.AreEqual( new SpritePerformance( -1, 2, 0, 0 ), file.Performances[1] );
		Assert.AreEqual( 4, file.Performances.Count );
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
	internal static GuestPathGrid CreateCross()
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
		// both lanes lie in the cross's upper left quarter, clear of its path row and column (a walker leaves a path cell for plain ground only with the leaving mask); the bus stop
		// has the two rows above it that the stop cells of tp.exe's 0x00500AD0 need, and each entrance is a cell of the path row
		ArrivalLaneA = new[] { (1, 2), (3, 2), (3, 3), (3, 4), (3, 5) },
		ArrivalLaneB = new[] { (8, 2), (6, 2), (6, 3), (6, 4), (6, 5) },
		// A group every five timer units (about 5 s); with no park points the group is 1.2 × NewParkBonus 20 / 6 = 4 guests.
		ArrivalTimeBetween = 5,
		AdmissionFee = 20
	};

	internal static readonly string[] ShopVariables = Enum.GetNames<RideVariables>().Append( "VAR_PEEPID" ).ToArray();
	private const int PeepId = 12;

	/// <summary>The corpus shop protocol (Coconut.RSE shape): take LETMEON, hold the guest 1 s, hand it back via LETMEOFF.</summary>
	internal static RideVM CreateShopScript( IRideScriptEffects effects, RideScriptWorld? world = null, int? seed = 3 )
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
		return new RideVM( new RideScriptFile( new MemoryStream( asm.Build( ShopVariables ) ) ), new RideVMOptions { Effects = effects, World = world, Seed = seed } );
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

	/// <summary>Lays the stand cell of a ride the harness registered without a placement (<see cref="ObjectAccessCells.LayStandCell"/>).</summary>
	internal static void LayStandCell( GuestPathGrid grid, IRideVisitorBridge ride )
	{
		var (x, y) = GuestSimulation.StandCellOf( ride );
		ObjectAccessCells.LayStandCell( grid.Cells, x, y );
	}

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
		// Capacity 1: the next guest is called only once VAR_ONRIDE < VAR_CAPACITY again (0xe1404); the script lowers VAR_ONRIDE as it writes VAR_LETMEOFF, and the ride update runs the admission
		// before its dismiss (tp.exe 0x004E14E0: 0x004E1220, then 0x004E1410), so the next guest is offered in the update that lets the last one off
		CollectionAssert.AreEqual( new[] { "offer 7", "board 7", "offer 8", "release 7", "board 8", "offer 9", "release 8", "board 9", "release 9" }, host.Events, string.Join( ", ", host.Events ) );
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
		Assert.AreEqual( QueueJoinResult.NoRoom, shop.Bridge.JoinQueue( 5 ), "queue full" );
		shop.Step(); // calls 1, script takes it
		shop.Step();
		shop.Open = false;
		// tp.exe 0x4e1220 → 0x4e0450: a closed ride sends one queue head away per ride update; the others wait.
		foreach ( var (gone, left) in new[] { (2, 2), (3, 1), (4, 0) } )
		{
			shop.Step();
			CollectionAssert.Contains( host.Events, $"away {gone}" );
			Assert.AreEqual( left, shop.Bridge.QueueLength );
		}
		// The join (0x4ffbc0) reads no open word: a closed ride still takes a joiner and sends it away from the head.
		Assert.IsTrue( shop.Bridge.TryJoinQueue( 5 ), "the join reads no open word" );
		shop.Step();
		CollectionAssert.Contains( host.Events, "away 5" );
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
			.I( Opcode.WALKON, on, 0, 0, 0, 0, 0, 0 ) // the walk table's entry that WALKOFF and WALKGET need (tp.exe 0x00556F40, 0x005571A0, 0x00557110)
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

	[TestMethod]
	public void HushPushesTheTakenGuestAndHopHandsTheNewestBackWhileTheProtocolDoesTheBoarding()
	{
		// Totem shape: HUSH VAR_LETMEON keeps the id in the script's heap, the host boards the guest when the script consumes VAR_LETMEON,
		// and the unload loop's HOP pops the newest id (LIFO: 3, 2, 1) for WALKOFF; the bridge never sees HUSH or HOP.
		var on = V( (int)RideVariables.VAR_LETMEON );
		var off = V( (int)RideVariables.VAR_LETMEOFF );
		var onRide = V( (int)RideVariables.VAR_ONRIDE );
		var temp = V( PeepId );
		var asm = new Asm()
			.Label( "loop" )
			.I( Opcode.CRIT_LOCK )
			.I( Opcode.TEST, on )
			.I( Opcode.BRANCH_Z, "check" )
			.I( Opcode.HUSH, on )
			.I( Opcode.WALKON, on, 0, 0, 0, 0, 0, 0 ) // the walk table's entry that WALKOFF and WALKGET need (tp.exe 0x00556F40, 0x005571A0, 0x00557110)
			.I( Opcode.COPY, on, 0 )
			.I( Opcode.ADD, onRide, 1 )
			.Label( "check" )
			.I( Opcode.CRIT_UNLOCK )
			.I( Opcode.CMP, onRide, 3 )
			.I( Opcode.BRANCH_NZ, "idle" )
			.Label( "unload" )
			.I( Opcode.TEST, off )
			.I( Opcode.BRANCH_NZ, "unload" )
			.I( Opcode.HOP, temp )
			.I( Opcode.WALKOFF, temp )
			.Label( "walkget" )
			.I( Opcode.WALKGET, off )
			.I( Opcode.BRANCH_Z, "walkget" )
			.I( Opcode.ADD, onRide, 0xFFFF )
			.I( Opcode.BRANCH_NZ, "unload" )
			.I( Opcode.BRANCH, "idle" )
			.Label( "idle" )
			.I( Opcode.ENDSLICE )
			.I( Opcode.BRANCH, "loop" );
		var ride = new ScriptedAttraction( 8, 3, 70, (0, 0), bridge => new RideVM( new RideScriptFile( new MemoryStream( asm.Build( ShopVariables ) ) ),
			new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1 } ), GuestNeeds.None );
		var host = new RecordingHost();
		ride.Bridge.Host = host;
		foreach ( var guest in new[] { 1, 2, 3 } )
			ride.Bridge.TryJoinQueue( guest );
		for ( var tick = 0; tick < 20 * 60; tick++ )
			ride.Step();
		CollectionAssert.AreEqual( new[] { "board 1", "board 2", "board 3", "release 3", "release 2", "release 1" },
			host.Events.Where( item => item.StartsWith( "board" ) || item.StartsWith( "release" ) ).ToArray(), string.Join( ", ", host.Events ) );
		Assert.AreEqual( 0, ride.Script.HeapCount, "every pushed id was popped" );
		Assert.AreEqual( 0, ride.Script.UnimplementedEffects.Count, "HUSH and HOP never reached the effects" );
	}

	[TestMethod]
	public void AGuestTakenAndHandedToLetMeOffInOneSliceIsBoardedThenReleased()
	{
		var on = V( (int)RideVariables.VAR_LETMEON );
		var off = V( (int)RideVariables.VAR_LETMEOFF );
		var temp = V( PeepId );
		var asm = new Asm()
			.Label( "loop" )
			.I( Opcode.CRIT_LOCK ).I( Opcode.TEST, on ).I( Opcode.BRANCH_Z, "idle" )
			.I( Opcode.HUSH, on ).I( Opcode.HOP, temp ).I( Opcode.COPY, off, temp ).I( Opcode.COPY, on, 0 )
			.Label( "idle" ).I( Opcode.CRIT_UNLOCK ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" );
		var ride = new ScriptedAttraction( 9, 2, 70, (0, 0), bridge => new RideVM( new RideScriptFile( new MemoryStream( asm.Build( ShopVariables ) ) ),
			new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1 } ), GuestNeeds.None );
		var host = new RecordingHost();
		ride.Bridge.Host = host;
		foreach ( var guest in new[] { 1, 2 } )
			ride.Bridge.TryJoinQueue( guest );
		for ( var tick = 0; tick < 10 * 60; tick++ )
			ride.Step();
		CollectionAssert.AreEqual( new[] { "board 1", "release 1", "board 2", "release 2" }, host.Events.Where( item => item.StartsWith( "board" ) || item.StartsWith( "release" ) ).ToArray(), string.Join( ", ", host.Events ) );
		Assert.AreEqual( 0, ride.Bridge.Riders.Count, "nobody is stranded aboard" );
	}

	private static byte[] LegacyUnloadProgram() => new Asm().WithHeader( 8, 2 )
		.I( Opcode.WALKON, 1, 0, 0, 0, 0, 0, 0 ).I( Opcode.WALKON, 2, 0, 0, 0, 0, 0, 0 ).I( Opcode.WALKON, 3, 0, 0, 0, 0, 0, 0 )
		.I( Opcode.COPY, V( (int)RideVariables.VAR_ONRIDE ), 3 )
		.Label( "closed" ).I( Opcode.ENDSLICE ).I( Opcode.TEST, V( (int)RideVariables.VAR_RIDECLOSED ) ).I( Opcode.BRANCH_Z, "closed" )
		.Label( "unload" )
		.I( Opcode.TEST, V( (int)RideVariables.VAR_LETMEOFF ) ).I( Opcode.BRANCH_NZ, "unload" )
		.I( Opcode.HOP, V( PeepId ) ).I( Opcode.WALKOFF, V( PeepId ) )
		.Label( "walkget" ).I( Opcode.WALKGET, V( (int)RideVariables.VAR_LETMEOFF ) ).I( Opcode.BRANCH_Z, "walkget" )
		.I( Opcode.ADD, V( (int)RideVariables.VAR_ONRIDE ), 0xFFFF ).I( Opcode.BRANCH_NZ, "unload" )
		.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( ShopVariables );

	/// <summary>Gives the ride's queue its one cell, so its snapshots carry queue geometry.</summary>
	private static ScriptedAttraction WithQueueCell( ScriptedAttraction ride )
	{
		var grid = new GuestPathGrid( 4, 4 );
		grid.SetPath( 0, 0, true );
		ride.Bridge.RecomputeQueue( grid );
		return ride;
	}

	private static ScriptedAttraction LegacyRide() => WithQueueCell( new( 11, 3, 70, (0, 0), bridge => new RideVM( new RideScriptFile( new MemoryStream( LegacyUnloadProgram() ) ),
		new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1 } ), GuestNeeds.None ) );

	[TestMethod]
	public void RidersOfASaveFromBeforeTheScriptHeapAreHandedBackByHopInTheOldOrder()
	{
		var saved = LegacyRide();
		for ( var tick = 0; tick < 5; tick++ )
			saved.Step();
		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, saved.Bridge.Riders.ToArray() );
		// The old save: VM snapshot without the array, bridge snapshot without the marker (the old HOP handed out the first boarded guest not yet hopped).
		var legacyVm = saved.Script.CaptureSnapshot() with { Stack = null, StackPointer = 0, HeapCount = 0, LimboValues = null, LimboExpiries = null, LimboCount = 0, CallStack = Array.Empty<int>() };
		var legacyBridge = saved.Bridge.CaptureSnapshot() with { VmOwnsHeapAndLimbo = false };
		var loaded = ParkResumeJson.Deserialize<RideVisitorBridgeSnapshot>( ParkResumeJson.Serialize( legacyBridge ) );
		Assert.IsFalse( loaded.VmOwnsHeapAndLimbo );

		var resumed = LegacyRide();
		var host = new RecordingHost();
		resumed.Bridge.Host = host;
		resumed.Script.RestoreSnapshot( legacyVm );
		resumed.Bridge.RestoreSnapshot( loaded );
		Assert.AreEqual( 3, resumed.Script.HeapCount, "the three riders were moved into the script's heap" );
		resumed.Script[RideVariables.VAR_RIDECLOSED] = 1;
		for ( var tick = 0; tick < 5 * 60; tick++ )
			resumed.Step();
		CollectionAssert.AreEqual( new[] { "release 1", "release 2", "release 3" }, host.Events.Where( item => item.StartsWith( "release" ) ).ToArray(), "the old HOP order: first boarded first" );
		Assert.AreEqual( 0, resumed.Bridge.Riders.Count );
		Assert.IsTrue( resumed.Bridge.CaptureSnapshot().VmOwnsHeapAndLimbo );
	}

	[TestMethod]
	public void RidersWithoutRoomInAnOldScriptsHeapAreReleasedAtTheFirstHostStep()
	{
		var saved = LegacyRide();
		for ( var tick = 0; tick < 5; tick++ )
			saved.Step();
		var legacyVm = saved.Script.CaptureSnapshot() with { Stack = null, StackPointer = 0, HeapCount = 0, LimboValues = null, LimboExpiries = null, LimboCount = 0, CallStack = Array.Empty<int>() };
		var legacyBridge = saved.Bridge.CaptureSnapshot() with { VmOwnsHeapAndLimbo = false };
		// A script whose heap holds only two of the three: the third (boarded last) would never come back through HOP.
		var smaller = new Asm().WithHeader( 2, 2 ).I( Opcode.HOP, V( 0 ) ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( ShopVariables );
		var resumed = WithQueueCell( new ScriptedAttraction( 11, 3, 70, (0, 0), bridge => new RideVM( new RideScriptFile( new MemoryStream( smaller ) ),
			new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1 } ), GuestNeeds.None ) );
		var host = new RecordingHost();
		resumed.Bridge.Host = host;
		resumed.Script[RideVariables.VAR_ONRIDE] = 3;
		resumed.Bridge.RestoreSnapshot( legacyBridge );
		Assert.AreEqual( 2, resumed.Script.HeapCount );
		resumed.Bridge.HostStep();
		CollectionAssert.AreEqual( new[] { "release 3" }, host.Events.Where( item => item.StartsWith( "release" ) ).ToArray() );
		Assert.AreEqual( 2, resumed.Script[RideVariables.VAR_ONRIDE], "its share of VAR_ONRIDE was given back" );
		Assert.AreEqual( 2, resumed.Bridge.Riders.Count );
	}

	[TestMethod]
	public void ALimboListOfASaveFromBeforeTheScriptTableMovesIntoTheScriptWithItsDeadline()
	{
		var idle = new Asm().I( Opcode.WALKON, 7, 0, 0, 0, 0, 0, 0 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( ShopVariables );
		ScriptedAttraction Make() => WithQueueCell( new( 12, 2, 70, (0, 0), bridge => new RideVM( new RideScriptFile( new MemoryStream( idle ) ),
			new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1 } ), GuestNeeds.None ) );
		var saved = Make();
		saved.Step();
		var legacy = saved.Bridge.CaptureSnapshot() with { VmOwnsHeapAndLimbo = false, Limbo = new[] { (Guest: 7, Until: 3000.0) } };
		var resumed = Make();
		resumed.Bridge.RestoreSnapshot( legacy );
		Assert.AreEqual( 1, resumed.Script.LimboCount );
		Assert.AreEqual( (7, 3000 + (int)resumed.Script.ClockOriginMilliseconds), resumed.Script.GetLimboSlot( 0 ), "the script-clock deadline became a scheduler-clock expiry" );
		Assert.AreEqual( 0, resumed.Script.HeapCount, "a limbo guest is not also handed out by HOP" );
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
	public void TheTicketAverageTruncatesEachGuestAndIsZeroWhileTheParkIsClosed()
	{
		// 0xc19e4 (PPC-scenarios): the mean of each in-park guest's (u8)trunc(happiness), 0.0 while the park is closed.
		var economy = EconomyTestData.Park();
		var simulation = new GuestSimulation( CreateCross(), CreateSettings(), 11 );
		var bridge = new GuestEconomyBridge( () => economy, simulation );
		var first = simulation.SpawnInPark( 19, 5 );
		var second = simulation.SpawnInPark( 19, 5 );
		first.Happiness = 75.5f;
		second.Happiness = 74.5f;
		Assert.AreEqual( 0f, bridge.AverageHappiness, "closed park" );
		economy.OpenPark();
		Assert.AreEqual( 74.5f, bridge.AverageHappiness, "(75 + 74) / 2, not (75.5 + 74.5) / 2" );
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
		// 40 s (was 20 s): turning back at the closed booth is a lane walk of 12 cells there and back, now at the traced
		// per-guest speed (WALK-I: from s = 0, at most 0.48 cell/s for the slowest base speed) plus the 1 s booth and 3 s bus
		// wait; under the old 1 cell/s no guest needed more than 20 s.
		Run( simulation, Array.Empty<ScriptedAttraction>(), 40 * 60 );
		Assert.AreEqual( 0, simulation.Admissions, "the economy's park is closed" );
		Assert.IsTrue( simulation.Departed > 0 || simulation.Guests.All( guest => !simulation.IsInPark( guest ) ) );

		economy.OpenPark();
		// The booth's verdict (tp.exe 0x004FF5B0): with no attraction points the park value is MinimumEntryFee 20, so 24 is fair (below 1.25 × 20)
		// and 35 would be on the expensive side, where guests wait and lose happiness until they go home.
		economy.SetEntranceFee( 24 );
		Assert.AreEqual( 24, simulation.AdmissionFee, "single source: the economy's entrance fee" );
		var drinks = CreateShop( 2, (0, 5), satisfies: GuestNeeds.Thirst );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1203, out var shop ) );
		bridge.Link( drinks.Bridge.AttractionId, shop!.Id );
		LayStandCell( simulation.Grid, drinks.Bridge );
		simulation.Register( drinks.Bridge );
		var thirsty = simulation.SpawnInPark( 19, 5 );
		thirsty.Thirst = 120;
		Run( simulation, new[] { drinks }, 60 * 60 );
		Assert.IsTrue( simulation.Admissions > 0 );
		Assert.AreEqual( simulation.Admissions * 24, economy.Ledger.CurrentTotals[LedgerCategory.GateTakings] );
		Assert.AreEqual( simulation.Admissions, economy.Counters[ParkCounters.Admissions] );
		Assert.IsTrue( shop.CustomersThisMonth > 0 );
		Assert.AreEqual( shop.CustomersThisMonth * 30, economy.Ledger.CurrentTotals[LedgerCategory.ShopTakings] );
		Assert.AreEqual( economy.Ledger.CurrentTotals[LedgerCategory.GateTakings] + economy.Ledger.CurrentTotals[LedgerCategory.ShopTakings], spent );
		Assert.AreEqual( simulation.GetStatistics().InPark, bridge.PeopleInPark );
		Assert.IsTrue( bridge.CountHappierThan( 0 ) == bridge.PeopleInPark );
	}

	[TestMethod]
	public void TheBoothBooksTheFeeWithoutReadingTheGuestsPurse()
	{
		// tp.exe 0x004FF9D0: the paying verdict books the fee with the bank deposit 0x004D0600; the guest's cash +0x1A0 is neither tested nor debited.
		var settings = CreateSettings();
		settings.Types = new() { new GuestType( 50, 10, 30 ) };
		settings.StartingCashVarPercent = 0;
		var simulation = new GuestSimulation( CreateCross(), settings, 1 ) { ArrivalsEnabled = false };
		var spent = 0L;
		simulation.MoneySpent += ( _, amount, attraction ) => spent += attraction == 0 ? amount : 0;
		var guest = simulation.SpawnArrival()!;
		var purse = guest.Money;
		Assert.IsTrue( purse < settings.AdmissionFee, "the guest's purse is short of the fee" );
		Run( simulation, Array.Empty<ScriptedAttraction>(), 30 * 60 );
		Assert.AreEqual( 1, simulation.Admissions );
		Assert.AreEqual( settings.AdmissionFee, spent, "the fee is booked" );
		Assert.AreEqual( purse, guest.Money, "the purse is untouched" );
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
		for ( var tick = 0; tick < 120 * 60; tick++ )
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
	public void NeedsGrowOnlyOnTheGuestsPhaseAndAShopServesThem()
	{
		var drinks = CreateShop( 2, (0, 5), satisfies: GuestNeeds.Thirst );
		drinks.Bridge.ThirstEffect = 100;
		var settings = CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		var simulation = new GuestSimulation( CreateCross(), settings, 9 ) { ArrivalsEnabled = false };
		var guests = Enumerable.Range( 0, 4 ).Select( _ => simulation.SpawnInPark( 19, 5 ) ).ToList();
		foreach ( var guest in guests )
			guest.Hunger = guest.Thirst = guest.Toilet = 0;
		// 32 park turns hold two turns that are multiples of 16: only the guest with id & 3 == 0 meets them on its phase (0xeed24, 0xeeebc)
		var start = simulation.ParkTurn;
		while ( simulation.ParkTurn < start + 33 )
			Run( simulation, Array.Empty<ScriptedAttraction>(), 1 );
		var fixedSteps = Enumerable.Range( (int)start + 1, 33 ).Count( turn => turn % 16 == 0 );
		foreach ( var guest in guests )
		{
			var phaseZero = (guest.Id & 3) == 0;
			Assert.AreEqual( phaseZero ? 2f * fixedSteps : 0f, guest.Hunger, $"guest {guest.Id} hunger" );
			Assert.AreEqual( phaseZero ? 2f * fixedSteps : 0f, guest.Thirst, $"guest {guest.Id} thirst" );
			Assert.AreEqual( phaseZero ? 1f * fixedSteps : 0f, guest.Toilet, $"guest {guest.Id} toilet" );
			Assert.AreEqual( 0f, guest.Energy, "nothing but a completed use changes the meter +0x1B8 (tp.exe 0x004FD970)" );
		}
		var thirsty = guests[0];
		thirsty.Thirst = 90;
		LayStandCell( simulation.Grid, drinks.Bridge );
		simulation.Register( drinks.Bridge );
		var served = false;
		for ( var tick = 0; tick < 60 * 60 && !served; tick++ )
		{
			Run( simulation, new[] { drinks }, 1 );
			served = thirsty.Thirst < 1;
		}
		Assert.IsTrue( served, "the thirsty guest walked to the drinks shop and its ThirstEffect emptied the meter" );
	}

	/// <summary>
	/// PPC oracle finding F20 (<c>walk-boost</c>): the only event that raises the speed boost +196 is a completed use of an object whose type has
	/// the special ingredient Sugar (the ice cream shops, <c>UsageInfo.SpecialIngredient</c> 4): trunc(6 × amount / 100), amount 50 at build.
	/// </summary>
	[TestMethod]
	public void LeavingASugarShopRaisesTheSpeedBoostAndOtherShopsDoNot()
	{
		var simulation = new GuestSimulation( CreateCross(), CreateSettings(), 9 ) { ArrivalsEnabled = false };
		var iceCream = CreateShop( 2, (0, 5), satisfies: GuestNeeds.Hunger );
		var burgers = CreateShop( 3, (0, 6), satisfies: GuestNeeds.Hunger );
		iceCream.Bridge.SpecialIngredient = GuestSpeed.SugarIngredient;
		burgers.Bridge.SpecialIngredient = 1;
		var host = (IRideVisitorHost)simulation;
		var guest = simulation.SpawnInPark( 19, 5 );
		Assert.AreEqual( (ushort)0, guest.Boost );
		host.OnVisitorReleased( burgers.Bridge, guest.Id );
		Assert.AreEqual( (ushort)0, guest.Boost, "Fat does not touch the boost" );
		host.OnVisitorReleased( iceCream.Bridge, guest.Id );
		Assert.AreEqual( (ushort)3, guest.Boost, "trunc(6 x 50 / 100)" );
		iceCream.Bridge.IngredientAmount = 100;
		host.OnVisitorReleased( iceCream.Bridge, guest.Id );
		Assert.AreEqual( (ushort)9, guest.Boost, "a second visit adds on top: 3 + 6" );
	}

	/// <summary>
	/// The choice loop (tp.exe 0x004FCB10) walks the object list newest first (0x00519D80 pushes each new object at the head) and takes an equal
	/// score only on an odd world turn, so of two attractions that tie the newest wins on an even turn and the oldest on an odd one (GUEST-002).
	/// </summary>
	[TestMethod]
	public void ATieGoesToTheNewestAttractionOnAnEvenTurnAndTheOldestOnAnOddOne()
	{
		var simulation = new GuestSimulation( CreateCross(), CreateSettings(), 9 ) { ArrivalsEnabled = false };
		var older = CreateShop( 1, (5, 5) );
		var newer = CreateShop( 2, (15, 5) );
		simulation.Register( newer.Bridge );
		simulation.Register( older.Bridge );
		var guest = simulation.SpawnInPark( 10, 5 );
		var score = simulation.ScoreAttraction( guest, older.Bridge );
		Assert.AreEqual( score, simulation.ScoreAttraction( guest, newer.Bridge ), "the two tie" );
		Assert.IsTrue( score >= AttractionScore.CandidateFloor, $"score {score}" );
		var seen = new HashSet<long>();
		while ( seen.Count < 2 )
		{
			var parity = simulation.ParkTurn & 1;
			seen.Add( parity );
			Assert.AreEqual( parity == 0 ? 2 : 1, simulation.PeekChoice( guest )?.AttractionId, $"turn {simulation.ParkTurn}" );
			simulation.Tick( Tick );
			(guest.CellX, guest.CellY) = (10, 5);
		}
	}

	/// <summary>
	/// The minor decision of a guest walking to a ride (tp.exe 0x004FD570, every twelfth update of state 10): a score-qualified ride in the 4 x 4 window
	/// round the guest that is nearer to the target's back cell and whose route to the guest is shorter replaces the target (GUEST-018).
	/// </summary>
	[TestMethod]
	public void AGuestWalkingToARideSwitchesToANearerRideInItsWindow()
	{
		var simulation = new GuestSimulation( CreateCross(), CreateSettings(), 9 ) { ArrivalsEnabled = false };
		var far = CreateShop( 1, (15, 5) );
		var near = CreateShop( 2, (11, 5) );
		var outside = CreateShop( 3, (5, 5) );
		simulation.Register( far.Bridge );
		simulation.Register( near.Bridge );
		simulation.Register( outside.Bridge );
		var guest = simulation.SpawnInPark( 10, 5 );
		guest.AttractionId = far.Bridge.AttractionId;
		guest.State = GuestState.GoingToRide;
		simulation.MinorDecision( guest );
		Assert.AreEqual( near.Bridge.AttractionId, guest.AttractionId, "the ride beside the guest, nearer to the target's back cell, is the better one" );
		// from the near ride the far one is not nearer to its back cell, and the ride behind the guest is outside the window
		simulation.MinorDecision( guest );
		Assert.AreEqual( near.Bridge.AttractionId, guest.AttractionId );
		// a guest without a target decides nothing
		guest.AttractionId = 0;
		simulation.MinorDecision( guest );
		Assert.AreEqual( 0, guest.AttractionId );
	}

	/// <summary>
	/// The original's meter +0x1B8 (OpenTPW's <c>Energy</c>): 0 for a new guest (tp.exe 0x004FAEC0), and a completed use takes the type's
	/// <c>UsageInfo.FatigueEffect</c> off it through the clamped adder (0x004FD970, 0x004FB4F0); walking, waiting and riding leave it alone.
	/// </summary>
	[TestMethod]
	public void OnlyACompletedUseMovesTheMeterAtPlus1B8()
	{
		var simulation = new GuestSimulation( CreateCross(), CreateSettings(), 9 ) { ArrivalsEnabled = false };
		var shop = CreateShop( 2, (0, 5), satisfies: GuestNeeds.Hunger );
		var host = (IRideVisitorHost)simulation;
		var guest = simulation.SpawnInPark( 19, 5 );
		Assert.AreEqual( 0f, guest.Energy, "a new guest starts at 0" );
		Run( simulation, new[] { shop }, 600 );
		Assert.AreEqual( 0f, guest.Energy, "ten seconds in the park change nothing" );
		shop.Bridge.FatigueEffect = 5;
		host.OnVisitorReleased( shop.Bridge, guest.Id );
		Assert.AreEqual( 0f, guest.Energy, "the shipped FatigueEffect 5 is clamped at 0" );
		shop.Bridge.FatigueEffect = -30;
		host.OnVisitorReleased( shop.Bridge, guest.Id );
		Assert.AreEqual( 30f, guest.Energy, "a negative effect raises it" );
		shop.Bridge.FatigueEffect = -90;
		host.OnVisitorReleased( shop.Bridge, guest.Id );
		Assert.AreEqual( 100f, guest.Energy, "and clamps at 100" );
	}

	/// <summary>
	/// The rest of tp.exe's use routine 0x004FE1E0 after the litter (GUEST-005): the small-portion tests (0x004FE436, 0x004FE488), the scaled
	/// happiness (0x004FE4CF), the special ingredients Fat, Salt and Ice (0x004FE527) and the relief block's toilet speed term (0x004FE7F5).
	/// Shops of excitement 0 keep the ride outcome out of the way.
	/// </summary>
	[TestMethod]
	public void AUseAppliesThePortionHappinessIngredientAndReliefSteps()
	{
		var simulation = new GuestSimulation( CreateCross(), CreateSettings(), 9 ) { ArrivalsEnabled = false };
		var host = (IRideVisitorHost)simulation;
		var guest = simulation.SpawnInPark( 19, 5 );
		void Set( float happiness, float thirst, float hunger, float toilet ) =>
			(guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea) = (happiness, thirst, hunger, toilet, 0);

		var burgers = CreateShop( 2, (0, 5), excitement: 0, satisfies: GuestNeeds.Hunger );
		burgers.Bridge.HungerEffect = 40;
		burgers.Bridge.HappinessEffect = 10;
		burgers.Bridge.SpecialIngredient = 1;
		Set( 50, 30, 80, 20 );
		host.OnVisitorReleased( burgers.Bridge, guest.Id );
		Assert.AreEqual( (65f, 30f, 40f, 70f), (guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet),
			"happiness 50 + 10 + trunc(50 × 10 / 100); 50 + 40 is no small portion; Fat adds the amount 50 to the toilet meter" );

		var fries = CreateShop( 3, (0, 6), excitement: 0, satisfies: GuestNeeds.Hunger );
		fries.Bridge.SpecialIngredient = 2;
		Set( 50, 30, 80, 20 );
		host.OnVisitorReleased( fries.Bridge, guest.Id );
		Assert.AreEqual( (50f, 80f), (guest.Happiness, guest.Thirst), "Salt adds the amount to thirst; no effect, so no draw and no penalty" );

		var drinks = CreateShop( 4, (0, 7), excitement: 0, satisfies: GuestNeeds.Thirst );
		drinks.Bridge.ThirstEffect = 60;
		drinks.Bridge.SpecialIngredient = 3;
		Set( 50, 90, 80, 20 );
		host.OnVisitorReleased( drinks.Bridge, guest.Id );
		Assert.AreEqual( (50f, 60f), (guest.Happiness, guest.Thirst), "thirst 90 − 60, then Ice adds trunc(50 × 60 / 100)" );

		var crumbs = CreateShop( 5, (0, 8), excitement: 0, satisfies: GuestNeeds.Hunger );
		crumbs.Bridge.HungerEffect = 2;
		crumbs.Bridge.IngredientAmount = 1;
		Set( 50, 30, 80, 20 );
		host.OnVisitorReleased( crumbs.Bridge, guest.Id );
		Assert.AreEqual( 45f, guest.Happiness, "(r & 7) + 1 + 2 is always below 30: the small step 5" );

		var toilet = CreateShop( 6, (0, 9), excitement: 0, satisfies: GuestNeeds.Toilet );
		Set( 50, 30, 80, 95 );
		guest.ToiletBonus = 0;
		host.OnVisitorReleased( toilet.Bridge, guest.Id );
		Assert.AreEqual( (0f, GuestSpeed.ToiletBonus), (guest.Toilet, guest.ToiletBonus), "a relief visit empties the meter and sets the speed term to 25" );
	}

	[TestMethod]
	public void ASugarBoostIsAddedOnceAndDecaysWithinAFewTurns()
	{
		Assert.AreEqual( (ushort)3, GuestSpeed.RaiseBoost( 0, GuestSpeed.SugarIngredient, GuestSpeed.DefaultIngredientAmount ) );
		var boost = GuestSpeed.RaiseBoost( 0, GuestSpeed.SugarIngredient, GuestSpeed.DefaultIngredientAmount );
		var turns = 0;
		var speed = 1f;
		var baseline = 1f;
		var peak = 0f;
		while ( boost > 0 )
		{
			(speed, boost, _, _) = GuestSpeed.Update( speed, 100, 0, boost );
			baseline = GuestSpeed.Smooth( baseline, 100, 0, 0 );
			peak = Math.Max( peak, speed - baseline );
			turns++;
		}
		Assert.AreEqual( 3, turns, "3 -> 2 -> 1 -> 0 (the decay is trunc(boost x 99 / 100))" );
		Assert.IsTrue( peak > 0.005f && peak < 0.02f, $"a sugar rush moves the smoothed speed of 1.0 by about one percent: {peak}" );
	}

	[TestMethod]
	public void TheSugarBoostFollowsTheOriginalArithmetic()
	{
		// 0xeb0cc: (amount byte x 6) / 100, added to the u16 boost (wraps); amounts above 255 are masked to the byte (0xe2384).
		Assert.AreEqual( (ushort)0, GuestSpeed.RaiseBoost( 0, 4, 0 ) );
		Assert.AreEqual( (ushort)0, GuestSpeed.RaiseBoost( 0, 4, 16 ), "96 / 100" );
		Assert.AreEqual( (ushort)1, GuestSpeed.RaiseBoost( 0, 4, 17 ), "102 / 100" );
		Assert.AreEqual( (ushort)6, GuestSpeed.RaiseBoost( 0, 4, 100 ) );
		Assert.AreEqual( (ushort)15, GuestSpeed.RaiseBoost( 0, 4, 255 ), "the most one use adds" );
		Assert.AreEqual( (ushort)3, GuestSpeed.RaiseBoost( 0, 4, 256 + 50 ) );
		Assert.AreEqual( (ushort)14, GuestSpeed.RaiseBoost( 0xFFFF, 4, 255 ), "65,535 + 15 wraps" );
		foreach ( var ingredient in new[] { 0, 1, 2, 3, 5, -1 } )
			Assert.AreEqual( (ushort)77, GuestSpeed.RaiseBoost( 77, ingredient, 255 ) );
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
			// the original tests the counter for exactly 0 only when the guest decides (state 6), so a guest whose counter passes 0 while it walks never leaves by it:
			// this holds the counter just above 0 while the guest is deciding, so that the needs phase brings it to 0 on the decision
			if ( guest.State == GuestState.DoingNothing && guest.ExitLevel > 1 )
				guest.ExitLevel = 1;
			simulation.Tick( Tick );
			states.Add( guest.State );
		}
		Assert.IsNull( simulation.Find( guest.Id ) );
		Assert.IsTrue( states.Contains( GuestState.LeavingPark ) && states.Contains( GuestState.CrossingRoadHome ) && states.Contains( GuestState.WaitingToGoHome ) );
	}

	// ---- Lifecycle (ORACLE.md F26) --------------------------------------------------------------

	[TestMethod]
	public void TheLeavingTestIsAHappinessByteOfZeroAnExactZeroCounterOrAClosedPark()
	{
		Assert.IsTrue( GuestLifecycle.ShouldLeave( 0f, 120, false ) );
		Assert.IsTrue( GuestLifecycle.ShouldLeave( 0.99f, 120, false ), "the byte of 0.99 is 0: happiness below 1" );
		Assert.IsFalse( GuestLifecycle.ShouldLeave( 1f, 120, false ) );
		Assert.IsFalse( GuestLifecycle.ShouldLeave( 10f, 120, false ), "no rule at 10 or 5" );
		Assert.IsFalse( GuestLifecycle.ShouldLeave( 5f, 120, false ) );
		Assert.IsTrue( GuestLifecycle.ShouldLeave( 50f, 0, false ) );
		Assert.IsFalse( GuestLifecycle.ShouldLeave( 50f, -1, false ), "the counter compares for equality: a counter below 0 never leaves" );
		Assert.IsFalse( GuestLifecycle.ShouldLeave( 50f, 1, false ) );
		Assert.IsTrue( GuestLifecycle.ShouldLeave( 50f, 120, true ), "a closed park sends everyone home" );
		Assert.IsTrue( GuestLifecycle.ShouldLeave( float.NaN, 120, false ), "NaN truncates to 0x80000000, whose low byte is 0" );
	}

	[TestMethod]
	public void TheStateEighteenRecallWantsAPositiveCounterANonzeroHappinessByteAndAnOpenPark()
	{
		Assert.IsTrue( GuestLifecycle.ShouldRecall( 50f, 100, false ) );
		Assert.IsTrue( GuestLifecycle.ShouldRecall( 1f, 1, false ), "the happiness byte of exactly 1 is nonzero" );
		Assert.IsFalse( GuestLifecycle.ShouldRecall( 0.99f, 100, false ), "the byte of 0.99 is 0" );
		Assert.IsFalse( GuestLifecycle.ShouldRecall( 50f, 0, false ), "the counter must be above 0, not equal" );
		Assert.IsFalse( GuestLifecycle.ShouldRecall( 50f, -1, false ), "a counter run negative stays leaving" );
		Assert.IsFalse( GuestLifecycle.ShouldRecall( 50f, 100, true ), "a closed park keeps everyone on the way home" );
	}

	/// <summary>
	/// The state-18 recall (0xedc98) unwinds a closed-park dismissal: a guest whose counter is still positive and whose
	/// happiness byte is nonzero turns back to deciding the turn after the park reopens. A guest that left on an empty
	/// counter or below happiness 1 is past recalling and stays on its way home.
	/// </summary>
	[TestMethod]
	public void ReopeningTheParkRecallsALeavingGuestWhoseCounterAndHappinessStillHold()
	{
		// The dismissal: a walking guest with a fresh counter and healthy happiness is sent home by the closing word alone. The leaving
		// gate needs a way out (a path to an arrival lane's end), so the rig gets a lane ending at the far end of its path.
		var rig = new QueueTests.Rig( configure: WayOut );
		var guest = rig.Simulation.SpawnInPark( 5, 0 );
		guest.Happiness = 50f;
		guest.ExitLevel = 100;
		var payments = new QueueTests.ClosedPayments();
		rig.Simulation.Payments = payments;
		payments.Closed = true;
		for ( var steps = 0; steps < 2000 && guest.State != GuestState.LeavingPark; steps++ )
			rig.Step();
		Assert.AreEqual( GuestState.LeavingPark, guest.State, "the closed park sent the guest home" );
		Assert.AreEqual( 25f, guest.Happiness, 0.0001f, "the dismissal lost the big happiness change and the byte stays nonzero" );

		// The recall: the same guest, one turn after the park reopens, decides again (state 6).
		payments.Closed = false;
		rig.RunTurns( 1 );
		Assert.IsTrue( guest.State is GuestState.DoingNothing or GuestState.WalkingAround, $"the recall returns the guest to deciding: {guest.State}" );

		// Past recalling: a guest that left when its counter read 0 keeps a counter at or below 0 (it loses one per needs
		// phase in every state) and is never recalled, and a guest below happiness 1 has a zero happiness byte for good.
		var counterRig = new QueueTests.Rig( configure: WayOut );
		var counterGuest = counterRig.Simulation.SpawnInPark( 5, 0 );
		counterGuest.Happiness = 50f;
		counterGuest.ExitLevel = 0;
		// It left on its zero counter (the needs phase that takes it below 0 may come before its next state-6 turn, so the leaving is set directly).
		counterGuest.State = GuestState.LeavingPark;
		counterRig.RunTurns( 3 );
		Assert.IsFalse( counterGuest.State is GuestState.DoingNothing or GuestState.WalkingAround, "the empty counter leaves for good" );
		var sadRig = new QueueTests.Rig( configure: WayOut );
		var sadGuest = sadRig.Simulation.SpawnInPark( 5, 0 );
		sadGuest.Happiness = 0.5f;
		sadGuest.ExitLevel = 100;
		sadRig.RunTurns( 3 );
		Assert.IsFalse( sadGuest.State is GuestState.DoingNothing or GuestState.WalkingAround, "a happiness byte of zero leaves for good" );
	}

	/// <summary>An arrival lane ending at (9, 0), the far end of <see cref="QueueTests.Rig"/>'s path: a way out for the leaving gate.</summary>
	private static void WayOut( GuestSettings settings ) => settings.ArrivalLaneA = new[] { (11, 3), (11, 2), (11, 1), (10, 0), (9, 0) };

	[TestMethod]
	public void AGuestLeavesWhenItsCounterReadsZeroAndLosesTheBigHappinessChangeOnTheWay()
	{
		var settings = CreateSettings();
		settings.ExitLevelSeconds = 3;
		settings.ExitLevelVarSeconds = 1;
		var simulation = new GuestSimulation( CreateCross(), settings, 4 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 10, 0 );
		guest.Happiness = 60;
		var states = new List<GuestState>();
		for ( var tick = 0; tick < 60 * 60 && guest.State is not (GuestState.LeavingPark or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome or GuestState.Gone); tick++ )
			simulation.Tick( Tick );
		Assert.IsTrue( guest.State is GuestState.LeavingPark or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome or GuestState.Gone, $"{guest.State}" );
		Assert.AreEqual( 0, guest.ExitLevel, "it went home the turn its counter read 0" );
		Assert.AreEqual( 60f - settings.BigHappinessChange, guest.Happiness, 0.0001f, "0xeb6f4 with selector 2 on the way out" );
	}

	[TestMethod]
	public void AnUnhappyGuestKeepsWalkingUntilItsHappinessIsBelowOne()
	{
		var settings = CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		var simulation = new GuestSimulation( CreateCross(), settings, 4 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 10, 0 );
		guest.Happiness = 5;
		for ( var tick = 0; tick < 10 * 60; tick++ )
			simulation.Tick( Tick );
		Assert.IsTrue( guest.State is GuestState.WalkingAround or GuestState.DoingNothing, $"happiness 5 did not send the guest home ({guest.State})" );
		guest.Happiness = 0.5f;
		// the decision comes when the guest has finished its walk (the wander lasts up to a minute with the world generator's draws)
		for ( var tick = 0; tick < 90 * 60 && guest.State is GuestState.WalkingAround or GuestState.DoingNothing; tick++ )
			simulation.Tick( Tick );
		Assert.AreEqual( GuestState.LeavingPark, guest.State );
	}

	private sealed class ClosedParkPayments : IGuestPayments
	{
		public bool Closed;
		public int AdmissionFee => 0;
		public bool ParkClosed => Closed;
		public bool TryPayAdmission( out int feePaid ) { feePaid = 0; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
	}

	[TestMethod]
	public void ClosingTheParkSendsEveryWalkingGuestHome()
	{
		var settings = CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		var payments = new ClosedParkPayments();
		var simulation = new GuestSimulation( CreateCross(), settings, 4 ) { ArrivalsEnabled = false, Payments = payments };
		var guests = Enumerable.Range( 0, 5 ).Select( _ => simulation.SpawnInPark( 10, 0 ) ).ToList();
		for ( var tick = 0; tick < 5 * 60; tick++ )
			simulation.Tick( Tick );
		Assert.IsTrue( guests.All( guest => guest.State is GuestState.WalkingAround or GuestState.DoingNothing ), "an open park keeps them" );
		payments.Closed = true;
		// A guest walking to a wander destination (state 7) reads the closed word at its next state-6 turn, after the walk.
		for ( var tick = 0; tick < 60 * 60; tick++ )
			simulation.Tick( Tick );
		Assert.IsTrue( guests.All( guest => guest.State is GuestState.LeavingPark or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome or GuestState.Gone ),
			string.Join( ", ", guests.Select( guest => guest.State ) ) );
	}

	[TestMethod]
	public void ARideDoesNotExtendTheExitCounter()
	{
		var ride = CreateShop( 1, (10, 9), excitement: 70 );
		var settings = CreateSettings();
		settings.Types = new() { new GuestType( 70, 300, 1000 ) };
		settings.ExitLevelSeconds = 10_000;
		settings.ExitLevelVarSeconds = 0;
		var simulation = new GuestSimulation( CreateCross(), settings, 5 ) { ArrivalsEnabled = false };
		simulation.Register( ride.Bridge );
		var guest = simulation.SpawnInPark( 6, 5 );
		var before = guest.ExitLevel;
		long ridden = -1;
		for ( var tick = 0; tick < 40 * 60 && guest.RidesTaken == 0; tick++ )
		{
			Run( simulation, new[] { ride }, 1 );
			ridden = simulation.ParkTurn;
		}
		Assert.AreEqual( 1, guest.RidesTaken );
		// a perfect ride gave +25 happiness; the former rule added it to the counter. The counter only ever counted down: one per needs phase.
		Assert.IsTrue( guest.ExitLevel < before && guest.ExitLevel >= before - ridden / 4 - 1, $"{before} -> {guest.ExitLevel} after {ridden} turns" );
	}

	private sealed class SatisfactionPayments : IGuestPayments
	{
		public readonly List<int> Changes = new();
		public int AdmissionFee => 0;
		public bool TryPayAdmission( out int feePaid ) { feePaid = 0; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
		public void UseSatisfaction( IRideVisitorBridge attraction, int change ) => Changes.Add( change );
	}

	[TestMethod]
	public void AFinishedUseReportsThreeTimesTheGuestsHappinessChange()
	{
		// tp.exe 0x004FD970: 3 x ((u8)happiness after the use - (u8)happiness at joining the queue) goes to the object's satisfaction average (0x004E1E00)
		var ride = CreateShop( 1, (10, 9), excitement: 70 );
		var settings = CreateSettings();
		settings.Types = new() { new GuestType( 70, 300, 1000 ) };
		var payments = new SatisfactionPayments();
		var simulation = new GuestSimulation( CreateCross(), settings, 5 ) { ArrivalsEnabled = false, Payments = payments };
		simulation.Register( ride.Bridge );
		var guest = simulation.SpawnInPark( 6, 5 );
		for ( var tick = 0; tick < 40 * 60 && guest.RidesTaken == 0; tick++ )
			Run( simulation, new[] { ride }, 1 );
		Assert.AreEqual( 1, guest.RidesTaken );
		Assert.AreEqual( 1, payments.Changes.Count );
		Assert.AreEqual( 3 * (((int)guest.Happiness & 0xFF) - ((int)guest.QueueJoinHappiness & 0xFF)), payments.Changes[0] );
	}

	[TestMethod]
	public void ArrivalGroupsFollowTheOriginalRule()
	{
		// 0x100CB4E8: (int)(1.2f × (points + NewParkBonus)) / PointsPerVisitor, at least MinPeople, none while bankrupt.
		var settings = CreateSettings();
		var points = 0;
		var bankrupt = false;
		var simulation = new GuestSimulation( CreateCross(), settings, 1 ) { ParkPoints = () => points, ArrivalsHalted = () => bankrupt };
		Assert.AreEqual( 4, simulation.ArrivalGroupSize(), "a new park without the Traffic Lights: 1.2 × 20 / 6" );
		points = 35;
		Assert.AreEqual( 11, simulation.ArrivalGroupSize(), "Belly Bounce's 25 + 10 new bonus: 1.2 × 55 / 6" );
		settings.ArrivalPointsPerVisitor = 1;
		Assert.AreEqual( 66, simulation.ArrivalGroupSize(), "PointsPerVisitor below 2 divides by 1" );
		settings.ArrivalPointsPerVisitor = 6;
		points = -20;
		settings.ArrivalMinPeople = 3;
		Assert.AreEqual( 3, simulation.ArrivalGroupSize(), "raised to MinPeople" );
		bankrupt = true;
		Assert.AreEqual( 0, simulation.ArrivalGroupSize(), "no group in world mode 4" );
	}

	[TestMethod]
	public void ArrivalGroupsUseTheWeatherDropsFactor()
	{
		// 0x100CB564..0x100CB594: the +48 word (mCurrentDrops) of the weather thing at game +0x1DA724 is positive while drops fall, so the factor is 0.8f; otherwise 1.2f.
		var settings = CreateSettings();
		var points = 0;
		var drops = true;
		var simulation = new GuestSimulation( CreateCross(), settings, 1 ) { ParkPoints = () => points, WeatherDropsFalling = () => drops };
		Assert.AreEqual( 2, simulation.ArrivalGroupSize(), "a new park in rain: (int)(0.8f × 20) / 6" );
		points = 35;
		Assert.AreEqual( 7, simulation.ArrivalGroupSize(), "Belly Bounce's 25 + 10 new bonus: (int)(0.8f × 55) / 6" );
		settings.ArrivalMinPeople = 5;
		points = 0;
		Assert.AreEqual( 5, simulation.ArrivalGroupSize(), "raised to MinPeople" );
		settings.ArrivalMinPeople = 1;
		drops = false;
		Assert.AreEqual( 4, simulation.ArrivalGroupSize(), "no drops falling: the factor is 1.2f" );
		simulation.WeatherDropsFalling = null;
		Assert.AreEqual( 4, simulation.ArrivalGroupSize(), "no weather hook: 1.2f" );
		var bankrupt = new GuestSimulation( CreateCross(), settings, 1 ) { WeatherDropsFalling = () => true, ArrivalsHalted = () => true };
		Assert.AreEqual( 0, bankrupt.ArrivalGroupSize(), "world mode 4 still gives none" );
	}

	[TestMethod]
	public void ArrivalFactorFollowsTheParkWeatherDrops()
	{
		// Level wires WeatherDropsFalling as ParkWeather.CurrentDrops > 0: dry weather gives 1.2f, a ramped-up rain 0.8f.
		var settings = CreateSettings();
		var keys = new SamSettings( new[] { SamDocument.Parse( "WeatherEffects.QualityForRainLow\t0\nWeatherEffects.QualityForRainHigh\t40\nWeatherEffects.MaxRaindrops\t600\nWeather.SpeedOfChange\t20\n", "test" ) } );
		var weather = new ParkWeather( keys, 1, 10, 10 );
		var simulation = new GuestSimulation( CreateCross(), settings, 1 ) { WeatherDropsFalling = () => weather is { CurrentDrops: > 0 } };
		Assert.AreEqual( 4, simulation.ArrivalGroupSize(), "before any weather: no drops" );
		weather.ApplyQuality( 1 );
		Assert.AreEqual( 4, simulation.ArrivalGroupSize(), "a rain quality with the drops still at 0: 1.2f" );
		weather.Step( 0 );
		Assert.AreEqual( 20, weather.CurrentDrops );
		Assert.AreEqual( 2, simulation.ArrivalGroupSize(), "drops falling: 0.8f" );
		weather.ApplyQuality( 78 );
		for ( var turn = 0; turn < 40; turn++ )
			weather.Step( 0 );
		Assert.AreEqual( 0, weather.CurrentDrops, "dry weather ramps the drops back to 0" );
		Assert.AreEqual( 4, simulation.ArrivalGroupSize(), "dry again: 1.2f" );
	}

	[TestMethod]
	public void OnlineVisitsCutArrivalGroupsTo500()
	{
		// 0x100CB4E8: with the GameType word 1 the group is cut to 500 − guests instead of 1500 − guests.
		var settings = CreateSettings();
		settings.ArrivalMinPeople = 40;
		var simulation = new GuestSimulation( CreateCross(), settings, 1 ) { OnlineVisit = true };
		var field = typeof( GuestSimulation ).GetField( "guests", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )!;
		var list = (List<Guest>)field.GetValue( simulation )!;
		list.AddRange( Enumerable.Repeat<Guest>( null!, 480 ) );
		Assert.AreEqual( 20, simulation.ArrivalGroupSize(), "500 − 480" );
		simulation.OnlineVisit = false;
		Assert.AreEqual( 40, simulation.ArrivalGroupSize(), "an offline park is cut at 1500" );
		simulation.OnlineVisit = true;
		list.AddRange( Enumerable.Repeat<Guest>( null!, 120 ) );
		Assert.AreEqual( -100, simulation.ArrivalGroupSize(), "600 guests: 500 − 600, not floored (tp.exe 0x004CF6A6)" );
		simulation.OnlineVisit = false;
		settings.ArrivalMinPeople = -5;
		simulation.ParkPoints = () => -100;
		list.RemoveRange( 0, 600 );
		// (int)(1.2 × (−100 + 20)) / 6 = −16, raised to MinPeople −5
		Assert.AreEqual( 1500, simulation.ArrivalGroupSize(), "a negative group wraps the unsigned compare and is cut to 1500 − 0" );
	}

	[TestMethod]
	public void ArrivalTimerCountsFourTurnUnitsAndRestartsAfterTheGroup()
	{
		// 0x100CB2AC: a group starts once (turn >> 2) − (start >> 2) exceeds TimeBetweenArrivals; the timer restarts when it is empty.
		var settings = CreateSettings();
		settings.ArrivalTimeBetween = 10;
		var turn = 0L;
		var simulation = new GuestSimulation( CreateCross(), settings, 1 ) { ArrivalClock = () => turn };
		void Step( long toTurn )
		{
			while ( turn < toTurn )
			{
				turn++;
				for ( var tick = 0; tick < 15; tick++ )
					simulation.Tick( Tick );
			}
		}
		Step( 43 );
		Assert.AreEqual( 0, simulation.Guests.Count, "43 turns are 10 timer units: not more than TimeBetweenArrivals" );
		Step( 44 );
		Step( 60 );
		Assert.AreEqual( 4, simulation.Guests.Count, "11 units start a group of four, dropped off one by one" );
		// The fourth guest steps off about 1.2 s (five turns) after the first; the timer restarts at the next step-off (turn 51 or 52).
		Step( 88 );
		Assert.AreEqual( 4, simulation.Guests.Count, "the timer restarted when the group was empty, not when it started" );
		Step( 110 );
		Assert.AreEqual( 8, simulation.Guests.Count, "the next group arrives 11 units after the restart" );
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

	// ---- Attraction choice (ORACLE.md F24) -----------------------------------------------------

	[TestMethod]
	public void AGuestScoresAnAttractionWithTheOriginalNormalisedFormula()
	{
		var ride = CreateShop( 1, (10, 9), excitement: 60 );
		var settings = CreateSettings();
		settings.Types = new() { new GuestType( 60, 300, 1000 ) };
		var simulation = new GuestSimulation( CreateCross(), settings, 5 ) { ArrivalsEnabled = false };
		simulation.Register( ride.Bridge );
		var guest = simulation.SpawnInPark( ride.Bridge.JoinCell!.Value.X, ride.Bridge.JoinCell!.Value.Y );
		// next to the queue back cell, an empty queue, matching excitement, a guest without needs: (100 + 100 + 100) / 11 weights
		Assert.AreEqual( 27u, simulation.ScoreAttraction( guest, ride.Bridge ) );
		ride.Bridge.AgeDays = 3;
		Assert.AreEqual( 135u, simulation.ScoreAttraction( guest, ride.Bridge ), "a ride up to 7 days old is new: times DecisionVariable2" );
		ride.Bridge.AgeDays = 100;
		guest.UsedHistory[0] = ride.Bridge.AttractionId;
		Assert.AreEqual( 0u, simulation.ScoreAttraction( guest, ride.Bridge ), "the ride just used (an attraction of the type used last) scores 0 outright" );
		guest.UsedHistory[0] = 99; // a ride that is gone: no type to compare
		guest.UsedHistory[1] = ride.Bridge.AttractionId;
		Assert.AreEqual( 6u, simulation.ScoreAttraction( guest, ride.Bridge ), "used one ride before: divided by 4" );
		guest.UsedHistory[1] = 0;
		guest.TemporaryHistory[1] = ride.Bridge.AttractionId;
		Assert.AreEqual( 6u, simulation.ScoreAttraction( guest, ride.Bridge ), "turned away second most recently: divided by 4" );
	}

	[TestMethod]
	public void TheScoreMeasuresTheGuestAgainstTheBackOfTheQueueNotTheJoinCell()
	{
		// F24: both the distance match and the queue term's d² ≤ 8 range read the back-of-queue cell (0xdd3f4), never the path cell
		// guests step from. The guest stands diagonally beside the back (d² 4 + 4 = 8, in range) while the join cell beyond it is
		// d² 9 + 4 = 13 away: with the back cell the score is (distance 99 + queue 50 + excitement 70) / 11 weights = 19, and the
		// join cell would drop the queue term and its weight entirely: (98 + 70) / 10 = 16.
		var grid = new GuestPathGrid( 12, 12 );
		grid.SetPath( 0, 1, true );
		grid.SetPath( 3, 3, true );
		var ride = new RideVisitorBridge( 1, "Queue ride", RideVisitorKind.Ride, 5, 55, 30, GuestNeeds.None )
		{
			EntranceCell = (2, 6),
			ExitCell = (2, 6),
			QueueFrontCell = (2, 5),
			QueueEntranceDirection = 2,
			Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 )
		};
		ride.Attach( CreateShopScript( new VisitorRideScriptEffects( ride, UnimplementedRideScriptEffects.Instance ) ), () => true );
		Assert.AreEqual( 3, QueuePaths.TryExtend( grid, ride, new[] { (2, 5), (2, 4), (2, 3) } ) );
		QueuePaths.TryConnect( grid, ride );
		Assert.AreEqual( (2, 3), ride.QueueBackCell );
		Assert.AreEqual( (3, 3), ride.JoinCell, "the only walkable neighbour of the back cell, away from the guest" );
		for ( var queued = 1; queued <= 6; queued++ )
			Assert.IsTrue( ride.TryJoinQueue( queued ), "six of the twelve physical places fill" );
		var simulation = new GuestSimulation( grid, CreateSettings(), 7 ) { ArrivalsEnabled = false };
		simulation.Register( ride );
		var guest = simulation.SpawnInPark( 0, 1 );
		Assert.AreEqual( 19u, simulation.ScoreAttraction( guest, ride ), "d² 8 to the back keeps the queue term: (99 + 50 + 70) / 11" );
		var connected = simulation.ViewOf( ride, guest );
		grid.SetPath( 3, 3, false );
		ride.RecomputeQueue( grid );
		Assert.IsNull( ride.JoinCell, "the sole connecting path was removed" );
		Assert.AreEqual( (byte)ParkCellType.Queue, grid.Cells.RawTypeAt( 2, 3 ) );
		CollectionAssert.AreEqual( new[] { (2, 5), (2, 4), (2, 3) }, ride.QueueCells.ToArray() );
		Assert.AreEqual( connected with { HasJoinCell = false }, simulation.ViewOf( ride, guest ), "all scoring facts survive; the public join flag stays false" );
		Assert.AreEqual( 19u, simulation.ScoreAttraction( guest, ride ), "the disconnected back still supplies d² 8, six queued guests and three cells" );
	}

	[TestMethod]
	public void ARideTheGuestJustUsedIsRememberedAndTurnedAwayRidesFadeAfterTwentyTurns()
	{
		var ride = CreateShop( 1, (10, 9), excitement: 70 );
		var settings = CreateSettings();
		settings.Types = new() { new GuestType( 70, 300, 1000 ) };
		settings.ExitLevelSeconds = 10_000;
		var simulation = new GuestSimulation( CreateCross(), settings, 5 ) { ArrivalsEnabled = false };
		simulation.Register( ride.Bridge );
		var guest = simulation.SpawnInPark( 6, 5 );
		for ( var tick = 0; tick < 40 * 60 && guest.RidesTaken == 0; tick++ )
			Run( simulation, new[] { ride }, 1 );
		Assert.AreEqual( 1, guest.RidesTaken );
		Assert.AreEqual( ride.Bridge.AttractionId, guest.UsedHistory[0], "the use pushed the attraction into the used history" );
		// a refusal is remembered for four multiples of 20 park turns: a 0 is shifted into the temporary history every 20th turn
		guest.TemporaryHistory[0] = 99;
		var start = simulation.ParkTurn;
		while ( simulation.ParkTurn % 20 != 0 || simulation.ParkTurn == start )
			Run( simulation, Array.Empty<ScriptedAttraction>(), 1 );
		CollectionAssert.AreEqual( new[] { 0, 99, 0, 0 }, guest.TemporaryHistory );
	}

	[TestMethod]
	public void AGuestTurnedAwayAsTooBoredIsNotAttractedAgainRightAway()
	{
		// a ride of excitement 100 for a guest preferring 20: the join gate refuses it (|80| >= 45), and the refusal enters the temporary history,
		// which divides that ride's score by 5 (about 20 s) instead of leaving the guest to choose it again at once.
		var ride = CreateShop( 1, (10, 9), excitement: 100 );
		var settings = CreateSettings();
		settings.Types = new() { new GuestType( 20, 300, 1000 ) };
		settings.ExitLevelSeconds = 10_000;
		var simulation = new GuestSimulation( CreateCross(), settings, 5 ) { ArrivalsEnabled = false };
		simulation.Register( ride.Bridge );
		var guest = simulation.SpawnInPark( 6, 5 );
		// Sent on its way at once: from (6, 5) the ride scores below the floor, so a wandering guest would choose it only near it.
		guest.AttractionId = ride.Bridge.AttractionId;
		guest.State = GuestState.GoingToRide;
		var refused = false;
		for ( var tick = 0; tick < 60 * 60 && !refused; tick++ )
		{
			Run( simulation, new[] { ride }, 1 );
			refused = guest.TemporaryHistory[0] == ride.Bridge.AttractionId;
		}
		Assert.IsTrue( refused, "the refusal pushed the ride's id into the temporary history" );
		Assert.AreEqual( 0L, guest.QueueStandingSinceTurn, "an excitement refusal clears the decision stamp (tp.exe 0x004FFCF6)" );
		Assert.AreEqual( 0, guest.RidesTaken );
		Assert.IsTrue( simulation.ScoreAttraction( guest, ride.Bridge ) < AttractionScore.CandidateFloor, "so its score fell below the choice floor" );
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
		var random = new WorldRng( 1 );
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
