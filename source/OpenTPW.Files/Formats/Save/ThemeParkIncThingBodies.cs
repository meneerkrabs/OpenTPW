namespace OpenTPW;

// Typed views of the Theme Park Inc thing bodies whose layouts are traced (ThemeParkIncBodyLayout). The words keep the width the serializer copies; the reader of the original
// is a plain copy of each member, so a word's signedness or float type is not visible there and is not guessed. Members the records do not name are reachable through
// ThemeParkIncBody by their path and offset. The names are the serializer's own (their spellings included, FinishTIme and MissonNeeded are the original's).

/// <summary>One weather spell of the weather thing (load 0x643100): the month, the quality, the length, a valid flag and the type.</summary>
public sealed record ThemeParkIncWeatherSpell(
	uint Month,
	uint Quality,
	uint Length,
	bool Valid,
	uint Type )
{
	internal static ThemeParkIncWeatherSpell Read( ThemeParkIncBody body, string p ) =>
		new(
			body.U32( p + "mMonth" ),
			body.U32( p + "mQuality" ),
			body.U32( p + "mLength" ),
			body.Flag( p + "mValid" ),
			body.U32( p + "mType" ) );
}

/// <summary>The weather thing (model 15, load 0x641510): two weather spells, the rain and lightning state and the wind. The words keep their stored width; whether one is signed or a float is not visible in the reader.</summary>
public sealed record ThemeParkIncWeather(
	int Id,
	SaveThingBase Base,
	IReadOnlyList<ThemeParkIncWeatherSpell> Spells,
	uint Quality,
	uint Length,
	uint CurrentDrops,
	uint TargetDrops,
	bool Lightning,
	uint LightningCountdown,
	uint LightningNumThisPeriod,
	uint ThunderCountdown,
	byte[] LastLightningLocation,
	uint RainSoundHandle,
	uint PType,
	uint Type,
	uint WindDir,
	uint WindForce,
	bool Overridden )
{
	internal static ThemeParkIncWeather Read( int id, ThemeParkIncBody body )
	{
		var p = string.Empty;
		return new(
			id,
			new( body.U16( p + "mX" ), body.U16( p + "mY" ), body.U16( p + "mMapChild" ), body.U16( p + "mMapParent" ) ),
			Enumerable.Range( 0, 2 ).Select( index => ThemeParkIncWeatherSpell.Read( body, p + $"mSpell[{index}]." ) ).ToArray(),
			body.U32( p + "mQuality" ),
			body.U32( p + "mLength" ),
			body.U32( p + "mCurrentDrops" ),
			body.U32( p + "mTargetDrops" ),
			body.Flag( p + "mLightning" ),
			body.U32( p + "mLightningCountdown" ),
			body.U32( p + "mLightningNumThisPeriod" ),
			body.U32( p + "mThunderCountdown" ),
			body.Bytes12( p + "mLastLightningLocation" ),
			body.U32( p + "mRainSoundHandle" ),
			body.U32( p + "mPType" ),
			body.U32( p + "mType" ),
			body.U32( p + "mWindDir" ),
			body.U32( p + "mWindForce" ),
			body.Flag( p + "mOverridden" ) );
	}
}

