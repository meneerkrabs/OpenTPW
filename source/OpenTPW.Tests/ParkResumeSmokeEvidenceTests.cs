using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Format tests of the native park-resume smoke evidence (park-resume milestone): the capture/restore pair
/// verification names the first divergence tick by tick, the containment rules keep the files to hashes and
/// counters (never asset bytes), and the JSON round trip is stable. These run without a GPU: the native phases
/// themselves are driven by OpenTPW.Client.ParkResumeSmokeTest through root's CLI hook.
/// </summary>
[TestClass]
public class ParkResumeSmokeEvidenceTests
{
	private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

	private static ParkResumeSmokeTest.SmokeCounters Counters( int seed ) => new()
	{
		Guests = 12 + seed, InPark = 10 + seed, WalkingAround = 3 + seed, GoingToRide = 2 + seed, InQueue = 4 + seed,
		BoardingOrWaiting = 1 + seed, Riding = 2 + seed, ExitingRide = seed, LeavingOrArriving = 1 + seed,
		Admissions = 30 + seed, Departed = 5 + seed, NextGuestId = 40 + seed, Attractions = 6, QueuedGuests = 4 + seed,
		Riders = 2 + seed, WalkingOff = seed, GuestTick = 5400 + seed, Objects = 8, NextAttractionId = 9,
		CellMapVersion = 7, Balance = 100_000 - seed, EconomyTick = 5400 + seed, EconomyObjects = 8,
		EconomyBindings = 7, StaffMembers = 2 + seed % 2, StaffActors = 2 + seed % 2, WeatherQuality = 80, Entities = 60 + seed,
	};

	private static ParkResumeSmokeTest.SmokeSample Sample( long tick ) => new()
	{
		Tick = tick,
		FullTypedSha256 = Hash,
		Sections = new Dictionary<string, string> { ["guests"] = Hash, ["scripts"] = Hash, ["runtimes"] = Hash },
		ComputeStateHash = "1759236",
		Counters = Counters( (int) tick ),
	};

	private static ParkResumeSmokeTest.SmokePostLoad PassedPostLoad() => new()
	{
		ParkReopenedAndClosed = true, ClosedParkRefusedAdmission = true, AdmissionBookedExactlyOnce = true,
		SoldObjectCreditedScrapOnce = true, SoldObjectLinksRemoved = true, NewObjectPlacedAfterSale = true,
		StaffHiredAndPaidOnce = true, PaymentBindingsOneToOne = true, BalanceAfter = 1, ScrapCredited = 1, WagesCharged = 1, StaffActors = 3,
	};

	private static ParkResumeSmokeTest.SmokeEvidence Evidence( string phase ) => new()
	{
		Phase = phase,
		Theme = "jungle",
		StartKind = "FullSimulation",
		Seed = "1234567890",
		SavedUtc = "2026-10-12T12:00:00.0000000Z",
		ManifestSha256 = Hash,
		Save = new ParkResumeSmokeTest.SmokeSaveFile { Name = "opentpw-jungle-world.json", ByteCount = 400_000, Sha256 = Hash },
		Boundary = Sample( 0 ),
		Ticks = new[] { Sample( 1 ), Sample( 2 ), Sample( 3 ) },
		Probes = new[] { new ParkResumeSmokeTest.SmokeProbe { Name = "version", Rejected = true, Message = "Unsupported world save version 6.", LiveParkIntact = true } },
		PostLoad = phase == "restore" ? PassedPostLoad() : null,
		Pngs = new[] { "native-smoke-park-resume-capture.png" },
		Completed = true,
	};

	[TestMethod]
	public void AVerifiedRestartPairPasses()
	{
		var problems = ParkResumeSmokeTest.VerifyEvidencePair( Evidence( "capture" ), Evidence( "restore" ) );
		Assert.AreEqual( "", string.Join( "; ", problems ), "an identical capture/restore pair must verify" );
	}

