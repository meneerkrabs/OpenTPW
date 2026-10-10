using System.Collections.Concurrent;
using Veldrid;

namespace OpenTPW;

/// <summary>Progress of a running texture pack switch, polled by the loading screen once per frame.</summary>
public interface ITexturePackSwitch
{
	/// <summary>Textures reloaded so far.</summary>
	int Done { get; }
	/// <summary>Textures to reload.</summary>
	int Total { get; }
	bool Finished { get; }
	/// <summary>Does a slice of the work (render thread, once per frame).</summary>
	void Pump();
}

/// <summary>
/// Switches the active texture pack while the game runs: every texture made from a game <c>.wct</c> is decoded again from
/// the new pack (or the original) and swapped in place. Decoding runs on a worker thread through a small bounded queue
/// (a 4x pack would otherwise hold gigabytes of pixels); GPU uploads and swaps happen in <see cref="Pump"/> on the render
/// thread within a time budget per frame, so the game stays responsive. The old GPU textures are deleted after the frame
/// that last drew them. Textures that fail to decode keep their current pixels.
/// </summary>
// [EXT:texture-pack] Runtime switch of the optional local texture pack (docs/TEXTURE-PACKS.md).
public sealed class TexturePackSwitch : ITexturePackSwitch
{
	private sealed record Loaded( string Path, TextureFlags Flags, byte[]? Data, int Width, int Height );

	/// <summary>Raised on the render thread once all textures are swapped; holders of their own GPU copies (UI images, guest sprite atlases) reload here.</summary>
	public static event Action? PackChanged;

	private readonly List<(string Path, TextureFlags Flags)> targets;
	private readonly BlockingCollection<Loaded> queue = new( 12 );
	private readonly CancellationTokenSource cancel = new();
	private readonly TimeSpan budget;
	private int done;

	/// <summary>
	/// The running switch. The game loop pumps it (<see cref="PumpCurrent"/>), so it completes whether or not a loading
	/// screen is still on the UI stack; a newer switch cancels an unfinished one (it reloads every texture anyway).
	/// </summary>
	public static TexturePackSwitch? Current { get; private set; }

	/// <summary>Does one slice of the running switch, if any (render thread, once per frame).</summary>
	public static void PumpCurrent()
	{
		var current = Current;
		if ( current == null )
			return;
		current.Pump();
		if ( current.Finished && Current == current )
			Current = null;
	}

	/// <summary>Abandons the switch: what is already swapped stays, the worker stops, <see cref="PackChanged"/> is not raised.</summary>
	public void Cancel()
	{
		if ( Finished )
			return;
		Finished = true;
		cancel.Cancel();
		if ( Current == this )
			Current = null;
	}

	public int Done => done;
	public int Total => targets.Count;
	public bool Finished { get; private set; }

	/// <summary>
	/// Activates <paramref name="name"/> (empty for the originals) and starts reloading. Null when nothing can change:
	/// an <c>OPENTPW_TEXTURE_PACK</c> override pins the pack.
	/// </summary>
	public static TexturePackSwitch? Begin( string? name, ICollection<string> diagnostics, string? packsDirectory = null, TimeSpan? budget = null )
	{
		if ( !string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "OPENTPW_TEXTURE_PACK" ) ) )
			return null;
		Current?.Cancel();
		TexturePack.Activate( name, diagnostics, packsDirectory );
		return Current = new TexturePackSwitch( Texture.ReloadTargets(), budget ?? TimeSpan.FromMilliseconds( 8 ), Texture.DecodeWct );
	}

	private readonly Func<string, (byte[] Data, int Width, int Height)> decode;
	private readonly Action<string, TextureFlags, byte[], int, int> apply;

	/// <summary>Test seam: a switch over explicit targets with its own decoder and swap.</summary>
	internal TexturePackSwitch( List<(string Path, TextureFlags Flags)> targets, TimeSpan budget,
		Func<string, (byte[] Data, int Width, int Height)> decode, Action<string, TextureFlags, byte[], int, int>? apply = null )
	{
		this.targets = targets;
		this.budget = budget;
		this.decode = decode;
		this.apply = apply ?? Texture.Reload;
		Task.Factory.StartNew( Decode, TaskCreationOptions.LongRunning );
	}

	private void Decode()
	{
		try
		{
			foreach ( var (path, flags) in targets )
			{
				Loaded loaded;
				try
				{
					var (data, width, height) = decode( path );
					loaded = new Loaded( path, flags, data, width, height );
				}
				catch ( Exception exception ) when ( exception is not OutOfMemoryException )
				{
					Log?.Warning( $"Texture pack switch: {path} could not be decoded ({exception.Message}); keeping its current pixels." );
					loaded = new Loaded( path, flags, null, 0, 0 );
				}
				queue.Add( loaded, cancel.Token );
			}
		}
		catch ( OperationCanceledException )
		{
		}
	}

	public void Pump()
	{
		if ( Finished )
			return;
		var started = System.Diagnostics.Stopwatch.GetTimestamp();
		while ( done < targets.Count && System.Diagnostics.Stopwatch.GetElapsedTime( started ) < budget )
		{
			if ( !queue.TryTake( out var loaded ) )
				break;
			if ( loaded.Data != null )
			{
				try
				{
					apply( loaded.Path, loaded.Flags, loaded.Data, loaded.Width, loaded.Height );
				}
				catch ( Exception exception ) when ( exception is not OutOfMemoryException )
				{
					Log?.Warning( $"Texture pack switch: {loaded.Path} could not be uploaded ({exception.Message}); keeping its current pixels." );
				}
			}
			done++;
		}
		if ( done < targets.Count )
			return;
		Finished = true;
		queue.Dispose();
		cancel.Dispose();
		PackChanged?.Invoke();
	}
}
