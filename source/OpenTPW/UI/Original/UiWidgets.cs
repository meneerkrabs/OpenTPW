using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

public enum UiAlign { Left, Center, Right }

/// <summary>Everything a screen needs to lay out and draw one frame.</summary>
public sealed class UiContext
{
	private readonly Dictionary<(FontAtlas, string, int), TextLayoutResult> layouts = new();

	public UiContext( UiStringTable strings, UiFonts fonts, UiModels models )
	{
		Strings = strings;
		this.fonts = fonts;
		Models = models;
	}

	public UiStringTable Strings { get; }
	private UiFonts fonts;
	/// <summary>Fonts of the tier that suits the current canvas.</summary>
	public UiFonts Fonts => fonts.Tier == Canvas.FontTier ? fonts : fonts = fonts.WithTier( Canvas.FontTier );
	public UiModels Models { get; }
	public UiBatch Batch { get; } = new();
	public UiCanvas Canvas { get; set; } = new( 1280, 720 );
	public float Time { get; set; }
	/// <summary>Seconds since the previous UI update.</summary>
	public float Delta { get; set; }
	/// <summary>Popup help shown for the hovered element (original UIHELPTEXT), null when none.</summary>
	public string? HoverHelp { get; set; }
	public bool PopupHelp { get; set; } = true;
	public Func<string, string?> ResolveTexture { get; set; } = name => UiImages.Resolve( name );

	public TextLayoutResult Layout( FontAtlas font, string text, int maximumWidthPixels = 0 )
	{
		var key = (font, text, maximumWidthPixels);
		if ( !layouts.TryGetValue( key, out var layout ) )
		{
			if ( layouts.Count > 2048 )
				layouts.Clear();
			layout = TextLayout.Create( font, text, maximumWidthPixels > 0 ? Math.Max( 1, maximumWidthPixels / Canvas.TextScale ) : 0 );
			layouts[key] = layout;
		}
		return layout;
	}

	/// <summary>Text size in framebuffer pixels at the current text scale.</summary>
	public (int Width, int Height) Measure( FontAtlas font, string text, int maximumWidthPixels = 0 )
	{
		var layout = Layout( font, text, maximumWidthPixels );
		return (layout.Width * Canvas.TextScale, layout.Height * Canvas.TextScale);
	}

	/// <summary>Draws text aligned horizontally inside <paramref name="rect"/> and centred vertically when it fits.</summary>
	public void DrawText( FontAtlas font, string text, UiRect rect, RgbaByte color, UiAlign align = UiAlign.Left, bool wrap = false, bool shadow = true, int? textScale = null )
	{
		if ( string.IsNullOrEmpty( text ) )
			return;
		var scale = wrap ? Canvas.TextScale : textScale ?? Canvas.TextScale;
		var layout = Layout( font, text, wrap ? (int)rect.Width : 0 );
		var width = layout.Width * scale;
		var height = layout.Height * scale;
		var x = align switch
		{
			UiAlign.Center => rect.X + (rect.Width - width) / 2,
			UiAlign.Right => rect.Right - width,
			_ => rect.X
		};
		var y = height <= rect.Height ? rect.Y + (rect.Height - height) / 2 : rect.Y;
		var ix = (int)MathF.Round( x );
		var iy = (int)MathF.Round( y );
		// [APPROX:UI-007] drop shadow one text pixel down-right — evidence needed: captures of original screens
		if ( shadow )
			Batch.AddText( font, layout, ix + scale, iy + scale, UiColors.Shadow, scale );
		Batch.AddText( font, layout, ix, iy, color, scale, text );
	}