/// <summary>The staff HQ thing (model 9, load 0x6326b0): the strike state, six training budgets (object 0x6394d0), the 20 staff slots and the trained-staff words. The 12-byte strike levels and staff slots are kept raw.</summary>
public sealed record ThemeParkIncStaffHq(
	int Id,
	SaveThingBase Base,
	uint ForceStrike,
	ushort StaffMemberPickedUp,
	IReadOnlyList<byte[]> StrikeLevels,
	IReadOnlyList<uint> Budgets,
	bool HaveEverTrained,
	IReadOnlyList<byte[]> StaffSlots,
	IReadOnlyList<uint> TrainedStaff )
{
	internal static ThemeParkIncStaffHq Read( int id, ThemeParkIncBody body )
	{
		var p = string.Empty;
		return new(
			id,
			new( body.U16( p + "mX" ), body.U16( p + "mY" ), body.U16( p + "mMapChild" ), body.U16( p + "mMapParent" ) ),
			body.U32( p + "mForceStrike" ),
			body.U16( p + "mStaffMemberPickedUp" ),
			Enumerable.Range( 0, 6 ).Select( index => body.Bytes12( p + $"mStrikeLevel[{index}]" ) ).ToArray(),
			Enumerable.Range( 0, 6 ).Select( index => body.U32( p + $"mBudget[{index}]" ) ).ToArray(),
			body.Flag( p + "mHaveEverTrained" ),
			Enumerable.Range( 0, 20 ).Select( index => body.Bytes12( p + $"mStaffSlots[{index}]" ) ).ToArray(),
			Enumerable.Range( 0, 15 ).Select( index => body.U32( p + $"mTrainedStaff[{index}]" ) ).ToArray() );
	}
}

/// <summary>One advisor message record (0x71af30): the thing it is about, a secondary word, the message, its score and response number and three flags.</summary>
public sealed record ThemeParkIncAdvisorMessage(
	ushort ThingId,
	uint Secondary,
	uint Message,
	uint Score,
	bool Valid,
	uint ResponseNo,
	bool OverrideOnlyOnce,
	bool ScoreOverridden )
{
	internal static ThemeParkIncAdvisorMessage Read( ThemeParkIncBody body, string p ) =>
		new(
			body.U16( p + "mThingId" ),
			body.U32( p + "mSecondary" ),
			body.U32( p + "mMessage" ),
			body.U32( p + "mScore" ),
			body.Flag( p + "mValid" ),
			body.U32( p + "mResponseNo" ),
			body.Flag( p + "mOverrideOnlyOnce" ),
			body.Flag( p + "mScoreOverridden" ) );
}

/// <summary>The advisor controller thing (model 11, load 0x7177f0): the eight-entry message queue, the queue position, the last action, the current message and the panel state.</summary>
public sealed record ThemeParkIncAdvisorController(
	int Id,
	SaveThingBase Base,
	uint TimeSig,
	uint SoundId,
	IReadOnlyList<ThemeParkIncAdvisorMessage> Queue,
	uint NextAvailableQueuePos,
	uint LastActionStarted,
	uint LastActionDuration,
	ThemeParkIncAdvisorMessage Current,
	bool RequestedLastTutorial,
	uint MessagePlaying,
	uint DelayedMessage,
	bool PanelUpFlg,
	bool FromHelpButton,
	bool FromUiPanel,
	uint AdvisorDelayTicks )
{
	internal static ThemeParkIncAdvisorController Read( int id, ThemeParkIncBody body )
	{
		var p = string.Empty;
		return new(
			id,
			new( body.U16( p + "mX" ), body.U16( p + "mY" ), body.U16( p + "mMapChild" ), body.U16( p + "mMapParent" ) ),
			body.U32( p + "mTimeSig" ),
			body.U32( p + "mSoundId" ),
			Enumerable.Range( 0, 8 ).Select( index => ThemeParkIncAdvisorMessage.Read( body, p + $"mQueue[{index}]." ) ).ToArray(),
			body.U32( p + "mNextAvailableQueuePos" ),
			body.U32( p + "mLastActionStarted" ),
			body.U32( p + "mLastActionDuration" ),
			ThemeParkIncAdvisorMessage.Read( body, p + "mCurrent." ),
			body.Flag( p + "mRequestedLastTutorial" ),
			body.U32( p + "mMessagePlaying" ),
			body.U32( p + "m_msgDelayedMessage" ),
			body.Flag( p + "mPanelUpFlg" ),
			body.Flag( p + "m_bFromHelpButton" ),
			body.Flag( p + "m_bFromUIPanel" ),
			body.U32( p + "m_lAdvisorDelayTicks" ) );
	}
}

