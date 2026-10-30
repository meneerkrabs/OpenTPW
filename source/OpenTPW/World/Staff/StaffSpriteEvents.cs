namespace OpenTPW;

/// <summary>
/// What the staff code of tp.exe asks of a person's sprite wrapper (<see cref="StaffSpriteState"/>), event by event: the state setters queue the templates (the table <c>0x0075A418</c>), the walk step queues the
/// walk, run or hold and an interval, the constructors, the drop and the rest's end create the source and the pick-up and the rest delete it. The pre-update applies what is queued. The pure rules, so every
/// event is testable without a world or a renderer.
/// </summary>
internal static class StaffSpriteEvents
{
	/// <summary>The template numbers the staff code queues (the table <c>0x0075A418</c>).</summary>
	public const int Walk = 1, Run = 2, Stand = 3, Patrol = 9, Research = 10, Sweep = 0x11, Repair = 0x12, Strike = 0x13, Catch = 0x16, Hold = 0x17, Release = 0x18;

	/// <summary>
	/// The template a state entry queues, 0 for none, as the setters do: 0x005054D0 (0 stand 3, 1 patrol walk 9, 2 walk 1, 4 patrol walk 9 with the strike bonus, 5 strike 0x13), the handyman's 0x004D7330 (8 and 10 patrol walk 9, 9 and 11 sweep 0x11),
	/// the mechanic's 0x004DA370 (12 patrol walk 9, 13 repair 0x12), the researcher's 0x00502C20 (15 research 10) and the guard's 0x004D65D0 (16, 18 and 19 queue none: the walk step asks for the run and the hold).
	/// </summary>
	// [BIN:TPW-EXE:0x005054D0 state setter] case 0 queues 3 (0x004FA460), 1 and 4 queue 9, 2 queues 1, 5 queues 0x13 after clearing the sprite angle (+0x1C), 6 queues 0x14; 3 and 7 delete the source (0x004D4170)
	// [BIN:TPW-EXE:0x004D7330 handyman setter] 8 and 10: the bonus +0xC2 = 25 and template 9; 9 and 11: the stamp +0x214 and template 0x11; the others 0x005054D0
	// [BIN:TPW-EXE:0x004DA370 mechanic setter] 0xC queues 9, 0xD queues 0x12; the others 0x005054D0
	// [BIN:TPW-EXE:0x00502C20 researcher setter] 0xF stamps +0x214 and queues 10; the others 0x005054D0
	// [BIN:TPW-EXE:0x004D65D0 guard setter] 0x10 sets the bonus and the chase timer, 0x12 and 0x13 clear the bonus: none queues a template; the others clear the bonus and go to 0x005054D0
	public static int TemplateOf( StaffType type, StaffJob job ) => job switch
	{
		StaffJob.Idle => Stand,
		StaffJob.Patrol or StaffJob.ToStrike => Patrol,
		StaffJob.ToRestArea => Walk,
		StaffJob.Striking => Strike,
		StaffJob.ToLitter or StaffJob.ToToilet when type == StaffType.Handyman => Patrol,
		StaffJob.Sweeping or StaffJob.Cleaning when type == StaffType.Handyman => Sweep,
		StaffJob.ToRide when type == StaffType.Mechanic => Patrol,
		StaffJob.Repairing when type == StaffType.Mechanic => Repair,
		StaffJob.Researching when type == StaffType.Researcher => Research,
		_ => 0
	};

	/// <summary>Whether a state has a source: a resting member is inside the Staff Room and a carried one is the tool's sprite instead (0x005054D0 states 3 and 7 delete it).</summary>
	public static bool HasSource( StaffJob job ) => job is not (StaffJob.Resting or StaffJob.PickedUp);

	/// <summary>The state setters' queue and deletions for a member entering <paramref name="job"/> from <paramref name="previous"/>.</summary>
	public static void EnterJob( ParkStaff staff, StaffMember member, StaffJob job, StaffJob previous )
	{
		var sprite = member.Sprite;
		if ( !HasSource( job ) )
		{
			// 0x005054D0 states 3 and 7
			sprite.Delete();
			return;
		}
		// 0x00506D10 (the rest's end) and 0x00505EA0 (the drop) make the stand source before they set state 0
		if ( job == StaffJob.Idle && sprite.Source == null && previous is StaffJob.Resting or StaffJob.PickedUp )
			Create( staff, member, Stand );
		if ( TemplateOf( member.Type, job ) is > 0 and var template )
			sprite.QueueTemplate( template );
	}

	/// <summary>Creates the member's source in <paramref name="template"/> at the staff's host clock sample (<c>0x004D4140</c>), the person's variant being the bank key its sprite is drawn with.</summary>
	public static void Create( ParkStaff staff, StaffMember member, int template ) =>
		member.Sprite.Create( member.Type, StaffSpriteRules.KeyOf( member ), template, staff.SpriteClockMilliseconds, staff.SpriteBanks );

