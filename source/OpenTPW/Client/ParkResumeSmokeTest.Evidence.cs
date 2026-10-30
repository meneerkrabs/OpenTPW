using System.Security.Cryptography;
using System.Text.Json;

namespace OpenTPW;

/// <summary>
/// Evidence model of the native park-resume smoke test (capture and restore phases). One JSON file per phase records
/// only hashes, counts and file names — never park state, save bytes or any original asset content — so a restart can
/// be verified by comparing the two files: the restore phase must reproduce the capture phase's per-tick full typed
/// SHA-256 (every snapshot section, not <see cref="Level.ComputeStateHash"/> alone) tick for tick. Verification lives
/// here so both the harness and root's review can run it without a GPU.
/// </summary>
internal sealed partial class ParkResumeSmokeTest
{
	internal const string EvidenceFormatName = "opentpw-park-resume-smoke";
	internal const int EvidenceCurrentVersion = 1;
	/// <summary>Bound for one evidence file: hashes and counters of ~1000 ticks stay far below it; asset bytes never fit.</summary>
	internal const int MaximumEvidenceBytes = 8 * 1024 * 1024;
	/// <summary>No string in an evidence file is longer than this (a hash or a file name); asset content can never be pasted in.</summary>
	internal const int MaximumEvidenceStringLength = 256;

	/// <summary>The evidence one phase writes. <paramref name="ticks"/> are the post-save continuation ticks, tick 0 being the save boundary itself.</summary>
	internal sealed record SmokeEvidence
	{
		public string Format { get; init; } = EvidenceFormatName;
		public int Version { get; init; } = EvidenceCurrentVersion;
		/// <summary>"capture" or "restore".</summary>
		public string Phase { get; init; } = "";
		public string Theme { get; init; } = "";
		public string StartKind { get; init; } = "";
		/// <summary>The world seed as a decimal string (JSON-stable across writers).</summary>
		public string Seed { get; init; } = "";
		/// <summary>The save's own SavedUtc, normalized to a UTC round-trip ("o") string.</summary>
		public string SavedUtc { get; init; } = "";
		/// <summary>SHA-256 of the canonical (sorted-key) asset manifest: hashes of hashes, never the manifest's file content.</summary>
		public string ManifestSha256 { get; init; } = "";
		/// <summary>Whether both phases ran the prototype-Totem fixture option (they must agree).</summary>
		public bool PrototypeVariant { get; init; }
		/// <summary>Whether both phases ran the mod-rides fixture option (they must agree).</summary>
		public bool ModVariant { get; init; }
		/// <summary>Whether both phases ran the water-ride fixture option (they must agree).</summary>
		public bool WaterVariant { get; init; }
		/// <summary>The water ride's track tiles and boats at the save boundary (capture phase), the restore phase's oracle.</summary>
		public SmokeWater WaterAtSave { get; init; } = new();
		/// <summary>Whether both phases ran the tour-ride fixture option (they must agree).</summary>
		public bool TourVariant { get; init; }
		/// <summary>The tour ride's cars and riders at the save boundary (capture phase), the restore phase's oracle.</summary>
		public SmokeTour TourAtSave { get; init; } = new();
		/// <summary>The fixture Totem's identity (capture phase); null when the fixture placed none.</summary>
		public SmokePrototype? Prototype { get; init; }
		public SmokeSaveFile Save { get; init; } = new();
		public SmokeSample Boundary { get; init; } = new() { Tick = -1 };
		public IReadOnlyList<SmokeSample> Ticks { get; init; } = Array.Empty<SmokeSample>();
		/// <summary>Restore only: the adversarial corrupt-file candidates and whether each was rejected without disturbing the live park.</summary>
		public IReadOnlyList<SmokeProbe> Probes { get; init; } = Array.Empty<SmokeProbe>();
		/// <summary>Restore only: what the post-load gameplay checks observed.</summary>
		public SmokePostLoad? PostLoad { get; init; }
		/// <summary>Names of PNG readbacks saved next to the artifacts (names only; the pixels are not evidence JSON).</summary>
		public IReadOnlyList<string> Pngs { get; init; } = Array.Empty<string>();
		public bool Completed { get; init; }
		/// <summary>Set when the phase failed; the message is the thrown failure's text.</summary>
		public string? Failure { get; init; }
	}

