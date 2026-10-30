namespace OpenTPW;

/// <summary>The two sign textures of one object type and name: RGBA halves, plus the GPU textures once requested.</summary>
public sealed class ObjectSignImages
{
	internal ObjectSignImages( string line1, string line2, byte[] left, byte[] right, int textPixels, IReadOnlyList<string> diagnostics )
	{
		Line1 = line1;
		Line2 = line2;
		Left = left;
		Right = right;
		TextPixelCount = textPixels;
		Diagnostics = diagnostics;
	}

	public string Line1 { get; private set; }
	public string Line2 { get; private set; }
	/// <summary>RGBA of the <c>sign1</c> texture (<see cref="SignSurfaceComposer.HalfWidth"/> x <see cref="SignSurfaceComposer.Height"/>).</summary>
	public byte[] Left { get; private set; }
	/// <summary>RGBA of the <c>sign2</c> texture.</summary>
	public byte[] Right { get; private set; }
	/// <summary>Nonzero text-mask pixels, independent of the board image.</summary>
	public int TextPixelCount { get; private set; }
	public IReadOnlyList<string> Diagnostics { get; private set; }

	private Texture? leftTexture;
	private Texture? rightTexture;

	/// <summary>
	/// New text for images owned by one entity (the park gate's sign after a rename): the halves are replaced and GPU textures
	/// made so far take the new pixels in place, so materials that bind them follow.
	/// </summary>
	internal void Replace( string line1, string line2, byte[] left, byte[] right, int textPixels, IReadOnlyList<string> diagnostics )
	{
		if ( left.Length != Left.Length || right.Length != Right.Length )
			throw new ArgumentException( "Sign halves must keep their size." );
		Line1 = line1;
		Line2 = line2;
		Left = left;
		Right = right;
		TextPixelCount = textPixels;
		Diagnostics = diagnostics;
		leftTexture?.Replace( left, SignSurfaceComposer.HalfWidth, SignSurfaceComposer.Height );
		rightTexture?.Replace( right, SignSurfaceComposer.HalfWidth, SignSurfaceComposer.Height );
	}

	/// <summary>Releases the GPU textures made so far, for images owned by one entity (the park gate's own sign); type signs are shared and stay.</summary>
	internal void ReleaseTextures()
	{
		leftTexture?.ReleaseOwned();
		rightTexture?.ReleaseOwned();
		leftTexture = null;
		rightTexture = null;
	}

	/// <summary>The GPU textures made so far (for the tests that count what a park leaves behind).</summary>
	internal IEnumerable<Texture> CreatedTextures => new[] { leftTexture, rightTexture }.OfType<Texture>();

	/// <summary>GPU texture of the <c>sign1</c> half (created on first use, shared by every object of the type).</summary>
	public Texture LeftTexture => leftTexture ??= new Texture( Left, SignSurfaceComposer.HalfWidth, SignSurfaceComposer.Height ).AsComposedSign();
	/// <summary>GPU texture of the <c>sign2</c> half.</summary>
	public Texture RightTexture => rightTexture ??= new Texture( Right, SignSurfaceComposer.HalfWidth, SignSurfaceComposer.Height ).AsComposedSign();

	/// <summary>The texture for a material slot name (<c>sign1</c>/<c>sign2</c>, with or without extension), or null.</summary>
	public Texture? TextureFor( string textureName ) => ObjectSigns.SlotOf( textureName ) switch { 0 => LeftTexture, 1 => RightTexture, _ => null };
}

/// <summary>
/// The text on ride, shop and sideshow signs. Object models that carry the material slots <c>sign1</c>/<c>sign2</c>
/// get their textures from here instead of the magenta "SIGN1"/"SIGN2" placeholders: the object's name is split into
/// two lines and drawn with the fonts and effects of the object's <c>.sgn</c> (docs/COMPATIBILITY.md).
/// </summary>
public static class ObjectSigns
{
	private static readonly Dictionary<string, ObjectSignImages?> cache = new( StringComparer.Ordinal );

	/// <summary>The GPU textures of the type signs kept for the session (one pair per object type and name drawn; kept on purpose).</summary>
	internal static Texture[] SessionTextures()
	{
		lock ( cache )
			return cache.Values.OfType<ObjectSignImages>().SelectMany( images => images.CreatedTextures ).ToArray();
	}

