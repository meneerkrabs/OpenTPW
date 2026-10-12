using OpenTPW;

// Build-time generator for the browser's predecoded interface art (docs/WEB.md): decodes every shipped
// hero-art PNG with the same StbImageSharp the game uses and stages PredecodedUiArt siblings that the web
// build embeds instead of the PNGs. The whole set takes under a second, so this runs on every build rather
// than trusting timestamp tracking over a changing glob: an output file is rewritten only when its bytes
// change (keeping timestamps of untouched files, and with them cheap incremental compiles), and staged
// files whose source disappeared are removed. Content edits, additions and removals, and format or tool
// changes are all covered by re-running.
//
// Usage: PredecodeHeroArt --source <content/hero-art> --staging <directory>

if ( args is not [ "--source", var sourceArgument, "--staging", var stagingArgument ] )
{
	Console.Error.WriteLine( "usage: PredecodeHeroArt --source <content/hero-art> --staging <directory>" );
	return 2;
}
var source = Path.GetFullPath( sourceArgument );
var staging = Path.GetFullPath( stagingArgument );
var (predecoded, copied, rewritten, removed) = (0, 0, 0, 0);
var staged = new HashSet<string>( StringComparer.Ordinal );
foreach ( var file in Directory.EnumerateFiles( source, "*", SearchOption.AllDirectories ).Order( StringComparer.Ordinal ) )
{
	var relative = Path.GetRelativePath( source, file );
	// X.wct.png becomes its predecoded sibling; anything else in the tree (nothing today) is copied so the
	// staged directory keeps mirroring the source.
	if ( relative.EndsWith( ".wct.png", StringComparison.OrdinalIgnoreCase ) )
	{
		Stage( Path.Combine( staging, PredecodedUiArt.SiblingPath( relative )! ), PredecodedUiArt.EncodePng( File.ReadAllBytes( file ) ) );
		predecoded++;
	}
	else
	{
		Stage( Path.Combine( staging, relative ), File.ReadAllBytes( file ) );
		copied++;
	}
}
if ( Directory.Exists( staging ) )
{
	// Staged files whose source disappeared would otherwise ride along forever; empty directories go with them.
	foreach ( var file in Directory.EnumerateFiles( staging, "*", SearchOption.AllDirectories ) )
	{
		if ( !staged.Contains( Path.GetFullPath( file ) ) )
		{
			File.Delete( file );
			removed++;
		}
	}
	foreach ( var directory in Directory.EnumerateDirectories( staging, "*", SearchOption.AllDirectories ).OrderByDescending( path => path.Length ) )
		if ( !Directory.EnumerateFileSystemEntries( directory ).Any() )
			Directory.Delete( directory );
}
Console.WriteLine( $"predecode-hero-art: {predecoded} predecoded, {copied} copied, {rewritten} rewritten, {removed} removed." );
return 0;

void Stage( string path, byte[] contents )
{
	if ( File.Exists( path ) && File.ReadAllBytes( path ).AsSpan().SequenceEqual( contents ) )
	{
		staged.Add( Path.GetFullPath( path ) );
		return;
	}
	Directory.CreateDirectory( Path.GetDirectoryName( path )! );
	File.WriteAllBytes( path, contents );
	staged.Add( Path.GetFullPath( path ) );
	rewritten++;
}
