namespace OpenTPW;

// Layouts traced from the load routines of Game.exe (member name, width and order as the routines read them); each was also checked against an emulation of its routine, see docs/THEME-PARK-INC.md ("Thing bodies").
public sealed partial class ThemeParkIncBodyLayout
{
	// [BIN:TPI-EXE:0x609170 person load] thing base 0x637230, then the walking person: the sprite script, the accurate destination, speeds, the path-following state (pos, vel, the 5-entry subpath buffer, the nav mode), the sprite and the 32-entry event history, ending with mTimeBubbleShown; the guest (0x60d260) and the staff base (0x6267c0) both call it
	private const string PersonFragment = """
		2 mX
		2 mY
		2 mMapChild
		2 mMapParent
		4 mSpriteScript
		4 mNextAnim
		4 mNextServiceInterval
		2 mAccurateDestX
		2 mAccurateDestY
		2 mAdjustorSpeed
		2 mBaseSpeed
		1 mCount
		4 mESPSprite
		2 mLastRecordedMapId
		8 force
		8 formation_pos
		8 local_xaxis
		8 local_yaxis
		4 mass
		4 max_force
		4 max_speed
		4 mCantReachDest
		4 nav_mode
		4 path_buffer_count
		4 path_count
		1 path_finished
		4 path_last_progress
		4 path_stuck_buffer
		4 path_subpath_dist
		4 path_tail_dist
		8 path_target_pos
		4 path_timestamp
		4 path_total_count
		4 path_total_dist
		8 pos
		4 radius
		rep 5 subpath
		  8 buffer
		  4 dist
		end
		8 vel
		4 mPreviousSpeed
		4 mPreviousX
		4 mPreviousY
		4 mStrandedTime
		2 mPurposeSpeed
		4 mSetDestSuccessfully
		4 mSpriteAngle
		4 mSpriteID
		4 mSpriteUnderRideCtrl
		4 mOnRideSoNoFloatingThingsAllowed
		4 mWhingeTimeout
		2 mActionHistIndex
		rep 32 mEventHistory
		  4 _
		end
		4 mLastThought
		4 mThoughtScript
		4 mTimeBubbleShown
		""";

	// [BIN:TPI-EXE:0x6267c0 staff load] after the person: salary, two stats written as floats truncated to a byte (pv, members +0x230 and +0x234), jobs done, the inline name of 33 UTF-16 units, rest area, the states, the hire time stamp, experience, a third stat (pv, +0x238), the abilities, the tick counters, the wage demand, six history rings (serializer 0x62f2b0) and the patrol rectangle
	private const string StaffFragment = """
		4 mSalary
		4 pv_0x230
		4 pv_0x234
		4 mJobsDone
		rep 33 mName
		  2 _
		end
		2 mRestArea
		4 mState
		4 mForcedState
		4 mSavedState
		4 mTimeStartedIdling
		8 mTimeHired
		4 mExperience
		4 pv_0x238
		rep 2 mKnownAbilities
		  4 _
		end
		4 mNumberOfKnownAbilities
		4 mCurrentlyLearning
		4 mGoingToLearn
		4 mSAProgress
		4 mSlotLocation
		4 mWorkingTicks
		4 mPatrollingTicks
		4 mRestingTicks
		4 mIdleTicks
		4 mTrainingTicks
		4 mResearchingTicks
		4 mStrikeTicks
		4 mTimeStartedResearching
		2 mRRoom
		1 mRmode
		4 mAgency
		4 mWageDemandDay
		4 mWageDemandPay
		1 mResearching
		rep 6 mHistory
		  ring 4 30 _
		end
		4 m_sx
		4 m_sy
		4 m_ex
		4 m_ey
		""";

