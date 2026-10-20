using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>UI-910: real visitor-window arrow callbacks share the locator's live type-1 projection, with their own loss policy.</summary>
[TestClass]
[DoNotParallelize]
public class VisitorInfoArrowOrderTests
{
	private CameraMode previousCamera = null!;
	private System.Numerics.Matrix4x4 previousView, previousProjection;
	private Level previousLevel = null!;
	private Logger previousLog = null!;
	private (Vector3 Position, Vector3 Forward, Vector3 Front, Vector3 Up) previousListener;
	[TestInitialize]
	public void Setup()
	{
		previousCamera = Camera.Mode; previousView = Camera.ViewMatrix; previousProjection = Camera.ProjMatrix;
		previousLevel = Level.Current; previousLog = Log;
		previousListener = (AudioListener.Position, AudioListener.Forward, AudioListener.Front, AudioListener.Up);
		Camera.Mode = new CameraMode(); Level.Current = null!;
	}
	[TestCleanup]
	public void Cleanup()
	{
		Camera.Mode = previousCamera; Camera.ViewMatrix = previousView; Camera.ProjMatrix = previousProjection;
		Level.Current = previousLevel; Log = previousLog;
		AudioListener.RestoreCaptured( previousListener.Position, previousListener.Forward );
		AudioListener.RestoreCapturedAxes( previousListener.Front, previousListener.Up );
	}

