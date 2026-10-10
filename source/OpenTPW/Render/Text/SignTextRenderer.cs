namespace OpenTPW;

/// <summary>A rendered line of in-world text: the GPU texture plus its pixel size and baseline row.</summary>
public sealed record SignTextTexture( Texture Texture, int Width, int Height, int BaselineY, SignTextMetrics Metrics );

/// <summary>
/// In-world TrueType text for park gates and ride/shop signs (docs/COMPATIBILITY.md), drawn with the
/// original fonts read in memory from <c>Data/fonts.wad</c>. For the rides/frontend slices:
/// <list type="bullet">
/// <item><see cref="RenderText"/>: string + font name (file or family) + em size -> texture.</item>
/// <item><see cref="CreateQuad"/>: a textured quad model for a <see cref="ModelEntity"/>.</item>
/// <item><see cref="RenderSign"/>: an object's <c>.sgn</c> text slots + lines -> the
/// <c>sign1</c>/<c>sign2</c> textures that replace the placeholder textures on its model.</item>
/// </list>
/// The CPU parts (<see cref="SignTextLayout"/>, <see cref="SignCanvas"/>) are testable without a GPU.
/// </summary>
public sealed class SignTextRenderer
{
	private static SignTextRenderer? shared;

	public SignTextRenderer( SignFontLibrary fonts )
	{
		Fonts = fonts ?? throw new ArgumentNullException( nameof( fonts ) );
	}

	/// <summary>The renderer over the game's <c>fonts.wad</c> (loaded on first use).</summary>
	public static SignTextRenderer Shared => shared ??= new SignTextRenderer( SignFontLibrary.LoadFromGameData() );

	public SignFontLibrary Fonts { get; }

	/// <summary>Renders one line. <paramref name="emPixels"/> is the em height in texels (GDI negative lfHeight).</summary>
	public SignTextTexture RenderText( string text, string fontName, float emPixels, Color? color = null, int padding = 2 )
	{
		var font = Fonts.Get( fontName );
		var tint = color ?? Color.White;
		static byte Channel( float value ) => (byte)Math.Round( Math.Clamp( value, 0, 1 ) * 255 );
		var (rgba, width, height, baseline) = SignTextLayout.RenderRgba( font, text, emPixels, (Channel( tint.R ), Channel( tint.G ), Channel( tint.B )), padding: padding );
		var metrics = SignTextLayout.Measure( font, text, emPixels );
		return new SignTextTexture( new Texture( rgba, width, height ), width, height, baseline, metrics );
	}

	/// <summary>
	/// A quad of <paramref name="worldHeight"/> engine units (width from the texture aspect), centred on
	/// the origin in the X/Z plane (Z up) and facing -Y; position it with <see cref="ModelEntity.TransformOverride"/>.
	/// </summary>
	public static Model CreateQuad( SignTextTexture text, float worldHeight )
	{
		var halfHeight = worldHeight / 2;
		var halfWidth = halfHeight * text.Width / Math.Max( 1, text.Height );
		var normal = new Vector3( 0, -1, 0 );
		var vertices = new[]
		{
			new Vertex( new Vector3( -halfWidth, 0, -halfHeight ), normal, new Vector2( 0, 0 ) ),
			new Vertex( new Vector3( halfWidth, 0, -halfHeight ), normal, new Vector2( 1, 0 ) ),
			new Vertex( new Vector3( -halfWidth, 0, halfHeight ), normal, new Vector2( 0, 1 ) ),
			new Vertex( new Vector3( halfWidth, 0, halfHeight ), normal, new Vector2( 1, 1 ) ),
		};
		return new Model( vertices, new uint[] { 0, 2, 3, 3, 1, 0 }, CreateMaterial( text.Texture ) );
	}

	/// <summary>
	/// Composes an object's sign (lines in slot order) and returns the textures for its <c>sign1</c>
	/// (left half) and <c>sign2</c> (right half) material slots.
	/// </summary>
	public (Texture Left, Texture Right, IReadOnlyList<string> Diagnostics, int TextPixelCount) RenderSign( SignFile sign, IReadOnlyList<string?> lines, (byte R, byte G, byte B, byte A) background )
	{
		var composed = ComposeSign( sign, lines, background );
		return (new Texture( composed.Left, SignCanvas.HalfWidth, SignCanvas.Height ), new Texture( composed.Right, SignCanvas.HalfWidth, SignCanvas.Height ), composed.Diagnostics, composed.TextPixelCount);
	}

	/// <summary>The GPU-free part of <see cref="RenderSign"/>: the RGBA bytes of the left (<c>sign1</c>) and right (<c>sign2</c>) halves.</summary>
	public (byte[] Left, byte[] Right, IReadOnlyList<string> Diagnostics, int TextPixelCount) ComposeSign( SignFile sign, IReadOnlyList<string?> lines, (byte R, byte G, byte B, byte A) background )
	{
		var diagnostics = new List<string>();
		var canvas = SignCanvas.Compose( sign, Fonts, lines, background, diagnostics );
		var textPixels = CountChangedPixels( canvas, background );
		var (left, right) = SignCanvas.SplitHalves( canvas );
		return (left, right, diagnostics, textPixels);
	}

	/// <summary>Reads the first <c>*.sgn</c> member of an object archive such as <c>/levels/jungle/features/gates</c>.</summary>
	public static SignFile? LoadSignFile( string archivePath, BaseFileSystem? fileSystem = null )
	{
		fileSystem ??= FileSystem;
		var member = fileSystem.GetFiles( archivePath ).FirstOrDefault( file => file.EndsWith( ".sgn", StringComparison.OrdinalIgnoreCase ) );
		if ( member == null )
			return null;
		var path = $"{archivePath.TrimEnd( '/' )}/{Path.GetFileName( member )}";
		// Hash-keyed in-memory data corrections (e.g. sign-font-substitution) apply only when enabled.
		return new SignFile( CompatibilityRuntime.Correct( path, fileSystem.ReadAllBytes( path ) ) );
	}

	internal static Material CreateMaterial( params Texture[] textures )
	{
		var slots = new Texture[16];
		for ( var i = 0; i < slots.Length; i++ )
			slots[i] = textures[Math.Min( i, textures.Length - 1 )];
		var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
		material.Set( "Color", slots );
		return material;
	}

	private static int CountChangedPixels( byte[] canvas, (byte R, byte G, byte B, byte A) background )
	{
		var count = 0;
		for ( var i = 0; i < canvas.Length; i += 4 )
		{
			if ( canvas[i] != background.R || canvas[i + 1] != background.G || canvas[i + 2] != background.B || canvas[i + 3] != background.A )
				count++;
		}
		return count;
	}
}
