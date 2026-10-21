using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>A cut the host phase took for the virtual member, as its Ready names it, with the staged state found at the cut.</summary>
internal sealed record CoopSmokeCut( uint Generation, ulong Assigned, long AssignedHostFrame, long Records, long Ticks, ulong Frame, ulong Digest, ulong WorldHash, string Sha256, long Bytes,
	long ReadyHostFrame, string File, string[] Staged );

/// <summary>The host phase's evidence: the join phase's oracle.</summary>
internal sealed record CoopSmokeHostEvidence( bool Completed, string? Failure, string Theme, string StartKind, int Coasters, CoopSmokeCut[] Cuts, ulong LastFrame, long LastTick,
	long LastRecord, ulong FinalDigest, ulong FinalWorldHash, string FinalSaveSha256, long HostFrames );

/// <summary>The join phase's evidence.</summary>
internal sealed record CoopSmokeJoinEvidence( bool Completed, string? Failure, int Builds, int FailedJoins, string[] Steps, ulong FinalDigest, ulong FinalWorldHash, string[] SaveDifferences );

/// <summary>
/// [EXT:online-coop-snapshot-join] The native join smoke (docs/COOP.md, "Joining a running session"): <c>--coop-join-smoke host|join --coop-join-evidence &lt;directory&gt;</c>, two processes with the game's
/// data, silent and offscreen. The host phase (<see cref="HostUpdate"/>) starts Jungle as Instant Action, builds two coasters by commands, hosts a session over a recording transport
/// (<see cref="CoopSmokeHostTransport"/>), puts in place what a join must carry (an employee in hand, a pending hire, a carried Move entry, the coaster tool open on a coaster; later a paused
/// park with edits since its last tick and a built coaster closed), takes two captures for a virtual member through the host's stream (the real cut), runs park minutes with commands at the
/// normal and the fastest speed and writes the frames, the captures and its evidence. The join phase (<see cref="JoinUpdate"/>) is a fresh process that starts in the front end and runs the
/// real game loop: it joins through the game's join host over a transport that plays the recorded host (<see cref="CoopSmokeReplayTransport"/>), whose first capture names a wrong digest
/// (a failed check, its build abandoned beside the live front end) and whose second builds and swaps in; it follows the host's frames (the guest stream checks the host's digest at every
/// checkpoint), is assigned the second capture while following (a resync over its park), and ends with the host's digest, world hash and save.
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
	private readonly System.Threading.Timer watchdog;
	private readonly BaseFileSystem originalSaveFileSystem;
	private int frame;
	private bool completed;
	private bool done;

	private CoopJoinSmokeTest( GameFlow flow, string evidenceDirectory, bool joining )
	{
		this.flow = flow;
		this.evidenceDirectory = evidenceDirectory;
		this.joining = joining;
		// the phases write nothing to the player's saves
		originalSaveFileSystem = SaveFileSystem;
		SaveFileSystem = new BaseFileSystem( Path.Combine( evidenceDirectory, joining ? "saves-join" : "saves-host" ) );
		watchdog = new System.Threading.Timer( _ =>
		{
			Console.Error.WriteLine( $"Native co-op join smoke test failed: timed out after {MaximumSeconds} s at frame {frame} ({Where()})." );
			Environment.Exit( 1 );
		}, null, TimeSpan.FromSeconds( MaximumSeconds ), System.Threading.Timeout.InfiniteTimeSpan );
	}

	/// <summary>Root's CLI hook: <c>--coop-join-smoke host|join</c> with <c>--coop-join-evidence</c>.</summary>
	internal static void Run( GameFlow flow, string phase, string evidenceDirectory )
	{
		ArgumentNullException.ThrowIfNull( flow );
		ArgumentException.ThrowIfNullOrWhiteSpace( evidenceDirectory );
		var joining = phase switch
		{
			"host" => false,
			"join" => true,
			_ => throw new ArgumentException( $"--coop-join-smoke expects 'host' or 'join', not '{phase}'." )
		};
		Directory.CreateDirectory( evidenceDirectory );
		using var test = new CoopJoinSmokeTest( flow, Path.GetFullPath( evidenceDirectory ), joining );
		Render.CaptureOutput = true;
		if ( joining )
		{
			test.StartJoin();
			// the smoke's driver first (the replayed host's frames and answers arrive as a link's pump raises them), then the game's own frame
			Render.OnUpdate += test.JoinUpdate;
			Render.OnUpdate += flow.Update;
			Render.OnRender += flow.Render;
			Render.Run();
			Render.OnUpdate -= test.JoinUpdate;
			Render.OnUpdate -= flow.Update;
			Render.OnRender -= flow.Render;
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
		Console.Error.WriteLine( $"Native co-op join smoke test failed ({Where()}): {message}" );
		Log.Error( $"Native co-op join smoke test failed ({Where()}): {message}\n{error.StackTrace}" );
		Environment.Exit( 1 );
	}

	private void Complete( string summary )
	{
		completed = true;
		done = true;
		Log.Trace( summary );
		Render.Window.SdlWindow.Close();
	}

	internal static void Require( bool condition, string step )
	{
		if ( !condition )
			throw new InvalidOperationException( $"Native co-op join smoke test failed: {step}." );
	}

	internal static string Sha256Hex( byte[] bytes ) => Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant();

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

	public void Dispose()
	{
		watchdog.Dispose();
		SaveFileSystem = originalSaveFileSystem;
	}
}