	// [BIN:TPI-EXE:0x60d260 guest load] case 1 of the dispatch at 0x64cb89 (thunk 0x4045a2, constructor 0x64df50, object 0x2f0 bytes): the person, then the guest members up to mLastDayChecked; the seven pv members are floats truncated to a byte (+0x1bc, +0x1cc, +0x1d8, +0x1c8, +0x1dc, +0x1d0, +0x1d4) and msFood to msRides are such floats at +0x2d0 to +0x2e0. No ring: 619 bytes
	private const string GuestBody = """
		use person
		4 mArrivalDate
		4 mArrivalIndex
		4 mBalloonScript
		4 mBalloonColour
		4 mUmbrellaScript
		4 mUmbrellaType
		4 mUmbrellaState
		4 mExactSpriteTypeIndex
		4 mBeenAdmitted
		4 mCash
		4 mExitLevel
		4 pv_0x1bc
		4 pv_0x1cc
		4 mLastPosX
		4 mLastPosY
		4 pv_0x1d8
		2 mMajorDest
		4 mNumRides
		4 mNumShops
		4 mNumSideshows
		4 mNumSideshowsWon
		4 mPaidAdmission
		4 mParkOpeningWaitingTime
		1 mPersonType
		4 mPrank
		4 mPrankTimer
		rep 4 mPrevious
		  2 ride
		  2 temporaryRide
		end
		2 mQNext
		2 mQPrev
		4 mQueueMoveDelay
		1 mQueuePos
		4 mRemainingBalloonLife
		4 mToiletBoobyFlg
		4 mGeneration
		4 mPreferredExcitement
		4 mBoredomThreshold
		1 mFoundWhingeVictimFlg
		1 mForcePrankFlg
		4 mFirstAidTimeout
		2 mSavedMajorDest
		2 mPhotoBooth
		4 mSavedState
		4 mState
		4 pv_0x1c8
		4 mTimeOfLastSpotAnim
		4 mTimeStartedIdling
		4 pv_0x1dc
		4 pv_0x1d0
		4 pv_0x1d4
		4 msFood
		4 msDrink
		4 msBought
		4 msWon
		4 msRides
		4 mPriceToPayAtEnd
		4 mInfoDeskTimer
		4 mCashSpent
		1 mWonLastThang
		4 mLastDayChecked
		""";

	// [BIN:TPI-EXE:0x5d9e40 mechanic load] case 4 at 0x64c785 (thunk 0x40ab14): the staff base 0x6267c0 (person, staff, six rings), the repair timer, the object to repair and the role's mNext link
	private const string MechanicBody = """
		use person
		use staff
		4 mDurationOfRepair
		2 mObjectToRepair
		2 mNext
		""";

	// [BIN:TPI-EXE:0x5d4ba0 handyman load] case 5 at 0x64c830 (thunk 0x40c130): the staff base, the litter cell, cleaning start, toilet and bin to clean, mNext
	private const string HandymanBody = """
		use person
		use staff
		2 mTargetLitterCell
		4 mTimeStartedCleaning
		2 mToiletToClean
		2 mBinToClean
		2 mNext
		""";

	// [BIN:TPI-EXE:0x5d1600 entertainer load] case 6 at 0x64c988 (thunk 0x40401b): the staff base, the time the entertaining started, mNext
	private const string EntertainerBody = """
		use person
		use staff
		4 mTimeStartedEntertaining
		2 mNext
		""";

	// [BIN:TPI-EXE:0x5d3500 guard load] case 7 at 0x64ca31 (thunk 0x40622b): the staff base, the perpetrator, the patient, the prosecution and first aid time stamps, mNext
	private const string GuardBody = """
		use person
		use staff
		2 mPerp
		2 mPatientId
		4 mProsecutionTimestamp
		4 mFirstAidTimestamp
		2 mNext
		""";

	// [BIN:TPI-EXE:0x61b3c0 researcher load] case 8 at 0x64cada (thunk 0x4032c4): the staff base, mNext
	private const string ResearcherBody = """
		use person
		use staff
		2 mNext
		""";

	// [BIN:TPI-EXE:0x5d2510 gardener load] case 20 at 0x64c8df (thunk 0x4079a5): the staff base, the watering start, the plant to water, mNext
	private const string GardenerBody = """
		use person
		use staff
		4 mTimeStartedWatering
		2 mPlantToWater
		2 mNext
		""";