	/// <summary>0 for <c>sign1</c>, 1 for <c>sign2</c> (extension and case ignored), else -1.</summary>
	public static int SlotOf( string textureName ) => Path.GetFileNameWithoutExtension( textureName.TrimEnd( '\0' ) ).ToLowerInvariant() switch { "sign1" => 0, "sign2" => 1, _ => -1 };

	/// <summary>True for a material name that the game draws at run time.</summary>
	public static bool IsSignSlot( string textureName ) => SlotOf( textureName ) >= 0;

	/// <summary>
	/// The two lines of an object's sign. The name is the object's info-record name (<see cref="ObjectCatalogEntry.SignName"/>:
	/// the OBJECT_NAMES entries joined by a space, as the build list shows it, or empty for an id outside the original's
	/// table), else its display name. Bonus objects whose language file gives <c>SIGNA</c>/<c>SIGNB</c> use those two lines.
	/// </summary>
	// [BIN:STP-PPC:0x1018E254 sign text builder] one name string is split at its middle space (SignCanvas.SplitAtMiddleSpace); a caller may instead pass the two lines (text slot 0 and 1) explicitly
	// [BIN:STP-PPC:0x1011A08C object name builder] the name string (info record +0x70C) is the first OBJECT_NAMES text, a space, then the second ("%ls%ls%ls"); an entry ending in '-' is joined to the next without space or dash
	// [BIN:STP-PPC:0x101598C4 object sign call] for objects built from the catalogue the sign text is that name string of the object's info record (no explicit text passed)
	// [BIN:TPW-EXE:0x004629D0 object builder] without explicit text the sign text is the info record's name at +0x410 (0x412F50), filled at 0x414126 by 0x412FB0 from the object text file's NAME or else 0x4146B0 over the table 0x7488D8, which gives an empty name for an id outside the table
	// [BIN:TPW-EXE:0x00467A80 sign generator] a non-null name is split at the space nearest its middle (searched outward, alternating sides); an empty name has no space, so line 1 is the empty name and line 2 empty, and the sign is still generated (0x5ECB40) with both glyph masks cleared by 0x5EAB70
	public static (string Line1, string Line2) Lines( ObjectCatalogEntry entry )
	{
		if ( entry.IsBonus && BonusNames.ReadSignLines( entry ) is { } explicitLines )
			return explicitLines;
		return SignCanvas.SplitAtMiddleSpace( (entry.SignName ?? entry.DisplayName).Trim() );
	}

	/// <summary>Failed reads of one sign (an I/O or access error) before it is given up for the session, as for coaster models.</summary>
	internal const int ReadAttempts = 3;
	private static readonly Dictionary<string, int> failedReads = new( StringComparer.Ordinal );

	/// <summary>
	/// Whether a result stays cached: a sign is, and so is a failure that repeats (unreadable data, a missing key or a bad argument in the sign data), or an archive that is there without a sign.
	/// A read that may succeed later (an I/O or access error) is tried again until it has failed <see cref="ReadAttempts"/> times; an archive with no files yet
	/// (the browser copies a theme's files when a park starts) is always tried again.
	/// </summary>
	internal static bool CanCache( Exception? failure, ObjectSignImages? images, bool archiveWithoutSign, int failedReadCount = 1 ) =>
		images != null || (failure == null ? archiveWithoutSign : failure is not (IOException or UnauthorizedAccessException) || failedReadCount >= ReadAttempts);

