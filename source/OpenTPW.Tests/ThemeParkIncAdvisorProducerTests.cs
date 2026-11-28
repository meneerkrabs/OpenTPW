using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// <c>content/data/tpi-advisor-producers.toml</c> (docs/reverse/TPI-ADVISOR.md §9): the producer of each of Theme Park Inc's 606 advisor messages. The inventory must classify every message of the
/// descriptor table exactly once, agree with the table on the scan rows, and call a message wired exactly when OpenTPW posts it. GPU-free; reads the shipped data only.
/// </summary>
[TestClass]
public sealed class ThemeParkIncAdvisorProducerTests
{
	private const string RelativePath = "content/data/tpi-advisor-producers.toml";

	private static readonly string[] Kinds = { "scan", "scan-zero", "event", "help", "unused" };

	/// <summary>
	/// The messages OpenTPW posts: the forecasts and the letters (P1), the unread mail, the window help of the tag tube, mission and wage windows, the mission controller's advice, the mission
	/// window's close advice and the lost challenge's second post (P4).
	/// </summary>
	private static readonly int[] Wired = { 0x154, 0x157, 0x15A, 0x15B, 0x1A2, 0x1A3, 0x198, 0x15D, 0x1AF, 0x1B0, 0x1B1, 0x1B6, 0x277, 0x1FA, 0x19B, 0x172, 0x1EB, 0x1EC,
		0x1ED, 0x1EE, 0x173, 0x21D, 0x1A4, 0x26C };

	private static readonly Regex MessageRow = new(
		@"^\s*\{ message = 0x([0-9a-f]+), kind = ""([^""]*)"", status = ""([^""]*)"", evidence = ""([^""]*)"", producer = ""([^""]*)"" \},?\s*$", RegexOptions.CultureInvariant );

	private sealed record Producer( int Message, string Kind, string Status, string Evidence, string Text );

