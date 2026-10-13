using NAudio.Utils;
using NAudio.Wave;

namespace OpenTPW;

/// <summary>Desktop CPU export of one explicitly selected SDT entry using the existing MPEG decoder.</summary>
// [EXT:sdt-wav-export] application-owned export of current decoded PCM, without event mixing or native audio parity claims
public static class SdtWavExport
{
	public static void Run( string inputPath, uint sampleNumber, string outputPath )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( inputPath );
		ArgumentException.ThrowIfNullOrWhiteSpace( outputPath );
		if ( sampleNumber == 0 )
			throw new ArgumentOutOfRangeException( nameof( sampleNumber ), "SDT entry numbers start at one." );
		if ( !Path.GetExtension( inputPath ).Equals( ".sdt", StringComparison.OrdinalIgnoreCase )
			|| !Path.GetExtension( outputPath ).Equals( ".wav", StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( "Sound export requires a local .sdt input and .wav output." );
		var destination = Path.GetFullPath( outputPath );
		if ( File.Exists( destination ) )
			throw new IOException( "Sound export will not overwrite an existing output." );

		var bank = new SoundBank( () => File.OpenRead( inputPath ), new FileInfo( inputPath ).Length );
		if ( sampleNumber > bank.Count )
			throw new ArgumentOutOfRangeException( nameof( sampleNumber ), "The selected SDT entry does not exist." );
		var mpeg = bank.GetMpeg( sampleNumber, Mp2Decoder.MaximumInputBytes )
			?? throw new InvalidDataException( "The selected SDT entry is missing or damaged." );
		var audio = Mp2Decoder.Decode( mpeg );
		if ( audio.TrailingBytes != 0 && !(audio.TrailingBytes == 1 && mpeg[^1] == 0) )
			throw new InvalidDataException( "The selected entry has unconsumed MPEG bytes; only the single zero SDT pad is accepted." );
		if ( audio.CrcFailures != 0 )
			throw new InvalidDataException( "The selected entry has failed MPEG CRC checks." );
		if ( audio.Samples.Length == 0 || audio.SampleRate <= 0 || audio.Channels is not (1 or 2)
			|| audio.Samples.Length % audio.Channels != 0 )
			throw new InvalidDataException( "The selected entry has no complete supported PCM frames." );
		var format = new WaveFormat( audio.SampleRate, 16, audio.Channels );

		// Decode and validation finish before any output mutation. Finalize RIFF before flushing the owned file.
		Directory.CreateDirectory( Path.GetDirectoryName( destination )! );
		var temporary = destination + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
		var ownsTemporary = false;
		try
		{
			using ( var file = new FileStream( temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None ) )
			{
				ownsTemporary = true;
				using ( var wave = new WaveFileWriter( new IgnoreDisposeStream( file ), format ) )
					wave.WriteSamples( audio.Samples, 0, audio.Samples.Length );
				file.Flush( true );
			}
			File.Move( temporary, destination, overwrite: false );
		}
		finally
		{
			if ( ownsTemporary ) File.Delete( temporary );
		}
		Console.WriteLine( $"Exported SDT entry {sampleNumber}: {audio.SampleFrames} decoded PCM frames, {audio.SampleRate} Hz, {audio.Channels} channel(s), signed 16-bit WAV to {destination}." );
	}
}
