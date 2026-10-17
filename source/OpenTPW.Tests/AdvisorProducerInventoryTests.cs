using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>
/// <c>content/data/advisor-producers.toml</c> (docs/reverse/ADVISOR-SCAN.md): the producer of each of the 351 advisor messages of tp.exe.
/// The inventory must cover every descriptor exactly once, agree with the descriptor table on what kind of producer a message has, and
/// call a message wired only when OpenTPW defines it in <see cref="AdvisorTables"/>. GPU-free; reads the shipped data only.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AdvisorProducerInventoryTests
{
	private const string RelativePath = "content/data/advisor-producers.toml";
	private const uint ConstantZeroCallback = 0x5D60C0;
	private const uint HelpCallback = 0x59E8C0;

	private static readonly string[] Kinds = { "scan", "scan-zero", "event", "help", "unused" };
	private static readonly string[] Statuses = { "wired", "ready", "blocked", "none" };
	private static readonly string[] Batches = { "money", "cleanliness", "rides", "queues_prices", "guest_mood", "staff", "research", "weather", "challenges", "tutorial", "other" };

	private static readonly Regex TagRow = new( @"^\s*\{ tag = ""(\w+)"", ready = (true|false), state = ""([^""]*)"" \},?\s*$", RegexOptions.CultureInvariant );
	private static readonly Regex MessageRow = new(
		@"^\s*\{ message = (\d+), kind = ""([^""]*)"", batch = ""([^""]*)"", status = ""([^""]*)"", producer = ""([^""]*)"", score = ""([^""]*)"", inputs = ""([^""]*)"", formula = ""([^""]*)"", captions = \[([\d, ]*)\], orphans = \[([\d, ]*)\], sample = (-?\d+), local = (true|false) \},?\s*$",
		RegexOptions.CultureInvariant );

	private sealed record Tag( string Name, bool Ready, string State );
	private sealed record Producer( int Message, string Kind, string Batch, string Status, string Producer_, string Score, string[] Inputs, string Formula, int[] Captions, int[] Orphans, int Sample, bool Local );

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

	private static int[] Numbers( string list ) => list.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).Select( item => int.Parse( item, CultureInfo.InvariantCulture ) ).ToArray();

	private static (IReadOnlyList<Tag> Tags, IReadOnlyList<Producer> Messages) Parse( string text )
	{
		var tags = new List<Tag>();
		var messages = new List<Producer>();
		var section = "";
		var lineNumber = 0;
		foreach ( var raw in text.Split( '\n' ) )
		{
			lineNumber++;
			var line = raw.TrimEnd( '\r' );
			if ( line.Trim().Length == 0 || line.TrimStart().StartsWith( '#' ) )
				continue;
			if ( line.Trim() is "tags = [" or "messages = [" )
			{
				section = line.Trim()[..^4];
				continue;
			}
			if ( line.Trim() == "]" )
			{
				section = "";
				continue;
			}
			if ( section == "tags" && TagRow.Match( line ) is { Success: true } tag )
			{
				tags.Add( new Tag( tag.Groups[1].Value, tag.Groups[2].Value == "true", tag.Groups[3].Value ) );
				continue;
			}
			if ( section == "messages" && MessageRow.Match( line ) is { Success: true } row )
			{
				var g = row.Groups;
				messages.Add( new Producer( int.Parse( g[1].Value, CultureInfo.InvariantCulture ), g[2].Value, g[3].Value, g[4].Value, g[5].Value, g[6].Value,
					g[7].Value.Split( ',', StringSplitOptions.RemoveEmptyEntries ), g[8].Value, Numbers( g[9].Value ), Numbers( g[10].Value ),
					int.Parse( g[11].Value, CultureInfo.InvariantCulture ), g[12].Value == "true" ) );
				continue;
			}
			throw new InvalidDataException( $"Advisor producers line {lineNumber} is not a tag or message row." );
		}
		return (tags, messages);
	}

	private static (IReadOnlyList<Tag> Tags, IReadOnlyList<Producer> Messages) Inventory() => Parse( File.ReadAllText( Shipped( RelativePath ) ) );

	private static IReadOnlyList<AdvisorDescriptorRow> Descriptors() => AdvisorDescriptorTable.Parse( File.ReadAllText( Shipped( AdvisorDescriptorTable.RelativePath ) ) );

	[TestMethod]
	public void ParserRejectsAMalformedRow()
	{
		Assert.ThrowsException<InvalidDataException>( () => Parse( "messages = [\n  { message = 1 },\n]\n" ) );
		var (tags, messages) = Parse( "tags = [\n  { tag = \"balance\", ready = true, state = \"x\" },\n]\nmessages = [\n  { message = 0, kind = \"event\", batch = \"other\", status = \"wired\", producer = \"p\", score = \"\", inputs = \"\", formula = \"f\", captions = [], orphans = [], sample = 1, local = true },\n]\n" );
		Assert.AreEqual( 1, tags.Count );
		Assert.AreEqual( 0, messages.Single().Message );
	}

	[TestMethod]
	public void InventoryCoversAllDescriptorsExactlyOnce()
	{
		var descriptors = Descriptors();
		var (_, messages) = Inventory();
		Assert.AreEqual( AdvisorDescriptorTable.Count, descriptors.Count, "351 descriptors" );
		Assert.AreEqual( descriptors.Count, messages.Count, "one inventory row per descriptor" );
		CollectionAssert.AreEqual( descriptors.Select( row => row.MessageId ).ToArray(), messages.Select( row => row.Message ).ToArray(), "same ids, same order, none twice" );
		Assert.AreEqual( messages.Count, messages.Select( row => row.Message ).Distinct().Count() );
	}

	[TestMethod]
	public void RowsUseKnownValuesAndKnownTags()
	{
		var (tags, messages) = Inventory();
		var byName = tags.ToDictionary( tag => tag.Name );
		Assert.AreEqual( tags.Count, byName.Count, "each tag once" );
		var responses = AdvisorResponses.Parse( File.ReadAllText( Shipped( AdvisorResponses.RelativePath ) ) );
		var descriptors = Descriptors();
		foreach ( var row in messages )
		{
			var id = $"message {row.Message}";
			CollectionAssert.Contains( Kinds, row.Kind, id );
			CollectionAssert.Contains( Statuses, row.Status, id );
			CollectionAssert.Contains( Batches, row.Batch, id );
			Assert.IsTrue( row.Producer_.Length > 0, $"{id} names its producer or says that none exists" );
			foreach ( var input in row.Inputs.Where( input => input != "window" ) )
				Assert.IsTrue( byName.ContainsKey( input ), $"{id}: unknown input tag {input}" );
			Assert.IsTrue( row.Captions.Concat( row.Orphans ).All( entry => entry is >= 0 and < 332 ), $"{id}: TAG_SYSTEM.str has 332 entries" );
			Assert.IsTrue( row.Sample == -1 || row.Sample > 0, id );
		}
		Assert.IsTrue( messages.Where( row => row.Status == "blocked" && row.Kind != "help" ).All( row => row.Inputs.Any( input => !byName[input].Ready ) ), "a blocked scan or event row has a missing input" );
		Assert.IsTrue( messages.Where( row => row.Status == "ready" ).All( row => row.Inputs.All( input => byName[input].Ready ) ), "a ready row has every input" );
		Assert.IsTrue( messages.Where( row => row.Status == "none" ).All( row => row.Kind is "scan-zero" or "unused" ), "only a row without a producer has nothing to produce" );
		Assert.IsTrue( messages.Where( row => row.Kind is "scan-zero" or "unused" ).All( row => row.Status == "none" ) );
		Assert.AreEqual( 0, messages.Count( row => row.Batch == "weather" ), "no advisor message reads the weather" );
		// the speech sample the inventory names is the first response's, as the response table has it
		foreach ( var row in messages.Where( row => row.Sample > 0 ) )
		{
			var response = responses[descriptors[row.Message].FirstResponseId];
			Assert.AreEqual( response.Sample, row.Sample, $"message {row.Message}" );
			Assert.AreEqual( response.Local, row.Local, $"message {row.Message}" );
		}
	}

	[TestMethod]
	public void KindsAgreeWithTheDescriptorTable()
	{
		var descriptors = Descriptors();
		var (_, messages) = Inventory();
		foreach ( var (descriptor, row) in descriptors.Zip( messages ) )
		{
			var id = $"message {descriptor.MessageId}";
			if ( descriptor.Scan )
			{
				// the background scan 0x59C680 reaches the descriptor: computed callback = scan, the constant-zero stub = scan-zero
				Assert.AreEqual( descriptor.Callback == ConstantZeroCallback ? "scan-zero" : "scan", row.Kind, id );
			}
			else
			{
				Assert.IsTrue( row.Kind is "event" or "help" or "unused", $"{id}: the scan skips it, so game code posts it or nothing does" );
				if ( descriptor.Callback == HelpCallback )
					Assert.IsTrue( row.Kind is "help" or "unused", $"{id}: a context-help descriptor" );
				else
					Assert.AreNotEqual( "help", row.Kind, id );
			}
		}
		Assert.AreEqual( 134, messages.Count( row => row.Kind == "scan" ), "134 computed scan callbacks" );
		Assert.AreEqual( 21, messages.Count( row => row.Kind == "scan-zero" ), "21 scanned descriptors score a constant 0" );
		Assert.AreEqual( 155, messages.Count( row => row.Kind is "scan" or "scan-zero" ), "155 scanned descriptors (ADVISOR-009)" );
	}

	[TestMethod]
	public void EightyTwoHelpDescriptorsEightyOneHelpRows()
	{
		var descriptors = Descriptors();
		var help = descriptors.Where( row => row.Callback == HelpCallback ).Select( row => row.MessageId ).ToArray();
		Assert.AreEqual( 82, help.Length, "82 descriptors share the context-help score callback 0x59E8C0" );
		var rows = AdvisorHelpTable.Messages.Select( row => row.MessageId ).ToArray();
		Assert.AreEqual( 81, rows.Length, "81 of them are in advisor-help-messages.toml" );
		CollectionAssert.AreEqual( new[] { 291 }, help.Except( rows ).ToArray(), "291 names the absent response 551" );
		Assert.IsFalse( AdvisorResponses.Parse( File.ReadAllText( Shipped( AdvisorResponses.RelativePath ) ) ).ContainsKey( descriptors[291].FirstResponseId ) );
		Assert.AreEqual( 82, AdvisorTables.HelpMessages.Count + 1 );
	}

	[TestMethod]
	public void WiredRowsHaveAProducerInAdvisorTables()
	{
		var (_, messages) = Inventory();
		var defined = AdvisorTables.AllMessages.Select( message => message.MessageId ).ToHashSet();
		var windowHelp = AdvisorUiHelp.AllWindowMessages.ToHashSet();
		var wired = messages.Where( row => row.Status == "wired" ).ToArray();
		Assert.IsTrue( wired.Length > 0 );
		foreach ( var row in wired )
		{
			Assert.IsTrue( defined.Contains( row.Message ), $"message {row.Message} is wired but AdvisorTables does not define it" );
			if ( row.Kind == "help" )
				Assert.IsTrue( windowHelp.Contains( row.Message ), $"help message {row.Message} is wired but no window or tool of AdvisorUiHelp posts it" );
			else
				Assert.AreEqual( "event", row.Kind, $"message {row.Message}" );
		}
		// and the other way: what OpenTPW posts is wired in the inventory
		foreach ( var id in windowHelp )
			Assert.AreEqual( "wired", messages[id].Status, $"help message {id} is posted by OpenTPW" );
		var staged = new[] { 66, 68, 69 };
		foreach ( var id in defined.Where( id => !windowHelp.Contains( id ) && !AdvisorTables.HelpMessages.Any( help => help.MessageId == id ) ) )
		{
			if ( staged.Contains( id ) )
				Assert.AreEqual( "ready", messages[id].Status, $"message {id} is defined in AdvisorTables but nothing posts it yet" );
			else
				Assert.AreEqual( "wired", messages[id].Status, $"message {id} is defined and posted by OpenTPW" );
		}
	}

	[TestMethod]
	public void GlobalTicketAdviceIsPostedButNotDefined()
	{
		// AutomaticAdvisor.GlobalTicketMovedMessage posts 234-237, but AdvisorTables has no definition, so Controller.Advise returns null:
		// the inventory keeps them "ready" until the four EventScoreKeys rows exist (docs/reverse/ADVISOR-SCAN.md).
		var (_, messages) = Inventory();
		var defined = AdvisorTables.AllMessages.Select( message => message.MessageId ).ToHashSet();
		var posted = new[] { GoldenTicketKind.CoasterHeight, GoldenTicketKind.GokartExcitement, GoldenTicketKind.WaterLength, GoldenTicketKind.BigPark }
			.Select( kind => AutomaticAdvisor.GlobalTicketMovedMessage( kind )!.Value ).ToArray();
		CollectionAssert.AreEqual( new[] { 234, 235, 236, 237 }, posted );
		foreach ( var id in posted )
			Assert.AreEqual( defined.Contains( id ), messages[id].Status == "wired", $"message {id}: wired exactly when AdvisorTables defines it" );
	}

	[TestMethod]
	public void KeyLinesAreWhereTheDocumentSaysTheyAre()
	{
		var (_, messages) = Inventory();
		Assert.IsTrue( Enumerable.Range( 102, 4 ).All( id => messages[id].Kind == "scan" && messages[id].Batch == "money" ), "the money warnings are scan rows of the money batch" );
		Assert.IsTrue( Enumerable.Range( 103, 3 ).All( id => messages[id].Inputs.Contains( "monthsInRed" ) ), "103-105 read the months in the red" );
		Assert.AreEqual( "event", messages[106].Kind, "bankruptcy is game event 2" );
		CollectionAssert.AreEqual( new[] { 123 }, messages[102].Captions );
		Assert.IsTrue( Enumerable.Range( 238, 5 ).All( id => messages[id].Kind == "scan" && messages[id].Batch == "queues_prices" && messages[id].Captions.Length == 0 ), "the five shop price lines are speech only" );
		Assert.IsTrue( new[] { 58, 59 }.All( id => messages[id].Kind == "scan-zero" ), "the long-queue lines are scored by the stub 0x5D60C0" );
		Assert.IsTrue( new[] { 8, 9, 10 }.All( id => messages[id].Batch == "cleanliness" ) );
		Assert.IsTrue( new[] { 5, 6, 7, 66, 68, 69, 70 }.All( id => messages[id].Batch == "rides" ) );
	}
}
