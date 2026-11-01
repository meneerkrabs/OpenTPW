using System;
using System.Collections.Generic;
using System.IO;
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
	/// The jungle head counts reported for the PC executable by the MIT fork (docs/exe/park.md, "The head table": Sun God 32, Crazy Ape 16,
	/// Mumbo 5, Rocky Racers 8, Tom Tom Twister 40, Eruption 16) and the three jungle scripts without ADDHEAD that carry a run
	/// (ferry 1, Inca Totem 13, Aztec Mayhem 27).
	/// </summary>
	[TestMethod]
	public void JungleHeadRunsMatchTheForkReportedMeasurements()
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
	/// A head that a ride script places hangs from the node of its record (header+0x46 + record index): every id of
	/// the run resolves to a node, including Eruption's head 1 whose node is called Head1a.
	/// </summary>
	[TestMethod]
	public void JungleHeadsResolveThroughTheirRecords()
	{
		var drawable = Scripted().Where( x => x.Theme == "jungle" && x.Adds > 0 && x.Space != null )
			.ToDictionary( x => x.Entry.DisplayName, x => Enumerable.Range( 0, x.Space!.Count ).Count( slot => x.Space.NodeAt( slot ) >= 0 ) );
		Assert.AreEqual( 32, drawable["Sun God"] );
		Assert.AreEqual( 16, drawable["Crazy Ape"] );
		Assert.AreEqual( 5, drawable["Mumbo"] );
		Assert.AreEqual( 8, drawable["Rocky Racers"] );
		Assert.AreEqual( 40, drawable["Tom Tom Twister"] );
		Assert.AreEqual( 16, drawable["Eruption"] );
	}

	/// <summary>
	/// The rides that show their riders through WALKON's head action 4 (no ADDHEAD): each has a head table, and the head id operand of that WALKON
	/// (a counter variable, or Inca Totem's) reaches a node of the model, so the rider's head can be drawn.
	/// </summary>
	[TestMethod]
	public void RidesThatWalkRidersOnWithTheHeadActionHaveHeadTables()
	{
		var rides = Scripted().Where( x => x.Adds == 0 && new RideScriptFile( x.Entry.FileSystem.OpenRead( x.Entry.ScriptPath! ) ).Instructions
			.Any( i => i.Opcode == (ushort)Opcode.WALKON && i.Operands.Count > 5 && i.Operands[5].Kind == RideScriptOperandKind.Literal && i.Operands[5].Value == 4 ) )
			.Select( x => $"{x.Theme}/{x.Entry.DisplayName}" ).OrderBy( x => x ).ToArray();
		CollectionAssert.AreEqual( new[] { "hallow/The Firepit", "jungle/Aztec Mayhem", "jungle/Inca Totem", "space/Hover Bot Havoc", "space/Rock 'n Roll" }, rides );
		foreach ( var ride in Scripted().Where( x => rides.Contains( $"{x.Theme}/{x.Entry.DisplayName}" ) ) )
		{
			Assert.IsNotNull( ride.Space, ride.Entry.DisplayName );
			Assert.IsTrue( Enumerable.Range( 0, ride.Space!.Count ).All( slot => ride.Space.NodeAt( slot ) >= 0 ), ride.Entry.DisplayName );
		}
	}

	/// <summary>
	/// The walk action operand (the sixth) of every WALKON in the shipped root scripts: 1, 2, 4, 5 and 6 occur, always as a literal. Only 4 reaches the head hang in tp.exe (the walk update 0x00557D80 state 1 and WALKOFF 0x005571A0 test it; the other callers of the hang 0x0044B410 are ADDHEAD, the car and the tour paths), so 5 and 6 (stalls, sprays, Devils Disc, Zero G) draw no head.
	/// </summary>
	[TestMethod]
	public void WalkOnActionsOfTheShippedScriptsArePinned()
	{
		var counts = new SortedDictionary<string, int>();
		foreach ( var (_, entry, _, _, _, _) in Scripted() )
		{
			var script = new RideScriptFile( entry.FileSystem.OpenRead( entry.ScriptPath! ) );
			foreach ( var instruction in script.Instructions.Where( x => x.Opcode == (ushort)Opcode.WALKON ) )
			{
				var key = instruction.Operands.Count > 5 && instruction.Operands[5].Kind == RideScriptOperandKind.Literal ? instruction.Operands[5].Value.ToString() : "non-literal";
				counts[key] = counts.GetValueOrDefault( key ) + 1;
			}
		}
		CollectionAssert.AreEqual( new[] { "1=20", "2=2", "4=5", "5=5", "6=15" }, counts.Select( x => $"{x.Key}={x.Value}" ).ToArray() );
	}

	/// <summary>
	/// The shipped scripts of the five head-action rides, run with real riders: heads go up on the ride's head nodes (they were missing, the riders vanished on board)
	/// and come down again when the ride unloads.
	/// </summary>
	[TestMethod]
	public void HeadActionRidesShowTheirRidersAndTakeThemDown()
	{
		foreach ( var (theme, name) in new[] { ("hallow", "The Firepit"), ("jungle", "Inca Totem"), ("jungle", "Aztec Mayhem"), ("space", "Hover Bot Havoc"), ("space", "Rock 'n Roll") } )
		{
			var entry = ObjectCatalog.Load( theme ).Entries.Single( x => x.DisplayName == name );
			var runtime = new OriginalObjectRuntime( entry, seed: 3, open: true );
			var visitors = runtime.Visitors;
			var guests = Enumerable.Range( 1, 12 ).ToArray();
			foreach ( var guest in guests )
				visitors.TryJoinQueue( guest );
			var maximumHung = 0;
			var hungAtEnd = -1;
			for ( var tick = 0; tick < 240 * 60; tick++ )
			{
				runtime.Simulate( 1f / FixedStepClock.TicksPerSecond );
				var hung = visitors.HungHeads().ToArray();
				Assert.AreEqual( hung.Length, hung.Select( head => head.Visitor ).Distinct().Count(), $"{name}: one head per rider" );
				maximumHung = Math.Max( maximumHung, hung.Length );
				hungAtEnd = hung.Length;
			}
			Assert.AreNotEqual( RideVMState.Faulted, runtime.Script!.State, $"{name}: {runtime.Script.FaultMessage}" );
			Assert.IsTrue( visitors.BoardedTotal > 0, $"{name}: riders boarded" );
			Assert.IsTrue( maximumHung > 0, $"{name}: a rider's head was hung ({visitors.BoardedTotal} boarded)" );
			Assert.IsTrue( hungAtEnd <= maximumHung );
			runtime.Stop();
		}
	}

	/// <summary>
	/// A park saved while riders sit on a head-action ride and loaded again keeps their heads (the head table and the walk slots are saved), and the loaded ride takes them down at
	/// WALKOFF exactly as the uninterrupted one does. A snapshot without the walk slots (an older save) loads too: its riders keep the heads they had and the ride unloads them.
	/// </summary>
	[TestMethod]
	public void HeadsAndWalkSlotsSurviveASaveInTheMiddleOfTheRide()
	{
		foreach ( var (theme, name) in new[] { ("hallow", "The Firepit"), ("jungle", "Inca Totem"), ("jungle", "Aztec Mayhem"), ("space", "Hover Bot Havoc"), ("space", "Rock 'n Roll") } )
		{
			var entry = ObjectCatalog.Load( theme ).Entries.Single( x => x.DisplayName == name );
			var world = new RideScriptWorld();
			var runtime = new OriginalObjectRuntime( entry, world, seed: 3, open: true );
			for ( var guest = 1; guest <= 12; guest++ )
				runtime.Visitors.TryJoinQueue( guest );
			var saveTick = -1;
			for ( var tick = 0; tick < 240 * 60 && saveTick < 0; tick++ )
			{
				runtime.Simulate( 1f / FixedStepClock.TicksPerSecond );
				if ( runtime.Visitors.HungHeads().Count() >= Math.Min( 3, runtime.Visitors.Heads!.Count ) )
					saveTick = tick;
			}
			Assert.IsTrue( saveTick >= 0, $"{name}: riders sat on the ride" );
			var snapshot = runtime.CaptureSnapshot();
			var worldSnapshot = world.CaptureSnapshot();
			// a ride outside a park has no queue cells of its own; a saved park always has them
			var visitorSnapshot = runtime.Visitors.CaptureSnapshot() with { QueueCells = new[] { (X: 3, Y: 4) } };
			Assert.IsNotNull( visitorSnapshot.WalkSlots, $"{name}: the walk slots are saved" );

			var restored = new RideScriptWorld();
			var candidate = OriginalObjectRuntime.CreateForRestore( entry, restored, seed: 3, open: true, attractionId: snapshot.AttractionId );
			var gaps = candidate.RestoreSnapshot( snapshot );
			Assert.AreEqual( 0, gaps.Count, string.Join( "; ", gaps ) );
			restored.RestoreSnapshot( worldSnapshot, new Dictionary<int, RideVM> { [runtime.Script!.ScriptId] = candidate.Script! } );
			candidate.Visitors.RestoreSnapshot( visitorSnapshot );
			CollectionAssert.AreEquivalent( runtime.Visitors.HungHeads().ToArray(), candidate.Visitors.HungHeads().ToArray(), $"{name}: heads after loading" );

			for ( var tick = 0; tick < 120 * 60; tick++ )
			{
				runtime.Simulate( 1f / FixedStepClock.TicksPerSecond );
				candidate.Simulate( 1f / FixedStepClock.TicksPerSecond );
				CollectionAssert.AreEquivalent( runtime.Visitors.HungHeads().ToArray(), candidate.Visitors.HungHeads().ToArray(), $"{name}: heads at tick {tick} after loading" );
			}

			// An older save has no walk slots (the head table is saved with the ride): it must load, and its riders' heads still go down when they leave (Release clears a leaving rider's head).
			var legacyWorld = new RideScriptWorld();
			var legacy = OriginalObjectRuntime.CreateForRestore( entry, legacyWorld, seed: 3, open: true, attractionId: snapshot.AttractionId );
			Assert.AreEqual( 0, legacy.RestoreSnapshot( snapshot ).Count, $"{name}: the ride of a save without walk slots loads" );
			legacyWorld.RestoreSnapshot( worldSnapshot, new Dictionary<int, RideVM> { [runtime.Script!.ScriptId] = legacy.Script! } );
			legacy.Visitors.RestoreSnapshot( visitorSnapshot with { WalkSlots = null } );
			for ( var tick = 0; tick < 120 * 60; tick++ )
			{
				legacy.Simulate( 1f / FixedStepClock.TicksPerSecond );
			}
			Assert.AreNotEqual( RideVMState.Faulted, legacy.Script!.State, $"{name}: {legacy.Script.FaultMessage}" );
			Assert.AreEqual( 0, legacy.Visitors.HungHeads().Count(), $"{name}: the heads of a save without walk slots are down again after the ride unloads" );
			runtime.Stop();
			candidate.Stop();
			legacy.Stop();
		}
	}

	/// <summary>Every head record of every ride and car model has the matrix flag 0x30 and a node inside the model, and the first record of an id wins.</summary>
	[TestMethod]
	public void EveryHeadRecordHasAMatrixAndAValidNode()
	{
		var checkedRecords = 0;
		foreach ( var (theme, entry, _, _, _, model) in Scripted() )
		{
			for ( var index = 0; index < model.DummyAttributes.Count; index++ )
			{
				var attribute = model.DummyAttributes[index];
				if ( (attribute.Type & RideHeadSpace.HeadFlag) == 0 )
					continue;
				checkedRecords++;
				Assert.AreNotEqual( 0u, attribute.Type & 0x30, $"{theme}/{entry.DisplayName} record {index} type {attribute.Type:x}" );
				var node = model.Counts.Unknown46 + index;
				Assert.IsTrue( node < model.Nodes.Count, $"{theme}/{entry.DisplayName} record {index} node {node}" );
			}
		}
		Assert.IsTrue( checkedRecords > 100 );
		foreach ( var wad in CarWads() )
		{
			foreach ( var (name, model) in CarModels( wad ) )
				for ( var index = 0; index < model.DummyAttributes.Count; index++ )
				{
					var attribute = model.DummyAttributes[index];
					if ( (attribute.Type & RideHeadSpace.HeadFlag) == 0 )
						continue;
					Assert.AreNotEqual( 0u, attribute.Type & 0x30, $"{name} record {index}" );
					Assert.IsTrue( model.Counts.Unknown46 + index < model.Nodes.Count, $"{name} record {index}" );
				}
		}
	}

	private static string[] CarWads()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		return new[] { "coaster1.wad", "coaster3.wad", "minecart.wad", "tourride.wad", "monkey.wad" }
			.SelectMany( name => new[] { "jungle", "hallow" }.Select( theme => Path.Combine( dataPath, "levels", theme, "rides", name ) ) )
			.Where( File.Exists ).ToArray();
	}

	private static IEnumerable<(string Name, ModelFile Model)> CarModels( string wad )
	{
		using var archive = new WadArchive( wad );
		foreach ( var member in archive.Root.Children.OfType<ArchiveFile>().Where( x => x.Name!.EndsWith( ".MD2", StringComparison.OrdinalIgnoreCase ) ) )
			yield return ($"{Path.GetFileName( wad )}/{member.Name}", new ModelFile( new MemoryStream( member.GetData() ) ));
	}

	/// <summary>The traced record-to-node mapping on the known models: monkey.MD2 id 3 is node 10 Head02 and id 5 Head06; Bird.MD2 id 9 is node 2 Head03.</summary>
	[TestMethod]
	public void KnownModelsMapHeadIdsToTheirTracedNodes()
	{
		var all = CarWads().SelectMany( CarModels ).ToArray();
		ModelFile Find( string name ) => all.First( x => x.Name.EndsWith( "/" + name, StringComparison.OrdinalIgnoreCase ) ).Model;
		var monkey = Find( "monkey.MD2" );
		Assert.AreEqual( 10, RideHeadSpace.NodeForHead( monkey, 3 ) );
		Assert.AreEqual( "Head02", monkey.Nodes[10].Name.Trim() );
		var five = RideHeadSpace.NodeForHead( monkey, 5 );
		Assert.AreEqual( "Head06", monkey.Nodes[five].Name.Trim() );
		var bird = Find( "Bird.MD2" );
		Assert.AreEqual( 2, RideHeadSpace.NodeForHead( bird, 9 ) );
		Assert.AreEqual( "Head03", bird.Nodes[2].Name.Trim() );
		Assert.AreEqual( RideHeadSpace.NoRecord, RideHeadSpace.NodeForHead( bird, 10 ) );
	}

	/// <summary>The head sprite sets are 7 direction rows of 8 frames: the 56-frame binning of the presentation lines up with the ESP slot 0.</summary>
	[TestMethod]
	public void HeadArtIsSevenRowsOfEightFrames()
	{
		foreach ( var atlas in GuestSpriteAtlas.LoadKidHeads() )
		{
			var slot = atlas.Animations.Animations[GuestSpriteAtlas.StandSlot];
			Assert.AreEqual( 0, slot.FirstFrame, atlas.Name );
			Assert.AreEqual( HeadPresentation.Columns, slot.FramesPerDirection, atlas.Name );
			Assert.AreEqual( HeadPresentation.Rows, slot.Directions, atlas.Name );
			Assert.IsTrue( atlas.Frames.Length >= HeadPresentation.Rows * HeadPresentation.Columns - 1, atlas.Name );
		}
	}

	/// <summary>Head space of the tube and Space Balls' models: Tubes Of Zob runs ADDHEAD but its model has no head id 1, so its table is null.</summary>
	[TestMethod]
	public void TubesOfZobRunsAddHeadWithoutAHeadTable()
	{
		var zob = Scripted().Single( x => x.Entry.DisplayName == "Tubes Of Zob" );
		Assert.IsTrue( zob.Adds > 0 );
		Assert.IsNull( zob.Space );
	}

	/// <summary>The head sets (<c>esprites/Generic/Kidsheads</c>) load from their TPC banks, one per kid type, with frames.</summary>
	[TestMethod]
	public void HeadSpriteSetsLoadFromTheirTpcBanks()
	{
		var heads = GuestSpriteAtlas.LoadKidHeads();
		var kids = GuestSpriteAtlas.LoadKids();
		Assert.AreEqual( kids.Count, heads.Count );
		Assert.IsTrue( heads.All( atlas => atlas.Frames.Length > 0 && atlas.PackKey.StartsWith( "esprites/generic/kidsheads/", StringComparison.Ordinal ) ) );
		Assert.IsTrue( heads.All( atlas => atlas.FramesPerDirection( GuestSpriteAtlas.StandSlot ) >= 1 ) );
	}

	/// <summary>
	/// Coaster and tour car models (their WADs in the jungle and hallow rides folders) and their head records. The Mac car
	/// setup (0x100303A8) counts the head records of a car type and prints "Car type %s can receive %d heads"; the car
	/// heads are not placed by a ride script (docs/exe/park.md: the cars are records with a handle and no head table).
	/// </summary>
	[TestMethod]
	public void CarModelsCarryHeadRecordsOrNot()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		var wads = new[] { "coaster1.wad", "coaster3.wad", "minecart.wad", "tourride.wad" }
			.SelectMany( name => new[] { "jungle", "hallow" }.Select( theme => Path.Combine( dataPath, "levels", theme, "rides", name ) ) )
			.Where( File.Exists ).ToArray();
		var found = new List<string>();
		foreach ( var wad in wads )
		{
			using var archive = new WadArchive( wad );
			foreach ( var member in archive.Root.Children.OfType<ArchiveFile>().Where( x => x.Name!.EndsWith( ".MD2", StringComparison.OrdinalIgnoreCase ) ) )
			{
				var model = new ModelFile( new MemoryStream( member.GetData() ) );
				var records = model.DummyAttributes.Count( attribute => (attribute.Type & RideHeadSpace.HeadFlag) != 0 );
				found.Add( $"{Path.GetFileName( wad )}/{member.Name}={records}" );
			}
		}
		var withRecords = found.Where( entry => !entry.EndsWith( "=0", StringComparison.Ordinal ) ).ToArray();
		CollectionAssert.AreEqual( new[] { "coaster1.wad/CrocCar.MD2=6", "coaster3.wad/ape.MD2=2", "minecart.wad/cart.MD2=6", "tourride.wad/Bird.MD2=9", "tourride.wad/Balloon.MD2=9" }, withRecords );
		Assert.IsTrue( found.Count >= 50 );
	}
	public TestContext TestContext { get; set; } = null!;

	/// <summary>Inspect every associated RSE, including sound and resolved child scripts, rather than only entry.ScriptPath.</summary>
	[TestMethod]
	public void AssociatedChildAndSoundScriptsAreIncludedInHeadCommandInventory()
	{
		var counts = new List<string>();
		var roots = 0;
		var children = 0;
		foreach ( var theme in Themes )
		{
			var catalog = ObjectCatalog.Load( theme );
			foreach ( var entry in catalog.Entries.Where( entry => entry.Scripts.Count > 0 ).OrderBy( entry => entry.InfoId ) )
			{
				if ( entry.ScriptPath != null )
					Assert.IsTrue( entry.Scripts.Contains( entry.ScriptPath ), $"{theme}/{entry.DisplayName}: associated scripts contain the root" );
				foreach ( var path in entry.Scripts.Distinct( StringComparer.OrdinalIgnoreCase ) )
				{
					using var stream = entry.FileSystem.OpenRead( path );
					var script = new RideScriptFile( stream );
					var adds = script.Instructions.Count( instruction => instruction.Opcode == (ushort)Opcode.ADDHEAD );
					var dels = script.Instructions.Count( instruction => instruction.Opcode == (ushort)Opcode.DELHEAD );
					var root = string.Equals( path, entry.ScriptPath, StringComparison.OrdinalIgnoreCase );
					if ( root ) roots++; else children++;
					if ( adds != 0 || dels != 0 )
						counts.Add( $"{theme}/{entry.DisplayName}/{Path.GetFileName( path )}: {(root ? "root" : "associated")} ADDHEAD={adds} DELHEAD={dels}" );
				}
			}
		}
		Assert.AreEqual( 262, roots, "all associated root scripts in the four-theme catalog" );
		Assert.AreEqual( 46, children, "all associated sound/child scripts, including scripts without a root" );
		TestContext.WriteLine( $"Head inventory: {roots} roots, {children} associated scripts; {counts.Count} scripts with head commands.\n{string.Join( "\n", counts )}" );
		Assert.AreEqual( 31, counts.Count, "the broader shipped corpus has the same 31 head-command roots" );
		Assert.IsTrue( counts.All( count => count.EndsWith( "root ADDHEAD=1 DELHEAD=1", StringComparison.Ordinal ) ), "none of the 46 associated child/sound scripts contains a head command" );
	}

}
