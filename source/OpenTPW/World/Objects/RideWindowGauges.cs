namespace OpenTPW;

/// <summary>
/// The values the ride and sideshow windows' gauges take from the window's own slider values (tp.exe's window refresh <c>0x4ade40</c> for rides, <c>0x4b16c0</c> for
/// sideshows): the excitement routine <c>0x4e0560</c> and the reliability routine <c>0x4df640</c>, which the windows call with the values on their sliders rather than the
/// object's applied ones, so the gauges move while a slider is dragged and before the window applies it. Each gauge then shows <c>((value &amp; 0xFF) &lt;&lt; 10) / 100</c>
/// of its 0..1024 range (<see cref="OpenTPW.UI.Original.UiGauge.OfPercent"/>).
/// </summary>
public static class RideWindowGauges
{
	/// <summary>The x87 <c>__ftol</c> (<c>0x67a830</c>): truncation toward zero; a NaN or an out-of-range value gives the integer indefinite 0x80000000.</summary>
	public static int Truncate( double value ) =>
		double.IsNaN( value ) || value >= 2147483648.0 || value <= -2147483649.0 ? int.MinValue : (int)value;

	/// <summary>
	/// The excitement of a sideshow (<c>0x4e0560</c> for a type whose record <c>+0x4ac</c> is 2): <c>20 - ftol(win × sqrt(clamp(prize - price, 0, 100)) × 0.1 × -0.8f)</c>, that
	/// is 20 plus the win chance times the root of the prize margin over 12.5, as a byte. The window passes its prize box, its price box and its chance slider.
	/// </summary>
	// [BIN:TPW-EXE:0x4e0581 sideshow excitement] the type record's +0x4ac (Info.WhichUIType, 2 = sideshows) selects the branch: arg1 - arg2 clamped to 0..100 (0x4e0592..0x4e05b1), arg3 × sqrt of it × the double 0.1 (0x7005a8) × the float -0.8 (0x7005b0), __ftol, and the byte 0x14 less that (0x4e05d0); the sideshow window passes (window +0x28 prize, +0x2c price, +0x24 chance) at 0x4b1a3c, the object's own 0x4e0860 (object +0x188 prize, +0x194 price, +0x190 win byte)
	public static int SideshowExcitement( int prize, int price, int winChance )
	{
		var margin = Math.Clamp( prize - price, 0, 100 );
		var product = (double)winChance * Math.Sqrt( margin ) * 0.1 * (double)-0.8f;
		return (20 - Truncate( product )) & 0xFF;
	}

	/// <summary>
	/// The excitement of any other ride (<c>0x4e0560</c>): the data level, or for a ride with a track controller its <see cref="BumpController.ExcitementBase"/>, times the
	/// speed ratio and the duration ratio, each clamped to 0.75..1.25, as a byte. The windows pass their speed and duration sliders.
	/// </summary>
	// [BIN:TPW-EXE:0x4e07be ride excitement] speed / Upgrades[level].InitSpeed (+0x1a8) in extended precision, clamped (a NaN takes 0.75: FCOM leaves C0 set) and stored as a float (0x4e0808); duration / InitDuration (+0x1a0) stored as a float first (0x4e07ce) and then clamped; the product of the two floats is stored as a float (0x4e0842) and multiplied by the base in extended precision before __ftol (0x4e0846), so a ratio such as 0.9f, a little below 0.9, truncates 50 × 0.9 to 44 where single-precision arithmetic would round to 45
	public static int RideExcitement( int baseLevel, int speed, int initialSpeed, int duration, int initialDuration )
	{
		var speedRatio = (float)ClampRatio( (double)speed / initialSpeed );
		var durationRatio = (float)ClampRatio( (float)((double)duration / initialDuration) );
		var product = (float)((double)durationRatio * speedRatio);
		return Truncate( (double)baseLevel * product ) & 0xFF;
	}

	private static double ClampRatio( double ratio ) => double.IsNaN( ratio ) || ratio < 0.75 ? 0.75 : ratio > 1.25 ? 1.25 : ratio;

	/// <summary>
	/// The reliability of a ride at the window's speed and capacity (<c>0x4df640</c>): <c>100 - ftol(wear × 9)</c> as a byte, with the wear of <c>0x4df450</c> in its
	/// window mode, the wear step's formula with the slider values for the speed and the capacity and the ride's live rider count for the red line test.
	/// </summary>
	/// <param name="maxSpeed">The type's <c>UsageInfo.MaxSpeed</c> (<c>+0x138</c>).</param>
	/// <param name="maxCapacity">The type's <c>UsageInfo.MaxCapacity</c> (<c>+0x128</c>).</param>
	/// <param name="redLineSpeed">The current level's <c>RedLineSpeed</c> (<c>+0x1ac + level × 0x40</c>).</param>
	/// <param name="redLineCapacity">The current level's <c>RedLineCapacity</c> (<c>+0x19c + level × 0x40</c>).</param>
	/// <param name="wearRate">The current level's <c>WearRate</c> (<c>+0x1b0 + level × 0x40</c>).</param>
	/// <param name="riders">The ride script's <c>VAR_ONRIDE</c> (script variable 5), which the routine clamps to 0..MaxCapacity.</param>
	// [BIN:TPW-EXE:0x4df640 reliability gauge] 100 - ftol(0x4df450(speed, capacity, duration, 1) × 9.0 (0x700594)); 0x4df450 with its flag set is 0x4df501..0x4df631: s = speed / MaxSpeed × 0.9f stored as a float, + 0.1f, or (s + 0.1f + 1) × 0.5 when RedLineSpeed <= speed; c = capacity / MaxCapacity × 0.9f, + 0.1f, or (c + 0.1f + 1) × 0.5 when RedLineCapacity <= riders × 0.1f (the riders are the script's variable 5, 0x4df460, also in this mode); wear = WearRate × (c + s) × 0.5; the duration argument is not read
	public static int Reliability( int maxSpeed, int maxCapacity, int redLineSpeed, int redLineCapacity, int wearRate, int speed, int capacity, int riders )
	{
		riders = riders < 0 ? 0 : riders > maxCapacity ? maxCapacity : riders;
		var s = (float)((double)speed / maxSpeed * (double)0.9f);
		var speedTerm = redLineSpeed <= speed ? (float)(((double)s + (double)0.1f + 1.0) * 0.5) : (float)((double)s + (double)0.1f);
		var c = (float)((double)capacity / maxCapacity * (double)0.9f);
		var capacityTerm = redLineCapacity <= riders * (double)0.1f ? (float)(((double)c + (double)0.1f + 1.0) * 0.5) : (float)((double)c + (double)0.1f);
		var wear = wearRate * ((double)capacityTerm + speedTerm) * 0.5;
		return (100 - Truncate( wear * 9.0 )) & 0xFF;
	}
}
