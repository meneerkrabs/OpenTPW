using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>
/// Texture of a UI draw: a solid colour (all null), a BF4 font atlas drawn at integer
/// <see cref="FontScale"/> (see <see cref="FontSmoothing"/>), an original image from the game file
/// system (pink 255/0/255 is transparent, linear filtered), a texture rendered this frame by its
/// owner (the park advisor; linear filtered, not disposed by the UI), or a <see cref="UiDepthImage"/> the
/// renderer draws first (the model previews).
/// </summary>
public sealed record UiTexture( string? ImagePath, FontAtlas? Atlas, int FontScale = 1, Veldrid.Texture? Rendered = null, UiDepthImage? DepthImage = null, UiTitleBitmap? Title = null )
{
	/// <summary>A composed title bitmap (<see cref="UiTitleBitmap"/>) drawn at text scale <paramref name="scale"/>.</summary>
	public static UiTexture Of( UiTitleBitmap title, int scale ) => new( null, null, Math.Max( 1, scale ), Title: title );
	public static UiTexture Of( UiDepthImage image ) => new( null, null, DepthImage: image );
	public static UiTexture Of( Veldrid.Texture rendered ) => new( null, null, Rendered: rendered );
	public static readonly UiTexture Solid = new( null, null );
	public static UiTexture Image( string path ) => images.GetOrAdd( path, static path => new( path, null ) );
	// Interface models name their images every frame; one record per path keeps that allocation-free.
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, UiTexture> images = new();
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
	public UiTexture Texture { get; private set; }
	public List<UiVertex> Vertices { get; } = new();

