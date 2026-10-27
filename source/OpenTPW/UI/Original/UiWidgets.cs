using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

public enum UiAlign { Left, Center, Right }

/// <summary>Everything a screen needs to lay out and draw one frame.</summary>
public sealed class UiContext
{
	private readonly Dictionary<(FontAtlas, string, int, int), TextLayoutResult> layouts = new();

	public UiContext( UiStringTable strings, UiFonts fonts, UiModels models )
	{
		Strings = strings;
		this.fonts = fonts;
		Models = models;
		RightToLeft = strings != null && GameLanguage.OriginalLanguageId( strings.Language ) == GameLanguage.HebrewLanguageId;
	}

	/// <summary>
	/// Whether text is drawn for a right-to-left language (Hebrew): left and right alignment trade places, as tp.exe's two text helpers do
	/// (docs/LANGUAGES.md, "Hebrew"). The lines themselves are put in visual order by <see cref="TextLayout"/>.
	/// </summary>
	public bool RightToLeft { get; set; }

	/// <summary><paramref name="align"/> as drawn: left and right swapped in a right-to-left language, centre unchanged.</summary>
	// [BIN:TPW-EXE:0x492750 Hebrew alignment] [BIN:TPW-EXE:0x4929f0 Hebrew alignment] both helpers turn alignment 0 (left) into 2 (right) and 2 into 0
	// when 0x41a850 reports Hebrew, before they pick the text library's mode (0 left, 2 centre, 1 right); 0x4925a0 does the same for the alignment
	// a caller keeps (0x4a1d70)
	public UiAlign Drawn( UiAlign align ) => !RightToLeft ? align : align switch { UiAlign.Left => UiAlign.Right, UiAlign.Right => UiAlign.Left, _ => align };

	public UiStringTable Strings { get; }
	private UiFonts fonts;
	/// <summary>Fonts of the tier that suits the current canvas.</summary>
	public UiFonts Fonts => fonts.Tier == Canvas.FontTier ? fonts : fonts = fonts.WithTier( Canvas.FontTier );
	/// <summary>False for a context made without fonts (tests that only route input).</summary>
	public bool HasFonts => fonts is not null;
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