	/// <summary>
	/// The CPU-composed sign of an object (cached per archive and text), or null when the object has no <c>.sgn</c>. A sign
	/// with two empty lines is composed too (its board alone). Drawing problems (missing glyphs or fonts) are logged once, not thrown.
	/// A composition started by <see cref="ComposeAhead"/> is used when it is finished, waited for when it is running, and run here
	/// when it has not started, so the result and the log are the same whichever thread composed the pixels (issue #754).
	/// </summary>
	public static ObjectSignImages? Get( ObjectCatalogEntry entry )
	{
		// Two empty lines still compose: the original draws the board and the paint and effect layers of empty masks (0x467A80 only skips a null name).
		var (line1, line2) = Lines( entry );
		var key = Key( entry, line1, line2 );
		while ( true )
		{
			SignComposition composition;
			lock ( cache )
			{
				if ( cache.TryGetValue( key, out var cached ) )
					return cached;
				// one composition per key until its result is applied below, shared by every caller (the worker's included)
				if ( !ahead.TryGetValue( key, out composition! ) || !composition.Matches( entry.FileSystem ) )
					ahead[key] = composition = new SignComposition( entry, line1, line2 );
			}
			var outcome = composition.Outcome;
			lock ( cache )
			{
				if ( cache.TryGetValue( key, out var cached ) )
					return cached;
				// Another caller applied this composition without keeping it (a failure that may pass, or an exception) or replaced it: compose
				// again, as a caller that waited for the cache lock did before.
				if ( !ahead.TryGetValue( key, out var current ) || current != composition )
					continue;
				ahead.Remove( key );
				// What the file system's lookups or the data corrections see changed while it ran, or the worker's composition failed (a file
				// busy then may be readable now): compose again here, so a result and a failure are those of a composition made on use.
				if ( !composition.Matches( entry.FileSystem ) || outcome.ComposedAhead && (outcome.Failure != null || outcome.Unexpected != null) )
					continue;
				// what the composition logged, in its order, as if it had run now
				Logger.Replay( outcome.Messages );
				outcome.Unexpected?.Throw();
				var images = outcome.Images;
				var failure = outcome.Failure;
				var failures = 0;
				if ( failure != null )
				{
					failedReads.TryGetValue( key, out failures );
					failedReads[key] = ++failures;
				}
				// A missing result stays uncached unless the archive is there without a sign: a failure that may pass, or an archive whose files have not arrived yet (the browser copies a theme's files when a park starts), must not leave the placeholder for the rest of the session
				var final = CanCache( failure, images, outcome.ArchiveWithoutSign, failures );
				// logged once per sign (the park HUD asks for a sign on every preview draw), and once more when it is given up
				if ( failure != null && (failures == 1 || final) )
					Log?.Warning( $"Sign of {entry.ArchiveName} unavailable: {failure.Message}{(final ? "; it is not drawn" : "; it is tried again")}" );
				if ( final )
				{
					cache[key] = images;
					failedReads.Remove( key );
				}
				return images;
			}
		}
	}

	private static string Key( ObjectCatalogEntry entry, string line1, string line2 ) => $"{entry.FileSystem.GetAbsolutePath( entry.ArchivePath )}\0{line1}\0{line2}";

	/// <summary>Compositions started and not yet applied by <see cref="Get"/>, by the same key as <see cref="cache"/> (guarded by <see cref="cache"/>).</summary>
	private static readonly Dictionary<string, SignComposition> ahead = new( StringComparer.Ordinal );
	/// <summary>The compositions <see cref="ComposeAhead"/> queued for the worker, oldest first (guarded by <see cref="cache"/>).</summary>
	private static readonly Queue<SignComposition> aheadQueue = new();
	private static bool aheadWorkerRunning;
	[ThreadStatic] private static bool onAheadWorker;

	/// <summary>
	/// Whether <see cref="ComposeAhead"/> starts compositions on a worker: the desktop's game, not the browser, whose single thread would run the
	/// queued work on the frames anyway and whose theme files may not have arrived yet.
	/// </summary>
	internal static bool ComposeAheadEnabled { get; set; } = !OperatingSystem.IsBrowser();

