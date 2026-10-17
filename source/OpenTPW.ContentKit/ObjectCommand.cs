using System.Text;
using StbImageSharp;

namespace OpenTPW.ContentKit;

/// <summary>What <see cref="ObjectBuilder.Build"/> needs.</summary>
/// <param name="GlbPath">The binary glTF the Blender exporter wrote (tools/blender).</param>
/// <param name="Name">The archive name: the folder's name and the stem of the main model, <c>&lt;name&gt;.MD2</c>.</param>
/// <param name="SamSource">A <c>.sam</c> file, or <see cref="Templates.SamTemplateName"/>.</param>
/// <param name="ScriptPath">An RSE text source, or null for the built-in placeholder pool script.</param>
/// <param name="OutDirectory">The object archive folder to write.</param>
public sealed record ObjectBuildOptions( string GlbPath, string Name, string OutDirectory, string SamSource = Templates.SamTemplateName, string? ScriptPath = null, string? DisplayName = null, bool Force = false );

/// <summary>
/// Builds one object archive folder from a glTF: the converted MD2, its textures as TGA, the settings and the assembled ride script, laid out like an
/// original object archive (<c>puzzle.MD2</c>, <c>Puzzle.sam</c>, <c>Puzzle.RSE</c>, <c>textures/</c>; <c>qickload.txt</c> and <c>.hmp</c> are optional in the original and not written).
/// </summary>
public static class ObjectBuilder
{
	public const int MaximumNameLength = 16;

	public static CommandResult Build( ObjectBuildOptions options )
	{
		var name = options.Name;
		if ( name.Length is 0 or > MaximumNameLength || !char.IsAsciiLetter( name[0] ) || name.Any( c => !(char.IsAsciiLetterOrDigit( c ) || c == '_') ) )
			throw new ArgumentException( $"The archive name '{name}' must be 1 to {MaximumNameLength} letters, digits or underscores and start with a letter." );
		var stem = name.ToLowerInvariant();
		var settingsName = char.ToUpperInvariant( name[0] ) + stem[1..];

		var scene = GltfScene.Read( options.GlbPath );
		var conversion = GltfModelConverter.Convert( scene, new GltfConversionOptions() );
		var warnings = new List<string>( conversion.Warnings );

		// Everything that can fail is produced before the first file is written.
		var modelBytes = conversion.Model.ToBytes();
		var textures = conversion.Textures.Select( texture => (texture.Name, Bytes: ConvertTexture( texture, warnings )) ).ToList();
		var samText = ReadSettings( options, settingsName );
		var script = AssembleScript( options.ScriptPath, conversion.Model, warnings );

		var directory = options.OutDirectory;
		var files = new List<(string Path, byte[] Bytes)> { ($"{stem}.MD2", modelBytes), ($"{settingsName}.sam", Encoding.Latin1.GetBytes( samText )), ($"{settingsName}.RSE", script) };
		files.AddRange( textures.Select( texture => ($"textures/{texture.Name}.tga", texture.Bytes) ) );
		var written = new List<string>();
		Output.Write( directory, files, options.Force, written );
		return new CommandResult( directory, written, warnings );
	}

	/// <summary>A PNG or JPEG image as a TGA of the corpus variant (<see cref="TgaWriter"/>).</summary>
	private static byte[] ConvertTexture( GltfConvertedTexture texture, List<string> warnings )
	{
		ImageResult image;
		try
		{
			image = ImageResult.FromMemory( texture.Image.Data, ColorComponents.RedGreenBlueAlpha );
		}
		catch ( Exception exception ) when ( exception is InvalidOperationException or IndexOutOfRangeException or ArgumentException )
		{
			throw new InvalidDataException( $"Image '{texture.Image.Name}' cannot be decoded as {texture.Image.MimeType}: {exception.Message}" );
		}
		if ( image.Width > TgaWriter.MaximumDimension || image.Height > TgaWriter.MaximumDimension )
			throw new InvalidDataException( $"Image '{texture.Image.Name}' is {image.Width} x {image.Height}; the content kit writes textures up to {TgaWriter.MaximumDimension} pixels on a side." );
		if ( !IsPowerOfTwo( image.Width ) || !IsPowerOfTwo( image.Height ) )
			warnings.Add( $"Texture '{texture.Name}' is {image.Width} x {image.Height}, not a power of two; every shipped model texture is (unverified whether the original renderer copes with others)." );
		return TgaWriter.Write( image.Width, image.Height, image.Data );
	}

