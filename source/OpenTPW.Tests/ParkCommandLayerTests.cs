using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] The command layer's static guard (docs/COOP.md, "The guard"): no HUD window, screen or tool calls a park mutator directly; each edit leaves the
/// tool layer as a <see cref="ParkCommand"/>. The guard reads the source of the tool layer and refuses a call of any mutator the layer has converted; the mutators not yet converted
/// are listed with the phase that converts them, and that list may only shrink.
/// </summary>
[TestClass]
public class ParkCommandLayerTests
{
	/// <summary>A mutator the tool layer must not call: the pattern of its call and the command that replaces it.</summary>
	internal sealed record Mutator( string Name, string Pattern, string Command );

	/// <summary>The converted mutators: their calls are refused in the tool layer.</summary>
	internal static readonly Mutator[] Converted =
	{
		// objects
		new( "Level.PlaceObject", Call( "PlaceObject" ), nameof( PlaceObjectCommand ) ),
		new( "Level.PlaceBuildTarget", Call( "PlaceBuildTarget" ), nameof( PlaceObjectCommand ) ),
		new( "Level.PlaceAddOn", Call( "PlaceAddOn" ), nameof( PlaceObjectCommand ) ),
		new( "Level.RemoveObjectAt", Call( "RemoveObjectAt" ), nameof( RemoveObjectAtCommand ) ),
		new( "Level.RemoveAt", LevelCall( "RemoveAt" ), nameof( RemoveAtCommand ) ),
		new( "Level.RemoveAttraction", Call( "RemoveAttraction" ), nameof( RemoveAttractionCommand ) ),
		new( "Level.DemolishObjectAt", Call( "DemolishObjectAt" ), nameof( RemoveObjectAtCommand ) ),
		new( "ParkObjects.Remove / TryPlace", @"\bObjects\??\.(Remove|TryPlace)\s*\(", nameof( RemoveAttractionCommand ) ),
		new( "Level.PlaceRide", Call( "PlaceRide" ), nameof( PlacePrototypeCommand ) ),
		new( "Level.RemoveRide", Call( "RemoveRide" ), nameof( RemovePrototypeCommand ) ),
		// paths and queues
		new( "Level.BuildPath", Call( "BuildPath" ), nameof( LayLineCommand ) ),
		new( "Level.RemovePathCell", Call( "RemovePathCell" ), nameof( RemovePathCellCommand ) ),
		new( "Level.BuildQueueCell", Call( "BuildQueueCell" ), nameof( LayLineCommand ) ),
		new( "Level.RemoveQueueCell", Call( "RemoveQueueCell" ), nameof( RemoveQueueCellCommand ) ),
		new( "Level.BacktrackAt", Call( "BacktrackAt" ), nameof( BacktrackCommand ) ),
		new( "ParkPathBuilder writes", @"\bPaths\??\.(BuildSegment|TryLayLine|Remove|RemoveFixed|RemoveOnce|Undo|Commit)\s*\(", nameof( LayLineCommand ) ),
		new( "QueuePaths writes", @"\bQueuePaths\.(TryExtend|RemoveFrom|DetachFromPath|Connect)\s*\(", nameof( EditQueueCommand ) ),
		// the park's settings (C1 part 2)
		new( "ParkEconomy.OpenPark / ClosePark", @"\.(OpenPark|ClosePark)\s*\(", nameof( SetParkOpenCommand ) ),
		new( "ParkEconomy.SetEntranceFee", Call( "SetEntranceFee" ), nameof( SetEntranceFeeCommand ) ),
		new( "ParkEconomy.SetPrices / SetShopSettings", @"\.(SetPrices|SetShopSettings)\s*\(", nameof( SetPriceCommand ) ),
		new( "ParkEconomy.TakeLoan / TryRepayLoan", @"\.(TakeLoan|TryRepayLoan)\s*\(", nameof( TakeLoanCommand ) ),
		new( "ParkEconomy.Fire", @"\.Fire\s*\(", nameof( FireStaffCommand ) ),
		new( "ParkEconomy.SetTrainingBudget", Call( "SetTrainingBudget" ), nameof( SetTrainingBudgetCommand ) ),
		new( "Research effort, order, workload", @"\.(SetResearchEffort|MoveCurrent|SetWorkload)\s*\(", nameof( SetResearchEffortCommand ) ),
		new( "Challenges", @"\.(AcceptChallenge|DeclineChallenge)\s*\(", nameof( AnswerChallengeCommand ) ),
		new( "ParkEconomy.RequestMaintenance", Call( "RequestMaintenance" ), nameof( RequestMaintenanceCommand ) ),
		new( "Ride upgrades", @"\.(TryBuyUpgrade|TryCancelUpgrade)\s*\(", nameof( BuyUpgradeCommand ) ),
		new( "Level.RenamePark", Call( "RenamePark" ), nameof( RenameParkCommand ) ),
		new( "Cheats", Call( "QueueCheat" ), nameof( QueueCheatCommand ) ),
		new( "Ride open and close", @"\b(item|ride)\??\.(Open|Close)\(\s*\)", nameof( SetAttractionOpenCommand ) ),
		new( "Ride window settings", Call( "ApplyRideWindow" ), nameof( SetRideSettingsCommand ) ),
		new( "Staff rename", @"\bStaff\.Rename\s*\(", nameof( RenameStaffCommand ) ),
		new( "Patrol areas", @"\.(SetPatrolArea|ClearPatrolArea)\s*\(", nameof( SetPatrolAreaCommand ) ),
		new( "Game speed", @"\beconomy\.Speed\s*=[^=]", nameof( SetGameSpeedCommand ) ),
	};