	/// <summary>
	/// Starts composing the sign of <paramref name="entry"/> on a worker, so that its first placement does not compose it on the frame of the
	/// click (issue #754: 36 to 46 ms for a ride with a sign on the Mac). The worker composes one sign at a time, in the order asked; only the CPU
	/// part runs there (the <c>.sgn</c> read, the board's wavelet decode, the glyph rasters and the layers), the GPU textures are made on the render
	/// thread when a material first asks for them (<see cref="ObjectSignImages.LeftTexture"/>). What the composition logs is held and written by
	/// <see cref="Get"/> when it uses the result. A sign already composed, already asked for, or not composable (no entry) is left alone.
	/// </summary>
	public static void ComposeAhead( ObjectCatalogEntry entry )
	{
		if ( !ComposeAheadEnabled )
			return;
		string key;
		string line1, line2;
		try
		{
			// Get reads the lines again (and logs what that read logs) when it uses the composition
			using ( Logger.BeginCapture() )
				(line1, line2) = Lines( entry );
			key = Key( entry, line1, line2 );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException )
		{
			return;
		}
		lock ( cache )
		{
			if ( cache.ContainsKey( key ) || ahead.TryGetValue( key, out var known ) && known.Matches( entry.FileSystem ) )
				return;
			// a composition made before the file system changed (a mod set, the interface language) is never used: forget those that wait unused
			foreach ( var stale in ahead.Where( pair => !pair.Value.Matches( pair.Value.FileSystem ) ).Select( pair => pair.Key ).ToArray() )
				ahead.Remove( stale );
			var composition = new SignComposition( entry, line1, line2 );
			ahead[key] = composition;
			aheadQueue.Enqueue( composition );
			if ( aheadWorkerRunning )
				return;
			aheadWorkerRunning = true;
		}
		Task.Run( ComposeQueued );
	}

	/// <summary>
	/// <see cref="ComposeAhead"/> on the calling thread over steps, for a park built over frames where no worker composes (the page: <see cref="ComposeAheadEnabled"/> off): the sign's
	/// read, then the composition's parts (<see cref="SignTextRenderer.ComposeSignSteps"/>, the steps <see cref="SignSurfaceComposer.Compose"/> runs too), each under a log capture closed
	/// before it yields, so the frames between them log as ever. A finished sign is handed to <see cref="Get"/> as the worker's composition is (its log, in order, written when a first
	/// object uses it), and only when nothing else has meanwhile: no sign cached for its key, no other composition ahead that is still current, and the same file system with no
	/// invalidation and the same data corrections as at its start. A sign already composed or asked for, an archive without a sign, or a read or composition that fails hands over
	/// nothing: a first use composes it then, as it would have. Disposing the steps part way leaves nothing behind.
	/// </summary>
	internal static IEnumerable<bool> ComposeAheadSteps( ObjectCatalogEntry entry )
	{
		string line1, line2, key;
		try
		{
			// Get reads the lines again (and logs what that read logs) when it uses the composition, as for one the worker composed
			using ( Logger.BeginCapture() )
				(line1, line2) = Lines( entry );
			key = Key( entry, line1, line2 );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException )
		{
			yield break;
		}
		if ( Known( key, entry.FileSystem ) )
			yield break;
		var fileSystem = entry.FileSystem;
		var generation = fileSystem.LookupGeneration;
		var flags = CompatibilityRuntime.Flags;
		var messages = new List<(Logger.Level Severity, string Text, bool Quiet)>();
		// one part of the composition under a capture of its own, closed before the steps yield: false when it threw, which hands over nothing
		bool Run( Action part )
		{
			using var capture = Logger.BeginCapture();
			using var profile = LoadProfile.Measure( "sign-ahead" );
			try
			{
				part();
				return true;
			}
			catch
			{
				return false;
			}
			finally
			{
				messages.AddRange( capture.Messages );
			}
		}
		LoadProfile.Count( "signs composed ahead" );
		SignFile? sign = null;
		if ( !Run( () => sign = SignTextRenderer.LoadSignFile( entry.ArchivePath, entry.FileSystem ) ) || sign == null )
			yield break;
		yield return true;
		var diagnostics = new List<string>();
		var composition = new SignSurfaceComposer.Composition();
		IEnumerator<bool>? parts = null;
		try
		{
			if ( !Run( () => parts = SignTextRenderer.Shared.ComposeSignSteps( sign, new[] { line1, line2 }, diagnostics, composition ).GetEnumerator() ) )
				yield break;
			var more = false;
			while ( true )
			{
				if ( !Run( () => more = parts!.MoveNext() ) )
					yield break;
				if ( !more )
					break;
				yield return true;
			}
		}
		finally
		{
			parts?.Dispose();
		}
		ObjectSignImages? images = null;
		// what Compose logs after the composition: its drawing problems, once each
		if ( !Run( () =>
		{
			foreach ( var diagnostic in diagnostics.Where( diagnostic => !diagnostic.StartsWith( "COMPAT-003", StringComparison.Ordinal ) ).Distinct() )
				Log?.Warning( $"Sign of {entry.ArchiveName}: {diagnostic}" );
			var (left, right, textPixels) = composition.Result;
			images = new ObjectSignImages( line1, line2, left, right, textPixels, diagnostics );
		} ) )
			yield break;
		var finished = new SignComposition( entry, line1, line2, generation, flags, new SignOutcome( images, false, null, messages, null, ComposedAhead: true ) );
		lock ( cache )
		{
			// a sign cached meanwhile (the live game's own use between the steps), or another composition still current, stands; one of changed data is no one's
			if ( !cache.ContainsKey( key ) && !(ahead.TryGetValue( key, out var other ) && other.Matches( entry.FileSystem )) && finished.Matches( entry.FileSystem ) )
				ahead[key] = finished;
		}
	}