/// <summary>One research project of the lab (0x61f670).</summary>
public sealed record ThemeParkIncResearchProject(
	ushort ObjectId,
	uint Cat,
	byte ResearchGroup,
	byte UpgradeLevel,
	uint Cost,
	uint CashPerBrainPoint,
	uint ResearchDone,
	uint Type,
	uint StaffRqd,
	uint StaffSA )
{
	internal static ThemeParkIncResearchProject Read( ThemeParkIncBody body, string p ) =>
		new(
			body.U16( p + "mObjId" ),
			body.U32( p + "mCat" ),
			body.U8( p + "mResearchGroup" ),
			body.U8( p + "mUpgradeLevel" ),
			body.U32( p + "mCost" ),
			body.U32( p + "mCashPerBrainPoint" ),
			body.U32( p + "mResearchDone" ),
			body.U32( p + "mType" ),
			body.U32( p + "StaffRqd" ),
			body.U32( p + "StaffSA" ) );
}

/// <summary>One research group of the lab (0x6202c0).</summary>
public sealed record ThemeParkIncResearchGroup(
	uint Effort,
	uint Output,
	uint CurrentProject,
	byte HighestGroupAvailable )
{
	internal static ThemeParkIncResearchGroup Read( ThemeParkIncBody body, string p ) =>
		new(
			body.U32( p + "effort" ),
			body.U32( p + "output" ),
			body.U32( p + "currentProject" ),
			body.U8( p + "highestGroupAvailable" ) );
}

/// <summary>One slot of the lab staff assignment: the primary and the secondary staff member (thing IDs).</summary>
public sealed record ThemeParkIncResearchStaffPair(
	ushort Pristaf,
	ushort Secstaf )
{
	internal static ThemeParkIncResearchStaffPair Read( ThemeParkIncBody body, string p ) =>
		new(
			body.U16( p + "pristaf" ),
			body.U16( p + "secstaf" ) );
}

/// <summary>The research lab thing (model 14, load 0x61bce0): 256 projects, seven research groups, the workload counters and the primary and secondary staff assignment.</summary>
public sealed record ThemeParkIncResearchLab(
	int Id,
	SaveThingBase Base,
	IReadOnlyList<ThemeParkIncResearchProject> Projects,
	IReadOnlyList<ThemeParkIncResearchGroup> Groups,
	uint NumProjects,
	uint WorkLoad,
	uint MostExpensiveProject,
	bool LabActive,
	uint Pcurrentp,
	uint Scurrentp,
	byte Pavail,
	byte Savail,
	uint LPrits,
	uint LPricat,
	uint LPrirt,
	uint LSeccat,
	IReadOnlyList<ThemeParkIncResearchStaffPair> Assignments,
	IReadOnlyList<ushort> Consultants )
{
	internal static ThemeParkIncResearchLab Read( int id, ThemeParkIncBody body )
	{
		var p = string.Empty;
		return new(
			id,
			new( body.U16( p + "mX" ), body.U16( p + "mY" ), body.U16( p + "mMapChild" ), body.U16( p + "mMapParent" ) ),
			Enumerable.Range( 0, 256 ).Select( index => ThemeParkIncResearchProject.Read( body, p + $"mProjects[{index}]." ) ).ToArray(),
			Enumerable.Range( 0, 7 ).Select( index => ThemeParkIncResearchGroup.Read( body, p + $"mGroups[{index}]." ) ).ToArray(),
			body.U32( p + "mNumProjects" ),
			body.U32( p + "mWorkLoad" ),
			body.U32( p + "mMostExpensiveProject" ),
			body.Flag( p + "mLabActive" ),
			body.U32( p + "pcurrentp" ),
			body.U32( p + "scurrentp" ),
			body.U8( p + "pavail" ),
			body.U8( p + "savail" ),
			body.U32( p + "lPrits" ),
			body.U32( p + "lPricat" ),
			body.U32( p + "lPrirt" ),
			body.U32( p + "lSeccat" ),
			Enumerable.Range( 0, 20 ).Select( index => ThemeParkIncResearchStaffPair.Read( body, p + $"mAssignments[{index}]." ) ).ToArray(),
			Enumerable.Range( 0, 2 ).Select( index => body.U16( p + $"mConsultants[{index}]" ) ).ToArray() );
	}
}