	/// <summary>
	/// Draws single-line text that fits <paramref name="rect"/>: the given font, else a smaller size of its family,
	/// else a smaller whole text scale (glyphs stay pixel-exact), else wrapped in the smallest size (UiTextFit).
	/// Fitting and placement use the drawn pixels, not the line box: BF4 glyph offsets put ink above and below
	/// the line box. Horizontally the text's own ink is aligned; vertically the font's letter box (capitals,
	/// ascenders and descenders) is centred, so labels on neighbouring buttons share a baseline.
	/// </summary>
	public void DrawFittedText( FontAtlas font, string text, UiRect rect, RgbaByte color, UiAlign align = UiAlign.Center, bool shadow = true )
	{
		if ( string.IsNullOrEmpty( text ) )
			return;
		var candidates = new List<FontAtlas> { font };
		candidates.AddRange( Fonts.Smaller( font ) );
		var boxes = candidates.Select( candidate => InkBox( candidate, text, shadow ) ).ToArray();
		var fit = UiTextFit.Choose( Canvas.TextScale, boxes.Select( box => (box.Right - box.Left, box.Bottom - box.Top) ).ToArray(), rect.Width, rect.Height );
		if ( fit.Wrap )
		{
			DrawText( candidates[fit.Index], text, rect, color, align, true, shadow );
			return;
		}
		var chosen = candidates[fit.Index];
		var box = boxes[fit.Index];
		var scale = fit.Scale;
		var width = (box.Right - box.Left) * scale;
		var x = align switch
		{
			UiAlign.Center => rect.X + (rect.Width - width) / 2,
			UiAlign.Right => rect.Right - width,
			_ => rect.X
		} - box.Left * scale;
		var y = rect.Y + (rect.Height - (box.Bottom - box.Top) * scale) / 2 - box.Top * scale;
		var layout = Layout( chosen, text );
		var ix = (int)MathF.Round( x );
		var iy = (int)MathF.Round( y );
		if ( shadow )
			Batch.AddText( chosen, layout, ix + scale, iy + scale, UiColors.Shadow, scale );
		Batch.AddText( chosen, layout, ix, iy, color, scale, text );
	}

	private readonly Dictionary<FontAtlas, (int Top, int Bottom)> letterBoxes = new();

	/// <summary>Unscaled box of <paramref name="text"/>'s drawn pixels, widened vertically to the font's letter box and by the drop shadow.</summary>
	private (int Left, int Top, int Right, int Bottom) InkBox( FontAtlas font, string text, bool shadow )
	{
		if ( !letterBoxes.TryGetValue( font, out var letters ) )
		{
			var reference = TextLayout.Create( font, "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" );
			letters = reference.Glyphs.Count == 0 ? (0, font.LineHeight) : (reference.InkTop, reference.InkBottom);
			letterBoxes[font] = letters;
		}
		var layout = Layout( font, text );
		var extra = shadow ? 1 : 0;
		if ( layout.Glyphs.Count == 0 )
			return (0, letters.Top, layout.Width + extra, letters.Bottom + extra);
		return (layout.InkLeft, Math.Min( layout.InkTop, letters.Top ), layout.InkRight + extra, Math.Max( layout.InkBottom, letters.Bottom ) + extra);
	}

	/// <summary>Draws a model frame stretched so its bounds fill <paramref name="rect"/>.</summary>
	public void DrawFrame( UiModelFrame frame, UiRect rect, RgbaByte? tint = null )
	{
		if ( frame.Width <= 0 || frame.Height <= 0 )
			return;
		var scaleX = rect.Width / frame.Width;
		var scaleY = rect.Height / frame.Height;
		var color = tint ?? RgbaByte.White;
		foreach ( var part in frame.Parts )
		{
			var path = part.TextureName.Length == 0 ? null : ResolveTexture( part.TextureName );
			var texture = path == null ? UiTexture.Solid : UiTexture.Image( part.Transparent ? path + UiImages.BlackKeySuffix : path );
			UiVertex Convert( int index )
			{
				var vertex = part.Vertices[index];
				return new UiVertex( new NVector2( rect.X + (vertex.Position.X - frame.MinX) * scaleX, rect.Y + (vertex.Position.Y - frame.MinY) * scaleY ), vertex.TexCoords, color );
			}
			for ( var index = 0; index + 2 < part.Indices.Count; index += 3 )
				Batch.AddTriangle( texture, Convert( part.Indices[index] ), Convert( part.Indices[index + 1] ), Convert( part.Indices[index + 2] ) );
		}
	}

