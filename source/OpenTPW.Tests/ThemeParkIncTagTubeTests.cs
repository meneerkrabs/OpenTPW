using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's tag tube (docs/reverse/TPI-TAG-TUBE.md): the rows the letters file, the unread counter and its advice, the delete and the expiry pass, the save, and the tube window
/// over its generated layout table.
/// </summary>
[TestClass]
public class ThemeParkIncTagTubeTests
{
	private static ThemeParkIncTagTubeRow Row( int tag, int category = 0, bool read = false, int expiry = 0, int date = 0x7D00001 ) => new()
	{
		Tag = tag,
		Kind = ThemeParkIncTagLetter.NullTag,
		Source = ThemeParkIncLetterSource.Objective,
		Argument = tag,
		Category = category,
		Read = read,
		ExpiryUnits = expiry,
		Date = date
	};

	[TestMethod]
	public void GameInsertsGoToTheHeadAndLoadsToTheTail()
	{
		var tube = new ThemeParkIncTagTube();
		var first = Row( 1 );
		var second = Row( 2 );
		tube.Insert( first, 10, load: false );
		tube.Insert( second, 10, load: false );
		CollectionAssert.AreEqual( new[] { second, first }, tube.Rows.ToArray(), "0x636880 links a game insert at the head" );
		var loaded = Row( 3 );
		tube.Insert( loaded, 10, load: true );
		Assert.AreSame( loaded, tube.Rows[^1], "a load walks to the tail" );
		Assert.AreEqual( (0u, 1u, 2u), (first.Serial, second.Serial, loaded.Serial), "every insert takes the next serial (0xAA0AF0)" );
	}

	[TestMethod]
	public void AGameInsertSupersedesAnOlderRowWhoseTagIsTheNewKind()
	{
		var tube = new ThemeParkIncTagTube();
		var older = Row( ThemeParkIncTagLetter.NullTag );
		tube.Insert( older, 0, load: false );
		var newer = Row( 5 );
		tube.Insert( newer, 0, load: false );
		Assert.IsTrue( older.Flagged && tube.UnlinkPending, "the older row's +0 equals the new row's +4 (0x97) and their +0xE18 match" );
		Assert.AreEqual( 1, tube.Listed.Count() );
		var objective = new ThemeParkIncTagTube();
		var a = ThemeParkIncTagLetter.Objective( 0, 0 );
		var b = ThemeParkIncTagLetter.Objective( 1, 0 );
		objective.Insert( a, 0, load: false );
		objective.Insert( b, 0, load: false );
		Assert.AreEqual( 2, objective.Listed.Count(), "objective rows (tag 0x8A, kind 0x97) never supersede each other" );
	}

	[TestMethod]
	public void TheUnreadCounterAdvisesAtThreeAndThenAtEveryDoubling()
	{
		var tube = new ThemeParkIncTagTube();
		for ( var index = 0; index < 12; index++ )
			tube.Insert( Row( index ), 0, load: false );
		CollectionAssert.AreEqual( new[] { ThemeParkIncTagTube.FirstUnreadAdvice, ThemeParkIncTagTube.UnreadAdvice, ThemeParkIncTagTube.UnreadAdvice }, tube.AdviceRequests,
			"0x54F1A0 posts 0x198 at 3, then 0x15D at 6 and 12" );
		Assert.AreEqual( (12, 24), (tube.Unread, tube.Threshold) );
		tube.MarkRead( tube.Rows[0] );
		Assert.AreEqual( (11, 24), (tube.Unread, tube.Threshold), "a read lowers the total, never the threshold (0x54F320)" );
		var read = new ThemeParkIncTagTube();
		read.Insert( Row( 1, category: 2, read: true ), 0, load: false );
		Assert.AreEqual( (0, 0), (read.Unread, read.CategoryUnread[2]), "a row filed read (kinds 6 to 8) does not count" );
	}

