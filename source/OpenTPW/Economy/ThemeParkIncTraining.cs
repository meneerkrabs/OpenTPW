namespace OpenTPW;

/// <summary>What the "Select Ability To Learn" panel's choice did (<c>0x580550</c>).</summary>
public enum ThemeParkIncTrainingChoice
{
	/// <summary>The member already has the ability: UITEXT 714 "EMPLOYEE ALREADY TRAINED".</summary>
	AlreadyTrained,
	/// <summary>The member has another ability, which retraining replaces: the confirmation (UITEXT 715) must be answered first.</summary>
	NeedsConfirmation,
	/// <summary>The ability is chosen and the member walks to the training room (state 7).</summary>
	Started
}

/// <summary>A training notice for the HUD: the advisor's "training finished" (0x175) or "training cancelled" (0x1C9) line with the member and the ability.</summary>
public readonly record struct ThemeParkIncTrainingNotice( ThemeParkIncStaffMember Member, bool Finished, int Ability );

/// <summary>
/// Theme Park Inc's staff training rules (docs/reverse/TPI-STAFF.md §12.2): which abilities a member may learn, the training room's capacity and the training tick. The walk to the room and the
/// state changes are <see cref="ThemeParkIncStaffWorld"/>'s.
/// </summary>
public static class ThemeParkIncTraining
{
	/// <summary>The experience every ability needs (hard-coded 10000, not <c>MinExperienceForTraining</c>).</summary>
	public const int RequiredExperience = 10000;

	/// <summary>The trainees the training room takes before the panel reports it full.</summary>
	public const int RoomCapacity = 3;

	/// <summary>The training states: walking to the room (7), training (8), and their suspended forms 0x26 and 0x25.</summary>
	public const int ToTrainingRoom = 7, Training = 8, TrainingSuspended = 0x25, ToTrainingRoomSuspended = 0x26;

	/// <summary>Whether <paramref name="role"/> can learn <paramref name="ability"/> (<c>0x62B6C0</c>): mechanics 1–6, handymen 7–8, guards 9, gardeners 10–14; entertainers and scientists none.</summary>
	// [BIN:TPI-EXE:0x62B6C0 ability compatible] kind byte +2 through the byte table 0x62B774 and the targets 0x62B75C: kind 4 1..6, 5 7..8, 7 9, 0x14 10..14, every other kind (6, 8) 0; ability 0 is never
	// compatible
	public static bool Compatible( ThemeParkIncStaffRole role, int ability ) => role switch
	{
		ThemeParkIncStaffRole.Mechanic => ability is >= 1 and <= 6,
		ThemeParkIncStaffRole.Handyman => ability is 7 or 8,
		ThemeParkIncStaffRole.Guard => ability == 9,
		ThemeParkIncStaffRole.Gardener => ability is >= 10 and <= 14,
		_ => false
	};

	/// <summary>Whether <paramref name="member"/> may learn <paramref name="ability"/> (<c>0x639B20</c>): the career discovered it, his role takes it and he has 10000 experience.</summary>
	// [BIN:TPI-EXE:0x639B20 can learn] 0x5F0370(ability) (the career's +0x1064[ability], "Ability not activated."), 0x62B6C0 ("Not compatible ability."), then the experience +0x210 >= 10000 unsigned
	// (0x639B77, "Not enough experience.")
	public static bool CanLearn( ThemeParkIncStaffMember member, int ability, IReadOnlyList<byte> discovered ) =>
		ability >= 0 && ability < discovered.Count && discovered[ability] != 0 && Compatible( member.Role, ability ) && (uint)member.Experience >= RequiredExperience;

	/// <summary>The abilities the panel lists for <paramref name="member"/> (<c>0x580860</c>): every ability 0..14 he may learn, in order.</summary>
	// [BIN:TPI-EXE:0x580860 ability list] for i = 0..14 the rows of 0x639B20(member, i) (ABILITIES.str[i], data i); no MinExperienceForTraining test
	public static IEnumerable<int> Learnable( ThemeParkIncStaffMember member, IReadOnlyList<byte> discovered ) =>
		Enumerable.Range( 0, ThemeParkIncCareer.AbilityCount ).Where( ability => CanLearn( member, ability, discovered ) );