	// [BIN:TPI-EXE:0x6326b0 staff HQ load] case 9 at 0x64cd3e (thunk 0x408edb, object 0x1ac bytes): thing base, mForceStrike, mStaffMemberPickedUp, six 12-byte strike levels, the training budget object 0x6394d0 (six budgets and mHaveEverTrained), 20 staff slots of 12 bytes, 15 trained-staff words: 411 bytes
	private const string StaffHqBody = """
		2 mX
		2 mY
		2 mMapChild
		2 mMapParent
		4 mForceStrike
		2 mStaffMemberPickedUp
		rep 6 mStrikeLevel
		  12 _
		end
		rep 6 mBudget
		  4 _
		end
		1 mHaveEverTrained
		rep 20 mStaffSlots
		  12 _
		end
		rep 15 mTrainedStaff
		  4 _
		end
		""";

	// [BIN:TPI-EXE:0x7177f0 advisor controller load] case 11 at 0x64ce81 (thunk 0x40a7a9, object 0x118 bytes): thing base, a time stamp (0x448c80), mSoundId, the 8-entry message queue (0x71af30), the queue position and last action, the current message (0x71af30), the tutorial flag, mMessagePlaying, the delayed message and three panel flags and the delay ticks: 233 bytes
	private const string AdvisorBody = """
		2 mX
		2 mY
		2 mMapChild
		2 mMapParent
		4 mTimeSig
		4 mSoundId
		rep 8 mQueue
		  2 mThingId
		  4 mSecondary
		  4 mMessage
		  4 mScore
		  1 mValid
		  4 mResponseNo
		  1 mOverrideOnlyOnce
		  1 mScoreOverridden
		end
		4 mNextAvailableQueuePos
		4 mLastActionStarted
		4 mLastActionDuration
		group mCurrent
		  2 mThingId
		  4 mSecondary
		  4 mMessage
		  4 mScore
		  1 mValid
		  4 mResponseNo
		  1 mOverrideOnlyOnce
		  1 mScoreOverridden
		end
		1 mRequestedLastTutorial
		4 mMessagePlaying
		4 m_msgDelayedMessage
		1 mPanelUpFlg
		1 m_bFromHelpButton
		1 m_bFromUIPanel
		4 m_lAdvisorDelayTicks
		""";

	// [BIN:TPI-EXE:0x5ace30 park analyser load] case 13 at 0x64cf8e (thunk 0x40978c, object 0x22878 bytes): thing base, the map grid sizes, 1024 grid cells (a rectangle and nine value pairs), then history rings (four of 3 u16, 21 of 144 u32, one of 50, one of 5 and 22 of 50 bytes) and six totals, ending with the park statistics. 63,177 bytes with empty rings, 76,617 in the prebuilt
	private const string AnalyserBody = """
		2 mX
		2 mY
		2 mMapChild
		2 mMapParent
		4 mMapSquaresPerCell
		4 mGridSizeX
		4 mGridSizeY
		4 mMapSizeX
		4 mMapSizeY
		rep 1024 mCells
		  4 mStartX
		  4 mStartY
		  4 mSizeX
		  4 mSizeY
		  rep 2 mMeasureA
		    4 mVal
		    1 mWorkedOut
		  end
		  rep 7 mMeasureB
		    4 mVal
		    1 mWorkedOut
		  end
		end
		rep 4 mShortRings
		  ring 2 3 _
		end
		rep 6 mTotals
		  4 mVal
		  1 mWorkedOut
		end
		rep 21 mDayRings
		  ring 4 144 _
		end
		ring 4 50 ring50
		ring 4 5 ring5
		rep 22 mByteRings
		  ring 1 50 _
		end
		4 mMisbehavingKids
		4 mMisbehavingKidsThatGotAwayWithIt
		4 mMisbehavingKidsEver
		4 mLifetimeVisitors
		4 mVisitorsThisMonth
		8 mLastStrike
		8 mLastPuke
		8 mLastPrank
		8 mParkLastOpened
		8 mLastObjectBuilt
		8 mLastStaffHired
		4 mMostPaidForTicket
		4 mLargestBusLoad
		4 mLongestStay
		4 mNumBreakdownsEver
		4 mNumBalloonsSoldEver
		4 mNumCostumesSoldEver
		""";

