using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.IO;
using System.Globalization;


namespace OpenTPW.Tests;

[TestClass]
public class TpiBalanceProfileTests
{
	private const string Supported = "BankAccountInfo.InitialCash 60000\nBankAccountInfo.InitialAdmissionFee 0\nCosts.QueueCell 10\nCosts.PathCell 5\nCosts.KartTrackCell 30\nCosts.WaterTrackCell 40\nCosts.MapCell 50\n";

	[TestMethod]
	public void CurrentTpwFixedSchemaCountExcludesDynamicTablesAndOptionalThemeKeys()
	{
		var fixedKeys = SamDocument.Parse( EconomyTestData.StandardText(), "TPW-test-schema" ).Entries
			.Where( entry => !entry.Key.StartsWith( "LoanInfo[", StringComparison.Ordinal ) && !entry.Key.StartsWith( "ChallengesInThisLevel[", StringComparison.Ordinal ) )
			.Select( entry => entry.Key ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();
		// 162 since the handymen's world reads PerGradeStaffConsts[g].IdleDuration and HandymanConstsPerGrade[g].DetectionRange
		Assert.AreEqual( 162, fixedKeys.Length );
	}

	[TestMethod]
	public void SupportedFieldsAreUsableWithoutTheTpwEconomySchema()
	{
		var profile = TpiBalanceProfile.FromLayers( new[] { SamDocument.Parse( Supported, "standard" ) } );
		Assert.AreEqual( GameEdition.ThemeParkInc, profile.Edition );
		Assert.AreEqual( 60000L, profile.InitialCash!.Value );
		Assert.AreEqual( 0, profile.InitialAdmissionFee!.Value, "an explicit original zero is present" );
		Assert.AreEqual( new CellCosts( 10, 5, 30, 40, 50 ), profile.Costs );
		Assert.IsTrue( profile.Readiness.SupportedSettingsReady );
		Assert.IsFalse( profile.Readiness.EconomyReady );
		Assert.AreEqual( 5, profile.Readiness.RuntimeBlockers.Count );
		Assert.IsFalse( EditionCapabilities.ForThemeParkIncSlice().Economy );
	}

	[TestMethod]
	public void MissingValuesRemainAbsentAndNameTheirKeys()
	{
		var profile = TpiBalanceProfile.FromLayers( new[] { SamDocument.Parse( "Costs.PathCell 5\n", "partial" ) } );
		Assert.IsNull( profile.InitialCash );
		Assert.IsNull( profile.Costs );
		Assert.AreEqual( 5, profile.PathCell!.Value );
		Assert.AreEqual( 6, profile.Readiness.MissingSupportedKeys.Count );
		CollectionAssert.Contains( profile.Readiness.MissingSupportedKeys.ToArray(), "Costs.QueueCell" );
		Assert.IsFalse( profile.Readiness.SupportedSettingsReady );
	}

	[TestMethod]
	public void LayerOverridesRetainWinningFieldProvenance()
	{
		var profile = TpiBalanceProfile.FromLayers( new[]
		{
			SamDocument.Parse( Supported, "global" ),
			SamDocument.Parse( "# theme\nCosts.PathCell 9\nBankAccountInfo.InitialCash 80000\n", "theme" )
		} );
		Assert.AreEqual( 9, profile.Costs!.PathCell );
		Assert.AreEqual( ("theme", 2, "Costs.PathCell"), (profile.PathCell!.Provenance.Source, profile.PathCell.Provenance.Line, profile.PathCell.Provenance.Key) );
		Assert.AreEqual( "global", profile.QueueCell!.Provenance.Source );
		Assert.AreEqual( 80000L, profile.InitialCash!.Value );
	}

	[TestMethod]
	public void MalformedPresentValuesAreRejectedRatherThanDeclaredReady()
	{
		Assert.ThrowsException<InvalidDataException>( () => TpiBalanceProfile.FromLayers( new[] { SamDocument.Parse( "Costs.PathCell 1.5\n", "bad" ) } ) );
		Assert.ThrowsException<OverflowException>( () => TpiBalanceProfile.FromLayers( new[] { SamDocument.Parse( "Costs.PathCell 2147483648\n", "bad" ) } ) );
	}

	[TestMethod]
	public void IntegerReadingIsInvariantAndDoesNotRoundOrClamp()
	{
		var before = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo( "nl-NL" );
			var profile = TpiBalanceProfile.FromLayers( new[] { SamDocument.Parse(
				"BankAccountInfo.InitialCash 9223372036854775807\nCosts.PathCell -2147483648\nCosts.QueueCell +2147483647\n", "limits" ) } );
			Assert.AreEqual( long.MaxValue, profile.InitialCash!.Value );
			Assert.AreEqual( int.MinValue, profile.PathCell!.Value );
			Assert.AreEqual( int.MaxValue, profile.QueueCell!.Value );
			foreach ( var token in new[] { "1.0", "1e3", "NaN", "Infinity", "\"1,000\"", "9223372036854775808" } )
				Assert.ThrowsException<InvalidDataException>( () => TpiBalanceProfile.FromLayers( new[] { SamDocument.Parse( $"BankAccountInfo.InitialCash {token}\n", "invalid" ) } ) );
			Assert.ThrowsException<OverflowException>( () => TpiBalanceProfile.FromLayers( new[] { SamDocument.Parse( "Costs.PathCell -2147483649\n", "underflow" ) } ) );
		}
		finally
		{
			CultureInfo.CurrentCulture = before;
		}
	}