	/// <summary>
	/// The drop (<c>0x00505EA0</c>, which sets a carried member down and takes a resting one out of the Staff Room) and the placing click's constructors (<c>0x004D4340</c>, <c>0x004D5DE0</c>, <c>0x004D6B60</c>,
	/// <c>0x004D9EB0</c>, <c>0x00502600</c>): a fresh stand source; the drop then sets state 0 (queues the stand), a constructor runs its first search instead.
	/// </summary>
	// [BIN:TPW-EXE:0x00505EA0 drop] 0x004D4140(3, +0x24, +0x20) makes the source, then 0x005054D0(0) queues template 3 (0x004FA460); the constructors call 0x004D4140(3, ...) after the person base constructor and run the role's search, which queues its own template
	public static void Dropped( ParkStaff staff, StaffMember member, bool constructed )
	{
		member.Sprite.Delete();
		Create( staff, member, Stand );
		if ( !constructed )
			member.Sprite.QueueTemplate( Stand );
	}

	/// <summary>The pick-up's state 7 (<c>0x005054D0</c>): the source goes.</summary>
	public static void PickUp( StaffMember member ) => member.Sprite.Delete();

	/// <summary>
	/// A walk step that is still walking (<c>0x004FA2A0</c>): a guard with a speed bonus above 0 runs (template 2), an escorting guard holds (0x17), anyone else walks (1); the template is queued unless the source's restart
	/// PC is that program's start already.
	/// </summary>
	// [BIN:TPW-EXE:0x004FA3B3 walk request] +0xC2 compared unsigned with the constant 0 (0x0075C7F0) for the types 1 (guest) and 7 (guard); a guard in state 0x12 (0x004D6350) uses the hold program 0x0074EF90 (template 0x17); 0x00475C50 compares the restart PC (+0xC) with the program, 0x004217F0 queues
	public static void Walking( StaffMember member )
	{
		var guard = member.Type == StaffType.Guard;
		var template = guard && unchecked( (ushort)member.SpeedBonus ) > 0 ? Run : guard && member.Job == StaffJob.Escorting ? Hold : Walk;
		if ( !member.Sprite.RestartsAt( template ) )
			member.Sprite.QueueTemplate( template );
	}

	/// <summary>The walk step's interval (<c>0x004D41D0</c>); the step asks for one every time it walks.</summary>
	public static void Interval( StaffMember member, int interval ) => member.Sprite.QueueInterval( interval );

	/// <summary>A guard has caught its guest (<c>0x004D6790</c>, after 0x004FEC10): template 0x16.</summary>
	public static void GuardCaught( StaffMember member ) => member.Sprite.QueueTemplate( Catch );

	/// <summary>A guard's escort has ended (<c>0x004D6A50</c>): template 0x18 before the guest is set down.</summary>
	public static void GuardReleased( StaffMember member ) => member.Sprite.QueueTemplate( Release );

	/// <summary>
	/// An entertainer's performance (<c>0x004D46D0</c>): the template is 13 when the bank has one performance entry, else 13 + (the search's second world draw mod (entries - 1)).
	/// </summary>
	// [BIN:TPW-EXE:0x004D47CC performance template] 0x00541FA0 returns the bank's count n of present entries (+0x20C); n == 1 gives 13, else (draw % (n - 1)) + 13, 0x004217F0 queues it and the state becomes 14
	public static void Performance( ParkStaff staff, StaffMember member, uint draw )
	{
		var entries = staff.SpriteBanks?.AnimationsOf( member.Type, StaffSpriteRules.KeyOf( member ) )?.PerformanceCount ?? 1;
		var template = entries <= 1 ? StaffSpriteState.FirstPerformanceTemplate : StaffSpriteState.FirstPerformanceTemplate + (int)(draw % (uint)(entries - 1));
		member.Sprite.QueueTemplate( template );
	}

	/// <summary>
	/// A member that should have a source and has none (a park saved before the sources were kept, an imported original save, a member put in a state without its setter): a fresh stand source and the state's
	/// own template. The original keeps the sprite records in the save's script block (SAVE-013), which is not applied.
	/// </summary>
	public static void Reconstruct( ParkStaff staff, StaffMember member )
	{
		Create( staff, member, Stand );
		var template = member.Job switch
		{
			StaffJob.Chasing => Run,
			StaffJob.Escorting => Hold,
			StaffJob.ReturningFromEscort => Walk,
			StaffJob.Performing => StaffSpriteState.FirstPerformanceTemplate,
			var job => TemplateOf( member.Type, job )
		};
		if ( template != 0 )
			member.Sprite.QueueTemplate( template );
	}

	/// <summary>
	/// The staff pre-update (<c>0x00505490</c> through <c>0x004FA870</c>, run before the role's state handler on every update of every staff thing): applies the pending template and interval, and first
	/// makes the source a member that should show a sprite is missing.
	/// </summary>
	public static void PreUpdate( ParkStaff staff, StaffMember member )
	{
		if ( member.Sprite.Source == null && StaffSpriteRules.HasBodySprite( member ) )
			Reconstruct( staff, member );
		member.Sprite.ApplyPending( member.Type, StaffSpriteRules.KeyOf( member ), staff.SpriteBanks );
	}
}
