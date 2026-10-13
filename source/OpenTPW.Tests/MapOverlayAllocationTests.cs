using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
public class MapOverlayAllocationTests
{
	public TestContext TestContext { get; set; } = null!;

	internal sealed class Rig
	{
		public readonly ParkHud Hud = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		public readonly GuestSimulation Guests;
		public readonly UiStringTable Strings = OriginalUiTests.FakeStrings();
		public readonly Func<string> Legend;
		public readonly UiElement View;
		public readonly UiLabel Label;
		public readonly UiContext Context;

		public Rig( int count )
		{
			var grid = new GuestPathGrid( 32, 32 );
			for ( var y = 0; y < 32; y++ ) for ( var x = 0; x < 32; x++ ) grid.SetPath( x, y, true );
			var settings = GuestTests.CreateSettings(); settings.MaximumGuests = 1500;
			Guests = new GuestSimulation( grid, settings, 7 ) { ArrivalsEnabled = false };
			for ( var index = 0; index < count; index++ )
			{
				var guest = Guests.SpawnInPark( index % 32, index / 32 % 32 );
				guest.Thought = index % 3 == 0 ? GuestThought.Pleased : index % 3 == 1 ? GuestThought.Dissatisfied : GuestThought.Happy;
				guest.Happiness = index % 101;
			}
			var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			Set( level, "<Objects>k__BackingField", new ParkObjects( null!, new SandboxParkGrid() ) );
			Set( level, "<Guests>k__BackingField", Guests );
			Set( Hud, "level", level ); Set( Hud, "strings", Strings ); Set( Hud, "mapView", MapViewState.Default );
			Hud.ToggleMapSatisfaction(); Hud.ToggleMapVisitors(); Hud.ToggleMapHappiness();
			Legend = (Func<string>)typeof( ParkHud ).GetMethod( "MapMetricLegend", BindingFlags.Instance | BindingFlags.NonPublic )!.CreateDelegate( typeof( Func<string> ), Hud );
			var viewType = typeof( ParkHud ).GetNestedType( "ParkMapView", BindingFlags.NonPublic )!;
			View = (UiElement)Activator.CreateInstance( viewType, Hud )!;
			View.Bounds = new UiRect( 0, 0, 512, 512 );
			Label = new UiLabel { Text = Legend, Fit = true, Bounds = new UiRect( 0, 512, 512, 48 ) };
			var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
			Context = new UiContext( Strings, new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1024, 768 ) };
		}

		public void Draw()
		{
			Context.Batch.Clear(); View.Draw( Context, false, false ); Label.Draw( Context, false, false );
		}
		private static void Set( object target, string field, object value ) => target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );
	}

	private static long Allocation( Action action, int repetitions )
	{
		for ( var index = 0; index < 5; index++ ) action();
		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( var index = 0; index < repetitions; index++ ) action();
		return (GC.GetAllocatedBytesForCurrentThread() - before) / repetitions;
	}

	[TestMethod]
	public void MeasureSteadyFullMapAndActualLegendAllocations()
	{
		var rig = new Rig( 1500 );
		var full = Allocation( rig.Draw, 100 );
		var legend = Allocation( () => _ = rig.Legend(), 100 );
		TestContext.WriteLine( $"1500 guests: steady map+legend {full} bytes/frame; legend alone {legend} bytes/call." );
		Assert.IsTrue( rig.Context.Batch.Draws.Count > 0 );
		Assert.IsTrue( rig.Context.Batch.Texts.Any( draw => draw.Text == rig.Legend() ) );
		Assert.IsTrue( legend < 2048, $"Counting legend totals must not regroup all visitors: {legend} bytes/call." );
	}

	[TestMethod]
	public void LegendMatchesVisibleInBoundsOutcomesAndImmediateLiveChanges()
	{
		var rig = new Rig( 6 );
		var guests = rig.Guests.Guests;
		guests[0].State = GuestState.Using;
		guests[1].Navigation.X = -Fixed16.One;
		guests[3].Navigation.X = 32 * Fixed16.One;
		void Matches( int pleased, int dissatisfied )
		{
			var cells = ParkMapMetrics.Outcomes( guests, 32, 32 ).ToArray();
			var expected = rig.Strings.Extra( OpenTpwText.MapRecordedVisitorReactions ) + "\n" + (cells.Length == 0
				? rig.Strings.Extra( OpenTpwText.MapNoObservations )
				: string.Format( rig.Strings.Extra( OpenTpwText.MapReactionLegend ), cells.Sum( cell => cell.Pleased ), cells.Sum( cell => cell.Dissatisfied ) ));
			Assert.AreEqual( pleased, cells.Sum( cell => cell.Pleased ) );
			Assert.AreEqual( dissatisfied, cells.Sum( cell => cell.Dissatisfied ) );
			Assert.AreEqual( expected, rig.Legend() );
		}
		Matches( 0, 1 );
		guests[4].Thought = GuestThought.Pleased; Matches( 1, 0 );
		guests[4].State = GuestState.Gone; Matches( 0, 0 );
		guests[0].State = GuestState.WalkingAround; guests[0].Navigation.X = 4 * Fixed16.One; Matches( 1, 0 );
		foreach ( var guest in guests ) guest.Thought = GuestThought.Happy;
		Matches( 0, 0 );
		rig.Hud.ToggleMapSatisfaction();
		Assert.AreEqual( "", rig.Legend() );
		rig.Hud.ToggleMapExcitement();
		Assert.AreEqual( rig.Strings.Extra( OpenTpwText.MapAttractionExcitement ) + "\n" + rig.Strings.Extra( OpenTpwText.MapNoObservations ), rig.Legend() );
	}
}
