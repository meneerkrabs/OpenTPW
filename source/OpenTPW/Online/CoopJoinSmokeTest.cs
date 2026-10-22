using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>A cut the host phase took for the virtual member, as its Ready names it, with the staged state found at the cut.</summary>
internal sealed record CoopSmokeCut( uint Generation, ulong Assigned, long AssignedHostFrame, long Records, long Ticks, ulong Frame, ulong Digest, ulong WorldHash, string Sha256, long Bytes,
	long ReadyHostFrame, string File, string[] Staged );

/// <summary>The host phase's evidence: the join phase's oracle, with what each capture cost the host (docs/COOP.md, "Measurements").</summary>
internal sealed record CoopSmokeHostEvidence( bool Completed, string? Failure, string Theme, string StartKind, int Coasters, CoopSmokeCut[] Cuts, ulong LastFrame, long LastTick,
	long LastRecord, ulong FinalDigest, ulong FinalWorldHash, string FinalSaveSha256, long HostFrames, CoopCaptureTimings[]? CaptureTimings = null,
	CoopSmokeEnvironment? Environment = null );

/// <summary>
/// The join phase's evidence, with what each build of a capture cost the guest and the phase's update frames (docs/COOP.md, "Measurements"). <paramref name="Mode"/> is "join" (the
/// adversarial join) or "measure" (the untouched captures, joined and resynced once each).
/// </summary>
internal sealed record CoopSmokeJoinEvidence( bool Completed, string? Failure, int Builds, int FailedJoins, string[] Steps, ulong FinalDigest, ulong FinalWorldHash, string[] SaveDifferences,
	string Mode = "join", CoopJoinTimings[]? JoinTimings = null, CoopSmokeFrame[]? Frames = null, CoopSmokeEnvironment? Environment = null );

/// <summary>One update frame of the join phase: its number, the join step it ran in, and the milliseconds of its update (the smoke's driver, the online pump and the game's).</summary>
internal sealed record CoopSmokeFrame( int Frame, string Step, double Milliseconds );

/// <summary>Where a phase ran (docs/COOP.md, "Measurements"): the build's version and configuration, the runtime, the machine, and the settings that change its work.</summary>
internal sealed record CoopSmokeEnvironment( string Version, string Configuration, string Runtime, string Os, string Architecture, int Processors, bool Silent, bool Advisor )
{
	public static CoopSmokeEnvironment Of( GameFlow flow ) => new(
		typeof( CoopSmokeEnvironment ).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
#if DEBUG
		"Debug",
#else
		"Release",
#endif
		System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, System.Runtime.InteropServices.RuntimeInformation.OSDescription,
		System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
		Environment.GetEnvironmentVariable( "OPENTPW_SILENT" ) is "1" or "true", flow.AutomaticAdvisorEnabled );
}

/// <summary>
/// [EXT:online-coop-snapshot-join] The native join smoke (docs/COOP.md, "The game's join"): <c>--coop-join-smoke host|join --coop-join-evidence &lt;directory&gt;</c>, two processes with the game's
/// data, silent and offscreen. The host phase (<see cref="HostUpdate"/>) starts Jungle as Instant Action and builds what a join must carry: two coasters by commands, staff, a pool with a
/// lazy river, a bridge, a slide and swimmers, a water ride with a track add-on, an object put back under a negative balance and a tour ride's site. It hosts a session over a recording
/// transport (<see cref="CoopSmokeHostTransport"/>) and stages each cut: the first with the water track cut open beside its add-on, an employee in hand, a pending hire, a carried Move entry
/// and the coaster tool open; the second with the track mended, a paused park with edits since its last tick, a built coaster closed and a tour ride bought while paused. It takes two
/// captures for a virtual member through the host's stream (the real cut), runs park minutes with commands at the normal and the fastest speed, sells the add-on while the member follows,
/// and writes the frames, the captures and its evidence. The join phase (<see cref="JoinUpdate"/>) is a fresh process that starts in the front end and runs the real game loop: it joins
/// through the game's join host over a transport that plays the recorded host (<see cref="CoopSmokeReplayTransport"/>), whose first answer names a wrong digest (a failed check, its build
/// abandoned beside the live front end), whose second is abandoned part way through its level by a newer assignment, and whose third builds and swaps in; it follows the host's frames (the
/// guest stream checks the host's digest at every checkpoint), resyncs over its park from the second capture, and ends with the host's digest, world hash and save.
/// </summary>
internal sealed partial class CoopJoinSmokeTest : IDisposable
{
	/// <summary>One fixed game frame of the host phase (the host's session clock and its park run on it).</summary>
	private const double Step = 1 / 60.0;
	/// <summary>A run that stops getting frames fails, not hangs.</summary>
	private const double MaximumSeconds = 1200;