	/// <summary>The UITEXT line the panel shows for a member with nothing to learn, by role (707..712).</summary>
	// [BIN:TPI-EXE:0x580A65 no ability text] by 0x62AF70's code: 0 -> 707, 1 -> 708, 2 -> 711, 3 -> 710, 4 -> 712, 5 -> 709
	public static int NothingToLearnText( ThemeParkIncStaffRole role ) => role switch
	{
		ThemeParkIncStaffRole.Handyman => 707,
		ThemeParkIncStaffRole.Mechanic => 708,
		ThemeParkIncStaffRole.Entertainer => 711,
		ThemeParkIncStaffRole.Guard => 710,
		ThemeParkIncStaffRole.Researcher => 712,
		_ => 709
	};

	/// <summary>The UITEXT line of a full training room.</summary>
	public const int RoomFullText = 713, AlreadyTrainedText = 714, RetrainText = 715, SelectAbilityText = 705;

	/// <summary>The staff in training or on their way (<c>0x639F40</c>): every member in state 7, 8, 0x25 or 0x26.</summary>
	// [BIN:TPI-EXE:0x639F40 trainees] every thing (0x649060/0x6490A0) of kind 4, 5, 6, 7, 8 or 0x14 whose state +0x1B4 is 7, 8, 0x25 or 0x26
	public static int Trainees( IEnumerable<ThemeParkIncStaffMember> members ) =>
		members.Count( member => member.State is ToTrainingRoom or Training or TrainingSuspended or ToTrainingRoomSuspended );

	/// <summary>Whether the panel reports the training room full (<c>0x580A04</c>: three trainees or more).</summary>
	public static bool RoomFull( IEnumerable<ThemeParkIncStaffMember> members ) => Trainees( members ) >= RoomCapacity;

	/// <summary>
	/// The training tick (<c>0x62AA50</c>, state 8): the progress grows by SkillIncrement; at ProgessUpperLimit the learnt ability replaces the trained one. True when the training finished (the
	/// caller then runs the finish and <see cref="Reward"/>).
	/// </summary>
	// [BIN:TPI-EXE:0x62AA50 training tick] +0x224 += SkillIncrement (0xA022B4); below ProgessUpperLimit (0xA022B8, signed) nothing else; else +0x218 = +0x220, +0x224 = 0, +0x220 = 0
	public static bool Tick( ThemeParkIncStaffMember member, ThemeParkIncStaffSettings settings )
	{
		member.TrainingProgress = unchecked( member.TrainingProgress + settings.SkillIncrement );
		if ( member.TrainingProgress < settings.ProgressUpperLimit )
			return false;
		member.Ability = member.LearningAbility;
		member.TrainingProgress = 0;
		member.LearningAbility = 0;
		return true;
	}

	/// <summary>A finished training's reward after the finish: motivation 100, TrainingExtraExperience, then the agency's TrainingExperienceGainModifier percent, at most MaxExperience.</summary>
	// [BIN:TPI-EXE:0x62AAD4 training reward] +0x234 = 100.0f; +0x210 += TrainingExtraExperience (0xA022AC); m = StaffAgencies[+0x244].TrainingExperienceGainModifier (0xA0242C + 0x24 × agency): m != 0 and
	// experience != 0 add (u32)(experience × m, 32-bit imul) / 100 (unsigned, 0x51EB851F); an experience above MaxExperience (unsigned) becomes MaxExperience
	public static void Reward( ThemeParkIncStaffMember member, ThemeParkIncStaffSettings settings )
	{
		member.Motivation = 100f;
		member.Experience = unchecked( member.Experience + settings.TrainingExtraExperience );
		var modifier = member.Agency >= 0 && member.Agency < settings.Agencies.Count ? settings.Agencies[member.Agency].TrainingExperienceGainModifier : 0;
		if ( modifier != 0 && member.Experience != 0 )
			member.Experience = unchecked( member.Experience + (int)(unchecked( (uint)(member.Experience * modifier) ) / 100u) );
		if ( (uint)member.Experience > (uint)settings.MaxExperience )
			member.Experience = settings.MaxExperience;
	}

	/// <summary>Entering state 8 (<c>0x628180</c>'s arm 8): the chosen ability becomes the one in training with no progress, for a member with 10000 experience. False when nothing is chosen (the cancel runs).</summary>
	// [BIN:TPI-EXE:0x62852A state 8 entry] +0x22C == 0: the cancel 0x62E7A0 (and the state still becomes 8); else experience >= 10000 (unsigned): +0x220 = +0x22C, +0x224 = 0; else both kept
	public static bool Enter( ThemeParkIncStaffMember member )
	{
		if ( member.ChosenAbility == 0 )
			return false;
		if ( (uint)member.Experience >= RequiredExperience )
		{
			member.LearningAbility = member.ChosenAbility;
			member.TrainingProgress = 0;
		}
		return true;
	}
}