	/// <summary>Reuses this draw (and its vertex list's capacity) for another texture.</summary>
	internal void Reset( UiTexture texture )
	{
		Texture = texture;
		Vertices.Clear();
	}
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
	// Draws of earlier frames, reused in order so the vertex lists keep their capacity from frame to frame.
	private readonly List<UiDraw> pool = new();
	private readonly List<UiGlyphQuad> glyphs = new();
	private readonly List<(UiRect Rect, string Text)> texts = new();
	private readonly List<UiTextDraw> textDraws = new();
	private readonly List<UiDepthImage> depthImages = new();

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
		depthImages.Clear();
	}

	private UiDraw Current( UiTexture texture )
	{
		if ( draws.Count > 0 && Equals( draws[^1].Texture, texture ) )
			return draws[^1];
		UiDraw draw;
		if ( draws.Count < pool.Count )
		{
			draw = pool[draws.Count];
			draw.Reset( texture );
		}
		else
		{
			draw = new UiDraw( texture );
			pool.Add( draw );
		}
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

	/// <summary>Adds a depth-tested image (see <see cref="UiDepthImage"/>) as a quad at its rectangle.</summary>
	public void AddDepthImage( UiDepthImage image )
	{
		depthImages.Add( image );
		AddQuad( UiTexture.Of( image ), image.Rect, NVector2.Zero, NVector2.One, RgbaByte.White );
	}

	/// <summary>The depth-tested images of this frame, in drawing order.</summary>
	public IReadOnlyList<UiDepthImage> DepthImages => depthImages;

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

	/// <summary>The title control's copy offset in text pixels: the copy sits this far to the left and below the string (rectangle (L-2, T+2, R-2, B+2), 0x491dd0-0x491dfb).</summary>
	public const int TitleShadowOffset = 2;
	/// <summary>The colour alpha of Theme Park World's title copy (0x491da1-0x491e1a).</summary>
	public const byte TitleCopyAlpha = 0x80;
	/// <summary>The colour alpha of Theme Park Inc's title copy (0x5162d2-0x5162f4).</summary>
	public const byte TpiTitleCopyAlpha = 0xC8;

	/// <summary>
	/// Draws a title control's text as the original does: the grey copy (128,128,128, colour alpha <paramref name="copyAlpha"/>) two text pixels to the left and below the string and then the white string
	/// are written into one bitmap (<see cref="UiTitleBitmap"/>, the string's texels replacing the copy's), which is drawn as one quad modulated by <paramref name="color"/>. The string's glyphs are
	/// recorded in <see cref="Glyphs"/> and <see cref="TextDraws"/> at their drawn positions, without quads of their own.
	/// </summary>
	// [BIN:TPW-EXE:0x491ab0 title control] the grey copy (128,128,128,0x80) on (L-2, T+2, R-2, B+2), then the white string, both written into the control's bitmap and modulated by the control colour
	// [BIN:TPI-EXE:0x515fe0 title control] the same with the colour alpha 0xc8
	// [BIN:TPW-EXE:0x48f3d0 control bitmap] the bitmap of every text control, title or plain (slot +0x28 of the vtables 0x6ff530 and 0x6ff320, 0x588940), is the control's box: [control+0xa8] x [control+0xaa] from the rectangle [+0x18..+0x1e] (0x48f456-0x48f4c5), placed at the box's top-left (0x491ab0 via 0x659dac), so both passes and the whole string are cut off at the box
	public void AddTitleText( FontAtlas atlas, TextLayoutResult layout, int x, int y, RgbaByte color, byte copyAlpha, UiRect clip, int scale = 1, string? text = null )
	{
		scale = Math.Max( 1, scale );
		RecordText( atlas, layout, x, y, color, scale, text );
		if ( UiTitleBitmap.Get( atlas, layout, copyAlpha, scale ) is not { } bitmap )
			return;
		var full = new UiRect( x + bitmap.Left * scale, y + bitmap.Top * scale, bitmap.TextWidth * scale, bitmap.TextHeight * scale );
		if ( !TryClip( full, clip, out var drawn ) )
			return;
		AddQuad( bitmap.Texture, drawn, new NVector2( (drawn.X - full.X) / full.Width, (drawn.Y - full.Y) / full.Height ), new NVector2( (drawn.Right - full.X) / full.Width, (drawn.Bottom - full.Y) / full.Height ), color );
	}

	/// <summary>The part of <paramref name="full"/> inside <paramref name="clip"/>; false when nothing of it is inside.</summary>
	private static bool TryClip( UiRect full, UiRect clip, out UiRect drawn )
	{
		var left = Math.Max( full.X, MathF.Round( clip.X ) );
		var top = Math.Max( full.Y, MathF.Round( clip.Y ) );
		var right = Math.Min( full.Right, MathF.Round( clip.Right ) );
		var bottom = Math.Min( full.Bottom, MathF.Round( clip.Bottom ) );
		drawn = new UiRect( left, top, right - left, bottom - top );
		return right > left && bottom > top;
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
		AddGlyphs( atlas, layout, x, y, color, scale, texture );
		RecordTextDraw( layout, x, y, scale, text, firstGlyph );
	}

	private void RecordText( FontAtlas atlas, TextLayoutResult layout, int x, int y, RgbaByte color, int scale, string? text )
	{
		var firstGlyph = glyphs.Count;
		AddGlyphs( atlas, layout, x, y, color, scale, null );
		RecordTextDraw( layout, x, y, scale, text, firstGlyph );
	}

	private void AddGlyphs( FontAtlas atlas, TextLayoutResult layout, int x, int y, RgbaByte color, int scale, UiTexture? texture )
	{
		// indexed rather than foreach: the layout's list is read through its interface, whose enumerator is allocated on every call
		var layoutGlyphs = layout.Glyphs;
		for ( var index = 0; index < layoutGlyphs.Count; index++ )
		{
			var glyph = layoutGlyphs[index];
			var quad = new UiGlyphQuad( atlas, x + glyph.X * scale, y + glyph.Y * scale, glyph.Glyph.Width * scale, glyph.Glyph.Height * scale, glyph.Glyph.X, glyph.Glyph.Y, scale, color );
			glyphs.Add( quad );
			if ( texture == null )
				continue;
			AddQuad( texture, new UiRect( quad.X, quad.Y, quad.Width, quad.Height ),
				new NVector2( (float)glyph.Glyph.X / atlas.Width, (float)glyph.Glyph.Y / atlas.Height ),
				new NVector2( (float)(glyph.Glyph.X + glyph.Glyph.Width) / atlas.Width, (float)(glyph.Glyph.Y + glyph.Glyph.Height) / atlas.Height ), color );
		}
	}

	private void RecordTextDraw( TextLayoutResult layout, int x, int y, int scale, string? text, int firstGlyph )
	{
		if ( text != null )
		{
			var rect = new UiRect( x, y, layout.Width * scale, layout.Height * scale );
			texts.Add( (rect, text) );
			textDraws.Add( new( rect, text, firstGlyph, glyphs.Count - firstGlyph ) );
		}
	}
}