	[TestMethod]
	public void ADeletedRowLeavesAtTheNextPassAndAnUnreadOneCountsDown()
	{
		var tube = new ThemeParkIncTagTube();
		var row = Row( 7 );
		tube.Insert( row, 0, load: false );
		tube.Insert( Row( 8 ), 0, load: false );
		tube.Delete( row );
		Assert.IsTrue( row.Flagged );
		Assert.AreEqual( 2, tube.Unread, "0x636A40 does not touch the counter" );
		Assert.AreEqual( 2, tube.Rows.Count, "the row stays until the pass" );
		tube.Update( 1 );
		Assert.AreEqual( (1, 1), (tube.Rows.Count, tube.Unread), "0x635AB0 marks the unread row read and unlinks it" );
		tube.Delete( row );
		Assert.AreEqual( 1, tube.Rows.Count, "a row no longer in the list deletes nothing" );
	}

	[TestMethod]
	public void ARowExpiresAfterItsUnitsOfFourTurns()
	{
		var tube = new ThemeParkIncTagTube();
		var row = Row( 1, expiry: 3 );
		tube.Insert( row, 100, load: false );
		Assert.AreEqual( (112u, 112u), (row.ExpiryTurn, tube.EarliestExpiry), "+0xE08 = clock + 3 * 4" );
		tube.Update( 111 );
		Assert.IsFalse( row.Flagged );
		tube.Update( 112 );
		Assert.IsTrue( row.Flagged, "the auto-delete check flags it" );
		tube.Update( 113 );
		Assert.AreEqual( (0, 0), (tube.Rows.Count, tube.Unread) );
	}

	[TestMethod]
	public void TheSaveKeepsTheUnflaggedRowsAndTheLoadRecountsSilently()
	{
		var tube = new ThemeParkIncTagTube();
		for ( var index = 0; index < 4; index++ )
			tube.Insert( Row( index, expiry: index == 0 ? 2 : 0 ), 50, load: false );
		tube.Delete( tube.Rows[0] );
		var snapshot = tube.Capture();
		Assert.AreEqual( 3, snapshot.Rows.Length, "0x6355D0 writes the rows whose +0xE28 is 0" );
		Assert.IsTrue( ThemeParkIncTagTube.IsValid( snapshot ) );
		var loaded = new ThemeParkIncTagTube();
		loaded.Restore( snapshot, 1000 );
		CollectionAssert.AreEqual( snapshot.Rows.Select( row => row.Tag ).ToArray(), loaded.Rows.Select( row => row.Tag ).ToArray(), "the load appends in file order" );
		Assert.AreEqual( (3, 6), (loaded.Unread, loaded.Threshold), "the silent count reaches the threshold and doubles it" );
		Assert.AreEqual( 0, loaded.AdviceRequests.Count, "a load posts no advice" );
		Assert.AreEqual( 1008u, loaded.Rows.Single( row => row.ExpiryUnits != 0 ).ExpiryTurn, "the expiry restarts from the load's clock" );
		Assert.IsFalse( ThemeParkIncTagTube.IsValid( new ThemeParkIncTagTubeSnapshot( new[] { snapshot.Rows[0] with { Category = 4 } } ) ), "the counter has four categories" );
	}

	[TestMethod]
	public void TheLettersFileTheRowsTheirFunctionsFill()
	{
		var objective = ThemeParkIncTagLetter.Objective( 6, 0 );
		Assert.AreEqual( (0x8A, 0x97, 0, ThemeParkIncObjectiveLetterGoto.Challenges, 0, false, 1), (objective.Tag, objective.Kind, objective.Category, objective.GotoKind, objective.ExpiryUnits, objective.Read,
			objective.Picture), "0x5CEC30 and the letter's +0x10 = 0" );
		var briefing = ThemeParkIncTagLetter.Introduction( 0 );
		Assert.AreEqual( (0x5C, 0x97, 0, 0, false), (briefing.Tag, briefing.Kind, briefing.Category, briefing.GotoKind, briefing.Read), "0x5CD840 over the tag record 0x5C" );
		var bank = ThemeParkIncTagLetter.Notification( ThemeParkIncAdviceMail.RedSixMonths, 0 );
		Assert.AreEqual( (0x5E, 2, true, ThemeParkIncTagLetter.RedSixMonthsId), (bank.Tag, bank.Category, bank.Read, bank.Argument), "id 8: +0 = 0x5E after the fill, read" );
		var fired = ThemeParkIncTagLetter.Notification( ThemeParkIncAdviceMail.ChallengeLost, 0 );
		Assert.AreEqual( (0x5F, ThemeParkIncTagLetter.ChallengeLostId), (fired.Tag, fired.Argument) );
		var date = ParkCalendar.DateAtTurn( 0 );
		Assert.AreEqual( (date.Day, date.Month, date.Year), bank.DateParts, "0x518BC0 packs the month less one; 0x518C30 adds it back" );
	}

