using System.Diagnostics;
using System.Text.Json;
using OpenTPW.Online.Packages;
using StbImageSharp;

namespace OpenTPW;

/// <summary>Upscales every PNG in a directory into another directory, keeping file names.</summary>
public interface IImageUpscaler
{
	int Scale { get; }
	string Name { get; }
	string Model { get; }
	void Upscale( string inputDirectory, string outputDirectory );
}

/// <summary>
/// Runs a player-supplied <c>realesrgan-ncnn-vulkan</c> executable (Real-ESRGAN, BSD-3-Clause). OpenTPW
/// neither ships nor downloads it.
/// </summary>
public sealed class RealEsrganUpscaler( string executable, string model = "realesrgan-x4plus", int scale = 4 ) : IImageUpscaler
{
	public int Scale => scale;
	public string Name => "realesrgan-ncnn-vulkan";
	public string Model => model;

	public void Upscale( string inputDirectory, string outputDirectory )
	{
		if ( !File.Exists( executable ) )
			throw new FileNotFoundException( $"Upscaler not found: {executable}", executable );
		var start = new ProcessStartInfo( executable )
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			WorkingDirectory = Path.GetDirectoryName( Path.GetFullPath( executable ) )!
		};
		foreach ( var argument in new[] { "-i", inputDirectory, "-o", outputDirectory, "-n", model, "-s", scale.ToString( System.Globalization.CultureInfo.InvariantCulture ), "-f", "png" } )
			start.ArgumentList.Add( argument );
		using var process = Process.Start( start ) ?? throw new InvalidOperationException( $"Could not start {executable}." );
		var error = process.StandardError.ReadToEndAsync();
		process.StandardOutput.ReadToEnd();
		process.WaitForExit();
		if ( process.ExitCode != 0 )
			throw new InvalidOperationException( $"{Name} exited with {process.ExitCode}: {error.Result.Trim()}" );
	}
}

public sealed record TexturePackBuildOptions
{
	/// <summary>Textures whose smaller side is below this are kept original (too little detail to upscale).</summary>
	public int MinimumSize { get; init; } = 32;
	/// <summary>Only WADs below this data-relative directory (e.g. <c>levels/jungle</c>); empty for all.</summary>
	public string Subtree { get; init; } = "";
	/// <summary>Only interface art (needs an interface upscaler); world textures are left out.</summary>
	public bool InterfaceOnly { get; init; }
	/// <summary>Keep the existing pack's textures and add or replace the ones built now.</summary>
	public bool Merge { get; init; }
}

/// <summary>
/// Builds a <see cref="TexturePack"/> from the player's own installation. Each world texture is wrap-padded
/// before upscaling and cropped afterwards, so tiling textures stay seamless. Interface art (<c>ui/</c>)
/// stays original unless an interface upscaler is given (docs/TEXTURE-PACKS.md); it is edge-padded instead,
/// because UI textures are atlases of separate pieces, not tiles. The low-detail <c>stexture</c>/<c>ssharete</c>
/// variants, fonts, small textures and world textures with an exact magenta chroma key stay original. The pack is written to a temporary directory and
/// moved into place only when complete; <c>pack.json</c> is written last.
/// </summary>
// [EXT:texture-pack] Builder for the optional local upscaled texture pack; original files are only read.
public static class TexturePackBuilder
{
	public enum Skip { None, Interface, LowDetail, Small, ChromaKey, Unreadable, NotSelected }

	/// <summary>Interface art an interface upscaler may enlarge: <c>ui/</c> textures except the low-detail variants and fonts.</summary>
	public static bool IsUpscalableInterface( string gamePath )
	{
		var segments = gamePath.Replace( '\\', '/' ).TrimStart( '/' ).ToLowerInvariant().Split( '/' );
		return segments[0] == "ui" && !segments.Contains( "stexture" ) && !segments.Contains( "fonts" );
	}

	/// <summary>Skip reasons that follow from the path alone, so those textures are never decoded.</summary>
	public static Skip ClassifyPath( string gamePath )
	{
		var segments = gamePath.Replace( '\\', '/' ).TrimStart( '/' ).ToLowerInvariant().Split( '/' );
		if ( segments.Contains( "stexture" ) || segments.Contains( "ssharete" ) )
			return Skip.LowDetail;
		if ( segments[0] == "ui" || segments.Contains( "fonts" ) )
			return Skip.Interface;
		return Skip.None;
	}

	public static Skip Classify( string gamePath, TextureData texture, int minimumSize )
	{
		if ( ClassifyPath( gamePath ) is var byPath and not Skip.None )
			return byPath;
		if ( Math.Min( texture.Width, texture.Height ) < minimumSize )
			return Skip.Small;
		for ( var i = 0; i + 3 < texture.Data.Length; i += 4 )
			if ( texture.Data[i] == 255 && texture.Data[i + 1] == 0 && texture.Data[i + 2] == 255 )
				return Skip.ChromaKey;
		return Skip.None;
	}

