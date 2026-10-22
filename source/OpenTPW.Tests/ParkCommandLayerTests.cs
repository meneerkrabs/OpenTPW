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
		new( "Developer prototype run", @"\bPlacedRide\??\.(Start|Stop|Open|Close)\s*\(", nameof( SetPrototypeRunningCommand ) ),
		// the winning screen
		new( "Level.DismissResearchFinale / ParkFinale.Dismiss", @"(\.\s*|" + BareCall + @")(DismissResearchFinale|ParkFinale\.Dismiss)\s*\(", nameof( DismissFinaleCommand ) ),
		// paths and queues
		new( "Level.BuildPath", Call( "BuildPath" ), nameof( LayLineCommand ) ),
		new( "Level.RemovePathCell", Call( "RemovePathCell" ), nameof( RemovePathCellCommand ) ),
		new( "Level.BuildQueueCell", Call( "BuildQueueCell" ), nameof( LayLineCommand ) ),
		new( "Level.RemoveQueueCell", Call( "RemoveQueueCell" ), nameof( RemoveQueueCellCommand ) ),
		new( "Level.BacktrackAt", Call( "BacktrackAt" ), nameof( BacktrackCommand ) ),
		new( "Level.Backtrack", Call( "Backtrack" ), nameof( BacktrackCommand ) ),
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
		// any receiver (a name, a call's or an index's result, null-forgiving or not) but the HUD's own panels (the locator, the camera slide) and a layout builder's track
		// the co-op link's open and close (OnlineSession's Coop) are a socket's, not a ride's
		new( "Ride open and close", @"([)\]]|\b(?!(Locator|CameraSlide|Track|Coop)\b)\w+)!?\??\.(Open|Close)\(\s*\)", nameof( SetAttractionOpenCommand ) ),
		new( "ParkEconomy.SetObjectOpen", Call( "SetObjectOpen" ), nameof( SetAttractionOpenCommand ) ),
		new( "Ride window settings", Call( "ApplyRideWindow" ), nameof( SetRideSettingsCommand ) ),
		new( "Ride setters", @"\.(SetSpeed|SetCapacity|SetDuration)\s*\(", nameof( SetRideSettingsCommand ) ),
		new( "Staff rename", @"\.Rename\s*\(", nameof( RenameStaffCommand ) ),
		new( "Patrol areas", @"\.(SetPatrolArea|ClearPatrolArea)\s*\(", nameof( SetPatrolAreaCommand ) ),
		// any speed but the HUD status's own (a park without an economy keeps its speed there)
		new( "Game speed", @"([)\]]|\b(?!Status\b)\w+)!?\??\.Speed\s*=[^=>]", nameof( SetGameSpeedCommand ) ),
		new( "Research lab (direct)", @"\.SetEffort\s*\(", nameof( SetResearchEffortCommand ) ),
		new( "Challenge slots", @"\.(AcceptAt|DeclineAt)\s*\(", nameof( AnswerChallengeCommand ) ),
		new( "Cheat queue (direct)", @"\bCheats!?\??\.Queue\s*\(", nameof( QueueCheatCommand ) ),
		// a method group handed on (a button's click, a callback) reaches the economy's mutators as surely as a call
		new( "Settings mutators as method groups", @"(?<!InputButton)\.(OpenPark|ClosePark|SetEntranceFee|SetPrices|SetShopSettings|TakeLoan|TryRepayLoan|SetTrainingBudget|SetResearchEffort|AcceptChallenge|DeclineChallenge|RequestMaintenance|TryBuyUpgrade|TryCancelUpgrade|QueueCheat|RefreshLoanOffers)\b(?!\s*[(=])", nameof( ParkSettingCommand ) ),
		new( "Loan list build", Call( "RefreshLoanOffers" ), nameof( RefreshLoanOffersCommand ) ),
		// the park's tools (C1 part 3)
		new( "Land", @"\.(ChargeLand|TryBuyCells|SetLand|ClaimEnclosedCells)\s*\(", nameof( BuyLandCommand ) ),
		// the staff placement's own helpers are bare calls inside its file, so the bare form counts too
		new( "ParkEconomy.Hire and the staff placement", @"\.(Hire|EndPlacement)\s*\(|" + Call( "PutDown" ) + "|" + Call( "ReleaseAssignments" ) + @"|\b(PendingPlacementId|PatrolArea|BubbleThought)\s*=[^=]",
			nameof( PlaceStaffCommand ) ),
		new( "Staff pick-up", @"\bState\s*=\s*StaffState\.PickedUp\b", nameof( PickUpStaffCommand ) ),
		new( "Pools", @"\.(SetPoolWaves|CyclePoolWaves|PaintCells|EraseCells|Paint|Erase)\s*\(", nameof( PaintPoolCellCommand ) ),
		new( "Pool river flags", @"\.Cells\??\.SetFlags\s*\(", nameof( UndoPoolStrokeCommand ) ),
		new( "Pool slides", @"\bSlides\??!?\.(AddCell|RemoveLastPiece)\s*\(", nameof( LayPoolSlidePieceCommand ) ),
		new( "Pool bridges", @"\bBridges\??!?\.(TryAdd|RemoveAt)\s*\(", nameof( PlacePoolBridgeCommand ) ),
		// the cell tool commits every line through the writer it holds, a command writer for paths, queues and tracks alike; a track write of its own is refused
		new( "Kart and water tracks", @"\.(BuildTrack|PlaceTile|RemoveTile|UndoLine|RepairStation|FindOpenEnd)\s*\(|\bnew\s+TrackLineWriter\s*\(|\.Cut\s*\([^)]*commit\s*:", nameof( LayTrackLineCommand ) ),
		// the coaster tool: its builder's writes (the panel and the HUD hold the builder) and the controller's tool state
		// any name a coaster builder goes by in the tool layer (the panel's builder, the level's coaster tool, the HUD's tool), its track and its ride, null-forgiving or not
		new( "Coaster builder writes", @"\b(builder|Builder|tool|CoasterTool)[!?]*\.(Place|RemoveLast|Complete|CloseCircuit|Move|SetHeight|EndEdit|FinishEdit|ApplyEdit|EditLoft|EditYaw|EditBank|EditIncline|EditLoopRotate|EditMove|SwapStack|Cut|Reopen|Delete|OpenFailingClosure|SetStation)\s*\("
			+ @"|\b(Track|Ride)[!?]*\.(Append|Replace|Relocate|RemoveLast|Cut|SwapStack|Clear|Complete|Close|OpenClosure|Reopen|Revalidate|SetStation|EnterState|ClearTrains|Rebuild)\s*\(|" + Call( "DeleteCoasterTrack" ) + "|" + Call( "AfterCoasterClick" ),
			nameof( CoasterClickCommand ) ),
		new( "Coaster tool state", @"\.EnterTool\s*\(|\bBeingEdited\s*=[^=]|" + Call( "SetBeingEdited" ) + "|" + Call( "ExecuteBeginCoasterTool" ) + "|" + Call( "ExecuteEndCoasterTool" ), nameof( BeginCoasterToolCommand ) ),
	};

	/// <summary>
	/// The mutators the tool layer still calls directly, with the phase of C1 that converts them (docs/COOP.md, "Converted and remaining"). Each still has a call in the tool
	/// layer (the guard checks it, so a converted one must move to <see cref="Converted"/>).
	/// </summary>
	internal static readonly (Mutator Mutator, string Phase)[] Remaining =
	{
		(new( "Developer sandbox load", Call( "LoadSandbox" ), "LoadSandbox" ), "never in a session (a load replaces the park; the developer sandbox is never shared, CoopSession.CanShare)"),
	};

	/// <summary>The starts of a bare call: a line start, an operator or bracket, or a keyword before an expression (a declaration has a type before the name instead).</summary>
	private const string BareCall = @"(^|[=(,!&|:;{}]|=>|\breturn|\belse|\?\?)\s*";

	/// <summary>A call of the method <paramref name="name"/>: a member access (<c>x.Name(</c>, <c>x?.Name(</c>) or a bare call.</summary>
	private static string Call( string name ) => $@"(\.\s*|{BareCall}){name}\s*\(";

	/// <summary>A call of the level's method <paramref name="name"/> whose name lists share (<c>RemoveAt</c>): through a level reference, or bare inside the level.</summary>
	private static string LevelCall( string name ) => $@"(\b(level|Level\.Current|this)\??\.\s*|{BareCall}){name}\s*\(";

	/// <summary>The source files of the tool layer: the HUD, the UI, the front end and online screens (not their smoke harnesses), the tools and the level's own input file.</summary>
	private static IReadOnlyList<string> ToolLayer( string source )
	{
		IEnumerable<string> All( string folder ) => Directory.EnumerateFiles( Path.Combine( source, folder ), "*.cs", SearchOption.AllDirectories );
		// a visit's park is built from its package as a save's restore builds a park (SharedParkLayout: the objects, paths and coasters of a read-only copy), not edited by a tool; the co-op
		// link (CoopLink) is the socket under the shared park screens, whose opens and closes are its sockets'
		var files = All( "Hud" ).Concat( All( "UI" ) ).Concat( All( "FrontEnd" ) ).Concat( All( "Online" ) )
			.Where( path => !Path.GetFileName( path ).Contains( "SmokeTest", StringComparison.Ordinal ) && Path.GetFileName( path ) is not ("SharedParkLayout.cs" or "CoopLink.cs") )
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
				Path.Combine( "World", "Coasters", "CoasterToolPanel.cs" ),
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
		var finale = Converted.Single( mutator => mutator.Name == "Level.DismissResearchFinale / ParkFinale.Dismiss" );
		Assert.IsTrue( Caught( finale, "level.DismissResearchFinale();" ) );
		Assert.IsTrue( Caught( finale, "var removed = ParkFinale.Dismiss( level.Objects );" ) );
		Assert.IsFalse( Caught( finale, "public bool DismissResearchFinale() => SupportsResearchFinale;" ) );
		Assert.IsFalse( Caught( finale, "level.Commands.Issue( new DismissFinaleCommand() );" ) );
		var open = Converted.Single( mutator => mutator.Name == "Ride open and close" );
		Assert.IsTrue( Caught( open, "item.Close();" ) );
		Assert.IsTrue( Caught( open, "selectedObject!.Open();" ) );
		Assert.IsTrue( Caught( open, "objects.Objects[0].Open();" ) );
		Assert.IsTrue( Caught( open, "level.FindEconomyObject( 3 )?.Close();" ) );
		Assert.IsFalse( Caught( open, "Locator.Close();" ), "the HUD's own panel" );
		Assert.IsFalse( Caught( open, "level.Commands.Issue( new SetAttractionOpenCommand( id, true ) );" ) );
		var speed = Converted.Single( mutator => mutator.Name == "Game speed" );
		Assert.IsTrue( Caught( speed, "park.Speed = GameSpeed.Paused;" ) );
		Assert.IsTrue( Caught( speed, "level.Park!.Economy.Speed = speed;" ) );
		Assert.IsFalse( Caught( speed, "Status.Speed = speed;" ), "a park without an economy keeps its speed in the HUD" );
		Assert.IsFalse( Caught( speed, "if ( economy.Speed == GameSpeed.Paused )" ) );
		var rename = Converted.Single( mutator => mutator.Name == "Staff rename" );
		Assert.IsTrue( Caught( rename, "economy.Staff.Rename( member, text );" ) );
		Assert.IsTrue( Caught( rename, "park!.Staff.Rename( member, text )" ) );
		var groups = Converted.Single( mutator => mutator.Name == "Settings mutators as method groups" );
		Assert.IsTrue( Caught( groups, "Clicked = economy.OpenPark," ) );
		Assert.IsTrue( Caught( groups, "Action take = park.RefreshLoanOffers;" ) );
		Assert.IsFalse( Caught( groups, "economy.OpenPark();" ), "a call is the call rows' to catch" );
		Assert.IsFalse( Caught( groups, "if ( economy.IsParkOpen )" ) );
		Assert.IsFalse( Caught( groups, "if ( Input.Pressed( InputButton.OpenPark ) )" ), "the park door's key binding" );
		var place2 = Converted.Single( mutator => mutator.Name == "Level.PlaceObject" );
		Assert.IsTrue( Caught( place2, "private OriginalObject? Build() => PlaceObject( entry, x, y, 0 );" ), "an expression body's call" );
		var prototype = Converted.Single( mutator => mutator.Name == "Developer prototype run" );
		Assert.IsTrue( Caught( prototype, "level.PlacedRide.Stop();" ) );
		Assert.IsTrue( Caught( prototype, "level.PlacedRide?.Start();" ) );
		Assert.IsFalse( Caught( prototype, "ImGui.Text( level.PlacedRide.IsOpen ? \"Ride: open\" : \"Ride: closed\" );" ) );
		// the tools (C1 part 3)
		var land = Converted.Single( mutator => mutator.Name == "Land" );
		Assert.IsTrue( Caught( land, "map.SetLand( merged );" ) );
		Assert.IsTrue( Caught( land, "merged.ClaimEnclosedCells( map );" ) );
		Assert.IsTrue( Caught( land, "economy()?.ChargeLand( check.Cost, check.Cells );" ) );
		Assert.IsFalse( Caught( land, "var check = land.CheckPurchase( map, corner, post, barrier, cost, balance );" ) );
		var staff = Converted.Single( mutator => mutator.Name == "ParkEconomy.Hire and the staff placement" );
		Assert.IsTrue( Caught( staff, "member = park.Hire( candidate.Id );" ) );
		Assert.IsTrue( Caught( staff, "park.Staff.PendingPlacementId = candidate.Id;" ) );
		Assert.IsTrue( Caught( staff, "member.PatrolArea = (0, 0, 127, 127);" ) );
		Assert.IsFalse( Caught( staff, "if ( member.PatrolArea == area )" ) );
		Assert.IsFalse( Caught( staff, "if ( !issue( new BeginStaffHireCommand( candidate.Id ) ).Accepted )" ) );
		Assert.IsTrue( Caught( staff, "PutDown( member, x, y );" ), "the placement's helper, called bare inside its own file" );
		Assert.IsTrue( Caught( staff, "ReleaseAssignments( park, member );" ) );
		Assert.IsTrue( Caught( staff, "StaffPlacement.PutDown( member, X, Y );" ) );
		Assert.IsFalse( Caught( staff, "internal static void PutDown( StaffMember member, int x, int y )" ), "the helper's declaration" );
		Assert.IsTrue( Caught( staff, "member.BubbleThought = 0;" ) );
		Assert.IsFalse( Caught( staff, "if ( member.BubbleThought != 0 && staffCell( member ) is { } cell )" ) );
		var pickUp = Converted.Single( mutator => mutator.Name == "Staff pick-up" );
		Assert.IsTrue( Caught( pickUp, "member.State = StaffState.PickedUp;" ) );
		Assert.IsFalse( Caught( pickUp, "if ( member.State == StaffState.PickedUp )" ) );
		var pools = Converted.Single( mutator => mutator.Name == "Pools" );
		Assert.IsTrue( Caught( pools, "var result = IsErasing ? builder.Erase( x, y ) : builder.Paint( x, y, IsRiver );" ) );
		Assert.IsTrue( Caught( pools, "level.SetPoolWaves( item, chosen );" ) );
		Assert.IsFalse( Caught( pools, "level.Commands.Issue( new SetPoolWavesCommand( id, true, 2, 1 ) );" ) );
		var river = Converted.Single( mutator => mutator.Name == "Pool river flags" );
		Assert.IsTrue( Caught( river, "builder.Cells.SetFlags( x, y, flags );" ) );
		Assert.IsFalse( Caught( river, "built.SetFlags( cell.X, cell.Y, flags );" ), "a visit's own copy of the shared map" );
		var tracks = Converted.Single( mutator => mutator.Name == "Kart and water tracks" );
		Assert.IsTrue( Caught( tracks, "level.BuildTrack( station, (14, 10), (20, 10) );" ) );
		Assert.IsTrue( Caught( tracks, "var writer = new TrackLineWriter( builder, changed, owner );" ) );
		Assert.IsTrue( Caught( tracks, "var cut = editor.Cut( a, b, commit: true );" ) );
		Assert.IsTrue( Caught( tracks, "var open = editor.FindOpenEnd();" ), "the open-end walk writes the tiles' overlap words" );
		Assert.IsFalse( Caught( tracks, "var result = writer.Commit( start, end );" ), "the cell tool commits through the command writer it holds" );
		var coaster = Converted.Single( mutator => mutator.Name == "Coaster builder writes" );
		Assert.IsTrue( Caught( coaster, "builder.RemoveLast();" ) );
		Assert.IsTrue( Caught( coaster, "var result = builder.Cut( first, index );" ) );
		Assert.IsTrue( Caught( coaster, "builder.EditLoft( edit, value );" ) );
		Assert.IsFalse( Caught( coaster, "builder.LoopMode = false;" ), "the panel's loop edit mode is its own state" );
		Assert.IsFalse( Caught( coaster, "if ( builder.BeginEdit( node ) is not { } edit )" ), "a press notes the pylon and writes nothing" );
		Assert.IsTrue( Caught( coaster, "builder!.RemoveLast();" ), "a null-forgiving receiver" );
		Assert.IsTrue( Caught( coaster, "level.CoasterTool?.RemoveLast();" ) );
		Assert.IsTrue( Caught( coaster, "var result = panel.Builder.Cut( first, index );" ) );
		Assert.IsTrue( Caught( coaster, "ReportCoasterResult( tool.Place( cursorX, cursorY ), closing );" ) );
		Assert.IsTrue( Caught( coaster, "builder.Track.Replace( index, node );" ) );
		Assert.IsTrue( Caught( coaster, "station.Runtime.Coaster!.Ride.Rebuild();" ) );
		Assert.IsTrue( Caught( coaster, "if ( closed && !builder.Track.Close() )" ), "the track's own close and its validator's marks" );
		Assert.IsTrue( Caught( coaster, "builder.Track.OpenClosure();" ) );
		Assert.IsTrue( Caught( coaster, "builder.Track.Revalidate( builder.Site, builder.Ride.World, builder.Ride );" ) );
		Assert.IsTrue( Caught( coaster, "level.DeleteCoasterTrack( station );" ) );
		Assert.IsFalse( Caught( coaster, "var closing = level.CoasterTool is { } tool && tool.Track.IsChainStart( cursorX, cursorY ) && !tool.Track.IsClosed;" ), "the HUD reads the track" );
		Assert.IsFalse( Caught( coaster, "if ( builder.DragRange( node, dragKind ) is not { } range )" ) );
		var toolState = Converted.Single( mutator => mutator.Name == "Coaster tool state" );
		Assert.IsTrue( Caught( toolState, "builder.Ride.EnterTool();" ) );
		Assert.IsTrue( Caught( toolState, "ride.BeingEdited = true;" ) );
		Assert.IsFalse( Caught( toolState, "if ( ride.BeingEdited == false )" ) );
		Assert.IsTrue( Caught( toolState, "SetBeingEdited( station, true );" ) );
		Assert.IsTrue( Caught( toolState, "level.ExecuteEndCoasterTool( id );" ) );
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