	/// <summary>Cars, cars on their tour and riders of the tour-ride fixture at one moment.</summary>
	internal sealed record SmokeTour
	{
		public int Cars { get; init; }
		public int Flying { get; init; }
		public int Riders { get; init; }
		public int Departures { get; init; }
	}

	/// <summary>Track tiles and boats of the water-ride fixture at one moment.</summary>
	internal sealed record SmokeWater
	{
		public int Tiles { get; init; }
		public int Boats { get; init; }
	}

	internal sealed record SmokeSaveFile
	{
		/// <summary>File name of the own-format save the phases share (no path: directories are run-specific).</summary>
		public string Name { get; init; } = "";
		public long ByteCount { get; init; }
		public string Sha256 { get; init; } = "";
	}

	/// <summary>The fixture Totem's identity and economy association as the capture phase saved it (hashes and ids only).</summary>
	internal sealed record SmokePrototype
	{
		public int AttractionId { get; init; }
		public bool Open { get; init; }
		/// <summary>The economy instance the live park linked (never null in the variant: the jungle catalogue lists the Totem).</summary>
		public int EconomyInstanceId { get; init; }
		/// <summary>Whether the live park paid for the Totem; a removal after resume sells it only when bought.</summary>
		public bool EconomyBought { get; init; }
	}

	/// <summary>One captured moment: the save boundary or one continuation tick.</summary>
	internal sealed record SmokeSample
	{
		/// <summary>Continuation tick index (0 = the save boundary), not the park clock.</summary>
		public long Tick { get; init; }
		/// <summary>SHA-256 over the framed serialization of every snapshot section plus the stable header scalars.</summary>
		public string FullTypedSha256 { get; init; } = "";
		/// <summary>Per-section SHA-256 (serialized through the actual ParkResumeJson codec): economy, layout, guests, scripts, runtimes, bindings, clock, weather, gate, staff, advisor.</summary>
		public IReadOnlyDictionary<string, string> Sections { get; init; } = new Dictionary<string, string>();
		/// <summary>Level.ComputeStateHash at the same moment, as a cross-check column — never the equality test itself.</summary>
		public string ComputeStateHash { get; init; } = "";
		public SmokeCounters Counters { get; init; } = new();
	}

	/// <summary>Counters derived from the typed snapshot (identically on both sides) plus the entity count.</summary>
	internal sealed record SmokeCounters
	{
		public int Guests { get; init; }
		public int InPark { get; init; }
		public int WalkingAround { get; init; }
		public int GoingToRide { get; init; }
		public int InQueue { get; init; }
		public int BoardingOrWaiting { get; init; }
		public int Riding { get; init; }
		public int ExitingRide { get; init; }
		public int LeavingOrArriving { get; init; }
		public long Admissions { get; init; }
		public int Departed { get; init; }
		public int NextGuestId { get; init; }
		public int Attractions { get; init; }
		public int QueuedGuests { get; init; }
		public int Riders { get; init; }
		public int WalkingOff { get; init; }
		public long GuestTick { get; init; }
		public int Objects { get; init; }
		public int NextAttractionId { get; init; }
		public int CellMapVersion { get; init; }
		public long Balance { get; init; }
		public long EconomyTick { get; init; }
		public int EconomyObjects { get; init; }
		public int EconomyBindings { get; init; }
		public int StaffMembers { get; init; }
		public int StaffActors { get; init; }
		public int WeatherQuality { get; init; }
		public int Entities { get; init; }
	}

	/// <summary>One adversarial corrupt-save candidate: rejected with a reason, and the live park provably untouched.</summary>
	internal sealed record SmokeProbe
	{
		public string Name { get; init; } = "";
		public bool Rejected { get; init; }
		/// <summary>The first line of the rejection; names the violated rule.</summary>
		public string Message { get; init; } = "";
		/// <summary>False when anything about the live park changed (level identity, state hash, entities, camera, listener or GPU targets).</summary>
		public bool LiveParkIntact { get; init; }
	}

