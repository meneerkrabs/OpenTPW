namespace OpenTPW;

/// <summary>
/// The sprites the windows own: the clone of a person's sprite the employee and visitor windows show as their portrait, and the hire window's figure. They are records of the same registry as the world's sprites
/// (tp.exe <c>0x007B49F0</c>), so the sprite scheduler (<c>0x00475360</c>, <see cref="Level"/>'s even-substep pass) steps them with the rest, on the one clock sample of the pass. Presentation only: nothing here is
/// hashed or saved (a window is not part of a save).
/// </summary>
internal sealed class PortraitSprites
{
	/// <summary>The employee window's clone scale (<c>0x004B537A</c> pushes 8.0).</summary>
	public const float EmployeeScale = 8f;
	/// <summary>The visitor window's clone scale (<c>0x004B6BDA</c> pushes 12.0).</summary>
	public const float VisitorScale = 12f;

	/// <summary>The window's person: tp.exe <c>[0x007C24D8]</c>, set by the full copy and cleared when the person has no sprite or the window closes.</summary>
	private StaffMember? staffSource;
	private Guest? guestSource;

	/// <summary>The clone of a staff member's source (<c>[0x007C24A0]</c>), null without one.</summary>
	public StaffSpriteVmState? StaffClone { get; private set; }
	/// <summary>The clone of a guest's body player.</summary>
	public VisitorSpritePlayer? GuestClone { get; private set; }
	/// <summary>Whether the cloned guest body is the costume bank's (the source's kind word, copied at each refresh).</summary>
	public bool GuestCloneInCostume { get; private set; }
	/// <summary>The cloned guest's heading sector at the last copy.</summary>
	public int GuestCloneHeading { get; private set; }
	/// <summary>The cloned guest's kept kid-set draw at the last copy (its variant word, <see cref="GuestAppearance.KidSet(Guest, int)"/>).</summary>
	public uint GuestCloneDraw { get; private set; }

	/// <summary>The person the clone slot follows: a person with a live source, until the window closes or another full copy replaces it.</summary>
	public bool HasSource => staffSource != null || guestSource != null;
	public StaffMember? SourceMember => staffSource;
	public Guest? SourceGuest => guestSource;

	/// <summary>The hire window's figure, null without one.</summary>
	public StaffSpriteVmState? Hire { get; private set; }
	/// <summary>The role and variant the hire figure was made for (<c>0x007523E8</c>, <c>0x007523EC</c>; -1 after the window closed).</summary>
	public (StaffType Type, int Variant)? HireKey { get; private set; }

	/// <summary>
	/// The clone's register words for a window's portrait box (<c>0x00486530</c>): x is the box's centre minus 0x3F8 over 1024, y the bottom, less a quarter of the height and the scale's quarter, plus 0x300, over 768, and z one minus the window's depth over 40.
	/// </summary>
	// [BIN:TPW-EXE:0x00486530 clone placement] x = float(left + ((right - left) >> 1) - 0x3F8) * 2^-10 (0x6FF154), y = float(((bottom - top) >> 2) - ftol(scale * -0.25) - bottom + 0x300) * (1/768) (0x6FF160, 0x6FF150), z = 1 - float(depth) / 40 (0x476A70: 0x74F898, 0x6FEDD8); the employee window passes depth + 1 and scale 8.0 (0x4B537A to 0x4B538E), the visitor window depth + 2 and 12.0 (0x4B6BDA to 0x4B6BF0)
	public static (uint X, uint Y, uint Z) Placement( int left, int top, int right, int bottom, float scale, int depth )
	{
		var x = (float)(left + ((right - left) >> 1) - 0x3F8) * 0.0009765625f;
		var lift = (int)(scale * -0.25f);
		var y = (float)(((bottom - top) >> 2) - lift - bottom + 0x300) * 0.0013020833721384406f;
		var z = 1f - (float)depth / 40f;
		return (BitConverter.SingleToUInt32Bits( x ), BitConverter.SingleToUInt32Bits( y ), BitConverter.SingleToUInt32Bits( z ));
	}

	/// <summary>
	/// The full copy for a staff member (<c>0x00486530</c>): an earlier clone goes first (only while a person was bound), the member is bound, and a member without a source binds nothing, so a later partial refresh cannot reach it.
	/// </summary>
	// [BIN:TPW-EXE:0x00486530 full copy] if [0x7C24D8] is set the old clone is deleted (0x475550, 0x486543); the argument becomes [0x7C24D8]; a person whose handle word (+0x0C) is 0 clears [0x7C24D8] again and returns (0x48655B), else 0x475CF0 copies the person's sprite and the new handle is stored in [0x7C24A0]
	public void CloneStaff( StaffMember member, (uint X, uint Y, uint Z) placement, float scale )
	{
		ArgumentNullException.ThrowIfNull( member );
		Unbind();
		staffSource = member;
		if ( member.Sprite.Source is not { } source )
		{
			staffSource = null;
			return;
		}
		StaffClone = StaffSpriteCloneCopy.Create( source, 0, placement.X, placement.Y, placement.Z, BitConverter.SingleToUInt32Bits( scale ) );
		StaffClone[StaffSpriteRegister.Heading] = (uint)GuestSpriteAtlas.HeadingSector( member.HeadingX, member.HeadingY );
	}

	/// <summary>The full copy for a guest's body, as <see cref="CloneStaff"/>.</summary>
	public void CloneGuest( Guest guest )
	{
		ArgumentNullException.ThrowIfNull( guest );
		Unbind();
		guestSource = guest;
		if ( guest.BodySprite.Player is not { } source )
		{
			guestSource = null;
			return;
		}
		GuestClone = source.Copy();
		CopyGuestWords( guest );
	}

