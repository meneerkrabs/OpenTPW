using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>Which framebuffer edge an element keeps its authored distance to.</summary>
public enum UiAnchor
{
	TopLeft, Top, TopRight,
	Left, Center, Right,
	BottomLeft, Bottom, BottomRight
}

/// <summary>
/// The single UI scale helper. Original UI models are authored in a 2048×1536 virtual screen
/// (docs/UI.md); the canvas scales it uniformly to fit the framebuffer (times an optional user UI
/// scale) and keeps each element at its authored distance from its anchor edge, so corner HUD
/// elements stay in the corners on wide screens. Anchoring is an OpenTPW policy for non-4:3 outputs;
/// on a 4:3 output at UI scale 1 every anchor gives the authored layout.
/// </summary>
public readonly record struct UiCanvas( int Width, int Height, float UserScale = 1f )
{
	public const float VirtualWidth = 2048;
	public const float VirtualHeight = 1536;

	/// <summary>Framebuffer pixels per virtual unit.</summary>
	public float Scale => Math.Max( 0.05f, Math.Min( Width / VirtualWidth, Height / VirtualHeight ) * Math.Clamp( UserScale, 0.5f, 3f ) );

	/// <summary>
	/// Font tier for this output: SMALL up to 640×480-like scales, MED up to about 1024×768 and
	/// 1280×720, BIG above (inferred from the three shipped sizes).
	/// </summary>
	public UiFontTier FontTier => Scale < 0.36f ? UiFontTier.Small : Scale < 0.6f ? UiFontTier.Medium : UiFontTier.Big;

	/// <summary>Integer pixel scale for BF4 text (point sampled, so it stays crisp) on very large outputs.</summary>
	public int TextScale => Math.Max( 1, (int)MathF.Floor( Scale / 0.7f + 0.001f ) );

	/// <summary>Maps an authored canvas point to framebuffer pixels.</summary>
	public NVector2 Map( NVector2 point, UiAnchor anchor )
	{
		var scale = Scale;
		var column = (int)anchor % 3;
		var row = (int)anchor / 3;
		var x = column switch
		{
			0 => point.X * scale,
			1 => Width / 2f + (point.X - VirtualWidth / 2) * scale,
			_ => Width - (VirtualWidth - point.X) * scale
		};
		var y = row switch
		{
			0 => point.Y * scale,
			1 => Height / 2f + (point.Y - VirtualHeight / 2) * scale,
			_ => Height - (VirtualHeight - point.Y) * scale
		};
		return new NVector2( x, y );
	}

	/// <summary>Maps an authored rectangle; the size scales uniformly.</summary>
	public UiRect Map( UiRect rect, UiAnchor anchor )
	{
		var topLeft = Map( new NVector2( rect.X, rect.Y ), anchor );
		return new UiRect( topLeft.X, topLeft.Y, rect.Width * Scale, rect.Height * Scale );
	}

	/// <summary>Anchor implied by where an authored point lies (thirds of the virtual screen).</summary>
	public static UiAnchor AnchorFor( NVector2 point )
	{
		var column = point.X < VirtualWidth / 3 ? 0 : point.X > VirtualWidth * 2 / 3 ? 2 : 1;
		var row = point.Y < VirtualHeight / 3 ? 0 : point.Y > VirtualHeight * 2 / 3 ? 2 : 1;
		return (UiAnchor)(row * 3 + column);
	}
}

/// <summary>Axis-aligned rectangle, x right and y down.</summary>
public readonly record struct UiRect( float X, float Y, float Width, float Height )
{
	public float Right => X + Width;
	public float Bottom => Y + Height;
	public NVector2 Center => new( X + Width / 2, Y + Height / 2 );
	public bool Contains( NVector2 point ) => point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;
	public static UiRect FromCenter( NVector2 center, float width, float height ) => new( center.X - width / 2, center.Y - height / 2, width, height );
	public UiRect Inflate( float amount ) => new( X - amount, Y - amount, Width + amount * 2, Height + amount * 2 );
}
