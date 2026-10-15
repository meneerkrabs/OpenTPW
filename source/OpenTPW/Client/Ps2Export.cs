using OpenTPW.Online.Packages;
using StbImageSharp;

namespace OpenTPW;

/// <summary>
/// <c>--export-ps2 &lt;PS2 DATA directory&gt; &lt;output directory&gt;</c>: makes the PS2 version's data viewable
/// (docs/PS2.md). Writes every texture as PNG (decoded from the truecolor TGA that accompanies each
/// SSH) under <c>&lt;output&gt;/&lt;archive&gt;/&lt;path&gt;.png</c>, plus <c>index.tsv</c> listing every archive member
/// with its size and, for SSH files, the image records. Reads the disc data only; nothing is written
/// next to it.
/// </summary>
// [EXT:ps2-data] viewing aid for the PS2 version's data, not original behaviour
public static class Ps2Export
{
	public static void Run( string dataDirectory, string outputDirectory )
	{
		var archives = Directory.EnumerateFiles( dataDirectory, "*", SearchOption.TopDirectoryOnly )
			.Where( path => path.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ).OrderBy( path => path, StringComparer.Ordinal ).ToArray();
		if ( archives.Length == 0 )
			throw new DirectoryNotFoundException( $"No PS2 .WAD archives in {dataDirectory}; pass the disc's DATA directory." );
		Directory.CreateDirectory( outputDirectory );
		using var index = new StreamWriter( Path.Combine( outputDirectory, "index.tsv" ) );
		index.WriteLine( "archive\tpath\tsize\tdetail" );
		int files = 0, textures = 0, failed = 0;
		foreach ( var archivePath in archives )
		{
			using var stream = File.OpenRead( archivePath );
			var header = new byte[4];
			stream.ReadExactly( header );
			stream.Position = 0;
			if ( !FknlArchive.IsFknl( header ) )
			{
				Log.Warning( $"{Path.GetFileName( archivePath )} is not an FKNL archive; skipped." );
				continue;
			}
			using var archive = new FknlArchive( stream );
			var archiveName = Path.GetFileNameWithoutExtension( archivePath );
			foreach ( var path in archive.EnumerateFiles().OrderBy( path => path, StringComparer.OrdinalIgnoreCase ) )
			{
				files++;
				var file = (FknlArchiveFile)archive.GetFile( path );
				var detail = file.IsEmptyPlaceholder ? "empty placeholder" : "";
				try
				{
					if ( path.EndsWith( ".ssh", StringComparison.OrdinalIgnoreCase ) && !file.IsEmptyPlaceholder )
					{
						var ssh = new SshFile( file.GetData() );
						detail = string.Join( "; ", ssh.Images.Select( image => $"{image.Width}x{image.Height} type 0x{image.Type:X2}{(image.IsGmCompressed ? " GM" : "")} {image.Name}" ) );
					}
					else if ( path.EndsWith( ".tga", StringComparison.OrdinalIgnoreCase ) && !file.IsEmptyPlaceholder )
					{
						var image = ImageResult.FromMemory( file.GetData(), ColorComponents.RedGreenBlueAlpha );
						var target = Path.Combine( outputDirectory, archiveName, path.Replace( '\\', '/' ) + ".png" );
						Directory.CreateDirectory( Path.GetDirectoryName( target )! );
						File.WriteAllBytes( target, PngImage.EncodeRgba( image.Width, image.Height, image.Data ) );
						detail = $"{image.Width}x{image.Height} exported";
						textures++;
					}
				}
				catch ( Exception exception )
				{
					detail = $"error: {exception.Message}";
					failed++;
				}
				index.WriteLine( $"{archiveName}\t{path}\t{file.Size}\t{detail}" );
			}
		}
		Log.Trace( $"PS2 export: {files} files in {archives.Length} archives, {textures} textures written as PNG, {failed} could not be read; index at {Path.Combine( outputDirectory, "index.tsv" )}." );
	}
}