	/// <summary>Whether a sign of <paramref name="key"/> is cached, or composed or being composed ahead from what <paramref name="fileSystem"/> reads now.</summary>
	private static bool Known( string key, BaseFileSystem fileSystem )
	{
		lock ( cache )
			return cache.ContainsKey( key ) || ahead.TryGetValue( key, out var known ) && known.Matches( fileSystem );
	}

	private static void ComposeQueued()
	{
		onAheadWorker = true;
		try
		{
			while ( true )
			{
				SignComposition next;
				lock ( cache )
				{
					// the queue is empty: the worker ends in the same step, so a composition queued after it starts a new one
					if ( !aheadQueue.TryDequeue( out next! ) )
					{
						aheadWorkerRunning = false;
						Monitor.PulseAll( cache );
						return;
					}
				}
				next.Run();
			}
		}
		catch
		{
			// a composition catches what it may throw; whatever else ends the worker, the queue does not wait for a worker that is gone
			lock ( cache )
			{
				aheadWorkerRunning = false;
				Monitor.PulseAll( cache );
			}
			throw;
		}
		finally
		{
			onAheadWorker = false;
		}
	}

	/// <summary>
	/// Waits until the worker has run every composition queued so far (tests and measurements); returns false when they are not done within
	/// <paramref name="timeout"/>.
	/// </summary>
	internal static bool WaitForComposeAhead( TimeSpan timeout )
	{
		lock ( cache )
		{
			// the worker pulses when its queue is empty
			while ( aheadWorkerRunning )
				if ( !Monitor.Wait( cache, timeout ) )
					return !aheadWorkerRunning;
			return true;
		}
	}

	/// <summary>Whether a composition for <paramref name="entry"/> has been started ahead and has finished (tests).</summary>
	internal static bool ComposedAhead( ObjectCatalogEntry entry )
	{
		var (line1, line2) = Lines( entry );
		var key = Key( entry, line1, line2 );
		lock ( cache )
			return ahead.TryGetValue( key, out var composition ) && composition.IsComposed;
	}

	/// <summary>The composition ahead for <paramref name="entry"/>'s sign, or null (tests that tell one composition from another).</summary>
	internal static object? AheadComposition( ObjectCatalogEntry entry )
	{
		var (line1, line2) = Lines( entry );
		var key = Key( entry, line1, line2 );
		lock ( cache )
			return ahead.TryGetValue( key, out var composition ) ? composition : null;
	}

	/// <summary>Forgets every composed sign and every composition started ahead (tests that compare a fresh composition with another).</summary>
	internal static void ForgetForTests()
	{
		WaitForComposeAhead( TimeSpan.FromMinutes( 5 ) );
		lock ( cache )
		{
			cache.Clear();
			ahead.Clear();
			failedReads.Clear();
		}
	}

	/// <summary>
	/// The CPU part of one sign: the <c>.sgn</c> read and the composition, run once (on the worker or by the first caller of <see cref="Outcome"/>)
	/// with what it logs held for <see cref="Get"/>. Its inputs are fixed when it is made (the entry's archive and file system, the two lines);
	/// <see cref="Matches"/> refuses it once what the file system's lookups or the data corrections see has changed since.
	/// </summary>
	private sealed class SignComposition
	{
		private readonly ObjectCatalogEntry entry;
		private readonly string line1;
		private readonly string line2;
		private readonly int lookupGeneration;
		private readonly CompatibilityFlags flags;
		private readonly Lazy<SignOutcome> outcome;

