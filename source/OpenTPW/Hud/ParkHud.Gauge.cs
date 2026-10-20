using OpenTPW.UI.Original;

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

	/// <summary>HUD child 0x1f: the sampled mean drives the cropped meter image inside the separate gauge shell.</summary>
	internal sealed class HappinessGaugeFill( ParkHud owner ) : UiElement
	{
		internal const string ArtPath = "/ui/textures/meter.wct";
		// [BIN:TPW-EXE:0x4a2490 HUD gauge] the builder attaches 0x477870's vertical meter model to child 0x1f; constructor zero already draws empty.
		public override void Draw( UiContext context, bool focused, bool pressed ) =>
			UiVerticalGauge.Draw( context.Batch, ScreenRect( context.Canvas ), owner.parkHappiness is { } mean ? GaugeValue( mean ) : 0, ArtPath );
	}
}
