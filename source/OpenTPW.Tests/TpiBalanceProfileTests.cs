using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.IO;


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
		Assert.AreEqual( 152, fixedKeys.Length );
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
}
