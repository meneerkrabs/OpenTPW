using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenTPW;

if ( args.Length == 1 && args[0] == "--self-test" )
{
	SelfTest();
	return;
}
if ( args.Length == 2 && args[0] == "--version-info" )
{
	var version = FileVersionInfo.GetVersionInfo( args[1] );
	Console.WriteLine( JsonSerializer.Serialize( new
	{
		filename = Path.GetFileName( args[1] ),
		version.FileVersion,
		version.ProductVersion,
		version.ProductName,
		version.CompanyName,
		limitation = "Standard file resource metadata, not an engine version or runtime equivalence proof"
	} ) );
	return;
}
if ( args.Length != 3 )
	throw new ArgumentException( "Usage: TpiCompare <corpus-root> <distinct-baseline-label> <metadata.json>; or --self-test" );
var root = Path.GetFullPath( args[0] );
if ( !Directory.Exists( root ) ) throw new DirectoryNotFoundException( root );
var records = new List<object>();
var signatures = new Dictionary<string, int>();
var extensionCounts = new Dictionary<string, int>();
var budget = new Dictionary<string, int>();
var files = Directory.EnumerateFiles( root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint } )
	.OrderBy( ProbePriority ).ThenBy( x => x, StringComparer.Ordinal ).Take( 50001 ).ToArray();
if ( files.Length > 50000 ) throw new InvalidDataException( "Corpus exceeds the 50,000-file inventory bound." );
foreach ( var path in files )
{
	var relative = Path.GetRelativePath( root, path ).Replace( '\\', '/' );
	var basename = Path.GetFileName( path );
	var exclusion = Exclusion( relative );
	if ( exclusion != null )
	{
		records.Add( new { path = relative, status = "not-read-user-exclusion", reason = exclusion } );
		continue;
	}
	var extension = Path.GetExtension( path ).ToLowerInvariant();
	extensionCounts[extension] = extensionCounts.GetValueOrDefault( extension ) + 1;
	using var input = File.OpenRead( path );
	var length = input.Length;
	var hash = Convert.ToHexString( SHA256.HashData( input ) ).ToLowerInvariant();
	input.Position = 0;
	var prefix = new byte[Math.Min( length, 512 )];
	input.ReadExactly( prefix );
	var signature = Signature( prefix );
	signatures[signature] = signatures.GetValueOrDefault( signature ) + 1;
	object? result = null;
	var family = ProbeFamily( signature, extension );
	if ( family != null )
	{
		if ( length > 128 * 1024 * 1024 ) result = new { status = "unverified-limit", reason = "128 MiB physical parse bound" };
		else if ( budget.GetValueOrDefault( family ) >= 12 ) result = new { status = "unverified-limit", reason = "12 physical reader probes per family" };
		else
		{
			budget[family] = budget.GetValueOrDefault( family ) + 1;
			var data = File.ReadAllBytes( path );
			result = Probe( relative, data, family, budget );
		}
	}
	records.Add( new { path = relative, bytes = length, sha256 = hash, signature, probeFamily = family, readerResult = result } );
}
var report = new
{
	schema = 1,
	baselineLabel = args[1],
	scope = "Static metadata and existing-reader acceptance only; no executable execution or protection removal",
	fileCount = records.Count,
	signatureCounts = signatures,
	extensionCounts,
	limits = new
	{
		maximumFiles = 50000,
		physicalParseBytes = 128 * 1024 * 1024,
		physicalProbesPerFamily = 12,
		memberProbesPerFamily = 24,
		compressedMemberBytes = 65536,
		memberBytes = 16 * 1024 * 1024
	},
	files = records
};
File.WriteAllText( args[2], JsonSerializer.Serialize( report, new JsonSerializerOptions { WriteIndented = true } ) );
Console.WriteLine( $"{args[1]}: {records.Count} physical files; {string.Join( ", ", signatures.Select( x => $"{x.Key}={x.Value}" ) )}." );