	/// <summary>Draws model <paramref name="name"/> frame <paramref name="frame"/> into <paramref name="rect"/>; false if the model is missing.</summary>
	public bool DrawModel( string name, int frame, UiRect rect, RgbaByte? tint = null )
	{
		var model = Models.Get( name );
		if ( model == null )
			return false;
		DrawFrame( model.GetFrame( frame ), rect, tint );
		return true;
	}

	/// <summary>Authored canvas rectangle of a model frame placed where the original file put it.</summary>
	public UiRect AuthoredRect( string name, int frame = 0, UiRect fallback = default )
	{
		var model = Models.Get( name );
		if ( model == null )
			return fallback;
		var bounds = model.GetFrame( frame );
		return new UiRect( bounds.MinX, bounds.MinY, bounds.Width, bounds.Height );
	}
}

/// <summary>
/// Base element. <see cref="Bounds"/> is in the authored 2048×1536 canvas and is mapped through
/// <see cref="Anchor"/> each frame, so layout is resolution independent.
/// </summary>
public abstract class UiElement
{
	public string Id { get; init; } = "";
	public UiRect Bounds { get; set; }
	public UiAnchor Anchor { get; set; } = UiAnchor.Center;
	public bool Visible { get; set; } = true;
	public bool Enabled { get; set; } = true;
	/// <summary>Original popup help (UIHELPTEXT) shown on hover.</summary>
	public string? Help { get; set; }
	public virtual bool Focusable => false;

	public UiRect ScreenRect( UiCanvas canvas ) => canvas.Map( Bounds, Anchor );
	public abstract void Draw( UiContext context, bool focused, bool pressed );
	public virtual void Activate() { }
	public virtual void Adjust( int direction ) { }
}

public sealed class UiLabel : UiElement
{
	public Func<string> Text { get; set; } = () => "";
	public Func<UiFonts, FontAtlas> Font { get; set; } = fonts => fonts.Label;
	public RgbaByte Color { get; set; } = UiColors.Text;
	public UiAlign Align { get; set; } = UiAlign.Left;
	public bool Wrap { get; set; }

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		var rect = ScreenRect( context.Canvas );
		var font = Font( context.Fonts );
		var text = Text();
		// [APPROX:UI-032] small-font fallback and greedy wrap for long labels — evidence needed: captures of translated original screens
		// Longer translations fall back to the small game font instead of overflowing the art.
		if ( !Wrap && context.Measure( font, text ).Width > rect.Width )
			font = context.Fonts.Small;
		context.DrawText( font, text, rect, Color, Align, Wrap );
	}
}

/// <summary>An original model drawn as decoration (panel, window, display).</summary>
public sealed class UiModelImage : UiElement
{
	public string Model { get; set; } = "";
	public Func<int> Frame { get; set; } = () => 0;
	public RgbaByte? Tint { get; set; }
	/// <summary>Name of a local art override (<see cref="UiArtOverrides"/>) drawn instead of the model when present.</summary>
	public string? ArtOverride { get; set; }

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		if ( ArtOverride != null && UiArtOverrides.Find( ArtOverride ) is { } art )
		{
			// Fit inside the model's box, keeping the image's aspect ratio.
			var rect = ScreenRect( context.Canvas );
			var scale = Math.Min( rect.Width / art.Width, rect.Height / art.Height );
			var size = new NVector2( art.Width * scale, art.Height * scale );
			context.Batch.AddQuad( UiTexture.HostImage( art.Path ), new UiRect( rect.X + (rect.Width - size.X) / 2, rect.Y + (rect.Height - size.Y) / 2, size.X, size.Y ),
				NVector2.Zero, NVector2.One, Tint ?? RgbaByte.White );
			return;
		}
		if ( !context.DrawModel( Model, Frame(), ScreenRect( context.Canvas ), Tint ) )
			context.Batch.AddRectangle( ScreenRect( context.Canvas ), UiColors.HelpBackground );
	}
}

/// <summary>An original image (.wct) drawn as decoration.</summary>
public sealed class UiImageElement : UiElement
{
	public string Image { get; set; } = "";
	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		var path = context.ResolveTexture( Image );
		if ( path != null )
			context.Batch.AddImage( path, ScreenRect( context.Canvas ) );
	}
}

