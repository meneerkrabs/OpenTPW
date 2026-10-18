using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The track add-ons without game data (docs/TRACKS.md "Add-ons"): the build tool's hover position, the circuit walk's run counter and open ends, and the Space Racers
/// Teleport's vehicle hook. <see cref="TrackAddOnDataTests"/> checks the shipped add-ons.
/// </summary>
[TestClass]
public class TrackAddOnTests
{
	// ---------------------------------------------------------------- the hover (tp.exe 0x005236B0)

	/// <summary>A map of straight kart tiles on the tile grid of a station anchored at (<paramref name="anchorX"/>, <paramref name="anchorY"/>), every tile flowing along <paramref name="link"/>.</summary>
	internal static TrackCellLayer Tiles( int anchorX, int anchorY, int rotation, byte link )
	{
		var layer = new TrackCellLayer( 128, 128 );
		var first = TrackStation.FromPlacement( anchorX, anchorY, rotation ).First;
		var mask = link is TrackHeading.NegY or TrackHeading.PosY ? (byte)0x11 : (byte)0x44;
		for ( var y = first.Y % 2; y + 1 < layer.Height; y += 2 )
		{
			for ( var x = first.X % 2; x + 1 < layer.Width; x += 2 )
			{
				layer.Set( x, y, new TrackCell { Type = TrackCellTypes.KartOrigin, Mask = mask, Link = link, Parent = -1, Piece = 1 } );
				foreach ( var (cx, cy) in TrackStation.TileCellsOf( x, y ).Skip( 1 ) )
					layer.Set( cx, cy, new TrackCell { Type = TrackCellTypes.Extension, Parent = layer.IndexOf( x, y ) } );
			}
		}
		return layer;
	}

	internal static ObjectSettings AddOnSettings( string adjust ) => new( new[] { new ObjectSettingsFile( $"Info.Id 1500\nAddOn.UpgradesId 1150\nAddOn.UpgradeType 1\n{adjust}", "addon.sam" ) } );

