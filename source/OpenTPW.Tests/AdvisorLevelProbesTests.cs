using System;
using OpenTPW.Hud;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// ADVISOR-009: the level answers the advisor's two world reads (<see cref="ParkEconomy.RideLiveExcitement"/> and <see cref="ParkEconomy.FirstUnconnectedQueueRide"/>) for the callbacks 141 and 113:
/// the live excitement of a ride is the bridge's, and the first queue ride that joins no path skips the object the player has selected (tp.exe <c>0x004CCB00</c>, <c>0x527930</c>).
/// </summary>
[TestClass, DoNotParallelize]
public class AdvisorLevelProbesTests
{
	private RideUpgradeIngressTests fixture = null!;

	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();

	private static void Attach( Level level ) =>
		typeof( Level ).GetMethod( "AttachAdvisorProbes", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, Array.Empty<object>() );

	[TestMethod]
	public void TheLevelAnswersTheLiveExcitementOfARideByItsEconomyInstance()
	{
		fixture.Fixture( BuildCategory.Rides, out var level, out var economy, out var first, out var second, out _, extra: "UsageInfo.ExcitementLevel 50\n" );
		Assert.IsNull( economy.RideLiveExcitement, "nothing is attached before the level does" );
		Attach( level );
		first.Visitors.ExcitementSource = () => 77;
		Assert.AreEqual( 77, economy.RideLiveExcitement!( level.GetEconomyInstance( first )!.Value ) );
		Assert.AreEqual( 0, economy.RideLiveExcitement!( level.GetEconomyInstance( second )!.Value ), "a ride without excitement data answers 0 (0x4E0860)" );
		Assert.AreEqual( 0, economy.RideLiveExcitement!( 9999 ), "an object the level does not hold" );
		// the mean over the two rides, through the analyser's cached reader
		Assert.AreEqual( 38, new EconomyParkState( economy ).AverageRideExcitement( 0 ), "(77 + 0) / 2 truncated" );
	}

	[TestMethod]
	public void TheFirstUnconnectedQueueSkipsTheSelectedRide()
	{
		fixture.Fixture( BuildCategory.Rides, out var level, out var economy, out var first, out var second, out _, extra: "Info.HasQueue 1\n" );
		Attach( level );
		var state = new EconomyParkState( economy );
		var grid = level.Guests!.Grid;
		// a typed entry without a cardinal link bit: the native front is 0 and the back-of-queue test fails (0x4DE4A0)
		grid.Cells.SetType( 5, 5, ParkCellTypes.Entrance );
		grid.Cells.SetLinks( 5, 5, 0 );
		first.Visitors.Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 );
		(first.Visitors.EntranceCell, first.Visitors.ExitCell, first.Visitors.QueueFrontCell, first.Visitors.QueueEntranceDirection, first.Visitors.HasCells) = ((1, 1), (1, 1), (1, 1), 2, true);
		first.Visitors.QueueEntryResolver = () => (5, 5);
		Assert.IsFalse( ParkGate.QueueConnected( grid, first.Visitors ), "the fixture's queue ride joins no path" );
		Assert.AreSame( first, level.Selection, "the fixture selects the first ride" );
		Assert.AreEqual( 0, economy.FirstUnconnectedQueueRide!(), "the selected object is left out (0x527930)" );
		Assert.AreEqual( false, state.AnyQueueUnconnected );
		level.Selection = second;
		Assert.AreEqual( level.GetEconomyInstance( first ), economy.FirstUnconnectedQueueRide!() );
		Assert.AreEqual( true, state.AnyQueueUnconnected );
		level.Selection = null;
		Assert.AreEqual( level.GetEconomyInstance( first ), economy.FirstUnconnectedQueueRide!() );
	}

	[TestMethod]
	public void TheLevelNamesItsAttractionsByTheirThingIdForTheAdvisorsWords()
	{
		fixture.Fixture( BuildCategory.Rides, out var level, out var economy, out var first, out var second, out _, extra: "Info.HasQueue 1\n" );
		Assert.IsNull( economy.ObjectThingId, "nothing is attached before the level does" );
		(first.ThingId, second.ThingId) = (123, 40000);   // the fixture's level has no thing pool; a slot above 32767 must survive the word
		Attach( level );
		var firstInstance = level.GetEconomyInstance( first )!.Value;
		var secondInstance = level.GetEconomyInstance( second )!.Value;
		Assert.AreEqual( first.ThingId, economy.ObjectThingId!( firstInstance ) );
		Assert.AreEqual( second.ThingId, economy.ObjectThingId!( secondInstance ) );
		Assert.AreNotEqual( economy.ObjectThingId!( firstInstance ), economy.ObjectThingId!( secondInstance ) );
		Assert.AreEqual( 0, economy.ObjectThingId!( 9999 ), "an object the level does not hold" );
		Assert.AreEqual( (ushort)first.ThingId, economy.ThingOf( firstInstance ), "the economy's word is the level's thing id, not its own instance id" );
		// the first unconnected queue is one read that carries both ids
		var grid = level.Guests!.Grid;
		grid.Cells.SetType( 5, 5, ParkCellTypes.Entrance );
		grid.Cells.SetLinks( 5, 5, 0 );
		first.Visitors.Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 );
		(first.Visitors.EntranceCell, first.Visitors.ExitCell, first.Visitors.QueueFrontCell, first.Visitors.QueueEntranceDirection, first.Visitors.HasCells) = ((1, 1), (1, 1), (1, 1), 2, true);
		first.Visitors.QueueEntryResolver = () => (5, 5);
		level.Selection = null;
		var found = new EconomyParkState( economy ).UnconnectedQueue;
		Assert.AreEqual( new AdvisorQueueProbe( firstInstance, (ushort)first.ThingId ), found );
		level.Selection = first;
		Assert.AreEqual( new AdvisorQueueProbe( 0, 0 ), new EconomyParkState( economy ).UnconnectedQueue, "the selected object is left out: no handle, no word" );
	}

	[TestMethod]
	public void TheLevelNamesTheSecondBusStopsCellWordOnlyWhenItsSettingsHoldOne()
	{
		fixture.Fixture( BuildCategory.Rides, out var level, out var economy, out _, out _, out _ );
		Assert.IsNull( economy.ArrivalBusStopCell );
		Attach( level );
		Assert.IsNull( economy.ArrivalBusStopCell!(), "nothing is made up for a level whose balance file names no second stop" );
		// [BIN:TPW-EXE:0x004D8650 bus stop cell word] the flag 1 returns (0x7855B8 << 7) + 0x7855B4 + 1
		level.Guests!.Settings.BusStopB = (34, 12);
		Assert.AreEqual( 12 * 128 + 34 + 1, economy.ArrivalBusStopCell!() );
		Assert.AreEqual( 12 * 128 + 34 + 1, new EconomyParkState( economy ).ArrivalBusStopCell );
	}

	[TestMethod]
	public void TheSettingsReadTheSecondBusStopFromTheFixedItemInfo()
	{
		static GuestSettings Of( string text )
		{
			var values = new System.Collections.Generic.Dictionary<string, string[]>();
			GuestSettings.ParseSam( text, values );
			return GuestSettings.FromValues( values );
		}
		Assert.AreEqual( (61, 99), Of( "FixedItemInfo.BusStopBPosX 61\nFixedItemInfo.BusStopBPosY 99\n" ).BusStopB );
		Assert.IsNull( Of( "FixedItemInfo.BusStopAPosX 1\nFixedItemInfo.BusStopAPosY 2\n" ).BusStopB );
	}
}