	// [BIN:TPI-EXE:0x61bce0 research lab load] case 14 at 0x64d21d (thunk 0x4016cc, object 0x2508 bytes): thing base, 256 projects of 32 bytes (0x61f670), seven research groups of 13 bytes (0x6202c0), the workload counters and the primary and secondary assignment: 8,414 bytes
	private const string ResearchLabBody = """
		2 mX
		2 mY
		2 mMapChild
		2 mMapParent
		rep 256 mProjects
		  2 mObjId
		  4 mCat
		  1 mResearchGroup
		  1 mUpgradeLevel
		  4 mCost
		  4 mCashPerBrainPoint
		  4 mResearchDone
		  4 mType
		  4 StaffRqd
		  4 StaffSA
		end
		rep 7 mGroups
		  4 effort
		  4 output
		  4 currentProject
		  1 highestGroupAvailable
		end
		4 mNumProjects
		4 mWorkLoad
		4 mMostExpensiveProject
		1 mLabActive
		4 pcurrentp
		4 scurrentp
		1 pavail
		1 savail
		4 lPrits
		4 lPricat
		4 lPrirt
		4 lSeccat
		rep 20 mAssignments
		  2 pristaf
		  2 secstaf
		end
		rep 2 mConsultants
		  2 _
		end
		""";

	// [BIN:TPI-EXE:0x641510 weather load] case 15 at 0x64d31d (thunk 0x401014, object 0x80 bytes): thing base, two weather spells (0x643100), the rain and lightning state and the wind: 104 bytes
	private const string WeatherBody = """
		2 mX
		2 mY
		2 mMapChild
		2 mMapParent
		rep 2 mSpell
		  4 mMonth
		  4 mQuality
		  4 mLength
		  1 mValid
		  4 mType
		end
		4 mQuality
		4 mLength
		4 mCurrentDrops
		4 mTargetDrops
		1 mLightning
		4 mLightningCountdown
		4 mLightningNumThisPeriod
		4 mThunderCountdown
		12 mLastLightningLocation
		4 mRainSoundHandle
		4 mPType
		4 mType
		4 mWindDir
		4 mWindForce
		1 mOverridden
		""";

	// [BIN:TPI-EXE:0x5c7c70 challenge system load] case 19 at 0x64d4af (thunk 0x408d9b, object 0x5d8 bytes): thing base, 10 challenge records of 125 bytes (0x5c2320), the next-challenge delay, the switch and the 10 actual things: 1,287 bytes
	private const string ChallengeBody = """
		2 mX
		2 mY
		2 mMapChild
		2 mMapParent
		rep 10 mChallenges
		  4 mType
		  4 mTargetTime
		  4 mTargetVal
		  4 mTargetVal2
		  2 mTargetObj
		  2 mTargetObj2
		  4 mTargetStaffType
		  4 mCashPrize
		  4 mDoubleOrNothingFollowup
		  1 mCheckAtEndOnly
		  4 mChallengeLost
		  4 mCurrentStatus
		  2 mThingInFocus
		  4 mDelay
		  4 mMinDaysTilTrigger
		  4 mMaxDaysTilTrigger
		  4 mGracePeriodLimit
		  4 mGraceTargetVal
		  1 mOfferTimeSet
		  8 mOfferTime
		  8 mFinishTIme
		  4 mValue
		  4 mValue2
		  4 mTriggerData
		  4 mGTPrize
		  4 mSharePrize
		  4 mSharePriceEffectIfWon
		  4 mSharePriceEffectIfLost
		  4 mSender
		  4 mScriptIndex
		  4 mMissonNeeded
		  4 mPromoGiven
		  1 mSentTimeWarning
		end
		8 mNextChallengeDelayTime
		1 mSystemSwitchedOn
		rep 10 mActualThing
		  2 _
		end
		""";

	// [BIN:TPI-EXE:0x6355d0 tag system load] case 12 at 0x64cf1d (thunk 0x405f29, object 0x830 bytes): thing base, a u32 numtags (0x6357bd) and numtags MailMessage records of 0xe2c bytes (0x635821); the loader copies a record's fields into a mail message object, of which the byte at +0xe28 (the loop at 0x635653 counts the ones where it is 0 when saving) is the only member the routine tests. 12 bytes when numtags is 0, as in the prebuilt
	private const string TagSystemBody = """
		2 mX
		2 mY
		2 mMapChild
		2 mMapParent
		table numtags MailMessage
		  3624 mMessage
		  1 mFlag
		  3 mPadding
		end
		""";
}