	private readonly GameFlow flow;
	private readonly string evidenceDirectory;
	private readonly bool joining;
	/// <summary>The join phase's measuring mode (<c>measure</c>): the untouched captures, joined and resynced once each, for the numbers; the adversarial join stays the check.</summary>
	private readonly bool measuring;
	/// <summary>Who runs the smoke and owns its loop ("Native": the process's own; a browser page's), named in its failure lines.</summary>
	private readonly string runner;
	/// <summary>
	/// What ends a run, once: the native runner closes the window on completion and exits on a failure; a page's runner records the outcome and lets the game run on. Both get the
	/// summary or the failure's line.
	/// </summary>
	private readonly Action<string> completedBy;
	private readonly Action<string> failedBy;
	private readonly BaseFileSystem originalSaveFileSystem;
	private int frame;
	private bool completed;
	private bool done;

	private CoopJoinSmokeTest( GameFlow flow, string evidenceDirectory, bool joining, bool measuring, string runner, Action<string> completedBy, Action<string> failedBy )
	{
		this.flow = flow;
		this.evidenceDirectory = evidenceDirectory;
		this.joining = joining;
		this.measuring = measuring;
		this.runner = runner;
		this.completedBy = completedBy;
		this.failedBy = failedBy;
		// the phases write nothing to the player's saves
		originalSaveFileSystem = SaveFileSystem;
		SaveFileSystem = new BaseFileSystem( Path.Combine( evidenceDirectory, joining ? "saves-join" : "saves-host" ) );
	}

	/// <summary>Root's CLI hook: <c>--coop-join-smoke host|join|measure</c> with <c>--coop-join-evidence</c>.</summary>
	internal static void Run( GameFlow flow, string phase, string evidenceDirectory )
	{
		ArgumentNullException.ThrowIfNull( flow );
		ArgumentException.ThrowIfNullOrWhiteSpace( evidenceDirectory );
		var (joining, measuring) = phase switch
		{
			"host" => (false, false),
			"join" => (true, false),
			"measure" => (true, true),
			_ => throw new ArgumentException( $"--coop-join-smoke expects 'host', 'join' or 'measure', not '{phase}'." )
		};
		Directory.CreateDirectory( evidenceDirectory );
		using var test = new CoopJoinSmokeTest( flow, Path.GetFullPath( evidenceDirectory ), joining, measuring, "Native", completedBy: _ => Render.Window.SdlWindow.Close(),
			failedBy: _ => Environment.Exit( 1 ) );
		// a run that stops getting frames fails, not hangs
		using var watchdog = new System.Threading.Timer( _ =>
		{
			Console.Error.WriteLine( $"Native co-op join smoke test failed: timed out after {MaximumSeconds} s at frame {test.frame} ({test.Where()})." );
			Environment.Exit( 1 );
		}, null, TimeSpan.FromSeconds( MaximumSeconds ), System.Threading.Timeout.InfiniteTimeSpan );
		Render.CaptureOutput = true;
		if ( joining )
		{
			// the level's construction by section, in the builds' timings (docs/COOP.md, "Measurements")
			LoadProfile.Enable( true );
			test.StartJoin();
			// the smoke's driver first (the replayed host's frames and answers arrive as a link's pump raises them), then the game's own frame, the whole update timed
			Render.OnUpdate += test.FrameStarts;
			Render.OnUpdate += test.JoinUpdate;
			Render.OnUpdate += flow.Update;
			Render.OnUpdate += test.FrameEnds;
			Render.OnRender += flow.Render;
			Render.Run();
			Render.OnUpdate -= test.FrameStarts;
			Render.OnUpdate -= test.JoinUpdate;
			Render.OnUpdate -= flow.Update;
			Render.OnUpdate -= test.FrameEnds;
			Render.OnRender -= flow.Render;
			LoadProfile.Enable( false );
		}
		else
		{
			test.StartHost();
			Render.OnUpdate += test.HostUpdate;
			Render.OnRender += flow.Render;
			Render.Run();
			Render.OnUpdate -= test.HostUpdate;
			Render.OnRender -= flow.Render;
		}
		Require( test.completed, "complete every frame of the native co-op join smoke" );
	}

	private string Where() => joining ? $"join step {joinStep}" : $"host stage {hostStage}";

	private void Fail( Exception error )
	{
		if ( done )
			return;
		done = true;
		var message = $"{error.GetType().Name}: {error.Message}";
		try
		{
			if ( joining )
				WriteJson( "join-evidence.json", JoinEvidence( completed: false, failure: message ) );
			else
				WriteJson( "host-evidence.json", HostEvidence( completed: false, failure: message ) );
		}
		catch ( Exception writeError )
		{
			Log.Warning( $"Co-op join smoke: writing the failure evidence failed too: {writeError.Message}" );
		}
		var line = $"{runner} co-op join smoke test failed ({Where()}): {message}";
		Console.Error.WriteLine( line );
		Log.Error( $"{line}\n{error.StackTrace}" );
		failedBy( line );
	}

