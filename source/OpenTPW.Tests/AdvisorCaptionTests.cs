using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The captions of advisor responses (tp.exe 0x0059B620 posts the tag of the response's row, field +0x1C of 0x768FB8, to the tag system, 0x00509BF0): the table, the
/// advisor posting one when it chooses a response, and the HUD showing the entry's text in the message tube. The bank's words are the captions of messages 102 to 106.
/// </summary>
[TestClass]
public class AdvisorCaptionTests
{
	[TestMethod]
	public void TheShippedTableGivesTheBankMessagesTheirTexts()
	{
		var table = AdvisorCaptionTable.Table;
		Assert.AreEqual( 353, table.Count );
		// messages 102 to 106 (responses 266 to 275) are the money warnings; their tags 124 to 128 show the entries 123 to 127 of TAG_SYSTEM.str
		foreach ( var (response, tag) in new[] { (266, 124), (267, 124), (268, 125), (270, 126), (272, 127), (274, 128), (275, 128) } )
			Assert.AreEqual( new AdvisorCaption( tag, tag - 1 ), table[response], $"response {response}" );
		Assert.IsNull( AdvisorCaptionTable.For( 0 ), "the tag 0x17F has no text, so the response has no row" );
	}

	[TestMethod]
	public void TheTableRejectsMalformedAndDuplicateRows()
	{
		var good = AdvisorCaptionTable.Parse( "# c\ncaptions = [\n  { response = 5, tag = 4, text = 8 },\n]\n" );
		Assert.AreEqual( new AdvisorCaption( 4, 8 ), good[5] );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorCaptionTable.Parse( "captions = [\n  { response = 5, tag = 4 },\n]\n" ) );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorCaptionTable.Parse( "captions = [\n  { response = 5, tag = 4, text = 8 },\n  { response = 5, tag = 4, text = 8 },\n]\n" ) );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorCaptionTable.Parse( "captions = [\n  { response = 5, tag = 4, text = 8 },\n" ) );
	}

	[TestMethod]
	public void AChosenResponsePostsItsCaptionEntry()
	{
		// [BIN:TPW-EXE:0x0059B620 advisor caption] the caption is posted when the response is chosen, whatever the speech does
		var advisor = new AutomaticAdvisor( new AdvisorController( AdvisorBalance.Parse( Encoding.ASCII.GetBytes(
			"GeneralAdvisor.MinTimeAnyMessage 1\nGeneralAdvisor.MinTimeSameMessage 1\nGeneralAdvisor.MinScoreForConsideration 1\nMessageGroups[0].MinTimeSameMessage 0\nMessageGroups[0].SayOnlyOnce 0\nMessageGroups[0].DiscardAfterSlaps 0\n"
			+ "MessageGroups[1].MinTimeSameMessage 0\nMessageGroups[1].SayOnlyOnce 0\nMessageGroups[1].DiscardAfterSlaps 0\nWelcome.Score 1\nBankrupted.Score 1\nParkNowOpen.Score 1\nParkNowClosed.Score 1\nPrebuiltPark.Score 1\n" ), "test.sam" ) ), () => 0L );
		var posted = new System.Collections.Generic.List<int>();
		advisor.CaptionSink = ( entry, _ ) => posted.Add( entry );
		advisor.PostCaption( 274 );
		advisor.PostCaption( 0 );
		advisor.PostCaption( 266 );
		CollectionAssert.AreEqual( new[] { 127, 123 }, posted, "response 0 has no caption" );
		advisor.CaptionSink = null;
		advisor.PostCaption( 274 );
		Assert.AreEqual( 2, posted.Count );
	}

	[TestMethod]
	public void TheTextsWithParameterSlotsTakeTheirArgumentsFromTheRecord()
	{
		var file = LanguageTests.OriginalLanguagePublic( "English" ).LoadStrings( "TAG_SYSTEM.str" );
		string? Name( int id ) => id == 1100 ? "Belly Bounce" : null;
		// [BIN:TPW-EXE:0x00509F09 object name case] the response 194's tag 68 shows entry 71 with the ride's name in slot 0
		var breakdown = new AdvisorPendingAdvice( 66, 105, ThingId: 12, Secondary: 1100 );
		var caption = AdvisorCaptionTable.For( 194 )!.Value;
		Assert.AreEqual( 71, caption.TextEntry );
		StringAssert.Contains( Hud.ParkHud.AdvisorCaptionText( file, caption.TextEntry, AdvisorCaptionArguments.For( caption.Kind, breakdown, Name, null ) ), "Belly Bounce" );
		StringAssert.Contains( Hud.ParkHud.AdvisorCaptionText( file, caption.TextEntry, AdvisorCaptionArguments.For( caption.Kind, breakdown, Name, null ) ), "ride is about to break down!" );
		// [BIN:TPW-EXE:0x0050A134 recent visitors ticket] the response 439's text names the visitors (slot 1) and the months (slot 2) of the ticket
		var tickets = new GoldenTicketSettings( 1, 1, 1, 1, 1, RecentVisitors: 9000, RecentVisitorMonths: 5, 0, 0, 0, 0, 0 );
		var win = AdvisorCaptionTable.For( 439 )!.Value;
		var text = Hud.ParkHud.AdvisorCaptionText( file, win.TextEntry, AdvisorCaptionArguments.For( win.Kind, new AdvisorPendingAdvice( 215, 1 ), Name, tickets ) )!;
		StringAssert.Contains( text, " 9000 " );
		StringAssert.Contains( text, "last 5 months" );
	}

	[TestMethod]
	public void TheMessageTubeShowsTheTextOfPlainEntriesAndFillsTheParameterSlots()
	{
		var file = LanguageTests.OriginalLanguagePublic( "English" ).LoadStrings( "TAG_SYSTEM.str" );
		StringAssert.StartsWith( Hud.ParkHud.AdvisorCaptionText( file, 127 ), "You have been in the red for six months, so the banks have closed your park" );
		StringAssert.StartsWith( Hud.ParkHud.AdvisorCaptionText( file, 123 ), "You have run out of money!" );
		StringAssert.Contains( Hud.ParkHud.AdvisorCaptionText( file, 71, new System.Collections.Generic.Dictionary<int, string> { [0] = "Totem" } ), "Totem" );
		Assert.IsNull( Hud.ParkHud.AdvisorCaptionText( file, -1 ) );
		Assert.IsNull( Hud.ParkHud.AdvisorCaptionText( file, file.Entries.Length ) );
	}
}
