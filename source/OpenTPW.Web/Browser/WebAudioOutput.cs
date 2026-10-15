using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;

namespace OpenTPW;

/// <summary>
/// The browser's answer to the desktop SDL queued audio output, under the same name so the game's
/// mixer, speech and movie players use it unchanged: 16-bit stereo PCM goes to a WebAudio
/// AudioWorklet (opentpw-audio.js), which reports the frames it has played.
/// </summary>
internal sealed partial class SdlMovieAudioOutput : IMovieAudioOutput
{
	private const string Module = "opentpw-audio";
	private int id;

	private SdlMovieAudioOutput( int id, int sampleRate )
	{
		this.id = id;
		SampleRate = sampleRate;
	}

	public static SdlMovieAudioOutput? TryOpen( int sampleRate, out string? failure )
	{
		var id = Open( sampleRate );
		failure = id == 0 ? $"the browser offers no audio output at {sampleRate} Hz" : null;
		return id == 0 ? null : new SdlMovieAudioOutput( id, sampleRate );
	}

	public int SampleRate { get; }
	public long PlayedFrames => id == 0 ? 0 : (long)Played( id );
	public long QueuedFrames => id == 0 ? 0 : (long)Queued( id );
	public double LatencySeconds => id == 0 ? 0 : Latency( id );

	public void Queue( ReadOnlySpan<short> interleavedStereo )
	{
		if ( id == 0 || interleavedStereo.IsEmpty )
			return;
		var bytes = MemoryMarshal.AsBytes( interleavedStereo );
		QueuePcm( id, MemoryMarshal.CreateSpan( ref MemoryMarshal.GetReference( bytes ), bytes.Length ) );
	}

	public void Play()
	{
		if ( id != 0 )
			PlayOutput( id );
	}

	public void Stop()
	{
		if ( id != 0 )
			StopOutput( id );
	}

	public void Dispose()
	{
		if ( id == 0 )
			return;
		Close( id );
		id = 0;
	}

	[JSImport( "open", Module )]
	private static partial int Open( int sampleRate );

	[JSImport( "queue", Module )]
	private static partial void QueuePcm( int id, [JSMarshalAs<JSType.MemoryView>] Span<byte> pcm );

	[JSImport( "play", Module )]
	private static partial void PlayOutput( int id );

	[JSImport( "stop", Module )]
	private static partial void StopOutput( int id );

	[JSImport( "played", Module )]
	private static partial double Played( int id );

	[JSImport( "queued", Module )]
	private static partial double Queued( int id );

	[JSImport( "latency", Module )]
	private static partial double Latency( int id );

	[JSImport( "close", Module )]
	private static partial void Close( int id );
}
