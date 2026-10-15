using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpenTPW;
using static OpenTPW.Tests.Layer1TestFrames;

if ( args.Length != 2 )
{
	Console.Error.WriteLine( "Usage: Layer1Corpus <private Data directory> <report path outside repository>" );
	return 2;
}

var root = Path.GetFullPath( args[0] );
var reportPath = Path.GetFullPath( args[1] );
var repository = Path.GetFullPath( Path.Combine( AppContext.BaseDirectory, "../../../../../../../../" ) );
// Corpus/PCM outputs must remain external. The executable's bin path can vary, so
// also reject the known source-root suffix rather than depending on its location.
if ( reportPath.Contains( "/tools/ppc-analysis/", StringComparison.Ordinal ) ||
	reportPath.StartsWith( repository + Path.DirectorySeparatorChar, StringComparison.Ordinal ) )
	throw new ArgumentException( "Place corpus comparison reports outside the repository." );

var results = new List<object>();
var seen = new HashSet<string>();
var banks = new List<object>();
long comparedSamples = 0;
var maximumError = 0;
var streamsWithTrailingBytes = 0;
var layerTwoRegressionStreams = 0;
var ffmpegVersion = ReferenceVersion();

var synthetic = new (string Name, byte[] Stream)[]
{
	("silent-mpeg2", Repeat( Frame(), 4 )),
	("silent-mpeg1", Repeat( Frame( version: 3, bitrateIndex: 2 ), 4 )),
	("protected-silent", Repeat( Frame( crc: true, padding: 1 ), 4 )),
	("positive-clipping", Repeat( Frame( allocation: 1, scale: 3, sampleCode: 3 ), 4 )),
	("negative-clipping", Repeat( Frame( allocation: 1, scale: 0, sampleCode: 0 ), 4 )),
	("tone-offset-code2", Repeat( Frame( allocation: 1, sampleCode: 2 ), 4 )),
	("negative-tone", Repeat( Frame( allocation: 1, sampleCode: 0 ), 4 )),
	("allocation14", Repeat( Frame( allocation: 14, bitrateIndex: 14 ), 4 )),
	("last-subband", Repeat( Frame( allocation: 3, subband: 31 ), 4 )),
	("stereo-factors", Repeat( Frame( allocation: 3, mode: 0, rightScale: 6 ), 4 )),
	("dual-channel", Repeat( Frame( allocation: 3, mode: 2 ), 4 )),
	("joint-independent", Repeat( Frame( allocation: 2, mode: 1, sampleCode: 4, rightSampleCode: 2 ), 4 )),
};
foreach ( var item in synthetic )
	Compare( "synthetic/" + item.Name, item.Stream );
for ( var extension = 0; extension < 4; extension++ )
	Compare( $"synthetic/joint-bound-{extension}", Repeat( Frame( allocation: 3, mode: 1,
		modeExtension: extension, subband: (extension + 1) * 4, rightScale: 6, bitrateIndex: 14 ), 4 ) );

var entries = 0;
foreach ( var path in Directory.EnumerateFiles( root, "*", SearchOption.AllDirectories ).Where( path => Path.GetExtension( path ).Equals( ".sdt", StringComparison.OrdinalIgnoreCase ) ).Order( StringComparer.Ordinal ) )
{
	var data = File.ReadAllBytes( path );
	if ( data.Length < 4 ) throw new InvalidDataException( "Short SDT." );
	var count = BinaryPrimitives.ReadInt32LittleEndian( data );
	if ( count < 0 || count > 65536 || 4L + count * 4L > data.Length ) throw new InvalidDataException( "Bad SDT directory." );
	var layerOneInBank = 0;
	for ( var index = 0; index < count; index++ )
	{
		var offset = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 4 + index * 4 ) );
		if ( offset < 4 + count * 4 || offset > data.Length - 40 ) throw new InvalidDataException( "Bad SDT entry offset." );
		var header = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( offset ) );
		var size = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( offset + 4 ) );
		if ( header < 40 || size < 0 || (long)offset + header + size > data.Length ) throw new InvalidDataException( "Bad SDT entry bounds." );
		var frame = data.AsSpan( offset + header, size );
		if ( frame.Length < 4 ) continue;
		var word = BinaryPrimitives.ReadUInt32BigEndian( frame );
		if ( word >> 21 != 0x7FF ) continue;
		var relative = Path.GetRelativePath( root, path ).Replace( '\\', '/' );
		if ( (word >> 17 & 3) == 2 && (relative.Equals( "global/sound/MusicHD.sdt", StringComparison.OrdinalIgnoreCase ) ||
			(relative.Equals( "global/Speech/speechHD.SDT", StringComparison.OrdinalIgnoreCase ) && index == 0)) )
		{
			var regressionName = System.Text.Encoding.ASCII.GetString( data, offset + 8, 16 ).TrimEnd( '\0' );
			Compare( "layer2-regression/" + relative + "/" + regressionName, frame.ToArray() );
			layerTwoRegressionStreams++;
		}
		if ( (word >> 17 & 3) != 3 ) continue;
		entries++;
		layerOneInBank++;
		var compressedHash = Convert.ToHexString( SHA256.HashData( frame ) );
		if ( !seen.Add( compressedHash ) ) continue;
		var name = System.Text.Encoding.ASCII.GetString( data, offset + 8, 16 ).TrimEnd( '\0' );
		Compare( relative + "/" + name, frame.ToArray() );
		if ( seen.Count % 100 == 0 ) Console.WriteLine( $"Compared {seen.Count} unique private Layer I streams; max error {maximumError}." );
	}
	if ( layerOneInBank > 0 )
		banks.Add( new { Bank = Path.GetRelativePath( root, path ), Sha256 = Convert.ToHexString( SHA256.HashData( data ) ), Entries = layerOneInBank } );
}

