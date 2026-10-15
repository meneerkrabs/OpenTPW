using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>The research lab's fluid animation (tp.exe 0x4a95b0 / 0x4a9fc0) and the rate its refresh passes (0x503980).</summary>
[TestClass]
public class ResearchFluidTests
{
	private static List<int> Run( ResearchFluid fluid, int ticks )
	{
		var frames = new List<int>();
		for ( var tick = 0; tick < ticks; tick++ )
		{
			fluid.Tick();
			frames.Add( fluid.Frame );
		}
		return frames;
	}

	[TestMethod]
	public void AnIdlePanelShowsTheEmptyFrameAndDoesNotStep()
	{
		var fluid = new ResearchFluid();
		Assert.AreEqual( (0x30, 0, 0x30), (fluid.Position, fluid.State, fluid.Frame) );
		CollectionAssert.AreEqual( Enumerable.Repeat( 0x30, 5 ).ToArray(), Run( fluid, 5 ).ToArray() );
		fluid.Apply( 0 );
		Assert.AreEqual( (0, 0x30), (fluid.State, fluid.Frame) );
	}

	[TestMethod]
	public void ARateFillsFromEmptyThenLoopsTheBubblingFrames()
	{
		// rate 30: speed = 50 × 0x10000 / 100 = 0x8000, so one frame per tick (2 × speed)
		var fluid = new ResearchFluid();
		fluid.Apply( 30 );
		Assert.AreEqual( (3, 0x30, 0x300000, 0x8000), (fluid.State, fluid.Frame, fluid.Position, fluid.Speed) );
		var frames = Run( fluid, 16 + 32 + 2 );
		CollectionAssert.AreEqual( Enumerable.Range( 49, 15 ).Append( 0 ).ToArray(), frames.Take( 16 ).ToArray(), "frames 49..63, then the loop" );
		Assert.AreEqual( 1, fluid.State );
		CollectionAssert.AreEqual( Enumerable.Range( 1, 31 ).Append( 0 ).ToArray(), frames.Skip( 16 ).Take( 32 ).ToArray(), "frames 1..31 wrap to 0" );
		// a new non-zero rate only changes the speed of a running fluid
		fluid.Apply( 80 );
		Assert.AreEqual( (1, 0x10000), (fluid.State, fluid.Speed) );
	}

	[TestMethod]
	public void AZeroRateDrainsARunningFluidAndStopsAtTheEmptyFrame()
	{
		var fluid = new ResearchFluid();
		fluid.Apply( 30 );
		Run( fluid, 20 );
		fluid.Apply( 0 );
		Assert.AreEqual( (2, 0x20, 0x200000), (fluid.State, fluid.Frame, fluid.Position) );
		var frames = Run( fluid, 18 );
		CollectionAssert.AreEqual( Enumerable.Range( 33, 15 ).Append( 0x30 ).Append( 0x30 ).Append( 0x30 ).ToArray(), frames.ToArray() );
		Assert.AreEqual( (0, 0x300000), (fluid.State, fluid.Position) );
		// a draining fluid that gets a rate again starts the fill
		fluid.Apply( 30 );
		Run( fluid, 3 );
		fluid.Apply( 0 );
		fluid.Tick();
		fluid.Apply( 10 );
		Assert.AreEqual( (3, 0x30, 0x300000, 30 * 0x10000 / 100), (fluid.State, fluid.Frame, fluid.Position, fluid.Speed) );
	}

	[TestMethod]
	public void TheRateIsTheCategorysShareTimesTheStaffingPercentage()
	{
		var effort = new[] { 100, 15, 30, 15, 10 };
		// share(ride) = (100 × 100 / 170) × 85 / 100 = 58 × 85 / 100 = 49; staffing = 11 × 100 / (10 × 6) = 18; rate = 49 × 18 / 100 = 8
		Assert.AreEqual( 8, ResearchFluid.Rate( effort, 0, 85, true, 11, 10, 6 ) );
		Assert.AreEqual( 0, ResearchFluid.Rate( effort, 0, 85, false, 11, 10, 6 ), "no current project" );
		Assert.AreEqual( 0, ResearchFluid.Rate( new int[5], 0, 85, true, 11, 10, 6 ), "no effort at all" );
		Assert.AreEqual( 0, ResearchFluid.Rate( effort, 0, 85, true, 11, 0, 6 ), "no researcher cap" );
		Assert.AreEqual( 0, ResearchFluid.Rate( effort, 0, 85, true, 0, 10, 6 ), "no researchers" );
		Assert.AreEqual( 85, ResearchFluid.Rate( new[] { 100, 0, 0, 0, 0 }, 0, 85, true, 60, 10, 6 ) );
	}

	[TestMethod]
	public void TheHudRateCountsEveryHiredResearchersGrade()
	{
		var park = EconomyTestData.Park();
		Assert.AreEqual( 0, ParkHud.ResearchFluidRate( park, ResearchCategory.Ride ) );
		var member = EconomyTestData.HireBest( park, StaffType.Researcher );
		var ability = park.Settings.ResearchAbility[member.Grade];
		var share = (uint)(park.Research.Effort[0] * 100) / (uint)park.Research.Effort.Sum() * (uint)park.Research.Workload / 100;
		var rate = ParkHud.ResearchFluidRate( park, ResearchCategory.Ride );
		Assert.IsTrue( rate > 0 );
		Assert.AreEqual( (int)(share * ((uint)(ability * 100) / (10u * 6u)) / 100), rate );
	}
}