	[TestMethod]
	public void TheEconomyFilesTheBankLetterReadAndTheSaveRefusesAParkWithoutATube()
	{
		var economy = ThemeParkIncChallengeTests.Economy();
		Assert.IsNotNull( economy.TpiTagTube, "a Theme Park Inc park has its tag system (0x6483B7)" );
		economy.DebugPostThemeParkIncLetter( ThemeParkIncAdviceMail.RedSixMonths );
		Assert.AreEqual( ThemeParkIncAdviceMail.RedSixMonths, economy.TakeTpiMail(), "the letter window still opens on the mail" );
		var row = economy.TpiTagTube!.Listed.Single();
		Assert.AreEqual( (ThemeParkIncLetterSource.Notification, true), (row.Source, row.Read) );
		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"TpiTagTube\"" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( 1, restored.TpiTagTube!.Listed.Count() );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );
		var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		node.Remove( "TpiTagTube" );
		var older = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ) );
		Assert.AreEqual( ParkSaveFile.TagTubeMissing, older.Message, "a save from before the tube is refused by its validation, before any asset comparison" );
	}

	[TestMethod]
	public void TheTubeTableIsWindow3000WithItsList()
	{
		var table = ThemeParkIncLayoutTables.TagTube;
		Assert.AreEqual( 0x9BBC84, table.Address );
		Assert.AreEqual( "!window", table.Root.Model );
		Assert.AreEqual( "!list", table.Control( 0xBBE ).Model );
		Assert.AreEqual( "b_delete", table.Control( 0xBBC ).Model );
		Assert.AreEqual( 78, table.Control( 0xBBC ).Help );
		Assert.AreEqual( "b_goto", table.Control( 0xBCB ).Model );
		Assert.AreEqual( "mugshot", table.Control( 0xBC2 ).Model );
		CollectionAssert.IsSubsetOf( new uint[] { 0x10, 0x11 }, table.Control( 0xBBE ).Children.Select( child => child.Id ).ToArray(), "the two column headers" );
	}

	private sealed class World( ThemeParkIncTagTube tube ) : IThemeParkIncTagTubeWorld
	{
		public ThemeParkIncTagTube Tube => tube;
		public void Delete( ThemeParkIncTagTubeRow row ) => tube.Delete( row );
		public bool Read( ThemeParkIncTagTubeRow row ) => tube.MarkRead( row );
	}

	private static UiStringTable Strings()
	{
		var ui = Enumerable.Range( 0, 1000 ).Select( index => $"ui{index}" ).ToArray();
		var help = Enumerable.Range( 0, 400 ).Select( index => $"help{index}" ).ToArray();
		return new UiStringTable( "English", ui, help, Array.Empty<string>(), Array.Empty<string>() );
	}

	private static (ThemeParkIncTagTubeWindow Window, ThemeParkIncTagTube Tube, List<string> Events) Window( params ThemeParkIncTagTubeRow[] rows )
	{
		var tube = new ThemeParkIncTagTube();
		foreach ( var row in rows )
			tube.Insert( row, 0, load: false );
		var events = new List<string>();
		var window = new ThemeParkIncTagTubeWindow( Strings(), new World( tube ), new ThemeParkIncTagTubeSession(),
			row => new ThemeParkIncLetterWindow.Letter( $"subject{row.Tag}", $"from{row.Tag}", "to", $"date{row.Date}", $"body{row.Tag}", row.Picture - 1, row.GotoKind, row.GotoArgument ),
			() => events.Add( "closed" ), ( kind, argument ) => events.Add( $"goto {kind} {argument}" ) );
		window.Build();
		return (window, tube, events);
	}

	[TestMethod]
	public void TheListShowsTheNewestDateFirstAndSelectsNothingUntilAClick()
	{
		var (window, _, _) = Window( Row( 1, date: 0x7D00001 ), Row( 2, date: 0x7D00105 ), Row( 3, date: 0x7D00003 ) );
		CollectionAssert.AreEqual( new[] { 2, 3, 1 }, window.ListedRows.Select( row => row.Tag ).ToArray(), "sort key -2: the date column, descending" );
		Assert.IsNull( window.Selected, "the refresh selects no row" );
		Assert.IsTrue( window.DeleteEnabled && !window.EmptyShown, "rows enable Delete and hide the empty label" );
		window.PressHeader( 1 );
		CollectionAssert.AreEqual( new[] { 1, 3, 2 }, window.ListedRows.Select( row => row.Tag ).ToArray(), "the date header turns the direction" );
		window.PressHeader( 0 );
		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, window.ListedRows.Select( row => row.Tag ).ToArray(), "the subject header sorts its text ascending (wcscmp)" );
	}

	[TestMethod]
	public void ASelectedRowIsReadAfterTwoAndAHalfSecondsAndNotByAClickAlone()
	{
		var row = Row( 1 );
		var (window, tube, events) = Window( row );
		window.SelectRow( 0 );
		Assert.AreEqual( ("from1", "to", "subject1", "body1"), window.Details );
		Assert.IsFalse( row.Read, "a selection only starts the deadline" );
		window.Tick( 2499 );
		Assert.IsFalse( row.Read );
		window.Tick( 1 );
		Assert.IsTrue( row.Read, "the tick reads it at the deadline" );
		Assert.AreEqual( 0, tube.Unread );
		Assert.AreEqual( 0, events.Count, "no letter window and no go-to" );
	}

	[TestMethod]
	public void ARowFiledWhileAnotherIsShownKeepsItsSelectionAndDeadline()
	{
		// [BIN:TPI-EXE:0x552CF0 tube refresh] the refresh clears the list's selection and refills it, but the shown row (+0x1C), its details and the read deadline (+0x5C) stay
		var row = Row( 1, date: 0x7D00001 );
		var (window, tube, _) = Window( row );
		window.SelectRow( 0 );
		window.Tick( 1000 );
		tube.Insert( Row( 2, date: 0x7D00105 ), 0, load: false );
		window.Tick( 1 );
		CollectionAssert.AreEqual( new[] { 2, 1 }, window.ListedRows.Select( listed => listed.Tag ).ToArray(), "the new row is listed first" );
		Assert.AreSame( row, window.Selected );
		Assert.AreEqual( ("from1", "to", "subject1", "body1"), window.Details );
		Assert.IsFalse( row.Read );
		window.Tick( 1498 );
		Assert.IsFalse( row.Read );
		window.Tick( 1 );
		Assert.IsTrue( row.Read, "the deadline set at the selection still reads it" );
	}

	[TestMethod]
	public void ClosingReadsTheRowOnlyAfterHalfASecond()
	{
		var row = Row( 1 );
		var (window, _, events) = Window( row );
		window.SelectRow( 0 );
		window.Tick( 400 );
		window.Close();
		Assert.IsFalse( row.Read, "shown for 400 ms: now + 2000 is not past the deadline" );
		var (later, _, _) = Window( row );
		later.SelectRow( 0 );
		later.Tick( 600 );
		later.Close();
		Assert.IsTrue( row.Read );
		Assert.AreEqual( "closed", events.Single() );
	}

	[TestMethod]
	public void DeleteWaitsForTheTickAndTheEmptyListDisablesIt()
	{
		var (window, tube, _) = Window( Row( 1 ) );
		window.SelectRow( 0 );
		window.PressDelete();
		Assert.IsFalse( window.DeleteEnabled, "the press disables Delete" );
		Assert.IsFalse( tube.Rows[0].Flagged );
		window.Tick( 16 );
		Assert.IsTrue( window.EmptyShown && !window.DeleteEnabled, "the next tick deletes and the refresh shows the empty list" );
		Assert.AreEqual( ("", "", "", ""), window.Details );
	}

	[TestMethod]
	public void TheGotoShowsForARowWithATargetAndRunsIt()
	{
		var (window, _, events) = Window( ThemeParkIncTagLetter.Objective( 1, 0 ) );
		window.SelectRow( 0 );
		Assert.IsTrue( window.GotoShown );
		window.PressGoto();
		CollectionAssert.AreEqual( new[] { "closed", $"goto {ThemeParkIncObjectiveLetterGoto.MissionWindow} 0" }, events );
		var (plain, _, _) = Window( ThemeParkIncTagLetter.Introduction( 0 ) );
		plain.SelectRow( 0 );
		Assert.IsFalse( plain.GotoShown );
	}
}