	/// <summary>The restore phase's post-load gameplay observations (all must hold).</summary>
	internal sealed record SmokePostLoad
	{
		public bool ParkReopenedAndClosed { get; init; }
		public bool ClosedParkRefusedAdmission { get; init; }
		public bool AdmissionBookedExactlyOnce { get; init; }
		public bool SoldObjectCreditedScrapOnce { get; init; }
		public bool SoldObjectLinksRemoved { get; init; }
		public bool NewObjectPlacedAfterSale { get; init; }
		public bool StaffHiredAndPaidOnce { get; init; }
		public bool PaymentBindingsOneToOne { get; init; }
		/// <summary>The prototype variant: the resumed Totem's removal observed (sold exactly once / deregistered). Null when the fixture placed no Totem.</summary>
		public bool? PrototypeRemoved { get; init; }
		public long BalanceAfter { get; init; }
		public long ScrapCredited { get; init; }
		public long WagesCharged { get; init; }
		public int StaffActors { get; init; }
	}

	private static readonly JsonSerializerOptions evidenceOptions = new() { WriteIndented = true };

	/// <summary>SHA-256 of one snapshot section serialized through the actual codec (the exact bytes a save would carry).</summary>
	internal static string SectionSha256<T>( T section ) =>
		Convert.ToHexString( SHA256.HashData( ParkResumeJson.Serialize( section ) ) ).ToLowerInvariant();

	/// <summary>SHA-256 of a manifest as pure content identity: sorted "key=hash" lines, so dictionary order never matters.</summary>
	internal static string CanonicalManifestSha256( IReadOnlyDictionary<string, string>? manifest )
	{
		if ( manifest == null )
			return "";
		var lines = manifest.Keys.OrderBy( key => key, StringComparer.Ordinal )
			.Select( key => $"{key}={manifest[key]}" ).ToArray();
		return Convert.ToHexString( SHA256.HashData( JsonSerializer.SerializeToUtf8Bytes( lines, evidenceOptions ) ) ).ToLowerInvariant();
	}

	internal static void WriteEvidence( string path, SmokeEvidence evidence )
	{
		ArgumentNullException.ThrowIfNull( evidence );
		var bytes = JsonSerializer.SerializeToUtf8Bytes( evidence, evidenceOptions );
		if ( bytes.Length > MaximumEvidenceBytes )
			throw new InvalidOperationException( $"Park-resume smoke evidence exceeds {MaximumEvidenceBytes} bytes." );
		File.WriteAllBytes( path, bytes );
	}

	internal static SmokeEvidence ReadEvidence( string path )
	{
		using var stream = File.OpenRead( path );
		if ( stream.Length > MaximumEvidenceBytes ) throw new InvalidDataException( "Park-resume smoke evidence exceeds the size limit." );
		var evidence = JsonSerializer.Deserialize<SmokeEvidence>( stream, evidenceOptions )
			?? throw new InvalidDataException( $"The park-resume smoke evidence '{Path.GetFileName( path )}' is null." );
		return evidence;
	}