	/// <summary>Wrap padding: an eighth of the smaller side, 2..8 pixels.</summary>
	public static int Padding( int width, int height ) => Math.Clamp( Math.Min( width, height ) / 8, 2, 8 );

	/// <summary>Surrounds an RGBA image with <paramref name="padding"/> pixels taken from the opposite edges.</summary>
	public static byte[] WrapPad( byte[] rgba, int width, int height, int padding )
	{
		var paddedWidth = width + 2 * padding;
		var paddedHeight = height + 2 * padding;
		var result = new byte[paddedWidth * paddedHeight * 4];
		for ( var y = 0; y < paddedHeight; y++ )
		{
			var sourceY = ((y - padding) % height + height) % height;
			for ( var x = 0; x < paddedWidth; x++ )
			{
				var sourceX = ((x - padding) % width + width) % width;
				Buffer.BlockCopy( rgba, (sourceY * width + sourceX) * 4, result, (y * paddedWidth + x) * 4, 4 );
			}
		}
		return result;
	}

	/// <summary>Surrounds an RGBA image with <paramref name="padding"/> copies of its edge pixels.</summary>
	public static byte[] ClampPad( byte[] rgba, int width, int height, int padding )
	{
		var paddedWidth = width + 2 * padding;
		var paddedHeight = height + 2 * padding;
		var result = new byte[paddedWidth * paddedHeight * 4];
		for ( var y = 0; y < paddedHeight; y++ )
		{
			var sourceY = Math.Clamp( y - padding, 0, height - 1 );
			for ( var x = 0; x < paddedWidth; x++ )
			{
				var sourceX = Math.Clamp( x - padding, 0, width - 1 );
				Buffer.BlockCopy( rgba, (sourceY * width + sourceX) * 4, result, (y * paddedWidth + x) * 4, 4 );
			}
		}
		return result;
	}

	/// <summary>Cuts the centre <paramref name="width"/>×<paramref name="height"/> region starting at (<paramref name="left"/>, <paramref name="top"/>).</summary>
	public static byte[] Crop( byte[] rgba, int sourceWidth, int left, int top, int width, int height )
	{
		var result = new byte[width * height * 4];
		for ( var y = 0; y < height; y++ )
			Buffer.BlockCopy( rgba, ((top + y) * sourceWidth + left) * 4, result, y * width * 4, width * 4 );
		return result;
	}