	/// <summary>
	/// The partial refresh of the clone (<c>0x00486610</c>, run for the window's message 0x1E of every frame): the person's current sprite is copied over the clone's program position, frame and output; with no
	/// person bound or no sprite at the moment the clone stands as it is and goes on by its own clock.
	/// </summary>
	// [BIN:TPW-EXE:0x00486610 partial refresh] [0x7C24D8] and its handle word (+0x0C) both non-zero: 0x475E10(clone, handle); else nothing, so a source that is gone leaves the clone running on its own, and a source that gets a new sprite is followed through the person's current handle
	public void Refresh()
	{
		if ( staffSource is { } member && member.Sprite.Source is { } staffSprite && StaffClone != null )
		{
			StaffSpriteCloneCopy.Refresh( StaffClone, staffSprite );
			StaffClone[StaffSpriteRegister.Heading] = (uint)GuestSpriteAtlas.HeadingSector( member.HeadingX, member.HeadingY );
		}
		else if ( guestSource is { } guest && guest.BodySprite.Player is { } bodySprite && GuestClone != null )
		{
			GuestClone.RefreshFrom( bodySprite );
			CopyGuestWords( guest );
		}
	}

	private void CopyGuestWords( Guest guest )
	{
		GuestCloneInCostume = guest.BodySpriteInCostume;
		GuestCloneDraw = guest.KidSetDraw;
		GuestCloneHeading = GuestSpriteAtlas.HeadingSector( guest.HeadingX, guest.HeadingY );
	}

	/// <summary>The window's close hook (<c>0x00486630</c>, vtable +0x20 of both windows): the clone and the bound person go.</summary>
	// [BIN:TPW-EXE:0x00486630 clone delete] with [0x7C24D8] set the clone is deleted (0x475550) and [0x7C24D8] zeroed
	public void Close() => Unbind();

	private void Unbind()
	{
		staffSource = null;
		guestSource = null;
		StaffClone = null;
		GuestClone = null;
		GuestCloneInCostume = false;
		GuestCloneHeading = 0;
		GuestCloneDraw = 0;
	}

	/// <summary>The frame the staff clone shows, as (slot, frame), null while it shows none.</summary>
	public (int Slot, int Frame)? StaffOutput => OutputOf( StaffClone );

	/// <summary>The frame the hire figure shows, as (slot, frame), null while it shows none.</summary>
	public (int Slot, int Frame)? HireOutput => OutputOf( Hire );

	private static (int Slot, int Frame)? OutputOf( StaffSpriteVmState? sprite ) =>
		sprite is { HasOutput: true } && sprite[StaffSpriteRegister.Slot] <= int.MaxValue && sprite[StaffSpriteRegister.Frame] <= int.MaxValue
			? ((int)sprite[StaffSpriteRegister.Slot], (int)sprite[StaffSpriteRegister.Frame]) : null;

	/// <summary>
	/// The hire window's figure for an applicant (<c>0x0049BA00</c>): the same role and variant keeps the figure, another deletes it and creates a new one at the clock sample, with no frame until its first due pass.
	/// </summary>
	// [BIN:TPW-EXE:0x0049BA00 hire figure] the role byte (+0) and variant byte (+9) are compared with the cached pair (0x7523E8, 0x7523EC); a change stores them, deletes the old figure (0x475550) and creates the script 0x74F758 (0x475A10) of the sprite kind 5, 6, 4, 7 or 8 for the roles 0..4 at the box's placement (0x49BA49 to 0x49BAA4, scale register 8 = 8.0 by 0x4755D0)
	public void ShowHire( StaffType type, int variantKey, int nowMilliseconds, IStaffSpriteBanks? banks = null )
	{
		if ( HireKey is { } key && key == (type, variantKey) && Hire != null )
			return;
		HireKey = (type, variantKey);
		var figure = StaffSpriteVm.Create( StaffSpriteProgram.HireFigure, nowMilliseconds );
		figure[StaffSpriteRegister.Kind] = (uint)StaffSpriteState.CategoryOf( type );
		figure[StaffSpriteRegister.Variant] = unchecked( (uint)variantKey );
		figure[StaffSpriteRegister.Slot] = StaffSpriteRules.PortraitSlot;
		figure[StaffSpriteRegister.PrimaryScale] = BitConverter.SingleToUInt32Bits( EmployeeScale );
		var animations = banks?.AnimationsOf( type, variantKey );
		figure[StaffSpriteRegister.FrameCount] = (uint)(animations != null && StaffSpriteRules.PortraitSlot < animations.Animations.Count ? animations.Animations[StaffSpriteRules.PortraitSlot].FramesPerDirection : 0);
		Hire = figure;
	}

	/// <summary>The hire window's close (<c>0x0049B7B4</c>): the figure goes and the cached pair is -1, so the next open starts a new figure.</summary>
	// [BIN:TPW-EXE:0x0049B7B4 hire window close] deletes the figure (0x475550) and writes -1 to 0x7523E8 and 0x7523EC
	public void CloseHire()
	{
		Hire = null;
		HireKey = null;
	}

	/// <summary>One scheduler pass (<c>0x00475360</c>): every sprite of the windows steps when the sample is past its due time.</summary>
	public void Tick( int nowMilliseconds )
	{
		if ( StaffClone != null )
			StaffSpriteVm.Tick( StaffClone, nowMilliseconds );
		GuestClone?.Tick( nowMilliseconds );
		if ( Hire != null )
			StaffSpriteVm.Tick( Hire, nowMilliseconds );
	}
}