/// <summary>One challenge record (0x5c2320). Times are 100 ns stamps like the calendar's.</summary>
public sealed record ThemeParkIncChallenge(
	uint Type,
	uint TargetTime,
	uint TargetVal,
	uint TargetVal2,
	ushort TargetObj,
	ushort TargetObj2,
	uint TargetStaffType,
	uint CashPrize,
	uint DoubleOrNothingFollowup,
	bool CheckAtEndOnly,
	uint ChallengeLost,
	uint CurrentStatus,
	ushort ThingInFocus,
	uint Delay,
	uint MinDaysTilTrigger,
	uint MaxDaysTilTrigger,
	uint GracePeriodLimit,
	uint GraceTargetVal,
	bool OfferTimeSet,
	long OfferTime,
	long FinishTime,
	uint Value,
	uint Value2,
	uint TriggerData,
	uint GtPrize,
	uint SharePrize,
	uint SharePriceEffectIfWon,
	uint SharePriceEffectIfLost,
	uint Sender,
	uint ScriptIndex,
	uint MissionNeeded,
	uint PromoGiven,
	bool SentTimeWarning )
{
	internal static ThemeParkIncChallenge Read( ThemeParkIncBody body, string p ) =>
		new(
			body.U32( p + "mType" ),
			body.U32( p + "mTargetTime" ),
			body.U32( p + "mTargetVal" ),
			body.U32( p + "mTargetVal2" ),
			body.U16( p + "mTargetObj" ),
			body.U16( p + "mTargetObj2" ),
			body.U32( p + "mTargetStaffType" ),
			body.U32( p + "mCashPrize" ),
			body.U32( p + "mDoubleOrNothingFollowup" ),
			body.Flag( p + "mCheckAtEndOnly" ),
			body.U32( p + "mChallengeLost" ),
			body.U32( p + "mCurrentStatus" ),
			body.U16( p + "mThingInFocus" ),
			body.U32( p + "mDelay" ),
			body.U32( p + "mMinDaysTilTrigger" ),
			body.U32( p + "mMaxDaysTilTrigger" ),
			body.U32( p + "mGracePeriodLimit" ),
			body.U32( p + "mGraceTargetVal" ),
			body.Flag( p + "mOfferTimeSet" ),
			body.I64( p + "mOfferTime" ),
			body.I64( p + "mFinishTIme" ),
			body.U32( p + "mValue" ),
			body.U32( p + "mValue2" ),
			body.U32( p + "mTriggerData" ),
			body.U32( p + "mGTPrize" ),
			body.U32( p + "mSharePrize" ),
			body.U32( p + "mSharePriceEffectIfWon" ),
			body.U32( p + "mSharePriceEffectIfLost" ),
			body.U32( p + "mSender" ),
			body.U32( p + "mScriptIndex" ),
			body.U32( p + "mMissonNeeded" ),
			body.U32( p + "mPromoGiven" ),
			body.Flag( p + "mSentTimeWarning" ) );
}

/// <summary>The challenge system thing (model 19, load 0x5c7c70): ten challenge records, the delay to the next challenge, the switch and the ten actual things.</summary>
public sealed record ThemeParkIncChallengeSystem(
	int Id,
	SaveThingBase Base,
	IReadOnlyList<ThemeParkIncChallenge> Challenges,
	long NextChallengeDelayTime,
	bool SystemSwitchedOn,
	IReadOnlyList<ushort> ActualThings )
{
	internal static ThemeParkIncChallengeSystem Read( int id, ThemeParkIncBody body )
	{
		var p = string.Empty;
		return new(
			id,
			new( body.U16( p + "mX" ), body.U16( p + "mY" ), body.U16( p + "mMapChild" ), body.U16( p + "mMapParent" ) ),
			Enumerable.Range( 0, 10 ).Select( index => ThemeParkIncChallenge.Read( body, p + $"mChallenges[{index}]." ) ).ToArray(),
			body.I64( p + "mNextChallengeDelayTime" ),
			body.Flag( p + "mSystemSwitchedOn" ),
			Enumerable.Range( 0, 10 ).Select( index => body.U16( p + $"mActualThing[{index}]" ) ).ToArray() );
	}
}


