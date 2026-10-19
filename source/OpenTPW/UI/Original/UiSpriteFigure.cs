using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>
/// One sprite frame for a <see cref="UiSpriteFigure"/>: the atlas texture, the frame's rectangle in it (pixels), its hotspot offset (the frame's
/// top-left corner relative to the figure's feet, pixels), whether it is drawn mirrored and the factors on the size of a pixel across and down (128 / A and 128 / B of the
/// frame's pixels-per-unit fields, <see cref="GuestAtlasFrame.ScaleX"/>; 1 for every shipped frame).
/// </summary>
public readonly record struct UiSpriteFrame( Veldrid.Texture Texture, int AtlasWidth, int AtlasHeight, int X, int Y, int Width, int Height, int OriginX, int OriginY, bool Mirror, float ScaleX = 1f, float ScaleY = 1f );

/// <summary>
/// A sprite figure standing in a control's rectangle: its feet at the rectangle's horizontal centre, a quarter of the rectangle's height above its
/// bottom edge by default, or at an explicit authored hotspot. Each sprite pixel spans <see cref="CanvasUnitsPerPixel"/> canvas units.
/// </summary>
public sealed class UiSpriteFigure : UiElement
{
	/// <summary>The frame to draw now; null draws nothing.</summary>
	public Func<UiSpriteFrame?> Frame { get; set; } = () => null;

	/// <summary>Canvas units per sprite pixel (the canvas is 2048 units wide).</summary>
	public float CanvasUnitsPerPixel { get; init; } = 3.2f;

	/// <summary>Optional authored-canvas hotspot, mapped before scaling. Null preserves the shared rectangle-relative placement.</summary>
	public NVector2? FeetInCanvas { get; init; }

	/// <summary>Where the figure's feet stand in <paramref name="rect"/>, in the rectangle's own units.</summary>
	public static NVector2 Feet( UiRect rect ) => new( rect.X + rect.Width / 2, rect.Bottom - rect.Height / 4 );

	/// <summary>The screen rectangle and texture coordinates of <paramref name="frame"/> with its feet at <paramref name="feet"/>, <paramref name="pixel"/> screen units per sprite pixel.</summary>
	public static (UiRect Rect, NVector2 Uv0, NVector2 Uv1) Layout( UiSpriteFrame frame, NVector2 feet, float pixel )
	{
		var left = frame.Mirror ? -(frame.OriginX + frame.Width) : frame.OriginX;
		var pixelX = pixel * frame.ScaleX;
		var pixelY = pixel * frame.ScaleY;
		var rect = new UiRect( feet.X + left * pixelX, feet.Y + frame.OriginY * pixelY, frame.Width * pixelX, frame.Height * pixelY );
		var u0 = frame.X / (float)frame.AtlasWidth;
		var u1 = (frame.X + frame.Width) / (float)frame.AtlasWidth;
		if ( frame.Mirror )
			(u0, u1) = (u1, u0);
		return (rect, new NVector2( u0, frame.Y / (float)frame.AtlasHeight ), new NVector2( u1, (frame.Y + frame.Height) / (float)frame.AtlasHeight ));
	}

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		if ( Frame() is not { } frame || Bounds.Width <= 0 )
			return;
		var rect = ScreenRect( context.Canvas );
		var feet = FeetInCanvas is { } authored ? context.Canvas.Map( authored, Anchor ) : Feet( rect );
		var (quad, uv0, uv1) = Layout( frame, feet, CanvasUnitsPerPixel * rect.Width / Bounds.Width );
		context.Batch.AddQuad( UiTexture.Of( frame.Texture ), quad, uv0, uv1, RgbaByte.White );
	}
}