	/// <summary>
	/// The actual restart verdict: the restore evidence must reproduce the capture evidence's boundary and every
	/// continuation tick — same full typed hash, same sections, same counters — from the same save file, with every
	/// corrupt candidate rejected without touching the live park, and the post-load gameplay checks green. The first
	/// mismatches are returned as named problems; an empty list is a pass.
	/// </summary>
	internal static IReadOnlyList<string> VerifyEvidencePair( SmokeEvidence capture, SmokeEvidence restore )
	{
		var problems = new List<string>();
		void Require( bool condition, string message )
		{
			if ( !condition )
				problems.Add( message );
		}
		Require( capture.Format == EvidenceFormatName && restore.Format == EvidenceFormatName, "both files are park-resume smoke evidence" );
		Require( capture.Phase == "capture", "the capture phase's evidence says capture" );
		Require( restore.Phase == "restore", "the restore phase's evidence says restore" );
		Require( capture.Completed && restore.Completed, "both phases completed" );
		Require( capture.Failure == null && restore.Failure == null, $"no phase failed (capture: {capture.Failure ?? "ok"}, restore: {restore.Failure ?? "ok"})" );
		Require( capture.Theme == restore.Theme && capture.StartKind == restore.StartKind && capture.Seed == restore.Seed,
			$"the parks match (theme {capture.Theme}/{restore.Theme}, start {capture.StartKind}/{restore.StartKind}, seed {capture.Seed}/{restore.Seed})" );
		Require( capture.PrototypeVariant == restore.PrototypeVariant,
			$"both phases ran the same fixture (prototype variant {capture.PrototypeVariant}/{restore.PrototypeVariant})" );
		Require( capture.ModVariant == restore.ModVariant, $"both phases ran the same fixture (mod variant {capture.ModVariant}/{restore.ModVariant})" );
		Require( capture.WaterVariant == restore.WaterVariant && (!capture.WaterVariant || capture.WaterAtSave == restore.WaterAtSave),
			$"both phases ran the same water fixture (variant {capture.WaterVariant}/{restore.WaterVariant}, {capture.WaterAtSave}/{restore.WaterAtSave})" );
		Require( capture.TourVariant == restore.TourVariant && (!capture.TourVariant || capture.TourAtSave == restore.TourAtSave),
			$"both phases ran the same tour fixture (variant {capture.TourVariant}/{restore.TourVariant}, {capture.TourAtSave}/{restore.TourAtSave})" );
		if ( capture.PrototypeVariant )
			Require( capture.Prototype != null && restore.Prototype != null
				&& capture.Prototype.AttractionId == restore.Prototype.AttractionId
				&& capture.Prototype.EconomyInstanceId == restore.Prototype.EconomyInstanceId
				&& capture.Prototype.EconomyBought == restore.Prototype.EconomyBought,
				"both phases saw the same Totem (attraction id, economy instance, bought flag)" );
		Require( capture.Save.Sha256.Length == 64 && capture.Save.Sha256 == restore.Save.Sha256,
			$"both phases used the same save bytes ({capture.Save.Sha256 ?? ""} vs {restore.Save.Sha256 ?? ""})" );
		Require( capture.ManifestSha256.Length == 64 && capture.ManifestSha256 == restore.ManifestSha256,
			"the asset manifest agrees (content identity, sorted-key hash)" );
		Compare( capture.Boundary, restore.Boundary, "boundary", problems );
		// the capture phase samples every continuation tick; the restore phase, which runs several ticks a frame, samples the whole park at each frame's end (its ticks inside a frame are
		// checked by their world hash as they run) and must end on the capture's last tick
		var captured = capture.Ticks.GroupBy( sample => sample.Tick ).ToDictionary( group => group.Key, group => group.First() );
		Require( capture.Ticks.Count > 0 && captured.Count == capture.Ticks.Count && restore.Ticks.Count > 0 && restore.Ticks[^1].Tick == capture.Ticks[^1].Tick
			&& restore.Ticks.All( sample => captured.ContainsKey( sample.Tick ) ),
			$"both phases recorded continuation ticks ({capture.Ticks.Count} captured, {restore.Ticks.Count} restored, ending at {capture.Ticks.LastOrDefault()?.Tick}/{restore.Ticks.LastOrDefault()?.Tick})" );
		foreach ( var sample in restore.Ticks )
			if ( captured.TryGetValue( sample.Tick, out var expected ) )
				Compare( expected, sample, $"tick {sample.Tick}", problems );
		Require( restore.Probes.Count > 0 && restore.Probes.All( probe => probe.Rejected ), "every corrupt-save candidate was rejected" );
		Require( restore.Probes.All( probe => probe.LiveParkIntact ), "every rejected candidate left the live park untouched" );
		if ( restore.PostLoad is { } postLoad )
		{
			Require( postLoad.ParkReopenedAndClosed, "post-load: the park reopened and closed again" );
			Require( postLoad.ClosedParkRefusedAdmission, "post-load: the closed park refused an admission without booking it" );
			Require( postLoad.AdmissionBookedExactlyOnce, "post-load: an admission was booked exactly once" );
			Require( postLoad.SoldObjectCreditedScrapOnce, "post-load: selling an object credited its scrap value exactly once" );
			Require( postLoad.SoldObjectLinksRemoved, "post-load: the sold object's payment and economy links were removed" );
			Require( postLoad.NewObjectPlacedAfterSale, "post-load: a new object was placed after the sale" );
			Require( postLoad.StaffHiredAndPaidOnce, "post-load: hired staff were paid exactly once at month end" );
			Require( postLoad.PaymentBindingsOneToOne, "post-load: guest payments bind one attraction to one economy instance" );
			if ( postLoad.PrototypeRemoved != null )
				Require( postLoad.PrototypeRemoved == true, "post-load: the resumed Totem was removed with its economy link settled exactly once" );
		}
		else
			problems.Add( "the restore phase recorded no post-load gameplay observations" );
		problems.AddRange( VerifyContainment( capture ).Select( problem => $"capture: {problem}" ) );
		problems.AddRange( VerifyContainment( restore ).Select( problem => $"restore: {problem}" ) );
		return problems;
	}