static string Signature( ReadOnlySpan<byte> data )
{
	if ( data.StartsWith( "DWFB"u8 ) ) return "DWFB";
	if ( data.Length >= 4 && U32( data, 0 ) == ModelFile.Magic ) return "M3D2";
	if ( data.StartsWith( "RSSEQ"u8 ) ) return "RSSEQ";
	if ( data.StartsWith( "TP2M"u8 ) ) return "TP2M";
	if ( data.StartsWith( "MZ"u8 ) ) return "MZ-candidate";
	if ( data.StartsWith( "Joy!peffpwpc"u8 ) ) return "PowerPC-PEF";
	if ( data.StartsWith( "MSCF"u8 ) ) return "Microsoft-CAB";
	if ( data.StartsWith( "BFST"u8 ) ) return "BFST";
	if ( data.StartsWith( "BFMU"u8 ) ) return "BFMU";
	if ( data.StartsWith( "SHPI"u8 ) ) return "SHPI";
	if ( data.StartsWith( "PK"u8 ) ) return "ZIP-candidate";
	return "unrecognized";
}

static string? ProbeFamily( string signature, string extension ) => signature switch
{
	"DWFB" => "WAD",
	"M3D2" => "MD2",
	"RSSEQ" => "RSE",
	"TP2M" => "MAP",
	"MZ-candidate" => "PE",
	_ => extension switch { ".sam" => "SAM", ".sdt" => "SDT", ".cos" => "COS", ".md2" => "MD2", ".rse" => "RSE", ".wad" => "WAD", ".map" => "MAP", ".fsh" => "FSH-unverified", _ => null }
};

static string? Exclusion( string path )
{
	var basename = Path.GetFileName( path );
	if ( basename.Contains( "serial", StringComparison.OrdinalIgnoreCase ) || basename.Contains( "license", StringComparison.OrdinalIgnoreCase ) || basename.Contains( "eula", StringComparison.OrdinalIgnoreCase ) )
		return "Serial/license content exclusion";
	if ( path.Split( '/' ).Any( p => p.Equals( "WIN10FIX+NOCDFIX", StringComparison.OrdinalIgnoreCase ) || p.Equals( "tpinc_nocd", StringComparison.OrdinalIgnoreCase ) || p.Equals( "noCD Crack", StringComparison.OrdinalIgnoreCase ) ) )
		return "Mixed-media fix variant excluded from retail evidence";
	return null;
}

static int ProbePriority( string path )
{
	var name = Path.GetFileName( path );
	if ( name.Equals( "terrain.wad", StringComparison.OrdinalIgnoreCase ) || name.Equals( "Standard.sam", StringComparison.OrdinalIgnoreCase ) ) return 0;
	if ( path.Contains( "features", StringComparison.OrdinalIgnoreCase ) || name.Contains( "totem", StringComparison.OrdinalIgnoreCase ) ) return 1;
	return 2;
}