		public SignComposition( ObjectCatalogEntry entry, string line1, string line2 )
		{
			this.entry = entry;
			this.line1 = line1;
			this.line2 = line2;
			FileSystem = entry.FileSystem;
			lookupGeneration = FileSystem.LookupGeneration;
			flags = CompatibilityRuntime.Flags;
			// ExecutionAndPublication: one composition whoever asks first; a second caller waits for it
			outcome = new Lazy<SignOutcome>( Compose, LazyThreadSafetyMode.ExecutionAndPublication );
		}

		/// <summary>A composition finished over steps (<see cref="ComposeAheadSteps"/>), with what the file system's lookups and the data corrections were when they began.</summary>
		public SignComposition( ObjectCatalogEntry entry, string line1, string line2, int lookupGeneration, CompatibilityFlags flags, SignOutcome composed )
		{
			this.entry = entry;
			this.line1 = line1;
			this.line2 = line2;
			FileSystem = entry.FileSystem;
			this.lookupGeneration = lookupGeneration;
			this.flags = flags;
			outcome = new Lazy<SignOutcome>( () => composed, LazyThreadSafetyMode.ExecutionAndPublication );
			_ = outcome.Value;
		}

		public BaseFileSystem FileSystem { get; }

		/// <summary>Whether this composition reads what a new one would: the same file system with no invalidation since, and the same data corrections.</summary>
		public bool Matches( BaseFileSystem fileSystem ) =>
			ReferenceEquals( fileSystem, FileSystem ) && FileSystem.LookupGeneration == lookupGeneration && ReferenceEquals( CompatibilityRuntime.Flags, flags );

		public bool IsComposed => outcome.IsValueCreated;

		/// <summary>The result: composed now when nobody started it, else the running composition's once it ends.</summary>
		public SignOutcome Outcome => outcome.Value;

		public void Run() => _ = outcome.Value;

		private SignOutcome Compose()
		{
			LoadProfile.Count( onAheadWorker ? "signs composed ahead" : "signs composed on use" );
			ObjectSignImages? images = null;
			var archiveWithoutSign = false;
			Exception? failure = null;
			System.Runtime.ExceptionServices.ExceptionDispatchInfo? unexpected = null;
			using var capture = Logger.BeginCapture();
			try
			{
				var sign = SignTextRenderer.LoadSignFile( entry.ArchivePath, entry.FileSystem );
				archiveWithoutSign = sign == null && entry.FileSystem.GetFiles( entry.ArchivePath ).Any();
				if ( sign != null )
				{
					var composed = SignTextRenderer.Shared.ComposeSign( sign, new[] { line1, line2 }, default );
					foreach ( var diagnostic in composed.Diagnostics.Where( diagnostic => !diagnostic.StartsWith( "COMPAT-003", StringComparison.Ordinal ) ).Distinct() )
						Log?.Warning( $"Sign of {entry.ArchiveName}: {diagnostic}" );
					images = new ObjectSignImages( line1, line2, composed.Left, composed.Right, composed.TextPixelCount, composed.Diagnostics );
				}
			}
			// KeyNotFoundException: a font or glyph table the sign names but the data lacks; ArgumentException: values the composer refuses. Both repeat, so they are cached like bad data.
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or ArgumentException or KeyNotFoundException or UnauthorizedAccessException )
			{
				failure = exception;
			}
			// anything else is thrown to the caller of Get, after what was logged before it, as when the composition ran inside Get
			catch ( Exception exception )
			{
				unexpected = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture( exception );
			}
			return new SignOutcome( images, archiveWithoutSign, failure, capture.Messages, unexpected, onAheadWorker );
		}
	}

	/// <param name="ComposedAhead">Whether the worker composed it (<see cref="ComposeAhead"/>); <see cref="Get"/> composes a failed one again.</param>
	private sealed record SignOutcome( ObjectSignImages? Images, bool ArchiveWithoutSign, Exception? Failure,
		IReadOnlyList<(Logger.Level Severity, string Text, bool Quiet)> Messages, System.Runtime.ExceptionServices.ExceptionDispatchInfo? Unexpected, bool ComposedAhead );
}