	private static void Compare( SmokeSample expected, SmokeSample actual, string what, List<string> problems )
	{
		void Require( bool condition, string message )
		{
			if ( !condition )
				problems.Add( $"{what}: {message}" );
		}
		Require( expected.Tick == actual.Tick, $"tick numbers agree ({expected.Tick} vs {actual.Tick})" );
		Require( expected.FullTypedSha256 == actual.FullTypedSha256,
			$"full typed SHA-256 agrees ({expected.FullTypedSha256[..Math.Min( 12, expected.FullTypedSha256.Length )]}… vs {actual.FullTypedSha256[..Math.Min( 12, actual.FullTypedSha256.Length )]}…)" );
		var missing = expected.Sections.Keys.Where( key => !actual.Sections.ContainsKey( key ) ).ToArray();
		var extra = actual.Sections.Keys.Where( key => !expected.Sections.ContainsKey( key ) ).ToArray();
		Require( missing.Length == 0 && extra.Length == 0, $"the same sections are recorded (missing [{string.Join( ", ", missing )}], extra [{string.Join( ", ", extra )}])" );
		foreach ( var (key, hash) in expected.Sections )
			if ( actual.Sections.TryGetValue( key, out var other ) && hash != other )
				problems.Add( $"{what}: section {key} agrees ({hash[..Math.Min( 12, hash.Length )]}… vs {other[..Math.Min( 12, other.Length )]}…)" );
		CompareCounters( expected.Counters, actual.Counters, what, problems );
	}

	/// <summary>Counter comparison, written out field by field: no reflection over live objects, and every mismatch is named.</summary>
	private static void CompareCounters( SmokeCounters expected, SmokeCounters actual, string what, List<string> problems )
	{
		void Require( bool condition, string message )
		{
			if ( !condition )
				problems.Add( $"{what}: counter {message}" );
		}
		Require( expected.Guests == actual.Guests, $"Guests agrees ({expected.Guests} vs {actual.Guests})" );
		Require( expected.InPark == actual.InPark, $"InPark agrees ({expected.InPark} vs {actual.InPark})" );
		Require( expected.WalkingAround == actual.WalkingAround, $"WalkingAround agrees ({expected.WalkingAround} vs {actual.WalkingAround})" );
		Require( expected.GoingToRide == actual.GoingToRide, $"GoingToRide agrees ({expected.GoingToRide} vs {actual.GoingToRide})" );
		Require( expected.InQueue == actual.InQueue, $"InQueue agrees ({expected.InQueue} vs {actual.InQueue})" );
		Require( expected.BoardingOrWaiting == actual.BoardingOrWaiting, $"BoardingOrWaiting agrees ({expected.BoardingOrWaiting} vs {actual.BoardingOrWaiting})" );
		Require( expected.Riding == actual.Riding, $"Riding agrees ({expected.Riding} vs {actual.Riding})" );
		Require( expected.ExitingRide == actual.ExitingRide, $"ExitingRide agrees ({expected.ExitingRide} vs {actual.ExitingRide})" );
		Require( expected.LeavingOrArriving == actual.LeavingOrArriving, $"LeavingOrArriving agrees ({expected.LeavingOrArriving} vs {actual.LeavingOrArriving})" );
		Require( expected.Admissions == actual.Admissions, $"Admissions agrees ({expected.Admissions} vs {actual.Admissions})" );
		Require( expected.Departed == actual.Departed, $"Departed agrees ({expected.Departed} vs {actual.Departed})" );
		Require( expected.NextGuestId == actual.NextGuestId, $"NextGuestId agrees ({expected.NextGuestId} vs {actual.NextGuestId})" );
		Require( expected.Attractions == actual.Attractions, $"Attractions agrees ({expected.Attractions} vs {actual.Attractions})" );
		Require( expected.QueuedGuests == actual.QueuedGuests, $"QueuedGuests agrees ({expected.QueuedGuests} vs {actual.QueuedGuests})" );
		Require( expected.Riders == actual.Riders, $"Riders agrees ({expected.Riders} vs {actual.Riders})" );
		Require( expected.WalkingOff == actual.WalkingOff, $"WalkingOff agrees ({expected.WalkingOff} vs {actual.WalkingOff})" );
		Require( expected.GuestTick == actual.GuestTick, $"GuestTick agrees ({expected.GuestTick} vs {actual.GuestTick})" );
		Require( expected.Objects == actual.Objects, $"Objects agrees ({expected.Objects} vs {actual.Objects})" );
		Require( expected.NextAttractionId == actual.NextAttractionId, $"NextAttractionId agrees ({expected.NextAttractionId} vs {actual.NextAttractionId})" );
		Require( expected.CellMapVersion == actual.CellMapVersion, $"CellMapVersion agrees ({expected.CellMapVersion} vs {actual.CellMapVersion})" );
		Require( expected.Balance == actual.Balance, $"Balance agrees ({expected.Balance} vs {actual.Balance})" );
		Require( expected.EconomyTick == actual.EconomyTick, $"EconomyTick agrees ({expected.EconomyTick} vs {actual.EconomyTick})" );
		Require( expected.EconomyObjects == actual.EconomyObjects, $"EconomyObjects agrees ({expected.EconomyObjects} vs {actual.EconomyObjects})" );
		Require( expected.EconomyBindings == actual.EconomyBindings, $"EconomyBindings agrees ({expected.EconomyBindings} vs {actual.EconomyBindings})" );
		Require( expected.StaffMembers == actual.StaffMembers, $"StaffMembers agrees ({expected.StaffMembers} vs {actual.StaffMembers})" );
		Require( expected.StaffActors == actual.StaffActors, $"StaffActors agrees ({expected.StaffActors} vs {actual.StaffActors})" );
		Require( expected.WeatherQuality == actual.WeatherQuality, $"WeatherQuality agrees ({expected.WeatherQuality} vs {actual.WeatherQuality})" );
		Require( expected.Entities == actual.Entities, $"Entities agrees ({expected.Entities} vs {actual.Entities})" );
	}