var report = new
{
	Reference = ffmpegVersion,
	PrivateEntries = entries,
	UniquePrivateStreams = seen.Count,
	SyntheticStreams = synthetic.Length + 4,
	CompletePrefixSamplesCompared = comparedSamples,
	MaximumAbsolutePcmError = maximumError,
	StreamsWithTrailingBytes = streamsWithTrailingBytes,
	LayerTwoRegressionStreams = layerTwoRegressionStreams,
	Banks = banks,
	Results = results,
	Limitations = "Reference decodes only the complete-frame prefix; partial final frames remain reported, not decoded. No original execution/device/capture proof."
};
File.WriteAllText( reportPath, JsonSerializer.Serialize( report, new JsonSerializerOptions { WriteIndented = true } ) );
Console.WriteLine( $"Layer I comparison: {entries} private entries / {seen.Count} unique; {comparedSamples} PCM samples; max error {maximumError}; report {reportPath}." );
return maximumError <= 1 ? 0 : 1;

void Compare( string identity, byte[] stream )
{
	var audio = Mp2Decoder.Decode( stream );
	if ( audio.TrailingBytes != 0 ) streamsWithTrailingBytes++;
	var complete = stream.AsMemory( 0, stream.Length - audio.TrailingBytes );
	var reference = DecodeReference( complete );
	if ( reference.Length != audio.Samples.Length * 2 )
		throw new InvalidDataException( $"Reference length differs for {identity}: {reference.Length} bytes vs {audio.Samples.Length * 2}." );
	var actual = new byte[reference.Length];
	var peak = 0;
	double errorSquared = 0, signalSquared = 0;
	for ( var index = 0; index < audio.Samples.Length; index++ )
	{
		var expected = BinaryPrimitives.ReadInt16LittleEndian( reference.AsSpan( index * 2 ) );
		var difference = audio.Samples[index] - expected;
		peak = Math.Max( peak, Math.Abs( difference ) );
		errorSquared += (double)difference * difference;
		signalSquared += (double)expected * expected;
		BinaryPrimitives.WriteInt16LittleEndian( actual.AsSpan( index * 2 ), audio.Samples[index] );
	}
	maximumError = Math.Max( maximumError, peak );
	comparedSamples += audio.Samples.Length;
	results.Add( new
	{
		Identity = identity,
		CompressedSha256 = Convert.ToHexString( SHA256.HashData( stream ) ),
		ActualPcmSha256 = Convert.ToHexString( SHA256.HashData( actual ) ),
		ReferencePcmSha256 = Convert.ToHexString( SHA256.HashData( reference ) ),
		audio.SampleRate,
		audio.Channels,
		audio.FrameCount,
		audio.TrailingBytes,
		Samples = audio.Samples.Length,
		MaximumAbsoluteError = peak,
		ErrorSquared = errorSquared,
		SignalSquared = signalSquared
	} );
}

static byte[] Repeat( byte[] frame, int count ) => Enumerable.Range( 0, count ).SelectMany( _ => frame ).ToArray();

static string ReferenceVersion()
{
	var info = new ProcessStartInfo( "ffmpeg" ) { RedirectStandardOutput = true, RedirectStandardError = true };
	info.ArgumentList.Add( "-version" );
	using var process = Process.Start( info ) ?? throw new IOException( "Cannot start installed ffmpeg reference." );
	var output = process.StandardOutput.ReadToEnd(); process.WaitForExit();
	if ( process.ExitCode != 0 ) throw new IOException( "Cannot inspect reference version." );
	return output.Split( '\n' )[0];
}

static byte[] DecodeReference( ReadOnlyMemory<byte> data )
{
	var info = new ProcessStartInfo( "ffmpeg" )
	{
		RedirectStandardInput = true,
		RedirectStandardOutput = true,
		RedirectStandardError = true,
	};
	var decoder = (BinaryPrimitives.ReadUInt32BigEndian( data.Span ) >> 17 & 3) == 3 ? "mp1" : "mp2";
	foreach ( var argument in new[] { "-hide_banner", "-loglevel", "error", "-f", "mp3", "-c:a", decoder, "-i", "pipe:0", "-f", "s16le", "-acodec", "pcm_s16le", "pipe:1" } )
		info.ArgumentList.Add( argument );
	using var process = Process.Start( info ) ?? throw new IOException( "Cannot start installed reference decoder." );
	using var output = new MemoryStream();
	var readOutput = process.StandardOutput.BaseStream.CopyToAsync( output );
	var readError = process.StandardError.ReadToEndAsync();
	process.StandardInput.BaseStream.Write( data.Span );
	process.StandardInput.Close();
	readOutput.GetAwaiter().GetResult(); process.WaitForExit();
	var diagnostics = readError.GetAwaiter().GetResult();
	if ( process.ExitCode != 0 ) throw new InvalidDataException( "Independent reference failed: " + diagnostics );
	return output.ToArray();
}