	/// <summary>Every <c>.wct</c> inside the installation's WADs, with the game path the engine loads it by.</summary>
	public static IEnumerable<(string GamePath, Func<TextureData> Load)> EnumerateGameTextures( string dataDirectory, string subtree = "" )
	{
		var data = Path.GetFullPath( dataDirectory );
		if ( Path.IsPathRooted( subtree ) || subtree.Replace( '\\', '/' ).Split( '/' ).Contains( ".." ) )
			throw new ArgumentException( $"The texture pack subtree must be a data-relative directory without '..', not '{subtree}'." );
		var root = Path.GetFullPath( Path.Combine( data, subtree ) );
		if ( !System.IO.Directory.Exists( root ) )
			yield break;
		var wads = System.IO.Directory.EnumerateFiles( root, "*", SearchOption.AllDirectories )
			.Where( path => path.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) )
			.OrderBy( path => path, StringComparer.Ordinal );
		foreach ( var wadPath in wads )
		{
			var relative = Path.GetRelativePath( data, wadPath ).Replace( '\\', '/' );
			var virtualRoot = relative[..^4].ToLowerInvariant();
			using var wad = new WadArchive( wadPath );
			// WadArchive splits member paths on the platform separator; pack keys always use '/'.
			var members = new List<(string Native, string Key)>();
			void Walk( string native, string key )
			{
				foreach ( var file in wad.GetFiles( native ) )
					if ( file.EndsWith( ".wct", StringComparison.OrdinalIgnoreCase ) )
						members.Add( (native.Length == 0 ? file : native + Path.DirectorySeparatorChar + file, key.Length == 0 ? file : $"{key}/{file}") );
				foreach ( var child in wad.GetDirectories( native ) )
					Walk( native.Length == 0 ? child : native + Path.DirectorySeparatorChar + child, key.Length == 0 ? child : $"{key}/{child}" );
			}
			Walk( "", "" );
			foreach ( var (native, key) in members )
			{
				var captured = native;
				yield return ($"{virtualRoot}/{key}".ToLowerInvariant(), () => new TextureFile( new MemoryStream( wad.GetFile( captured ).GetData() ) ).Data);
			}
		}
	}

	/// <summary>Builds the pack at <paramref name="packDirectory"/>, replacing an existing one only on success.</summary>
	/// <param name="interfaceUpscaler">Upscaler for interface art (same scale); null keeps interface art original.</param>
	public static TexturePackManifest Build( IEnumerable<(string GamePath, Func<TextureData> Load)> textures, string packDirectory,
		IImageUpscaler upscaler, TexturePackBuildOptions options, Action<string> log, IImageUpscaler? interfaceUpscaler = null )
	{
		if ( interfaceUpscaler != null && interfaceUpscaler.Scale != upscaler.Scale )
			throw new ArgumentException( $"The interface upscaler's scale ({interfaceUpscaler.Scale}) must match the world upscaler's ({upscaler.Scale})." );
		if ( options.InterfaceOnly && interfaceUpscaler == null )
			throw new ArgumentException( "An interface-only texture pack build needs an interface upscaler." );
		var work = Path.Combine( Path.GetTempPath(), $"opentpw-texture-pack-{Guid.NewGuid():N}" );
		var input = Path.Combine( work, "in" );
		var output = Path.Combine( work, "out" );
		var interfaceInput = Path.Combine( work, "ui-in" );
		var interfaceOutput = Path.Combine( work, "ui-out" );
		foreach ( var directory in new[] { input, output, interfaceInput, interfaceOutput } )
			System.IO.Directory.CreateDirectory( directory );
		var parent = Path.GetDirectoryName( Path.GetFullPath( packDirectory ) )!;
		System.IO.Directory.CreateDirectory( parent );
		var staging = Path.Combine( parent, $".{Path.GetFileName( packDirectory )}.building-{Guid.NewGuid():N}" );
		try
		{
			var queued = new List<(string Flat, string GamePath, int Width, int Height, int Padding, bool Interface)>();
			var skipped = new Dictionary<Skip, int>();
			foreach ( var (gamePath, load) in textures )
			{
				var interfaceArt = interfaceUpscaler != null && IsUpscalableInterface( gamePath );
				if ( options.InterfaceOnly && !interfaceArt )
				{
					skipped[Skip.NotSelected] = skipped.GetValueOrDefault( Skip.NotSelected ) + 1;
					continue;
				}
				if ( !interfaceArt && ClassifyPath( gamePath ) is var byPath and not Skip.None )
				{
					skipped[byPath] = skipped.GetValueOrDefault( byPath ) + 1;
					continue;
				}
				TextureData texture;
				try
				{
					texture = load();
				}
				catch ( Exception exception )
				{
					// A texture the decoder cannot read stays original; one bad member must not stop the pack.
					log( $"Skipping {gamePath}: {exception.Message}" );
					skipped[Skip.Unreadable] = skipped.GetValueOrDefault( Skip.Unreadable ) + 1;
					continue;
				}
				if ( gamePath.Replace( '\\', '/' ).Split( '/' ).Contains( ".." ) )
				{
					log( $"Skipping {gamePath}: path leaves the pack." );
					skipped[Skip.Unreadable] = skipped.GetValueOrDefault( Skip.Unreadable ) + 1;
					continue;
				}
				// Interface textures carry real alpha after decoding, so the chroma-key rule is for world textures only.
				var skip = interfaceArt ? (Math.Min( texture.Width, texture.Height ) < options.MinimumSize ? Skip.Small : Skip.None)
					: Classify( gamePath, texture, options.MinimumSize );
				if ( skip != Skip.None )
				{
					skipped[skip] = skipped.GetValueOrDefault( skip ) + 1;
					continue;
				}
				var padding = Padding( texture.Width, texture.Height );
				var flat = $"t{queued.Count:D6}.png";
				var padded = interfaceArt ? ClampPad( texture.Data, texture.Width, texture.Height, padding ) : WrapPad( texture.Data, texture.Width, texture.Height, padding );
				File.WriteAllBytes( Path.Combine( interfaceArt ? interfaceInput : input, flat ), PngImage.EncodeRgba( texture.Width + 2 * padding, texture.Height + 2 * padding, padded ) );
				queued.Add( (flat, gamePath, texture.Width, texture.Height, padding, interfaceArt) );
			}
			var interfaceCount = queued.Count( entry => entry.Interface );
			log( $"Upscaling {queued.Count - interfaceCount} textures {upscaler.Scale}x with {upscaler.Name} ({upscaler.Model})" +
				(interfaceUpscaler != null ? $" and {interfaceCount} interface textures with {interfaceUpscaler.Model}" : "") + "; kept original: " +
				string.Join( ", ", skipped.OrderBy( entry => entry.Key ).Select( entry => $"{entry.Value} {entry.Key}" ) ) + "." );
			if ( queued.Count > interfaceCount )
				upscaler.Upscale( input, output );
			if ( interfaceCount > 0 )
				interfaceUpscaler!.Upscale( interfaceInput, interfaceOutput );

			var texturesRoot = Path.Combine( staging, TexturePack.TexturesDirectoryName );
			var kept = 0;
			if ( options.Merge && TexturePack.Open( packDirectory, new List<string>() ) is { } existing )
			{
				if ( existing.Scale != upscaler.Scale )
					throw new InvalidOperationException( $"The existing pack is {existing.Scale}x, this build {upscaler.Scale}x; merge needs the same scale." );
				var existingRoot = Path.Combine( packDirectory, TexturePack.TexturesDirectoryName );
				foreach ( var file in System.IO.Directory.EnumerateFiles( existingRoot, "*", SearchOption.AllDirectories ) )
				{
					var target = Path.Combine( texturesRoot, Path.GetRelativePath( existingRoot, file ) );
					System.IO.Directory.CreateDirectory( Path.GetDirectoryName( target )! );
					File.Copy( file, target );
					kept++;
				}
				log( $"Kept {kept} textures of the existing pack." );
			}
			var written = 0;
			foreach ( var (flat, gamePath, width, height, padding, interfaceArt) in queued )
			{
				var upscaledPath = Path.Combine( interfaceArt ? interfaceOutput : output, flat );
				if ( !File.Exists( upscaledPath ) )
					throw new InvalidOperationException( $"{upscaler.Name} produced no output for {gamePath}; the existing pack is kept." );
				var image = ImageResult.FromMemory( File.ReadAllBytes( upscaledPath ), ColorComponents.RedGreenBlueAlpha );
				var scale = upscaler.Scale;
				if ( image.Width != (width + 2 * padding) * scale || image.Height != (height + 2 * padding) * scale )
					throw new InvalidOperationException( $"Upscaled {gamePath} is {image.Width}x{image.Height}, expected {(width + 2 * padding) * scale}x{(height + 2 * padding) * scale}; the existing pack is kept." );
				var cropped = Crop( image.Data, image.Width, padding * scale, padding * scale, width * scale, height * scale );
				var target = Path.Combine( texturesRoot, TexturePack.RelativeFileName( gamePath ) );
				System.IO.Directory.CreateDirectory( Path.GetDirectoryName( target )! );
				if ( options.Merge && kept > 0 && File.Exists( target ) )
					kept--;
				File.WriteAllBytes( target, PngImage.EncodeRgba( width * scale, height * scale, cropped ) );
				written++;
			}

			var manifest = new TexturePackManifest
			{
				Scale = upscaler.Scale,
				Upscaler = upscaler.Name,
				Model = upscaler.Model,
				InterfaceModel = interfaceUpscaler?.Model ?? "",
				Textures = written + kept,
				InterfaceTextures = interfaceCount,
				SkippedSmall = skipped.GetValueOrDefault( Skip.Small ),
				SkippedLowDetail = skipped.GetValueOrDefault( Skip.LowDetail ),
				SkippedInterface = skipped.GetValueOrDefault( Skip.Interface ),
				SkippedChromaKey = skipped.GetValueOrDefault( Skip.ChromaKey ),
				SkippedUnreadable = skipped.GetValueOrDefault( Skip.Unreadable ),
				MinimumSize = options.MinimumSize,
				Subtree = options.Subtree,
				CreatedUtc = DateTime.UtcNow
			};
			System.IO.Directory.CreateDirectory( texturesRoot );
			File.WriteAllText( Path.Combine( staging, TexturePack.ManifestFileName ), JsonSerializer.Serialize( manifest, new JsonSerializerOptions { WriteIndented = true } ) );

			if ( System.IO.Directory.Exists( packDirectory ) )
			{
				var old = Path.Combine( parent, $".{Path.GetFileName( packDirectory )}.old-{Guid.NewGuid():N}" );
				System.IO.Directory.Move( packDirectory, old );
				try
				{
					System.IO.Directory.Move( staging, packDirectory );
				}
				catch
				{
					// Put the previous pack back before reporting the failure.
					if ( !System.IO.Directory.Exists( packDirectory ) )
						System.IO.Directory.Move( old, packDirectory );
					throw;
				}
				System.IO.Directory.Delete( old, true );
			}
			else
				System.IO.Directory.Move( staging, packDirectory );
			log( $"Texture pack written to {packDirectory}: {written} textures built{(kept > 0 ? $", {kept} kept" : "")}." );
			return manifest;
		}
		finally
		{
			if ( System.IO.Directory.Exists( work ) )
				System.IO.Directory.Delete( work, true );
			if ( System.IO.Directory.Exists( staging ) )
				System.IO.Directory.Delete( staging, true );
		}
	}
}
