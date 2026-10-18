using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A track add-on against a real <see cref="Level"/> (docs/TRACKS.md "Add-ons"), asset-free: the build tool's target puts a jump on the ride's straight, the walk after the
/// placement links the add-on section, a click on the add-on opens its ride's window, and selling the add-on walks the circuit back to plain sections. The level is assembled
/// without a GPU from its own track set-up, with the footprint handlers wired after it as the level wires them.
/// </summary>
[TestClass, DoNotParallelize]
public class TrackAddOnLevelTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private string root = null!;
	private BaseFileSystem previousFiles = null!;
	private List<Entity> previousEntities = null!;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Initialize()
	{
		previousFiles = FileSystem; previousEntities = Entity.All; previousLog = Log;
		Entity.All = new(); Log = new Logger();
		root = Path.Combine( Path.GetTempPath(), "opentpw-addon-level-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( Path.Combine( root, "synthetic" ) );
		var model = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x42 ), 1 );
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x44 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x268 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x78 ), 0x264 );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( offset ), 0 );
		File.WriteAllBytes( Path.Combine( root, "synthetic/model.MD2" ), model );
		File.WriteAllBytes( Path.Combine( root, "synthetic/script.RSE" ), new RideVMTests.Asm().I( Opcode.END ).Build( Enum.GetNames<RideVariables>() ) );
		FileSystem = new BaseFileSystem( root );
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var entity in Entity.All.ToArray() ) entity.Delete();
		Entity.All = previousEntities; FileSystem = previousFiles; Log = previousLog;
		Directory.Delete( root, true );
	}

	private ObjectCatalogEntry Entry( ObjectCategory category, string sam, params string[] shape ) =>
		new( "jungle", category, "/synthetic", new ObjectSettings( new[] { new ObjectSettingsFile( sam, "synthetic.sam" ) } ), new ObjectShape( shape ), "/synthetic/model.MD2", null,
			"/synthetic/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), FileSystem, null );

	/// <summary>A go-kart ride (the Dino Karts' controller type −4) and its jump (the Dino Karts Jump's footprint and Bumper.*Adjust).</summary>
	private (ObjectCatalogEntry Ride, ObjectCatalogEntry Jump) Entries() =>
		(Entry( ObjectCategory.Ride, "Info.Id 1150\nInfo.Name SyntheticKarts\nInfo.WhichUIType 0\nInfo.IsChoosable 1\nBumper.WhichTrackType 1\nBumper.BumperType -4\n", "*" ),
		Entry( ObjectCategory.Upgrade, "Info.Id 1500\nInfo.Name SyntheticJump\nInfo.WhichUIType 0\nAddOn.UpgradesId 1150\nAddOn.UpgradeType 1\nBumper.WhichTrackType 1\n"
			+ "Bumper.NorthXAdjust 1\nBumper.NorthYAdjust 0\nBumper.EastXAdjust 0\nBumper.EastYAdjust 0\nBumper.SouthXAdjust 0\nBumper.SouthYAdjust -1\nBumper.WestXAdjust 1\nBumper.WestYAdjust -1\n", "++", "++" ));

	private static void Set( object target, string field, object? value ) => typeof( Level ).GetField( field, Private )!.SetValue( target, value );

	/// <summary>
	/// The level with the ride placed at (10, 9) (its station tiles (10, 10) and (12, 10)) and the long kart loop of <see cref="TrackAddOnTests"/>: the straights
	/// (14, 10) … (28, 10) and (28, 14) … (10, 14) between the corners (30, 10), (30, 14), (8, 14) and (8, 10).
	/// </summary>
	private (Level Level, OriginalObject Ride, ObjectCatalogEntry Jump) World()
	{
		var (rideEntry, jump) = Entries();
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), Private, null, new object[] { "jungle", new[] { rideEntry, jump } }, null )!;
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		var objects = new ParkObjects( catalog, new SandboxParkGrid() );
		var guests = new GuestSimulation( new GuestPathGrid( 48, 32 ), GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
		Set( level, "<Objects>k__BackingField", objects );
		Set( level, "<Guests>k__BackingField", guests );
		Set( level, "<LastActionMessage>k__BackingField", "" );
		Set( level, "<CellTool>k__BackingField", new CellBuildTool() );
		Set( level, "trackStations", new Dictionary<OriginalObject, TrackStation>() );
		Set( level, "trackRenderers", new Dictionary<TrackFamily, TrackRenderer>() );
		Set( level, "trackPieceAnimations", new TrackPieceAnimations() );
		Set( level, "waterPieceHandlers", new Dictionary<OriginalObject, Action<bool>>() );
		Set( level, "addOnSurfaces", new Dictionary<ObjectCatalogEntry, TrackSurface?>() );
		typeof( Level ).GetMethod( "SetupTracks", Private )!.Invoke( level, null );
		// the footprint handlers the level wires after its track set-up (Level.ConnectObjectsToGuests), in that order
		var cells = guests.Grid.Cells;
		objects.ObjectPlaced += item => cells.WriteFootprint( item.Cells, Level.OwnerWord( item ), item.Placement.Rotation );
		objects.ObjectRemoved += item => cells.ClearFootprint( item.Cells );
		var ride = objects.TryPlace( rideEntry, 10, 9, 0, out _ );
		Assert.IsNotNull( ride?.Runtime.Bump, "the kart ride and its controller" );
		var station = level.TrackStations[ride!];
		Assert.AreEqual( (10, 10), station.First );
		var writer = new TrackLineWriter( level.KartTracks!, null, level.KartTracks!.OwnerOf( station ) );
		writer.Commit( (14, 10), (30, 10) );
		writer.Commit( (30, 10), (30, 14) );
		writer.Commit( (30, 14), (8, 14) );
		writer.Commit( (8, 14), (8, 10) );
		Assert.IsTrue( level.RewalkTrack( ride!, TrackWalkCause.Line ) is { IsValid: true }, "the loop closes" );
		return (level, ride!, jump);
	}

	private static TrackSection SectionAt( OriginalObject ride, int x, int y ) =>
		ride.Runtime.Bump!.Sections.Single( section => (section.X / TrackSections.Cell, section.Y / TrackSections.Cell) == (x, y) );

	[TestMethod]
	public void AJumpPlacedFromTheToolIsLinkedOpensItsRidesWindowAndItsSaleWalksTheCircuitBack()
	{
		var (level, ride, jump) = World();
		Assert.IsTrue( level.BeginAddOn( jump, ride ) );
		Assert.AreSame( ride, level.BuildAddOnRide );
		// the pointer on the far cell of the straight (16, 10), which flows +X: the hover turns the jump along it (0x005236B0)
		var (x, y, rotation) = level.BuildTarget( jump, 17, 11 );
		Assert.AreEqual( 270, rotation );
		Assert.AreEqual( OriginalPlacementResult.Allowed, level.CheckBuildTarget( jump, x, y, rotation ) );
		var addOn = (OriginalObject?)typeof( Level ).GetMethod( "PlaceAddOnUnpaid", Private )!.Invoke( level, new object[] { jump, x, y, rotation, ride } );
		Assert.IsNotNull( addOn );
		CollectionAssert.AreEquivalent( TrackStation.TileCellsOf( 16, 10 ).ToArray(), addOn!.Cells.Select( cell => (cell.X, cell.Y) ).ToArray(), "the jump covers the hovered tile" );
		var bump = ride.Runtime.Bump!;
		Assert.IsTrue( bump.IsTrackValid );
		// the walk after the placement (0x00524D0A): the flagged straight builds kind 12 and the section points at the jump
		var section = SectionAt( ride, 16, 10 );
		Assert.AreEqual( 12, section.Kind );
		Assert.AreEqual( 1500, section.AddOn?.TypeId );
		Assert.AreEqual( 9, SectionAt( ride, 18, 10 ).Kind );
		// a click on the add-on opens its ride's window (0x00486920)
		Assert.AreSame( ride, level.WindowTarget( addOn ) );
		Assert.AreSame( ride, level.WindowTarget( ride ) );

		// the sale: the flags come off and the circuit is walked at once ([EXT:addon-sale-walk]), whichever removal handler runs first
		level.Objects.Remove( addOn );
		foreach ( var (cx, cy) in TrackStation.TileCellsOf( 16, 10 ) )
			Assert.AreEqual( 0, level.KartTracks!.Tracks.Get( cx, cy ).Flags & TrackCell.FlagAddOn, $"({cx}, {cy}) loses the add-on flag" );
		Assert.IsTrue( bump.IsTrackValid );
		Assert.IsFalse( bump.Sections.Any( candidate => candidate.Kind is 12 or 13 || candidate.AddOn != null ), "no section keeps the sold add-on" );
		Assert.AreEqual( 9, SectionAt( ride, 16, 10 ).Kind );
		Assert.IsNull( level.WindowTarget( addOn ), "a removed add-on opens nothing" );
	}

	[TestMethod]
	public void AnAddOnOffItsRidesTrackIsRefusedAndOpensNoWindow()
	{
		var (level, ride, jump) = World();
		Assert.IsTrue( level.BeginAddOn( jump, ride ) );
		Assert.AreEqual( OriginalPlacementResult.Blocked, level.CheckBuildTarget( jump, 20, 20, 0 ), "free ground holds none of the ride's track" );
		Assert.AreEqual( OriginalPlacementResult.Blocked, level.CheckBuildTarget( jump, 30, 10, 0 ), "a corner tile" );
		var stray = level.Objects.TryPlace( jump, 20, 20, 0, out _ );
		Assert.IsNotNull( stray, "placed without the add-on rule, as an ordinary object on free ground" );
		Assert.IsNull( level.WindowTarget( stray ), "an add-on on no ride's track opens nothing" );
	}
}