	[TestMethod]
	public void ATickHashMismatchNamesTheTick()
	{
		var capture = Evidence( "capture" );
		var ticks = Evidence( "restore" ).Ticks.ToArray();
		ticks[2] = ticks[2] with { FullTypedSha256 = Hash[..63] + "0" };
		var restore = Evidence( "restore" ) with { Ticks = ticks };
		var problems = ParkResumeSmokeTest.VerifyEvidencePair( capture, restore );
		// the third sample is continuation tick 3: a mismatch is named by its continuation tick
		Assert.IsTrue( problems.Any( problem => problem.StartsWith( "tick 3: full typed SHA-256" ) ),
			$"the mismatch must name tick 3's full typed hash, got: {string.Join( "; ", problems )}" );
	}

	[TestMethod]
	public void ARestoreThatSamplesOnlyAtFrameEndsIsComparedTickByTick()
	{
		// the restore phase runs several ticks a frame and samples the whole park at each frame's end
		var sparse = Evidence( "restore" ) with { Ticks = new[] { Sample( 2 ), Sample( 3 ) } };
		Assert.AreEqual( "", string.Join( "; ", ParkResumeSmokeTest.VerifyEvidencePair( Evidence( "capture" ), sparse ) ) );
		var drifted = sparse with { Ticks = new[] { Sample( 2 ) with { FullTypedSha256 = Hash[..63] + "0" }, Sample( 3 ) } };
		Assert.IsTrue( ParkResumeSmokeTest.VerifyEvidencePair( Evidence( "capture" ), drifted ).Any( problem => problem.StartsWith( "tick 2: full typed SHA-256" ) ),
			"a frame-end sample is compared with the capture's sample of the same tick" );
		var unknown = sparse with { Ticks = new[] { Sample( 2 ), Sample( 7 ) } };
		Assert.IsTrue( ParkResumeSmokeTest.VerifyEvidencePair( Evidence( "capture" ), unknown ).Any( problem => problem.StartsWith( "both phases recorded continuation ticks" ) ),
			"a restore sample of a tick the capture has not, or a restore ending elsewhere, is named" );
	}

	[TestMethod]
	public void ACounterDriftAtTheBoundaryIsNamed()
	{
		var restore = Evidence( "restore" );
		var boundary = restore.Boundary with { Counters = restore.Boundary.Counters with { Riders = 99 } };
		var problems = ParkResumeSmokeTest.VerifyEvidencePair( Evidence( "capture" ), restore with { Boundary = boundary } );
		Assert.IsTrue( problems.Any( problem => problem == "boundary: counter Riders agrees (2 vs 99)" ),
			$"the boundary counter drift must be named exactly, got: {string.Join( "; ", problems )}" );
	}

	[TestMethod]
	public void ASectionMismatchAndShortContinuationAreNamed()
	{
		var restore = Evidence( "restore" );
		var boundary = restore.Boundary with { Sections = new Dictionary<string, string> { ["guests"] = Hash, ["scripts"] = Hash[..63] + "0", ["runtimes"] = Hash } };
		var problems = ParkResumeSmokeTest.VerifyEvidencePair( Evidence( "capture" ), restore with { Boundary = boundary } );
		Assert.IsTrue( problems.Any( problem => problem.StartsWith( "boundary: section scripts" ) ), "the drifted section is named" );
		problems = ParkResumeSmokeTest.VerifyEvidencePair( Evidence( "capture" ), Evidence( "restore" ) with { Ticks = Evidence( "restore" ).Ticks.Take( 2 ).ToArray() } );
		Assert.IsTrue( problems.Any( problem => problem.StartsWith( "both phases recorded continuation ticks (3 captured, 2 restored, ending at 3/2)" ) ), "a short restore continuation is named" );
	}

