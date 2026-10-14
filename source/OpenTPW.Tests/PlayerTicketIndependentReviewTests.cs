using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class PlayerTicketIndependentReviewTests
{
	[DataTestMethod]
	[DataRow( "Mode", "FullSimulation, InstantAction", null )]
	[DataRow( "Mode", "FullSimulation, FullSimulation", null )]
	[DataRow( "Kind", "Visitors, PeopleInPark", "jungle" )]
	[DataRow( "Kind", "PeopleInPark, Happiness", "jungle" )]
	[DataRow( "Kind", "Happiness, ProfitYear", null )]
	[DataRow( "Kind", "CamerasEverywhere, AllLandOwned", null )]
	[DataRow( "Kind", "Visitors, Visitors", "jungle" )]
	public void CombinedNonFlagsEnumNamesCannotForgeOneKnownIdentity( string field, string value, string? theme )
	{
		var item = field == "Kind" ? $"{{\"Theme\":{(theme == null ? "null" : "\"" + theme + "\"")},\"Kind\":\"{value}\"}}" : "";
		var mode = field == "Mode" ? value : "FullSimulation";
		var json = $"{{\"Format\":\"opentpw-player-progress\",\"Version\":1,\"Mode\":\"{mode}\",\"ExtraKeys\":0,\"SpentTickets\":0,\"Earned\":[{item}]}}";
		Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Deserialize( Encoding.UTF8.GetBytes( json ) ) );
	}
	private static string Json( string mode, string? kind = null, string? theme = null )
	{
		var item = kind == null ? "" : $"{{\"Theme\":{(theme == null ? "null" : "\"" + theme + "\"")},\"Kind\":\"{kind}\"}}";
		return $"{{\"Format\":\"opentpw-player-progress\",\"Version\":1,\"Mode\":\"{mode}\",\"ExtraKeys\":0,\"SpentTickets\":0,\"Earned\":[{item}]}}";
	}

	[DataTestMethod, DataRow( "FullSimulation" ), DataRow( "InstantAction" )]
	public void BothExactModeNamesRemainValid( string name )
	{
		var ledger = PlayerTicketFile.Deserialize( Encoding.UTF8.GetBytes( Json( name ) ) );
		Assert.AreEqual( name, ledger.Mode.ToString() ); Assert.AreEqual( 0, ledger.EarnedTickets );
		StringAssert.Contains( Encoding.UTF8.GetString( PlayerTicketFile.Serialize( ledger.Capture() ) ), $"\"Mode\":\"{name}\"" );
	}

	[DataTestMethod]
	[DataRow( "Visitors", "jungle" )]
	[DataRow( "PeopleInPark", "jungle" )]
	[DataRow( "Happiness", "jungle" )]
	[DataRow( "AllResearchedAndBuilt", "jungle" )]
	[DataRow( "ProfitYear", "jungle" )]
	[DataRow( "RecentVisitors", "jungle" )]
	[DataRow( "CoasterHeight", null )]
	[DataRow( "GokartExcitement", null )]
	[DataRow( "WaterLength", null )]
	[DataRow( "BigPark", null )]
	[DataRow( "CamerasEverywhere", null )]
	[DataRow( "AllLandOwned", null )]
	public void EveryExactKindNameRemainsValidAndWritesSameToken( string name, string? theme )
	{
		var ledger = PlayerTicketFile.Deserialize( Encoding.UTF8.GetBytes( Json( "FullSimulation", name, theme ) ) );
		Assert.AreEqual( name, ledger.Capture().Earned.Single().Kind.ToString() ); Assert.AreEqual( theme, ledger.Capture().Earned.Single().Theme );
		StringAssert.Contains( Encoding.UTF8.GetString( PlayerTicketFile.Serialize( ledger.Capture() ) ), $"\"Kind\":\"{name}\"" );
	}

	[DataTestMethod]
	[DataRow( "fullsimulation", null )]
	[DataRow( "FULLSIMULATION", null )]
	[DataRow( " FullSimulation", null )]
	[DataRow( "FullSimulation ", null )]
	[DataRow( "0", null )]
	[DataRow( "Unknown", null )]
	[DataRow( "FullSimulation", "visitors" )]
	[DataRow( "FullSimulation", "VISITORS" )]
	[DataRow( "FullSimulation", " Visitors" )]
	[DataRow( "FullSimulation", "Visitors " )]
	[DataRow( "FullSimulation", "0" )]
	[DataRow( "FullSimulation", "Unknown" )]
	public void EnumNamesAreExactCaseWithoutNumericWhitespaceOrUnknownAliases( string mode, string? kind )
		=> Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Deserialize( Encoding.UTF8.GetBytes( Json( mode, kind, "jungle" ) ) ) );

	private static IEnumerable<GoldenTicketKind> FailingBatch()
	{
		yield return GoldenTicketKind.Visitors; yield return GoldenTicketKind.CoasterHeight;
		throw new IOException( "Synthetic enumeration failure after valid identities." );
	}
	[TestMethod]
	public void EnumerationFailureAfterValidPrefixLeavesExistingLedgerUnchanged()
	{
		var ledger = new PlayerTicketLedger(); ledger.RecordEarned( "hallow", GoldenTicketKind.Happiness );
		var before = PlayerTicketFile.Serialize( ledger.Capture() );
		Assert.ThrowsException<IOException>( () => ledger.Reconcile( "jungle", FailingBatch() ) );
		CollectionAssert.AreEqual( before, PlayerTicketFile.Serialize( ledger.Capture() ) );
		Assert.AreEqual( 2, ledger.Reconcile( "jungle", new[] { GoldenTicketKind.Visitors, GoldenTicketKind.CoasterHeight } ) );
		Assert.AreEqual( 3, ledger.EarnedTickets ); Assert.AreEqual( 1, ledger.Keys );
	}

	[TestMethod]
	public void MaximumKeyStateAndReadOnlyCapturePreserveAllIdentitiesAndZeroAvailable()
	{
		var ledger = new PlayerTicketLedger();
		foreach ( var theme in new[] { "jungle", "hallow", "fantasy", "space" } ) ledger.Reconcile( theme, Enum.GetValues<GoldenTicketKind>() );
		var capture = ledger.Capture(); var view = (IList<EarnedTicketIdentity>)capture.Earned;
		Assert.ThrowsException<NotSupportedException>( () => view[0] = new( "space", GoldenTicketKind.Visitors ) );
		ledger = PlayerTicketLedger.Restore( capture with { ExtraKeys = int.MaxValue - 10, SpentTickets = 30 } );
		ledger = PlayerTicketFile.Deserialize( PlayerTicketFile.Serialize( ledger.Capture() ) );
		Assert.AreEqual( int.MaxValue, ledger.Keys ); Assert.AreEqual( 0, ledger.AvailableTickets ); Assert.AreEqual( 30, ledger.EarnedTickets );
		Assert.AreEqual( 0, ledger.Reconcile( "space", Enum.GetValues<GoldenTicketKind>() ) ); Assert.AreEqual( int.MaxValue, ledger.Keys );
	}

	[TestMethod]
	public void ExactFileSizeBoundaryAndStrictReadFailureLeaveSourceAndOtherLedgerUntouched()
	{
		var ledger = new PlayerTicketLedger( ParkGameMode.InstantAction ); ledger.RecordEarned( "space", GoldenTicketKind.ProfitYear );
		var initial = PlayerTicketFile.Serialize( ledger.Capture() ); var padded = new byte[PlayerTicketFile.MaximumFileSize];
		Array.Fill( padded, (byte)' ' ); initial.CopyTo( padded, 0 );
		Assert.AreEqual( 1, PlayerTicketFile.Deserialize( padded ).EarnedTickets );
		var oversized = padded.Concat( new[] { (byte)' ' } ).ToArray(); Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Deserialize( oversized ) );
		var root = Directory.CreateTempSubdirectory( "opentpw-ticket-review-" ).FullName;
		try
		{
			var good = Path.Combine( root, "good.json" ); var bad = Path.Combine( root, "bad.json" );
			PlayerTicketFile.Save( good, ledger.Capture() ); var originalFile = File.ReadAllBytes( good );
			var malformed = Encoding.UTF8.GetBytes( Json( "FullSimulation", "PeopleInPark, Happiness", "jungle" ) ); File.WriteAllBytes( bad, malformed );
			Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.LoadIfPresent( bad ) );
			CollectionAssert.AreEqual( malformed, File.ReadAllBytes( bad ) ); CollectionAssert.AreEqual( originalFile, File.ReadAllBytes( good ) );
			CollectionAssert.AreEqual( initial, PlayerTicketFile.Serialize( ledger.Capture() ) ); Assert.AreEqual( 2, Directory.GetFiles( root ).Length );
		}
		finally { Directory.Delete( root, true ); }
	}

	[DataTestMethod]
	[DataRow( "nested-unknown" )]
	[DataRow( "duplicate-kind" )]
	[DataRow( "escaped-kind" )]
	[DataRow( "escaped-theme" )]
	[DataRow( "duplicate-identity" )]
	[DataRow( "null-identity" )]
	[DataRow( "numeric-mode" )]
	[DataRow( "numeric-kind" )]
	public void StrictNestedFieldsEscapedDuplicatesAndIdentityShapesAreRefused( string mutation )
	{
		var json = Json( "FullSimulation", "Visitors", "jungle" );
		Assert.AreEqual( 1, PlayerTicketFile.Deserialize( Encoding.UTF8.GetBytes( json ) ).EarnedTickets );
		var item = "{\"Theme\":\"jungle\",\"Kind\":\"Visitors\"}";
		var invalid = mutation switch
		{
			"nested-unknown" => json.Replace( "\"Kind\":\"Visitors\"", "\"Kind\":\"Visitors\",\"NativeFlag\":true" ),
			"duplicate-kind" => json.Replace( "\"Kind\":\"Visitors\"", "\"Kind\":\"Visitors\",\"Kind\":\"Happiness\"" ),
			"escaped-kind" => json.Replace( "\"Kind\":\"Visitors\"", "\"Kind\":\"Visitors\",\"\\u004bind\":\"Happiness\"" ),
			"escaped-theme" => json.Replace( "\"Theme\":\"jungle\"", "\"Theme\":\"jungle\",\"\\u0054heme\":\"hallow\"" ),
			"duplicate-identity" => json.Replace( item, item + "," + item ),
			"null-identity" => json.Replace( item, "null" ),
			"numeric-mode" => json.Replace( "\"Mode\":\"FullSimulation\"", "\"Mode\":0" ),
			_ => json.Replace( "\"Kind\":\"Visitors\"", "\"Kind\":0" )
		};
		Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Deserialize( Encoding.UTF8.GetBytes( invalid ) ) );
	}

}
