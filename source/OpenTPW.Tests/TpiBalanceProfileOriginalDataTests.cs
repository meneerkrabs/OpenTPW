using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>Reads private original SAM files only when OPENTPW_TPI_PATH is supplied.</summary>
[TestClass]
public class TpiBalanceProfileOriginalDataTests
{
	public TestContext TestContext { get; set; } = null!;

	private static string DataFolder()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to the original TPI Data directory." );
		var data = Path.Combine( path!, "Data" );
		return Directory.Exists( data ) ? data : path!;
	}

	private static SamDocument Read( string data, string relative ) => SamDocument.Parse(
		File.ReadAllText( Path.Combine( data, relative ) ), relative.Replace( '\\', '/' ) );

	[TestMethod]
	public void RetailCashAndCellFieldsHaveOriginalProvenanceInEveryTheme()
	{
		var data = DataFolder();
		var global = Read( data, "levels/Standard.sam" );
		foreach ( var theme in new[] { "arabian", "science", "water", "wIntro" } )
		{
			var local = Read( data, $"levels/{theme}/Standard.sam" );
			var settings = new SamSettings( new[] { global, local } );
			var profile = new TpiBalanceProfile( settings );
			Assert.IsTrue( profile.Readiness.SupportedSettingsReady, $"{theme}: {string.Join( ", ", profile.Readiness.MissingSupportedKeys )}" );
			Assert.IsNotNull( profile.Costs );
			Assert.IsFalse( profile.Readiness.EconomyReady );
			var ints = new[] { profile.InitialAdmissionFee!, profile.QueueCell!, profile.PathCell!, profile.KartTrackCell!, profile.WaterTrackCell!, profile.MapCell! };
			foreach ( var field in ints )
			{
				Assert.AreEqual( settings.Get( field.Provenance.Key ), field.Provenance );
				Assert.AreEqual( settings.GetInt( field.Provenance.Key ), field.Value );
				Assert.IsTrue( field.Provenance.Line > 0 );
				TestContext.WriteLine( $"{theme}: {field.Provenance.Source}:{field.Provenance.Line} {field.Provenance.Key} = {field.Value}" );
			}
			Assert.AreEqual( settings.GetLong( "BankAccountInfo.InitialCash" ), profile.InitialCash!.Value );
		}
	}

	[TestMethod]
	public void ReportFixedTpwKeyCoverageSeparatelyFromDynamicLoanAndChallengeTables()
	{
		var data = DataFolder();
		var tpi = new SamSettings( new[] { Read( data, "levels/Standard.sam" ) } );
		// Concrete fixed reads represented by the existing TPW functional-test schema. Loan offers and
		// ChallengesInThisLevel are index-discovered, so their absence cannot be represented by a fixed count.
		var fixedKeys = SamDocument.Parse( EconomyTestData.StandardText(), "TPW-test-schema" ).Entries
			.Select( entry => entry.Key )
			.Where( key => !key.StartsWith( "LoanInfo[", StringComparison.Ordinal ) && !key.StartsWith( "ChallengesInThisLevel[", StringComparison.Ordinal ) )
			.Distinct( StringComparer.OrdinalIgnoreCase ).OrderBy( key => key, StringComparer.Ordinal ).ToArray();
		Assert.AreEqual( 152, fixedKeys.Length );
		var missing = fixedKeys.Where( key => !tpi.Contains( key ) ).ToArray();
		TestContext.WriteLine( $"Current fixed Standard reads: {fixedKeys.Length}; missing from retail TPI global Standard: {missing.Length}." );
		TestContext.WriteLine( string.Join( "\n", missing ) );
		Assert.IsTrue( missing.Length > 0, "the TPW constructor must not be treated as TPI-ready" );
		TestContext.WriteLine( $"TPI LoanInfo indices: {tpi.GetIndices( "LoanInfo", "LoanAmount" ).Count}; ChallengesInThisLevel indices: {tpi.GetIndices( "ChallengesInThisLevel", "ChallengeType" ).Count}." );
	}
}
