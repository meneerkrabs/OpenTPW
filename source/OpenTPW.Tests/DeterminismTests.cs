using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>Reproducible runs (docs/DETERMINISM.md): world seed, per-park state, park-save streams and the canonical hash.</summary>
[TestClass]
public class DeterminismTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private const int HalfRun = 6000;

	/// <summary>
	/// Economy, guests, a shop script and an unseeded RAND script wired like Level: guests pay into the
	/// economy and every stream comes from one <see cref="WorldSeed"/>. Ticks run in Level.Update's order.
	/// </summary>
	private sealed class SyntheticPark
	{
		private readonly GuestEconomyBridge payments;
		private readonly RideVisitorBridge shop;
		private readonly RideVM shopScript;
		private readonly List<RideVM> dice = new();

		public SyntheticPark( WorldSeed seed )
		{
			Seed = seed;
			Economy = EconomyTestData.Park( seed: seed.EconomyStream );
			// Fastest speed so the staff pool (economy stream) draws within the run.
			Economy.Speed = GameSpeed.Fastest;
			Guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), seed.GuestStream );
			Scripts = new RideScriptWorld( seed.ScriptStream );
			// the park's one world generator, as Level links it: the economy's, which the guests and the scripts draw from
			Guests.AttachWorld( Economy.World );
			Scripts.AttachWorld( Economy.World );
			Sound = new SoundEventSystem( new AudioMixer( null ), seed.SoundStream );
			payments = new GuestEconomyBridge( () => Economy, Guests );
			Guests.Payments = payments;
			Economy.GuestStatistics = payments;
			Economy.RideOperations = payments;
			Economy.OpenPark();
			shop = new RideVisitorBridge( Scripts.AllocateAttractionId(), "Drinks Shop", RideVisitorKind.Shop, 1, 70, 30, GuestNeeds.Thirst ) { EntranceCell = (10, 9), ExitCell = (10, 9) };
			shopScript = GuestTests.CreateShopScript( new VisitorRideScriptEffects( shop, UnimplementedRideScriptEffects.Instance ), Scripts, seed: null );
			shop.Attach( shopScript, () => true );
			Guests.Register( shop );
			Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, Economy.TryBuild( 1203, out var bought ) );
			payments.Link( shop.AttractionId, bought!.Id );
			AddDice();
		}

		public WorldSeed Seed { get; }
		public ParkEconomy Economy { get; private set; }
		public GuestSimulation Guests { get; }
		public RideScriptWorld Scripts { get; }
		/// <summary>A device-less sound chooser, as GameAudio.EnterPark seeds it.</summary>
		public SoundEventSystem Sound { get; }
		/// <summary>The same capture, restore and hash code Level's SavePark/LoadPark use; no park runtime here.</summary>
		public ParkWorldStreams Streams => new( Seed, null, Guests, Scripts, Sound );
		public int AttractionId => shop.AttractionId;
		public long Ticks { get; private set; }

		/// <summary>An unseeded script: its seed comes from the park's script stream.</summary>
		public void AddDice() => dice.Add( Script( new Asm()
			.Label( "loop" ).I( Opcode.RAND, V( 0 ), 1000 )
			.I( Opcode.ADD, V( 1 ), V( 0 ) )
			.I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" ), new RideVMOptions { World = Scripts } ) );

		public void Run( int ticks )
		{
			for ( var tick = 0; tick < ticks; tick++ )
			{
				Guests.Tick( Tick );
				shop.HostStep();
				shopScript.Advance( Tick );
				foreach ( var script in dice )
					script.Advance( Tick );
				// The fixture runs the economy at its Fastest speed, four ticks to a step (the staff pool draws within the run).
				Economy.Advance( (int)Economy.Speed );
				// Both halves of a save/load run reach this tick, so the script stream is consumed after a load.
				if ( ++Ticks == HalfRun * 3 / 2 )
					AddDice();
			}
		}

		public WorldRandomState RandomState => Streams.CaptureRandomState();

		public void Save( string path ) => ParkSaveFile.Save( path, Economy, RandomState );

		public void Load( string path )
		{
			var (economy, world) = ParkSaveFile.LoadState( path, ( theme, easy ) => (Economy.Settings, Economy.Catalog) );
			Economy = economy;
			Economy.GuestStatistics = payments;
			Assert.IsNotNull( world );
			Assert.AreEqual( Seed.Value, world.Seed );
			// the loaded economy holds its own generator object: the guests and the scripts continue it
			Guests.AttachWorld( Economy.World );
			Scripts.AttachWorld( Economy.World );
			Streams.RestoreRandomState( world );
		}

		public ulong Hash => WorldStateHash.Compute( new WorldStateSources { Seed = Seed, Economy = Economy, Guests = Guests, Scripts = Scripts } );
	}

	private static RideVM Script( Asm asm, RideVMOptions options ) =>
		new( new RideScriptFile( new MemoryStream( asm.Build( new[] { "V0", "V1" } ) ) ), options );

	private static ulong RunToEnd( WorldSeed seed )
	{
		var park = new SyntheticPark( seed );
		park.Run( 2 * HalfRun );
		Assert.IsTrue( park.Guests.Admissions > 10, "guests arrived and paid" );
		return park.Hash;
	}

	[TestMethod]
	public void DefaultWorldSeedKeepsThePreviousStreamSeeds()
	{
		var seed = WorldSeed.Default;
		Assert.AreEqual( Level.GuestSeed, seed.GuestStream, "guests keep Level.GuestSeed" );
		Assert.AreEqual( 1UL, seed.EconomyStream, "the economy keeps ParkEconomy.CreateForTheme's seed 1" );
		Assert.AreEqual( 0, seed.ObjectScriptKey, "placed objects keep script seeds 1, 2, 3, …" );
		var other = new WorldSeed( 1 );
		Assert.AreNotEqual( seed.GuestStream, other.GuestStream );
		Assert.AreNotEqual( seed.EconomyStream, other.EconomyStream );
		Assert.AreNotEqual( seed.ScriptStream, other.ScriptStream );
		Assert.AreNotEqual( seed.SoundStream, other.SoundStream );
		Assert.AreNotEqual( 0, other.ObjectScriptKey );
	}

	[TestMethod]
	public void AttractionIdsArePerParkNotPerProcess()
	{
		var first = new RideScriptWorld();
		var second = new RideScriptWorld();
		Assert.AreEqual( 1, first.AllocateAttractionId() );
		Assert.AreEqual( 2, first.AllocateAttractionId() );
		Assert.AreEqual( 1, second.AllocateAttractionId(), "a second park in the same process starts at 1 again" );
		Assert.AreEqual( new SyntheticPark( WorldSeed.Default ).AttractionId, new SyntheticPark( WorldSeed.Default ).AttractionId );
	}

	[TestMethod]
	public void UnseededScriptsDrawFromTheirWorldStream()
	{
		int[] Draws( ulong worldSeed )
		{
			var world = new RideScriptWorld( worldSeed );
			var vm = Script( new Asm().Label( "loop" ).I( Opcode.RAND, V( 0 ), 1000 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" ), new RideVMOptions { World = world } );
			return Enumerable.Range( 0, 16 ).Select( _ =>
			{
				vm.Advance( RideVMTests.Tick );
				return vm.Variables[0];
			} ).ToArray();
		}
		CollectionAssert.AreEqual( Draws( 5 ), Draws( 5 ) );
		CollectionAssert.AreNotEqual( Draws( 5 ), Draws( 6 ) );
	}

	[TestMethod]
	public void SameSeedTwiceInOneProcessGivesTheSameHash()
	{
		Assert.AreEqual( RunToEnd( WorldSeed.Default ), RunToEnd( WorldSeed.Default ) );
		Assert.AreEqual( RunToEnd( new WorldSeed( 77 ) ), RunToEnd( new WorldSeed( 77 ) ) );
	}

	[TestMethod]
	public void DifferentSeedGivesADifferentHash()
	{
		Assert.AreNotEqual( RunToEnd( WorldSeed.Default ), RunToEnd( new WorldSeed( 77 ) ) );
		Assert.AreNotEqual( RunToEnd( new WorldSeed( 77 ) ), RunToEnd( new WorldSeed( 78 ) ) );
	}

	/// <summary>
	/// Pins the hash of a fixed run, so it is checked in every test process: equal values in separate
	/// processes and machines mean no process state (counters, Random.Shared, string hashing) leaks in.
	/// A deliberate change to the simulation or to the hashed fields changes this value.
	/// </summary>
	[TestMethod]
	public void HashOfAFixedRunIsTheSameInEveryProcess()
	{
		var park = new SyntheticPark( WorldSeed.Default );
		park.Run( 3600 );
		Assert.AreEqual( FixedRunHash, park.Hash, $"0x{park.Hash:X16}" );
	}

	/// <summary>The hash of <see cref="HashOfAFixedRunIsTheSameInEveryProcess"/>'s run, for tests that repeat it under other conditions.</summary>
	internal static ulong RunFixedPark()
	{
		var park = new SyntheticPark( WorldSeed.Default );
		park.Run( 3600 );
		return park.Hash;
	}

	/// <summary>The pinned hash of <see cref="HashOfAFixedRunIsTheSameInEveryProcess"/>'s run (docs/DETERMINISM.md).</summary>
	internal const ulong FixedRunHash = 0x7AED41D19433D4B2UL;

	/// <summary>
	/// Switching only the <c>opentpw.online</c> extension (docs/MODS.md) leaves the pinned digests unchanged: this fixed run,
	/// the guest pathing of a cross park and the empty mod set's digest.
	/// </summary>
	[TestMethod]
	public void TogglingOnlyTheOnlineExtensionKeepsThePinnedDigests()
	{
		ulong? pathing = null;
		foreach ( var online in new[] { true, false } )
		{
			var set = OnlineExtensionGateTests.WithOnline( online );
			using var scope = OpenTPW.Mods.OpenTpwExtensions.Use( set );
			Assert.AreEqual( online, FeatureGates.Online );
			Assert.AreEqual( "", set.Digest, "no files-based mods" );
			var park = new SyntheticPark( WorldSeed.Default );
			park.Run( 3600 );
			Assert.AreEqual( FixedRunHash, park.Hash, $"0x{park.Hash:X16}" );
			var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
			for ( var tick = 0; tick < 120 * 60; tick++ )
				simulation.Tick( Tick );
			pathing ??= simulation.ComputeStateHash();
			Assert.AreEqual( pathing, simulation.ComputeStateHash(), "the guest pathing hash does not depend on the online extension" );
		}
	}

	[TestMethod]
	public void SaveAndLoadMidRunGivesTheSameFinalHashAsAnUninterruptedRun()
	{
		var seed = new WorldSeed( 2026 );
		var reference = new SyntheticPark( seed );
		reference.Run( HalfRun );
		var economyStreamAtHalf = reference.Economy.World.State;
		reference.Run( HalfRun );
		Assert.AreNotEqual( economyStreamAtHalf, reference.Economy.World.State, "the world generator is drawn after the save point" );

		var resumed = new SyntheticPark( seed );
		resumed.Run( HalfRun );
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-det-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );
		try
		{
			var path = Path.Combine( directory, "park.json" );
			resumed.Save( path );
			var atSave = resumed.Hash;
			var streams = resumed.RandomState;
			// Move the world generator away from the save point by drawing, not through the restore path under
			// test (the economy's staff pool draws from it; the guests are covered by
			// LoadRestoresEveryStreamDrawnAfterTheSave, because their ticks also move the unsaved guests of this legacy economy-only save).
			resumed.Economy.Advance( 50_000 );
			resumed.Scripts.World.NextWord();
			Assert.AreNotEqual( atSave, resumed.Hash );
			resumed.Load( path );
			Assert.AreEqual( streams, resumed.RandomState );
			Assert.AreEqual( atSave, resumed.Hash, "the load restores the hashed state of the save point" );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
		resumed.Run( HalfRun );
		Assert.AreEqual( reference.Hash, resumed.Hash, "a save/load mid-run continues exactly like the uninterrupted run" );
	}

	/// <summary>
	/// Every saved stream is moved by drawing from it (guest ticks, a script seed) or, for the sound chooser,
	/// whose draws never store their successor, by reseeding it; never through RestoreRandomState, so a
	/// restore that does nothing fails here.
	/// </summary>
	[TestMethod]
	public void LoadRestoresEveryStreamDrawnAfterTheSave()
	{
		Log ??= new();
		var park = new SyntheticPark( new WorldSeed( 2026 ) );
		park.Run( HalfRun );
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-det-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );
		try
		{
			var path = Path.Combine( directory, "park.json" );
			park.Sound.Draw();
			park.Save( path );
			var saved = park.RandomState;
			Assert.AreEqual( park.Sound.Seed, saved.SoundSeed, "the chooser's advanced state is what the save holds (AUDIO-037: the original's chooser is clock-seeded and unsaved)" );
			Assert.AreNotEqual( park.Seed.SoundStream, saved.SoundSeed, "the draw before the save advanced the chooser (tp.exe 0x6bc84c stores every draw's state)" );
			for ( var tick = 0; tick < 600; tick++ )
				park.Guests.Tick( Tick );
			var afterGuests = park.RandomState.Generator;
			park.Scripts.World.NextWord();
			park.Sound.Draw();
			park.Sound.Reseed( saved.SoundSeed + 1 );
			var moved = park.RandomState;
			Assert.AreNotEqual( saved.Generator, afterGuests, "guest ticks drew from the world generator" );
			Assert.AreNotEqual( afterGuests, moved.Generator, "a script draw moved the same generator" );
			Assert.AreNotEqual( saved.SoundSeed, moved.SoundSeed );
			park.Load( path );
			Assert.AreEqual( saved.Generator, park.Guests.RandomState, "the guests' generator" );
			Assert.AreEqual( saved.Generator, park.Scripts.RandomState, "the scripts' generator" );
			Assert.AreSame( park.Guests.World, park.Scripts.World, "one generator for everyone after the load" );
			Assert.AreEqual( saved.SoundSeed, park.Sound.Seed, "sound seed" );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	/// <summary>The sound chooser advances on UI clicks and audio timing, so it is saved but not hashed.</summary>
	[TestMethod]
	public void SoundDrawsDoNotChangeTheCanonicalHash()
	{
		var park = new SyntheticPark( WorldSeed.Default );
		park.Run( 600 );
		var before = park.Streams.ComputeStateHash();
		park.Sound.Draw();
		Assert.AreNotEqual( park.Seed.SoundStream, park.Sound.Seed, "the draw advanced the chooser" );
		Assert.AreEqual( before, park.Streams.ComputeStateHash() );
		Assert.AreEqual( before, new ParkWorldStreams( park.Seed, null, park.Guests, park.Scripts, null ).ComputeStateHash(), "a headless run hashes like a run with sound" );
	}

	[TestMethod]
	public void ParkSaveRoundTripsTheWorldStreams()
	{
		var park = EconomyTestData.Park();
		var state = new WorldRandomState( 0xFFFF_FFFF_FFFF_FFFF, 0x8000_0001, 0xFFFF_FFFF );
		var json = ParkSaveFile.Serialize( park, state );
		Assert.AreEqual( state, ParkSaveFile.ToWorldState( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ) ) );
		Assert.AreEqual( json, ParkSaveFile.Serialize( ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog ), state ) );
		// Saves without the section (economy-only callers, older files) load with no world streams.
		var economyOnly = ParkSaveFile.Serialize( park );
		Assert.IsNull( ParkSaveFile.ToWorldState( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( economyOnly ) ) ) );
		var older = Regex.Replace( economyOnly, @",\s*""World"": null", "" );
		Assert.AreNotEqual( economyOnly, older, "the section was removed for the older-save case" );
		Assert.IsNull( ParkSaveFile.ToWorldState( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( older ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.Replace( "\"SoundSeed\"", "\"Unknown\"" ) ) ) );
	}

	/// <summary>
	/// Forbidden in simulation code: unseeded or shared <see cref="Random"/> (also target-typed <c>new()</c>),
	/// the wall clock, mutable static numeric fields and properties (process-wide counters), and
	/// <c>GetHashCode</c>/<c>HashCode</c> (string hash codes are randomized per process in .NET).
	/// </summary>
	internal static readonly Regex ForbiddenInSimulation = new(
		@"new\s+(System\.)?Random\s*\(\s*\)|\bRandom\??\s+\w+\s*=\s*new\s*\(\s*\)|Random\.Shared|RandomVector3|Environment\.TickCount|DateTime(Offset)?\.(Utc)?Now|Stopwatch"
		+ @"|^(?!.*\b(readonly|const)\b).*\bstatic\s+(volatile\s+)?(s?byte|u?short|u?int|u?long|nu?int|float|double|decimal|(System\.)?(U?Int(16|32|64)|S?Byte|Single|Double|Decimal))\??\s+\w+\s*([;{]|=(?!>))"
		+ @"|\.GetHashCode\s*\(|\bHashCode\.\w+|new\s+HashCode\s*\(",
		RegexOptions.CultureInvariant );

	private const string ApprovedCivilTimeProvider = "civilTime = GameEditions.Current == GameEdition.ThemeParkWorld ? clock ?? (() => DateTime.Now) : null;";

	internal static bool IsApprovedCivilTimeProvider( string relativePath, string line ) =>
		relativePath.Replace( '\\', '/' ) == "VM/RideScriptWorld.cs" && line.Trim() == ApprovedCivilTimeProvider;

	/// <summary>Top-level World files that are presentation (cameras, sky, advisor UI, sandbox file I/O); every other World file is scanned.</summary>
	private static readonly string[] PresentationWorldFiles =
	{
		"Advisor.cs", "AdvisorController.cs", "AdvisorResponses.cs", "AdvisorScoreQueue.cs", "Camera.cs", "CameraMode.cs",
		"GlobeCameraMode.cs", "LobbyCameraMode.cs", "ParkCameraMode.cs", "SandboxSave.cs", "Sky.cs", "Sun.cs"
	};

	/// <summary>Simulation folders reject uncontrolled sources except one explicitly qualified host-civil provider boundary.</summary>
	[TestMethod]
	public void SimulationCodeUsesNoProcessWideRandomnessOrClock()
	{
		var root = new DirectoryInfo( AppContext.BaseDirectory );
		while ( root != null && !File.Exists( Path.Combine( root.FullName, "docs", "DETERMINISM.md" ) ) )
			root = root.Parent;
		if ( root == null )
			Assert.Inconclusive( "Repository sources are not next to the test binaries." );
		var source = Path.Combine( root.FullName, "source", "OpenTPW" );
		var folders = new[] { "VM", "Economy", Path.Combine( "World", "Guests" ), Path.Combine( "World", "Objects" ), Path.Combine( "World", "Coasters" ) };
		var world = Directory.EnumerateFiles( Path.Combine( source, "World" ), "*.cs", SearchOption.TopDirectoryOnly )
			.Where( path => !PresentationWorldFiles.Contains( Path.GetFileName( path ) ) ).ToList();
		CollectionAssert.IsSubsetOf( new[] { "Level.cs", "Level.Objects.cs", "FixedStepClock.cs", "PrototypeRide.cs", "Ride.cs" }, world.Select( Path.GetFileName ).ToArray() );
		var lines = folders.SelectMany( folder => Directory.EnumerateFiles( Path.Combine( source, folder ), "*.cs", SearchOption.AllDirectories ) )
			.Concat( world )
			.SelectMany( path => File.ReadLines( path ).Select( ( line, index ) => (path, line, index) ) ).ToList();
		Assert.AreEqual( 1, lines.Count( item => IsApprovedCivilTimeProvider( Path.GetRelativePath( source, item.path ), item.line ) ),
			"Only the exact single TPW-gated provider boundary is approved (the host clock input, [EXT:vm-host-time])." );
		var hits = lines.Where( item => ForbiddenInSimulation.IsMatch( item.line )
			&& !IsApprovedCivilTimeProvider( Path.GetRelativePath( source, item.path ), item.line ) )
			.Select( item => $"{Path.GetRelativePath( root.FullName, item.path )}:{item.index + 1}: {item.line.Trim()}" ).ToList();
		Assert.AreEqual( 0, hits.Count, string.Join( Environment.NewLine, hits ) );
	}

	/// <summary>Each forbidden form is caught, and the allowed forms next to them are not (the guard's own mutation check).</summary>
	[TestMethod]
	public void SourceGuardCatchesEachForbiddenForm()
	{
		var forbidden = new[]
		{
			"var random = new Random();",
			"private readonly System.Random random = new System.Random( );",
			"System.Random leak = new();",
			"private Random? random = new();",
			"var x = Random.Shared.Next();",
			"var seed = Environment.TickCount;",
			"var now = DateTime.UtcNow;",
			"var now = DateTimeOffset.Now;",
			"var watch = Stopwatch.StartNew();",
			"private static int nextAttractionId = 1;",
			"private static long attractionCounter;",
			"static ulong state = 5;",
			"internal static volatile uint ticks;",
			"private static float scale;",
			"public static System.Int64 Count;",
			"public static int NextId { get; set; }",
			"var bucket = name.GetHashCode() % 7;",
			"var bucket = string.GetHashCode( name );",
			"var bucket = StringComparer.Ordinal.GetHashCode( name );",
			"var key = HashCode.Combine( name, id );",
		};
		var allowed = new[]
		{
			"var random = new Random( seed );",
			"private readonly Random random = new( seed );",
			"private static readonly int Limit = 3;",
			"private readonly static int Limit = 3;",
			"private const int Limit = 3;",
			"private static bool logged;",
			"public static int Clamp( int value ) => value;",
			"private static int Limit => 3;",
			"public override int GetHashCode() => Id;",
			"private int nextId;",
		};
		foreach ( var line in forbidden )
			Assert.IsTrue( ForbiddenInSimulation.IsMatch( line ), line );
		foreach ( var line in allowed )
			Assert.IsFalse( ForbiddenInSimulation.IsMatch( line ), line );
	}

	[TestMethod]
	public void CivilTimeBoundaryDoesNotExemptOtherClockExpressionsOrFiles()
	{
		Assert.IsTrue( IsApprovedCivilTimeProvider( "VM/RideScriptWorld.cs", ApprovedCivilTimeProvider ) );
		Assert.IsTrue( ForbiddenInSimulation.IsMatch( ApprovedCivilTimeProvider ), "The underlying guard still detects the clock." );
		Assert.IsFalse( IsApprovedCivilTimeProvider( "Economy/RideScriptWorld.cs", ApprovedCivilTimeProvider ) );
		Assert.IsFalse( IsApprovedCivilTimeProvider( "VM/Other.cs", ApprovedCivilTimeProvider ) );
		foreach ( var line in new[] { "var leak = DateTime.Now;", "var leak = DateTime.UtcNow;", "var leak = DateTimeOffset.Now;",
			"var leak = DateTimeOffset.UtcNow;", ApprovedCivilTimeProvider + " var leak = DateTime.Now;",
			ApprovedCivilTimeProvider.Replace( "GameEditions.Current == GameEdition.ThemeParkWorld", "true" ) } )
		{
			Assert.IsTrue( ForbiddenInSimulation.IsMatch( line ), line );
			Assert.IsFalse( IsApprovedCivilTimeProvider( "VM/RideScriptWorld.cs", line ), line );
		}
	}
}

