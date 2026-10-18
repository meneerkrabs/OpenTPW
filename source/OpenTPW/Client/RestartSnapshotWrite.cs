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
	internal static string MarkerPath( string path ) => path + ".pending.json";
	internal bool Done { get; private set; }

	internal RestartSnapshotWrite( string path, ParkResumeSnapshot snapshot, Action<string, byte[]>? persist = null )
	{
		this.path = path;
		this.persist = persist ?? ParkResumeSaveFile.SaveBytes;
		serializer = ParkResumeSaveFile.BeginSlicedSerialize( snapshot );
		// Use the existing flushed temporary-file/rename protocol for the marker too.
		this.persist( MarkerPath( path ), Encoding.UTF8.GetBytes( "\"pending\"" ) );
	}

	internal bool Run( TimeSpan budget )
	{
		if ( Done ) return true;
		using ( LoadProfile.Measure( "restart-serialize-slice" ) )
			if ( !serializer.Run( budget ) ) return false;
		var bytes = ParkResumeSaveFile.CheckSize( serializer.Bytes );
		using ( LoadProfile.Measure( "restart-write" ) )
		{
			var digest = Convert.ToHexString( SHA256.HashData( bytes ) );
			persist( MarkerPath( path ), Encoding.UTF8.GetBytes( "\"" + digest + "\"" ) );
			persist( path, bytes );
			Done = true;
			// A failed cleanup is harmless: Load recognizes the completed file by its digest.
			try { File.Delete( MarkerPath( path ) ); }
			catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException ) { }
		}
		return true;
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