static object Probe( string path, byte[] data, string family, Dictionary<string, int> budget )
{
	try
	{
		using var stream = new MemoryStream( data, false );
		switch ( family )
		{
			case "MD2":
				var model = new ModelFile( stream );
				return new
				{
					status = "parsed-existing-reader",
					reader = "ModelFile",
					model.VersionMajor,
					model.VersionMinor,
					kind = model.Kind.ToString(),
					model.Counts,
					meshCount = model.Meshes.Count,
					nodeCount = model.Nodes.Count,
					textureCount = model.Textures.Count,
					hasAnimation = model.Clip != null,
					hasHeightfield = model.Heightfield != null
				};
			case "RSE":
				var script = new RideScriptFile( stream );
				return new
				{
					status = "parsed-existing-reader",
					reader = "RideScriptFile",
					script.VariableCount,
					script.StackSize,
					script.TimeSlice,
					script.LimboSize,
					script.BounceSize,
					script.WalkSize,
					script.CodeWordCount,
					script.StringBlobLength,
					instructionCount = script.Instructions.Count,
					variableNames = script.VariableNames,
					opcodeCounts = script.Instructions.GroupBy( x => x.Opcode ).ToDictionary( x => x.Key.ToString(), x => x.Count() )
				};
			case "MAP":
				var map = new MapFile( stream );
				return new
				{
					status = "parsed-existing-reader",
					reader = "MapFile",
					map.Width,
					map.Height,
					map.OpaqueHeaderValues,
					cellCount = map.Cells.Length,
					cellValueCounts = map.Cells.GroupBy( x => x ).ToDictionary( x => x.Key.ToString(), x => x.Count() )
				};
			case "SAM":
				if ( data.Any( b => b < 32 && b is not (9 or 10 or 13) || b > 126 ) )
					throw new InvalidDataException( "Not plain ASCII SAM candidate; existing parser is not used on binary data." );
				var settings = new SettingsFile( stream );
				return new
				{
					status = "parsed-existing-reader",
					reader = "SettingsFile/SAMParser",
					limitation = "Syntactic key/value acceptance, not schema compatibility",
					entryCount = settings.Entries.Count,
					keys = settings.Entries.Select( x => x.Key ).Distinct().Order().ToArray(),
					entriesSha256 = Hash( Encoding.UTF8.GetBytes( string.Join( "\n", settings.Entries.Select( x => x.Key + "=" + x.Value ) ) ) )
				};
			case "SDT":
				using ( var bank = new SdtArchive( stream ) )
					return new
					{
						status = bank.SkippedEntries.Count == 0 ? "parsed-existing-reader" : "partial-reader-result",
						reader = "SdtArchive",
						declaredEntries = data.Length >= 4 ? U32( data, 0 ) : 0,
						parsedEntries = bank.soundFiles.Count,
						skippedEntries = bank.SkippedEntries,
						headerSizes = bank.soundFiles.GroupBy( x => x.Header ).ToDictionary( x => x.Key.ToString(), x => x.Count() ),
						limitation = "Entry/header parsing only; no audio payload decode or playback"
					};
			case "COS":
				using ( var save = new SaveReader( stream ) )
					return new { status = "existing-envelope-inspected", reader = "SaveReader.Inspect", container = save.Inspect(), limitation = "Save envelope only; no COS payload decoder or coaster implementation" };
			case "WAD":
				HashSet<string> oversizedMembers;
				try { oversizedMembers = ValidateWad( data ); }
				catch ( InvalidDataException error ) { return new { status = "preflight-rejected", readerInvoked = false, reason = error.Message }; }
				using ( var wad = new WadArchive( stream ) )
				{
					var members = new List<object>();
					var leaves = Walk( wad.Root, "" ).ToArray();
					foreach ( var (name, file) in leaves )
					{
						var memberFamily = ProbeFamily( "unrecognized", Path.GetExtension( name ).ToLowerInvariant() );
						if ( memberFamily == null || memberFamily is "WAD" or "PE" ) continue;
						var key = "member-" + memberFamily;
						if ( budget.GetValueOrDefault( key ) >= 24 ) continue;
						budget[key] = budget.GetValueOrDefault( key ) + 1;
						if ( oversizedMembers.Contains( file.Name ?? "" ) )
						{
							members.Add( new { path = name, status = "unverified-limit", reason = "Member exceeds conservative compressed/decompressed read budget", readerInvoked = false } );
							continue;
						}
						try
						{
							var bytes = file.GetData();
							if ( bytes.Length > 16 * 1024 * 1024 ) throw new InvalidDataException( "Decompressed member exceeds 16 MiB probe bound." );
							var sig = Signature( bytes );
							members.Add( new
							{
								path = name,
								bytes = bytes.Length,
								sha256 = Hash( bytes ),
								signature = sig,
								readerResult = Probe( path + "!" + name, bytes, ProbeFamily( sig, Path.GetExtension( name ).ToLowerInvariant() ) ?? memberFamily, budget )
							} );
						}
						catch ( Exception error ) when ( error is not OutOfMemoryException ) { members.Add( new { path = name, status = "reader-rejected", errorType = error.GetType().Name, reason = error.Message } ); }
						if ( file is WadArchiveFile entry ) entry.Free();
					}
					return new
					{
						status = "parsed-existing-reader",
						reader = "WadArchive",
						version = U32( data, 4 ),
						declaredEntries = U32( data, 72 ),
						fileListOffset = U32( data, 76 ),
						fileListBytes = U32( data, 80 ),
						parsedEntries = leaves.Length,
						extensionCounts = leaves.GroupBy( x => Path.GetExtension( x.Name ).ToLowerInvariant() ).ToDictionary( x => x.Key, x => x.Count() ),
						sampledMembers = members
					};
				}
			case "PE":
				using ( var pe = new PEReader( stream ) )
				{
					var headers = pe.PEHeaders;
					if ( headers.PEHeader == null ) throw new InvalidDataException( "MZ candidate is not a PE image." );
					return new
					{
						status = "parsed-standard-header",
						reader = "System.Reflection.PortableExecutable.PEReader",
						machine = headers.CoffHeader.Machine.ToString(),
						headers.CoffHeader.NumberOfSections,
						headers.CoffHeader.TimeDateStamp,
						flags = headers.CoffHeader.Characteristics.ToString(),
						optionalMagic = headers.PEHeader.Magic.ToString(),
						headers.PEHeader.MajorLinkerVersion,
						headers.PEHeader.MinorLinkerVersion,
						headers.PEHeader.MajorImageVersion,
						headers.PEHeader.MinorImageVersion,
						headers.PEHeader.AddressOfEntryPoint,
						importDirectoryRva = headers.PEHeader.ImportTableDirectory.RelativeVirtualAddress,
						importDirectoryBytes = headers.PEHeader.ImportTableDirectory.Size,
						sections = headers.SectionHeaders.Select( x => new { x.Name, x.VirtualAddress, x.VirtualSize, x.PointerToRawData, x.SizeOfRawData, flags = x.SectionCharacteristics.ToString() } )
					};
				}
			default: return new { status = "unverified", reason = $"No validated {family} decoder; extension is not a format proof", prefixSha256 = Hash( data.AsSpan( 0, Math.Min( data.Length, 64 ) ) ) };
		}
	}
	catch ( Exception error ) when ( error is not OutOfMemoryException )
	{
		return new
		{
			status = "reader-rejected",
			reader = family,
			errorType = error.GetType().Name,
			reason = error.Message,
			modelHeaderVersion = Signature( data ) == "M3D2" && data.Length >= 12 ? new { major = U32( data, 4 ), minor = U32( data, 8 ) } : null
		};
	}
}

