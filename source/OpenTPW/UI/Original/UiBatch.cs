using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>
/// Texture of a UI draw: a solid colour (both null), a BF4 font atlas drawn at integer
/// <see cref="FontScale"/> (see <see cref="FontSmoothing"/>) or an original image from the game file
/// system (pink 255/0/255 is transparent, linear filtered).
/// </summary>
public sealed record UiTexture( string? ImagePath, FontAtlas? Atlas, int FontScale = 1 )
{
	public static readonly UiTexture Solid = new( null, null );
	public static UiTexture Image( string path ) => new( path, null );
	/// <summary>An image file on the host file system, e.g. a local art override.</summary>
	public static UiTexture HostImage( string path ) => new( UiImages.HostPrefix + path, null );
	public static UiTexture Font( FontAtlas atlas, int scale = 1 ) => new( null, atlas, Math.Max( 1, scale ) );
}

/// <summary>One UI vertex in framebuffer pixels (origin top-left).</summary>
public readonly record struct UiVertex( NVector2 Position, NVector2 TexCoords, RgbaByte Color );

/// <summary>A run of triangles that share one texture.</summary>
public sealed class UiDraw
{
	public UiDraw( UiTexture texture ) => Texture = texture;
	public UiTexture Texture { get; }
	public List<UiVertex> Vertices { get; } = new();
}

/// <summary>A placed glyph, kept so GPU readback of text can be checked against the atlas.</summary>
public readonly record struct UiGlyphQuad( FontAtlas Atlas, int X, int Y, int Width, int Height, int AtlasX, int AtlasY, int Scale, RgbaByte Color );

/// <summary>Associates one text draw with its exact glyph range, excluding underlying screens.</summary>
public readonly record struct UiTextDraw( UiRect Rect, string Text, int FirstGlyph, int GlyphCount );

/// <summary>
/// CPU list of UI triangles in draw order: original model frames, images, solid rectangles and BF4
/// text. <see cref="UiRenderer"/> draws it; tests and the smoke test inspect it without a GPU.
/// </summary>
public sealed class UiBatch
{
	private readonly List<UiDraw> draws = new();
	private readonly List<UiGlyphQuad> glyphs = new();
	private readonly List<(UiRect Rect, string Text)> texts = new();
	private readonly List<UiTextDraw> textDraws = new();

	public IReadOnlyList<UiDraw> Draws => draws;
	public IReadOnlyList<UiGlyphQuad> Glyphs => glyphs;
	/// <summary>Every text string drawn this frame with its pixel bounds (used by tests and the smoke test).</summary>
	public IReadOnlyList<(UiRect Rect, string Text)> Texts => texts;
	public IReadOnlyList<UiTextDraw> TextDraws => textDraws;

	public void Clear()
	{
		draws.Clear();
		glyphs.Clear();
		texts.Clear();
		textDraws.Clear();
	}

	private UiDraw Current( UiTexture texture )
	{
		if ( draws.Count > 0 && Equals( draws[^1].Texture, texture ) )
			return draws[^1];
		var draw = new UiDraw( texture );
		draws.Add( draw );
		return draw;
	}

	public void AddTriangle( UiTexture texture, UiVertex a, UiVertex b, UiVertex c )
	{
		var draw = Current( texture );
		draw.Vertices.Add( a );
		draw.Vertices.Add( b );
		draw.Vertices.Add( c );
	}

	public void AddQuad( UiTexture texture, UiRect rect, NVector2 uv0, NVector2 uv1, RgbaByte color )
	{
		if ( rect.Width <= 0 || rect.Height <= 0 )
			return;
		var topLeft = new UiVertex( new( rect.X, rect.Y ), uv0, color );
		var topRight = new UiVertex( new( rect.Right, rect.Y ), new( uv1.X, uv0.Y ), color );
		var bottomLeft = new UiVertex( new( rect.X, rect.Bottom ), new( uv0.X, uv1.Y ), color );
		var bottomRight = new UiVertex( new( rect.Right, rect.Bottom ), uv1, color );
		AddTriangle( texture, topLeft, bottomLeft, topRight );
		AddTriangle( texture, topRight, bottomLeft, bottomRight );
	}

	public void AddRectangle( UiRect rect, RgbaByte color ) => AddQuad( UiTexture.Solid, rect, NVector2.Zero, NVector2.One, color );

	public void AddImage( string path, UiRect rect, RgbaByte? tint = null ) =>
		AddQuad( UiTexture.Image( path ), rect, NVector2.Zero, NVector2.One, tint ?? RgbaByte.White );

	/// <summary>
	/// Draws an original model frame: its authored canvas coordinates are moved so that
	/// <paramref name="authoredCenter"/> lands on <paramref name="screenCenter"/>, scaled by
	/// <paramref name="scale"/> (pixels per canvas unit). Texture names resolve through
	/// <paramref name="resolveTexture"/> (null → untextured white).
	/// </summary>
	public void AddModelFrame( UiModelFrame frame, NVector2 authoredCenter, NVector2 screenCenter, float scale, Func<string, string?> resolveTexture, RgbaByte? tint = null )
	{
		var color = tint ?? RgbaByte.White;
		foreach ( var part in frame.Parts )
		{
			var path = part.TextureName.Length == 0 ? null : resolveTexture( part.TextureName );
			var texture = path == null ? UiTexture.Solid : UiTexture.Image( path );
			UiVertex Convert( int index )
			{
				var vertex = part.Vertices[index];
				return new UiVertex( screenCenter + (vertex.Position - authoredCenter) * scale, vertex.TexCoords, color );
			}
			for ( var index = 0; index + 2 < part.Indices.Count; index += 3 )
				AddTriangle( texture, Convert( part.Indices[index] ), Convert( part.Indices[index + 1] ), Convert( part.Indices[index + 2] ) );
		}
	}

	/// <summary>
	/// Draws BF4 text with its top-left at (<paramref name="x"/>, <paramref name="y"/>), each atlas texel
	/// covering <paramref name="scale"/>×<paramref name="scale"/> pixels at integer positions.
	/// </summary>
	public void AddText( FontAtlas atlas, TextLayoutResult layout, int x, int y, RgbaByte color, int scale = 1, string? text = null )
	{
		scale = Math.Max( 1, scale );
		var texture = UiTexture.Font( atlas, scale );
		var firstGlyph = glyphs.Count;
		foreach ( var glyph in layout.Glyphs )
		{
			var quad = new UiGlyphQuad( atlas, x + glyph.X * scale, y + glyph.Y * scale, glyph.Glyph.Width * scale, glyph.Glyph.Height * scale, glyph.Glyph.X, glyph.Glyph.Y, scale, color );
			glyphs.Add( quad );
			AddQuad( texture, new UiRect( quad.X, quad.Y, quad.Width, quad.Height ),
				new NVector2( (float)glyph.Glyph.X / atlas.Width, (float)glyph.Glyph.Y / atlas.Height ),
				new NVector2( (float)(glyph.Glyph.X + glyph.Glyph.Width) / atlas.Width, (float)(glyph.Glyph.Y + glyph.Glyph.Height) / atlas.Height ), color );
		}
		if ( text != null )
		{
			var rect = new UiRect( x, y, layout.Width * scale, layout.Height * scale );
			texts.Add( (rect, text) );
			textDraws.Add( new( rect, text, firstGlyph, glyphs.Count - firstGlyph ) );
		}
	}
}
