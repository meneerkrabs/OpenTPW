namespace OpenTPW;

/// <summary>One TPI precipitation update, including calls made to the renderer and rain-volume control.</summary>
internal readonly record struct ThemeParkIncDropStep( int Current, int? Rendered, int? VolumePercent, bool Stop );

internal static class ThemeParkIncPrecipitation
{
	// [BIN:TPI-EXE:0x642A80 spell presentation] Weather modes: 0 rain, 1 snow, 2 off.
	// Rain, snow and storms write different fall speeds; dry spells leave the renderer's speed alone.
	internal static (int Mode, float? Speed) Presentation( int kind ) => kind switch
	{
		2 => (0, 2.5f),
		3 => (0, 4.5f),
		4 => (1, 0.8f),
		_ => (2, null)
	};

	// [BIN:TPI-EXE:0x642C89 spell wind] Force comes from quality; the angle reflects at zero
	// before taking the remainder. A negative angle is not wrapped to 360 minus its magnitude.
	internal static float WindForce( int quality ) => Math.Clamp( unchecked(100 - quality) * 0.01f, 0f, 1f );

	internal static float NextWindDirection( float direction, uint word ) => MathF.Abs( direction - 45f + word % 90 ) % 360f;

	// [BIN:TPI-EXE:0x6EA9A0 precipitation fall vector] Multiply the angle by the two binary32
	// constants separately. The horizontal components are stored as floats before normalization.
	// Precision is explicit until the original graphics device's control word is established.
	internal static System.Numerics.Vector3 FallVector( float direction, float force, bool singlePrecision )
	{
		double Round( double value ) => singlePrecision ? (float)value : value;
		var angle = Round( Round( (double)MathF.PI * direction ) * (1f / 180f) );
		var x = (float)(DetMath.Cos( angle ) * force);
		var z = (float)(DetMath.Sin( angle ) * force);
		var squared = Round( Round( Round( (double)z * z ) + Round( (double)x * x ) ) + 1.0 );
		var inverse = Round( 1.0 / Round( Math.Sqrt( squared ) ) );
		return new( (float)(x * inverse), -(float)inverse, (float)(z * inverse) );
	}

	// [BIN:TPI-EXE:0x642ADB rain target] Rain and storm share the rain band's endpoints; snow uses its own.
	// The native calculation truncates the band percentage before scaling it to the drop count.
	internal static int Target( WeatherSpell spell, (int Low, int High) rainQuality, (int Low, int High) snowQuality, int maximum )
	{
		if ( spell.Kind is not (2 or 3 or 4) )
			return 0;
		var (low, high) = spell.Kind == 4 ? snowQuality : rainQuality;
		var percentage = high == low ? 100 : unchecked((spell.Quality - low) * 100) / unchecked(high - low);
		return Math.Min( maximum, unchecked((100 - percentage) * maximum) / 100 );
	}

	// [BIN:TPI-EXE:0x64223B drop update] A negative target disables this part of the turn. A positive target within one step
	// is not snapped to; a zero target within one step disables precipitation and writes zero volume.
	internal static ThemeParkIncDropStep Step( int current, int target, int speed, int maximum )
	{
		if ( target < 0 )
			return new( current, null, null, false );
		var difference = unchecked(current - target);
		var distance = difference < 0 ? unchecked(-difference) : difference;
		if ( distance < speed )
			return target == 0 ? new( 0, 0, 0, true ) : new( current, null, null, false );
		var next = current < target ? unchecked(current + speed) : unchecked(current - speed);
		// [BIN:TPI-EXE:0x64226A drop outputs] Only the renderer count is clamped. The volume uses the raw current value.
		return new( next, Math.Min( Math.Max( next, 0 ), maximum ), unchecked(next * 100) / maximum, false );
	}
}
