namespace OpenTPW;

/// <summary>
/// The saved state of an object's hoarding: the thing flags of its variant and fade (<see cref="ObjectHoardingState.SavedFlags"/>) and its rise level.
/// Presentation only: no hash reads it (docs/OBJECTS.md, "Hoarding").
/// </summary>
public sealed record ObjectHoardingRecord( uint Flags, float Level );

/// <summary>
/// The hoarding of one placed object (docs/OBJECTS.md, "Hoarding"): which of the four texture sets it shows, whether it stands and how far it has risen. The
/// simulation's closings, breakdowns and openings make the model calls; the render loop advances the rise. Presentation only: nothing here feeds the
/// simulation or a state hash.
/// </summary>
// [BIN:TPW-EXE:0x00454550 model state] the thing's flags +4 hold the hoarding: 0x20 standing (in the scene), 0x40 rising, 0x80 sinking, and one variant bit 0x100 Closed, 0x200 Hoarding, 0x400 Condemn or 0x800 Upgrade; the level +0xB8 runs 0 to 1 at the rate +0xBC per second
public sealed class ObjectHoardingState
{
	public const uint Standing = 0x20, Rising = 0x40, Sinking = 0x80;
	public const uint ClosedVariant = 0x100, HoardingVariant = 0x200, CondemnVariant = 0x400, UpgradeVariant = 0x800;
	/// <summary>The flag bits a save keeps (the original's packed bits 0..6).</summary>
	public const uint SavedFlags = 0xFE0;
	/// <summary>The level per second while rising and while sinking.</summary>
	public const float RiseRate = 0.2f, SinkRate = -0.3f;

	/// <summary>The model call modes: the closed set, the broken-down set, the condemned set and the upgrade set.</summary>
	public const int CloseMode = 1, BreakdownMode = 2, CondemnMode = 4, UpgradeMode = 8;

	/// <summary>Whether the object's type has a hoarding (the thing's +0xB0); without one every call does nothing.</summary>
	public bool Present { get; internal set; }
	public uint Flags { get; private set; }
	public float Level { get; private set; }
	public float Rate { get; private set; }
	/// <summary>Moves on every change the renderer has to show (level, variant, standing).</summary>
	public int Version { get; private set; }

	public bool IsStanding => (Flags & Standing) != 0;

	/// <summary>
	/// Whether the boards have taken the shape of a level yet: the builder makes them at full height (top 10 above the bottom, the texture's whole height)
	/// and only a changed level or a load reshapes them (<see cref="ObjectHoardingGeometry.Rise"/>).
	/// </summary>
	public bool Shaped { get; private set; }

	/// <summary>The texture set shown: 0 Closed, 1 Hoarding, 2 Condemn, 3 Upgrade.</summary>
	// [BIN:TPW-EXE:0x004547F0 hoarding restore] the variant bits are read in the order 0x100, 0x200, 0x400, 0x800 (texture +0, +8, +0x10, +0x18), and a thing without one shows the closed set and gets 0x100
	public int Variant => (Flags & ClosedVariant) != 0 ? 0 : (Flags & HoardingVariant) != 0 ? 1 : (Flags & CondemnVariant) != 0 ? 2 : (Flags & UpgradeVariant) != 0 ? 3 : 0;

	/// <summary>
	/// The model call (tp.exe <c>0x00454550</c>): mode 1 shows the closed set unless another set stands and is not sinking, 2 the broken-down set, 4 the
	/// condemned set and 8 the upgrade set (each unless already shown); then the hoarding rises from its level at 0.2 per second.
	/// </summary>
	// [BIN:TPW-EXE:0x00454550 model state] nothing without a hoarding (+0xB0); mode 1 writes the base texture (+0x50 of the hoarding's model, flags & ~0xE00 | 0x100) when the thing is sinking (0x80) or has no variant bit, mode 2 +8 (| 0x200) without 0x200, 4 +0x10 (| 0x400) without 0x400, 8 +0x18 (| 0x800) without 0x800; then the rate +0xBC = 0.2, a thing not standing is put back in the scene (0x00454670(1), its level kept) and the flags become flags & ~0x80 | 0x60
	public void SetState( int mode )
	{
		if ( !Present )
			return;
		if ( mode == CloseMode && ((Flags & Sinking) != 0 || (Flags & 0xF00) == 0) )
			Flags = (Flags & 0xFFFFF1FF) | ClosedVariant;
		else if ( mode == BreakdownMode && (Flags & HoardingVariant) == 0 )
			Flags = (Flags & 0xFFFFF2FF) | HoardingVariant;
		else if ( mode == CondemnMode && (Flags & CondemnVariant) == 0 )
			Flags = (Flags & 0xFFFFF4FF) | CondemnVariant;
		else if ( mode == UpgradeMode && (Flags & UpgradeVariant) == 0 )
			Flags = (Flags & 0xFFFFF8FF) | UpgradeVariant;
		Rate = RiseRate;
		Flags = (Flags & ~Sinking) | Standing | Rising;
		Version++;
	}

