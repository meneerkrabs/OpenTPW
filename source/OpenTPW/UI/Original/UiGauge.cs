using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>
/// The red-to-green gauge bar of the original windows (the type-9 controls: the hire window's skill, the ride and buy windows' excitement,
/// reliability, repair and life): the art <c>ridestatbar.wct</c> (32 x 32 texels), whose top half is the gradient bar and whose bottom half is the dark track.
/// The track fills the control's bar rectangle and the bar is the art cut off at the value (its u runs 0 to the value, not stretched).
/// </summary>
// [BIN:TPW-EXE:0x477ff0 gauge model] the gauge's two models are built from the texture ridestatbar.wct (string 0x750e00, used by the builders of the buy, ride, shop, sideshow, attractions, visitors, employee and staff windows); the draw cuts the bar quad at value / 1024 of its width with the texture's u at the same share (0x478400, see FullValue), so the colours are the art's: red at the left end running through orange and yellow to green (a 20 % bar of the hire window shows red to orange, as the video's guard candidate of 12:00 does)
public static class UiGauge
{
	/// <summary>The gauge art (the original's <c>ridestatbar.wct</c>), 32 x 32 texels: rows 0-15 the bar, rows 16-31 the track.</summary>
	public const string ArtPath = "/ui/textures/ridestatbar.wct";
	private const float ArtSize = 32f;
/// <summary>The range of a type-9 gauge's value (0x66d885 clamps it to 0..0x400).</summary>
	public const int FullValue = 1024;

	/// <summary>
	/// The share a type-9 gauge shows for the percentage <paramref name="percent"/>: the windows set <c>(percent &lt;&lt; 10) / 100</c> in integer steps, which the
	/// control clamps to 0..1024.
	/// </summary>
	// [BIN:TPW-EXE:0x66d885 gauge SetValue] the value is clamped to 0..0x400 and sent to the gauge's two models with message 8, whose handler (0x478400) turns it into value / 1024 × 100 percent of the bar's width: vertices 1, 2, 4 and 5 move to that percent and the texture's u of the fill quad to percent / 100 (0x4785a2..0x478675), so the fill is the art cut off at the value, not stretched
	public static float OfPercent( int percent ) => Math.Clamp( (int)(((long)percent << 10) / 100), 0, FullValue ) / (float)FullValue;

	/// <summary>
	/// The vertical inset of a gauge's bar in its control's rectangle, in virtual canvas units: each window's builder calls <c>0x485e30(gauge, 0, 10)</c> on the gauge control, which
	/// deflates its rectangle by 10 units at the top and at the bottom (<c>0x65a43e</c>: top + 10, bottom - 10), and the bar's model fills that rectangle exactly (0x476c50 maps the
	/// model's bounds to it), so a 45 unit cell has a 25 unit bar.
	/// </summary>
	// [BIN:TPW-EXE:0x485e30 gauge inset] the callers 0x4ab6b0 (buy window gauges 0x1ee, 0x1ef, 0x1f0), 0x4ad890 (ride window 0x3e16..0x3e19), 0x4afc10 and 0x4b1040 (shop and sideshow windows) all push 0 and 10; the attractions and visitors lists (0x496620: 8, 0x4939d0 and 0x494690: 10) and the employee and training windows use their own
	public const float BarInsetUnits = 10;

	/// <summary>The bar's rectangle in a gauge control's screen rectangle <paramref name="control"/>: deflated vertically by <paramref name="insetUnits"/> canvas units.</summary>
	public static UiRect BarRect( UiRect control, UiCanvas canvas, float insetUnits = BarInsetUnits ) =>
		new( control.X, control.Y + insetUnits * canvas.Scale, control.Width, Math.Max( 0f, control.Height - 2 * insetUnits * canvas.Scale ) );

	/// <summary>Draws a gauge in <paramref name="track"/> filled to <paramref name="fraction"/> (0..1); null or 0 draws the empty track.</summary>
	public static void Draw( UiBatch batch, UiRect track, float? fraction )
	{
		var texture = UiTexture.Image( ArtPath );
		// Half a texel inside each half keeps the linear filter from mixing the bar's last row with the track's first.
		const float barTop = 0.5f / ArtSize, barBottom = 15.5f / ArtSize, trackTop = 16.5f / ArtSize, trackBottom = 31.5f / ArtSize;
		batch.AddQuad( texture, track, new NVector2( 0.5f / ArtSize, trackTop ), new NVector2( 31.5f / ArtSize, trackBottom ), RgbaByte.White );
		var value = Math.Clamp( fraction ?? 0f, 0f, 1f );
		if ( value <= 0 )
			return;
		var bar = new UiRect( track.X, track.Y, track.Width * value, track.Height );
		var uEnd = (0.5f + 31f * value) / ArtSize;
		batch.AddQuad( texture, bar, new NVector2( 0.5f / ArtSize, barTop ), new NVector2( uEnd, barBottom ), RgbaByte.White );
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
// [APPROX:UI-050] the box and the tick are drawn with rectangles and thick strokes in the colours of the video frames (buy window 1:50: a dark blue box outline, a green tick); the original's row cell (0x4ad340) sets the model i_boxtick (0x755144) and a state 1 owned, 2 first three of a category, 0 otherwise (0x4aaf70), but the frame each state draws is not read
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
