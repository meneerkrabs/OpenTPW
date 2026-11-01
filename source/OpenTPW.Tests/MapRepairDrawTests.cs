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
	/// <summary>Draws the map as it stands when the window opens: the canvas is written again, as the original writes its classes and draws its surface at every opening.</summary>
	private static UiVertex[] Draw( World w )
	{
		Set( w.Hud, "mapCanvas", null );
		w.Context.Batch.Clear(); w.View.Draw( w.Context, false, false );
		return w.Context.Batch.Draws.SelectMany( draw => draw.Vertices ).ToArray();
	}
	private static UiVertex[] OfColor( UiVertex[] vertices, RgbaByte color ) => vertices.Where( vertex => vertex.Color == color ).ToArray();
	private static UiRect[] Rectangles( UiVertex[] vertices, RgbaByte color ) => OfColor( vertices, color ).Chunk( 6 )
		.Select( quad => new UiRect( quad.Min( v => v.Position.X ), quad.Min( v => v.Position.Y ),
			quad.Max( v => v.Position.X ) - quad.Min( v => v.Position.X ), quad.Max( v => v.Position.Y ) - quad.Min( v => v.Position.Y ) ) ).ToArray();

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
		Assert.AreEqual( 24, OfColor( drawn, Red ).Length ); Assert.AreEqual( 6, OfColor( drawn, Green ).Length );
		var expected = w.First.Cells.OrderBy( cell => cell.Y ).ThenBy( cell => cell.X ).Select( cell => original ? new UiRect( 95 + cell.X * 15, 66 + cell.Y * 13, 15, 13 )
			: new UiRect( 80 + cell.X * 16, 24 + cell.Y * 16, 16, 16 ) ).ToArray();
		CollectionAssert.AreEqual( expected, Rectangles( drawn, Red ), "the pre-existing map grid and image offsets are unchanged" );
		w.Hud.ToggleMapCategory( category ); drawn = Draw( w );
		Assert.AreEqual( 0, OfColor( drawn, Red ).Length ); Assert.AreEqual( 0, OfColor( drawn, Green ).Length );
		Assert.AreEqual( 0, OfColor( drawn, Fallback ).Length );
		w.Hud.ToggleMapCategory( category ); State( w, w.First ).Repair = 50;
		Assert.AreEqual( 24, OfColor( Draw( w ), new RgbaByte( 248, 200, 0, 255 ) ).Length, "live repair changes refresh without caching" );
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
		Assert.AreEqual( 24, OfColor( Draw( w ), Red ).Length );
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
		Assert.AreEqual( reason is "readOnly" or "staleStatus" or "noPark" or "noEconomy" or "nullStatus" ? 30 : 24, OfColor( drawn, Fallback ).Length );
		Assert.IsTrue( OfColor( drawn, Fallback ).All( vertex => vertex.Color.A == 235 ) );
	}

	[TestMethod]
	public void DeletedOrRemovedObjectsCannotLeaveStaleFillsAndNewBindingsAreReadImmediately()
	{
		var w = Create(); var firstState = State( w, w.First );
		var replacement = w.Economy.RegisterExisting( w.First.Entry.InfoId ); replacement.Repair = 50;
		Bindings( w )[w.First] = replacement.Id;
		Assert.AreEqual( 24, OfColor( Draw( w ), new RgbaByte( 248, 200, 0, 255 ) ).Length );
		Assert.AreEqual( 0d, firstState.Repair, "the displaced record is untouched" );
		w.First.Delete(); var drawn = Draw( w );
		Assert.AreEqual( 0, OfColor( drawn, new RgbaByte( 248, 200, 0, 255 ) ).Length );
		Assert.AreEqual( 0, OfColor( drawn, Fallback ).Length ); Assert.AreEqual( 6, OfColor( drawn, Green ).Length );
		((List<OriginalObject>)w.Level.Objects.Objects).Remove( w.Second );
		Assert.AreEqual( 0, OfColor( Draw( w ), Green ).Length );
	}

	[TestMethod]
	public void PartialViewportClippingAndAllTriangleGeometryMatchFallbackRendering()
	{
		var w = Create(); Place( w.First, 7, 7 ); Place( w.Second, 1, 1 );
		Set( w.Hud, "mapView", new MapViewState( 2, new NVector2( 15.25f ) ) );
		State( w, w.First ).Repair = double.NaN; var fallback = Draw( w );
		State( w, w.First ).Repair = 0; var actual = Draw( w );
		CollectionAssert.AreEqual( fallback.Select( vertex => (vertex.Position, vertex.TexCoords) ).ToArray(), actual.Select( vertex => (vertex.Position, vertex.TexCoords) ).ToArray() );
		CollectionAssert.AreEqual( Rectangles( fallback, Fallback ), Rectangles( actual, Red ) );
		var rects = Rectangles( actual, Red ); Assert.AreEqual( 4, rects.Length );
		Assert.IsTrue( rects.Any( rect => rect.Width == 8 && rect.Height == 8 ), "the upper-left cell is partially clipped on both axes" );
		Assert.IsTrue( rects.All( rect => rect.X >= 80 && rect.Y >= 24 && rect.Right <= 592 && rect.Bottom <= 536 ) );
		Assert.AreEqual( 0, OfColor( actual, Green ).Length, "a fully offscreen object stays clipped" );
	}

	[TestMethod]
	public void DrawingDoesNotMutateEconomySaveRandomnessBindingsOrObjects()
	{
		var w = Create( original: true );
		var save = JsonSerializer.Serialize( ParkSaveFile.Capture( w.Economy ) );
		var hash = WorldStateHash.Compute( new WorldStateSources { Economy = w.Economy } );
		var random = (w.Economy.World.State, w.Level.Guests!.RandomState, w.Level.Objects.ScriptWorld.Coasters.Random.State);
		var bindings = Bindings( w ).ToArray(); var objects = w.Level.Objects.Objects.ToArray();
		var cells = w.First.Cells.ToArray(); var placement = w.First.Placement; var variables = w.First.Script!.Variables.ToArray();
		for ( var frame = 0; frame < 20; frame++ ) Draw( w );
		Assert.AreEqual( save, JsonSerializer.Serialize( ParkSaveFile.Capture( w.Economy ) ) );
		Assert.AreEqual( hash, WorldStateHash.Compute( new WorldStateSources { Economy = w.Economy } ) );
		Assert.AreEqual( random, (w.Economy.World.State, w.Level.Guests.RandomState, w.Level.Objects.ScriptWorld.Coasters.Random.State) );
		CollectionAssert.AreEqual( bindings, Bindings( w ).ToArray() ); CollectionAssert.AreEqual( objects, w.Level.Objects.Objects.ToArray() );
		CollectionAssert.AreEqual( cells, w.First.Cells.ToArray() ); CollectionAssert.AreEqual( variables, w.First.Script.Variables.ToArray() );
		Assert.AreEqual( placement, w.First.Placement ); Assert.AreEqual( 0d, State( w, w.First ).Repair ); Assert.AreEqual( 100d, State( w, w.Second ).Repair );
	}
}
