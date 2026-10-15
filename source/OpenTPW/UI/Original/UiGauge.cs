using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>
/// The red-to-green gauge bar of the original windows (the type-9 controls: the hire window's skill, the ride and buy windows' excitement,
/// reliability, repair and life): a dark track, filled from the left by a fixed gradient that is cut off at the value, so a low value shows only the red and
/// orange end and a full bar reaches green.
/// </summary>
// [APPROX:UI-046] the gradient stops and the track colours are read off the 720p video frames (buy window 1:50, hire window 12:00: a 70 % bar runs red, orange, yellow to lime green; a 12 % bar is red/orange), not from the original's gauge art, whose texture is not identified
public static class UiGauge
{
	private static readonly (float At, RgbaByte Colour)[] Stops =
	{
		(0.00f, new( 255, 40, 0, 255 )),
		(0.15f, new( 255, 100, 0, 255 )),
		(0.35f, new( 255, 200, 0, 255 )),
		(0.50f, new( 230, 225, 0, 255 )),
		(0.72f, new( 130, 215, 0, 255 )),
		(1.00f, new( 90, 205, 0, 255 )),
	};
	private const int Segments = 32;

	/// <summary>The track behind the fill.</summary>
	public static readonly RgbaByte Track = new( 62, 62, 72, 255 );

	/// <summary>The gradient colour at <paramref name="t"/> (0 = red end, 1 = green end) of the full gauge width.</summary>
	public static RgbaByte ColourAt( float t )
	{
		t = Math.Clamp( t, 0f, 1f );
		for ( var index = 1; index < Stops.Length; index++ )
		{
			if ( t > Stops[index].At )
				continue;
			var (from, to) = (Stops[index - 1], Stops[index]);
			var k = (t - from.At) / (to.At - from.At);
			byte Mix( byte a, byte b ) => (byte)MathF.Round( a + (b - a) * k );
			return new RgbaByte( Mix( from.Colour.R, to.Colour.R ), Mix( from.Colour.G, to.Colour.G ), Mix( from.Colour.B, to.Colour.B ), 255 );
		}
		return Stops[^1].Colour;
	}

	/// <summary>Draws a gauge in <paramref name="track"/> filled to <paramref name="fraction"/> (0..1); null or 0 draws the empty track.</summary>
	public static void Draw( UiBatch batch, UiRect track, float? fraction )
	{
		batch.AddRectangle( track, Track );
		var value = Math.Clamp( fraction ?? 0f, 0f, 1f );
		if ( value <= 0 )
			return;
		var fillWidth = track.Width * value;
		var count = Math.Max( 1, (int)MathF.Ceiling( Segments * value ) );
		for ( var index = 0; index < count; index++ )
		{
			var left = fillWidth * index / count;
			var right = fillWidth * (index + 1) / count;
			var leftColour = ColourAt( left / track.Width );
			var rightColour = ColourAt( right / track.Width );
			var (x0, x1, y0, y1) = (track.X + left, track.X + right, track.Y, track.Bottom);
			var topLeft = new UiVertex( new NVector2( x0, y0 ), NVector2.Zero, leftColour );
			var bottomLeft = new UiVertex( new NVector2( x0, y1 ), NVector2.Zero, leftColour );
			var topRight = new UiVertex( new NVector2( x1, y0 ), NVector2.Zero, rightColour );
			var bottomRight = new UiVertex( new NVector2( x1, y1 ), NVector2.Zero, rightColour );
			batch.AddTriangle( UiTexture.Solid, topLeft, bottomLeft, topRight );
			batch.AddTriangle( UiTexture.Solid, topRight, bottomLeft, bottomRight );
		}
	}
}

/// <summary>A gauge element: the value is read every frame; null shows the empty track.</summary>
public sealed class UiGaugeBar : UiElement
{
	public Func<float?> Value { get; set; } = () => null;

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		var rect = ScreenRect( context.Canvas );
		UiGauge.Draw( context.Batch, rect.Inflate( -rect.Height * 0.12f ), Value() );
	}
}

/// <summary>The small tick box of the buy list's third column: an empty box, or a box with a green tick.</summary>
// [APPROX:UI-050] the box and the tick are drawn with rectangles and thick strokes in the colours of the video frames (buy window 1:50: a dark blue box outline, a green tick); the original's art for the box is not identified
public static class UiTickBox
{
	private static readonly RgbaByte Outline = new( 40, 130, 190, 255 );
	private static readonly RgbaByte Inside = new( 6, 20, 40, 255 );
	private static readonly RgbaByte Tick = new( 60, 220, 70, 255 );

	/// <summary>Draws the box centred in <paramref name="cell"/>, sized from the cell's height.</summary>
	public static void Draw( UiBatch batch, UiRect cell, bool ticked )
	{
		var size = MathF.Min( cell.Height * 0.62f, cell.Width );
		var box = new UiRect( cell.X + (cell.Width - size) / 2, cell.Y + (cell.Height - size) / 2, size, size );
		var edge = MathF.Max( 1f, size * 0.12f );
		batch.AddRectangle( box, Outline );
		batch.AddRectangle( box.Inflate( -edge ), Inside );
		if ( !ticked )
			return;
		// Two strokes: down to the foot, then up and out to the right, reaching past the box like the original's tick.
		var foot = new NVector2( box.X + size * 0.30f, box.Y + size * 0.78f );
		Stroke( batch, new NVector2( box.X + size * 0.08f, box.Y + size * 0.48f ), foot, MathF.Max( 1.5f, size * 0.2f ) );
		Stroke( batch, foot, new NVector2( box.X + size * 1.08f, box.Y - size * 0.06f ), MathF.Max( 1.5f, size * 0.2f ) );
	}

	private static void Stroke( UiBatch batch, NVector2 from, NVector2 to, float width )
	{
		var direction = NVector2.Normalize( to - from );
		var normal = new NVector2( -direction.Y, direction.X ) * width / 2;
		var a = new UiVertex( from + normal, NVector2.Zero, Tick );
		var b = new UiVertex( from - normal, NVector2.Zero, Tick );
		var c = new UiVertex( to + normal, NVector2.Zero, Tick );
		var d = new UiVertex( to - normal, NVector2.Zero, Tick );
		batch.AddTriangle( UiTexture.Solid, a, b, c );
		batch.AddTriangle( UiTexture.Solid, c, b, d );
	}
}

/// <summary>A filled disc drawn as a triangle fan (the tube's white message icons).</summary>
public static class UiDisc
{
	public static void Draw( UiBatch batch, NVector2 centre, float radius, RgbaByte colour, int segments = 20 )
	{
		var hub = new UiVertex( centre, NVector2.Zero, colour );
		for ( var index = 0; index < segments; index++ )
		{
			var a = index * MathF.Tau / segments;
			var b = (index + 1) * MathF.Tau / segments;
			batch.AddTriangle( UiTexture.Solid, hub,
				new UiVertex( centre + new NVector2( MathF.Cos( a ), MathF.Sin( a ) ) * radius, NVector2.Zero, colour ),
				new UiVertex( centre + new NVector2( MathF.Cos( b ), MathF.Sin( b ) ) * radius, NVector2.Zero, colour ) );
		}
	}
}
