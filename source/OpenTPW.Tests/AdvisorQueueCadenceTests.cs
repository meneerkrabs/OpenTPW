using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// ADVISOR-017: the advisor's queue is served at the end of tp.exe's advisor update 0x59A550, which the thing loop calls once per park turn (0x516380 -> 0x50B360), so the
/// queue steps once for every scan call and never while the park is paused or lost. GPU-free, no original assets.
/// </summary>
[TestClass, DoNotParallelize]
public class AdvisorQueueCadenceTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static (AutomaticAdvisor Advisor, ParkEconomy Park) Attached( bool broke = false )
	{
		var park = EconomyTestData.Park( initialCash: broke ? 100 : 100_000 );
		if ( broke )
		{
			EconomyTestData.HireBest( park, StaffType.Researcher );
			park.OpenPark();
		}
		var advisor = new AutomaticAdvisor( new AdvisorController( AdvisorSnapshotTests.Balance() ), () => 0, speechActive: () => false );
		var type = typeof( AutomaticAdvisor );
		type.GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( advisor, RuntimeHelpers.GetUninitializedObject( typeof( Level ) ) );
		type.GetField( "park", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( advisor, ParkEconomyRuntime.ForResume( park, ParkStartKind.FullSimulation ) );
		return (advisor, park);
	}

	[TestMethod]
	public void TheQueueIsServedOncePerParkTurnNotOncePerFrame()
	{
		var previous = GameOptions.Current.Advisor;
		try
		{
			GameOptions.Current.Advisor = false;
			var (advisor, park) = Attached();
			advisor.Update();
			Assert.AreEqual( 0, advisor.QueueServices, "the first update only takes the turn as the start" );
			for ( var frame = 0; frame < 5; frame++ )
				advisor.Update();
			Assert.AreEqual( 0, advisor.QueueServices, "frames in which no park turn passed serve nothing" );
			park.Advance( ParkCalendar.TickOfTurn( 1 ) );
			advisor.Update();
			Assert.AreEqual( 1, advisor.QueueServices, "one turn passed: one service" );
			advisor.Update();
			Assert.AreEqual( 1, advisor.QueueServices );
			park.Advance( ParkCalendar.TickOfTurn( 4 ) - park.Tick );
			advisor.Update();
			Assert.AreEqual( 4, advisor.QueueServices, "three more turns passed between two frames: three more services" );
		}
		finally { GameOptions.Current.Advisor = previous; }
	}

	[TestMethod]
	public void TheQueueIsNotServedWhileTheGameIsPausedOrTheParkIsLost()
	{
		var (advisor, park) = Attached( broke: true );
		advisor.Update();
		park.Advance( ParkCalendar.TickOfTurn( 2 ) );
		advisor.Update( gamePaused: true );
		Assert.AreEqual( 0, advisor.QueueServices, "a pause owner holds the turns: nothing is scanned or served, and its turns are not caught up" );
		park.Advance( ParkCalendar.TickOfTurn( 3 ) - park.Tick );
		advisor.Update( gamePaused: false );
		Assert.AreEqual( 1, advisor.QueueServices );
		park.Advance( ParkCalendar.TickOfTurn( 4 ) - park.Tick );
		advisor.Update( gamePaused: false, parkPaused: true );
		Assert.AreEqual( 1, advisor.QueueServices, "a stopped park serves no queue" );
		for ( var month = 0; month < 12 && !park.IsBankrupt; month++ )
			park.AdvanceMonths( 1 );
		Assert.IsTrue( park.IsBankrupt );
		advisor.Update();
		var served = advisor.QueueServices;
		park.AdvanceMonths( 1 );
		advisor.Update();
		Assert.AreEqual( served, advisor.QueueServices, "a lost park runs no thing loop, so no advisor update" );
	}
}