	private static string Shipped( string relative )
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var path = Path.Combine( directory.FullName, relative );
			if ( File.Exists( path ) )
				return path;
		}
		Assert.Fail( $"{relative} not found above the test directory." );
		return "";
	}

	private static IReadOnlyList<Producer> Parse( string text )
	{
		var rows = new List<Producer>();
		var open = false;
		var lineNumber = 0;
		foreach ( var raw in text.Split( '\n' ) )
		{
			lineNumber++;
			var line = raw.TrimEnd( '\r' );
			if ( line.Trim().Length == 0 || line.TrimStart().StartsWith( '#' ) )
				continue;
			if ( line.Trim() == "messages = [" )
			{
				open = true;
				continue;
			}
			if ( line.Trim() == "]" && open )
			{
				open = false;
				continue;
			}
			if ( !open || MessageRow.Match( line ) is not { Success: true } row )
				throw new InvalidDataException( $"Theme Park Inc advisor producers line {lineNumber} is not a message row." );
			rows.Add( new Producer( int.Parse( row.Groups[1].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture ), row.Groups[2].Value, row.Groups[3].Value,
				row.Groups[4].Value, row.Groups[5].Value ) );
		}
		return rows;
	}

	private static IReadOnlyList<Producer> Inventory() => Parse( File.ReadAllText( Shipped( RelativePath ) ) );

	[TestMethod]
	public void TheParserRejectsAMalformedRow()
	{
		Assert.ThrowsException<InvalidDataException>( () => Parse( "messages = [\n  { message = 1 },\n]\n" ) );
		Assert.AreEqual( 0x1AF, Parse( "messages = [\n  { message = 0x1af, kind = \"help\", status = \"wired\", evidence = \"traced\", producer = \"p\" },\n]\n" ).Single().Message );
	}

	[TestMethod]
	public void EveryMessageIsClassifiedExactlyOnce()
	{
		var table = ThemeParkIncAdvisorTable.Parse( File.ReadAllText( Shipped( ThemeParkIncAdvisorTable.RelativePath ) ) );
		var messages = table.Descriptors.Select( row => row.Message ).Distinct().OrderBy( message => message ).ToArray();
		var inventory = Inventory();
		Assert.AreEqual( 606, messages.Length, "606 message ids" );
		CollectionAssert.AreEqual( messages, inventory.Select( row => row.Message ).ToArray(), "same ids in ascending order, none twice" );
		foreach ( var row in inventory )
		{
			var id = $"message 0x{row.Message:X}";
			CollectionAssert.Contains( Kinds, row.Kind, id );
			Assert.IsTrue( row.Text.Length > 0, $"{id} names its producer or the search" );
			Assert.AreEqual( row.Kind switch { "scan-zero" or "unused" => "none", _ => row.Status == "wired" ? "wired" : "gated" }, row.Status, id );
			Assert.AreEqual( row.Kind == "unused" ? "searched" : "traced", row.Evidence, id );
			if ( row.Status == "gated" && row.Kind is "event" or "help" )
				StringAssert.Contains( row.Text, "[EXT:tpi-capability-gates]", $"{id}: a gated sender names the gate" );
		}
	}

	[TestMethod]
	public void TheScanRowsAreTheTablesAndTheWiredRowsAreTheOnesOpenTpwPosts()
	{
		var table = ThemeParkIncAdvisorTable.Parse( File.ReadAllText( Shipped( ThemeParkIncAdvisorTable.RelativePath ) ) );
		var inventory = Inventory().ToDictionary( row => row.Message );
		var scanned = table.Descriptors.Where( row => row.Scan ).GroupBy( row => row.Message ).ToDictionary( group => group.Key, group => group.First() );
		Assert.AreEqual( 232, scanned.Count, "232 scanned ids" );
		foreach ( var (message, row) in inventory )
		{
			if ( scanned.TryGetValue( message, out var scan ) )
				Assert.AreEqual( scan.ConstantScore == 0 ? "scan-zero" : "scan", row.Kind, $"message 0x{message:X}" );
			else
				Assert.IsTrue( row.Kind is "event" or "help" or "unused", $"message 0x{message:X}: the scan never evaluates it" );
		}
		Assert.AreEqual( 15, inventory.Values.Count( row => row.Kind == "scan-zero" ) );
		CollectionAssert.AreEquivalent( Wired, inventory.Values.Where( row => row.Status == "wired" ).Select( row => row.Message ).ToArray(), "wired exactly when OpenTPW posts it" );
		// every wired message scores without the scan's callbacks and has a response the speech reads
		foreach ( var message in Wired )
		{
			var row = table.Find( message )!.Value;
			Assert.IsTrue( ThemeParkIncAdvisor.Scores( row ), $"message 0x{message:X} scores" );
			for ( var variant = 0; variant < row.Count; variant++ )
			{
				var response = ThemeParkIncAdviceResponse.For( row.Response + variant );
				Assert.IsNotNull( response, $"message 0x{message:X} response {row.Response + variant}" );
				// the speech refuses the level bank and the presenter's voices (the mission panel's 0x223 to 0x234 have them, so they stay gated)
				Assert.IsFalse( response.Value.LevelBank || ThemeParkIncAdviceResponse.PresenterGroup( response.Value.Voice ) >= 0, $"message 0x{message:X}" );
			}
		}
		Assert.AreEqual( "help", inventory[0x1AF].Kind );
		Assert.AreEqual( "gated", inventory[0x211].Status, "the go-to hint waits for the first-hour clock (P7)" );
		Assert.IsTrue( Enumerable.Range( 0x223, 18 ).All( panel => inventory[panel].Status == "gated" ), "the mission panel's presenter advice is gated" );
		Assert.IsTrue( new[] { 0xBF, 0x13A, 0x13B, 0x13C, 0x13D, 0x13E, 0xC0, 0xC1, 0xC3, 0x142, 0x143, 0x144, 0x145, 0x146, 0xC4, 0xC5 }
			.All( award => inventory[award].Kind == "event" && inventory[award].Status == "gated" ), "the award advice is gated (D-7)" );
	}
}