	public TextLayoutResult Layout( FontAtlas font, string text, int maximumWidthPixels = 0, int? textScale = null )
	{
		var scale = Math.Max( 1, textScale ?? Canvas.TextScale );
		var key = (font, text, maximumWidthPixels, scale);
		if ( !layouts.TryGetValue( key, out var layout ) )
		{
			if ( layouts.Count > 2048 )
				layouts.Clear();
			layout = TextLayout.Create( font, text, maximumWidthPixels > 0 ? Math.Max( 1, maximumWidthPixels / scale ) : 0 );
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

	/// <summary>Draws text inside <paramref name="rect"/> as tp.exe's text library places it: the block aligned horizontally, each line aligned inside the block, the block centred vertically.</summary>
	public void DrawText( FontAtlas font, string text, UiRect rect, RgbaByte color, UiAlign align = UiAlign.Left, bool wrap = false, byte shadowAlpha = 0, int? textScale = null, bool originalPlacement = true )
	{
		if ( string.IsNullOrEmpty( text ) )
			return;
		align = Drawn( align );
		var scale = textScale ?? Canvas.TextScale;
		var layout = Layout( font, text, wrap ? (int)rect.Width : 0, scale );
		var textAlignment = align switch { UiAlign.Center => TextAlignment.Center, UiAlign.Right => TextAlignment.Right, _ => TextAlignment.Left };
		int ix, iy;
		var width = layout.Width * scale;
		var height = layout.Height * scale;
		if ( originalPlacement )
		{
			// [BIN:TPW-EXE:0x6ae550 line offset] a centred or right aligned multi-line text moves each line inside the widest one
			layout = layout.AlignLines( textAlignment );
			// [BIN:TPW-EXE:0x6aeb10 block box] whole-pixel block placement; a block taller than the box is still centred (the original's control bitmap cuts it off at both ends; OpenTPW does not clip)
			var left = (int)MathF.Round( rect.X );
			var top = (int)MathF.Round( rect.Y );
			ix = TextPlacement.BlockLeft( textAlignment, left, (int)MathF.Round( rect.Right ), width );
			iy = TextPlacement.BlockTopCentered( top, (int)MathF.Round( rect.Bottom ), height );
		}
		else
		{
			// [EXT:fit-button-text] the wrapped fallback of a button label that fits at no size keeps its own placement (block aligned in the box, lines left aligned in it,
			// an overflowing block hangs from the box top), so the approved fitting does not move with the text library's rules
			var x = align switch
			{
				UiAlign.Center => rect.X + (rect.Width - width) / 2,
				UiAlign.Right => rect.Right - width,
				_ => rect.X
			};
			var y = height <= rect.Height ? rect.Y + (rect.Height - height) / 2 : rect.Y;
			ix = (int)MathF.Round( x );
			iy = (int)MathF.Round( y );
		}
		// [BIN:TPW-EXE:0x48f830 text control] one 0x6ae0f0 call into the control's bitmap (texel alpha 17 x coverage, 0x6b24b0), then the sprite quads of the state word 0x7060556 (0x58a010): the control colour blended with coverage / 15; plain text has no shadow
		// [BIN:TPW-EXE:0x491ab0 title control] a title control (constructor 0x491920) first draws a grey (128,128,128, alpha 0x80) copy on the rectangle moved by (-2, +2) pixels (0x491dd0-0x491e1a), then the white string (0x491e27-0x491e84), both modulated by the control colour
		if ( shadowAlpha != 0 )
			Batch.AddTitleText( font, layout, ix, iy, color, shadowAlpha, scale, text );
		else
			Batch.AddText( font, layout, ix, iy, color, scale, text );
	}

	/// <summary>
	/// Draws single-line text that fits <paramref name="rect"/>: the given font, else a smaller size of its family,
	/// else a smaller whole text scale (glyphs stay pixel-exact), else wrapped in the smallest size (UiTextFit).
	/// Fitting and placement use the drawn pixels, not the line box: BF4 glyph offsets put ink above and below
	/// the line box. Horizontally the text's own ink is aligned; vertically the font's letter box (capitals,
	/// ascenders and descenders) is centred, so labels on neighbouring buttons share a baseline.
	/// </summary>
	public void DrawFittedText( FontAtlas font, string text, UiRect rect, RgbaByte color, UiAlign align = UiAlign.Center, byte shadowAlpha = 0 )
	{
		if ( string.IsNullOrEmpty( text ) )
			return;
		var candidates = new List<FontAtlas> { font };
		candidates.AddRange( Fonts.Smaller( font ) );
		// [EXT:fit-button-text] a label that fits no size of its own family goes on to the body fonts, smaller than the last size tried, so a small icon button
		// (a bulk-mark or close button) still shows its whole text; the original's buttons carry art, not these labels
		foreach ( var body in new[] { Fonts.Label, Fonts.Small }.Where( body => body.LineHeight < candidates[^1].LineHeight && !candidates.Contains( body ) ).OrderByDescending( body => body.LineHeight ) )
			candidates.Add( body );
		var boxes = candidates.Select( candidate => InkBox( candidate, text ) ).ToArray();
		var fit = UiTextFit.Choose( Canvas.TextScale, boxes.Select( box => (box.Right - box.Left + FitExtra( shadowAlpha ), box.Bottom - box.Top + FitExtra( shadowAlpha )) ).ToArray(), rect.Width, rect.Height );
		if ( fit.Wrap )
		{
			// [EXT:fit-button-text] before any word is cut between characters, the label is wrapped at its spaces in the largest size whose lines all fit the box
			var words = text.Split( new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries );
			var wrapped = UiTextFit.ChooseWrapped( Canvas.TextScale, candidates.Count, ( index, scale ) =>
			{
				var available = (int)(rect.Width / scale);
				var layout = Layout( candidates[index], text, (int)rect.Width, scale );
				var wordsWhole = words.All( word => Layout( candidates[index], word ).Width <= available );
				return (wordsWhole, layout.Height, layout.LineCount);
			}, rect.Height );
			if ( wrapped is { } choice )
			{
				DrawText( candidates[choice.Index], text, rect, color, align, true, shadowAlpha, choice.Scale, originalPlacement: false );
				return;
			}
			// No size fits even wrapped (a box of a few pixels): the smallest size on one line, centred, as the original's unwrapped control text is.
			DrawTextAt( candidates[^1], 1, text, rect, color, align, shadowAlpha );
			return;
		}
		DrawTextAt( candidates[fit.Index], fit.Scale, text, rect, color, align, shadowAlpha );
	}

	/// <summary>Draws single-line text in <paramref name="chosen"/> at whole text scale <paramref name="scale"/>, placed like <see cref="DrawFittedText"/> (ink aligned horizontally, letter box centred vertically).</summary>
	public void DrawTextAt( FontAtlas chosen, int scale, string text, UiRect rect, RgbaByte color, UiAlign align = UiAlign.Left, byte shadowAlpha = 0 )
	{
		if ( string.IsNullOrEmpty( text ) )
			return;
		align = Drawn( align );
		var box = InkBox( chosen, text );
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
		if ( shadowAlpha != 0 )
			Batch.AddTitleText( chosen, layout, ix, iy, color, shadowAlpha, scale, text );
		else
			Batch.AddText( chosen, layout, ix, iy, color, scale, text );
	}

	/// <summary>
	/// Font and whole text scale for <paramref name="label"/> of <paramref name="group"/>. The page size is the family size
	/// and scale (up to the current text scale) whose letter box is closest to <see cref="UiLabelGroup.TargetHeight"/> of
	/// the shortest label rectangle without exceeding it. A label uses it when its widest variant text fits its rectangle;
	/// otherwise it takes the largest smaller size that fits, or the smallest size when none does.
	/// </summary>
	// [BIN:TPW-EXE:0x4a43b0 options label font] the original's options labels take font slot 6 of the screen mode's bank ([0x7537b4], 0x4a443c) with no fitting
	// [EXT:option-label-fit] option label size: letter box about 58 % of the label rectangle height (the capture's labels are ~26 of 1536 units for 45-unit rectangles), shared per page; a label whose widest value does not fit drops alone to the largest size that does; the original does no fitting and wraps an overflowing label into its rectangle through the text library (0x6ae930, 0x6aeb10, 0x6aec80), where OpenTPW shrinks it
	// [BIN:TPW-EXE:0x4a443c bank font] slot 6 is GAME5AA/GAME7/GAME8/GAME9 for banks 0-3; which shipped font serves the label per size tier is [EXT:ui-font-tier-by-scale], and a label that overflows is not shrunk in the original but laid out by the text library (wrapped into its rectangle: 0x6ae930, 0x6aeb10, 0x6aec80)
	public (FontAtlas Font, int Scale) ChooseUniform( UiLabelGroup group, UiLabel label )
	{
		var members = group.Members.Where( member => member.Visible ).ToList();
		var candidates = new List<FontAtlas> { group.Font( Fonts ) };
		candidates.AddRange( Fonts.Smaller( candidates[0] ) );
		var sizes = new List<(FontAtlas Font, int Scale, int Height)>();
		foreach ( var candidate in candidates )
		{
			var box = InkBox( candidate, "x" );
			for ( var scale = Math.Max( 1, Canvas.TextScale ); scale >= 1; scale-- )
				sizes.Add( (candidate, scale, (box.Bottom - box.Top) * scale) );
		}
		var heights = (members.Count == 0 ? new List<UiLabel> { label } : members).Select( member => member.ScreenRect( Canvas ).Height ).ToList();
		var maximum = heights.Min();
		var target = maximum * UiLabelGroup.TargetHeight;
		var page = sizes.Where( size => size.Height <= maximum ).OrderBy( size => Math.Abs( size.Height - target ) ).ThenByDescending( size => size.Height ).Cast<(FontAtlas Font, int Scale, int Height)?>().FirstOrDefault()
			?? sizes.OrderBy( size => size.Height ).First();
		var rect = label.ScreenRect( Canvas );
		var texts = label.Variants?.Invoke().ToList() ?? new List<string> { label.Text() };
		bool Fits( (FontAtlas Font, int Scale, int Height) size ) => texts.All( text =>
		{
			var box = InkBox( size.Font, text );
			return (box.Right - box.Left + (label.Shadow ? UiBatch.TitleShadowOffset : 0)) * size.Scale <= rect.Width;
		} );
		if ( Fits( page ) )
			return (page.Font, page.Scale);
		var smaller = sizes.Where( size => size.Height <= page.Height ).OrderByDescending( size => size.Height ).ToList();
		var fit = smaller.Cast<(FontAtlas Font, int Scale, int Height)?>().FirstOrDefault( size => Fits( size!.Value ) ) ?? smaller[^1];
		return (fit.Font, fit.Scale);
	}

	private readonly Dictionary<FontAtlas, (int Top, int Bottom)> letterBoxes = new();

	/// <summary>Extra unscaled pixels a title's copy adds to the width and height a fitted title must have room for; placement ignores the copy, as the original's does.</summary>
	private static int FitExtra( byte shadowAlpha ) => shadowAlpha != 0 ? UiBatch.TitleShadowOffset : 0;

	/// <summary>Unscaled box of <paramref name="text"/>'s drawn pixels, widened vertically to the font's letter box.</summary>
	private (int Left, int Top, int Right, int Bottom) InkBox( FontAtlas font, string text )
	{
		if ( !letterBoxes.TryGetValue( font, out var letters ) )
		{
			var reference = TextLayout.Create( font, "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" );
			letters = reference.Glyphs.Count == 0 ? (0, font.LineHeight) : (reference.InkTop, reference.InkBottom);
			letterBoxes[font] = letters;
		}
		var layout = Layout( font, text );
		if ( layout.Glyphs.Count == 0 )
			return (0, letters.Top, layout.Width, letters.Bottom);
		return (layout.InkLeft, Math.Min( layout.InkTop, letters.Top ), layout.InkRight, Math.Max( layout.InkBottom, letters.Bottom ));
	}

	/// <summary>Draws a model frame stretched so its selected-mesh fit bounds fill <paramref name="rect"/>.</summary>
	public void DrawFrame( UiModelFrame frame, UiRect rect, RgbaByte? tint = null ) => DrawFrame( frame, rect, tint, 0, default );

	/// <summary>
	/// Draws a model frame fitted to <paramref name="rect"/> and then turned <paramref name="turn"/> radians clockwise on the screen about <paramref name="pivot"/>
	/// (framebuffer pixels), as a window's model takes its angle (window +0xE2) about its pivot (+0xE4).
	/// </summary>
	// [BIN:TPW-EXE:0x00476C50 UI model matrix] a nonzero angle a (4096 a turn: a·π/2048) makes the rows (cos a, −4/3 sin a) and (3/4 sin a, cos a) about the pivot in the 4:3 view's
	// coordinates (y up), applied to row vectors: on the screen, a rigid turn clockwise
	public void DrawFrame( UiModelFrame frame, UiRect rect, RgbaByte? tint, float turn, NVector2 pivot )
	{
		if ( !frame.TryGetFitBounds( out var fit ) )
			return;
		var scaleX = rect.Width / fit.Width;
		var scaleY = rect.Height / fit.Height;
		var color = tint ?? RgbaByte.White;
		var (sin, cos) = turn == 0 ? (0f, 1f) : MathF.SinCos( turn );
		foreach ( var part in frame.Parts )
		{
			var texture = TextureFor( part );
			UiVertex Convert( int index )
			{
				var vertex = part.Vertices[index];
				var at = new NVector2( rect.X + (vertex.Position.X - fit.X) * scaleX, rect.Y + (vertex.Position.Y - fit.Y) * scaleY );
				if ( turn != 0 )
				{
					var offset = at - pivot;
					at = pivot + new NVector2( offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos );
				}
				return new UiVertex( at, vertex.TexCoords, color );
			}
			for ( var index = 0; index + 2 < part.Indices.Count; index += 3 )
				Batch.AddTriangle( texture, Convert( part.Indices[index] ), Convert( part.Indices[index + 1] ), Convert( part.Indices[index + 2] ) );
		}
	}

	/// <summary>The exact resource key used when drawing this model part.</summary>
	internal UiTexture TextureFor( UiModelPart part )
	{
		var path = part.TextureName.Length == 0 ? null : ResolveTexture( part.TextureName );
		return path == null ? UiTexture.Solid : UiTexture.Image( path );
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

	/// <summary>Whether every named texture of model <paramref name="name"/> frame <paramref name="frame"/> resolves (a missing one would draw as untextured white).</summary>
	public bool FrameTexturesResolve( string name, int frame ) =>
		Models.Get( name ) is { } model && model.GetFrame( frame ).Parts.All( part => part.TextureName.Length == 0 || ResolveTexture( part.TextureName ) != null );

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
	/// <summary>The help read each frame when set (a text that follows the language); otherwise <see cref="Help"/>.</summary>
	public Func<string?>? HelpSource { get; set; }
	public string? CurrentHelp => HelpSource?.Invoke() ?? Help;
	/// <summary>
	/// [EXT:ONLINE-UI] Whether the element's help shows on hover while it is disabled, which is how a greyed line says why (the online menu's Publish park). A disabled element cannot be focused or clicked, so
	/// only its help is looked up under the pointer (<see cref="UiScreen.DisabledHelpAt"/>).
	/// </summary>
	public bool HelpWhenDisabled { get; set; }
	public virtual bool Focusable => false;
	/// <summary>Whether the element keeps a click at <paramref name="point"/> (framebuffer pixels) off the park: anywhere in its box, unless it is shaped.</summary>
	public virtual bool CoversPoint( UiCanvas canvas, NVector2 point ) => ScreenRect( canvas ).Contains( point );

	public UiRect ScreenRect( UiCanvas canvas ) => canvas.Map( Bounds, Anchor );
	public abstract void Draw( UiContext context, bool focused, bool pressed );
	public virtual void Activate() { }
	public virtual void Adjust( int direction ) { }
}

/// <summary>Labels that are drawn in one common size (the option labels of a page).</summary>
public sealed class UiLabelGroup
{
	/// <summary>Share of the label rectangle's height the letter box (capitals, ascenders, descenders) should have.</summary>
	public const float TargetHeight = 0.58f;
	public List<UiLabel> Members { get; } = new();
	public Func<UiFonts, FontAtlas> Font { get; set; } = fonts => fonts.Label;
}

public sealed class UiLabel : UiElement
{
	/// <summary>Optional title action, preserving label art and text layout; ordinary labels remain inert.</summary>
	public Action? Clicked { get; set; }
	public override bool Focusable => Visible && Enabled && Clicked != null;
	public override void Activate() { if ( Visible && Enabled ) Clicked?.Invoke(); }

	public Func<string> Text { get; set; } = () => "";
	public Func<UiFonts, FontAtlas> Font { get; set; } = fonts => fonts.Label;
	public RgbaByte Color { get; set; } = UiColors.Text;
	/// <summary>When set, the text colour is read from here every frame instead of <see cref="Color"/>.</summary>
	public Func<RgbaByte>? ColorSource { get; set; }
	public UiAlign Align { get; set; } = UiAlign.Left;
	public bool Wrap { get; set; }
	/// <summary>
	/// The title control's shadow (<see cref="UiBatch.AddTitleText"/>): window, screen and lobby titles have it; every other text control of the original draws its string once.
	/// </summary>
	public bool Shadow { get; set; }
	/// <summary>The colour alpha of the title copy when <see cref="Shadow"/> is set: 0x80 for Theme Park World's title control, 0xc8 for Theme Park Inc's.</summary>
	public byte ShadowAlpha { get; set; } = UiBatch.TitleCopyAlpha;
	/// <summary>Fit the single line into the rectangle (smaller font, smaller scale) instead of the small-font fallback.</summary>
	public bool Fit { get; set; }
	/// <summary>Labels of one group share a single font and scale (<see cref="UiContext.ChooseUniform"/>).</summary>
	public UiLabelGroup? Group { get; set; }
	/// <summary>Every text this label can show (so a group's size does not change while values change); null = just <see cref="Text"/>.</summary>
	public Func<IEnumerable<string>>? Variants { get; set; }

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		var rect = ScreenRect( context.Canvas );
		var font = Font( context.Fonts );
		var text = Text();
		var color = ColorSource?.Invoke() ?? Color;
		if ( Group != null )
		{
			var (groupFont, scale) = context.ChooseUniform( Group, this );
			context.DrawTextAt( groupFont, scale, text, rect, color, Align, Shadow ? ShadowAlpha : (byte)0 );
			return;
		}
		if ( Fit && !Wrap )
		{
			context.DrawFittedText( font, text, rect, color, Align, Shadow ? ShadowAlpha : (byte)0 );
			return;
		}
		// [BIN:TPW-EXE:0x6aec80 text wrap] the original's formatter wraps greedily: a line ends at the last break opportunity before the glyph that overflows, trailing spaces dropped, or before that glyph when the line has none
		// [BIN:TPW-EXE:0x6aeab0 glyph class] only space and tab are break opportunities (0x10) and spaces (2); every text control lays out through 0x6ae930 -> 0x6aeb10, which splits at line feeds and wraps each line with 0x6aec80, so the catalogue slots wrap
		// [EXT:fit-button-text] the small-font fallback for long labels: the original's control renderer 0x48f830 keeps its one slot font, and a translated label that overflowed its art would be unreadable
		// Longer translations fall back to the small game font instead of overflowing the art.
		if ( !Wrap && context.Measure( font, text ).Width > rect.Width )
			font = context.Fonts.Small;
		context.DrawText( font, text, rect, color, Align, Wrap, Shadow ? ShadowAlpha : (byte)0 );
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
	/// <summary>
	/// Whether the image keeps clicks off the park only on its model's triangles, not over its whole box (a frame round the park view: the chat frame's
	/// panels, not its opening). The shape is the frame it last drew, so before its first draw, or without its model, it covers nothing.
	/// </summary>
	public bool CoversShape { get; init; }
	private UiModelFrame? drawnShape;

	public override bool CoversPoint( UiCanvas canvas, NVector2 point )
	{
		if ( !CoversShape )
			return base.CoversPoint( canvas, point );
		var rect = ScreenRect( canvas );
		if ( drawnShape is not { } shape || !shape.TryGetFitBounds( out var fit ) || rect.Width <= 0 || rect.Height <= 0 )
			return false;
		// the point in the model's own units, as DrawFrame maps the frame onto the box
		var at = new NVector2( fit.X + (point.X - rect.X) * fit.Width / rect.Width, fit.Y + (point.Y - rect.Y) * fit.Height / rect.Height );
		foreach ( var part in shape.Parts )
		{
			for ( var index = 0; index + 2 < part.Indices.Count; index += 3 )
			{
				if ( InTriangle( at, part.Vertices[part.Indices[index]].Position, part.Vertices[part.Indices[index + 1]].Position, part.Vertices[part.Indices[index + 2]].Position ) )
					return true;
			}
		}
		return false;
	}

	/// <summary>Whether <paramref name="point"/> lies in the triangle or on its edge; a triangle without area holds no point.</summary>
	private static bool InTriangle( NVector2 point, NVector2 a, NVector2 b, NVector2 c )
	{
		float Side( NVector2 from, NVector2 to ) => (to.X - from.X) * (point.Y - from.Y) - (to.Y - from.Y) * (point.X - from.X);
		float ab = Side( a, b ), bc = Side( b, c ), ca = Side( c, a );
		if ( ab == 0 && bc == 0 && ca == 0 )
			return false;
		return ab >= 0 && bc >= 0 && ca >= 0 || ab <= 0 && bc <= 0 && ca <= 0;
	}

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
		else if ( CoversShape )
			drawnShape = context.Models.Get( Model )?.GetFrame( Frame() );
	}
}

/// <summary>
/// [EXT:online-two-step] A QR code (for an authenticator app's <c>otpauth://</c> URI): black modules on a white panel with
/// the standard four-module quiet zone, drawn as rectangles at a whole number of pixels per module so scanners read it.
/// </summary>
public sealed class UiQrCode : UiElement
{
	private string text = "";
	private bool[,] modules = new bool[0, 0];

	public string Text
	{
		get => text;
		set
		{
			text = value;
			modules = Modules( value );
		}
	}

	/// <summary>The module grid of <paramref name="text"/> at medium error correction; [x, y], true is dark.</summary>
	public static bool[,] Modules( string text )
	{
		if ( text.Length == 0 )
			return new bool[0, 0];
		var code = Net.Codecrete.QrCodeGenerator.QrCode.EncodeText( text, Net.Codecrete.QrCodeGenerator.QrCode.Ecc.Medium );
		var grid = new bool[code.Size, code.Size];
		for ( var y = 0; y < code.Size; y++ )
			for ( var x = 0; x < code.Size; x++ )
				grid[x, y] = code.GetModule( x, y );
		return grid;
	}

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		var size = modules.GetLength( 0 );
		if ( size == 0 )
			return;
		var rect = ScreenRect( context.Canvas );
		var span = size + 8;
		var module = MathF.Max( 1, MathF.Floor( MathF.Min( rect.Width, rect.Height ) / span ) );
		var x0 = MathF.Round( rect.X + (rect.Width - module * span) / 2 );
		var y0 = MathF.Round( rect.Y + (rect.Height - module * span) / 2 );
		context.Batch.AddRectangle( new UiRect( x0, y0, module * span, module * span ), new RgbaByte( 255, 255, 255, 255 ) );
		var black = new RgbaByte( 0, 0, 0, 255 );
		for ( var y = 0; y < size; y++ )
		{
			for ( var x = 0; x < size; x++ )
			{
				if ( !modules[x, y] )
					continue;
				var run = 1;
				while ( x + run < size && modules[x + run, y] )
					run++;
				context.Batch.AddRectangle( new UiRect( x0 + (x + 4) * module, y0 + (y + 4) * module, run * module, module ), black );
				x += run - 1;
			}
		}
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

/// <summary>The timer of a repeating button: after <see cref="Delay"/> held it calls <see cref="Start"/>, then <see cref="Tick"/> every time more than <see cref="Interval"/> has passed.</summary>
// [BIN:TPW-EXE:0x668dd9 button hold timer] the per-frame message 0x1e of a pressed button with style bit 0x20 (+0x48): state +0x15a 1 waits until now - +0x144 >= +0x13c (the delay), then sets state 2 and posts 0x10009 to the parent; state 2 posts 0x10008 each time now - +0x144 > +0x140 (the interval); both only while the button's pressed bit (+0x138 & 2) is set, and they restart +0x144; the ctor 0x6688b3 stores +0x13c = 500 ms and +0x140 = 0x7d = 125 ms for every button, and only a button with style bit 0x20 repeats (the scroll arrows are style 0x21); the press 0x668ecf sets state 1 and +0x144, the release 0x668eff clears the state and the click (0x100) follows only when it was not 2
public sealed record UiButtonRepeat( float Delay, float Interval, Action Start, Action Tick );

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
	/// <summary>The grey a disabled button takes when its model has no usable disable frame (the purple text buttons' disabled grey).</summary>
	public static readonly RgbaByte DisabledTint = new( 160, 160, 160, 255 );
	/// <summary>Share of each purple_button half outside its bar (16 of 64 texture rows); the bar is centred in the button.</summary>
	public const float PurpleBarGap = 0.25f;

	public string? Model { get; set; }
	public Func<string>? Text { get; set; }
	public Func<UiFonts, FontAtlas> Font { get; set; } = fonts => fonts.Menu;
	public Action? Clicked { get; set; }
	/// <summary>
	/// The hold-repeat of a button created with the repeat attribute (0x20 of its table attributes, 0x21 for the variable box's b_minus and b_plus): null for a plain button.
	/// <see cref="UiScreen"/> runs it while the button is held (<see cref="UiButtonRepeat"/>).
	/// </summary>
	public UiButtonRepeat? Repeat { get; set; }
	/// <summary>
	/// The <c>cat_ui</c> event a release plays. Defaults to <see cref="GameAudio.ClickEvent"/>. A button whose parent has style bit 0x10
	/// plays <see cref="GameAudio.SelectClickEvent"/> (<see cref="LayoutTable.ClickSoundFor"/>).
	/// </summary>
	public uint ClickSound { get; set; } = GameAudio.ClickEvent;
	/// <summary>First repeat of a held scroll arrow, in seconds (button ctor <c>0x6688b3</c> stores +0x13c = 500 for every button; only style bit 0x20 repeats).</summary>
	public const float ScrollArrowDelay = 0.5f;
	/// <summary>Later repeats of a held scroll arrow, in seconds (the same ctor stores +0x140 = 0x7d = 125).</summary>
	public const float ScrollArrowInterval = 0.125f;

	/// <summary>
	/// Hold-repeat for a list arrow: the first step after <see cref="ScrollArrowDelay"/>, then one every time more than
	/// <see cref="ScrollArrowInterval"/> has passed (timer <c>0x668dd9</c>). The release <c>0x668eff</c> posts no 0x100 once the hold
	/// state is 2, so a release after repeating has started plays nothing. <paramref name="step"/> scrolls one row and plays no sound of its own.
	/// </summary>
	public static UiButtonRepeat ScrollArrowRepeat( Action step ) => new( ScrollArrowDelay, ScrollArrowInterval, step, step );
	/// <summary>The hold state of a repeating button (the original's byte +0x15a): 0 idle, 1 pressed and waiting for the delay, 2 repeating.</summary>
	internal int HoldState { get; set; }
	/// <summary>The time of the press, then of the last start or tick, in seconds of <see cref="UiContext.Time"/> (the original's +0x144).</summary>
	internal float HoldTime { get; set; }
	/// <summary>Left/right keys on the focused button (e.g. turn the lobby to the next island).</summary>
	public Action<int>? Adjusted { get; set; }
	/// <summary>The mouse wheel over the button also calls <see cref="Adjusted"/> (OpenTPW page cycle buttons); off elsewhere, so the wheel over a lobby button does not turn the island.</summary>
	public bool WheelAdjusts { get; set; }
	public Func<bool>? Selected { get; set; }
	/// <summary>Keeps the normal frame while focused.</summary>
	public bool KeepNormalFrameWhenFocused { get; set; }
	public UiAlign Align { get; set; } = UiAlign.Center;
	/// <summary>A button that only shows its <see cref="Selected"/> state: drawn like an enabled one (the down frame while selected), never focused, hit or activated.</summary>
	public bool Inert { get; set; }
	public override bool Focusable => Visible && Enabled && !Inert;

	public override void Adjust( int direction ) => Adjusted?.Invoke( direction );

	/// <summary>The model frame the button draws in this state.</summary>
	internal int StateFrame( bool focused, bool pressed ) =>
		!Enabled ? DisabledFrame : pressed || Selected?.Invoke() == true ? DownFrame : focused && !KeepNormalFrameWhenFocused ? HighlightFrame : NormalFrame;

	public override void Activate()
	{
		if ( !Enabled || Inert )
			return;
		// [BIN:TPW-EXE:0x485780 UI message hook] installed once by the UI init 0x489ca0 (via 0x6590ea, stored at 0xfaa5fc) and called for every control message by the dispatcher 0x658d9f and the queue pump 0x658e42 (the call at 0x658eb2) before the control's own procedure. Message 0x100 plays cat_ui 0x1F, or 0xBD when the receiver's style word +0x48 has bit 0x10 (the choice at 0x4857cb). The button class 0x668f9c is the only sender of 0x100 for a mouse release over a button: it posts to the button's parent ([+0x60]) at 0x6691eb through the queue 0x658ccc, on a release over the button that was pressed. Window procedures also send 0x100 to their own window (0x48d83b, 0x49973e, 0x49ec53, 0x4a6df5 (via the dispatcher 0x658d9f), 0x4b640a, 0x4b9f06, 0x4bb020 and 0x4bfc29; ids -1, -2 and 0xe059, on 0x802 or 0x804), and the hook clicks for those too by that window's own style (style 1 in all eight: 0x1F); OpenTPW's dialogs route Enter to their tick's Activate there (docs/AUDIO.md, "List owners"). A release after auto-repeat has started (hold state +0x15a is 2, 0x668eff) returns false and posts no 0x100, so the sound is skipped then too.
		if ( HoldState != 2 )
			GameAudio.PlayUi( ClickSound );
		Clicked?.Invoke();
	}

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		var rect = ScreenRect( context.Canvas );
		var frame = StateFrame( focused, pressed );
		RgbaByte? tint = null;
		// [EXT:missing-state-texture] a state frame whose texture ui.wad lacks (the disable frames of b_callmech, b_callhandy and b_upgrade name b_callmechg, b_callhandyg
		// and b_upgradeg) draws the normal frame instead of untextured white, greyed when disabled; the original's mesh texture binder 0x46EAD0 (which the UI model loaders 0x488D20,
		// 0x488EE0 and 0x489060 call) logs "Could not load texture" and binds entry 0 of the texture table (+0x4C) instead — evidence needed: which texture that entry holds at run time
		if ( Model != null && frame != NormalFrame && !context.FrameTexturesResolve( Model, frame ) )
		{
			if ( frame == DisabledFrame )
				tint = DisabledTint;
			frame = NormalFrame;
		}
		if ( Model != null && context.DrawModel( Model, frame, rect, tint ) )
		{
			if ( Text != null )
				context.DrawFittedText( Font( context.Fonts ), Text(), rect, focused ? UiColors.Highlight : UiColors.Text, Align );
			return;
		}
		// [APPROX:UI-008] purple_button as a mirrored end cap, upper half normal, lower half focused/pressed; no UI model of the PC's ui.wad names it — evidence needed: which PC screens draw a model-less text button and with which art, or a capture of the original front-end buttons
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
/// A horizontal slider in the style of the original options screen: a <c>b_scroller</c> ball on a track,
/// <see cref="UiElement.Bounds"/> being the mouse hit region. Values are step indices 0..Steps-1.
/// </summary>
public sealed class UiSlider : UiElement, IUiDragTarget
{
	public const string KnobModel = "b_scroller";

	/// <summary>The ball's model: <see cref="KnobModel"/>, or the table's own thumb child (the object windows use <c>b_scrollera</c>).</summary>
	public string Knob { get; set; } = KnobModel;
	/// <summary>The slider control's own model drawn under the ball over <see cref="UiElement.Bounds"/> (the object windows' <c>slider_w</c>, <c>slider_n</c>, <c>slider_q</c>); none when null.</summary>
	public string? TrackModel { get; set; }

	/// <summary>Authored track the ball runs along.</summary>
	public UiRect Track { get; set; }
	/// <summary>Authored size of the ball.</summary>
	public NVector2 KnobSize { get; set; } = new( 67, 67 );
	/// <summary>Authored vertical position of the ball's top.</summary>
	public float KnobTop { get; set; }
	public Func<int> Steps { get; set; } = () => 2;
	public Func<int> Value { get; set; } = () => 0;
	public Action<int>? Changed { get; set; }
	/// <summary>The thumb was pressed (an original-class slider, <see cref="Truncates"/>): the original's slider posts 0x801 to its owner with the value.</summary>
	public Action<int>? Grabbed { get; set; }
	/// <summary>The thumb was let go (0x802).</summary>
	public Action? Released { get; set; }
	/// <summary>Truncate a drag position to its value instead of taking the nearest value (the original's slider class, used by the research effort sliders).</summary>
	// [BIN:TPW-EXE:0x66afc9 slider drag] value = min + trunc((thumbLeft - trackLeft) * (max - min) / (trackWidth - thumbWidth)); the research effort sliders 0xd2d0b..0xd2d0f are this class with SetRange(0,100) (0x4aa6bd..0x4aa725)
	public bool Truncates { get; set; }
	public override bool Focusable => Visible && Enabled;

	/// <summary>
	/// Authored ball rectangle for <paramref name="value"/>: the ball's centre moves linearly from half a ball
	/// inside the track's left end to half a ball inside its right end.
	/// </summary>
	// [BIN:TPW-EXE:0x66b088 slider thumb] the original's slider class (vtable 0x705b20) places the thumb at track left + (value - min) × (track width - thumb width) / (max - min) in integer units; the options page's sliders are 0..100 (0x4a455a)
	// [EXT:options-slider-steps] OpenTPW's option sliders step through its own graphics presets and 11 volume steps and take the nearest step; the original's are 0..100 with the change handler 0x4a2e90 mapping the value to a resolution index (v × 2) / 100 and a quality index (k × v) / 100 (k 2, or 1 when [0x78d8d8] is 0)
	// [BIN:TPW-EXE:0x4a455a options slider range] the options page creates its sliders with the range 0..100 and no page setting (the default 1 stays), its change handler 0x4a2e90 only updates the setting's text and leaves the knob where it was dragged; the original-class mechanics are built for the sliders that use Truncates (research effort, object settings), and the options page's own presets and volume steps are [EXT:options-slider-steps]
	public UiRect KnobRect( int value )
	{
		var steps = Math.Max( 2, Steps() );
		var fraction = Math.Clamp( value, 0, steps - 1 ) / (float)(steps - 1);
		var left = Track.X + (Track.Width - KnobSize.X) * fraction;
		return new UiRect( left, KnobTop, KnobSize.X, KnobSize.Y );
	}

	/// <summary>Value whose ball centre is nearest to the authored x position <paramref name="authoredX"/>.</summary>
	public int ValueAt( float authoredX )
	{
		var steps = Math.Max( 2, Steps() );
		var travel = Track.Width - KnobSize.X;
		if ( travel <= 0 )
			return 0;
		var fraction = (authoredX - Track.X - KnobSize.X / 2) / travel;
		fraction = Math.Clamp( fraction, 0f, 1f );
		return Math.Clamp( Truncates ? (int)MathF.Floor( fraction * (steps - 1) + 1e-4f ) : (int)MathF.Round( fraction * (steps - 1) ), 0, steps - 1 );
	}

	// the press of an original-class slider (Truncates): on the thumb it grabs it (the thumb keeps the offset of the grab), on the track it moves one page toward the click
	private enum PressMode { None, Grab, Page }
	private PressMode pressMode;
	private float grabOffset;

	/// <summary>Value change of a click on the track beside the thumb (the original's page, +0x148, default 1 from 0x66adc9).</summary>
	public int Page { get; set; } = 1;

	private float AuthoredX( UiCanvas canvas, NVector2 point )
	{
		var rect = ScreenRect( canvas );
		return Bounds.X + (point.X - rect.X) / rect.Width * Bounds.Width;
	}

	void IUiDragTarget.DragBegin( UiCanvas canvas, NVector2 point )
	{
		pressMode = PressMode.None;
		if ( !Truncates || !Enabled || ScreenRect( canvas ).Width <= 0 )
			return;
		var x = AuthoredX( canvas, point );
		var knob = KnobRect( Value() );
		// [BIN:TPW-EXE:0x66b8aa slider track click] a click off the thumb is compared with the thumb's far edge (horizontal: its right edge) and calls the page move 0x66b088 (mode 2 or 3, +-0x148),
		// inverted for a slider whose step +0x144 is positive (the default): left of the thumb lowers the value, right of it raises it; 0x66b088 clamps to the range and posts 0x800 when the value changed
		if ( x >= knob.X && x <= knob.Right )
		{
			pressMode = PressMode.Grab;
			grabOffset = x - (knob.X + knob.Width / 2);
			Grabbed?.Invoke( Value() );
		}
		else
		{
			pressMode = PressMode.Page;
			var steps = Math.Max( 2, Steps() );
			Set( Math.Clamp( Value() + (x < knob.Right ? -Page : Page), 0, steps - 1 ) );
		}
	}

	void IUiDragTarget.DragEnd( UiCanvas canvas, NVector2 point )
	{
		if ( pressMode == PressMode.Grab )
			Released?.Invoke();
		pressMode = PressMode.None;
	}

	void IUiDragTarget.DragTo( UiCanvas canvas, NVector2 point )
	{
		if ( pressMode == PressMode.Page )
			return;
		if ( pressMode == PressMode.Grab )
		{
			if ( !Enabled || ScreenRect( canvas ).Width <= 0 )
				return;
			// [BIN:TPW-EXE:0x66af19 slider drag] the thumb follows the pointer at the grabbed offset; the value is read from the thumb's left edge
			Set( ValueAt( AuthoredX( canvas, point ) - grabOffset ) );
			return;
		}
		SetFromMouse( canvas, point );
	}

	/// <summary>Sets the value from a mouse position in framebuffer pixels.</summary>
	public void SetFromMouse( UiCanvas canvas, NVector2 point )
	{
		if ( !Enabled )
			return;
		var rect = ScreenRect( canvas );
		if ( rect.Width <= 0 )
			return;
		Set( ValueAt( Bounds.X + (point.X - rect.X) / rect.Width * Bounds.Width ) );
	}

	private void Set( int value )
	{
		if ( value != Value() )
			Changed?.Invoke( value );
	}

	public override void Adjust( int direction )
	{
		if ( Enabled )
			Set( Math.Clamp( Value() + direction, 0, Math.Max( 2, Steps() ) - 1 ) );
	}

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		if ( TrackModel != null )
			context.DrawModel( TrackModel, 0, ScreenRect( context.Canvas ) );
		var knob = KnobRect( Value() );
		var rect = context.Canvas.Map( knob, Anchor );
		if ( !context.DrawModel( Knob, 0, rect ) )
			context.Batch.AddRectangle( rect, UiColors.Value );
	}
}

/// <summary>
/// An options row of the OpenTPW page in the style of the original options panel (f_optpanel2 bar): label on the left,
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
		// [EXT:opentpw-page] option-row arrow/value positions: OpenTPW's own row style for its page (the original page uses sliders and toggles)
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