	/// <summary>
	/// The mutators the tool layer still calls directly, with the phase of C1 that converts them (docs/COOP.md, "Converted and remaining"). Each still has a call in the tool
	/// layer (the guard checks it, so a converted one must move to <see cref="Converted"/>).
	/// </summary>
	internal static readonly (Mutator Mutator, string Phase)[] Remaining =
	{
		(new( "ParkEconomy.Hire and the staff placement", @"\.(Hire|EndPlacement)\s*\(|\bPendingPlacementId\s*=[^=]", "PlaceStaff" ), "C1 part 3 (tools)"),
		(new( "Land", @"\.(ChargeLand|TryBuyCells|SetLand)\s*\(", "BuyLand" ), "C1 part 3 (tools)"),
		(new( "Pools", @"\.(SetPoolWaves|CyclePoolWaves|PaintCells|EraseCells|Paint|Erase)\s*\(", "PaintPool" ), "C1 part 3 (tools)"),
		(new( "Coasters", Call( "CoasterClick" ), "CoasterClick" ), "C1 part 3 (tools)"),
	};

	/// <summary>The starts of a bare call: a line start, an operator or bracket, or a keyword before an expression (a declaration has a type before the name instead).</summary>
	private const string BareCall = @"(^|[=(,!&|:;{}]|\breturn|\belse|\?\?)\s*";

	/// <summary>A call of the method <paramref name="name"/>: a member access (<c>x.Name(</c>, <c>x?.Name(</c>) or a bare call.</summary>
	private static string Call( string name ) => $@"(\.\s*|{BareCall}){name}\s*\(";

	/// <summary>A call of the level's method <paramref name="name"/> whose name lists share (<c>RemoveAt</c>): through a level reference, or bare inside the level.</summary>
	private static string LevelCall( string name ) => $@"(\b(level|Level\.Current|this)\??\.\s*|{BareCall}){name}\s*\(";

	/// <summary>The source files of the tool layer: the HUD, the UI, the front end and online screens (not their smoke harnesses), the tools and the level's own input file.</summary>
	private static IReadOnlyList<string> ToolLayer( string source )
	{
		IEnumerable<string> All( string folder ) => Directory.EnumerateFiles( Path.Combine( source, folder ), "*.cs", SearchOption.AllDirectories );
		var files = All( "Hud" ).Concat( All( "UI" ) ).Concat( All( "FrontEnd" ) ).Concat( All( "Online" ) )
			.Where( path => !Path.GetFileName( path ).Contains( "SmokeTest", StringComparison.Ordinal ) )
			.Concat( new[]
			{
				Path.Combine( "Client", "ThemeParkIncHud.cs" ),
				Path.Combine( "World", "Level.Input.cs" ),
				Path.Combine( "World", "BuyLandTool.cs" ),
				Path.Combine( "World", "AreaClearTool.cs" ),
				Path.Combine( "World", "CellBuildTool.cs" ),
				Path.Combine( "World", "Staff", "StaffPlacement.cs" ),
				Path.Combine( "World", "Staff", "PatrolAreaTool.cs" ),
				Path.Combine( "World", "Pools", "PoolTool.cs" ),
			}.Select( relative => Path.Combine( source, relative ) ) )
			.ToList();
		foreach ( var file in files )
			Assert.IsTrue( File.Exists( file ), $"the tool layer lists {file}" );
		return files;
	}

	private static string Source()
	{
		var root = new DirectoryInfo( AppContext.BaseDirectory );
		while ( root != null && !File.Exists( Path.Combine( root.FullName, "docs", "DETERMINISM.md" ) ) )
			root = root.Parent;
		if ( root == null )
			Assert.Inconclusive( "Repository sources are not next to the test binaries." );
		return Path.Combine( root!.FullName, "source", "OpenTPW" );
	}

	/// <summary>The code of a line without its comment (a line comment, or a doc comment line).</summary>
	private static string Code( string line )
	{
		var trimmed = line.TrimStart();
		if ( trimmed.StartsWith( "//", StringComparison.Ordinal ) || trimmed.StartsWith( "*", StringComparison.Ordinal ) )
			return "";
		var comment = line.IndexOf( "//", StringComparison.Ordinal );
		return comment >= 0 && !line[..comment].Contains( '"' ) ? line[..comment] : line;
	}

