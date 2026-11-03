using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>Actual ParkMapView CPU triangles over synthetic placed objects; no original assets or GPU are needed.</summary>
[TestClass, DoNotParallelize]
public class MapRepairDrawTests
{
	private RideUpgradeIngressTests fixture = null!;
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly RgbaByte Red = new( 248, 0, 0, 255 ), Green = new( 48, 252, 0, 255 ), Fallback = new( 207, 67, 198, 235 );
	private sealed record World( ParkHud Hud, Level Level, ParkEconomy Economy, OriginalObject First, OriginalObject Second, UiElement View, UiContext Context );

	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();
	private static void Set( object target, string field, object? value ) => target.GetType().GetField( field, Private )!.SetValue( target, value );
	private static Dictionary<OriginalObject, int> Bindings( World w ) => (Dictionary<OriginalObject, int>)typeof( Level ).GetField( "economyInstances", Private )!.GetValue( w.Level )!;
	private static ParkObjectState State( World w, OriginalObject item )
	{
		Assert.IsTrue( w.Economy.TryGetObject( w.Level.GetEconomyInstance( item )!.Value, out var state ) );
		return state;
	}
	private static void Place( OriginalObject item, int x, int y ) => Set( item, "<Placement>k__BackingField", item.Placement with { X = x, Y = y } );
	private World Create( BuildCategory category = BuildCategory.Rides, bool original = false )
	{
		var hud = fixture.Fixture( category, out var level, out var economy, out var first, out var second, out _, extra: "UsageInfo.ExcitementLevel 20\n" );
		// Two separate instances of the same InfoId: a catalogue default cannot supply the right repair.
		Set( first.Entry, "<Shape>k__BackingField", new ObjectShape( new[] { "**", "**" } ) );
		Place( first, 2, 2 ); Place( second, 8, 8 );
		// without a cell map the level's objects stand on their footprints (LevelMapCells), which follow the placements these tests move
		Set( level, "<Paths>k__BackingField", null );
		if ( !hud.MapCategoryShown( category ) ) hud.ToggleMapCategory( category );
		if ( original )
		{
			var park = (OriginalPark)RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) );
			Set( park, "<Heightfield>k__BackingField", new ModelHeightfield( 32, 32, 10, 10, 0, 0, Array.Empty<uint>(), new float[33 * 33], new uint[32 * 32] ) );
			Set( park, "<LevelName>k__BackingField", "synthetic" ); Set( level, "<OriginalPark>k__BackingField", park );
		}
		var view = (UiElement)Activator.CreateInstance( typeof( ParkHud ).GetNestedType( "ParkMapView", BindingFlags.NonPublic )!, hud )!;
		view.Bounds = new UiRect( 0, 0, 672, 560 ); view.Anchor = UiAnchor.TopLeft;
		var w = new World( hud, level, economy, first, second, view, RideUpgradeIngressTests.Context() );
		State( w, first ).Repair = 0; State( w, second ).Repair = 100;
		return w;
	}
	/// <summary>Draws the map as it stands when the window opens: the canvas is written again, as the original writes its classes and draws its surface at every opening. Returns the rectangles the render drew into the surface.</summary>
	private static MapPrim[] Draw( World w )
	{
		Set( w.Hud, "mapCanvas", null );
		w.Context.Batch.Clear(); w.View.Draw( w.Context, false, false );
		return ((ParkMapCanvas)typeof( ParkHud ).GetField( "mapCanvas", Private )!.GetValue( w.Hud )!).Prims.ToArray();
	}
	private static MapPrim[] OfColor( MapPrim[] prims, RgbaByte color ) => prims.Where( prim => prim.Color == color && !prim.Line ).ToArray();
	private static UiRect[] Rectangles( MapPrim[] prims, RgbaByte color ) => OfColor( prims, color ).Select( prim => new UiRect( prim.X, prim.Y, prim.Width, prim.Height ) ).ToArray();

	[DataTestMethod]
	[DataRow( BuildCategory.Rides, false )]
	[DataRow( BuildCategory.Rides, true )]
	[DataRow( BuildCategory.Shops, false )]
	[DataRow( BuildCategory.Sideshows, false )]
	[DataRow( BuildCategory.Features, false )]
	public void CurrentInstanceRepairFillsEveryOccupiedCellAndCategoryFilteringIsUnchanged( BuildCategory category, bool original )
	{
		var w = Create( category, original );
		Assert.AreEqual( w.First.Entry.InfoId, w.Second.Entry.InfoId );
		Assert.AreNotEqual( w.Level.GetEconomyInstance( w.First ), w.Level.GetEconomyInstance( w.Second ) );
		var drawn = Draw( w );
		Assert.AreEqual( 4, OfColor( drawn, Red ).Length ); Assert.AreEqual( 1, OfColor( drawn, Green ).Length );
		var expected = w.First.Cells.OrderBy( cell => cell.Y ).ThenBy( cell => cell.X ).Select( cell => original ? new UiRect( 15 + cell.X * 15, 42 + cell.Y * 13, 15, 13 )
			: new UiRect( cell.X * 16, cell.Y * 16, 16, 16 ) ).ToArray();
		CollectionAssert.AreEqual( expected, Rectangles( drawn, Red ), "the cells lie on the original's grid of the surface (15 + column x 15, 42 + row x 13), the sandbox's fills the image" );
		w.Hud.ToggleMapCategory( category ); drawn = Draw( w );
		Assert.AreEqual( 0, OfColor( drawn, Red ).Length ); Assert.AreEqual( 0, OfColor( drawn, Green ).Length );
		Assert.AreEqual( 0, OfColor( drawn, Fallback ).Length );
		w.Hud.ToggleMapCategory( category ); State( w, w.First ).Repair = 50;
		Assert.AreEqual( 4, OfColor( Draw( w ), new RgbaByte( 248, 200, 0, 255 ) ).Length, "live repair changes refresh without caching" );
	}

	[DataTestMethod]
	[DataRow( "unbound" )]
	[DataRow( "missingRecord" )]
	[DataRow( "wrongInfoId" )]
	[DataRow( "wrongRecordId" )]
	[DataRow( "readOnly" )]
	[DataRow( "staleStatus" )]
	[DataRow( "noPark" )]
	[DataRow( "noEconomy" )]
	[DataRow( "nullStatus" )]
	[DataRow( "nonfinite" )]
	[DataRow( "overflow" )]
	public void UnavailableOrMismatchedCurrentStateRetainsExactOldFallback( string reason )
	{
		var w = Create(); var state = State( w, w.First );
		Assert.AreEqual( 4, OfColor( Draw( w ), Red ).Length );
		switch ( reason )
		{
			case "unbound": Bindings( w ).Remove( w.First ); break;
			case "missingRecord": w.Economy.Sell( state.Id ); break;
			case "wrongInfoId": Set( state, "<InfoId>k__BackingField", w.Second.Entry.InfoId + 1 ); break;
			case "wrongRecordId": Set( state, "<Id>k__BackingField", state.Id + 100 ); break;
			case "readOnly": Set( w.Level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) ); break;
			case "staleStatus":
				var stale = EconomyTestData.Park(); stale.RegisterExisting( w.First.Entry.InfoId ).Repair = 50; stale.RegisterExisting( w.Second.Entry.InfoId );
				Set( w.Hud, "<Status>k__BackingField", new EconomyParkStatus( () => stale ) ); break;
			case "noPark": Set( w.Level, "<Park>k__BackingField", null ); break;
			case "noEconomy": Set( w.Hud, "<Status>k__BackingField", new NoEconomyStatus() ); break;
			case "nullStatus": Set( w.Hud, "<Status>k__BackingField", null ); break;
			case "nonfinite": state.Repair = double.NaN; break;
			case "overflow": state.Repair = int.MaxValue; break;
		}
		var drawn = Draw( w );
		Assert.AreEqual( 0, OfColor( drawn, Red ).Length );
		Assert.AreEqual( reason is "readOnly" or "staleStatus" or "noPark" or "noEconomy" or "nullStatus" ? 5 : 4, OfColor( drawn, Fallback ).Length );
		Assert.IsTrue( OfColor( drawn, Fallback ).All( prim => prim.Color.A == 235 ) );
	}

	[TestMethod]
	public void DeletedOrRemovedObjectsCannotLeaveStaleFillsAndNewBindingsAreReadImmediately()
	{
		var w = Create(); var firstState = State( w, w.First );
		var replacement = w.Economy.RegisterExisting( w.First.Entry.InfoId ); replacement.Repair = 50;
		Bindings( w )[w.First] = replacement.Id;
		Assert.AreEqual( 4, OfColor( Draw( w ), new RgbaByte( 248, 200, 0, 255 ) ).Length );
		Assert.AreEqual( 0d, firstState.Repair, "the displaced record is untouched" );
		w.First.Delete(); var drawn = Draw( w );
		Assert.AreEqual( 0, OfColor( drawn, new RgbaByte( 248, 200, 0, 255 ) ).Length );
		Assert.AreEqual( 0, OfColor( drawn, Fallback ).Length ); Assert.AreEqual( 1, OfColor( drawn, Green ).Length );
		((List<OriginalObject>)w.Level.Objects.Objects).Remove( w.Second );
		Assert.AreEqual( 0, OfColor( Draw( w ), Green ).Length );
	}

	private static ParkMapCanvas CanvasOf( World w ) => (ParkMapCanvas)typeof( ParkHud ).GetField( "mapCanvas", Private )!.GetValue( w.Hud )!;

	[TestMethod]
	public void TheSurfaceIsBakedOnceAndShownAsOneQuadWhateverTheView()
	{
		var w = Create(); Place( w.First, 7, 7 ); Place( w.Second, 1, 1 );
		Set( w.Hud, "mapView", new MapViewState( 2, new NVector2( 15.25f ) ) );
		State( w, w.First ).Repair = 0;
		Draw( w );
		var surface = CanvasOf( w ).Pixels;
		var quads = w.Context.Batch.Draws.Where( draw => ReferenceEquals( draw.Texture.Surface, surface ) ).ToArray();
		Assert.AreEqual( 1, quads.Length );
		Assert.AreEqual( 6, quads[0].Vertices.Count, "one quad: the cells, outlines and areas are in the surface, not drawn one by one" );
		Assert.AreEqual( 1, w.Context.Batch.Draws.Count( draw => draw.Texture.Surface != null ) );
		// the sandbox's 32 x 32 cells are 16 pixels: the cell (7, 7) is red in the baked pixels, a cell no object stands on keeps the sandbox base
		var at = ((7 * 16 + 8) * 512 + 7 * 16 + 8) * 4;
		CollectionAssert.AreEqual( new byte[] { 248, 0, 0, 255 }, surface.Rgba[at..(at + 4)] );
		var empty = (20 * 16 * 512 + 20 * 16) * 4;
		Assert.AreNotEqual( (byte)248, surface.Rgba[empty] );
		var version = surface.Version;
		w.Context.Batch.Clear(); w.View.Draw( w.Context, false, false );
		Assert.AreEqual( version, surface.Version, "a drawn frame does not bake again" );
	}

	[TestMethod]
	public void DrawingDoesNotMutateEconomySaveRandomnessBindingsOrObjects()
	{
		var w = Create( original: true );
		var guests = w.Level.Guests!;
		guests.Grid.SetPath( 2, 2, true ); guests.Grid.Cells.SetType( 3, 2, ParkCellType.Queue ); guests.Grid.Cells.SetFlags( 4, 2, ParkCellFlags.Unowned );
		guests.Register( w.First.Visitors );
		var guest = guests.SpawnInPark( 2, 2 ); guest.State = GuestState.WalkingAround; guest.Happiness = 80; guest.Thought = GuestThought.Pleased;
		var coaster = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), w.Level.Objects.ScriptWorld.Coasters );
		new CoasterBuilder( coaster, new CoasterSite( 32, 32 ) ).SetStation( (3, 2), (2, 2) );
		EconomyTestData.HireBest( w.Economy, StaffType.Mechanic );
		Set( w.Level, "<StaffWorld>k__BackingField", new StaffWorldSimulation( guests.Grid, () => w.Economy.Staff.Members, _ => null, (2, 2) ) );
		var save = JsonSerializer.Serialize( ParkSaveFile.Capture( w.Economy ) );
		var hash = WorldStateHash.Compute( new WorldStateSources { Economy = w.Economy } );
		var random = (w.Economy.World.State, w.Level.Guests!.RandomState, w.Level.Objects.ScriptWorld.Coasters.Random.State);
		var bindings = Bindings( w ).ToArray(); var objects = w.Level.Objects.Objects.ToArray();
		var cells = w.First.Cells.ToArray(); var placement = w.First.Placement; var variables = w.First.Script!.Variables.ToArray();
		// every layer, value mode and staff role, with a hover over the map, before the state is compared
		w.Hud.ToggleMapTracks(); w.Hud.ToggleMapVisitors(); w.Hud.ToggleMapHappiness(); w.Hud.ToggleMapExcitement(); w.Hud.ToggleMapSatisfaction();
		foreach ( var category in Enum.GetValues<BuildCategory>() ) w.Hud.ToggleMapCategory( category );
		foreach ( var type in Enum.GetValues<StaffType>() ) w.Hud.ToggleMapStaff( type );
		var geometry = (new UiRect( 0, 0, 512, 512 ), NVector2.Zero, 512f);
		for ( var frame = 0; frame < 20; frame++ )
		{
			Draw( w );
			w.Hud.UpdateMapHover( geometry, new NVector2( 15 + frame * 20, 42 + frame * 15 ) );
		}
		Assert.AreEqual( save, JsonSerializer.Serialize( ParkSaveFile.Capture( w.Economy ) ) );
		Assert.AreEqual( hash, WorldStateHash.Compute( new WorldStateSources { Economy = w.Economy } ) );
		Assert.AreEqual( random, (w.Economy.World.State, w.Level.Guests.RandomState, w.Level.Objects.ScriptWorld.Coasters.Random.State) );
		CollectionAssert.AreEqual( bindings, Bindings( w ).ToArray() ); CollectionAssert.AreEqual( objects, w.Level.Objects.Objects.ToArray() );
		CollectionAssert.AreEqual( cells, w.First.Cells.ToArray() ); CollectionAssert.AreEqual( variables, w.First.Script.Variables.ToArray() );
		Assert.AreEqual( placement, w.First.Placement ); Assert.AreEqual( 0d, State( w, w.First ).Repair ); Assert.AreEqual( 100d, State( w, w.Second ).Repair );
	}

	private static long Allocation( Action action, int repetitions )
	{
		for ( var index = 0; index < 10; index++ ) action();
		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( var index = 0; index < repetitions; index++ ) action();
		return (GC.GetAllocatedBytesForCurrentThread() - before) / repetitions;
	}

	[TestMethod]
	public void ADrawnMapFrameAndAHoverUpdateAllocateNothingSteady()
	{
		var w = Create( original: true );
		w.Hud.ToggleMapVisitors(); w.Hud.ToggleMapTracks();
		w.Level.Guests!.Grid.SetPath( 2, 2, true ); w.Level.Guests.Register( w.First.Visitors );
		var guest = w.Level.Guests.SpawnInPark( 2, 2 ); guest.State = GuestState.WalkingAround; guest.Happiness = 80;
		Draw( w );
		var geometry = (new UiRect( 0, 0, 512, 512 ), NVector2.Zero, 512f);
		var frame = Allocation( () => { w.Context.Batch.Clear(); w.View.Draw( w.Context, false, false ); }, 200 );
		var hover = Allocation( () => w.Hud.UpdateMapHover( geometry, new NVector2( 95, 66 ) ), 200 );
		Assert.IsTrue( frame < 512, $"a steady map frame allocated {frame} bytes" );
		Assert.IsTrue( hover < 64, $"a steady hover update allocated {hover} bytes" );
	}
}
