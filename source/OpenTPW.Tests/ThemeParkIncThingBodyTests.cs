using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The traced thing-body layouts of Theme Park Inc (guests, staff, gardener, staff HQ, advisor controller, park analyser, research lab, weather, challenge system): synthetic bodies
/// made from the layouts, damage to them, the widths the load routines of Game.exe consume (checked by emulating the routines with chosen ring counts), and the prebuilt's six
/// formerly closure-derived bodies. Only the prebuilt and the thing-chain tests need OPENTPW_TPI_PATH; none of the guest, staff and gardener bodies is in any save.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncThingBodyTests
{
	private static readonly ThemeParkIncThingModel[] Models =
	{
		ThemeParkIncThingModel.Guest, ThemeParkIncThingModel.Mechanic, ThemeParkIncThingModel.Handyman, ThemeParkIncThingModel.Entertainer, ThemeParkIncThingModel.Guard,
		ThemeParkIncThingModel.Researcher, ThemeParkIncThingModel.Gardener, ThemeParkIncThingModel.StaffHq, ThemeParkIncThingModel.AdvisorController,
		ThemeParkIncThingModel.ParkAnalyser, ThemeParkIncThingModel.ResearchLab, ThemeParkIncThingModel.Weather, ThemeParkIncThingModel.ChallengeSystem,
	};

	private static readonly ThemeParkIncThingModel[] StaffModels =
	{
		ThemeParkIncThingModel.Mechanic, ThemeParkIncThingModel.Handyman, ThemeParkIncThingModel.Entertainer, ThemeParkIncThingModel.Guard,
		ThemeParkIncThingModel.Researcher, ThemeParkIncThingModel.Gardener,
	};

	private static byte Pattern( int index ) => (byte)(0x21 + index % 90);

	/// <summary>A body written from the layout: patterned bytes in every field and ring, with the given entry counts for the rings in the order the layout reads them.</summary>
	private static byte[] Body( ThemeParkIncThingModel model, params int[] ringCounts )
	{
		var layout = ThemeParkIncBodyLayout.For( model )!;
		var output = new List<byte>();
		var ring = 0;

		void Pad( int count )
		{
			for ( var index = 0; index < count; index++ )
				output.Add( Pattern( output.Count ) );
		}

		void Write( IReadOnlyList<ThemeParkIncBodySegment> segments )
		{
			foreach ( var segment in segments )
			{
				switch ( segment.Kind )
				{
					case ThemeParkIncBodySegmentKind.Field:
						Pad( segment.Size );
						break;
					case ThemeParkIncBodySegmentKind.Repeat:
						for ( var index = 0; index < segment.Count; index++ )
							Write( segment.Children );
						break;
					case ThemeParkIncBodySegmentKind.Group:
						Write( segment.Children );
						break;
					default:
					{
						var count = ring < ringCounts.Length ? ringCounts[ring] : 0;
						ring++;
						output.AddRange( BitConverter.GetBytes( 0xFFFFFFFFu ) );
						output.AddRange( BitConverter.GetBytes( (uint)count ) );
						output.Add( 0 );
						Pad( segment.Size );
						Pad( count * segment.Size );
						break;
					}
				}
			}
		}

		Write( layout.Segments );
		return output.ToArray();
	}

	/// <summary>Writes <paramref name="value"/> little-endian into the field at <paramref name="path"/>.</summary>
	private static void Patch( byte[] body, ThemeParkIncThingModel model, string path, ulong value )
	{
		var field = new ThemeParkIncBody( model, body ).Field( path );
		for ( var index = 0; index < field.Size; index++ )
			body[field.Offset + index] = index < 8 ? (byte)(value >> (8 * index)) : (byte)0;
	}

	private static int RingCount( ThemeParkIncThingModel model ) => Count( ThemeParkIncBodyLayout.For( model )!.Segments );

	private static int Count( IReadOnlyList<ThemeParkIncBodySegment> segments ) => segments.Sum( segment => segment.Kind switch
	{
		ThemeParkIncBodySegmentKind.Ring => 1,
		ThemeParkIncBodySegmentKind.Repeat => segment.Count * Count( segment.Children ),
		ThemeParkIncBodySegmentKind.Group => Count( segment.Children ),
		_ => 0,
	} );

	/// <summary>
	/// Widths with every ring empty and full, which Game.exe's load routines consume: the guest and the staff HQ, advisor, lab, weather and challenge bodies have no ring; the staff
	/// have six rings of 30 four-byte entries; the analyser has 4 of 3 words (2 bytes), 21 of 144 and one each of 50 and 5 words (4 bytes) and 22 of 51 bytes. Each pair was
	/// obtained by running the routine on a stream whose ring counts were all 0 and all at capacity.
	/// </summary>
	[TestMethod]
	public void LayoutWidthsAreThoseOfTheEmulatedLoadRoutines()
	{
		var expected = new Dictionary<ThemeParkIncThingModel, (int Minimum, int Maximum)>
		{
			[ThemeParkIncThingModel.Guest] = (619, 619),
			[ThemeParkIncThingModel.Mechanic] = (690, 1_410),
			[ThemeParkIncThingModel.Handyman] = (694, 1_414),
			[ThemeParkIncThingModel.Entertainer] = (688, 1_408),
			[ThemeParkIncThingModel.Guard] = (696, 1_416),
			[ThemeParkIncThingModel.Researcher] = (684, 1_404),
			[ThemeParkIncThingModel.Gardener] = (690, 1_410),
			[ThemeParkIncThingModel.StaffHq] = (411, 411),
			[ThemeParkIncThingModel.AdvisorController] = (233, 233),
			[ThemeParkIncThingModel.ParkAnalyser] = (63_177, 76_639),
			[ThemeParkIncThingModel.ResearchLab] = (8_414, 8_414),
			[ThemeParkIncThingModel.Weather] = (104, 104),
			[ThemeParkIncThingModel.ChallengeSystem] = (1_287, 1_287),
		};
		CollectionAssert.AreEquivalent( Models, ThemeParkIncBodyLayout.All.Keys.ToArray() );
		foreach ( var model in Models )
		{
			var layout = ThemeParkIncBodyLayout.For( model )!;
			Assert.AreEqual( expected[model].Minimum, layout.MinimumBytes, model.ToString() );
			Assert.AreEqual( expected[model].Maximum, layout.MaximumBytes, model.ToString() );
			Assert.AreEqual( expected[model].Minimum, Body( model ).Length, model.ToString() );
			Assert.AreEqual( expected[model].Minimum, layout.Measure( Body( model ), 0 ), model.ToString() );
		}
		Assert.AreEqual( 6, RingCount( ThemeParkIncThingModel.Mechanic ) );
		Assert.AreEqual( 49, RingCount( ThemeParkIncThingModel.ParkAnalyser ) );
		Assert.AreEqual( 0, RingCount( ThemeParkIncThingModel.Guest ) );
		Assert.IsNull( ThemeParkIncBodyLayout.For( ThemeParkIncThingModel.Attraction ) );
		Assert.IsNull( ThemeParkIncBodyLayout.For( ThemeParkIncThingModel.Bank ) );
	}

	/// <summary>Widths for chosen ring counts, each also produced by running the load routine in an emulator on a stream with those counts (rings in the order they are read).</summary>
	[TestMethod]
	public void WidthsFollowTheRingCountsAsTheEmulatedRoutinesDo()
	{
		var analyserPrebuilt = new[] { 3, 3, 3, 3 }.Concat( Enumerable.Repeat( 144, 21 ) ).Concat( new[] { 50, 5 } ).Concat( Enumerable.Repeat( 50, 22 ) ).ToArray();
		var analyserFull = new[] { 0, 0, 0, 0 }.Concat( Enumerable.Repeat( 144, 21 ) ).Concat( new[] { 50, 5 } ).Concat( Enumerable.Repeat( 51, 22 ) ).ToArray();
		var vectors = new (ThemeParkIncThingModel Model, int[] Counts, int Width)[]
		{
			(ThemeParkIncThingModel.Mechanic, new[] { 3, 0, 30, 1, 0, 7 }, 854),
			(ThemeParkIncThingModel.Guard, new[] { 30, 30, 30, 30, 30, 30 }, 1_416),
			(ThemeParkIncThingModel.Researcher, new[] { 1, 2, 3, 4, 5, 6 }, 768),
			(ThemeParkIncThingModel.Handyman, new[] { 0, 0, 0, 0, 0, 0 }, 694),
			(ThemeParkIncThingModel.Entertainer, new[] { 5, 4, 3, 2, 1, 0 }, 748),
			(ThemeParkIncThingModel.Gardener, new[] { 30, 0, 0, 0, 0, 29 }, 926),
			(ThemeParkIncThingModel.ParkAnalyser, analyserPrebuilt, 76_617),
			(ThemeParkIncThingModel.ParkAnalyser, analyserFull, 76_615),
			(ThemeParkIncThingModel.ParkAnalyser, new[] { 1, 2, 3, 0 }, 63_189),
		};
		foreach ( var (model, counts, width) in vectors )
		{
			var body = Body( model, counts );
			Assert.AreEqual( width, body.Length, model.ToString() );
			var layout = ThemeParkIncBodyLayout.For( model )!;
			Assert.AreEqual( width, layout.Measure( body, 0 ), model.ToString() );
			// bytes after the body are not part of it
			Assert.AreEqual( width, layout.Measure( body.Concat( new byte[] { 1, 2, 3 } ).ToArray(), 0 ), model.ToString() );
			// measured from a non-zero offset
			Assert.AreEqual( width, layout.Measure( new byte[] { 9, 9, 9 }.Concat( body ).ToArray(), 3 ), model.ToString() );
		}
	}

	[TestMethod]
	public void FieldsTileEveryBodyWithoutGapsAndNameTheTracedMembers()
	{
		foreach ( var model in Models )
		{
			var counts = Enumerable.Range( 0, 49 ).Select( ring => (ring * 7) % 4 ).ToArray();
			var body = Body( model, counts );
			var typed = new ThemeParkIncBody( model, body );
			var position = 0;
			foreach ( var field in typed.Fields )
			{
				Assert.AreEqual( position, field.Offset, $"{model} {field.Path}" );
				position += field.Size;
			}
			Assert.AreEqual( body.Length, position, model.ToString() );
			Assert.AreEqual( typed.Fields.Count, typed.Fields.Select( field => field.Path ).Distinct().Count(), $"{model} paths are unique" );
			Assert.AreEqual( 0, typed.Fields[0].Offset );
			CollectionAssert.AreEqual( new[] { "mX", "mY", "mMapChild", "mMapParent" }, typed.Fields.Take( 4 ).Select( field => field.Path ).ToArray(), model.ToString() );
			Assert.IsFalse( Encoding.ASCII.GetString( body ).Contains( "LDMT", StringComparison.Ordinal ), $"{model}: the builder's pattern holds no header marker" );
		}

		// offsets of named members in the traced routines (stream offsets of the emulated reads), rings empty
		void Offsets( ThemeParkIncThingModel model, params (string Path, int Offset, int Size)[] members )
		{
			var typed = new ThemeParkIncBody( model, Body( model ) );
			foreach ( var (path, offset, size) in members )
			{
				var field = typed.Field( path );
				Assert.AreEqual( (offset, size), (field.Offset, field.Size), $"{model} {path}" );
			}
		}

		Offsets( ThemeParkIncThingModel.Guest, ("mSpriteScript", 8, 4), ("mAccurateDestX", 20, 2), ("pos", 132, 8), ("mSpriteID", 238, 4), ("mEventHistory[31]", 380, 4), ("mTimeBubbleShown", 392, 4),
			("mCash", 432, 4), ("mExitLevel", 436, 4), ("pv_0x1bc", 440, 4), ("mLastPosX", 448, 4), ("mMajorDest", 460, 2), ("mPersonType", 486, 1), ("mState", 554, 4),
			("msFood", 582, 4), ("msRides", 598, 4), ("mWonLastThang", 614, 1), ("mLastDayChecked", 615, 4) );
		Offsets( ThemeParkIncThingModel.Mechanic, ("mTimeBubbleShown", 392, 4), ("mSalary", 396, 4), ("pv_0x230", 400, 4), ("mJobsDone", 408, 4), ("mName[0]", 412, 2), ("mName[32]", 476, 2),
			("mRestArea", 478, 2), ("mState", 480, 4), ("mTimeHired", 496, 8), ("mExperience", 504, 4), ("pv_0x238", 508, 4), ("mKnownAbilities[1]", 516, 4), ("mAgency", 575, 4),
			("mResearching", 587, 1), ("mHistory[0].mCurrentEntry", 588, 4), ("mHistory[0].mNumEntries", 592, 4), ("mHistory[0].mTemp", 597, 4), ("mHistory[1].mCurrentEntry", 601, 4),
			("mHistory[5].mTemp", 662, 4), ("m_sx", 666, 4), ("m_ey", 678, 4), ("mDurationOfRepair", 682, 4), ("mObjectToRepair", 686, 2), ("mNext", 688, 2) );
		Offsets( ThemeParkIncThingModel.Handyman, ("mTargetLitterCell", 682, 2), ("mTimeStartedCleaning", 684, 4), ("mToiletToClean", 688, 2), ("mBinToClean", 690, 2), ("mNext", 692, 2) );
		Offsets( ThemeParkIncThingModel.Entertainer, ("mTimeStartedEntertaining", 682, 4), ("mNext", 686, 2) );
		Offsets( ThemeParkIncThingModel.Guard, ("mPerp", 682, 2), ("mPatientId", 684, 2), ("mProsecutionTimestamp", 686, 4), ("mFirstAidTimestamp", 690, 4), ("mNext", 694, 2) );
		Offsets( ThemeParkIncThingModel.Researcher, ("mNext", 682, 2) );
		Offsets( ThemeParkIncThingModel.Gardener, ("mTimeStartedWatering", 682, 4), ("mPlantToWater", 686, 2), ("mNext", 688, 2) );
		Offsets( ThemeParkIncThingModel.StaffHq, ("mForceStrike", 8, 4), ("mStaffMemberPickedUp", 12, 2), ("mStrikeLevel[0]", 14, 12), ("mStrikeLevel[5]", 74, 12), ("mBudget[0]", 86, 4),
			("mHaveEverTrained", 110, 1), ("mStaffSlots[0]", 111, 12), ("mStaffSlots[19]", 339, 12), ("mTrainedStaff[0]", 351, 4), ("mTrainedStaff[14]", 407, 4) );
		Offsets( ThemeParkIncThingModel.AdvisorController, ("mTimeSig", 8, 4), ("mSoundId", 12, 4), ("mQueue[0].mThingId", 16, 2), ("mQueue[7].mScoreOverridden", 183, 1), ("mNextAvailableQueuePos", 184, 4),
			("mCurrent.mThingId", 196, 2), ("mCurrent.mScoreOverridden", 216, 1), ("mRequestedLastTutorial", 217, 1), ("mMessagePlaying", 218, 4), ("m_msgDelayedMessage", 222, 4),
			("mPanelUpFlg", 226, 1), ("m_lAdvisorDelayTicks", 229, 4) );
		Offsets( ThemeParkIncThingModel.Weather, ("mSpell[0].mMonth", 8, 4), ("mSpell[1].mType", 38, 4), ("mQuality", 42, 4), ("mLightning", 58, 1), ("mLastLightningLocation", 71, 12), ("mWindDir", 95, 4), ("mOverridden", 103, 1) );
		Offsets( ThemeParkIncThingModel.ChallengeSystem, ("mChallenges[0].mType", 8, 4), ("mChallenges[0].mOfferTime", 72, 8), ("mChallenges[1].mType", 133, 4), ("mChallenges[9].mSentTimeWarning", 1257, 1),
			("mNextChallengeDelayTime", 1258, 8), ("mSystemSwitchedOn", 1266, 1), ("mActualThing[9]", 1285, 2) );
		Offsets( ThemeParkIncThingModel.ResearchLab, ("mProjects[0].mObjId", 8, 2), ("mProjects[0].StaffSA", 36, 4), ("mProjects[255].StaffSA", 8_196, 4), ("mGroups[0].effort", 8_200, 4), ("mGroups[6].highestGroupAvailable", 8_290, 1),
			("mNumProjects", 8_291, 4), ("mLabActive", 8_303, 1), ("lSeccat", 8_326, 4), ("mAssignments[0].pristaf", 8_330, 2), ("mConsultants[1]", 8_412, 2) );
		Offsets( ThemeParkIncThingModel.ParkAnalyser, ("mMapSquaresPerCell", 8, 4), ("mMapSizeY", 24, 4), ("mCells[0].mStartX", 28, 4), ("mCells[1023].mMeasureB[6].mWorkedOut", 62_491, 1), ("mShortRings[0].mCurrentEntry", 62_492, 4),
			("mTotals[0].mVal", 62_536, 4), ("mDayRings[0].mCurrentEntry", 62_566, 4), ("ring50.mTemp", 62_839 + 9, 4), ("mByteRings[21].mTemp", 63_084, 1), ("mMisbehavingKids", 63_085, 4),
			("mLastStrike", 63_105, 8), ("mNumCostumesSoldEver", 63_173, 4) );
	}

	[TestMethod]
	public void TypedRecordsReadWhatTheBodyHolds()
	{
		// a staff member: name, hire time, ring entries and the role link
		var body = Body( ThemeParkIncThingModel.Mechanic, 2, 0, 1, 0, 0, 3 );
		Patch( body, ThemeParkIncThingModel.Mechanic, "mHistory[0].mTemp", 77 );
		Patch( body, ThemeParkIncThingModel.Mechanic, "mHistory[0].mData[1]", 0x01020304 );
		Patch( body, ThemeParkIncThingModel.Mechanic, "mTimeHired", 125_911_584_000_000_000ul );
		Patch( body, ThemeParkIncThingModel.Mechanic, "mNext", 0x1234 );
		Patch( body, ThemeParkIncThingModel.Mechanic, "mAccurateDestX", 40 );
		Patch( body, ThemeParkIncThingModel.Mechanic, "mAccurateDestY", 41 );
		for ( var unit = 0; unit < 33; unit++ )
			Patch( body, ThemeParkIncThingModel.Mechanic, $"mName[{unit}]", (ulong)(unit switch { 0 => 'A', 1 => 'n', 2 => 'n', _ => '\0' }) );
		var person = ThemeParkIncPerson.Read( 12, ThemeParkIncThingModel.Mechanic, new ThemeParkIncBody( ThemeParkIncThingModel.Mechanic, body ) );
		Assert.AreEqual( 12, person.Id );
		Assert.IsTrue( person.IsStaff );
		Assert.AreEqual( "Ann", person.Name );
		Assert.AreEqual( 125_911_584_000_000_000L, person.TimeHired );
		Assert.AreEqual( (ushort)0x1234, person.Next );
		Assert.AreEqual( ((ushort)40, (ushort)41), person.AccurateDestination );
		Assert.IsNull( person.Cash );
		Assert.AreEqual( 6, person.Histories.Count );
		Assert.AreEqual( 2, person.Histories[0].Entries.Count );
		Assert.AreEqual( 77u, person.Histories[0].Current );
		Assert.AreEqual( 0x01020304u, person.Histories[0].Entries[1] );
		Assert.AreEqual( 0xFFFFFFFFu, person.Histories[0].CurrentEntry );
		CollectionAssert.AreEqual( new[] { 2, 0, 1, 0, 0, 3 }, person.Histories.Select( ring => ring.Entries.Count ).ToArray() );

		// a guest: cash and state; no staff members
		var guestBody = Body( ThemeParkIncThingModel.Guest );
		Patch( guestBody, ThemeParkIncThingModel.Guest, "mCash", 1234 );
		Patch( guestBody, ThemeParkIncThingModel.Guest, "mState", 9 );
		var guest = ThemeParkIncPerson.Read( 13, ThemeParkIncThingModel.Guest, new ThemeParkIncBody( ThemeParkIncThingModel.Guest, guestBody ) );
		Assert.AreEqual( 1234u, guest.Cash );
		Assert.AreEqual( 9u, guest.State );
		Assert.IsFalse( guest.IsStaff );
		Assert.IsNull( guest.Name );
		Assert.IsNull( guest.Next );
		Assert.AreEqual( 0, guest.Histories.Count );

		// the weather
		var weatherBody = Body( ThemeParkIncThingModel.Weather );
		Patch( weatherBody, ThemeParkIncThingModel.Weather, "mSpell[1].mMonth", 7 );
		Patch( weatherBody, ThemeParkIncThingModel.Weather, "mSpell[1].mValid", 1 );
		Patch( weatherBody, ThemeParkIncThingModel.Weather, "mLightning", 1 );
		Patch( weatherBody, ThemeParkIncThingModel.Weather, "mWindForce", 55 );
		var weather = ThemeParkIncWeather.Read( 3, new ThemeParkIncBody( ThemeParkIncThingModel.Weather, weatherBody ) );
		Assert.AreEqual( 2, weather.Spells.Count );
		Assert.AreEqual( 7u, weather.Spells[1].Month );
		Assert.IsTrue( weather.Spells[1].Valid );
		Assert.IsTrue( weather.Lightning );
		Assert.AreEqual( 55u, weather.WindForce );
		Assert.AreEqual( 12, weather.LastLightningLocation.Length );

		// the challenge system, staff HQ, advisor controller and research lab
		var challengeBody = Body( ThemeParkIncThingModel.ChallengeSystem );
		Patch( challengeBody, ThemeParkIncThingModel.ChallengeSystem, "mChallenges[4].mCashPrize", 5000 );
		Patch( challengeBody, ThemeParkIncThingModel.ChallengeSystem, "mChallenges[4].mOfferTime", 99 );
		Patch( challengeBody, ThemeParkIncThingModel.ChallengeSystem, "mActualThing[9]", 77 );
		var challenges = ThemeParkIncChallengeSystem.Read( 4, new ThemeParkIncBody( ThemeParkIncThingModel.ChallengeSystem, challengeBody ) );
		Assert.AreEqual( 10, challenges.Challenges.Count );
		Assert.AreEqual( 5000u, challenges.Challenges[4].CashPrize );
		Assert.AreEqual( 99L, challenges.Challenges[4].OfferTime );
		Assert.AreEqual( (ushort)77, challenges.ActualThings[9] );

		var hqBody = Body( ThemeParkIncThingModel.StaffHq );
		Patch( hqBody, ThemeParkIncThingModel.StaffHq, "mBudget[5]", 321 );
		Patch( hqBody, ThemeParkIncThingModel.StaffHq, "mHaveEverTrained", 1 );
		var hq = ThemeParkIncStaffHq.Read( 5, new ThemeParkIncBody( ThemeParkIncThingModel.StaffHq, hqBody ) );
		Assert.AreEqual( 321u, hq.Budgets[5] );
		Assert.IsTrue( hq.HaveEverTrained );
		Assert.AreEqual( 20, hq.StaffSlots.Count );
		Assert.AreEqual( 15, hq.TrainedStaff.Count );

		var advisorBody = Body( ThemeParkIncThingModel.AdvisorController );
		Patch( advisorBody, ThemeParkIncThingModel.AdvisorController, "mQueue[3].mScore", 20 );
		Patch( advisorBody, ThemeParkIncThingModel.AdvisorController, "mCurrent.mMessage", 167 );
		var advisor = ThemeParkIncAdvisorController.Read( 6, new ThemeParkIncBody( ThemeParkIncThingModel.AdvisorController, advisorBody ) );
		Assert.AreEqual( 8, advisor.Queue.Count );
		Assert.AreEqual( 20u, advisor.Queue[3].Score );
		Assert.AreEqual( 167u, advisor.Current.Message );

		var labBody = Body( ThemeParkIncThingModel.ResearchLab );
		Patch( labBody, ThemeParkIncThingModel.ResearchLab, "mProjects[255].mObjId", 1100 );
		Patch( labBody, ThemeParkIncThingModel.ResearchLab, "mGroups[6].effort", 75 );
		Patch( labBody, ThemeParkIncThingModel.ResearchLab, "mConsultants[1]", 9 );
		var lab = ThemeParkIncResearchLab.Read( 7, new ThemeParkIncBody( ThemeParkIncThingModel.ResearchLab, labBody ) );
		Assert.AreEqual( 256, lab.Projects.Count );
		Assert.AreEqual( (ushort)1100, lab.Projects[255].ObjectId );
		Assert.AreEqual( 7, lab.Groups.Count );
		Assert.AreEqual( 75u, lab.Groups[6].Effort );
		Assert.AreEqual( (ushort)9, lab.Consultants[1] );

		// the analyser, with a ring of each element width
		var analyserBody = Body( ThemeParkIncThingModel.ParkAnalyser, 2, 0, 0, 0 );
		Patch( analyserBody, ThemeParkIncThingModel.ParkAnalyser, "mGridSizeX", 32 );
		Patch( analyserBody, ThemeParkIncThingModel.ParkAnalyser, "mCells[1000].mStartX", 124 );
		Patch( analyserBody, ThemeParkIncThingModel.ParkAnalyser, "mCells[1000].mMeasureB[6].mVal", 5 );
		Patch( analyserBody, ThemeParkIncThingModel.ParkAnalyser, "mShortRings[0].mData[1]", 0xBEEF );
		var analyser = ThemeParkIncParkAnalyser.Read( 8, new ThemeParkIncBody( ThemeParkIncThingModel.ParkAnalyser, analyserBody ) );
		Assert.AreEqual( 1024, analyser.Cells.Count );
		Assert.AreEqual( 32u, analyser.GridSizeX );
		Assert.AreEqual( 124u, analyser.Cells[1000].StartX );
		Assert.AreEqual( 5u, analyser.Cells[1000].MeasureB[6].Value );
		Assert.AreEqual( 2, analyser.ShortRings[0].Entries.Count );
		Assert.AreEqual( 0xBEEFu, analyser.ShortRings[0].Entries[1] );
		Assert.AreEqual( 22, analyser.ByteRings.Count );
		Assert.AreEqual( 21, analyser.DayRings.Count );
	}

	[TestMethod]
	public void RingCountsAboveCapacityTruncationAndWrongSizesAreRejected()
	{
		foreach ( var model in Models )
		{
			var layout = ThemeParkIncBodyLayout.For( model )!;
			var body = Body( model );
			// every prefix of the body is too short; so is a body that starts past the data
			foreach ( var length in new[] { 0, 1, 7, 8, body.Length / 2, body.Length - 1 } )
				Assert.ThrowsException<InvalidDataException>( () => layout.Measure( body.AsSpan( 0, length ).ToArray(), 0 ), $"{model} {length}" );
			Assert.ThrowsException<InvalidDataException>( () => layout.Measure( body, -1 ), model.ToString() );
			Assert.ThrowsException<InvalidDataException>( () => layout.Measure( body, body.Length + 1 ), model.ToString() );
			// a body of the wrong length is not a body of this layout
			Assert.ThrowsException<InvalidDataException>( () => new ThemeParkIncBody( model, body.Concat( new byte[] { 0 } ).ToArray() ), model.ToString() );
			Assert.ThrowsException<InvalidDataException>( () => new ThemeParkIncBody( model, body.AsSpan( 1 ).ToArray() ), model.ToString() );
			var typed = new ThemeParkIncBody( model, body );
			foreach ( var field in typed.Fields.Where( item => item.Path.EndsWith( "mNumEntries", StringComparison.Ordinal ) ).Take( 3 ) )
			{
				// the first ring entry past the capacity: read the capacity from the layout by trying 255 (above every capacity) and then the largest valid count
				var damaged = (byte[])body.Clone();
				damaged[field.Offset] = 255;
				Assert.ThrowsException<InvalidDataException>( () => layout.Measure( damaged, 0 ), $"{model} {field.Path}" );
			}
		}
		// the capacity is a hard limit: one above it fails, the capacity itself reads
		var mechanic = ThemeParkIncBodyLayout.For( ThemeParkIncThingModel.Mechanic )!;
		var full = Body( ThemeParkIncThingModel.Mechanic, 30, 30, 30, 30, 30, 30 );
		Assert.AreEqual( mechanic.MaximumBytes, mechanic.Measure( full, 0 ) );
		var over = Body( ThemeParkIncThingModel.Mechanic, 31 );
		Assert.ThrowsException<InvalidDataException>( () => mechanic.Measure( over, 0 ) );
		var overBytes = Body( ThemeParkIncThingModel.ParkAnalyser, Enumerable.Repeat( 0, 4 + 21 + 2 ).Concat( new[] { 52 } ).ToArray() );
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncBodyLayout.For( ThemeParkIncThingModel.ParkAnalyser )!.Measure( overBytes, 0 ) );
		Assert.ThrowsException<ArgumentException>( () => new ThemeParkIncBody( ThemeParkIncThingModel.Bank, new byte[292] ) );
		Assert.ThrowsException<KeyNotFoundException>( () => new ThemeParkIncBody( ThemeParkIncThingModel.Weather, Body( ThemeParkIncThingModel.Weather ) ).Field( "mNothing" ) );
		Assert.ThrowsException<InvalidDataException>( () => new ThemeParkIncBody( ThemeParkIncThingModel.Weather, Body( ThemeParkIncThingModel.Weather ) ).U16( "mQuality" ) );
	}

	// ------------------------------------------------------------------ the thing chain (the prebuilt with synthetic things added)

	private static byte[]? prebuilt;

	private static byte[] Prebuilt()
	{
		if ( prebuilt != null )
			return (byte[])prebuilt.Clone();
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH for the Theme Park Inc prebuilt-save fixture." );
		var dataPath = Directory.Exists( Path.Combine( path!, "Data" ) ) ? Path.Combine( path!, "Data" ) : path!;
		using var reader = new SaveReader( Path.Combine( dataPath, "PreBuilt", "prebuilt.TPWS" ) );
		prebuilt = reader.ReadFile();
		return (byte[])prebuilt.Clone();
	}

	private static ThemeParkIncSaveWorld World( byte[] payload ) => ThemeParkIncSaveWorld.Parse( payload, SavePayloadLayout.ParseThemeParkInc( payload ), 128, 128 );

	/// <summary>The prebuilt with <paramref name="things"/> appended to its used-thing list (before ENOD), the old last thing linked to the first new one.</summary>
	private static byte[] WithThings( byte[] payload, params (int Id, ThemeParkIncThingModel Model, byte[] Body)[] things )
	{
		var world = World( payload );
		var last = world.Things[^1];
		var added = new List<byte>();
		for ( var index = 0; index < things.Length; index++ )
		{
			var (id, model, body) = things[index];
			added.AddRange( BitConverter.GetBytes( index + 1 < things.Length ? (uint)things[index + 1].Id : 0u ) );
			added.AddRange( BitConverter.GetBytes( (uint)model ) );
			added.AddRange( "LDMT"u8.ToArray() );
			added.AddRange( Encoding.ASCII.GetBytes( ThemeParkIncSaveWorld.Tags[model] ) );
			added.AddRange( body );
		}
		var result = new byte[payload.Length + added.Count];
		Array.Copy( payload, result, last.End );
		added.CopyTo( result, last.End );
		Array.Copy( payload, last.End, result, last.End + added.Count, payload.Length - last.End );
		BitConverter.GetBytes( (uint)things[0].Id ).CopyTo( result, last.Offset );
		return result;
	}

	[TestMethod]
	public void GuestsStaffAndGardenersAreSizedByTheirLayoutsInTheThingChain()
	{
		var original = Prebuilt();
		var baseline = World( original );
		var things = new (int Id, ThemeParkIncThingModel Model, byte[] Body)[]
		{
			(100, ThemeParkIncThingModel.Guest, Body( ThemeParkIncThingModel.Guest )),
			(101, ThemeParkIncThingModel.Mechanic, Body( ThemeParkIncThingModel.Mechanic, 3, 0, 30, 1, 0, 7 )),
			(102, ThemeParkIncThingModel.Guest, Body( ThemeParkIncThingModel.Guest )),
			(103, ThemeParkIncThingModel.Gardener, Body( ThemeParkIncThingModel.Gardener, 30, 0, 0, 0, 0, 29 )),
			(104, ThemeParkIncThingModel.Researcher, Body( ThemeParkIncThingModel.Researcher, 1, 2, 3, 4, 5, 6 )),
			(105, ThemeParkIncThingModel.Handyman, Body( ThemeParkIncThingModel.Handyman )),
			(106, ThemeParkIncThingModel.Entertainer, Body( ThemeParkIncThingModel.Entertainer, 5, 4, 3, 2, 1, 0 )),
			(107, ThemeParkIncThingModel.Guard, Body( ThemeParkIncThingModel.Guard, 30, 30, 30, 30, 30, 30 )),
		};
		var payload = WithThings( original, things );
		var world = World( payload );
		Assert.AreEqual( baseline.Things.Count + things.Length, world.Things.Count );
		Assert.IsTrue( ThemeParkIncSaveWorld.TryMeasure( payload, SavePayloadLayout.ParseThemeParkInc( payload )[SaveBlock.World].Offset, out _ ) );
		foreach (var (id, model, body) in things )
		{
			var thing = world.Find( id )!.Value;
			Assert.AreEqual( model, thing.Model, id.ToString() );
			Assert.AreEqual( body.Length, thing.BodyLength, id.ToString() );
			Assert.AreEqual( ThemeParkIncWidthSource.Reader, thing.Width, id.ToString() );
			CollectionAssert.AreEqual( body, world.BodyOf( thing ).ToArray(), id.ToString() );
		}
		Assert.AreEqual( things.Length, world.People.Count );
		CollectionAssert.AreEqual( things.Select( item => item.Id ).ToArray(), world.People.Select( person => person.Id ).ToArray() );
		Assert.AreEqual( 2, world.People.Count( person => person.Model == ThemeParkIncThingModel.Guest ) );
		Assert.AreEqual( 30, world.People.Single( person => person.Id == 103 ).Histories[0].Entries.Count );
		Assert.AreEqual( 29, world.People.Single( person => person.Id == 103 ).Histories[5].Entries.Count );
		// the prebuilt's own things are unchanged
		CollectionAssert.AreEqual( baseline.Things.Select( item => (item.Id, item.BodyLength) ).ToArray(), world.Things.Take( baseline.Things.Count ).Select( item => (item.Id, item.BodyLength) ).ToArray() );
	}

	/// <summary>The offset of a 16-byte window of plain data (no ring counter inside) in the middle of a body.</summary>
	private static int PlainWindow( ThemeParkIncThingModel model, byte[] body )
	{
		var typed = new ThemeParkIncBody( model, body );
		var counters = typed.Fields.Where( field => field.Path.EndsWith( "mNumEntries", StringComparison.Ordinal ) ).ToList();
		return typed.Fields.Where( field => field.Offset >= 16 && field.Offset + 16 <= body.Length ).First( field =>
			!counters.Any( counter => counter.Offset < field.Offset + 16 && counter.Offset + 4 > field.Offset ) ).Offset;
	}

	private static void Plant( byte[] bytes, int at )
	{
		BitConverter.GetBytes( 0x8000u ).CopyTo( bytes, at );
		BitConverter.GetBytes( (uint)ThemeParkIncThingModel.Gardener ).CopyTo( bytes, at + 4 );
		"LDMT"u8.CopyTo( bytes.AsSpan( at + 8 ) );
		"DRAG"u8.CopyTo( bytes.AsSpan( at + 12 ) );
	}

	/// <summary>
	/// A complete thing header planted inside the body of every traced model is data: the six formerly opaque bodies of the prebuilt (planted in place) and the guest, staff and
	/// gardener bodies (with entries in their rings, planted in a thing appended in the middle and at the end of the list) keep the thing count, order, ids and widths.
	/// </summary>
	[TestMethod]
	public void HeaderLookalikeInEveryTracedBodyIsData()
	{
		var original = Prebuilt();
		var baseline = World( original );
		var inPlace = 0;
		foreach ( var model in Models.Where( model => baseline.OfModel( model ).Any() ) )
		{
			var thing = baseline.OfModel( model ).Single();
			var payload = (byte[])original.Clone();
			var body = baseline.BodyOf( thing ).ToArray();
			Plant( payload, thing.BodyOffset + PlainWindow( model, body ) );
			var parsed = World( payload );
			CollectionAssert.AreEqual( baseline.Things.Select( item => (item.Id, item.Model, item.BodyLength) ).ToArray(), parsed.Things.Select( item => (item.Id, item.Model, item.BodyLength) ).ToArray(), model.ToString() );
			Assert.IsFalse( parsed.OfModel( ThemeParkIncThingModel.Gardener ).Any(), model.ToString() );
			inPlace++;
		}
		Assert.AreEqual( 6, inPlace );

		foreach ( var model in Models.Where( model => !baseline.OfModel( model ).Any() ) )
		{
			var body = Body( model, 2, 3, 1, 0, 4, 2 );
			var planted = (byte[])body.Clone();
			Plant( planted, PlainWindow( model, body ) );
			Assert.AreEqual( body.Length, new ThemeParkIncBody( model, planted ).Length, model.ToString() );
			var follower = (200, ThemeParkIncThingModel.Entertainer, Body( ThemeParkIncThingModel.Entertainer, 1 ));
			foreach ( var chain in new[] { new[] { (150, model, planted), follower }, new[] { (150, model, planted) } } )
			{
				var expected = World( WithThings( original, chain.Select( item => (item.Item1, item.Item2, item.Item1 == 150 ? body : item.Item3) ).ToArray() ) );
				var parsed = World( WithThings( original, chain ) );
				Assert.AreEqual( expected.Things.Count, parsed.Things.Count, model.ToString() );
				CollectionAssert.AreEqual( expected.Things.Select( item => (item.Id, item.Model, item.BodyLength) ).ToArray(), parsed.Things.Select( item => (item.Id, item.Model, item.BodyLength) ).ToArray(), model.ToString() );
				Assert.IsFalse( parsed.OfModel( ThemeParkIncThingModel.Gardener ).Any( item => item.Id == 0x8000 ), model.ToString() );
			}
		}
	}

	[TestMethod]
	public void DamagedTracedBodiesAreRefusedInTheThingChain()
	{
		var original = Prebuilt();
		var mechanic = Body( ThemeParkIncThingModel.Mechanic, 1, 1, 1, 1, 1, 1 );
		var good = WithThings( original, (100, ThemeParkIncThingModel.Mechanic, mechanic) );
		Assert.AreEqual( 1, World( good ).People.Count );

		// a ring count above its capacity
		var typed = new ThemeParkIncBody( ThemeParkIncThingModel.Mechanic, mechanic );
		var over = (byte[])mechanic.Clone();
		over[typed.Field( "mHistory[2].mNumEntries" ).Offset] = 31;
		Assert.ThrowsException<InvalidDataException>( () => World( WithThings( original, (100, ThemeParkIncThingModel.Mechanic, over) ) ) );
		Assert.IsFalse( ThemeParkIncSaveWorld.TryMeasure( WithThings( original, (100, ThemeParkIncThingModel.Mechanic, over) ), SavePayloadLayout.ParseThemeParkInc( original )[SaveBlock.World].Offset, out _ ) );

		// a body one byte short: the list's ENOD is then read as part of it and the next header is missing
		var shortBody = mechanic.Take( mechanic.Length - 1 ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => World( WithThings( original, (100, ThemeParkIncThingModel.Mechanic, shortBody) ) ) );
		// one byte long
		Assert.ThrowsException<InvalidDataException>( () => World( WithThings( original, (100, ThemeParkIncThingModel.Mechanic, mechanic.Concat( new byte[] { 0 } ).ToArray()) ) ) );
		// two weather things are not a World
		var twice = WithThings( original, (100, ThemeParkIncThingModel.Weather, Body( ThemeParkIncThingModel.Weather )) );
		Assert.ThrowsException<InvalidDataException>( () => World( twice ) );
		// the traced body must be followed by a thing header at its width
		var shifted = (byte[])good.Clone();
		var thing = World( good ).Find( 100 )!.Value;
		shifted[thing.End - 1] ^= 0xFF;
		shifted[thing.End + 2] ^= 0xFF;
		Assert.ThrowsException<InvalidDataException>( () => World( shifted ) );
	}

	// ------------------------------------------------------------------ the prebuilt

	[TestMethod]
	public void PrebuiltBodiesAreTracedAtTheirOldWidthsAndTyped()
	{
		var payload = Prebuilt();
		var world = World( payload );
		foreach ( var (model, width) in new[] { (ThemeParkIncThingModel.StaffHq, 411), (ThemeParkIncThingModel.AdvisorController, 233), (ThemeParkIncThingModel.ParkAnalyser, 76_617),
			(ThemeParkIncThingModel.ResearchLab, 8_414), (ThemeParkIncThingModel.Weather, 104), (ThemeParkIncThingModel.ChallengeSystem, 1_287) } )
		{
			var thing = world.OfModel( model ).Single();
			Assert.AreEqual( ThemeParkIncWidthSource.Reader, thing.Width, model.ToString() );
			Assert.AreEqual( width, thing.BodyLength, model.ToString() );
			Assert.AreEqual( width, ThemeParkIncBodyLayout.For( model )!.Measure( payload, thing.BodyOffset ), model.ToString() );
			Assert.AreEqual( width, new ThemeParkIncBody( model, world.BodyOf( thing ) ).Length, model.ToString() );
		}
		Assert.AreEqual( 0, world.People.Count );

		// the analyser: a 32 x 32 grid of 4-square cells over the 128 x 128 map, all rings full
		var analyser = world.ParkAnalyser!;
		Assert.AreEqual( 4u, analyser.MapSquaresPerCell );
		Assert.AreEqual( (32u, 32u, 128u, 128u), (analyser.GridSizeX, analyser.GridSizeY, analyser.MapSizeX, analyser.MapSizeY) );
		Assert.AreEqual( 1024, analyser.Cells.Count );
		for ( var cell = 0; cell < 1024; cell++ )
			Assert.AreEqual( ((uint)(cell % 32 * 4), (uint)(cell / 32 * 4), 4u, 4u), (analyser.Cells[cell].StartX, analyser.Cells[cell].StartY, analyser.Cells[cell].SizeX, analyser.Cells[cell].SizeY), $"cell {cell}" );
		Assert.IsTrue( analyser.ShortRings.All( ring => ring.Entries.Count == 3 ) );
		Assert.IsTrue( analyser.DayRings.All( ring => ring.Entries.Count == 144 ) );
		Assert.AreEqual( 50, analyser.Ring50.Entries.Count );
		Assert.AreEqual( 5, analyser.Ring5.Entries.Count );
		Assert.IsTrue( analyser.ByteRings.All( ring => ring.Entries.Count == 50 ) );
		Assert.AreEqual( 0u, analyser.LifetimeVisitors );

		// the challenge system: the first challenge is a timed target with a prize, the system is on
		var challenges = world.ChallengeSystem!;
		Assert.IsTrue( challenges.SystemSwitchedOn );
		Assert.AreEqual( 10, challenges.Challenges.Count );
		Assert.AreEqual( (2u, 60u, 30u, 5000u), (challenges.Challenges[0].Type, challenges.Challenges[0].TargetTime, challenges.Challenges[0].TargetVal, challenges.Challenges[0].CashPrize) );
		Assert.AreEqual( 9000u, challenges.Challenges[0].MaxDaysTilTrigger );

		// the research lab: the first project is an object id, the last slot is uninitialized memory (0xCD)
		var lab = world.ResearchLab!;
		Assert.AreEqual( (ushort)1100, lab.Projects[0].ObjectId );
		Assert.AreEqual( 0xCDCDCDCDu, lab.Projects[255].StaffSA );
		Assert.AreEqual( 50u, lab.NumProjects );

		// the advisor controller names the thing, the queue and the current message
		var advisor = world.AdvisorController!;
		Assert.AreEqual( 8, advisor.Queue.Count );
		Assert.AreEqual( 167u, advisor.Current.Message );
		Assert.IsTrue( advisor.RequestedLastTutorial );

		// the staff HQ and the weather
		Assert.AreEqual( 0u, world.StaffHq!.ForceStrike );
		Assert.AreEqual( 6, world.StaffHq.Budgets.Count );
		Assert.AreEqual( 2, world.Weather!.Spells.Count );
		Assert.AreEqual( (ushort)128, world.Weather.Base.X );

		// each typed thing is the one the World variables name
		Assert.AreEqual( (int)world.Variables.ParkAnalyser, analyser.Id );
		Assert.AreEqual( (int)world.Variables.ResearchLab, lab.Id );
		Assert.AreEqual( (int)world.Variables.StaffHq, world.StaffHq.Id );
		Assert.AreEqual( (int)world.Variables.Weather, world.Weather.Id );
	}
}