	private static List<string> Calls( IEnumerable<string> files, string source, Mutator mutator )
	{
		var pattern = new Regex( mutator.Pattern, RegexOptions.CultureInvariant );
		return files.SelectMany( path => File.ReadLines( path ).Select( ( line, index ) => (path, line, index) ) )
			.Where( item => pattern.IsMatch( Code( item.line ) ) )
			.Select( item => $"{Path.GetRelativePath( source, item.path )}:{item.index + 1}: {item.line.Trim()}" ).ToList();
	}

	[TestMethod]
	public void NoHudWindowScreenOrToolCallsAConvertedParkMutatorDirectly()
	{
		var source = Source();
		var files = ToolLayer( source );
		var hits = Converted.SelectMany( mutator => Calls( files, source, mutator ).Select( hit => $"{hit}  ({mutator.Name}: issue a {mutator.Command})" ) ).ToList();
		Assert.AreEqual( 0, hits.Count, string.Join( Environment.NewLine, hits ) );
	}

	[TestMethod]
	public void EveryRemainingMutatorIsStillCalledFromTheToolLayerSoTheListOnlyShrinks()
	{
		var source = Source();
		var files = ToolLayer( source );
		var converted = Remaining.Where( row => Calls( files, source, row.Mutator ).Count == 0 ).Select( row => row.Mutator.Name ).ToList();
		Assert.AreEqual( 0, converted.Count, "converted: move to Converted: " + string.Join( ", ", converted ) );
	}

	/// <summary>The guard's own mutation check: each forbidden call form is caught, and the declarations and look-alikes beside them are not.</summary>
	[TestMethod]
	public void TheGuardCatchesEachCallFormAndNotTheDeclarationsOrComments()
	{
		var place = Converted.Single( mutator => mutator.Name == "Level.PlaceObject" );
		var remove = Converted.Single( mutator => mutator.Name == "Level.RemoveAt" );
		var paths = Converted.Single( mutator => mutator.Name == "ParkPathBuilder writes" );
		bool Caught( Mutator mutator, string line ) => new Regex( mutator.Pattern ).IsMatch( Code( line ) );
		Assert.IsTrue( Caught( place, "level.PlaceObject( entry, 1, 2, 0 );" ) );
		Assert.IsTrue( Caught( place, "var placed = level?.PlaceObject(entry, 1, 2, 0);" ) );
		Assert.IsTrue( Caught( place, "PlaceObject( entry, x, y, rotation );" ) );
		Assert.IsTrue( Caught( place, "return Level.Current.PlaceObject ( e, 0, 0, 0 );" ) );
		Assert.IsFalse( Caught( place, "public OriginalObject? PlaceObject( ObjectCatalogEntry entry, int x, int y, int rotation )" ) );
		Assert.IsFalse( Caught( place, "// level.PlaceObject( entry, 1, 2, 0 );" ) );
		Assert.IsFalse( Caught( place, "/// <see cref=\"Level.PlaceObject\"/> builds it" ) );
		Assert.IsFalse( Caught( place, "level.Commands.Issue( new PlaceObjectCommand( key, 1, 2, 0 ) );" ) );
		Assert.IsTrue( Caught( remove, "if ( level.RemoveAt( x, y ) )" ) );
		Assert.IsFalse( Caught( remove, "level.Commands.Issue( new RemoveAtCommand( x, y ) );" ) );
		Assert.IsFalse( Caught( remove, "list.RemoveAt( 0 );" ), "a list's RemoveAt is not the level's" );
		Assert.IsTrue( Caught( remove, "return RemoveAt( x, y );" ) );
		Assert.IsTrue( Caught( paths, "level.Paths.BuildSegment( a, b );" ) );
		Assert.IsTrue( Caught( paths, "level.Paths?.Remove( x, y );" ) );
		Assert.IsFalse( Caught( paths, "level.CellTool.Undo();" ) );
	}

	/// <summary>The command code itself keeps to the simulation's determinism rules (no process-wide randomness, clock or mutable statics), as the simulation folders do.</summary>
	[TestMethod]
	public void TheCommandLayerUsesNoProcessWideRandomnessOrClock()
	{
		var source = Source();
		var forbidden = (Regex)typeof( DeterminismTests ).GetField( "ForbiddenInSimulation", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public )!.GetValue( null )!;
		var files = Directory.EnumerateFiles( Path.Combine( source, "World", "Commands" ), "*.cs" ).Append( Path.Combine( source, "World", "Level.Commands.cs" ) ).Append( Path.Combine( source, "World", "Level.Input.cs" ) );
		var hits = files.SelectMany( path => File.ReadLines( path ).Select( ( line, index ) => (path, line, index) ) ).Where( item => forbidden.IsMatch( item.line ) )
			.Select( item => $"{Path.GetRelativePath( source, item.path )}:{item.index + 1}: {item.line.Trim()}" ).ToList();
		Assert.AreEqual( 0, hits.Count, string.Join( Environment.NewLine, hits ) );
	}
}