static HashSet<string> ValidateWad( byte[] data )
{
	var oversized = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
	if ( data.Length < 88 || !data.AsSpan().StartsWith( "DWFB"u8 ) ) throw new InvalidDataException( "Missing bounded DWFB header." );
	var count = U32( data, 72 );
	if ( count > 4096 || 88L + count * 40L > data.Length ) throw new InvalidDataException( "WAD directory exceeds bounded entry count/span." );
	for ( var i = 0; i < count; i++ )
	{
		var offset = 88 + i * 40;
		var name = U32( data, offset + 4 ); var nameBytes = U32( data, offset + 8 );
		var payload = U32( data, offset + 12 ); var packedBytes = U32( data, offset + 16 );
		if ( nameBytes is 0 or > 4096 || (long)name + nameBytes > data.Length || (long)payload + packedBytes > data.Length )
			throw new InvalidDataException( "WAD name or member span exceeds bounded input." );
		if ( packedBytes > 16 * 1024 * 1024 || U32( data, offset + 24 ) > 16 * 1024 * 1024 || U32( data, offset + 20 ) == 4 && packedBytes > 65536 )
			oversized.Add( Encoding.ASCII.GetString( data, (int)name, (int)nameBytes - 1 ).Split( '\\' )[^1] );
	}
	return oversized;
}

