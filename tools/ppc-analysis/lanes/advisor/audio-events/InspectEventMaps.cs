using OpenTPW;
using System.Security.Cryptography;
using System.Text.Json;

// Parse supplied scripts with the existing strict reader; never run them.
if ( args.Length != 2 )
{
	Console.Error.WriteLine( "Usage: AudioEventAssets <private Data root> <external JSON report>" );
	return 2;
}
var root = Path.GetFullPath( args[0] );
var output = Path.GetFullPath( args[1] );
var directory = new DirectoryInfo( AppContext.BaseDirectory );
while ( directory != null && !File.Exists( Path.Combine( directory.FullName, ".git" ) ) &&
	!Directory.Exists( Path.Combine( directory.FullName, ".git" ) ) ) directory = directory.Parent;
if ( directory == null || output.StartsWith( directory.FullName + Path.DirectorySeparatorChar, StringComparison.Ordinal ) )
	throw new ArgumentException( "Write the interpreted corpus report outside the repository." );
var results = new List<object>();
foreach ( var path in Directory.EnumerateFiles( Path.Combine( root, "levels" ), "*.wad", SearchOption.AllDirectories ).Order( StringComparer.Ordinal ) )
{
	using var wad = new WadArchive( path );
	foreach ( var member in wad.GetFiles( "" ).Where( name => name.Equals( "EventMap.RSE", StringComparison.OrdinalIgnoreCase ) ) )
	{
		var data = wad.GetFile( member ).GetData();
		var script = new RideScriptFile( new MemoryStream( data ) );
		var assignments = script.Instructions.Where( instruction => instruction.Opcode == 3 &&
			instruction.Operands.Count == 2 && instruction.Operands[0].Kind == RideScriptOperandKind.Variable &&
			instruction.Operands[1].Kind == RideScriptOperandKind.Literal ).Select( instruction => new
			{
				Name = script.VariableNames[instruction.Operands[0].Value],
				Index = instruction.Operands[0].Value,
				Value = instruction.Operands[1].Value
			} ).ToArray();
		results.Add( new
		{
			Wad = Path.GetRelativePath( root, path ).Replace( '\\', '/' ),
			Member = member,
			Sha256 = Convert.ToHexString( SHA256.HashData( data ) ).ToLowerInvariant(),
			Variables = script.VariableNames,
			Assignments = assignments
		} );
	}
}
File.WriteAllText( output, JsonSerializer.Serialize( results, new JsonSerializerOptions { WriteIndented = true } ) );
Console.WriteLine( $"Parsed {results.Count} EventMap scripts without execution; interpreted report: {output}" );
return 0;