	private static bool IsPowerOfTwo( int value ) => (value & (value - 1)) == 0;

	private static string ReadSettings( ObjectBuildOptions options, string settingsName )
	{
		string text;
		if ( string.Equals( options.SamSource, Templates.SamTemplateName, StringComparison.OrdinalIgnoreCase ) )
			text = Templates.RenderSam( options.DisplayName ?? settingsName );
		else
		{
			if ( !File.Exists( options.SamSource ) )
				throw new FileNotFoundException( $"The settings file '{options.SamSource}' does not exist (use 'template' for the built-in settings)." );
			text = Encoding.Latin1.GetString( File.ReadAllBytes( options.SamSource ) );
			if ( options.DisplayName != null )
				throw new ArgumentException( "--display-name only applies to the built-in template; edit Info.Name in your own .sam." );
		}
		var pairs = new SAMParser( text ).Parse();
		if ( !pairs.Any( pair => pair.Key == "Info.Id" ) )
			throw new InvalidDataException( "The settings have no Info.Id; the catalog only lists archives whose .sam has one." );
		return text;
	}

	private static byte[] AssembleScript( string? path, ModelBuilder model, List<string> warnings )
	{
		var source = path == null ? Templates.PoolScript : File.ReadAllText( path );
		byte[] bytes;
		try
		{
			bytes = RideScriptAssembler.Assemble( source );
		}
		catch ( RideScriptAssemblyException exception )
		{
			throw new InvalidDataException( $"{path ?? "built-in script"}: {exception.Message}" );
		}
		using var stream = new MemoryStream( bytes );
		var script = new RideScriptFile( stream );
		var heads = model.Nodes.Count( node => node.HeadId != null );
		var addsHeads = script.Instructions.Any( instruction => instruction.Opcode == (ushort)Opcode.ADDHEAD );
		if ( addsHeads && heads == 0 )
			warnings.Add( "The script runs ADDHEAD but the model has no head records (mark empties with opentpw_head); riders get no head." );
		return bytes;
	}
}

/// <summary>Writes the files of a command below its output folder, refusing to replace existing files unless asked to.</summary>
internal static class Output
{
	public static void Write( string directory, IReadOnlyList<(string Path, byte[] Bytes)> files, bool force, List<string> written )
	{
		var root = Path.GetFullPath( directory );
		foreach ( var (path, _) in files )
		{
			var target = Path.GetFullPath( Path.Combine( root, path ) );
			if ( !target.StartsWith( root + Path.DirectorySeparatorChar, StringComparison.Ordinal ) )
				throw new ArgumentException( $"The path '{path}' leaves the output folder." );
			if ( File.Exists( target ) && !force )
				throw new IOException( $"{target} already exists; pass --force to replace it." );
		}
		foreach ( var (path, bytes) in files )
		{
			var target = Path.Combine( root, path );
			Directory.CreateDirectory( Path.GetDirectoryName( target )! );
			File.WriteAllBytes( target, bytes );
			written.Add( path );
		}
	}
}

public static class ObjectCommand
{
	public static CommandResult Run( CommandOptions options )
	{
		var build = new ObjectBuildOptions( options.Require( "glb" ), options.Require( "name" ), options.Require( "out" ),
			options.Optional( "sam" ) ?? Templates.SamTemplateName, options.Optional( "script" ), options.Optional( "display-name" ), options.Has( "force" ) );
		options.RejectUnknown();
		return ObjectBuilder.Build( build );
	}
}
