namespace OpenTPW;

/// <summary>
/// One run of the track mesh's cross-section table (the 16-byte record at <c>0x792600</c>): the section the slices up to <see cref="Key"/> use. <see cref="Position"/> is where the run ends on the sample list, and
/// <see cref="Weight"/> is how much of a transition section's blend the run keeps. <see cref="Section"/> indexes <see cref="CoasterCrossSections.Sections"/>.
/// </summary>
public record struct CoasterExtrusionRun( float Key, float Position, float Weight, int Section );

/// <summary>
/// The run table of a chain's track mesh: the cross section the select table gives along the sampled track, in runs, with the transition runs of a type whose sections blend (the flumes' dry and wet sections).
/// </summary>
// [BIN:TPW-EXE:0x4402d0 run table] the builder walks the sample coordinate in steps of 0.25 from 0 while it is below the controller's end coordinate (+0xa8, 0x43c080), turns the frame there into the select answer (0x43e9c0, the roll taken modulo π, fmod 0x6fe158) and, whenever the answer differs from the previous one, writes a run of key (the knot number plus the fraction to the next knot, 0x440a30), position, weight and section; the table ends with the key 100000 and the position (pos + 0.25) + 1 (0x440a00..0x440a17)
public static class CoasterExtrusionRunBuilder
{
	/// <summary>The sentinel key that ends the table.</summary>
	public const float EndKey = 100000f;
	/// <summary>The step of the sample coordinate.</summary>
	public const float Step = 0.25f;

	/// <summary>One frame of the track at a sample coordinate: the yaw, the pitch and the roll of the slice frame (<c>0x43e6b0</c>).</summary>
	public readonly record struct Frame( float Yaw, float Pitch, float Roll );

	/// <summary>
	/// Builds the table. <paramref name="endCoordinate"/> is the sampler's end coordinate, <paramref name="frameAt"/> the frame at a sample coordinate and <paramref name="keyAt"/> the key of a sample coordinate.
	/// A type without transitions writes a run at every change; with them (<see cref="CoasterCrossSections.HasTransitions"/>) a change also looks up the transition section of the pair.
	/// </summary>
	// [BIN:TPW-EXE:0x440420 change without transitions] a type whose flag +0x74 bit 0 is clear writes the run position and key of the change and goes on with the new section (0x440885..0x44090d)
	// [BIN:TPW-EXE:0x44042f transition lookup] with the flag the table is walked for the entry whose from and to sections (+0x24, +0x28) are the previous and the new answer (0x43f..0x440459); its section is the transition, whose length (+0x14) tells the rest: a positive length opens a blend of that length from the change on (0x44049b..0x4405d3), a length of zero or below puts the blend before it (0x4405da..0x44090d)
	// [BIN:TPW-EXE:0x440604 blend ratio] two transitions that meet (a blend still open when the next change comes, the new one reaching back past its start) share the stretch by their lengths: ratio = (pos − (start − |length of the open one|)) / (|open| + |new|), clamped to [0, 1] (0x6fe10c, 0x6fe108), and the open run's end moves back by (1 − ratio) × |open| (0x44066c..0x440695)
	// [BIN:TPW-EXE:0x440920 open blend end] while a blend is open its run ends where the sample coordinate reaches the run's position, which writes the next run with the new answer (0x440920..0x4409d4)
	public static List<CoasterExtrusionRun> Build( CoasterCrossSections sections, float endCoordinate, Func<float, Frame> frameAt, Func<float, float> keyAt )
	{
		ArgumentNullException.ThrowIfNull( sections );
		ArgumentNullException.ThrowIfNull( frameAt );
		ArgumentNullException.ThrowIfNull( keyAt );
		var runs = new List<CoasterExtrusionRun> { default };
		var transitions = sections.HasTransitions;
		var previous = sections.Selects.Count > 0 ? sections.Selects[0].CrossSection - 1 : -1;
		runs[0] = new CoasterExtrusionRun( 0f, 0f, 0f, previous );
		var current = 0;
		var inBranch = false;
		var branch = -1;
		var position = 0f;
		float Length( int section ) => section >= 0 && section < sections.Sections.Count ? sections.Sections[section].BlendLength : 0f;
		void Set( int index, float key, float at ) => runs[index] = runs[index] with { Key = key, Position = at };
		CoasterExtrusionRun At( int index ) => runs[index];
		void Grow( int index )
		{
			while ( runs.Count <= index )
				runs.Add( default );
		}

		while ( position < endCoordinate )
		{
			var frame = frameAt( position );
			// [BIN:TPW-EXE:0x4403f4 roll modulo π] the frame's roll is replaced by its remainder modulo the double π (0x6fe158) before the select table sees it
			var roll = (float)((double)frame.Roll % 3.1415927410125732);
			var answer = sections.SelectAnswer( 0f, frame.Yaw, frame.Pitch, roll );
			var keep = answer;
			if ( answer != previous )
			{
				var transition = -1;
				if ( transitions )
					transition = sections.SelectTransition( previous, answer );
				Grow( current + 1 );
				if ( !transitions )
				{
					Set( current, keyAt( position ), position );
					current++;
				}
				else
				{
					Set( current, keyAt( position ), position );
					current++;
					if ( transition >= 0 )
					{
						var length = Length( transition );
						if ( length > 0f )
						{
							keep = transition;
							if ( inBranch )
								Set( branch, keyAt( position ), position );
							inBranch = true;
							var end = position + length;
							Grow( current );
							runs[current] = runs[current] with { Key = keyAt( end ), Position = end, Weight = 1f };
							branch = current;
						}
						else
						{
							Grow( current );
							runs[current] = runs[current] with { Section = transition };
							var end = position + length;
							if ( branch >= 0 && At( branch ).Position > end )
							{
								if ( inBranch )
								{
									var lengthB = MathF.Abs( Length( At( branch ).Section ) );
									var lengthA = MathF.Abs( length );
									var lowest = At( branch ).Position;
									var start = lowest - lengthB;
									var denominator = lengthB + lengthA;
									var ratio = (position - start) / denominator;
									if ( !(ratio > 0f) )
										ratio = 0f;
									if ( !(ratio < 1f) )
										ratio = 1f;
									runs[branch] = runs[branch] with { Weight = ratio };
									var moved = lowest - (1f - ratio) * lengthB;
									runs[current] = runs[current] with { Weight = ratio };
									Set( branch, keyAt( moved ), moved );
									Set( current, keyAt( position ), position );
									current++;
									Grow( current );
								}
								else
								{
									Set( current, keyAt( position ), position );
									runs[current] = runs[current] with { Weight = 1f };
									current++;
									Grow( current );
								}
							}
							else
							{
								var back = position + length;
								Set( current - 1, keyAt( back ), back );
								Set( current, keyAt( position ), position );
								runs[current] = runs[current] with { Weight = 1f };
								current++;
								Grow( current );
							}
							inBranch = false;
						}
						branch = current;
					}
				}
				previous = answer;
				runs[current] = runs[current] with { Section = keep };
			}
			if ( inBranch )
			{
				var limit = At( branch ).Position;
				if ( !(position < limit) )
				{
					Set( current, keyAt( position ), position );
					inBranch = false;
					Grow( current + 1 );
					runs[current + 1] = runs[current + 1] with { Section = answer };
					current++;
				}
			}
			position -= -Step;
		}
		Grow( current );
		runs[current] = runs[current] with { Key = EndKey, Position = position - -Step - -1f };
		runs.RemoveRange( current + 1, runs.Count - current - 1 );
		return runs;
	}
}