/// <summary>Per-park attraction ids with the original objects; inconclusive without OPENTPW_GAME_PATH.</summary>
[TestClass]
[DoNotParallelize]
public class DeterminismAssetTests
{
	/// <summary>
	/// Level.SavePark/LoadPark delegate to <see cref="ParkWorldStreams"/>; this runs that path with the real
	/// jungle park runtime and guests (Level itself needs a renderer). Every stream is moved by drawing, except the
	/// sound chooser, whose draws never store their successor (0x100118C8 is its only store), which is reseeded.
	/// </summary>
	[TestMethod]
	public void ParkSaveAndLoadThroughTheLevelPathRestoreEveryStream()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the jungle park." );
		Log ??= new();
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-det-{Guid.NewGuid():N}" );
		try
		{
			var seed = new WorldSeed( 2026 );
			var original = OriginalPark.Load( "jungle" );
			var park = ParkEconomyRuntime.ForOriginalLevel( original, ParkStartKind.OriginalSaveReference, seed );
			var guests = new GuestSimulation( GuestPathGrid.FromOriginal( original.Map, original.Save ), GuestSettings.Load( "jungle" ), seed.GuestStream );
			park.AttachGuests( guests );
			var streams = new ParkWorldStreams( seed, park, guests, new RideScriptWorld( seed.ScriptStream ), new SoundEventSystem( new AudioMixer( null ), seed.SoundStream ) ).LinkGenerator();
			void Run( int ticks )
			{
				for ( var tick = 0; tick < ticks; tick++ )
				{
					guests.Tick( 1f / FixedStepClock.TicksPerSecond );
					park.Economy.Advance( (int)park.Economy.Speed );
				}
			}
			park.Economy.Speed = GameSpeed.Fastest;
			Run( 3000 );
			streams.Scripts.World.NextWord();
			streams.Sound!.Draw();
			Directory.CreateDirectory( directory );
			var path = Path.Combine( directory, "park.json" );
			streams.SavePark( path );
			var saved = streams.CaptureRandomState();
			var economy = ParkSaveFile.Serialize( park.Economy );
			Run( 3000 );
			streams.Scripts.World.NextWord();
			// A draw stores the chooser's advanced state (tp.exe 0x6bc84c), and the park saves it so a resume continues the same draws.
			Assert.AreEqual( saved.SoundSeed, streams.CaptureRandomState().SoundSeed, "no draw since the save" );
			streams.Sound.Draw();
			streams.Sound.Reseed( unchecked(saved.SoundSeed + 1) );
			var moved = streams.CaptureRandomState();
			Assert.IsTrue( saved.Generator != moved.Generator && saved.SoundSeed != moved.SoundSeed, "every stream was drawn after the save" );
			Assert.AreNotEqual( economy, ParkSaveFile.Serialize( park.Economy ) );
			streams.LoadPark( path );
			Assert.AreEqual( saved, streams.CaptureRandomState() );
			Assert.AreEqual( economy, ParkSaveFile.Serialize( park.Economy ), "the economy (and its stream) is the saved one" );
			Assert.AreSame( park.Guests, park.Economy.GuestStatistics, "the loaded economy keeps the guest statistics" );
		}
		finally
		{
			if ( Directory.Exists( directory ) )
				Directory.Delete( directory, true );
			FileSystem = previous;
		}
	}

	[TestMethod]
	public void AParkWithACivilClockObjectHashesTheSameUnderAFixedClock()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the Halloween clock tower." );
		try
		{
			// Haunted Tower (Info.Id 2418) spawns Clock.RSE, which reads HOUR.
			var tower = ObjectCatalog.Load( "hallow" ).Find( 2418 ) ?? throw new AssertFailedException( "Haunted Tower is missing from the Halloween catalog." );
			ulong Run( Func<DateTime> clock )
			{
				var world = new RideScriptWorld( 7, clock );
				var runtime = new OriginalObjectRuntime( tower, world, seed: 1, open: true );
				for ( var tick = 0; tick < 120 * FixedStepClock.TicksPerSecond; tick++ )
					runtime.Simulate( FixedStepClock.TickDuration );
				var hash = WorldStateHash.Compute( new WorldStateSources { Scripts = world } );
				Assert.IsTrue( world.Scripts.Count > 0, "the tower's scripts are live when hashed" );
				runtime.Stop();
				return hash;
			}
			var reads = 0;
			var fixedRun = Run( () => { reads++; return RideScriptWorld.FixedCivilTime; } );
			Assert.IsTrue( reads > 0, "the tower's scripts read the civil clock" );
			Assert.AreEqual( fixedRun, Run( RideScriptWorld.FixedClock ), "a fixed civil time gives the same run" );
			Assert.AreNotEqual( Run( () => new DateTime( 2000, 1, 1, 3, 0, 0 ) ), Run( () => new DateTime( 2000, 1, 1, 16, 0, 0 ) ),
				"the hour reaches the world state, so a headless run that pins a hash must fix the clock" );
			// MIN and SEC are host time too (tp.exe 0x5566FF, 0x55674E), so a two-run comparison with a real clock can flake on them as well.
			// In this 120 s run the minute and second do not reach the hash (only the hour does).
			Assert.AreEqual( Run( () => new DateTime( 2000, 1, 1, 3, 10, 20 ) ), Run( () => new DateTime( 2000, 1, 1, 3, 40, 50 ) ) );
		}
		finally
		{
			FileSystem = previous;
		}
	}

	[TestMethod]
	public void OriginalObjectAttractionIdsRestartInEachPark()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the object corpus." );
		var bonus = ObjectCatalog.BonusDataRoot;
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			var entries = ObjectCatalog.Load( "jungle" ).Entries.Where( entry => entry.ScriptPath != null ).Take( 4 ).ToArray();
			(int[] Ids, ulong Hash) Park()
			{
				var seed = WorldSeed.Default;
				var world = new RideScriptWorld( seed.ScriptStream );
				var runtimes = entries.Select( ( entry, index ) => new OriginalObjectRuntime( entry, world, (index + 1) ^ seed.ObjectScriptKey ) ).ToArray();
				for ( var tick = 0; tick < 10 * FixedStepClock.TicksPerSecond; tick++ )
					foreach ( var runtime in runtimes )
						runtime.Simulate( FixedStepClock.TickDuration );
				return (runtimes.Select( runtime => runtime.Visitors.AttractionId ).ToArray(), WorldStateHash.Compute( new WorldStateSources { Scripts = world } ));
			}
			var first = Park();
			var second = Park();
			CollectionAssert.AreEqual( Enumerable.Range( 1, entries.Length ).ToArray(), first.Ids );
			CollectionAssert.AreEqual( first.Ids, second.Ids, "a second park in the same process gets the same ids" );
			Assert.AreEqual( first.Hash, second.Hash );
		}
		finally
		{
			ObjectCatalog.BonusDataRoot = bonus;
			FileSystem = previous;
		}
	}
}
