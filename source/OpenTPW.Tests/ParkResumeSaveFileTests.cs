using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// The own-format world-save envelope's codec and file behaviour (save/load milestone): a complete synthetic
/// park — real guests walking and queueing in a miniature simulation, the legacy economy embedded by value,
/// a hand-built runtime with vertex caches — round-trips byte-stably through the JSON codec with every tuple
/// array intact; malformed saves (missing required members, unknown members, wrong format or version) are
/// rejected before anything is used; oversized files are refused before they are read; saves are atomic,
/// written with the own-format suffix only (original TPWS/TPWI are never written or read), and a failed write
/// leaves the previous save untouched with no temporary file left behind. No game data needed.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkResumeSaveFileTests
{
	/// <summary>
	/// A one-attraction guest simulation on a 20×10 path row (the guest lane's rig, kept small): guests arrive
	/// along both lanes, pay, wander and queue, so the captured snapshot holds tuple arrays (lanes, routes,
	/// queue cells) and live membership — the content the envelope's JSON codec must survive.
	/// </summary>
	internal sealed class MiniaturePark
	{
		public const float Tick = 1f / FixedStepClock.TicksPerSecond;
		public readonly GuestPathGrid Grid = new( 20, 10 );
		public readonly GuestSimulation Simulation;
		private readonly RideVM script;

		/// <summary>
		/// The guests' snapshot for a fixture whose layout is another, unstamped map: the walks' stamps (raised by this park's own path
		/// edits) are rebased to 0, as a walk set up on that map would hold, so the save's walk stamps stay within its layout's counter.
		/// </summary>
		public GuestSimulationSnapshot CaptureGuestsForUnstampedLayout()
		{
			var snapshot = Simulation.CaptureSnapshot();
			return snapshot with
			{
				Guests = snapshot.Guests.Select( guest => guest.Navigation.OriginalWalk is { } walk
					? guest with { Navigation = guest.Navigation with { OriginalWalk = walk with { Stamp = 0 } } }
					: guest ).ToArray()
			};
		}

		public MiniaturePark()
		{
			for ( var x = 0; x < 20; x++ )
				Grid.SetPath( x, 0, true );
			var settings = GuestTests.CreateSettings();
			// the lanes run along row 8, clear of the ride's queue column (x = 2)
			settings.ArrivalLaneA = new[] { (0, 8), (4, 8), (6, 8), (8, 8), (10, 0) };
			settings.ArrivalLaneB = new[] { (19, 8), (15, 8), (13, 8), (12, 8), (9, 0) };
			settings.ArrivalTimeBetween = 2;
			settings.AdmissionFee = 5;
			settings.ExitLevelSeconds = 45;
			settings.ExitLevelVarSeconds = 15;
			settings.MaximumGuests = 150;
			Simulation = new GuestSimulation( Grid, settings, 7 );
			var bridge = new RideVisitorBridge( 1, "Snake ride", RideVisitorKind.Ride, 5, 55, 30 )
			{
				EntranceCell = (2, 0),
				ExitCell = (2, 0),
				QueueFrontCell = (2, 5),
				QueueEntranceDirection = 2,
				Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 )
			};
			script = new RideVM( new RideScriptFile( new MemoryStream(
				new Asm().Label( "loop" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" ).Build( GuestTests.ShopVariables ) ) ),
				new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1 } );
			bridge.Attach( script, () => true );
			Assert.AreEqual( 5, QueuePaths.TryExtend( Grid, bridge, Enumerable.Range( 0, 5 ).Select( index => (2, 5 - index) ) ),
				"the fixture park builds its queue" );
			QueuePaths.TryConnect( Grid, bridge );
			Simulation.Register( bridge );
			script.Advance( Tick );
		}

		public void Step()
		{
			Simulation.Tick( Tick );
			script.Advance( Tick );
		}
	}

	/// <summary>A complete valid envelope around a miniature park (see the class comment); the mutation base line.
	/// Longer runs (more ticks) put guests further into the park, for fixtures that need settled in-park state.</summary>
	internal static ParkResumeSnapshot Envelope( int ticks = 3000 )
	{
		var park = new MiniaturePark();
		for ( var tick = 0; tick < ticks; tick++ )
			park.Step();
		var guests = park.Simulation.CaptureSnapshot();
		Assert.IsTrue( guests.Guests.Length >= 4, "the fixture park holds guests to save" );

		var scriptWorld = new RideScriptWorld();
		scriptWorld.AttachWorld( park.Simulation.World );   // one world generator for the whole park
		var scripts = scriptWorld.CaptureSnapshot() with { NextAttractionId = 2 };
		var economy = EconomyTestData.Park();
		var instance = economy.RegisterExisting( 1100 );
		var world = new WorldRandomState( WorldSeed.Default.Value, (uint)guests.RandomState, 42u );
		var seed = WorldSeed.Default;
		var objects = new ParkObjects( null!, new SandboxParkGrid(), seed );
		var layout = ParkLayoutSnapshot.CaptureSnapshot( park.Grid.Cells, objects ) with
		{
			Objects = new List<ParkLayoutSnapshot.PlacedObjectData>
			{
				new()
				{
					InfoId = 1100,
					Archive = "/levels/jungle/rides/bouncy.wad",
					X = 2,
					Y = 6,
					Rotation = 0,
					Open = true,
					CreationSeed = 1 ^ seed.ObjectScriptKey,
					AttractionId = 1,
					BaseHeight = 0,
					BaseHeightRule = 1,
					IsFixedItem = false
				}
			},
			NextSeed = 2,
			NextAttractionId = 2
		};
		return new ParkResumeSnapshot
		{
			Format = ParkResumeSaveFile.FormatName,
			Version = ParkResumeSaveFile.CurrentVersion,
			Theme = "jungle",
			StartKind = ParkStartKind.FullSimulation,
			Seed = seed.Value,
			DisplayName = "Test park",
			SavedUtc = new DateTimeOffset( 2026, 10, 12, 12, 0, 0, TimeSpan.Zero ),
			Economy = ParkSaveFile.Capture( economy, world ),
			Layout = layout,
			Guests = guests,
			Scripts = scripts,
			Runtimes = new List<ParkResumeRuntime> { new( 0, RuntimeSnapshot( 1, 1100, "/levels/jungle/rides/bouncy.wad" ) ) },
			EconomyBindings = new List<ParkResumeEconomyBinding> { new( 1, instance.Id ) },
			Clock = new FixedStepClockSnapshot( 12345, 0.004, 0 ),
			SoundSeed = 42,
			AssetHashes = new Dictionary<string, string> { ["rides/bouncy.wad"] = new string( 'a', 64 ) }
		};
	}

	/// <summary>A structurally valid runtime snapshot with no script, an animation channel and vertex caches.</summary>
	internal static OriginalObjectRuntimeSnapshot RuntimeSnapshot( int attractionId, int infoId, string archive ) => new()
	{
		AttractionId = attractionId,
		EntryArchivePath = archive,
		EntryInfoId = infoId,
		ModelPath = "models/object.hzm",
		HasScript = false,
		ScriptIdentityHash = 0,
		Animator = new ObjectAnimatorSnapshot
		{
			ClockMilliseconds = 1234.5,
			Serial = 3,
			HiddenVersion = 1,
			TicksPerSecond = 30f,
			KeepsPoseOnClipChange = false,
			Channels = new[]
			{
				new AnimatorChannelSnapshot
				{
					ChannelIndex = 0, ClipName = "spin", AnimationId = 2, Variant = 1, Loop = true, Speed = 1f,
					Serial = 3, StartMilliseconds = 900, Ended = false, Tick = 12.5f, ClipDurationTicks = 40,
					Deferred = new AnimatorDeferredClipSnapshot { Name = "idle", AnimationId = 1, Variant = 0, Loop = false, Speed = 1f, ClipDurationTicks = 30 }
				}
			},
			NodeStateBits = new bool[4],
			Meshes = new[]
			{
				new AnimatorMeshCacheSnapshot
				{
					Mesh = 0, VertexVersion = 0, VertexSerial = 3, VertexTick = 12.5f,
					Positions = new float[] { 1, 2, 3, 4, 5, 6 }, UvSerial = 0, UvTick = 0,
					TexCoords = new float[] { 0.25f, 0.75f, 0.5f, 0.5f }
				}
			},
			VertexLimitations = Array.Empty<string>()
		},
		Stopped = false,
		WasRunning = false,
		CompletedCycles = 2,
		AnimationsPlayed = 7,
		UnresolvedAnimations = new[] { "3/1" },
		HeldScream = false,
		OwnedScreamCount = 0
	};

	[TestMethod]
	public void ACompleteEnvelopeRoundTripsThroughTheOwnFormatCodec()
	{
		var snapshot = Envelope();
		var bytes = ParkResumeSaveFile.Serialize( snapshot );
		var restored = ParkResumeSaveFile.Deserialize( bytes );

		Assert.IsTrue( ParkResumeSaveFile.Serialize( restored ).SequenceEqual( bytes ), "the round trip re-serializes byte for byte" );
		Assert.AreEqual( ( snapshot.Format, snapshot.Version, snapshot.Theme, snapshot.StartKind, snapshot.Seed, snapshot.DisplayName, snapshot.SavedUtc, snapshot.SoundSeed ),
			(restored.Format, restored.Version, restored.Theme, restored.StartKind, restored.Seed, restored.DisplayName, restored.SavedUtc, restored.SoundSeed) );

		// Tuple arrays (routes, queue cells) and float caches survive as values, not as dropped or
		// re-ordered content: compare the restored guest list against the original, guest by guest.
		Assert.AreEqual( snapshot.Guests.Guests.Length, restored.Guests.Guests.Length );
		for ( var index = 0; index < snapshot.Guests.Guests.Length; index++ )
		{
			var (expected, actual) = (snapshot.Guests.Guests[index], restored.Guests.Guests[index]);
			CollectionAssert.AreEqual( expected.Navigation.Route, actual.Navigation.Route, $"guest {expected.Id}'s route" );
			CollectionAssert.AreEqual( expected.Navigation.AlsoOpen, actual.Navigation.AlsoOpen, $"guest {expected.Id}'s extra open cells" );
			Assert.IsNull( actual.Lane, $"guest {expected.Id} walks the cell map, not a lane" );
		}
		Assert.IsTrue( snapshot.Guests.Guests.Any( guest => guest.State is GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth or GuestState.WaitingAtBooth
			or GuestState.AtTicketBooth or GuestState.EnteringPark ), "the fixture park holds a guest on the arrival walk" );
		Assert.IsTrue( snapshot.Guests.Guests.Any( guest => guest.Navigation.Route.Length > 1 ), "the fixture park holds a guest mid-route" );

		var expectedBridge = snapshot.Guests.Attractions[0];
		var actualBridge = restored.Guests.Attractions[0];
		CollectionAssert.AreEqual( expectedBridge.QueueCells, actualBridge.QueueCells, "the queue's cells survive as tuples" );
		CollectionAssert.AreEqual( expectedBridge.Queue, actualBridge.Queue );
		CollectionAssert.AreEqual( expectedBridge.Limbo, actualBridge.Limbo );
		CollectionAssert.AreEqual( expectedBridge.Bounce, actualBridge.Bounce );

		var positions = restored.Runtimes[0].State.Animator.Meshes[0].Positions!;
		CollectionAssert.AreEqual( snapshot.Runtimes[0].State.Animator.Meshes[0].Positions!, positions, "the vertex cache survives" );
		CollectionAssert.AreEqual( snapshot.Runtimes[0].State.Animator.Meshes[0].TexCoords!, restored.Runtimes[0].State.Animator.Meshes[0].TexCoords!, "the UV cache survives" );
		Assert.AreEqual( snapshot.Runtimes[0].State.Animator.Channels[0].Deferred, restored.Runtimes[0].State.Animator.Channels[0].Deferred, "the deferred clip survives" );
		Assert.AreEqual( snapshot.Economy.World!.ScriptRandom, restored.Economy.World!.ScriptRandom, "the world streams survive" );
		Assert.AreEqual( snapshot.AssetHashes.Keys.Single(), restored.AssetHashes.Keys.Single() );
	}

	[TestMethod]
	public void MalformedSavesAreRejectedBeforeAnythingIsUsed()
	{
		var json = JsonNode.Parse( Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( Envelope() ) ) )!.AsObject();
		void Rejects( Action<JsonObject> corrupt, string message )
		{
			var copy = JsonNode.Parse( json.ToJsonString() )!.AsObject();
			corrupt( copy );
			var bytes = Encoding.UTF8.GetBytes( copy.ToJsonString() );
			var exception = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Deserialize( bytes ) );
			StringAssert.Contains( exception.Message, message );
		}
		Rejects( copy => copy.Remove( "Clock" ), "malformed" ); // a required member is missing
		Rejects( copy => copy.Remove( "AssetHashes" ), "malformed" );
		Rejects( copy => copy["Surprise"] = 1, "malformed" ); // an unknown member
		Rejects( copy => copy["Version"] = 99, "Unsupported world save version 99" );
		Rejects( copy => copy["Version"] = 0, "Unsupported world save version 0" );
		Rejects( copy => copy["Format"] = "opentpw-park", "Not an OpenTPW world save" ); // the reserved legacy format
		Rejects( copy => copy["Format"] = "tpws", "Not an OpenTPW world save" );
		Rejects( copy => copy["Theme"] = "moon", "not a Theme Park World theme" );
	}

	[TestMethod]
	public void AssetHelpersDigestCallerFilesAndBoundTheManifest()
	{
		var directory = Path.Combine( Path.GetTempPath(), "opentpw-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory );
		try
		{
			var first = Path.Combine( directory, "one.bin" );
			var second = Path.Combine( directory, "two.bin" );
			File.WriteAllBytes( first, new byte[] { 1, 2, 3 } );
			File.WriteAllBytes( second, new byte[] { 1, 2, 4 } );
			var manifest = ParkResumeAssets.HashFiles( new[] { ("one", first), ("two", second) } );
			Assert.AreEqual( 2, manifest.Count );
			Assert.IsTrue( manifest.Values.All( ParkResumeAssets.IsValidSha256 ), "digests are 64 lowercase hex characters" );
			Assert.AreNotEqual( manifest["one"], manifest["two"] );
			Assert.AreEqual( manifest["one"], ParkResumeAssets.Sha256( first ), "the digest is stable" );
			Assert.AreEqual( "039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81", manifest["one"], "SHA-256 of the bytes 1, 2, 3" );
			Assert.IsFalse( ParkResumeAssets.IsValidSha256( new string( '0', 63 ) ) );
			Assert.IsFalse( ParkResumeAssets.IsValidSha256( new string( 'g', 64 ) ) );
			Assert.ThrowsException<InvalidDataException>( () => ParkResumeAssets.HashFiles( new[] { ("one", first), ("one", second) } ), "a duplicate key is refused" );
			Assert.ThrowsException<InvalidDataException>( () => ParkResumeAssets.HashFiles( new[] { ("", first) } ), "an empty key is refused" );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public void OversizedFilesAreRejectedBeforeTheyAreRead()
	{
		var directory = Path.Combine( Path.GetTempPath(), "opentpw-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory );
		try
		{
			var path = Path.Combine( directory, "big.otpw" );
			using ( var stream = new FileStream( path, FileMode.Create ) )
				stream.SetLength( ParkResumeSaveFile.MaximumFileSize + 1L );
			var exception = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Load( path ) );
			StringAssert.Contains( exception.Message, "size limit" );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public void SaveWritesOnlyOwnFormatSuffixesAtomicallyAndKeepsThePriorFileOnFailure()
	{
		var snapshot = Envelope();
		var directory = Path.Combine( Path.GetTempPath(), "opentpw-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory );
		try
		{
			foreach ( var foreign in new[] { "park.tpws", "park.tpwi", "park.txt", "park" } )
			{
				var path = Path.Combine( directory, foreign );
				Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Save( path, snapshot ), foreign + " is not an own-format destination" );
				Assert.IsFalse( File.Exists( path ), foreign + " was not written" );
				Assert.IsNull( Directory.GetFiles( directory ).Select( Path.GetFileName ).FirstOrDefault( name => name.EndsWith( ".tmp", StringComparison.Ordinal ) ), "no temporary file for " + foreign );
			}

			var save = Path.Combine( directory, "park.otpw" );
			ParkResumeSaveFile.Save( save, snapshot );
			var previous = File.ReadAllBytes( save );
			ParkResumeSaveFile.Load( save );

			// A shape failure happens before a byte is written: the previous save survives, nothing temporary stays.
			Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Save( save, snapshot with { Version = ParkResumeSaveFile.CurrentVersion + 1 } ) );
			CollectionAssert.AreEqual( previous, File.ReadAllBytes( save ), "the previous save survives a failed write" );
			Assert.AreEqual( "park.otpw", Directory.GetFiles( directory ).Select( Path.GetFileName ).Single(), "no temporary file is left behind" );

			// The .json suffix is own-format too; original extensions are never read either.
			ParkResumeSaveFile.Save( Path.Combine( directory, "second.json" ), snapshot );
			Assert.AreEqual( 2, Directory.GetFiles( directory ).Length );
			var original = Path.Combine( directory, "original.tpwi" );
			File.WriteAllText( original, "x" );
			Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Load( original ) );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}
}