/// <summary>
/// Button. With <see cref="Model"/> it draws the original state frames (0 normal, 1 disabled,
/// 2 highlighted, 5 down); otherwise a text button on the original purple button art.
/// </summary>
public sealed class UiButton : UiElement
{
	public const int NormalFrame = 0;
	public const int DisabledFrame = 1;
	public const int HighlightFrame = 2;
	public const int DownFrame = 5;
	/// <summary>Share of each purple_button half outside its bar (16 of 64 texture rows); the bar is centred in the button.</summary>
	public const float PurpleBarGap = 0.25f;

	public string? Model { get; set; }
	public Func<string>? Text { get; set; }
	public Func<UiFonts, FontAtlas> Font { get; set; } = fonts => fonts.Menu;
	public Action? Clicked { get; set; }
	/// <summary>Left/right keys on the focused button (e.g. turn the lobby to the next island).</summary>
	public Action<int>? Adjusted { get; set; }
	public Func<bool>? Selected { get; set; }
	public UiAlign Align { get; set; } = UiAlign.Center;
	public override bool Focusable => Visible && Enabled;

	public override void Adjust( int direction ) => Adjusted?.Invoke( direction );

	public override void Activate()
	{
		if ( Enabled )
			Clicked?.Invoke();
	}

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		var rect = ScreenRect( context.Canvas );
		var frame = !Enabled ? DisabledFrame : pressed || Selected?.Invoke() == true ? DownFrame : focused ? HighlightFrame : NormalFrame;
		if ( Model != null && context.DrawModel( Model, frame, rect ) )
		{
			if ( Text != null )
				context.DrawFittedText( Font( context.Fonts ), Text(), rect, focused ? UiColors.Highlight : UiColors.Text, Align );
			return;
		}
		// [APPROX:UI-008] purple_button as a mirrored end cap, upper half normal, lower half focused/pressed — evidence needed: capture of the original front-end buttons
		var art = context.ResolveTexture( "purple_button" );
		var bar = rect;
		if ( art != null )
		{
			var lit = focused || pressed || Selected?.Invoke() == true;
			bar = new UiRect( rect.X, rect.Y + rect.Height * PurpleBarGap / 2, rect.Width, rect.Height * (1 - PurpleBarGap) );
			DrawPurpleBar( context, UiTexture.Image( art ), bar, lit, Enabled ? RgbaByte.White : new RgbaByte( 160, 160, 160, 255 ) );
		}
		else
			context.Batch.AddRectangle( rect, focused ? UiColors.Highlight : UiColors.HelpBackground );
		// Margins keep the label off the bar's rim and rounded ends.
		var inner = new UiRect( bar.X + bar.Height * 0.5f, bar.Y + bar.Height * 0.05f, bar.Width - bar.Height, bar.Height * 0.9f );
		context.DrawFittedText( Font( context.Fonts ), Text?.Invoke() ?? "", inner, !Enabled ? UiColors.Disabled : focused ? UiColors.Highlight : UiColors.Text, Align );
	}

	/// <summary>
	/// Each half of the purple_button texture is one end of a button: a bar in 48 of its 64 rows with a rounded
	/// end (left in the upper, normal half; right in the lower, lit half) and a cut end. A whole button is that
	/// piece and its mirror image: rounded caps at their own aspect, the body stretched to meet in the middle.
	/// The outermost texture columns at both ends are edges and stay out of the caps and the seam.
	/// </summary>
	private static void DrawPurpleBar( UiContext context, UiTexture texture, UiRect bar, bool lit, RgbaByte tint )
	{
		// The outermost column of the rounded end is a translucent grey edge that would show as a line.
		const float EdgeU = 1.5f / 128, CapU = 0.25f, CutU = 0.96f;
		// The bar's first row is a faint rim that also runs over the rounded corners; start below it, and stay half a
		// texel inside so linear filtering does not pull in the neighbouring rows.
		var (v0, v1) = lit ? (81.5f / 128, 127.5f / 128) : (1.5f / 128, 47.5f / 128);
		// Texture u measured from the rounded end of the piece.
		float U( float fromRound ) => lit ? 1 - fromRound : fromRound;
		var cap = Math.Min( bar.Height * CapU * 128 / 48, bar.Width / 2 );
		var middle = bar.X + bar.Width / 2;
		var capEnd = cap < bar.Width / 2 ? CapU : CapU * (bar.Width / 2) / cap;
		void Quad( float left, float right, float uLeft, float uRight )
		{
			if ( right > left )
				context.Batch.AddQuad( texture, new UiRect( left, bar.Y, right - left, bar.Height ), new NVector2( uLeft, v0 ), new NVector2( uRight, v1 ), tint );
		}
		Quad( bar.X, bar.X + cap, U( EdgeU ), U( capEnd ) );
		Quad( bar.X + cap, middle, U( CapU ), U( CutU ) );
		Quad( middle, bar.Right - cap, U( CutU ), U( CapU ) );
		Quad( bar.Right - cap, bar.Right, U( capEnd ), U( EdgeU ) );
	}
}

