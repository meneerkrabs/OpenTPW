using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace OpenTPW;

/// <summary>
/// A detached restart snapshot, serialized only on the game thread. A pending marker prevents a crash before the atomic
/// replacement from exposing the previous park's restart point. Once bytes exist, the marker pins their digest, so a crash
/// after replacement but before marker cleanup still leaves a usable new snapshot. No original game files are written.
/// </summary>
internal sealed class RestartSnapshotWrite
{
	private readonly string path;
	private readonly SlicedJsonSerializer serializer;
	private readonly Action<string, byte[]> persist;
	private readonly bool cooperative;
	private readonly Func<Action, Task> background;
	private Task? writing;
	private byte[]? copiedBytes;
	private IncrementalHash? hash;
	private int copied;
	// the capture's validation steps, still to run before the first serialization slice (null: validated already)
	private IEnumerator<bool>? validation;
	internal const int CopyChunkBytes = 64 * 1024;
	internal int CopiedBytes => copied;
	internal static string MarkerPath( string path ) => path + ".pending.json";
	internal bool Done { get; private set; }

	/// <param name="validation">
	/// The capture's closing validation in steps (<see cref="ParkResumeSaveFile.ValidateSnapshotSteps"/>) when it has not run yet: the first slices run its steps (at least one per slice,
	/// none with serialization, all at once with an unlimited budget), and a refusal there ends the write before any byte of the snapshot is written. It includes the shape check,
	/// so the serialization does not repeat it.
	/// </param>
	internal RestartSnapshotWrite( string path, ParkResumeSnapshot snapshot, Action<string, byte[]>? persist = null, bool? cooperative = null, Func<Action, Task>? background = null, IEnumerable<bool>? validation = null )
	{
		this.path = path;
		this.validation = validation?.GetEnumerator();
		this.persist = persist ?? ParkResumeSaveFile.SaveBytes;
		this.cooperative = cooperative ?? OperatingSystem.IsBrowser();
		this.background = background ?? (work => Task.Run( work ));
		using ( LoadProfile.Measure( "restart-json-begin" ) )
			serializer = ParkResumeSaveFile.BeginSlicedSerialize( snapshot, validation != null ? static () => { } : null );
		// Use the existing flushed temporary-file/rename protocol for the marker too.
		using ( LoadProfile.Measure( "restart-marker" ) )
			this.persist( MarkerPath( path ), Encoding.UTF8.GetBytes( "\"pending\"" ) );
	}

	/// <summary>One soft-budget slice; an unlimited budget also waits for an already-started desktop write (restart/shutdown).</summary>
	internal bool Run( TimeSpan budget )
	{
		if ( Done ) return true;
		if ( validation != null )
		{
			var started = Stopwatch.GetTimestamp();
			try
			{
				using ( LoadProfile.Measure( "restart-validate" ) )
					while ( validation.MoveNext() )
						if ( budget != TimeSpan.MaxValue && Stopwatch.GetElapsedTime( started ) >= budget )
							return false;
			}
			catch
			{
				// as a capture refused in its own frame: no earlier park's snapshot is left behind to be restarted into (nor its marker)
				validation.Dispose();
				validation = null;
				Done = true;
				foreach ( var file in new[] { path, MarkerPath( path ) } )
					try { File.Delete( file ); }
					catch ( Exception deleteError ) when ( deleteError is IOException or UnauthorizedAccessException ) { }
				throw;
			}
			validation.Dispose();
			validation = null;
			if ( budget != TimeSpan.MaxValue ) return false;
		}
		if ( writing != null ) return FinishWrite( budget );
		if ( !serializer.Done )
		{
			using ( LoadProfile.Measure( "restart-serialize-slice" ) )
				if ( !serializer.Run( budget ) ) return false;
			if ( serializer.WrittenMemory.Length > ParkResumeSaveFile.MaximumFileSize )
				throw new InvalidDataException( "World save exceeds the size limit." );
			if ( !cooperative )
			{
				// Only completed detached bytes cross threads. The serializer is never advanced again and owns its buffer
				// until this job finishes; no level, catalogue, rendering object or mutable world state is read here.
				writing = background( () =>
				{
					var bytes = serializer.WrittenMemory.ToArray();
					Persist( bytes, Convert.ToHexString( SHA256.HashData( bytes ) ) );
				} );
				return FinishWrite( budget );
			}
			// Keep the browser's final copy/hash out of the last serialization slice.
			if ( budget != TimeSpan.MaxValue ) return false;
		}
		try
		{
			using ( LoadProfile.Measure( "restart-copy-hash-slice" ) )
			{
				var started = Stopwatch.GetTimestamp();
				copiedBytes ??= new byte[serializer.WrittenMemory.Length];
				hash ??= IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
				while ( copied < copiedBytes.Length )
				{
					var length = Math.Min( CopyChunkBytes, copiedBytes.Length - copied );
					var part = serializer.WrittenMemory.Span.Slice( copied, length );
					part.CopyTo( copiedBytes.AsSpan( copied, length ) );
					hash.AppendData( part ); copied += length;
					if ( Stopwatch.GetElapsedTime( started ) >= budget ) return false;
				}
			}
			var digest = Convert.ToHexString( hash!.GetHashAndReset() );
			hash.Dispose(); hash = null;
			using ( LoadProfile.Measure( "restart-write" ) ) Persist( copiedBytes!, digest );
			copiedBytes = null; Done = true;
			return true;
		}
		catch
		{
			hash?.Dispose(); hash = null; copiedBytes = null; copied = 0;
			throw;
		}
	}

	private bool FinishWrite( TimeSpan budget )
	{
		if ( budget != TimeSpan.MaxValue && !writing!.IsCompleted ) return false;
		writing!.GetAwaiter().GetResult(); // surface the original failure to GameFlow's existing policy
		Done = true; writing = null;
		return true;
	}

	private void Persist( byte[] bytes, string digest )
	{
		persist( MarkerPath( path ), Encoding.UTF8.GetBytes( "\"" + digest + "\"" ) );
		persist( path, bytes );
		// A failed cleanup is harmless: Load recognizes the completed file by its digest.
		try { File.Delete( MarkerPath( path ) ); }
		catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException ) { }
	}

	internal static ParkResumeSnapshot Load( string path )
	{
		if ( !File.Exists( MarkerPath( path ) ) ) return ParkResumeSaveFile.Load( path );
		using var marker = File.OpenRead( MarkerPath( path ) );
		if ( marker.Length != 66 ) throw new InvalidDataException( "The restart snapshot was interrupted before it was committed." );
		var expected = new byte[66]; marker.ReadExactly( expected );
		using var file = File.OpenRead( path );
		if ( file.Length > ParkResumeSaveFile.MaximumFileSize ) throw new InvalidDataException( "World save exceeds the size limit." );
		var bytes = new byte[file.Length]; file.ReadExactly( bytes );
		var actual = Encoding.UTF8.GetBytes( "\"" + Convert.ToHexString( SHA256.HashData( bytes ) ) + "\"" );
		if ( !expected.AsSpan().SequenceEqual( actual ) ) throw new InvalidDataException( "The restart snapshot was interrupted before it was committed." );
		return ParkResumeSaveFile.Deserialize( bytes );
	}
}