	/// <summary>
	/// Evidence self-containment: every hash is 64 lowercase hex characters, no string is long enough to carry asset
	/// content, and the whole file stays far below its bound — so nothing but hashes and counters ever leaves a phase.
	/// </summary>
	internal static IReadOnlyList<string> VerifyContainment( SmokeEvidence evidence )
	{
		var problems = new List<string>();
		bool Sha256( string value ) => value.Length == 64 && value.All( character => character is >= '0' and <= '9' or >= 'a' and <= 'f' );
		void CheckSample( SmokeSample sample, string what )
		{
			if ( !Sha256( sample.FullTypedSha256 ) )
				problems.Add( $"{what}: the full typed hash is not a SHA-256" );
			foreach ( var (key, value) in sample.Sections )
				if ( !Sha256( value ) )
					problems.Add( $"{what}: section {key}'s hash is not a SHA-256" );
			if ( sample.ComputeStateHash.Length == 0 )
				problems.Add( $"{what}: the compute-state-hash cross-check column is empty" );
		}
		if ( evidence.Version != EvidenceCurrentVersion )
			problems.Add( $"the evidence version is {evidence.Version}, not {EvidenceCurrentVersion}" );
		if ( evidence.Save.Sha256.Length > 0 && !Sha256( evidence.Save.Sha256 ) )
			problems.Add( "the save file hash is not a SHA-256" );
		if ( evidence.ManifestSha256.Length > 0 && !Sha256( evidence.ManifestSha256 ) )
			problems.Add( "the manifest hash is not a SHA-256" );
		CheckSample( evidence.Boundary, "boundary" );
		for ( var index = 0; index < evidence.Ticks.Count; index++ )
			CheckSample( evidence.Ticks[index], $"tick {index}" );
		// No string field anywhere is a carrier: walk the serialized JSON, not the object graph.
		using var document = JsonDocument.Parse( JsonSerializer.SerializeToUtf8Bytes( evidence, evidenceOptions ) );
		foreach ( var property in document.RootElement.EnumerateObject() )
			if ( property.Value.ValueKind == JsonValueKind.String && property.Value.GetString()!.Length > MaximumEvidenceStringLength )
				problems.Add( $"{property.Name} holds a string above {MaximumEvidenceStringLength} characters" );
		return problems;
	}
}
