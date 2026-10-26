using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>
/// UI-096 and ADVISOR-017: the owners of tp.exe's game pause 0x4092A0 and the gate they share. The pause menu, the map window and the postcard pause (0x48C87E, 0x5F0B7F, 0x4A93D7); the generic
/// message boxes 0x47F020 pause every park (0x47F26C: the enable word 0x786BA4 is 1 and [0xF82884] is 0, which holds in every park: only the front end sets it), except that a shared park or a visit is gated off ([EXT:online-coop-lockstep]); the research window's two refusals are such boxes (0x4AA533, 0x4AA596);
/// the P key and the application's deactivation pause the same flag and freeze the same timer. Asset-free.
/// </summary>
[TestClass]
public class PauseOwnersTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static void Set( object o, string field, object? value ) => o.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( o, value );

	[TestMethod]
	public void EveryMessageBoxOfAParkPausesAndNoneDoesInASharedParkOrAVisit()
	{
		var boxes = new[] { "confirmDelete", "confirmDeleteAttraction", "confirmDismissStaff", ParkHud.WinningMessageScreen, ParkHud.ResearchRefusedScreen, ParkHud.AlertBoxScreen };
		foreach ( var box in boxes )
		{
			Assert.IsTrue( ParkHud.IsPausingMessageBox( box ), box );
			Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 2f, new[] { "hud", box } ), box + " pauses the park (0x47F26C)" );
			Assert.AreEqual( 2f, ParkHud.TimeScaleFor( 2f, new[] { "hud", box }, online: true ), box + " pauses nothing in a shared park or a visit ([EXT:online-coop-lockstep])" );
		}
		foreach ( var owner in new[] { ParkHud.PauseMenuScreen, ParkHud.MapScreenName, ParkHud.PostcardScreen } )
			Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 2f, new[] { "hud", owner }, online: true ), owner + ": the pause menu, the map and the postcard do not test the online object" );
		foreach ( var other in new[] { "loanConfirm", "loanFunds", "loanFailure", "research", "buy", "restart" } )
			Assert.IsFalse( ParkHud.IsPausingMessageBox( other ), other + " is no 0x47F020 call site of the park" );
	}

	[TestMethod]
	public void TheResearchWindowRefusesInInstantActionAndWithoutAScientistWithAPausingBox()
	{
		var instant = EconomyTestData.Park( mode: ParkGameMode.InstantAction );
		var hud = StaffWindowTests.Hud( instant );
		hud.OpenResearchWindow();
		Assert.IsFalse( hud.ResearchWindowOpen );
		Assert.AreEqual( ParkHud.ResearchRefusedScreen, hud.Stack.Top!.Name );
		Assert.AreEqual( 0f, hud.SimulationTimeScale, "the box pauses the park" );
		Assert.IsTrue( hud.ClockPaused, "and the advisor's clock" );
		hud.Stack.Pop();
		Assert.AreEqual( 1f, hud.SimulationTimeScale, "closing it resumes" );

		var empty = EconomyTestData.Park();
		hud = StaffWindowTests.Hud( empty );
		Assert.IsFalse( empty.Staff.Members.Any( member => member.Type == StaffType.Researcher ) );
		hud.OpenResearchWindow();
		Assert.IsFalse( hud.ResearchWindowOpen, "UITEXT 0x1d3: no scientist, no window" );
		Assert.AreEqual( ParkHud.ResearchRefusedScreen, hud.Stack.Top!.Name );
	}

	[TestMethod]
	public void TheClockStandsStillForEveryOwnerOfTheGamePauseAndOnlyForThem()
	{
		var economy = EconomyTestData.Park();
		EconomyTestData.HireBest( economy, StaffType.Researcher );
		var hud = StaffWindowTests.Hud( economy );
		Set( hud, "researchFluids", Enumerable.Range( 0, 5 ).Select( _ => new ResearchFluid() ).ToArray() );
		Set( hud, "researchFluidClock", new ReferenceFrameClock( 0.25f ) );
		Assert.IsFalse( hud.ClockPaused );
		hud.OpenResearchWindow();
		Assert.IsTrue( hud.ResearchWindowOpen );
		Assert.IsFalse( hud.ClockPaused, "the research window is no caller of 0x4092A0: the game timer and the advisor go on" );
		hud.CloseResearchWindow();
		Assert.IsFalse( hud.ClockPaused );
		hud.Stack.Push( new OpenTPW.UI.Original.UiScreen( ParkHud.PauseMenuScreen ) );
		Assert.IsTrue( hud.ClockPaused, "the pause menu" );
		hud.Stack.Pop();
		hud.Stack.Push( new OpenTPW.UI.Original.UiScreen( ParkHud.MapScreenName ) );
		Assert.IsTrue( hud.ClockPaused, "the map window" );
		hud.Stack.Pop();
		Assert.IsFalse( hud.ClockPaused );
		Set( hud, "applicationPaused", true );
		Assert.IsTrue( hud.ClockPaused, "WM_ACTIVATEAPP deactivation (0x46B74C)" );
		Set( hud, "applicationPaused", false );
		Assert.IsFalse( hud.ClockPaused );
		economy.Speed = GameSpeed.Paused;
		Assert.IsTrue( hud.ClockPaused, "the P key (0x409350) freezes the same timer (0x402DD0)" );
	}

	[TestMethod]
	public void ALoggedInPlayersOwnParkPausesOnItsBoxesButAVisitDoesNot()
	{
		var economy = EconomyTestData.Park();
		var hud = StaffWindowTests.Hud( economy );
		// tp.exe's [0xF82884] is 0 in every park (only the front end sets it), so being logged in changes nothing for the park's own boxes (0x47F26C)
		Set( hud, "host", new HudHost { IsLoggedIn = () => true } );
		hud.Stack.Push( new OpenTPW.UI.Original.UiScreen( "confirmDelete" ) );
		Assert.AreEqual( 0f, hud.SimulationTimeScale );
		Assert.IsTrue( hud.ClockPaused );
		// [EXT:online-coop-lockstep] a read-only visit is the host's park: no box stops its clock
		var level = (OpenTPW.Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
		typeof( OpenTPW.Level ).GetField( "<Visit>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( OpenTPW.ParkVisitInfo ) ) );
		Assert.AreEqual( 1f, hud.SimulationTimeScale );
		Assert.IsFalse( hud.ClockPaused );
	}
}