/// <summary>One saved mail message of the tag system (a 0xe2c-byte record, load 0x6355d0): the 3,624 bytes before the flag are kept raw (texts and time stamps copied into the message object), then the flag byte the saver counts.</summary>
public sealed record ThemeParkIncMailMessage( byte[] Message, byte Flag );

/// <summary>The tag system thing (model 12, load 0x6355d0): the thing base, <c>numtags</c> and that many mail messages. 12 bytes with no message, as in the prebuilt.</summary>
public sealed record ThemeParkIncTagSystem( int Id, SaveThingBase Base, IReadOnlyList<ThemeParkIncMailMessage> Messages )
{
	internal static ThemeParkIncTagSystem Read( int id, ThemeParkIncBody body ) => new(
		id, new( body.U16( "mX" ), body.U16( "mY" ), body.U16( "mMapChild" ), body.U16( "mMapParent" ) ),
		Enumerable.Range( 0, (int)Math.Max( 0, (int)body.U32( "numtags" ) ) ).Select( index => new ThemeParkIncMailMessage( body.Raw( $"MailMessage[{index}].mMessage" ).ToArray(), body.U8( $"MailMessage[{index}].mFlag" ) ) ).ToArray() );
}

/// <summary>One value and its worked-out flag in the park analyser (<c>mVal</c>, <c>mWorkedOut</c>).</summary>
public readonly record struct ThemeParkIncAnalyserValue( uint Value, bool WorkedOut );

/// <summary>One of the 1,024 grid cells of the park analyser: a rectangle in map squares and nine value pairs (two, then seven).</summary>
public sealed record ThemeParkIncAnalyserCell( uint StartX, uint StartY, uint SizeX, uint SizeY, IReadOnlyList<ThemeParkIncAnalyserValue> MeasureA, IReadOnlyList<ThemeParkIncAnalyserValue> MeasureB );

