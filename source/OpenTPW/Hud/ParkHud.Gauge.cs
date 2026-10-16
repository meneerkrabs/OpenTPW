using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Hud;

/// <summary>The happiness gauge on the main panel (tooltip 477 "Happiness of the park visitors").</summary>
public sealed partial class ParkHud
{
	/// <summary>How often the gauge resamples the visitors' mean happiness (seconds).</summary>
	// [BIN:TPW-EXE:0x4a1cd0 gauge refresh] the gauge control's procedure re-arms the timer 0x80083 at 2000 ms and on each tick sets the gauge to ftol(mean x 1024 x 0.01), the mean being 0x4c7bb0's
	internal const float HappinessSampleSeconds = 2f;
	private float? parkHappiness;
	private float happinessSampled;

	/// <summary>The mean happiness (0..100) of the visitors in the park, sampled for the gauge; null when none is in or the park is closed.</summary>
	public float? ParkHappiness => parkHappiness;

	private void SampleHappiness( float delta )
	{
		happinessSampled -= delta;
		if ( happinessSampled > 0 )
			return;
		happinessSampled = HappinessSampleSeconds;
		var guests = level.Guests;
		parkHappiness = guests == null ? null : HappinessMean( guests.Guests.Where( guests.IsInPark ).Select( guest => guest.Happiness ), FinanceEconomy?.IsParkOpen ?? true );
	}

	/// <summary>The gauge's mean: each in-park visitor's happiness truncated to a byte, averaged; null (an empty dish) while the park is closed or nobody is in.</summary>
	// [BIN:TPW-EXE:0x4c7bb0 gauge mean] returns 0.0 while the park-closed word (+0x1da710) is set, else the sum of the byte-truncated happiness (+0x19c) of the in-park guests (kind byte 1, on a park cell 0x4fa990) over their count, 0.0 without any
	internal static float? HappinessMean( IEnumerable<float> inPark, bool parkOpen )
	{
		if ( !parkOpen )
			return null;
		int count = 0, sum = 0;
		foreach ( var happiness in inPark )
		{
			count++;
			sum += (byte)(int)happiness;
		}
		return count == 0 ? null : (float)sum / count;
	}

	/// <summary>The value the original sets the gauge to for a mean happiness: ftol(mean x 1024 x 0.01) clamped to 0..1024 (the type-9 gauge's range, setter 0x66d885).</summary>
	// [BIN:TPW-EXE:0x4a1d44 gauge value] the mean is multiplied by the float constants 1024.0 (0x6ffa34) and 0.01f (0x6ffa38) and truncated, so a full mean of 100 gives 1023, not 1024: 0.01f is a hair under 0.01
	internal static int GaugeValue( float mean ) => (int)Math.Clamp( Math.Truncate( mean * 1024.0 * (double)0.01f ), 0, 1024 );

	/// <summary>Fraction of the gauge dish the mean happiness fills: the original's 0..1024 value over its range, null without a mean.</summary>
	internal static float? GaugeFill( float? happiness ) => happiness is { } value ? GaugeValue( value ) / 1024f : null;

	/// <summary>The fill colour of a mean happiness 0..100: red through yellow to green.</summary>
	internal static RgbaByte GaugeColour( float happiness )
	{
		var t = Math.Clamp( happiness / 100f, 0f, 1f ) * 2f;
		var red = t <= 1 ? 230 + 5 * t : 235 - 155 * (t - 1);
		var green = t <= 1 ? 60 + 150 * t : 210 + 10 * (t - 1);
		var blue = t <= 1 ? 60 + 20 * t : 80;
		return new RgbaByte( (byte)MathF.Round( red ), (byte)MathF.Round( green ), (byte)MathF.Round( blue ), 210 );
	}

	/// <summary>The rising fill of the gauge dish: the sampled visitors' mean happiness drawn over the model's empty dish.</summary>
	private sealed class HappinessGaugeFill( ParkHud owner ) : UiElement
	{
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			// The model's quad is the empty dish; the fill rises in its middle third, clear of the rim.
			if ( GaugeFill( owner.parkHappiness ) is not { } fraction )
				return;
			var dish = ScreenRect( context.Canvas );
			var x = dish.X + dish.Width * 0.32f;
			var width = dish.Width * 0.36f;
			var bottom = dish.Y + dish.Height * 0.92f;
			var top = dish.Y + dish.Height * (0.92f - 0.82f * fraction);
			context.Batch.AddRectangle( new UiRect( x, top, width, bottom - top ), GaugeColour( owner.parkHappiness!.Value ) );
		}
	}
}