	private static Guest Visitor( int id, GuestState state = GuestState.WalkingAround ) => new( id, 0 )
	{
		State = state, VisitorNumber = 1000 + id, Money = 100 + id, RidesTaken = id, ShopsVisited = id + 1,
		SideshowsVisited = id + 2, SideshowsWon = id + 3, Happiness = 50, Thought = GuestThought.Thirsty, TimeInParkSeconds = 2,
	};
	private static ParkHud Fixture( out GuestSimulation simulation, out List<Guest> guests )
	{
		var hud = VisitorWorldSelectionTests.Fixture( out simulation, out _ );
		typeof( ParkHud ).GetField( "strings", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( hud,
			new UiStringTable( "English", Enumerable.Range( 0, 473 ).Select( index => index == (int)UIStrings.VisitorCount ? "Visitor #" : index == (int)UIStrings.NewVisitor ? "New Visitor" : $"ui{index}" ).ToArray(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
				thoughtNames: new[] { "", "Hungry", "Thirsty" } ) );
		guests = (List<Guest>)simulation.Guests;
		guests.AddRange( new[] { Visitor( 7 ), Visitor( 2 ), Visitor( 9 ) } );
		simulation.Things.Restore( new[]
		{
			new ThingSlot( 2, SaveThingModel.Guest ), new ThingSlot( 90, SaveThingModel.Attraction ), new ThingSlot( 9, SaveThingModel.Guest ),
			new ThingSlot( 14, SaveThingModel.Handyman ), new ThingSlot( 7, SaveThingModel.Guest ),
		} );
		Assert.IsTrue( hud.OpenVisitorInfoWindow( guests.Single( guest => guest.Id == 2 ) ) );
		return hud;
	}
	private static UiContext Context( float delta = 0 )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( 1000, 1000 ), Delta = delta };
	}
	private static float Age( ParkHud hud ) => (float)typeof( ParkHud ).GetField( "visitorInfoAge", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
	private static Action Arrow( ParkHud hud, bool forward ) => ((UiButton)hud.Stack.Top!.Find( forward ? "nextVisitor" : "previousVisitor" )!).Clicked!;
	private static void Update( ParkHud hud, float delta = 0 ) => hud.Stack.Top!.Update( Context( delta ), UiInput.Idle( new System.Numerics.Vector2( 1, 1 ) ) );
	private static string Label( ParkHud hud, string id ) => ((UiLabel)hud.Stack.Top!.Find( id )!).Text();
	private static void Cycle( ParkHud hud, bool forward, params int[] ids )
	{
		foreach ( var id in ids )
		{
			Arrow( hud, forward )();
			Assert.AreEqual( id, hud.InfoVisitor?.Id );
		}
	}

	[TestMethod]
	public void RealArrowsFollowBothDirectionsOfTheRestoredChainWithoutMutatingSimulation()
	{
		var hud = Fixture( out var simulation, out var guests );
		var screen = hud.Stack.Top;
		var before = JsonSerializer.Serialize( simulation.CaptureSnapshot() );
		var pool = JsonSerializer.Serialize( simulation.Things.CaptureSnapshot() );
		var roster = guests.ToArray();
		var rng = simulation.RandomState;
		var hash = simulation.ComputeStateHash();
		var camera = Camera.Mode;
		var locator = (hud.Locator.ActorId, hud.Locator.StripShown, hud.Locator.Following);
		for ( var repeat = 0; repeat < 4; repeat++ )
		{
			Cycle( hud, true, 9, 7, 2 );
			Cycle( hud, false, 7, 9, 2 );
		}
		Assert.AreSame( screen, hud.Stack.Top, "arrows refresh the existing window without reopening it" );
		Assert.AreSame( camera, Camera.Mode );
		Assert.AreEqual( locator, (hud.Locator.ActorId, hud.Locator.StripShown, hud.Locator.Following) );
		CollectionAssert.AreEqual( roster, guests );
		Assert.AreEqual( pool, JsonSerializer.Serialize( simulation.Things.CaptureSnapshot() ) );
		Assert.AreEqual( before, JsonSerializer.Serialize( simulation.CaptureSnapshot() ) );
		Assert.AreEqual( rng, simulation.RandomState );
		Assert.AreEqual( hash, simulation.ComputeStateHash() );
	}

	[TestMethod]
	public void EachChangedSelectionImmediatelyRefreshesTitleCountersThoughtAndStatsAge()
	{
		var hud = Fixture( out _, out var guests );
		var target = guests.Single( guest => guest.Id == 9 );
		target.Money = 987; target.Thought = GuestThought.Hungry; target.VisitorNumber = 42;
		Update( hud, 3 );
		Assert.AreEqual( 3f, Age( hud ) );
		Cycle( hud, true, 9 );
		var shown = hud.VisitorInfoShown;
		Assert.AreEqual( (987, 9, 10, 11, 12, 512, 0), (shown.Cash, shown.Rides, shown.Purchases, shown.SideshowsPlayed, shown.SideshowsWon, shown.HappinessGauge, shown.ThoughtFrame) );
		Assert.AreEqual( 8L, shown.Hours );
		Assert.AreEqual( 0f, Age( hud ) );
		Assert.AreEqual( "Visitor #42", hud.VisitorInfoTitle );
		Assert.AreEqual( hud.VisitorInfoTitle, Label( hud, "title" ) );
		Assert.AreEqual( "9", Label( hud, "rides" ) );
		Assert.AreEqual( "10", Label( hud, "purchases" ) );
		Assert.AreEqual( "11", Label( hud, "sideshowsPlayed" ) );
		Assert.AreEqual( "12", Label( hud, "sideshowsWon" ) );
		Assert.AreEqual( "Hungry", shown.Thought );
		Assert.AreEqual( "Hungry", Label( hud, "thought" ) );
		Assert.AreEqual( shown.Status, Label( hud, "status" ) );
		Cycle( hud, false, 2 );
		Assert.AreEqual( 102, hud.VisitorInfoShown.Cash );
		Assert.AreEqual( 1, hud.VisitorInfoShown.ThoughtFrame );
		Assert.AreEqual( "Thirsty", Label( hud, "thought" ) );
		Assert.AreEqual( "Visitor #1002", hud.VisitorInfoTitle );
		target.VisitorNumber = 0;
		Cycle( hud, true, 9 ); Assert.AreEqual( "New Visitor", Label( hud, "title" ) );
		target.VisitorNumber = 77; Update( hud, 4 );
		Assert.AreEqual( "Visitor #77", Label( hud, "title" ), "a new visitor acquires its number on refresh" );
		target.VisitorNumber = 88; Update( hud, 4 );
		Assert.AreEqual( "Visitor #77", Label( hud, "title" ), "an established title keeps the stored number until selection changes" );
		Cycle( hud, false, 2 ); Cycle( hud, true, 9 );
		Assert.AreEqual( "Visitor #88", Label( hud, "title" ) );
	}

	[TestMethod]
	public void HiddenLiveStatesAndWalkingRideWalkingTransitionsKeepTheSameArrowOrder()
	{
		var hud = Fixture( out var simulation, out var guests );
		foreach ( var state in new[] { GuestState.WalkingAround, GuestState.Using, GuestState.WalkingAround, GuestState.HeldByGuard, GuestState.Queueing, GuestState.ChangingOut } )
		{
			foreach ( var guest in guests ) guest.State = state;
			Update( hud );
			Assert.IsTrue( hud.VisitorInfoWindowOpen );
			Assert.AreEqual( 2, hud.InfoVisitor?.Id );
			CollectionAssert.AreEqual( new[] { 2, 9, 7 }, LocatorOrder.VisitorIds( guests, simulation.Things ).ToArray() );
			Cycle( hud, true, 9, 7, 2 );
			Cycle( hud, false, 7, 9, 2 );
		}
	}

	[TestMethod]
	public void RejectedUsedSlotsGoneFirstClaimsAndDuplicatesNeverLeakThroughFallback()
	{
		var hud = Fixture( out var simulation, out var guests );
		guests.Clear();
		guests.AddRange( new[] { Visitor( 40 ), Visitor( 2 ), Visitor( 9 ), Visitor( 7, GuestState.Gone ), Visitor( 7 ), Visitor( 90 ), Visitor( 14 ),
			Visitor( 50 ), Visitor( 40 ), Visitor( 30, GuestState.Gone ), Visitor( 30 ), Visitor( 0 ), Visitor( -1 ), Visitor( 80 ), Visitor( 80 ) } );
		simulation.Things.QueueDestroy( 9 ); simulation.Things.Hold( 2 );
		Assert.IsTrue( simulation.Things.Occupy( 60, SaveThingModel.Guest ), "missing roster actor" );
		Assert.IsTrue( simulation.Things.Occupy( 80, SaveThingModel.Guest ) );
		// 14/90 wrong models; 9 queued; 2 placeholder; first 7/30 Gone; 40/50 pool-free; nonpositive IDs ignored.
		CollectionAssert.AreEqual( new[] { 80, 40, 50 }, LocatorOrder.VisitorIds( guests, simulation.Things ).ToArray() );
		Assert.IsTrue( hud.OpenVisitorInfoWindow( guests.First( guest => guest.Id == 80 ) ) );
		Cycle( hud, true, 40, 50, 80 );
		Cycle( hud, false, 50, 40, 80 );
		CollectionAssert.AreEqual( new[] { 2, 9, 14, 40, 50, 80, 90 }, LocatorOrder.VisitorIds( guests, null ).ToArray(), "host-only pool-free order is ascending, first-claim-wins" );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void MissingOrGoneSelectionRefusesNavigationThenClosesUntilExplicitlyReopened( bool gone )
	{
		var hud = Fixture( out var simulation, out var guests );
		var next = Arrow( hud, true ); var previous = Arrow( hud, false );
		var selected = guests.Single( guest => guest.Id == 2 );
		var survivor = guests.Single( guest => guest.Id == 9 );
		var shown = hud.VisitorInfoShown;
		if ( gone ) selected.State = GuestState.Gone;
		else { guests.Remove( selected ); simulation.Things.QueueDestroy( selected.Id ); }
		Assert.IsNull( hud.InfoVisitor );
		Assert.IsFalse( hud.CycleVisitorInfo( true ) ); Assert.IsFalse( hud.CycleVisitorInfo( false ) );
		next(); previous();
		Assert.AreEqual( shown, hud.VisitorInfoShown, "missing selection must not silently retarget" );
		Assert.IsTrue( hud.VisitorInfoWindowOpen, "the existing UI update owns closure" );
		Update( hud );
		Assert.IsFalse( hud.VisitorInfoWindowOpen );
		for ( var repeat = 0; repeat < 3; repeat++ ) { next(); previous(); }
		Assert.IsFalse( hud.VisitorInfoWindowOpen );
		Assert.IsNull( hud.InfoVisitor );
		Assert.IsFalse( hud.OpenVisitorInfoWindow( selected ) );
		survivor.Money = 555;
		Assert.IsTrue( hud.OpenVisitorInfoWindow( survivor ) );
		Assert.AreSame( survivor, hud.InfoVisitor );
		Assert.AreEqual( 555, hud.VisitorInfoShown.Cash );
		Assert.AreEqual( 0f, Age( hud ) );
		Cycle( hud, true, 7, 9 );
		hud.CloseVisitorInfoWindow();
		next(); previous();
		Assert.IsFalse( hud.VisitorInfoWindowOpen );
	}

	[TestMethod]
	public void ReusedLowerSlotBecomesNewestAndExactPoolRestorePreservesBothDirections()
	{
		var hud = Fixture( out var simulation, out var guests );
		Assert.IsTrue( simulation.Things.Occupy( 1, SaveThingModel.Attraction ) );
		Assert.IsTrue( simulation.Things.Release( 1 ) );
		var id = simulation.Things.Allocate( SaveThingModel.Guest );
		Assert.AreEqual( 1, id );
		var newest = Visitor( id ); guests.Add( newest );
		Cycle( hud, false, 1 );
		Cycle( hud, true, 2, 9, 7, 1 );
		Cycle( hud, false, 7, 9, 2, 1 );
		var snapshot = simulation.Things.CaptureSnapshot();
		var restored = new ThingPool(); restored.RestoreSnapshot( snapshot );
		CollectionAssert.AreEqual( new[] { 1, 2, 9, 7 }, LocatorOrder.VisitorIds( guests, restored ).ToArray() );
		simulation.Things.RestoreSnapshot( restored.CaptureSnapshot() );
		hud.CloseVisitorInfoWindow(); Assert.IsTrue( hud.OpenVisitorInfoWindow( newest ) );
		Cycle( hud, true, 2, 9, 7, 1 );
		Cycle( hud, false, 7, 9, 2, 1 );
		Assert.AreEqual( JsonSerializer.Serialize( snapshot ), JsonSerializer.Serialize( simulation.Things.CaptureSnapshot() ) );
	}

	[TestMethod]
	public void SingletonArrowsDoNotRefreshAgedStatsAndAnEmptyRosterCannotNavigate()
	{
		var hud = Fixture( out _, out var guests );
		guests.RemoveAll( guest => guest.Id != 2 );
		var selected = guests.Single();
		var shown = hud.VisitorInfoShown;
		Update( hud, 3 ); selected.Money = 999;
		foreach ( var forward in new[] { true, false } )
		{
			Assert.IsFalse( hud.CycleVisitorInfo( forward ) );
			Cycle( hud, forward, 2, 2 );
			Assert.AreEqual( shown, hud.VisitorInfoShown );
			Assert.AreEqual( 3f, Age( hud ), "a singleton never invokes the change refresh" );
		}
		Update( hud, 1 );
		Assert.AreEqual( 999, hud.VisitorInfoShown.Cash, "the normal four-second timer still refreshes" );
		Assert.AreEqual( 0f, Age( hud ) );
		guests.Clear();
		Assert.IsFalse( hud.CycleVisitorInfo( true ) ); Assert.IsFalse( hud.CycleVisitorInfo( false ) );
		Arrow( hud, true )(); Arrow( hud, false )(); Update( hud );
		Assert.IsFalse( hud.VisitorInfoWindowOpen );
	}
}