	private void Complete( string summary )
	{
		completed = true;
		done = true;
		Log.Trace( summary );
		completedBy( summary );
	}

	internal static void Require( bool condition, string step )
	{
		if ( !condition )
			throw new InvalidOperationException( $"Native co-op join smoke test failed: {step}." );
	}

	internal static string Sha256Hex( byte[] bytes ) => Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant();

	/// <summary>
	/// A file of the evidence directory: a flat name of letters, digits, dots, dashes and underscores, never a path out of the directory, nor a name Windows reads as another file (a
	/// device such as <c>NUL</c> or <c>COM1.bin</c>, or a name with a trailing dot).
	/// </summary>
	internal static bool IsEvidenceFileName( string? name ) => name is { Length: > 0 and <= 128 } && char.IsAsciiLetterOrDigit( name[0] ) && name[^1] != '.'
		&& name.All( character => char.IsAsciiLetterOrDigit( character ) || character is '.' or '-' or '_' ) && !IsWindowsDeviceName( name.Split( '.' )[0] );

	private static bool IsWindowsDeviceName( string stem ) => stem.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
		|| stem.Length == 4 && (stem.StartsWith( "COM", StringComparison.OrdinalIgnoreCase ) || stem.StartsWith( "LPT", StringComparison.OrdinalIgnoreCase )) && char.IsAsciiDigit( stem[3] );

	private static readonly JsonSerializerOptions evidenceOptions = new() { WriteIndented = true };

	private void WriteJson<T>( string name, T value ) => File.WriteAllText( Path.Combine( evidenceDirectory, name ), JsonSerializer.Serialize( value, evidenceOptions ) );

	private T ReadJson<T>( string name ) => JsonSerializer.Deserialize<T>( File.ReadAllText( Path.Combine( evidenceDirectory, name ) ) )
		?? throw new InvalidDataException( $"The co-op join smoke's {name} is empty." );

	/// <summary>The host's frames as the host phase recorded them: a count, then each frame's number, host frame and bytes.</summary>
	private void WriteFrames( IReadOnlyList<(ulong Seq, long HostFrame, byte[] Payload)> frames )
	{
		using var stream = File.Create( Path.Combine( evidenceDirectory, "frames.bin" ) );
		Span<byte> header = stackalloc byte[20];
		BinaryPrimitives.WriteInt32LittleEndian( header, frames.Count );
		stream.Write( header[..4] );
		foreach ( var (seq, hostFrame, payload) in frames )
		{
			BinaryPrimitives.WriteUInt64LittleEndian( header, seq );
			BinaryPrimitives.WriteInt64LittleEndian( header[8..], hostFrame );
			BinaryPrimitives.WriteInt32LittleEndian( header[16..], payload.Length );
			stream.Write( header );
			stream.Write( payload );
		}
	}

	private List<(ulong Seq, long HostFrame, byte[] Payload)> ReadFrames()
	{
		var bytes = File.ReadAllBytes( Path.Combine( evidenceDirectory, "frames.bin" ) ).AsSpan();
		var count = BinaryPrimitives.ReadInt32LittleEndian( bytes );
		var at = 4;
		var frames = new List<(ulong, long, byte[])>( count );
		for ( var index = 0; index < count; index++ )
		{
			var seq = BinaryPrimitives.ReadUInt64LittleEndian( bytes[at..] );
			var hostFrame = BinaryPrimitives.ReadInt64LittleEndian( bytes[(at + 8)..] );
			var length = BinaryPrimitives.ReadInt32LittleEndian( bytes[(at + 16)..] );
			frames.Add( (seq, hostFrame, bytes.Slice( at + 20, length ).ToArray()) );
			at += 20 + length;
		}
		Require( at == bytes.Length, "the recorded frames read back whole" );
		return frames;
	}

	/// <summary>A join payload written again with another generation, and optionally another digest at its cut (the join phase's replayed host serves it).</summary>
	private static byte[] Rewrite( byte[] payload, uint generation, ulong? digest = null )
	{
		var decoded = CoopJoinPayload.Decode( payload );
		var writer = new CoopJoinPayloadWriter( decoded with { Generation = generation, Digest = digest ?? decoded.Digest } );
		while ( !writer.Run( TimeSpan.FromSeconds( 1 ) ) ) { }
		return writer.Bytes;
	}

	public void Dispose() => SaveFileSystem = originalSaveFileSystem;
}
