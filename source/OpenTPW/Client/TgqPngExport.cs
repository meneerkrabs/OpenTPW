using OpenTPW.Online.Packages;

namespace OpenTPW;

/// <summary>Exports one explicitly selected current decoded TGQ raster, without audio, presentation or a game window.</summary>
// [EXT:tgq-frame-export] current opaque RGBA conversion at encoded size, not native IDCT/colour/display-aspect parity
public static class TgqPngExport
{
	public static void Run( string inputPath, int frameIndex, string outputPath )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( inputPath );
		ArgumentException.ThrowIfNullOrWhiteSpace( outputPath );
		ArgumentOutOfRangeException.ThrowIfNegative( frameIndex );
		if ( !Path.GetExtension( inputPath ).Equals( ".tgq", StringComparison.OrdinalIgnoreCase ) ||
			!Path.GetExtension( outputPath ).Equals( ".png", StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( "TGQ frame export requires a local .tgq input and .png output." );
		var destination = Path.GetFullPath( outputPath );
		if ( File.Exists( destination ) ) throw new IOException( "TGQ frame export will not overwrite an existing output file." );
		TgqMovieFile movie;
		using ( var input = File.OpenRead( inputPath ) ) movie = new TgqMovieFile( input );
		var frame = movie.DecodeVideoFrame( frameIndex );
		var rgba = new byte[checked(frame.Width * frame.Height * 4)];
		frame.WriteRgba32( rgba );
		var png = PngImage.EncodeRgba( frame.Width, frame.Height, rgba );
		// Strict container checks, selected payload decode and PNG encoding complete before any output mutation.
		Directory.CreateDirectory( Path.GetDirectoryName( destination )! );
		var temporary = destination + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
		var ownsTemporary = false;
		try
		{
			using ( var stream = new FileStream( temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None ) )
			{
				ownsTemporary = true;
				stream.Write( png );
				stream.Flush( true );
			}
			File.Move( temporary, destination, overwrite: false );
		}
		finally { if ( ownsTemporary ) File.Delete( temporary ); }
		Console.WriteLine( $"Exported TGQ frame {frameIndex}: {frame.Width}x{frame.Height} current decoded opaque RGBA to {destination}." );
	}
}
