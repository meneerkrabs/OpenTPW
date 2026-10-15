using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class AdvisorResponseTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static string ShippedTable()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var path = Path.Combine( directory.FullName, AdvisorResponses.RelativePath );
			if ( File.Exists( path ) )
				return path;
		}
		Assert.Fail( "content/data/advisor-responses.toml not found above the test directory." );
		return "";
	}

	[TestMethod]
	public void ParsesTheGeneratedLayoutStrictly()
	{
		var table = AdvisorResponses.Parse( "# comment\n\nresponses = [\n  { id = 0, sample = 638, lip = 0, animation = -1, model = 0, local = false },\n  { id = 1, sample = 1, lip = 1, animation = 16, model = 1, local = true },\n]\n" );
		Assert.AreEqual( new AdvisorResponse( 0, 638, 0, -1, 0, false ), table[0] );
		Assert.IsTrue( table[1].Local );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorResponses.Parse( "responses = [\n  { id = 0 },\n]\n" ) );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorResponses.Parse( "responses = [\n  { id = 0, sample = 1, lip = 1, animation = -1, model = 0, local = false },\n" ), "unclosed" );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorResponses.Parse( "responses = [\n  { id = 0, sample = 1, lip = 1, animation = -1, model = 0, local = false },\n  { id = 0, sample = 1, lip = 1, animation = -1, model = 0, local = false },\n]\n" ), "duplicate" );
	}

	[TestMethod]
	public void ShippedTableHoldsTheTracedResponses()
	{
		var table = AdvisorResponses.Parse( File.ReadAllText( ShippedTable() ) );
		Assert.AreEqual( 610, table.Count );
		Assert.AreEqual( new AdvisorResponse( 0, 638, 0, -1, 0, false ), table[0], "response 0 plays z_error (sample 638) without a LIP" );
		CollectionAssert.AreEqual( new[] { 1, 399, 400, 401, 402 }, table.Values.Where( response => response.Local ).Select( response => response.Id ).OrderBy( id => id ).ToArray(), "only these use the level speech bank" );
		Assert.IsTrue( table.Values.Where( response => response.Local ).All( response => response.Sample == 1 && response.Lip == 1 ) );
	}

	[TestMethod]
	public void OriginalResponsesLoadFromTheGlobalAndLevelBanks()
	{
		var data = StringTableTests.OriginalDataDirectory();
		var fileSystem = new BaseFileSystem( data );
		var table = AdvisorResponses.Parse( File.ReadAllText( ShippedTable() ) );
		var error = Advisor.LoadResponse( fileSystem, table[0], "jungle" );
		Assert.AreEqual( 0, error.Timeline.Marks.Count, "no LIP for the error sound" );
		Assert.IsTrue( error.Audio.DurationSeconds > 0 );
		var global = Advisor.LoadResponse( fileSystem, table[2], "jungle" );
		Assert.IsTrue( global.Timeline.Marks.Count > 0 );
		StringAssert.Contains( global.Source, "/global/Speech" );
		var local = Advisor.LoadResponse( fileSystem, table[1], "jungle" );
		StringAssert.Contains( local.Source, "/levels/jungle/Speech" );
		Assert.IsTrue( local.Timeline.Marks.Count > 0 );
		Assert.ThrowsException<InvalidOperationException>( () => Advisor.LoadResponse( fileSystem, table[1], null ) );
	}
}
