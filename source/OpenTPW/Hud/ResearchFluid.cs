namespace OpenTPW.Hud;

/// <summary>
/// The animated fluid of one research lab category panel (control 0xd2d01/0xd2d07..0xd2d0a, child 0xd2d06 drawing
/// <c>res_fluid</c>). Its 64 frames are the authored nodes in order: <c>fluid</c>..<c>fluid32</c> (0..31, the bubbling loop),
/// <c>empty01</c>..<c>empty16</c> (32..47, draining) and <c>empty17</c>..<c>empty32</c> (48..63, filling from empty).
/// The original keeps the animation in the panel control's user slots: slot 0 the position in 16.16 frames, slot 2 the
/// speed and slot 3 the state. Presentation only: no simulation state and no random draws.
/// </summary>
public sealed class ResearchFluid
{
	/// <summary>Frame 48 (<c>empty17</c>): the empty fluid shown while a panel is idle.</summary>
	public const int IdleFrame = 0x30;
	public const int FrameCount = 64;

	// [BIN:TPW-EXE:0x004aa74c research panel init] the window build 0x4aa480 sets slot 0 to the raw 0x30, slot 3 (state) to 0 and the fluid child's frame to 0x30
	public int Position { get; private set; } = 0x30;
	public int Speed { get; private set; }
	public int State { get; private set; }
	public int Frame { get; private set; } = IdleFrame;

	/// <summary>Applies a panel's research rate, as the refresh 0x4a9c70 does through 0x4a95b0.</summary>
	public void Apply( int rate )
	{
		// [BIN:TPW-EXE:0x004a95b0 fluid rate] a zero rate turns a running fluid (state 1 or 3) into state 2 at frame 0x20 (position 0x200000), the drain;
		// a non-zero rate starts an idle or draining fluid (state 0 or 2) in state 3 at frame 0x30 (position 0x300000), the fill, and always stores the
		// speed (rate + 20) × 0x10000 / 100 (signed division)
		if ( rate == 0 )
		{
			if ( State is 3 or 1 )
			{
				State = 2;
				Frame = 0x20;
				Position = 0x200000;
			}
			return;
		}
		var speed = unchecked((rate + 20) << 16) / 100;
		if ( State is 0 or 2 )
		{
			State = 3;
			Position = 0x300000;
			Frame = 0x30;
		}
		Speed = speed;
	}

	/// <summary>One message 0x1e of the panel control.</summary>
	public void Tick()
	{
		// [BIN:TPW-EXE:0x004a9fc0 fluid tick] state 0 does nothing; otherwise the position advances by twice the speed and frame = position >> 16:
		// state 1 restarts at 0 past frame 0x1f (the loop), state 2 past 0x2f stops in state 0 at frame 0x30 (position 0x300000), state 3 past 0x3f
		// enters state 1 at 0; the frame goes to the fluid child (0x65d3a3) and the position back to slot 0
		if ( State is not (1 or 2 or 3) )
			return;
		var position = unchecked(Position + Speed * 2);
		var frame = position >> 16;
		switch ( State )
		{
			case 1 when frame > 0x1f:
				frame = 0;
				position = 0;
				break;
			case 2 when frame > 0x2f:
				State = 0;
				frame = 0x30;
				position = 0x300000;
				break;
			case 3 when frame > 0x3f:
				State = 1;
				frame = 0;
				position = 0;
				break;
		}
		Frame = frame;
		Position = position;
	}

	/// <summary>
	/// The rate the refresh 0x4a9c70 passes for a category: 0 without a current project (0x5039f0), else the category's share
	/// times the staffing percentage / 100 (0x503980).
	/// </summary>
	/// <param name="effort">The five <c>ResearchCategories[n].Effort</c> values.</param>
	/// <param name="abilitySum">The <c>ResearchAbility</c> of every hired researcher's grade, summed.</param>
	/// <param name="maximumResearchers"><c>StaffPoolInfo.MaxResearchersInPark</c>.</param>
	/// <param name="bestAbility"><c>ResearcherConstsPerGrade[4].ResearchAbility</c>.</param>
	public static int Rate( IReadOnlyList<int> effort, int category, int workload, bool hasProject, int abilitySum, int maximumResearchers, int bestAbility )
	{
		ArgumentNullException.ThrowIfNull( effort );
		if ( !hasProject )
			return 0;
		// [BIN:TPW-EXE:0x00503870 research share] the share stored beside each effort: Σ effort == 0 ? 0 : (effort × 100 / Σ effort) × workload (+0x1460) / 100, unsigned
		uint total = 0;
		foreach ( var value in effort )
			total = unchecked(total + (uint)value);
		var share = total == 0 ? 0u : unchecked(unchecked((uint)effort[category] * 100u) / total * (uint)workload) / 100u;
		// [BIN:TPW-EXE:0x00503980 research rate] 0x4c9ed0 = MaxResearchersInPark (0x7852f8, the hire cap 0x508000 tests) × the grade-4 ability (0x78548c);
		// 0x4c9e60 sums ResearchAbility (0x78545c + 12 × grade) over the researcher list (game +0x1da742) whatever their state; rate = share × (sum × 100 / cap) / 100
		var capacity = unchecked((uint)maximumResearchers * (uint)bestAbility);
		if ( capacity == 0 )
			return 0;
		return unchecked((int)(share * (unchecked((uint)abilitySum * 100u) / capacity) / 100u));
	}
}