	/// <summary>The opening's model call (tp.exe <c>0x004547C0</c>): a standing hoarding sinks at 0.3 per second.</summary>
	// [BIN:TPW-EXE:0x004547C0 hoarding sinks] with a hoarding the rate +0xBC becomes −0.3 and a standing thing (0x20) turns from rising to sinking (flags & ~0x40 | 0x80)
	public void StartOpenFade()
	{
		if ( !Present )
			return;
		Rate = SinkRate;
		if ( (Flags & Standing) != 0 )
			Flags = (Flags & ~Rising) | Sinking;
		Version++;
	}

	/// <summary>
	/// One render frame of <paramref name="seconds"/> (tp.exe <c>0x004548F0</c>): a rising or sinking hoarding moves its level by the rate, held to 0..1; on a
	/// frame that leaves the level unchanged a risen hoarding stops rising and a sunk one leaves the scene and goes back to the closed set.
	/// </summary>
	// [BIN:TPW-EXE:0x004548F0 hoarding fade] with a hoarding and the flags 0xC0: level = rate · dt + level (dt the frame's real time in seconds, 0x00473440: milliseconds × 0.001), raised to 0 and lowered to 1; a changed level reshapes the boards (0x004543C0) and returns; else a thing not sinking clears 0x40 at level 1, and a sinking one at level 0 clears 0x20 and 0x80 and leaves the scene (0x00454670(0): the base texture again, flags & ~0xE00 | 0x100)
	// [BIN:TPW-EXE:0x0044E410 frame update] the park frame (0x0054E2B3, before the scene is drawn) calls 0x004548F0 for every standing thing each frame, whether or not it is on screen
	public void Advance( float seconds )
	{
		if ( !Present || (Flags & (Rising | Sinking)) == 0 )
			return;
		var previous = Level;
		var level = (float)((double)Rate * seconds + Level);
		if ( level < 0f )
			level = 0f;
		if ( level > 1f )
			level = 1f;
		Level = level;
		if ( level != previous )
		{
			Shaped = true;
			Version++;
			return;
		}
		if ( (Flags & Sinking) == 0 )
		{
			if ( level >= 1f )
				Flags &= ~Rising;
		}
		else if ( level <= 0f )
		{
			Flags = (Flags & ~(Standing | Sinking) & 0xFFFFF1FF) | ClosedVariant;
			Version++;
		}
	}

	/// <summary>The state to save, or null for a hoarding that never stood (no variant, level 0), so such a snapshot keeps its bytes.</summary>
	public ObjectHoardingRecord? Capture() => Present && ((Flags & SavedFlags) != 0 || Level != 0f) ? new ObjectHoardingRecord( Flags & SavedFlags, Level ) : null;

	/// <summary>
	/// A loaded hoarding (tp.exe <c>0x004647A0</c> then <c>0x004547F0</c>): its saved flags and level; a standing one rises or sinks on at the rate its
	/// flags name, and one without a variant shows the closed set.
	/// </summary>
	// [BIN:TPW-EXE:0x004647A0 hoarding load] the loader clears the flags 0x8000FE0, sets 0x20, 0x40, 0x80, 0x100, 0x200, 0x400 and 0x800 from bits 0..6 of the record's flag word +0x1C and the level +0xB8 from +0x22, then calls 0x004547F0
	// [BIN:TPW-EXE:0x004547F0 hoarding restore] with a hoarding: a standing thing is put back in the scene and takes the rate 0.2 when rising, else −0.3 when sinking; the boards take the level's shape (0x004543C0), and the variant bit picks the texture
	public void Restore( uint flags, float level )
	{
		if ( !Present )
			return;
		Flags = (Flags & ~SavedFlags) | (flags & SavedFlags);
		// the save's +0x22 dword is copied raw; held to 0..1 as the frame update holds it, so a stray value never reaches a snapshot
		Level = float.IsFinite( level ) ? Math.Clamp( level, 0f, 1f ) : 0f;
		if ( (Flags & Standing) != 0 )
		{
			if ( (Flags & Rising) != 0 )
				Rate = RiseRate;
			else if ( (Flags & Sinking) != 0 )
				Rate = SinkRate;
		}
		if ( (Flags & 0xF00) == 0 )
			Flags |= ClosedVariant;
		Shaped = true;
		Version++;
	}

	/// <summary>The thing flags of the original save's packed hoarding bits (the RideSystem record's +0x1C bits 0..6 are the flags 0x20..0x800).</summary>
	public static uint UnpackSavedFlags( uint packed )
	{
		uint flags = 0;
		for ( var bit = 0; bit < 7; bit++ )
			if ( (packed & (1u << bit)) != 0 )
				flags |= Standing << bit;
		return flags;
	}
}
