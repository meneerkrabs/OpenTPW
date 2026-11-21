using System.Numerics;

namespace OpenTPW;

internal readonly record struct ThemeParkIncStormState( int LightningCountdown, int BoltCount, int ThunderCountdown, Vector3 ThunderPosition );
internal readonly record struct ThemeParkIncStormStep( ThemeParkIncStormState State, WeatherStrike? Strike, Vector3? Thunder );

/// <summary>The TPI turn's lightning and thunder timers and strike geometry.</summary>
internal static class ThemeParkIncStorm
{
	// [BIN:TPI-EXE:0x642550 strike geometry] Four draws: ground X/Z, then the top's X/Z offsets.
	// The top is calculated from the stored, unclamped ground coordinates. The native precision
	// selection is an input here; it has not yet been traced through TPI's device startup.
	internal static WeatherStrike Geometry( int width, int height, bool singlePrecision, Func<uint> draw )
	{
		double Round( double value ) => singlePrecision ? (float)value : value;
		double Unit() => Round( Round( draw() & 0x3FFFFFFFu ) * (1.0 / 1073741824.0) );
		var maxX = (float)(width * 10.0);
		var maxZ = (float)(height * 10.0);
		var x = (float)Round( Unit() * maxX );
		var z = (float)Round( Unit() * maxZ );
		float Top( float ground ) => (float)Round( Round( Round( Unit() * 100.0 ) + ground ) - 50.0 );
		var top = new System.Numerics.Vector3( Top( x ), 300f, Top( z ) );
		return new( top, new( Math.Max( 1f, Math.Min( maxX, x ) ), 0f, Math.Max( 1f, Math.Min( maxZ, z ) ) ) );
	}

	// [BIN:TPI-EXE:0x6422FE storm turn] A strike requires a strictly negative lightning timer. A pending thunder
	// keeps its position and skips the re-arm draw. The thunder timer decrements even outside a storm.
	internal static ThemeParkIncStormStep Step( ThemeParkIncStormState state, bool storm, int countdownStep,
		uint countdownBound, int thunderStart, uint thunderBound, Func<WeatherStrike> makeStrike, Func<uint> draw )
	{
		var (lightning, bolts, countdown, position) = state;
		WeatherStrike? strike = null;
		if ( !storm )
			bolts = 0;
		else if ( (lightning = unchecked(lightning - countdownStep)) < 0 )
		{
			strike = makeStrike();
			if ( countdown <= 0 )
			{
				// 0x6423AB copies the top point returned into the caller's +0x10..+0x18 slots.
				position = strike.Value.Top;
				countdown = unchecked(Math.Max( 0, unchecked(thunderStart - bolts) ) + (int)(draw() % thunderBound));
			}
			bolts = unchecked(bolts + 1);
			lightning = unchecked((int)(draw() % countdownBound));
		}
		countdown = unchecked(countdown - 1);
		return new( new( lightning, bolts, countdown, position ), strike, countdown == 0 ? position : null );
	}
}