/// <summary>
/// Which texture an object's <c>sign1</c>/<c>sign2</c> material slots show: the park name for the gate, the object's own sign for any other object
/// that has one, and for a fixed item whose sign cannot be drawn (the gate above all) a plain brown board instead of the shipped magenta "SIGN1 SIGN2" placeholder.
/// </summary>
// [EXT:fixed-sign-fallback] a fixed item (the park gate above all) that has no runtime sign shows a plain brown board instead of the shipped placeholder; an OpenTPW rendering choice, the original always draws the sign
internal readonly record struct SignSlotPlan( ObjectSignImages? Images, bool UsesFallback )
{
	public static readonly SignSlotPlan None = new( null, false );

	/// <summary>The plain board of a sign slot that has no sign: one texture for every part, park and lobby, never released (<see cref="Texture.SignBoard"/>).</summary>
	public static Texture FallbackBoard => Texture.SignBoard;

	/// <param name="infoId">The object's <see cref="ObjectCatalogEntry.InfoId"/>.</param>
	/// <param name="entryArchivePath">The object's archive path (<see cref="ObjectCatalogEntry.ArchivePath"/>), used only when the level names no gate.</param>
	/// <param name="gateInfoId">The level's gate (<c>global.sam</c> <c>ParkName.GateObjectId</c>), or null when it names none.</param>
	/// <param name="gateImages">The park name's sign of the object's own level, or null when it failed.</param>
	/// <param name="typeSign">The object type's own sign, asked for every object but the gate.</param>
	public static SignSlotPlan For( bool isFixedItem, int infoId, string entryArchivePath, int? gateInfoId, ObjectSignImages? gateImages, Func<ObjectSignImages?> typeSign )
	{
		// the gate never draws its type sign: that would be its default lines, not the park's name
		if ( IsGate( isFixedItem, infoId, entryArchivePath, gateInfoId ) )
			return gateImages != null ? new( gateImages, false ) : new( null, true );
		// [BIN:TPW-EXE:0x004629D0 object builder] every model the builder makes, fixed items included, reaches the sign generator 0x467A80 once its mesh has loaded (0x461F10 returns 1 at 0x462C18; the 0x400 flag test at 0x462DCC and its fall-through both reach 0x462E94, and 0x468250 fails only without the fonts directory), with the info record's name when no lines are passed (0x412F50 at 0x462ECE): a fixed item with its own .sgn draws its own sign as any catalogue object does
		if ( typeSign() is { } own )
			return new( own, false );
		return isFixedItem ? new( null, true ) : None;
	}

	/// <summary>Whether an object is its level's gate, whose sign slots show the park's name (<see cref="For"/>) and never its type's own sign.</summary>
	public static bool IsGate( bool isFixedItem, int infoId, string entryArchivePath, int? gateInfoId ) =>
		// [DATA:levels/<level>/global.sam:ParkName.GateObjectId] the level names its gate, whether or not its sign could be drawn; without that line the fixed item named "gates" stands in
		gateInfoId is { } gate
			? infoId == gate
			: isFixedItem && string.Equals( Path.GetFileName( entryArchivePath.TrimEnd( '/' ) ), "gates", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// The plan of a front-end scene's model (the lobby islands and gates, the globe): its runtime sign once it is composed, else the plain board; a front-end model
	/// never shows the shipped placeholder.
	/// </summary>
	// [EXT:fixed-sign-fallback] the lobby's sign models show the board until (or unless) their sign is composed
	public static SignSlotPlan ForScene( ObjectSignImages? images ) => images != null ? new( images, false ) : new( null, true );

	/// <summary>Where one material slot's texture comes from under this plan.</summary>
	public SignSlotSource SourceOf( string textureName ) =>
		!ObjectSigns.IsSignSlot( textureName ) ? SignSlotSource.Shipped
		: Images != null ? SignSlotSource.RuntimeSign
		: UsesFallback ? SignSlotSource.FallbackBoard : SignSlotSource.Shipped;
}

internal enum SignSlotSource { Shipped, RuntimeSign, FallbackBoard }
