using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

public enum UiAlign { Left, Center, Right }

/// <summary>Original UI models by name, loaded once; missing models are reported once and drawn as fallbacks.</summary>
public sealed class UiModels
{
	private readonly Dictionary<string, UiModel?> models = new( StringComparer.OrdinalIgnoreCase );
	private readonly Func<string, UiModel> load;

	public UiModels( Func<string, UiModel>? load = null ) => this.load = load ?? UiModel.Load;

	public UiModel? Get( string name )
	{
		if ( models.TryGetValue( name, out var model ) )
			return model;
		try { model = load( name ); }
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or InvalidOperationException or ArgumentException )
		{
			Log?.Warning( $"Original UI model {name} unavailable: {exception.Message}" );
			model = null;
		}
		models[name] = model;
		return model;
	}
}

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
	public void DrawText( FontAtlas font, string text, UiRect rect, RgbaByte color, UiAlign align = UiAlign.Left, bool wrap = false, bool shadow = true )
	{
		if ( string.IsNullOrEmpty( text ) )
			return;
		var scale = Canvas.TextScale;
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
			var texture = path == null ? UiTexture.Solid : UiTexture.Image( path );
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

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
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
				context.DrawText( Font( context.Fonts ), Text(), rect, focused ? UiColors.Highlight : UiColors.Text, Align );
			return;
		}
		// [APPROX:UI-008] purple_button halves for normal/focused — evidence needed: capture of the original front-end buttons
		var art = context.ResolveTexture( "purple_button" );
		if ( art != null )
		{
			var lower = focused || pressed || Selected?.Invoke() == true;
			context.Batch.AddQuad( UiTexture.Image( art ), rect, new NVector2( 0, lower ? 0.5f : 0 ), new NVector2( 1, lower ? 1f : 0.5f ), Enabled ? RgbaByte.White : new RgbaByte( 160, 160, 160, 255 ) );
		}
		else
			context.Batch.AddRectangle( rect, focused ? UiColors.Highlight : UiColors.HelpBackground );
		var inner = new UiRect( rect.X + rect.Height * 0.4f, rect.Y, rect.Width - rect.Height * 0.8f, rect.Height );
		context.DrawText( Font( context.Fonts ), Text?.Invoke() ?? "", inner, !Enabled ? UiColors.Disabled : focused ? UiColors.Highlight : UiColors.Text, Align );
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
