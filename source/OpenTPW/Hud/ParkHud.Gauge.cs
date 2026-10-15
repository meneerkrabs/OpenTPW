using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Hud;

/// <summary>The happiness gauge on the main panel (tooltip 477 "Happiness of the park visitors").</summary>
public sealed partial class ParkHud
{
	/// <summary>How often the gauge resamples the visitors' mean happiness (seconds).</summary>
	internal const float HappinessSampleSeconds = 0.5f;
	private float? parkHappiness;
	private float happinessSampled;

	/// <summary>The mean happiness (0..100) of the visitors in the park, sampled for the gauge; null when none is in.</summary>
	public float? ParkHappiness => parkHappiness;

	private void SampleHappiness( float delta )
	{
		happinessSampled -= delta;
		if ( happinessSampled > 0 )
			return;
		happinessSampled = HappinessSampleSeconds;
		var statistics = level.Guests?.GetStatistics();
		parkHappiness = statistics is { } sample && sample.InPark > 0 ? sample.AverageHappiness : null;
	}

	/// <summary>Fraction of the gauge dish the mean happiness fills: clamped 0..1, null without visitors.</summary>
	internal static float? GaugeFill( float? happiness ) => happiness is { } value ? Math.Clamp( value / 100f, 0f, 1f ) : null;

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