static IEnumerable<(string Name, ArchiveFile File)> Walk( ArchiveDirectory directory, string prefix )
{
	foreach ( var item in directory.Children )
	{
		if ( item is ArchiveDirectory child ) foreach ( var leaf in Walk( child, prefix + child.Name + "/" ) ) yield return leaf;
		else if ( item is ArchiveFile file ) yield return (prefix + item.Name, file);
	}
}

static uint U32( ReadOnlySpan<byte> bytes, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( bytes[offset..] );
static string Hash( ReadOnlySpan<byte> bytes ) => Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant();

static void SelfTest()
{
	var budget = new Dictionary<string, int>();
	if ( Signature( "TP2M"u8 ) != "TP2M" || ProbeFamily( "TP2M", ".rse" ) != "MAP" ) throw new Exception( "Signature must take priority over extension." );
	if ( ProbeFamily( "unrecognized", ".cos" ) != "COS" ) throw new Exception( "Unknown COS must remain unverified." );
	if ( Exclusion( "serial.txt" ) == null || Exclusion( "WIN10FIX+NOCDFIX/Game.exe" ) == null || Exclusion( "Data/levels/Standard.sam" ) != null ) throw new Exception( "Retail/user exclusions failed." );
	var sam = JsonSerializer.Serialize( Probe( "x.sam", Encoding.ASCII.GetBytes( "A 1\nB 2\n" ), "SAM", budget ) );
	if ( !sam.Contains( "parsed-existing-reader" ) || !sam.Contains( "\"entryCount\":2" ) ) throw new Exception( "Existing SAM reader did not accept the fixture." );
	if ( !JsonSerializer.Serialize( Probe( "x.sam", new byte[] { 0, 255 }, "SAM", budget ) ).Contains( "reader-rejected" ) ) throw new Exception( "Binary SAM candidate must be rejected." );
	var invalid = new byte[88]; "DWFB"u8.CopyTo( invalid ); BinaryPrimitives.WriteUInt32LittleEndian( invalid.AsSpan( 72 ), uint.MaxValue );
	try { ValidateWad( invalid ); throw new Exception( "Unbounded WAD count was accepted." ); } catch ( InvalidDataException ) { }
	if ( !JsonSerializer.Serialize( Probe( "x.exe", "MZ"u8.ToArray(), "PE", budget ) ).Contains( "reader-rejected" ) ) throw new Exception( "Truncated PE must be rejected." );
	if ( !JsonSerializer.Serialize( Probe( "x.rse", "RSSEQ"u8.ToArray(), "RSE", budget ) ).Contains( "reader-rejected" ) ) throw new Exception( "Truncated RSE must be rejected." );
	var payload = Encoding.ASCII.GetBytes( "A 1\n" );
	var wad = new byte[136 + payload.Length];
	"DWFB"u8.CopyTo( wad ); BinaryPrimitives.WriteUInt32LittleEndian( wad.AsSpan( 72 ), 1 );
	BinaryPrimitives.WriteUInt32LittleEndian( wad.AsSpan( 92 ), 128 ); BinaryPrimitives.WriteUInt32LittleEndian( wad.AsSpan( 96 ), 8 );
	BinaryPrimitives.WriteUInt32LittleEndian( wad.AsSpan( 100 ), 136 ); BinaryPrimitives.WriteUInt32LittleEndian( wad.AsSpan( 104 ), (uint)payload.Length );
	"one.sam\0"u8.CopyTo( wad.AsSpan( 128 ) ); payload.CopyTo( wad.AsSpan( 136 ) );
	var parsed = JsonSerializer.Serialize( Probe( "fixture.wad", wad, "WAD", budget ) );
	if ( !parsed.Contains( "WadArchive" ) || !parsed.Contains( "SettingsFile/SAMParser" ) || !parsed.Contains( "\"entryCount\":1" ) ) throw new Exception( "Existing WAD/member readers did not accept the synthetic fixture." );
	Console.WriteLine( "Nine signature, actual WAD/member readers, exclusion and bounds cases passed." );
}
