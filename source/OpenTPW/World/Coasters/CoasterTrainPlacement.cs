namespace OpenTPW;

/// <summary>
/// The train placement after a rating pass (tp.exe <c>0x43c1f0</c> and <c>0x43cbda..0x43cd47</c>) in the executable's operation order at the thread's 24-bit precision: every
/// product, sum and quotient here is one single-precision operation. Pure arithmetic on arrays so the oracle golden can replay it (<c>CoasterPlacementGolden</c>, ORACLE X46).
/// </summary>
internal static class CoasterTrainPlacement
{
	/// <summary>The release time in seconds: the release rate in milliseconds (an unsigned) times the single 0.001.</summary>
	// [BIN:TPW-EXE:0x43c226 release time] fild of the rate as a 64-bit integer times 0x6fe030 (0.001f), stored as a single
	public static float ReleaseSeconds( int releaseRateMilliseconds ) => (float)((double)(uint)releaseRateMilliseconds * 0.001f);

	/// <summary>
	/// The last car's coordinate of each train. Train 0 keeps the lead coordinate (plus the end coordinate when it is negative); every next train stands where a train leaving the
	/// previous one's last car would be after the release time at the rated sample speeds.
	/// </summary>
	// [BIN:TPW-EXE:0x43c23d lead coordinate] each train's +0x30 is its last car's +0x40, plus ctl +0xa8 when that is below 0; the placement then walks from train 0 on
	// [BIN:TPW-EXE:0x43c2ac release walk] the previous train's +0x30 is split by modf (0x67b280) and ftol into the sample i and the fraction f0; t = ((1 − f0) × spacing) / speed[i], t' = 0; while t < R: i = (i + 1) mod n (ctl +0xa4), t' = t, t = t + spacing / speed[i]; then f = (t − R) / (t − t'), the position (i − 1) × (1 − f) + i × f and the train's +0x30 = fmod(position + total, total) with total = ctl +0xa8 (0x43c387..0x43c39f), every step a single
	public static float[] ReleaseSpacing( IReadOnlyList<float> speeds, int sampleCount, float spacing, float total, float release, float lead, int count )
	{
		var result = new float[count];
		var first = lead < 0 ? total + lead : lead;
		// [EXT:original-undefined-reads] a stale lead coordinate past this track's samples (a shorter track since) is wrapped into them; the original reads the sample list beyond its end
		result[0] = first >= sampleCount || first < 0 ? (float)(((double)first % total + total) % total) : first;
		float Speed( int index ) => speeds[index <= sampleCount ? index : index % sampleCount];
		for ( var train = 1; train < count; train++ )
		{
			var previous = result[train - 1];
			var sample = (int)MathF.Truncate( previous );
			var fraction = previous - MathF.Truncate( previous );
			var time = (1f - fraction) * spacing / Speed( sample );
			var before = 0f;
			// a lap of samples that adds no time (every rated speed infinite) would never reach the release time: the walk stops after one such lap
			var stalled = 0;
			while ( time < release && stalled < sampleCount )
			{
				sample = (sample + 1) % sampleCount;
				before = time;
				time += spacing / Speed( sample );
				stalled = time > before ? 0 : stalled + 1;
			}
			var f = (time - release) / (time - before);
			// a sample rated at speed 0 takes forever (t = infinity, f = NaN in the original's x87 sum); the train then stands on that sample, f's limit
			if ( !float.IsFinite( f ) || time < release )
				f = 1f;
			var position = (sample - 1) * (1f - f) + sample * f;
			result[train] = (position + total) % total;
		}
		return result;
	}

	/// <summary>The car coordinates of a train laid out from its last car's coordinate: each car before it adds (its front extent + the next car's rear extent) / spacing.</summary>
	// [BIN:TPW-EXE:0x43cbf1 car coordinates] fst of the running coordinate into car +0x40, then coordinate + ((front[c] + rear[c − 1]) / spacing) toward car 0, every step a single
	public static float[] CarCoordinates( float lastCar, IReadOnlyList<float> fronts, IReadOnlyList<float> rears, float spacing )
	{
		var cars = fronts.Count;
		var coordinates = new float[cars];
		var coordinate = lastCar;
		for ( var car = cars - 1; car >= 0; car-- )
		{
			coordinates[car] = coordinate;
			if ( car > 0 )
				coordinate = coordinate + (fronts[car] + rears[car - 1]) / spacing;
		}
		return coordinates;
	}

	/// <summary>The train's front and rear (train <c>+0x20</c>, <c>+0x24</c>) from its position.</summary>
	// [BIN:TPW-EXE:0x43cc37 train extents] +0x20 = (car 0's offset / spacing + position) + its front extent / spacing; +0x24 = (the last car's offset / spacing + position) − its rear extent / spacing
	public static (float Front, float Rear) Extents( float position, float firstOffset, float firstFront, float lastOffset, float lastRear, float spacing ) =>
		(firstOffset / spacing + position + firstFront / spacing, lastOffset / spacing + position - lastRear / spacing);

	/// <summary>The mean over the cars of the sample speed interpolated at the car's coordinate.</summary>
	// [BIN:TPW-EXE:0x43cc94 train speed] per car modf of +0x40 into the sample i and the fraction f; (speed[i + 1] × f) + (speed[i] × (1 − f)), added to the running sum, which is divided by the car count at the end (0x43cd1a..0x43cd29)
	public static float MeanSpeed( IReadOnlyList<float> coordinates, Func<int, float> speed )
	{
		var sum = 0f;
		foreach ( var at in coordinates )
		{
			var whole = MathF.Truncate( at );
			var fraction = at - whole;
			var index = (int)whole;
			sum = speed( index + 1 ) * fraction + speed( index ) * (1f - fraction) + sum;
		}
		return sum / coordinates.Count;
	}
}