	[TestMethod]
	public void DuplicateExactKeysRetainLastWinningEntryAndIgnoreAliases()
	{
		var global = SamDocument.Parse( Supported + "Costs.PathCell 7\n", "global" );
		var local = SamDocument.Parse( "costs.pathcell 8\nCosts.PathCell 0 trailing comment\nCosts.PathCellAlias 99\nRenamed.InitialCash 123\n", "local" );
		var settings = new SamSettings( new[] { global, local } );
		var profile = new TpiBalanceProfile( settings );
		Assert.AreEqual( 0, profile.PathCell!.Value );
		Assert.AreSame( local.Entries[1], profile.PathCell.Provenance );
		Assert.AreEqual( ("local", 2, "0"), (profile.PathCell.Provenance.Source, profile.PathCell.Provenance.Line, profile.PathCell.Provenance.Values.Single()) );
		Assert.AreEqual( 60000L, profile.InitialCash!.Value );
		foreach ( var field in new[] { profile.InitialAdmissionFee!, profile.QueueCell!, profile.PathCell!, profile.KartTrackCell!, profile.WaterTrackCell!, profile.MapCell! } )
			Assert.AreSame( settings.Get( field.Provenance.Key ), field.Provenance );
		Assert.AreSame( settings.Get( "BankAccountInfo.InitialCash" ), profile.InitialCash.Provenance );
	}

	[TestMethod]
	public void ParserOmittedUnrecognizedValuesRemainMissingAndCannotEnableCapabilities()
	{
		var profile = TpiBalanceProfile.FromLayers( new[] { SamDocument.Parse( "Costs.PathCell invalid\nCosts.QueueCell 0\n", "invalid-token" ) } );
		Assert.IsNull( profile.PathCell );
		CollectionAssert.Contains( profile.Readiness.MissingSupportedKeys.ToArray(), "Costs.PathCell" );
		Assert.AreEqual( 0, profile.QueueCell!.Value );
		Assert.IsFalse( profile.Readiness.SupportedSettingsReady );
		Assert.IsFalse( profile.Readiness.EconomyReady );
		var capabilities = EditionCapabilities.ForThemeParkIncSlice();
		Assert.IsFalse( capabilities.Economy );
		Assert.IsFalse( capabilities.Staff );
		Assert.IsFalse( capabilities.FrontEnd );
		Assert.IsFalse( capabilities.ParkSaves );
	}
}