/// <summary>
/// The park analyser thing (model 13, load 0x5ace30): the map grid sizes, 1,024 grid cells, the history rings and totals, and the park statistics. The rings keep their entries as
/// words (their element widths are 1, 2 or 4 bytes); what each ring measures is not named by the serializer.
/// </summary>
public sealed record ThemeParkIncParkAnalyser(
	int Id,
	SaveThingBase Base,
	uint MapSquaresPerCell,
	uint GridSizeX,
	uint GridSizeY,
	uint MapSizeX,
	uint MapSizeY,
	IReadOnlyList<ThemeParkIncAnalyserCell> Cells,
	IReadOnlyList<ThemeParkIncHistoryRing> ShortRings,
	IReadOnlyList<ThemeParkIncAnalyserValue> Totals,
	IReadOnlyList<ThemeParkIncHistoryRing> DayRings,
	ThemeParkIncHistoryRing Ring50,
	ThemeParkIncHistoryRing Ring5,
	IReadOnlyList<ThemeParkIncHistoryRing> ByteRings,
	uint MisbehavingKids,
	uint MisbehavingKidsThatGotAwayWithIt,
	uint MisbehavingKidsEver,
	uint LifetimeVisitors,
	uint VisitorsThisMonth,
	long LastStrike,
	long LastPuke,
	long LastPrank,
	long ParkLastOpened,
	long LastObjectBuilt,
	long LastStaffHired,
	uint MostPaidForTicket,
	uint LargestBusLoad,
	uint LongestStay,
	uint NumBreakdownsEver,
	uint NumBalloonsSoldEver,
	uint NumCostumesSoldEver )
{
	public const int GridCells = 1024;

	private static ThemeParkIncAnalyserValue Value( ThemeParkIncBody body, string p ) => new( body.U32( p + "mVal" ), body.Flag( p + "mWorkedOut" ) );

	internal static ThemeParkIncParkAnalyser Read( int id, ThemeParkIncBody body ) => new(
		id,
		new( body.U16( "mX" ), body.U16( "mY" ), body.U16( "mMapChild" ), body.U16( "mMapParent" ) ),
		body.U32( "mMapSquaresPerCell" ), body.U32( "mGridSizeX" ), body.U32( "mGridSizeY" ), body.U32( "mMapSizeX" ), body.U32( "mMapSizeY" ),
		Enumerable.Range( 0, GridCells ).Select( cell => new ThemeParkIncAnalyserCell(
			body.U32( $"mCells[{cell}].mStartX" ), body.U32( $"mCells[{cell}].mStartY" ), body.U32( $"mCells[{cell}].mSizeX" ), body.U32( $"mCells[{cell}].mSizeY" ),
			Enumerable.Range( 0, 2 ).Select( index => Value( body, $"mCells[{cell}].mMeasureA[{index}]." ) ).ToArray(),
			Enumerable.Range( 0, 7 ).Select( index => Value( body, $"mCells[{cell}].mMeasureB[{index}]." ) ).ToArray() ) ).ToArray(),
		Enumerable.Range( 0, 4 ).Select( index => body.Ring( $"mShortRings[{index}]" ) ).ToArray(),
		Enumerable.Range( 0, 6 ).Select( index => Value( body, $"mTotals[{index}]." ) ).ToArray(),
		Enumerable.Range( 0, 21 ).Select( index => body.Ring( $"mDayRings[{index}]" ) ).ToArray(),
		body.Ring( "ring50" ),
		body.Ring( "ring5" ),
		Enumerable.Range( 0, 22 ).Select( index => body.Ring( $"mByteRings[{index}]" ) ).ToArray(),
		body.U32( "mMisbehavingKids" ), body.U32( "mMisbehavingKidsThatGotAwayWithIt" ), body.U32( "mMisbehavingKidsEver" ), body.U32( "mLifetimeVisitors" ), body.U32( "mVisitorsThisMonth" ),
		body.I64( "mLastStrike" ), body.I64( "mLastPuke" ), body.I64( "mLastPrank" ), body.I64( "mParkLastOpened" ), body.I64( "mLastObjectBuilt" ), body.I64( "mLastStaffHired" ),
		body.U32( "mMostPaidForTicket" ), body.U32( "mLargestBusLoad" ), body.U32( "mLongestStay" ),
		body.U32( "mNumBreakdownsEver" ), body.U32( "mNumBalloonsSoldEver" ), body.U32( "mNumCostumesSoldEver" ) );
}

/// <summary>
/// A guest, staff member or gardener thing (models 1, 4 to 8 and 20): the body read through its traced layout. None of these is in the prebuilt save, so the layouts rest on the
/// load routines and an emulation of them, not on a witnessed save. The staff share the person block with the guests (sprite, walking state, thoughts) and add the staff block with
/// six history rings; the guest adds the guest block. Everything beyond the helpers below is reachable by path through <see cref="Body"/>.
/// </summary>
public sealed record ThemeParkIncPerson( int Id, ThemeParkIncThingModel Model, SaveThingBase Base, ThemeParkIncBody Body )
{
	/// <summary>The cell the walk is heading for (<c>mAccurateDestX</c>, <c>mAccurateDestY</c>).</summary>
	public (ushort X, ushort Y) AccurateDestination => (Body.U16( "mAccurateDestX" ), Body.U16( "mAccurateDestY" ));

	public uint SpriteId => Body.U32( "mSpriteID" );

	public bool IsStaff => Model is >= ThemeParkIncThingModel.Mechanic and <= ThemeParkIncThingModel.Researcher or ThemeParkIncThingModel.Gardener;

	/// <summary>A guest's cash (<c>mCash</c>); null for staff.</summary>
	public uint? Cash => Model == ThemeParkIncThingModel.Guest ? Body.U32( "mCash" ) : null;

