using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The screen-space particles (tp.exe's draw list 0x51F01A and quad builder 0x57C620) and the advisor's screen effects: the timer's
/// Key effect (0x5999AD) and the effect groups the accessory bits fill (0x598EAA, 0x5ED5A0/0x5ED660/0x5ED700/0x5ED770). GPU free.
/// </summary>
[TestClass]
public class AdvisorScreenEffectTests
{
	[TestInitialize]
	public void Init() => Log = new();

	private sealed class RecordingPool : IAdvisorParticlePool
	{
		private int next = 1;
		public readonly List<(int Effect, (int X, int Y, int Z) Native, int Handle)> Created = new();
		public readonly Dictionary<int, (int X, int Y, int Z)> Positions = new();
		public readonly List<int> Killed = new();
		public bool Refuse { get; set; }

		public object? Create( int effectId, (int X, int Y, int Z) native )
		{
			if ( Refuse )
				return null;
			var handle = next++;
			Created.Add( (effectId, native, handle) );
			Positions[handle] = native;
			return handle;
		}

		public void Move( object handle, (int X, int Y, int Z) native ) => Positions[(int)handle] = native;
		public void Kill( object handle ) => Killed.Add( (int)handle );
	}

	/// <summary>Native positions within one unit: the single-precision sums land on either side of a whole number.</summary>
	private static void Near( (int X, int Y, int Z) expected, (int X, int Y, int Z) actual )
	{
		Assert.AreEqual( expected.X, actual.X, 1, $"x of {actual}" );
		Assert.AreEqual( expected.Y, actual.Y, $"y of {actual}" );
		Assert.AreEqual( expected.Z, actual.Z, 1, $"z of {actual}" );
	}

	[TestMethod]
	public void TheTimerStartsTheKeyEffectAtTheTopRight()
	{
		// [BIN:TPW-EXE:0x005999AD] 0x521E60(0x57, 0x15F90, 0, 0x1B58)
		Assert.AreEqual( 87, AdvisorSequences.TimerEffect );
		Assert.AreEqual( (90000, 0, 7000), AdvisorSequences.TimerEffectPosition );
	}

	// ---- effect groups ----

	[TestMethod]
	public void AOneTicketClipJumpsOneGoldenTicketBesideTheAdvisorAndCirclesItOnceOverTheClip()
	{
		// [BIN:TPW-EXE:0x598FDF] 0x20000: 0x5ED5A0(0, 1, 0x2C), then jump to (0.34, 0.62), radii (0.01, 0.2), 1 turn, over the clip's length
		var pool = new RecordingPool();
		var groups = new AdvisorEffectGroups( pool );
		groups.ApplyAccessoryBits( AdvisorEffectGroups.OneTicketBit, active: true, clipMilliseconds: 1000, now: 5000 );
		Assert.AreEqual( 1, groups.Count( 0 ) );
		Assert.AreEqual( (AdvisorEffectGroups.GoldenTicketEffect, (0, 0, 0)), (pool.Created.Single().Effect, pool.Created.Single().Native) );
		groups.Update( 5000 );
		// angle 0: (cx + rx sin 0, cy - ry cos 0) = (0.34, 0.62 - 0.2 * 255/256): the 512-entry table truncates sin(pi/2) * 256 to 255
		Near( (67000, 0, 53279), pool.Positions[1] );
		groups.Update( 5250 );
		// a quarter of the clip: a quarter turn, angle 128: (0.34 + 0.01 * 255/256, 0.62)
		Near( (67498, 0, 60750), pool.Positions[1] );
		groups.ApplyAccessoryBits( AdvisorEffectGroups.OneTicketBit, active: true, clipMilliseconds: 1000, now: 6000 );
		Assert.AreEqual( 1, pool.Created.Count, "the group already holds one: 0x5ED5A0 returns 0 and nothing is moved" );
	}

	[TestMethod]
	public void TwoTicketsCircleOppositeEachOtherWhileTheCentreMovesFromTheLastTarget()
	{
		// [BIN:TPW-EXE:0x598F1C] 0x40000: 2 x 0x31 from the previous target to (0, 0) over the clip, radii from the previous ones to (0.6, 0.4), 2 turns
		var pool = new RecordingPool();
		var groups = new AdvisorEffectGroups( pool );
		groups.ApplyAccessoryBits( AdvisorEffectGroups.OneTicketBit, true, 1000, 0 );
		groups.ApplyAccessoryBits( AdvisorEffectGroups.TwoTicketsBit, true, 2000, 1000 );
		Assert.AreEqual( 2, groups.Count( 0 ) );
		Assert.AreEqual( AdvisorEffectGroups.GoldenTicket2Effect, pool.Created[1].Effect, "the existing emitter keeps its effect, the new one is 0x31" );
		groups.Update( 3000 );
		// t = 1: centre (0, 0), radii (0.6, 0.4); 2 turns: angles 0 and 256 (cos 255/256 and -255/256)
		Near( (50000, 0, 22558), pool.Positions[1] );
		Near( (50000, 0, 52441), pool.Positions[2] );
		groups.Update( 4000 );
		// [BIN:TPW-EXE:0x5ED7C6] the centre uses t unclamped (only the radii clamp it): at t = 1.5 it has run on to (-0.17, -0.31)
		Near( (41500, 0, 10933), pool.Positions[1] );
	}

	[TestMethod]
	public void TheEffectBitsActOnlyAloneAndOnlyTheOffBitActsWithoutAnActiveResponse()
	{
		var pool = new RecordingPool();
		var groups = new AdvisorEffectGroups( pool );
		groups.ApplyAccessoryBits( AdvisorEffectGroups.OneTicketBit | AdvisorEffectGroups.TwoTicketsBit, true, 1000, 0 );
		Assert.AreEqual( 0, pool.Created.Count, "0x60000 matches no case" );
		groups.ApplyAccessoryBits( AdvisorEffectGroups.ThreeTicketsBit, false, 1000, 0 );
		Assert.AreEqual( 0, pool.Created.Count, "without the active flag 0xF7966C only 0x200000 acts" );
		groups.ApplyAccessoryBits( AdvisorEffectGroups.ThreeTicketsBit | AdvisorAccessories.SuppressCostumeBit, true, 1000, 0 );
		Assert.AreEqual( 3, groups.Count( 0 ), "the costume bit is outside the effect mask" );
		groups.ApplyAccessoryBits( AdvisorEffectGroups.EffectsOffBit, false, 1000, 0 );
		Assert.AreEqual( 0, groups.Count( 0 ) );
		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, pool.Killed );
	}

	[TestMethod]
	public void TheTicketsFlyOffFreshAndARefusedStartIsKeptAsAnEmptySlot()
	{
		// [BIN:TPW-EXE:0x598F8D] 0x100000 empties the group, starts 2 x 0x2C and moves them towards (0.8, -0.8) with the radii shrinking to 0
		var pool = new RecordingPool();
		var groups = new AdvisorEffectGroups( pool );
		groups.ApplyAccessoryBits( AdvisorEffectGroups.ThreeTicketsBit, true, 1000, 0 );
		groups.ApplyAccessoryBits( AdvisorEffectGroups.TicketsAwayBit, true, 1000, 1000 );
		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, pool.Killed );
		CollectionAssert.AreEqual( new[] { 0x31, 0x31, 0x31, 0x2C, 0x2C }, pool.Created.Select( created => created.Effect ).ToArray() );
		groups.Update( 2000 );
		Near( (90000, 0, 7500), pool.Positions[4] );
		// [BIN:TPW-EXE:0x5ED5D9] a refused start keeps its slot; [BIN:TPW-EXE:0x5ED61F] only nonzero handles are ended
		pool.Refuse = true;
		Assert.AreEqual( -2, groups.SetCount( 0, 0, 0 ) );
		Assert.AreEqual( 3, groups.SetCount( 0, 3, 0x2C ) );
		Assert.AreEqual( 0, groups.SetCount( 0, 0, 0 ), "nothing to end" );
	}

	[TestMethod]
	public void FtolTruncatesAndGivesTheIntegerIndefiniteForNaN()
	{
		Assert.AreEqual( 2, AdvisorEffectGroups.Ftol( 2.9f ) );
		Assert.AreEqual( -2, AdvisorEffectGroups.Ftol( -2.9f ) );
		Assert.AreEqual( int.MinValue, AdvisorEffectGroups.Ftol( float.NaN ) );
		Assert.AreEqual( int.MinValue, AdvisorEffectGroups.Ftol( 3e9f ) );
	}
}
