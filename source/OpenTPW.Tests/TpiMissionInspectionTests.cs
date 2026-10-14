using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class TpiMissionInspectionTests
{
	[TestMethod]
	public void ActualCliInspectsBeforeGameAssetsAndLeavesInputUnchanged()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-mission-" ).FullName;
		var previousLog = Log;
		var previousOutput = Console.Out;
		try
		{
			var path = Path.Combine( root, "mission.sam" );
			const string text = "Info.NoResearch 1\nExitStatistic[4].Type 99\nInfo.NoResearch banana\n";
			File.WriteAllText( path, text );
			using var output = new StringWriter();
			Console.SetOut( output );
			Game.Run( new[] { "--inspect-tpi-mission", path, "--headless", "--game-path", Path.Combine( root, "missing" ) } );
			StringAssert.Contains( output.ToString(), "modified-NoCD" );
			StringAssert.Contains( output.ToString(), "omitted" );
			Assert.AreEqual( text, File.ReadAllText( path ) );
			Assert.ThrowsException<ArgumentException>( () => Game.Run( new[] { "--inspect-tpi-mission", path, "--export-fsh-png" } ) );
			Assert.AreEqual( 1, Directory.GetFiles( root ).Length );
		}
		finally { Console.SetOut( previousOutput ); Log = previousLog; Directory.Delete( root, true ); }
	}
	[DataTestMethod]
	[DataRow( "Info.NoResearch", 2 )]
	[DataRow( "Completion.ThemeOpened", 2 )]
	[DataRow( "Completion.EndOfGame", 2 )]
	[DataRow( "Completion.LoadNewHoardings", 2 )]
	[DataRow( "TechLevel.Theme", 3 )]
	public void ScalarBoundsKeepEveryOccurrenceAndProvenance( string key, int upper )
	{
		for ( var value = -1; value <= upper; value++ )
		{
			var document = SamDocument.Parse( $"# comment\n{key} {value}\n", "synthetic.sam" );
			var result = TpiMissionSamValidation.ValidatePresentEntries( document )[0];
			Assert.AreSame( document.Entries[0], result.Entry );
			Assert.AreEqual( 2, result.Entry.Line );
			Assert.IsTrue( result.Checked );
			Assert.AreEqual( value < 0 || value >= upper, result.Error != null );
		}
		var duplicates = TpiMissionSamValidation.ValidatePresentEntries( SamDocument.Parse( $"{key} {upper}\n{key} 0", "duplicates" ) );
		Assert.IsNotNull( duplicates[0].Error ); Assert.IsNull( duplicates[1].Error );
	}

	[DataTestMethod]
	[DataRow( "BoundaryClicks", 64 )]
	[DataRow( "ExitStatistic", 5 )]
	public void IndexedCapacityChecksDoNotGuessMemberValues( string group, int capacity )
	{
		foreach ( var index in new[] { "-1", "+1", "x", "2147483648", capacity.ToString() } )
			Assert.IsNotNull( TpiMissionSamValidation.ValidatePresentEntries( SamDocument.Parse( $"{group}[{index}].Unknown -999999", "index" ) )[0].Error );
		var valid = TpiMissionSamValidation.ValidatePresentEntries( SamDocument.Parse( $"{group}[{capacity - 1}].Unknown -999999", "index" ) )[0];
		Assert.IsNull( valid.Error ); Assert.IsTrue( valid.Checked );
	}

	[DataTestMethod]
	[DataRow( "1.5" )]
	[DataRow( "1e0" )]
	[DataRow( "NaN" )]
	[DataRow( "Infinity" )]
	[DataRow( "9223372036854775808" )]
	[DataRow( "\"banana\"" )]
	public void RetainedNonIntegersAreRejected( string token )
	{
		var results = TpiMissionSamValidation.ValidatePresentEntries( SamDocument.Parse( "Info.NoResearch " + token, "integer" ) );
		Assert.AreEqual( 1, results.Count ); Assert.IsNotNull( results[0].Error );
	}

	[TestMethod]
	public void MissingUnknownAndExtraTokensDoNotAcquireNativeSemantics()
	{
		Assert.AreEqual( 0, TpiMissionSamValidation.ValidatePresentEntries( SamDocument.Parse( "Info.NoResearch banana", "omitted" ) ).Count );
		var results = TpiMissionSamValidation.ValidatePresentEntries( SamDocument.Parse( "Info.Number -999\ninfo.noresearch 99\nUnknown.Key 99\nInfo.NoResearch 1 99", "partial" ) );
		for ( var index = 0; index < 3; index++ ) Assert.IsFalse( results[index].Checked );
		Assert.IsNull( results[3].Error ); Assert.AreEqual( 2, results[3].Entry.Values.Count );
	}

	[TestMethod]
	public void ActualCliRejectsKnownBadEntriesAndArgumentErrors()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-mission-invalid-" ).FullName;
		var previousLog = Log; var previousOutput = Console.Out; var previousError = Console.Error; var previousExit = Environment.ExitCode;
		try
		{
			var path = Path.Combine( root, "invalid.sam" ); File.WriteAllText( path, "Info.NoResearch 2\nBoundaryClicks[64].X 0" );
			using var output = new StringWriter(); Console.SetOut( output ); Console.SetError( output );
			Assert.ThrowsException<InvalidDataException>( () => Game.Run( new[] { "--inspect-tpi-mission", path } ) );
			StringAssert.Contains( output.ToString(), path + ":1" ); StringAssert.Contains( output.ToString(), path + ":2" );
			Assert.AreEqual( 1, Program.Main( new[] { "--inspect-tpi-mission", path } ) );
			Assert.ThrowsException<ArgumentException>( () => Game.Run( new[] { "--inspect-tpi-mission" } ) );
			Assert.ThrowsException<ArgumentException>( () => Game.Run( new[] { "--inspect-tpi-mission", "--headless" } ) );
			Assert.ThrowsException<ArgumentException>( () => Game.Run( new[] { "--inspect-tpi-mission", path + ".txt" } ) );
			Assert.AreEqual( 1, Directory.GetFiles( root ).Length );
		}
		finally { Console.SetOut( previousOutput ); Console.SetError( previousError ); Environment.ExitCode = previousExit; Log = previousLog; Directory.Delete( root, true ); }
	}
}
