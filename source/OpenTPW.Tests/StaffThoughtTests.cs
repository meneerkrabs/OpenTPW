using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The staff's thoughts (tp.exe <c>0x00506A40</c>, <c>0x005056E0</c>, <c>0x00506F30</c>): 19 for an unhappy member and 18 for a very happy one (one gate pass
/// in sixteen), 20 for a tired one, 21 while walking out to strike, 22 when no cell of the patrol area can be reached; and their bubbles.
/// </summary>
[TestClass]
public class StaffThoughtTests
{
	private sealed class Fixture
	{
		public readonly GuestPathGrid Grid = new( 20, 20 );
		public readonly ParkEconomy Park = EconomyTestData.Park();
		public readonly StaffWorldSimulation World;

		public Fixture()
		{
			for ( var x = 0; x < 16; x++ )
				Grid.SetPath( x, 5, true );
			Park.OpenPark();
			World = new StaffWorldSimulation( Grid, () => Park.Staff.Members, _ => null, (0, 5) );
			Park.StaffWorld = World;
		}

		public StaffMember Hire( StaffType type = StaffType.Researcher )
		{
			var member = EconomyTestData.HireBest( Park, type );
			World.Synchronize();
			return member;
		}

		public void Turns( int turns, System.Action? each = null )
		{
			for ( var turn = 0; turn < turns; turn++ )
			{
				each?.Invoke();
				EconomyTestData.AdvanceToTurn( Park, Park.Turn + 1 );
			}
		}
	}

	[TestMethod]
	public void AnUnhappyMemberThinksNineteenAndAHappyOneEighteenNowAndThen()
	{
		var f = new Fixture();
		var member = f.Hire();
		f.Turns( 2 );
		var seen = new System.Collections.Generic.HashSet<int>();
		f.Turns( 200, () =>
		{
			member.Energy = StaffVitals.Maximum;
			member.Happiness = 5;
			seen.Add( member.Thought );
		} );
		Assert.IsTrue( seen.Contains( StaffThoughts.Unhappy ) );
		Assert.IsFalse( seen.Contains( StaffThoughts.VeryHappy ) );
		member.Thought = 0;
		var happy = 0;
		f.Turns( 4000, () =>
		{
			member.Energy = StaffVitals.Maximum;
			member.Happiness = StaffVitals.Maximum;
			if ( member.Thought == StaffThoughts.VeryHappy )
				happy++;
		} );
		Assert.IsTrue( happy > 0, "one gate pass in sixteen" );
	}

	[TestMethod]
	public void ATiredMemberThinksTwentyAndTheThoughtIsSaved()
	{
		var f = new Fixture();
		var member = f.Hire();
		f.Turns( 2 * GuestThoughts.CooldownTurns );
		member.Energy = 0;
		long thought = -1;
		f.Turns( 200, () =>
		{
			if ( thought < 0 && member.Thought == StaffThoughts.Tired )
				thought = f.Park.Turn;
			member.Energy = 0;
		} );
		Assert.IsTrue( thought >= 0, "no rest area: it thinks 20 on every gate pass" );
		Assert.AreEqual( StaffThoughts.Tired, member.BubbleThought, "priority 0: the bubble starts again with every pass" );
		var json = ParkSaveFile.Serialize( f.Park );
		Assert.IsTrue( json.Contains( "\"BubbleThought\": 20" ) || json.Contains( "\"BubbleThought\":20" ), "the bubble is in the save" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), f.Park.Settings, f.Park.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ), "the thought and its bubble are saved" );
	}

	[TestMethod]
	public void AThoughtIsSavedForAnIdleMemberWithoutAWalk()
	{
		var park = EconomyTestData.Park();
		var member = EconomyTestData.HireBest( park, StaffType.Handyman );
		(member.Thought, member.BubbleThought, member.BubbleTurn) = (StaffThoughts.Tired, StaffThoughts.Tired, 7);
		Assert.AreEqual( (StaffJob.Idle, (GuestNavigation?)null), (member.Job, member.Navigation), "as just after a drop" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), park.Settings, park.Catalog );
		var copy = restored.Staff.Members.Single( item => item.Id == member.Id );
		Assert.AreEqual( (StaffThoughts.Tired, StaffThoughts.Tired, 7L), (copy.Thought, copy.BubbleThought, copy.BubbleTurn ) );
	}
}