	/// <summary>
	/// For every ride rotation and flow and for every pointer cell around the station: the hover's angle is the flow's, and the add-on's '+' cells, centred on the hover's
	/// click (the placement's centred anchor), are whole tiles on the line of the flow through the tile under the snapped pointer.
	/// </summary>
	internal static void AssertHoverCoversWholeTiles( ObjectSettings settings, ObjectShape shape, string name )
	{
		const int anchorX = 50, anchorY = 50;
		var plusCount = shape.Cells.Count( cell => cell.CellType is ObjectShapeSymbols.TypeUpgrade or ObjectShapeSymbols.TypeHash );
		Assert.AreEqual( 0, plusCount % 4, $"{name}: whole tiles" );
		foreach ( var rideRotation in new[] { 0, 90, 180, 270 } )
		{
			var first = TrackStation.FromPlacement( anchorX, anchorY, rideRotation ).First;
			(int X, int Y) TileOf( int x, int y ) => (first.X + 2 * (int)Math.Floor( (x - first.X) / 2.0 ), first.Y + 2 * (int)Math.Floor( (y - first.Y) / 2.0 ));
			foreach ( var (link, angle) in new[] { (TrackHeading.NegY, 0), (TrackHeading.PosX, 270), (TrackHeading.PosY, 180), (TrackHeading.NegX, 90) } )
			{
				var layer = Tiles( anchorX, anchorY, rideRotation, link );
				for ( var pointerX = 40; pointerX < 60; pointerX++ )
				{
					for ( var pointerY = 40; pointerY < 60; pointerY++ )
					{
						var (x, y, rotation) = TrackAddOns.HoverTarget( settings, layer, anchorX, anchorY, rideRotation, pointerX, pointerY, 0 );
						var where = $"{name}: ride turned {rideRotation}, flow 0x{link:X2}, pointer ({pointerX}, {pointerY})";
						Assert.AreEqual( angle, rotation, where );
						var anchor = Level.GetCentredAnchor( shape, x, y, rotation );
						var plus = shape.Cells.Where( cell => cell.CellType is ObjectShapeSymbols.TypeUpgrade or ObjectShapeSymbols.TypeHash )
							.Select( cell => ObjectFootprint.ToGrid( anchor.X, anchor.Y, rotation, cell.U, cell.V ) ).ToHashSet();
						var tiles = plus.Select( cell => TileOf( cell.X, cell.Y ) ).ToHashSet();
						Assert.IsTrue( plus.SetEquals( tiles.SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) ) ), $"{where}: whole tiles" );
						// the snap counts even cells from the anchor (0x005236E3..0x0052374D)
						var snapped = TileOf( anchorX + 2 * (int)Math.Floor( (pointerX - anchorX) / 2.0 ), anchorY + 2 * (int)Math.Floor( (pointerY - anchorY) / 2.0 ) );
						Assert.IsTrue( tiles.Contains( snapped ), $"{where}: the hovered tile" );
						Assert.IsTrue( tiles.All( tile => TrackHeading.Dx( link ) == 0 ? tile.X == snapped.X : tile.Y == snapped.Y ), $"{where}: along the flow" );
					}
				}
			}
		}
	}

	[TestMethod]
	public void TheHoverPutsEveryShippedAddOnShapeOnWholeTilesAlongTheFlow()
	{
		// the footprints and Bumper.*Adjust offsets of the shipped add-ons (the jungle jump and kart tunnel, and the 4 x 4 family with its two West offsets)
		AssertHoverCoversWholeTiles( AddOnSettings( "Bumper.NorthXAdjust 1\nBumper.NorthYAdjust 0\nBumper.EastXAdjust 0\nBumper.EastYAdjust 0\nBumper.SouthXAdjust 0\nBumper.SouthYAdjust -1\nBumper.WestXAdjust 1\nBumper.WestYAdjust -1\n" ),
			new ObjectShape( new[] { "++", "++" } ), "jump" );
		AssertHoverCoversWholeTiles( AddOnSettings( "Bumper.NorthXAdjust 0\nBumper.NorthYAdjust -1\nBumper.EastXAdjust 1\nBumper.EastYAdjust -1\nBumper.SouthXAdjust 1\nBumper.SouthYAdjust -2\nBumper.WestXAdjust 2\nBumper.WestYAdjust 0\n" ),
			new ObjectShape( new[] { ".++", "*++", "*++", ".++" } ), "kart tunnel" );
		foreach ( var westX in new[] { 2, 0 } )
			AssertHoverCoversWholeTiles( AddOnSettings( $"Bumper.NorthXAdjust 1\nBumper.NorthYAdjust 1\nBumper.EastXAdjust 1\nBumper.EastYAdjust 0\nBumper.SouthXAdjust 0\nBumper.SouthYAdjust 0\nBumper.WestXAdjust {westX}\nBumper.WestYAdjust -1\n" ),
				new ObjectShape( new[] { "*++*", "*++*", "*++*", "*++*" } ), $"4 x 4, West x {westX}" );
	}

	[TestMethod]
	public void TheHoverOffTheMapOrOffTheTrackKeepsTheAngle()
	{
		var settings = AddOnSettings( "Bumper.NorthXAdjust 1\nBumper.NorthYAdjust 0\nBumper.WestXAdjust 1\nBumper.WestYAdjust -1\n" );
		var layer = new TrackCellLayer( 128, 128 );
		Assert.AreEqual( (-3, 5, 90), TrackAddOns.HoverTarget( settings, layer, 50, 50, 0, -3, 5, 90 ), "off the 128 x 128 map the pointer's cell and the angle stay (0x004D8300)" );
		// on the map but on no track the cell snaps and moves by the adjust of the angle it keeps (West at 90)
		Assert.AreEqual( (50 + 1, 50 - 1, 90), TrackAddOns.HoverTarget( settings, layer, 50, 50, 0, 51, 51, 90 ) );
		Assert.AreEqual( (48 + 1 - 1, 52 + 1, 0), TrackAddOns.HoverTarget( settings, layer, 50, 50, 180, 49, 53, 0 ), "the ride's own rotation 180 adds (-1, +1)" );
	}

	// ---------------------------------------------------------------- the walk (tp.exe 0x0052A970)

	/// <summary>
	/// A kart loop with a long straight: station (10, 10) and (12, 10), then the straights (14, 10) … (28, 10), the corner (30, 10), (30, 12), the corner (30, 14), the straights
	/// (28, 14) … (10, 14), the corner (8, 14), (8, 12) and the corner (8, 10).
	/// </summary>
	private static (TrackBuilder Builder, TrackStation Station) LongLoop()
	{
		var cells = new ParkCellMap( 48, 32 );
		var builder = new TrackBuilder( cells, TrackFamily.Kart, () => null, new SandboxParkGrid() );
		var station = TrackStation.FromPlacement( 10, 9, 0 );
		builder.PlaceStation( station );
		var writer = new TrackLineWriter( builder, null, builder.OwnerOf( station ) );
		writer.Commit( (14, 10), (30, 10) );
		writer.Commit( (30, 10), (30, 14) );
		writer.Commit( (30, 14), (8, 14) );
		writer.Commit( (8, 14), (8, 10) );
		Assert.IsTrue( TrackCircuitWalker.WalkAndRefresh( builder, station ).IsValid );
		return (builder, station);
	}

	/// <summary>Flags the tiles of an add-on as its placement does (flag word 0x10 on every cell of the tile).</summary>
	private static void Flag( TrackBuilder builder, params (int X, int Y)[] tiles )
	{
		foreach ( var (x, y) in tiles )
		{
			foreach ( var (cx, cy) in TrackStation.TileCellsOf( x, y ) )
				builder.Tracks.Set( cx, cy, builder.Tracks.Get( cx, cy ) with { Flags = TrackCell.FlagAddOn } );
		}
	}

	[TestMethod]
	public void TheWalkNumbersEachRunOfAddOnTilesAndTheSectionsKeepTheNumber()
	{
		var (builder, station) = LongLoop();
		Flag( builder, (16, 10), (18, 10), (22, 10) );
		var circuit = TrackCircuitWalker.WalkAndRefresh( builder, station );
		Assert.IsTrue( circuit.IsValid );
		int RunAt( int x, int y ) => circuit.Steps.Single( step => (step.X, step.Y) == (x, y) ).AddOnRun;
		// tp.exe 0x0052AC68: section +8 is the counter before it is raised, and any other tile resets it
		Assert.AreEqual( (0, 1, 0, 0), (RunAt( 16, 10 ), RunAt( 18, 10 ), RunAt( 20, 10 ), RunAt( 22, 10 )) );
		var sections = TrackSections.Build( circuit.Steps, 1148, Array.Empty<Buoy>() ).Sections;
		var addOns = sections.Where( section => section.Kind == 12 ).Select( section => (section.X / TrackSections.Cell, section.AddOnRun) ).ToArray();
		CollectionAssert.AreEqual( new[] { (16, 0), (18, 1), (22, 0) }, addOns );
		Assert.IsTrue( sections.Where( section => section.Kind != 12 ).All( section => section.AddOnRun == 0 ) );
	}

	[TestMethod]
	public void TheSecondTileOfARunSetsTheOpenEndsAndTheBitsCarryOverToLaterRuns()
	{
		var (builder, station) = LongLoop();
		// A on (16, 10) and (18, 10), a plain (20, 10), B on (22, 10) and (24, 10), and C on (26, 10) and (28, 10) right behind B
		Flag( builder, (16, 10), (18, 10), (22, 10), (24, 10), (26, 10), (28, 10) );
		var circuit = TrackCircuitWalker.WalkAndRefresh( builder, station );
		var addOns = new HashSet<(int, int)> { (16, 10), (18, 10), (22, 10), (24, 10), (26, 10), (28, 10) };
		var ends = TrackAddOns.OpenEndsOf( builder.Tracks, circuit.Steps, ( x, y ) => addOns.Contains( (x, y) ) );
		// A hides both ends (the plain tiles behind and ahead, bits 0x11); B and C are one run of four, which acts on its second tile only: B's own ahead is C's tile (no piece),
		// but the bits still hold A's 0x10 (0x0052AE1E never clears them), so B hides its end ahead too, and C, the run's fourth tile, is left as it is
		CollectionAssert.AreEqual( new[] { new TrackAddOns.OpenEnds( 18, 10, true, true ), new TrackAddOns.OpenEnds( 24, 10, true, true ) }, ends.ToArray() );
	}

	[TestMethod]
	public void AOneTileAddOnKeepsItsEndsAndAnAddOnWithoutAnObjectOnlyAdvancesTheRun()
	{
		var (builder, station) = LongLoop();
		Flag( builder, (16, 10), (20, 10), (22, 10) );
		var circuit = TrackCircuitWalker.WalkAndRefresh( builder, station );
		// the jump on (16, 10) is a run of one; the run on (20, 10) and (22, 10) has no object on its first tile, which adds no bits, and the second tile's own neighbours
		// are the plain (24, 10) ahead and the add-on tile (20, 10) behind
		var ends = TrackAddOns.OpenEndsOf( builder.Tracks, circuit.Steps, ( x, y ) => (x, y) != (20, 10) );
		CollectionAssert.AreEqual( new[] { new TrackAddOns.OpenEnds( 22, 10, true, true ) }, ends.ToArray(), "the jump's bits (0x11) stay for the later run" );
		Assert.AreEqual( 0, TrackAddOns.OpenEndsOf( builder.Tracks, circuit.Steps.Where( step => step.X < 18 ).ToArray(), ( _, _ ) => true ).Count, "a run of one sets nothing" );
	}

	// ---------------------------------------------------------------- the teleport (tp.exe 0x00548C81)

	/// <summary>A teleport as its sections see it: channel 0's AnimIDs and the clips queued on it.</summary>
	private sealed class Teleport : ITrackAddOn
	{
		public int TypeId { get; init; } = TrackAddOns.TeleportTypeId;
		public int Animation { get; set; } = TrackAddOns.IdleAnimation;
		public int DeferredAnimation { get; set; } = ObjectAnimator.NoAnimation;
		public List<string> Calls { get; } = new();
		public void FlushDeferred() { Calls.Add( "flush" ); DeferredAnimation = ObjectAnimator.NoAnimation; }
		public void Queue( int animation, bool loop ) { Calls.Add( $"queue {animation}{(loop ? " loop" : "")}" ); DeferredAnimation = animation; }
	}

	[TestMethod]
	public void TheTeleportHidesAVehicleInItsFirstTileAndShowsItPastTheFarSideOfTheSecond()
	{
		var (builder, station) = LongLoop();
		// the teleport on (16, 10) and (18, 10): the karts run towards +x, so (16, 10) is the first tile of the run
		Flag( builder, (16, 10), (18, 10) );
		var circuit = TrackCircuitWalker.WalkAndRefresh( builder, station );
		var controller = new BumpController( BumperTemplates.Get( -9 ), () => 0 );
		var reference = station.Reference;
		controller.SetStation( reference.X, reference.Y, station.Heading );
		controller.Rebuild( circuit );
		var teleport = new Teleport();
		foreach ( var section in controller.Sections.Where( section => section.Kind == 12 ) )
			section.AddOn = teleport;
		controller.Open();
		Assert.IsTrue( controller.Launch() );
		var vehicle = controller.Vehicles.Last();
		var first = controller.Sections.Single( section => (section.X, section.Y) == (16 * TrackSections.Cell, 10 * TrackSections.Cell) );
		var second = controller.Sections.Single( section => (section.X, section.Y) == (18 * TrackSections.Cell, 10 * TrackSections.Cell) );
		void At( TrackSection section, int offset )
		{
			var item = section.Objects[0];
			(vehicle.X, vehicle.Y, vehicle.VX, vehicle.VY) = (item.X + offset, item.Y, 0, 0);
			vehicle.ObjectIndex = item.Index;
			controller.IntegrateVehicle( vehicle );
		}
		At( first, -TrackAddOns.TeleportReach );
		Assert.AreEqual( 0, vehicle.Flags & BumpVehicle.Hidden, "at the reach it is still outside (the bounds are strict)" );
		At( first, -TrackAddOns.TeleportReach + 1 );
		Assert.AreNotEqual( 0, vehicle.Flags & BumpVehicle.Hidden, "within reach of the first tile's centre it vanishes" );
		Assert.AreNotEqual( 0, vehicle.Flags & BumpVehicle.FromNegative, "it came from the negative side" );
		CollectionAssert.AreEqual( new[] { "flush", "queue 5" }, teleport.Calls, "the pass animation waits behind the idle loop (flags 0)" );
		Assert.IsTrue( first.AddOnPassing );
		teleport.Animation = TrackAddOns.PassAnimation;
		teleport.DeferredAnimation = ObjectAnimator.NoAnimation;
		At( second, 0 );
		Assert.AreNotEqual( 0, vehicle.Flags & BumpVehicle.Hidden, "still inside" );
		CollectionAssert.AreEqual( new[] { "flush", "queue 5" }, teleport.Calls, "the idle loop is queued by the first tile's section only" );
		At( first, 0 );
		CollectionAssert.AreEqual( new[] { "flush", "queue 5", "queue 2 loop" }, teleport.Calls, "once nothing is queued the idle loop goes behind the pass" );
		Assert.IsFalse( first.AddOnPassing );
		At( second, TrackAddOns.TeleportReach );
		Assert.AreNotEqual( 0, vehicle.Flags & BumpVehicle.Hidden );
		At( second, TrackAddOns.TeleportReach + 1 );
		Assert.AreEqual( 0, vehicle.Flags & BumpVehicle.Hidden, "past the far side of the second tile it shows again" );
		// a vehicle that is hidden shows as soon as it sits in an object that is no add-on's
		vehicle.Flags |= BumpVehicle.Hidden;
		var plain = controller.Sections.First( section => section.Kind is 9 or 10 );
		At( plain, 0 );
		Assert.AreEqual( 0, vehicle.Flags & BumpVehicle.Hidden );
	}

	[TestMethod]
	public void OtherAddOnsHaveNoVehicleHook()
	{
		var (builder, station) = LongLoop();
		Flag( builder, (16, 10), (18, 10) );
		var controller = new BumpController( BumperTemplates.Get( -9 ), () => 0 );
		var reference = station.Reference;
		controller.SetStation( reference.X, reference.Y, station.Heading );
		controller.Rebuild( TrackCircuitWalker.WalkAndRefresh( builder, station ) );
		var tunnel = new Teleport { TypeId = 1501 };
		foreach ( var section in controller.Sections.Where( section => section.Kind == 12 ) )
			section.AddOn = tunnel;
		controller.Open();
		Assert.IsTrue( controller.Launch() );
		var vehicle = controller.Vehicles.Last();
		var item = controller.Sections.Single( section => (section.X, section.Y) == (16 * TrackSections.Cell, 10 * TrackSections.Cell) ).Objects[0];
		(vehicle.X, vehicle.Y, vehicle.VX, vehicle.VY, vehicle.ObjectIndex) = (item.X, item.Y, 0, 0, item.Index);
		controller.IntegrateVehicle( vehicle );
		Assert.AreEqual( 0, vehicle.Flags & BumpVehicle.Hidden );
		Assert.AreEqual( 0, tunnel.Calls.Count );
	}
}