	[TestMethod]
	public void AnAcceptedCorruptCandidateOrMissingObservationsFailThePair()
	{
		var accepted = Evidence( "restore" ) with
		{
			Probes = new[] { new ParkResumeSmokeTest.SmokeProbe { Name = "late-reference", Rejected = false, Message = "", LiveParkIntact = true } },
		};
		var problems = ParkResumeSmokeTest.VerifyEvidencePair( Evidence( "capture" ), accepted );
		Assert.IsTrue( problems.Any( problem => problem.Contains( "corrupt-save candidate was rejected" ) ), "an accepted corrupt save fails the pair" );
		var disturbing = Evidence( "restore" ) with
		{
			Probes = new[] { new ParkResumeSmokeTest.SmokeProbe { Name = "fingerprint", Rejected = true, Message = "refused", LiveParkIntact = false } },
		};
		problems = ParkResumeSmokeTest.VerifyEvidencePair( Evidence( "capture" ), disturbing );
		Assert.IsTrue( problems.Any( problem => problem.Contains( "live park untouched" ) ), "a disturbed live park fails the pair" );
		problems = ParkResumeSmokeTest.VerifyEvidencePair( Evidence( "capture" ), Evidence( "restore" ) with { PostLoad = null } );
		Assert.IsTrue( problems.Any( problem => problem.Contains( "no post-load gameplay observations" ) ), "missing post-load observations fail the pair" );
		var partial = Evidence( "restore" );
		partial = partial with { PostLoad = partial.PostLoad! with { StaffHiredAndPaidOnce = false } };
		problems = ParkResumeSmokeTest.VerifyEvidencePair( Evidence( "capture" ), partial );
		Assert.IsTrue( problems.Any( problem => problem.Contains( "paid exactly once" ) ), "a failed post-load check is named" );
	}

	[TestMethod]
	public void EvidenceStaysHashesAndCountersOnly()
	{
		var problems = ParkResumeSmokeTest.VerifyContainment( Evidence( "capture" ) );
		Assert.AreEqual( "", string.Join( "; ", problems ), "a well-formed evidence file is self-contained" );
		var misformed = Evidence( "capture" ) with { Save = new ParkResumeSmokeTest.SmokeSaveFile { Name = "x", ByteCount = 1, Sha256 = "not-a-hash" } };
		Assert.IsTrue( ParkResumeSmokeTest.VerifyContainment( misformed ).Any( problem => problem.Contains( "save file hash" ) ),
			"a malformed hash is refused" );
		var byteCarrier = Evidence( "capture" ) with { Theme = new string( 'a', ParkResumeSmokeTest.MaximumEvidenceStringLength + 1 ) };
		Assert.IsTrue( ParkResumeSmokeTest.VerifyContainment( byteCarrier ).Any( problem => problem.Contains( "above" ) && problem.Contains( "characters" ) ),
			"a string long enough to carry asset bytes is refused" );
	}

	[TestMethod]
	public void EvidenceRoundTripsThroughJsonStably()
	{
		var first = Path.Combine( Path.GetTempPath(), $"opentpw-smoke-evidence-{Guid.NewGuid():N}.json" );
		var second = first + ".reread";
		try
		{
			var capture = Evidence( "capture" );
			ParkResumeSmokeTest.WriteEvidence( first, capture );
			var read = ParkResumeSmokeTest.ReadEvidence( first );
			Assert.AreEqual( capture.Phase, read.Phase );
			Assert.AreEqual( capture.Seed, read.Seed );
			Assert.AreEqual( capture.Boundary.Tick, read.Boundary.Tick );
			Assert.AreEqual( capture.Ticks.Count, read.Ticks.Count );
			Assert.AreEqual( capture.Save.Sha256, read.Save.Sha256 );
			Assert.IsTrue( read.Completed, "the scalar fields survive the round trip" );
			ParkResumeSmokeTest.WriteEvidence( second, read );
			CollectionAssert.AreEqual( File.ReadAllBytes( first ), File.ReadAllBytes( second ),
				"re-writing the read evidence is byte-stable (collections deserialize, so record equality alone would not show this)" );
		}
		finally { File.Delete( first ); File.Delete( second ); }
	}

	[TestMethod]
	public void TheManifestHashIgnoresKeyOrderButNotContent()
	{
		var first = new Dictionary<string, string> { ["a"] = Hash, ["b"] = Hash };
		var second = new Dictionary<string, string> { ["b"] = Hash, ["a"] = Hash };
		Assert.AreEqual( ParkResumeSmokeTest.CanonicalManifestSha256( first ), ParkResumeSmokeTest.CanonicalManifestSha256( second ),
			"dictionary enumeration order never changes the manifest identity" );
		second["a"] = Hash[..63] + "0";
		Assert.AreNotEqual( ParkResumeSmokeTest.CanonicalManifestSha256( first ), ParkResumeSmokeTest.CanonicalManifestSha256( second ),
			"one changed asset digest changes the manifest identity" );
	}
}
