using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>
/// A model window driven by a <see cref="UiWindowAnimation"/>: its rectangle moves by the animation's edge offsets and its model turns by the animation's angle
/// about a fixed pivot (the winning screen's banner).
/// </summary>
public sealed class UiAnimatedModel : UiElement
{
	public string Model { get; set; } = "";

	/// <summary>The animation, started by the owner; null draws the model at rest.</summary>
	public UiWindowAnimation? Animation { get; set; }

	/// <summary>The point the model turns about, in canvas units, mapped with the element's <see cref="UiElement.Anchor"/>.</summary>
	public NVector2 Pivot { get; set; }

	/// <summary>
	/// The pivot a window takes from its own rectangle: half its right and half its bottom edge (the original halves the right and bottom words of the rectangle, not its
	/// width and height, so a window away from the origin turns about a point up and left of its middle).
	/// </summary>
	// [BIN:TPW-EXE:0x0048AF66 banner pivot] 0x65D42E copies the banner's rectangle (left, top, right, bottom words, 0x659E3E reads width = right - left) and the right and bottom words, each shifted right by 1 (sar), go to 0x65F345 as the pivot (+0xE4, +0xE6), which the model takes as an absolute canvas point (0x476C50)
	public static NVector2 PivotOf( UiRect rect ) => new( (short)rect.Right >> 1, (short)rect.Bottom >> 1 );

	/// <summary>The rectangle as the animation has moved it now.</summary>
	public UiRect AnimatedBounds
	{
		get
		{
			if ( Animation is not { } animation )
				return Bounds;
			var (left, top, right, bottom) = animation.Offset;
			return new UiRect( Bounds.X + left, Bounds.Y + top, Bounds.Width + right - left, Bounds.Height + bottom - top );
		}
	}

	/// <summary>The animation's angle in radians, clockwise on the screen (4096 a turn).</summary>
	public float Turn => Animation is { Angle: var angle } && angle % 4096 != 0 ? angle * MathF.PI / 2048f : 0f;

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		Animation?.Update( (long)Math.Floor( context.Time * 1000.0 ) );
		if ( context.Models.Get( Model ) is not { } model )
			return;
		var rect = context.Canvas.Map( AnimatedBounds, Anchor );
		context.DrawFrame( model.GetFrame( 0 ), rect, null, Turn, context.Canvas.Map( Pivot, Anchor ) );
	}
}
