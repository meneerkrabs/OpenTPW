using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Which original ride scripts place riders' heads (ADDHEAD / DELHEAD), the head records of their models and the
/// model nodes the heads hang from. Inconclusive without OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class RideHeadsCorpusTests
{
	private static readonly string[] Themes = { "fantasy", "hallow", "jungle", "space" };

	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the ride head corpus." );
		originalFileSystem = previous;
		initialized = true;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
	}

	/// <summary>Every object with a script: its ADDHEAD and DELHEAD counts, and its model's head space (null when it has no head id 1).</summary>
	private static IEnumerable<(string Theme, ObjectCatalogEntry Entry, int Adds, int Dels, RideHeadSpace? Space, ModelFile Model)> Scripted()
	{
		foreach ( var theme in Themes )
		{
			var catalog = ObjectCatalog.Load( theme );
			foreach ( var entry in catalog.Entries.Where( x => x.ScriptPath != null && x.ModelPath != null ).OrderBy( x => x.InfoId ) )
			{
				var script = new RideScriptFile( entry.FileSystem.OpenRead( entry.ScriptPath! ) );
				var adds = script.Instructions.Count( x => x.Opcode == (ushort)Opcode.ADDHEAD );
				var dels = script.Instructions.Count( x => x.Opcode == (ushort)Opcode.DELHEAD );
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath! );
				yield return (theme, entry, adds, dels, RideHeadSpace.Create( model, entry.InfoId ), model);
			}
		}
	}

	/// <summary>The 31 scripts that place heads (the ADDHEAD/DELHEAD pair), by name, in four themes.</summary>
	[TestMethod]
	public void ScriptsThatPlaceHeadsAreThePinnedThirtyOne()
	{
		var pairs = Scripted().Where( x => x.Adds > 0 ).ToArray();
		Assert.AreEqual( 31, pairs.Length );
		Assert.IsTrue( pairs.All( x => x.Dels == x.Adds ), "every ADDHEAD script also removes heads" );
		CollectionAssert.AreEqual( new[] { 8, 9, 6, 8 }, Themes.Select( theme => pairs.Count( x => x.Theme == theme ) ).ToArray() );
		var jungle = pairs.Where( x => x.Theme == "jungle" ).Select( x => x.Entry.DisplayName ).OrderBy( x => x ).ToArray();
		CollectionAssert.AreEqual( new[] { "Crazy Ape", "Eruption", "Mumbo", "Rocky Racers", "Sun God", "Tom Tom Twister" }, jungle );
	}

	/// <summary>
	/// The jungle head counts measured in the PC executable (docs/exe/park.md, "The head table": Sun God 32, Crazy Ape 16,
	/// Mumbo 5, Rocky Racers 8, Tom Tom Twister 40, Eruption 16) and the three jungle scripts without ADDHEAD that carry a run
	/// (ferry 1, Inca Totem 13, Aztec Mayhem 27).
	/// </summary>
	[TestMethod]
	public void JungleHeadRunsMatchTheExecutableMeasurements()
	{
		var runs = Scripted().Where( x => x.Theme == "jungle" && x.Space != null )
			.ToDictionary( x => x.Entry.DisplayName, x => (x.Adds, x.Space!.Count) );
		Assert.AreEqual( (1, 32), runs["Sun God"] );
		Assert.AreEqual( (1, 16), runs["Crazy Ape"] );
		Assert.AreEqual( (1, 5), runs["Mumbo"] );
		Assert.AreEqual( (1, 8), runs["Rocky Racers"] );
		Assert.AreEqual( (1, 40), runs["Tom Tom Twister"] );
		Assert.AreEqual( (1, 16), runs["Eruption"] );
		Assert.AreEqual( (0, 1), runs["Ferry"] );
		Assert.AreEqual( (0, 13), runs["Inca Totem"] );
		Assert.AreEqual( (0, 27), runs["Aztec Mayhem"] );
	}

	/// <summary>
	/// A head that a jungle ride script places hangs from a model node: the nodes named after the head ids. Eruption names
	/// no node for its head 1 (its node is called Head1a), so that head is not drawn.
	/// </summary>
	[TestMethod]
	public void JungleHeadsHangFromNamedNodes()
	{
		var drawable = Scripted().Where( x => x.Theme == "jungle" && x.Adds > 0 )
			.ToDictionary( x => x.Entry.DisplayName, x => x.Space!.Count == 0 ? 0 : Enumerable.Range( 0, x.Space.Count ).Count( slot => x.Space.NodeAt( slot ) >= 0 ) );
		Assert.AreEqual( 32, drawable["Sun God"] );
		Assert.AreEqual( 16, drawable["Crazy Ape"] );
		Assert.AreEqual( 5, drawable["Mumbo"] );
		Assert.AreEqual( 8, drawable["Rocky Racers"] );
		Assert.AreEqual( 40, drawable["Tom Tom Twister"] );
		Assert.AreEqual( 15, drawable["Eruption"] );
	}

	/// <summary>Head space of the tube and Space Balls' models: Tubes Of Zob runs ADDHEAD but its model has no head id 1, so its table is null.</summary>
	[TestMethod]
	public void TubesOfZobRunsAddHeadWithoutAHeadTable()
	{
		var zob = Scripted().Single( x => x.Entry.DisplayName == "Tubes Of Zob" );
		Assert.IsTrue( zob.Adds > 0 );
		Assert.IsNull( zob.Space );
	}
}
