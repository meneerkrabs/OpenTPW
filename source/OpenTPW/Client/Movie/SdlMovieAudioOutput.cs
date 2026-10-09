using System.Runtime.InteropServices;
using Veldrid.Sdl2;

namespace OpenTPW;

/// <summary>
/// Movie audio through SDL2's push queue (SDL_QueueAudio), using the SDL2 library Veldrid already loads: no
/// callback thread in managed code and no extra dependency. Signed 16-bit stereo at the movie's rate; SDL
/// converts to the device format. <see cref="LatencySeconds"/> is the obtained device buffer size.
/// </summary>
internal sealed unsafe class SdlMovieAudioOutput : IMovieAudioOutput
{
	private const uint InitAudio = 0x10;
	private const ushort AudioS16LittleEndian = 0x8010;

	[StructLayout( LayoutKind.Sequential )]
	private struct AudioSpec
	{
		public int Frequency;
		public ushort Format;
		public byte Channels;
		public byte Silence;
		public ushort Samples;
		public ushort Padding;
		public uint Size;
		public IntPtr Callback;
		public IntPtr UserData;
	}

	[UnmanagedFunctionPointer( CallingConvention.Cdecl )] private delegate int InitSubSystemFunction( uint flags );
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )] private delegate void QuitSubSystemFunction( uint flags );
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )] private delegate uint OpenAudioDeviceFunction( IntPtr device, int capture, AudioSpec* desired, AudioSpec* obtained, int allowedChanges );
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )] private delegate int QueueAudioFunction( uint device, void* data, uint length );
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )] private delegate uint GetQueuedAudioSizeFunction( uint device );
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )] private delegate void ClearQueuedAudioFunction( uint device );
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )] private delegate void PauseAudioDeviceFunction( uint device, int pause );
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )] private delegate void CloseAudioDeviceFunction( uint device );
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )] private delegate IntPtr GetErrorFunction();

	private static readonly Lazy<Functions> Native = new( () => new Functions() );

	private sealed class Functions
	{
		public readonly InitSubSystemFunction InitSubSystem = Sdl2Native.LoadFunction<InitSubSystemFunction>( "SDL_InitSubSystem" );
		public readonly QuitSubSystemFunction QuitSubSystem = Sdl2Native.LoadFunction<QuitSubSystemFunction>( "SDL_QuitSubSystem" );
		public readonly OpenAudioDeviceFunction OpenAudioDevice = Sdl2Native.LoadFunction<OpenAudioDeviceFunction>( "SDL_OpenAudioDevice" );
		public readonly QueueAudioFunction QueueAudio = Sdl2Native.LoadFunction<QueueAudioFunction>( "SDL_QueueAudio" );
		public readonly GetQueuedAudioSizeFunction GetQueuedAudioSize = Sdl2Native.LoadFunction<GetQueuedAudioSizeFunction>( "SDL_GetQueuedAudioSize" );
		public readonly ClearQueuedAudioFunction ClearQueuedAudio = Sdl2Native.LoadFunction<ClearQueuedAudioFunction>( "SDL_ClearQueuedAudio" );
		public readonly PauseAudioDeviceFunction PauseAudioDevice = Sdl2Native.LoadFunction<PauseAudioDeviceFunction>( "SDL_PauseAudioDevice" );
		public readonly CloseAudioDeviceFunction CloseAudioDevice = Sdl2Native.LoadFunction<CloseAudioDeviceFunction>( "SDL_CloseAudioDevice" );
		public readonly GetErrorFunction GetError = Sdl2Native.LoadFunction<GetErrorFunction>( "SDL_GetError" );
		public string Error => Marshal.PtrToStringUTF8( GetError() ) ?? "unknown SDL error";
	}

	private readonly uint device;
	private long totalQueuedFrames;
	private bool disposed;

	private SdlMovieAudioOutput( uint device, int sampleRate, int bufferFrames )
	{
		this.device = device;
		SampleRate = sampleRate;
		LatencySeconds = bufferFrames / (double)sampleRate;
	}

	/// <summary>Opens the default output device, or returns null (with the reason) when SDL audio is unavailable.</summary>
	public static SdlMovieAudioOutput? TryOpen( int sampleRate, out string? failure )
	{
		failure = null;
		try
		{
			var native = Native.Value;
			if ( native.InitSubSystem( InitAudio ) != 0 )
			{
				failure = native.Error;
				return null;
			}
			var desired = new AudioSpec { Frequency = sampleRate, Format = AudioS16LittleEndian, Channels = 2, Samples = 1024 };
			AudioSpec obtained;
			var device = native.OpenAudioDevice( IntPtr.Zero, 0, &desired, &obtained, 0 );
			if ( device == 0 )
			{
				failure = native.Error;
				native.QuitSubSystem( InitAudio );
				return null;
			}
			return new SdlMovieAudioOutput( device, sampleRate, obtained.Samples );
		}
		catch ( Exception exception ) when ( exception is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException )
		{
			failure = exception.Message;
			return null;
		}
	}

	public int SampleRate { get; }
	public double LatencySeconds { get; }
	public long QueuedFrames => disposed ? 0 : Native.Value.GetQueuedAudioSize( device ) / 4;
	public long PlayedFrames => Math.Max( 0, totalQueuedFrames - QueuedFrames );

	public void Queue( ReadOnlySpan<short> interleavedStereo )
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		if ( interleavedStereo.Length % 2 != 0 )
			throw new ArgumentException( "Stereo PCM must contain whole frames.", nameof( interleavedStereo ) );
		fixed ( short* samples = interleavedStereo )
		{
			if ( Native.Value.QueueAudio( device, samples, (uint)(interleavedStereo.Length * sizeof( short )) ) != 0 )
				throw new InvalidOperationException( $"SDL_QueueAudio failed: {Native.Value.Error}" );
		}
		totalQueuedFrames += interleavedStereo.Length / 2;
	}

	public void Play()
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		Native.Value.PauseAudioDevice( device, 0 );
	}

	public void Stop()
	{
		if ( disposed )
			return;
		var played = PlayedFrames;
		Native.Value.PauseAudioDevice( device, 1 );
		Native.Value.ClearQueuedAudio( device );
		totalQueuedFrames = played;
	}

	public void Dispose()
	{
		if ( disposed )
			return;
		Stop();
		Native.Value.CloseAudioDevice( device );
		Native.Value.QuitSubSystem( InitAudio );
		disposed = true;
	}
}