/// <summary>
/// An options row in the style of the original options panel (f_optpanel2 bar): label on the left,
/// value on the right, cycled with the arrows (b_sleft/b_sright), the mouse wheel or left/right keys.
/// </summary>
public sealed class UiOptionRow : UiElement
{
	public Func<string> Label { get; set; } = () => "";
	public Func<string> Value { get; set; } = () => "";
	public Action<int>? Changed { get; set; }
	public override bool Focusable => Visible && Enabled;

	public override void Adjust( int direction )
	{
		if ( Enabled )
			Changed?.Invoke( direction );
	}

	public override void Activate() => Adjust( 1 );

	public (UiRect Left, UiRect Right) ArrowRects( UiCanvas canvas )
	{
		var rect = ScreenRect( canvas );
		var size = rect.Height * 0.8f;
		var top = rect.Y + (rect.Height - size) / 2;
		// [APPROX:UI-009] option-row arrow/value positions — evidence needed: capture of the original options screen
		// The f_optpanel2 art: label lozenge up to ~58% of the frame width, then the value lozenges to the end.
		var left = new UiRect( rect.X + rect.Width * 0.585f, top, size, size );
		var right = new UiRect( rect.X + rect.Width * 0.975f - size, top, size, size );
		return (left, right);
	}

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		var rect = ScreenRect( context.Canvas );
		if ( !context.DrawModel( "f_optpanel2", 0, rect, focused ? new RgbaByte( 255, 255, 200, 255 ) : null ) )
			context.Batch.AddRectangle( rect, UiColors.HelpBackground );
		var (left, right) = ArrowRects( context.Canvas );
		var pad = rect.Height * 0.35f;
		context.DrawText( context.Fonts.Label, Label(), new UiRect( rect.X + pad, rect.Y, left.X - rect.X - pad, rect.Height ), focused ? UiColors.Highlight : UiColors.Text );
		if ( Enabled )
		{
			context.DrawModel( "b_sleft", focused ? UiButton.HighlightFrame : 0, left );
			context.DrawModel( "b_sright", focused ? UiButton.HighlightFrame : 0, right );
		}
		context.DrawText( context.Fonts.Label, Value(), new UiRect( left.Right, rect.Y, right.X - left.Right, rect.Height ), Enabled ? UiColors.Value : UiColors.Disabled, UiAlign.Center );
	}
}

/// <summary>A selectable line in a list (load screen).</summary>
public sealed class UiListItem : UiElement
{
	public Func<string> Text { get; set; } = () => "";
	public Action? Clicked { get; set; }
	public override bool Focusable => Visible && Enabled;
	public override void Activate() => Clicked?.Invoke();

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		var rect = ScreenRect( context.Canvas );
		if ( focused )
			context.Batch.AddRectangle( rect, new RgbaByte( 60, 90, 200, 160 ) );
		context.DrawText( context.Fonts.Label, Text(), new UiRect( rect.X + rect.Height * 0.3f, rect.Y, rect.Width, rect.Height ), focused ? UiColors.Highlight : UiColors.Text );
	}
}