	/// <summary>The member's own state word (<c>mState</c>), the guest's or the staff member's.</summary>
	public uint State => Body.U32( "mState" );

	/// <summary>A staff member's name (<c>mName</c>, 33 UTF-16 units); null for a guest.</summary>
	public string? Name => IsStaff ? Body.Utf16( "mName", 33 ) : null;

	/// <summary>A staff member's hire time stamp, 100 ns since 1601-01-01 like the calendar; null for a guest.</summary>
	public long? TimeHired => IsStaff ? Body.I64( "mTimeHired" ) : null;

	/// <summary>The thing id the role links to next (<c>mNext</c>); null for a guest.</summary>
	public ushort? Next => IsStaff ? Body.U16( "mNext" ) : null;

	/// <summary>A staff member's six history rings; empty for a guest.</summary>
	public IReadOnlyList<ThemeParkIncHistoryRing> Histories => IsStaff ? Enumerable.Range( 0, 6 ).Select( index => Body.Ring( $"mHistory[{index}]" ) ).ToArray() : Array.Empty<ThemeParkIncHistoryRing>();

	internal static ThemeParkIncPerson Read( int id, ThemeParkIncThingModel model, ThemeParkIncBody body ) =>
		new( id, model, new( body.U16( "mX" ), body.U16( "mY" ), body.U16( "mMapChild" ), body.U16( "mMapParent" ) ), body );
}

/// <summary>The typed things of the traced-layout models collected while a World block is parsed; each singleton may occur once.</summary>
internal sealed class ThemeParkIncTracedThings
{
	private readonly List<ThemeParkIncPerson> people = new();

	public IReadOnlyList<ThemeParkIncPerson> People => people.AsReadOnly();
	public ThemeParkIncStaffHq? StaffHq { get; private set; }
	public ThemeParkIncAdvisorController? AdvisorController { get; private set; }
	public ThemeParkIncParkAnalyser? ParkAnalyser { get; private set; }
	public ThemeParkIncResearchLab? ResearchLab { get; private set; }
	public ThemeParkIncWeather? Weather { get; private set; }
	public ThemeParkIncChallengeSystem? ChallengeSystem { get; private set; }
	public ThemeParkIncTagSystem? TagSystem { get; private set; }

	public void Add( ThemeParkIncThing thing, ThemeParkIncBody body )
	{
		switch ( thing.Model )
		{
			case ThemeParkIncThingModel.StaffHq:
				StaffHq = Single( StaffHq, ThemeParkIncStaffHq.Read( thing.Id, body ), thing );
				break;
			case ThemeParkIncThingModel.AdvisorController:
				AdvisorController = Single( AdvisorController, ThemeParkIncAdvisorController.Read( thing.Id, body ), thing );
				break;
			case ThemeParkIncThingModel.ParkAnalyser:
				ParkAnalyser = Single( ParkAnalyser, ThemeParkIncParkAnalyser.Read( thing.Id, body ), thing );
				break;
			case ThemeParkIncThingModel.ResearchLab:
				ResearchLab = Single( ResearchLab, ThemeParkIncResearchLab.Read( thing.Id, body ), thing );
				break;
			case ThemeParkIncThingModel.Weather:
				Weather = Single( Weather, ThemeParkIncWeather.Read( thing.Id, body ), thing );
				break;
			case ThemeParkIncThingModel.ChallengeSystem:
				ChallengeSystem = Single( ChallengeSystem, ThemeParkIncChallengeSystem.Read( thing.Id, body ), thing );
				break;
			case ThemeParkIncThingModel.TagSystem:
				TagSystem = Single( TagSystem, ThemeParkIncTagSystem.Read( thing.Id, body ), thing );
				break;
			default:
				people.Add( ThemeParkIncPerson.Read( thing.Id, thing.Model, body ) );
				break;
		}
	}

	private static T Single<T>( T? existing, T value, ThemeParkIncThing thing ) where T : class =>
		existing == null ? value : throw new InvalidDataException( $"The save Theme Park Inc World holds more than one {thing.Model} (thing {thing.Id})." );
}
