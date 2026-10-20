using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// The original's one world generator (tp.exe 0x00516330, DET-013 to DET-015): the generator itself, the one stream shared by guests, staff, economy and scripts,
/// the reseed and draw order of a guest's creation, its save and the load of saves from before it.
/// </summary>
[TestClass]
public class WorldRngTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	/// <summary>
	/// Vectors from the original's own code: the 0x00516330 bytes of tp.exe executed in an x86 emulator on a world object whose +0x1DA708 word
	/// holds the state (state, new state, result).
	/// </summary>
	[DataTestMethod]
	[DataRow( 0x00000000u, 0x9AF9E377u, 0x65061C89u )]
	[DataRow( 0x00000001u, 0xCB61E442u, 0x349E1BBEu )]
	[DataRow( 0x00000002u, 0xFBC9E50Du, 0x04361AF3u )]
	[DataRow( 0x12345678u, 0x3BBBAA19u, 0x3BBBAA19u )]
	[DataRow( 0xFFFFFFFFu, 0x6A91E2ACu, 0x6A91E2ACu )]
	[DataRow( 0x3F416BBAu, 0x3E8846BCu, 0x3E8846BCu )]
	public void TheStepRotatesRightByThirteenAndReturnsTheAbsoluteValue( uint state, uint next, uint returned )
	{
		var rng = new WorldRng( state );
		Assert.AreEqual( returned, rng.NextWord() );
		Assert.AreEqual( next, rng.State, "the rotated value is the new state" );
	}

	/// <summary>A state of 0x2B625FE5 steps to 0x80000000, whose absolute value (neg) keeps its bits: the word stays 0x80000000 (emulated 0x00516330).</summary>
	[TestMethod]
	public void TheMostNegativeStateKeepsItsBits()
	{
		var rng = new WorldRng( 0x2B625FE5 );
		Assert.AreEqual( 0x80000000u, rng.NextWord() );
		Assert.AreEqual( 0x80000000u, rng.State );
	}

	[TestMethod]
	public void AChainOfDrawsFollowsTheEmulatedOriginal()
	{
		var rng = new WorldRng( 1 );
		var expected = new[] { (0xCB61E442u, 0x349E1BBEu), (0xB5CEDE5Eu, 0x4A3121A2u), (0x912CDDBDu, 0x6ED32243u), (0x1FC11C24u, 0x1FC11C24u), (0xC99C4FBDu, 0x3663B043u) };
		foreach ( var (state, word) in expected )
		{
			Assert.AreEqual( word, rng.NextWord() );
			Assert.AreEqual( state, rng.State );
		}
	}

	/// <summary>The reseed (0x00516370) stores its argument, a zero-extended halfword thing id, as the state, so the draws after it depend on the id alone.</summary>
	[TestMethod]
	public void AReseedMakesTheNextDrawsAFunctionOfTheIdAlone()
	{
		var first = new WorldRng( 0xDEADBEEF );
		var second = new WorldRng( 0x01234567 );
		first.Reseed( 77 );
		second.Reseed( 77 );
		Assert.AreEqual( 77u, first.State );
		Assert.AreEqual( second.NextWord(), first.NextWord() );
		Assert.AreEqual( second.NextWord(), first.NextWord() );
		Assert.AreNotEqual( new WorldRng( 78 ).NextWord(), new WorldRng( 77 ).NextWord() );
	}

	[TestMethod]
	public void BoundedDrawsUseTheUnsignedRemainderOfTheWord()
	{
		var reference = new WorldRng( 5 );
		var rng = new WorldRng( 5 );
		Assert.AreEqual( reference.NextWord() % 9u, rng.Next( 9u ) );
		Assert.AreEqual( (int)(reference.NextWord() % 28u), rng.Next( 28 ) );
		Assert.AreEqual( 0, rng.Next( 0 ), "a bound of 0 is the original's division by zero: nothing here" );
		Assert.AreEqual( reference.State, rng.State, "a bound of 0 draws nothing" );
	}

	[TestMethod]
	public void TheStartStateOfASeedIsStableAndSpreadsSmallSeeds()
	{
		Assert.AreEqual( WorldRng.StateFromSeed( 1 ), WorldRng.StateFromSeed( 1 ) );
		Assert.AreNotEqual( WorldRng.StateFromSeed( 1 ), WorldRng.StateFromSeed( 2 ) );
		Assert.AreEqual( WorldSeed.Default.GeneratorState, WorldRng.StateFromSeed( WorldSeed.Default.EconomyStream ) );
		Assert.AreEqual( new WorldSeed( 5 ).GeneratorState, WorldRng.StateFromSeed( new WorldSeed( 5 ).EconomyStream ) );
		Assert.AreNotEqual( WorldSeed.Default.GeneratorState, new WorldSeed( 5 ).GeneratorState );
	}

	// ---- Scripts: RAND, FINDSCRIPTRAND, SPAWNCHILD -----------------------------------------------

	private static RideVM Load( Asm asm, RideVMOptions options, string[]? variables = null, params string[] strings ) =>
		new( new RideScriptFile( new MemoryStream( asm.Build( variables ?? new[] { "V0", "V1", "V2", "V3" }, strings ) ) ), options );

	/// <summary>RAND is labs(labs(R >>> 1) % (int16 bound + 1)) of one world draw (tp.exe 0x0055398A); the draws are the emulated 0x00516330 chain from state 0.</summary>
	[TestMethod]
	public void RandTakesTheOriginalsProjectionOfTheWorldGenerator()
	{
		var world = new RideScriptWorld();
		world.World.Restore( 0 );
		var script = Load( new Asm().I( Opcode.RAND, V( 0 ), 10 ).I( Opcode.RAND, V( 1 ), 100 ).I( Opcode.RAND, V( 2 ), 7 ).I( Opcode.ENDSLICE ).Label( "end" ).I( Opcode.BRANCH, "end" ),
			new RideVMOptions { World = world } );
		script.Advance( Tick );
		// the words 0x65061C89, 0x535712FF, 0x2290229C (state 0, then each new state) shifted right by one, modulo 11, 101 and 8
		Assert.AreEqual( (10, 53, 6), (script.Variables[0], script.Variables[1], script.Variables[2]) );
		Assert.AreEqual( 3, (int)script.CaptureSnapshot().RandomDraws );
		Assert.IsTrue( script.CaptureSnapshot().UsesWorldRandom && script.CaptureSnapshot().RandomState == null, "a script without a seed has no generator of its own" );
		Assert.AreEqual( 0u, script.Seed == 0 ? 0u : 1u );
	}

	/// <summary>A bound of -1 is the original's division by zero (movsx cx; inc ecx; idiv at 0x0055399A): the script faults here, as it does for any negative bound.</summary>
	[TestMethod]
	public void ARandBoundOfMinusOneFaultsTheScript()
	{
		var world = new RideScriptWorld();
		var before = world.World.State;
		var script = Load( new Asm().I( Opcode.RAND, V( 0 ), -1 ).I( Opcode.ENDSLICE ).Label( "end" ).I( Opcode.BRANCH, "end" ), new RideVMOptions { World = world } );
		script.Advance( Tick );
		Assert.AreEqual( RideVMState.Faulted, script.State );
		Assert.AreEqual( before, world.World.State, "no draw is taken for a bound the original cannot divide by" );
	}

	[TestMethod]
	public void TwoScriptsDrawFromTheSameStreamInTheOrderTheyRun()
	{
		var world = new RideScriptWorld();
		var mirror = new WorldRng( world.World.State );
		var one = Load( new Asm().I( Opcode.RAND, V( 0 ), 1000 ).I( Opcode.ENDSLICE ).Label( "end" ).I( Opcode.BRANCH, "end" ), new RideVMOptions { World = world } );
		var two = Load( new Asm().I( Opcode.RAND, V( 0 ), 1000 ).I( Opcode.ENDSLICE ).Label( "end" ).I( Opcode.BRANCH, "end" ), new RideVMOptions { World = world } );
		two.Advance( Tick );
		one.Advance( Tick );
		Assert.AreEqual( (int)((mirror.NextWord() >> 1) % 1001), two.Variables[0], "the script that ran first took the first draw" );
		Assert.AreEqual( (int)((mirror.NextWord() >> 1) % 1001), one.Variables[0] );
		Assert.AreEqual( mirror.State, world.World.State );
	}

	/// <summary>A script made with an explicit seed keeps its own seeded generator and takes nothing from the world's (tests, standalone scripts, older saves).</summary>
	[TestMethod]
	public void AScriptWithAnExplicitSeedKeepsItsOwnGenerator()
	{
		var world = new RideScriptWorld();
		var before = world.World.State;
		var seeded = Load( new Asm().I( Opcode.RAND, V( 0 ), 1000 ).I( Opcode.ENDSLICE ).Label( "end" ).I( Opcode.BRANCH, "end" ), new RideVMOptions { World = world, Seed = 9 } );
		seeded.Advance( Tick );
		Assert.AreEqual( before, world.World.State );
		Assert.AreEqual( new Random( 9 ).Next( 0, 1001 ), seeded.Variables[0] );
		Assert.IsFalse( seeded.CaptureSnapshot().UsesWorldRandom );
	}

	/// <summary>FINDSCRIPTRAND draws only when a script matches and picks (labs(R >>> 1) % matches) + 1 (tp.exe 0x005560CB).</summary>
	[TestMethod]
	public void FindScriptRandDrawsOnlyOnAMatchAndTakesTheModuloOfTheMatchCount()
	{
		var world = new RideScriptWorld();
		var named = new List<RideVM>();
		for ( var index = 0; index < 3; index++ )
			named.Add( Load( new Asm().I( Opcode.NAME, S( 0 ) ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), new RideVMOptions { World = world }, null, "Lights" ) );
		foreach ( var script in named )
			script.Advance( Tick );
		var finder = Load( new Asm()
			.I( Opcode.FINDSCRIPTRAND, S( 7 ), V( 0 ) )
			.I( Opcode.FINDSCRIPTRAND, S( 0 ), V( 1 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), new RideVMOptions { World = world }, null, "Lights", "Nothing" );
		var mirror = new WorldRng( world.World.State );
		finder.Advance( Tick );
		Assert.AreEqual( 0, finder.Variables[0], "no script is named Nothing" );
		// the count and the pick both walk the script list from its head (0x5560E2 and 0x556150: the highest id first in a park's list)
		var ids = named.Select( script => script.ScriptId ).OrderByDescending( id => id ).ToArray();
		Assert.AreEqual( ids[(int)((mirror.NextWord() >> 1) % 3)], finder.Variables[1], "the drawn index into the three matches, counted from the head of the list" );
		Assert.AreEqual( mirror.State, world.World.State, "the miss drew nothing, the hit one word" );
	}

	/// <summary>A spawned child of a script without a seed has none either: the original's scripts are not seeded, so spawning draws nothing and the child's RAND takes a world draw.</summary>
	[TestMethod]
	public void ASpawnedChildOfAWorldScriptDrawsFromTheWorldToo()
	{
		var world = new RideScriptWorld();
		var mirror = new WorldRng( world.World.State );
		var parent = Load( new Asm().I( Opcode.SPAWNCHILD, S( 0 ) ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ),
			new RideVMOptions
			{
				World = world,
				ResolveScript = _ => new RideScriptFile( new MemoryStream( new Asm().I( Opcode.RAND, V( 0 ), 9 ).Label( "end" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "end" ).Build( new[] { "V0" } ) ) )
			}, null, "child.rse" );
		parent.Advance( Tick );
		Assert.IsNotNull( parent.Child );
		Assert.IsTrue( parent.Child!.CaptureSnapshot().UsesWorldRandom );
		Assert.AreEqual( 0, parent.Child.Seed );
		Assert.AreEqual( 0, (int)parent.CaptureSnapshot().RandomDraws, "the parent drew nothing to seed the child" );
		parent.Child.Advance( Tick );
		Assert.AreEqual( (int)((mirror.NextWord() >> 1) % 10), parent.Child.Variables[0], "the child's one RAND is the first world draw" );
		Assert.AreEqual( mirror.State, world.World.State );
	}

	[TestMethod]
	public void ASnapshotRestoresAScriptIntoTheWayItDrewWhateverItsCandidateWas()
	{
		byte[] Idle() => new Asm().I( Opcode.RAND, V( 0 ), 50 ).Label( "end" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "end" ).Build( new[] { "V0" } );
		RideVM Make( RideScriptWorld world, int? seed ) => new( new RideScriptFile( new MemoryStream( Idle() ) ), new RideVMOptions { World = world, Seed = seed, SourceName = "d.rse" } );
		foreach ( var (savedSeed, candidateSeed) in new (int?, int?)[] { (null, 4), (6, null), (null, null), (6, 6) } )
		{
			var world = new RideScriptWorld();
			var original = Make( world, savedSeed );
			original.Advance( Tick );
			var snapshot = world.CaptureSnapshot();
			var restoredWorld = new RideScriptWorld();
			var candidate = Make( restoredWorld, candidateSeed );
			restoredWorld.RestoreSnapshot( snapshot, new Dictionary<int, RideVM> { [original.ScriptId] = candidate } );
			Assert.AreEqual( savedSeed == null, candidate.CaptureSnapshot().UsesWorldRandom, $"saved {savedSeed}, candidate {candidateSeed}" );
			Assert.AreEqual( savedSeed ?? 0, candidate.Seed );
			Assert.AreEqual( world.RandomState, restoredWorld.RandomState, "the world generator comes back with the world" );
			Assert.AreEqual( snapshot.Scripts[0].RandomState?.Inext, candidate.CaptureSnapshot().RandomState?.Inext );
		}
	}

	// ---- One stream ---------------------------------------------------------------------------------

	[TestMethod]
	public void GuestsEconomyAndScriptsShareOneGeneratorOnceAttached()
	{
		var economy = EconomyTestData.Park();
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var scripts = new RideScriptWorld();
		Assert.AreNotSame( economy.World, guests.World, "built apart, each has a generator of its own" );
		guests.AttachWorld( economy.World );
		scripts.AttachWorld( economy.World );
		Assert.AreSame( economy.World, guests.World );
		Assert.AreSame( economy.World, scripts.World );
		var mirror = new WorldRng( economy.World.State );
		guests.SpawnInPark( 10, 0 );
		scripts.World.NextWord();
		var after = economy.World.State;
		Assert.AreNotEqual( mirror.State, after );
		Assert.AreEqual( (ulong)after, guests.RandomState );
		Assert.AreEqual( (ulong)after, scripts.RandomState );
		Assert.AreEqual( after, new ParkWorldStreams( WorldSeed.Default, null, guests, scripts, null ).Generator.State );
	}

	/// <summary>The state is part of the canonical hash once, from the economy's generator.</summary>
	[TestMethod]
	public void TheHashReadsTheOneGeneratorAndMovesWithIt()
	{
		var economy = EconomyTestData.Park();
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var scripts = new RideScriptWorld();
		guests.AttachWorld( economy.World );
		scripts.AttachWorld( economy.World );
		ulong Hash() => WorldStateHash.Compute( new WorldStateSources { Economy = economy, Guests = guests, Scripts = scripts } );
		var before = Hash();
		Assert.AreEqual( before, Hash(), "hashing draws nothing" );
		economy.World.NextWord();
		Assert.AreNotEqual( before, Hash() );
	}

	// ---- Saves --------------------------------------------------------------------------------------

	[TestMethod]
	public void TheEconomySaveCarriesTheGeneratorAndContinuesIt()
	{
		var economy = EconomyTestData.Park();
		economy.World.NextWord();
		economy.World.NextWord();
		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"WorldGenerator\"" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( economy.World.State, restored.World.State );
		Assert.AreEqual( economy.World.NextWord(), restored.World.NextWord(), "the draws continue" );
	}

	/// <summary>
	/// A save written before the port has no WorldGenerator: with a world section the generator starts from the saved world seed, without one from the old economy stream's state;
	/// the load never reads the old SplitMix64 words as a state.
	/// </summary>
	[TestMethod]
	public void ASaveFromBeforeThePortStartsTheGeneratorFromItsSeed()
	{
		var economy = EconomyTestData.Park();
		var seed = new WorldSeed( 2026 );
		var withWorld = ParkSaveFile.Serialize( economy, new WorldRandomState( seed.Value, 123u, 42u ) );
		var older = Regex.Replace( withWorld, @"""WorldGenerator"":\s*\d+,?\s*", "" );
		Assert.AreNotEqual( withWorld, older, "the field was removed" );
		var node = JsonNode.Parse( older )!.AsObject();
		node["RandomState"] = 0x9E3779B97F4A7C15UL;                                   // an old economy SplitMix64 state
		node["World"]!.AsObject()["GuestRandom"] = 0xFFFFFFFFFFFFFFFFUL;               // the guests' old stream, read and ignored
		node["World"]!.AsObject()["ScriptRandom"] = 5UL;
		var data = ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) );
		Assert.IsNull( data.WorldGenerator );
		Assert.AreEqual( seed.GeneratorState, ParkSaveFile.Restore( data, economy.Settings, economy.Catalog ).World.State, "derived from the world seed" );
		Assert.AreEqual( seed.GeneratorState, ParkSaveFile.ToWorldState( data )!.Generator );

		var withoutWorld = JsonNode.Parse( ParkSaveFile.Serialize( economy ) )!.AsObject();
		withoutWorld.Remove( "WorldGenerator" );
		withoutWorld["RandomState"] = 12345UL;
		var legacy = ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( withoutWorld.ToJsonString() ) );
		Assert.AreEqual( WorldRng.StateFromSeed( 12345 ), ParkSaveFile.Restore( legacy, economy.Settings, economy.Catalog ).World.State, "derived from the old economy stream's state, the only seed such a save has" );
		Assert.IsNull( ParkSaveFile.ToWorldState( legacy ) );
	}

	[TestMethod]
	public void TheGuestAndScriptSnapshotsCarryAndRestoreTheGenerator()
	{
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		guests.SpawnInPark( 10, 0 );
		var snapshot = guests.CaptureSnapshot();
		Assert.AreEqual( guests.World.State, snapshot.WorldGenerator );
		var other = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 99 ) { ArrivalsEnabled = false };
		other.RestoreSnapshot( snapshot );
		Assert.AreEqual( guests.World.State, other.World.State );

		// an older snapshot has no generator and the restore leaves the one the simulation was built with
		var builtWith = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 99 ) { ArrivalsEnabled = false };
		var state = builtWith.World.State;
		builtWith.RestoreSnapshot( snapshot with { WorldGenerator = null, RandomState = 0xDEADBEEFCAFEF00DUL } );
		Assert.AreEqual( state, builtWith.World.State );

		var scripts = new RideScriptWorld();
		scripts.World.NextWord();
		var worldSnapshot = scripts.CaptureSnapshot();
		Assert.AreEqual( scripts.World.State, worldSnapshot.WorldGenerator );
		var restored = new RideScriptWorld();
		restored.RestoreSnapshot( worldSnapshot, new Dictionary<int, RideVM>() );
		Assert.AreEqual( scripts.World.State, restored.World.State );
		var older = new RideScriptWorld();
		var builtState = older.World.State;
		older.RestoreSnapshot( worldSnapshot with { WorldGenerator = null, RandomState = 0xDEADBEEFCAFEF00DUL }, new Dictionary<int, RideVM>() );
		Assert.AreEqual( builtState, older.World.State );
	}

	/// <summary>A save-load of the guests of a linked park continues one stream: the park's draws after the load are those of the run that was not saved.</summary>
	[TestMethod]
	public void ASaveAndLoadInTheMiddleOfARunContinuesTheOneStream()
	{
		ulong Run( bool saveAndLoad )
		{
			var economy = EconomyTestData.Park();
			var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 );
			guests.AttachWorld( economy.World );
			for ( var tick = 0; tick < 1200; tick++ )
			{
				guests.Tick( Tick );
				economy.Advance( 4 );
			}
			if ( saveAndLoad )
			{
				var guestSnapshot = guests.CaptureSnapshot();
				var restoredEconomy = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( economy ) ) ), economy.Settings, economy.Catalog );
				var restoredGuests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 );
				restoredGuests.AttachWorld( restoredEconomy.World );
				restoredGuests.RestoreSnapshot( guestSnapshot );
				(economy, guests) = (restoredEconomy, restoredGuests);
			}
			for ( var tick = 0; tick < 1200; tick++ )
			{
				guests.Tick( Tick );
				economy.Advance( 4 );
			}
			return WorldStateHash.Compute( new WorldStateSources { Economy = economy, Guests = guests } );
		}
		Assert.AreEqual( Run( false ), Run( true ) );
	}

	// ---- Boundaries ---------------------------------------------------------------------------------

	/// <summary>
	/// Presentation (audio, particles, the weather's visuals, the advisor, the guests' and balloons' drawing, the path tiles' texture roll of PATH-014) takes no draw of the world generator,
	/// and the weather's own simulation stream ([EXT:weather-stream]) is not the world generator either. Every file named here must exist, so a rename cannot make the check pass by scanning nothing.
	/// </summary>
	[TestMethod]
	public void PresentationCodeNeverReachesTheWorldGenerator()
	{
		var root = new DirectoryInfo( AppContext.BaseDirectory );
		while ( root != null && !File.Exists( Path.Combine( root.FullName, "docs", "DETERMINISM.md" ) ) )
			root = root.Parent;
		Assert.IsNotNull( root, "the repository root" );
		var source = Path.Combine( root.FullName, "source", "OpenTPW" );
		var files = new List<string>();
		foreach ( var folder in new[] { "Audio", "World/Particles", "World/Weather", "World/Terrain" } )
		{
			Assert.IsTrue( Directory.Exists( Path.Combine( source, folder ) ), folder );
			files.AddRange( Directory.EnumerateFiles( Path.Combine( source, folder ), "*.cs", SearchOption.AllDirectories ).Select( file => Path.GetRelativePath( source, file ) ) );
		}
		files.AddRange( Directory.EnumerateFiles( Path.Combine( source, "World" ), "Advisor*.cs" ).Select( file => Path.GetRelativePath( source, file ) ) );
		foreach ( var file in new[] { "World/ParkWeather.cs", "World/Original/PathTileVariants.cs", "World/Guests/GuestRenderer.cs", "World/Guests/BalloonRenderer.cs", "World/Sky.cs", "World/Sun.cs",
			"Online/ParkPresence.cs", "Online/ParkPresenceState.cs", "Online/VisitPresenceSmoke.cs", "Hud/ParkHud.VisitChat.cs", "Online/ChatPanel.cs", "Online/ChatPanelModel.cs" } )
		{
			Assert.IsTrue( File.Exists( Path.Combine( source, file ) ), file );
			files.Add( file );
		}
		Assert.IsTrue( files.Count > 25, $"{files.Count} presentation files scanned" );
		var offenders = files.Distinct().Where( file => Regex.IsMatch( File.ReadAllText( Path.Combine( source, file ) ), @"\bWorldRng\b|\.World\.(NextWord|Next|Reseed)\b|NextWorldDraw" ) ).ToList();
		Assert.AreEqual( 0, offenders.Count, string.Join( ", ", offenders ) );
	}
}
