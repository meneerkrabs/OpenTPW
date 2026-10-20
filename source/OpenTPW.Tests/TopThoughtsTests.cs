using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.Mods;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The park information window's Top 3 Thoughts (tp.exe 0x4A52A0): the ranking 0x4C8430, the icons 0x4A4AB0, the one-second timer 0x80083, and the
/// <c>opentpw.guest-insights</c> refusal counters shown beside them.
/// </summary>
[TestClass, DoNotParallelize]
public class TopThoughtsTests
{
	private static TopThoughts Rank( params GuestThought[] thoughts ) => TopThoughtRanking.Rank( thoughts.Select( thought => (int)thought ) );

	private static IEnumerable<GuestThought> Repeat( GuestThought thought, int count ) => Enumerable.Repeat( thought, count );

	private static void SetGuests( GuestSimulation simulation, params Guest[] guests ) =>
		typeof( GuestSimulation ).GetField( "guests", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( simulation, guests.ToList() );

	private static UiContext Context( float delta )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1000, 1000 ), Delta = delta };
	}

	// ---- The ranking ----------------------------------------------------------------------------

	[TestMethod]
	public void NoGuestsLeavesEverySlotEmpty()
	{
		var top = TopThoughtRanking.Rank( Array.Empty<int>() );
		Assert.AreEqual( new TopThoughts( TopThoughtRanking.NoThought, TopThoughtRanking.NoThought, TopThoughtRanking.NoThought ), top );
		Assert.AreEqual( -1, top.FrameAt( 0 ) );
		Assert.IsNull( top.ThoughtAt( 2 ) );
	}

	[TestMethod]
	public void TheThreeMostCommonThoughtsFillTheSlotsInTheOrderTheyWereTaken()
	{
		// 5 x3, 9 x2, 12 x1: each id from 3 up takes the slot with the lowest count, the first slot on equal lows
		var top = Rank( Repeat( GuestThought.Pleased, 3 ).Concat( Repeat( GuestThought.Happy, 2 ) ).Append( GuestThought.Bored ).ToArray() );
		Assert.AreEqual( (GuestThought.Pleased, GuestThought.Happy, GuestThought.Bored), (top.ThoughtAt( 0 ), top.ThoughtAt( 1 ), top.ThoughtAt( 2 )) );
		Assert.AreEqual( (4, 8, 11), (top.FrameAt( 0 ), top.FrameAt( 1 ), top.FrameAt( 2 )), "the icon frame is the thought less 1" );
	}

	[TestMethod]
	public void TheSlotsAreNotSortedByCount()
	{
		// thirsty x3 starts in slot 2; need-toilet x1 then takes slot 0; scared x5 takes slot 1 (the lowest left, a count of 0)
		var top = Rank( Repeat( GuestThought.Thirsty, 3 ).Append( GuestThought.NeedToilet ).Concat( Repeat( GuestThought.Scared, 5 ) ).ToArray() );
		Assert.AreEqual( (GuestThought.NeedToilet, GuestThought.Scared, GuestThought.Thirsty), (top.ThoughtAt( 0 ), top.ThoughtAt( 1 ), top.ThoughtAt( 2 )) );
	}

	[TestMethod]
	public void AnEqualCountNeverReplacesASlotSoTheLowerIdWinsATie()
	{
		var tied = Rank( Repeat( GuestThought.Dissatisfied, 2 ).Concat( Repeat( GuestThought.TooMessy, 2 ) ).Concat( Repeat( GuestThought.SuperHappy, 2 ) ).Concat( Repeat( GuestThought.Happy, 2 ) ).ToArray() );
		Assert.AreEqual( (GuestThought.Dissatisfied, GuestThought.TooMessy, GuestThought.SuperHappy), (tied.ThoughtAt( 0 ), tied.ThoughtAt( 1 ), tied.ThoughtAt( 2 )), "the fourth thought ties and stays out" );
		var beaten = Rank( Repeat( GuestThought.Dissatisfied, 2 ).Concat( Repeat( GuestThought.TooMessy, 2 ) ).Concat( Repeat( GuestThought.SuperHappy, 2 ) ).Concat( Repeat( GuestThought.Happy, 3 ) ).ToArray() );
		Assert.AreEqual( (GuestThought.Happy, GuestThought.TooMessy, GuestThought.SuperHappy), (beaten.ThoughtAt( 0 ), beaten.ThoughtAt( 1 ), beaten.ThoughtAt( 2 )), "a higher count takes the first of the equal lowest slots" );
	}

	[TestMethod]
	public void TheCountOfNoThoughtIsDiscardedSoThoseGuestsNeverTakeASlot()
	{
		// 0x4C84D7 writes 0 to counts[0] after counting: 50 guests without a thought are as good as none
		var top = Rank( Repeat( GuestThought.None, 50 ).Concat( Repeat( GuestThought.Hungry, 2 ) ).Append( GuestThought.Thirsty ).Append( GuestThought.Scared ).ToArray() );
		Assert.AreEqual( (GuestThought.Scared, GuestThought.Hungry, GuestThought.Thirsty), (top.ThoughtAt( 0 ), top.ThoughtAt( 1 ), top.ThoughtAt( 2 )) );
		Assert.AreEqual( (14, 0, 1), (top.FrameAt( 0 ), top.FrameAt( 1 ), top.FrameAt( 2 )) );
		Assert.AreEqual( new TopThoughts( TopThoughtRanking.NoThought, TopThoughtRanking.NoThought, TopThoughtRanking.NoThought ), Rank( Repeat( GuestThought.None, 100 ).ToArray() ), "only guests without a thought: three empty slots" );
	}

	[TestMethod]
	public void ThoughtsOutsideTheIdRangeAreIgnored() =>
		Assert.AreEqual( new TopThoughts( TopThoughtRanking.NoThought, TopThoughtRanking.NoThought, TopThoughtRanking.NoThought ), TopThoughtRanking.Rank( new[] { -1, 23, 99 } ) );

	[TestMethod]
	public void TheSimulationRanksOnlyGuestsOnAParkCell()
	{
		var simulation = new GuestSimulation( new GuestPathGrid( 32, 32 ), GuestTests.CreateSettings(), 1 ) { ArrivalsEnabled = false };
		simulation.Grid.Cells.SetType( 9, 9, (byte)0x1E );
		Guest At( int id, int x, int y, GuestThought thought, GuestState state = GuestState.WalkingAround )
		{
			var guest = new Guest( id, 0 ) { State = state, Thought = thought };
			guest.Navigation.X = (x << 16) + 0x8000;
			guest.Navigation.Y = (y << 16) + 0x8000;
			return guest;
		}
		SetGuests( simulation,
			At( 1, 2, 2, GuestThought.Hungry ), At( 2, 3, 2, GuestThought.Hungry ),
			At( 3, 9, 9, GuestThought.Angry ), At( 4, 9, 9, GuestThought.Angry ), At( 5, 9, 9, GuestThought.Angry ),
			At( 6, 4, 2, GuestThought.Bored, GuestState.Gone ),
			At( 7, 5, 2, GuestThought.Pleased ) );
		var top = simulation.GetTopThoughts();
		Assert.AreEqual( GuestThought.Hungry, top.ThoughtAt( 1 ) );
		Assert.AreEqual( GuestThought.Pleased, top.ThoughtAt( 0 ), "a count of 1 takes slot 0 from the empty ones" );
		Assert.IsFalse( Enumerable.Range( 0, 3 ).Any( slot => top.ThoughtAt( slot ) is GuestThought.Angry or GuestThought.Bored ), "the guests on the gate walkway and the gone guest do not count" );
	}

	// ---- The window ------------------------------------------------------------------------------

	[TestMethod]
	public void TheTableGivesTheThoughtsFrameItsTitleAndThreeIconsLeftToRight()
	{
		var table = OriginalLayoutTables.ParkInfoWindow;
		Assert.AreEqual( new UiRect( 208, 30, 1632, 977 ), table.Root.Rect );
		Assert.AreEqual( "window3", table.Root.Model );
		var frame = table.Control( 0x4A68D1 );
		Assert.AreEqual( ("toughts", 186), (frame.Model, frame.Help) );
		Assert.AreEqual( new UiRect( 269, 633, 425, 173 ), frame.Rect );
		var icons = new uint[] { 0x4A68D2, 0x4A68D3, 0x4A68D4 }.Select( id => table.Control( id ) ).ToArray();
		Assert.IsTrue( icons.All( icon => icon.Model == "ti_hungry" && icon.Type == 6 ) );
		Assert.IsTrue( icons[0].Rect.X < icons[1].Rect.X && icons[1].Rect.X < icons[2].Rect.X );
		Assert.AreEqual( new UiRect( 291, 740, 382, 45 ), table.Control( 0x4A68CE ).Rect, "the title row under the icons" );
	}

	[TestMethod]
	public void TheWindowShowsThreeIconsFromTheRankingAndRefreshesEverySecond()
	{
		var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ );
		SetGuests( simulation, new Guest( 1, 0 ) { State = GuestState.WalkingAround, Thought = GuestThought.Thirsty }, new Guest( 2, 0 ) { State = GuestState.WalkingAround, Thought = GuestThought.Thirsty },
			new Guest( 3, 0 ) { State = GuestState.WalkingAround, Thought = GuestThought.Scared } );
		hud.OpenParkInfoWindow();
		Assert.IsTrue( hud.ParkInfoWindowOpen );
		var screen = hud.Stack.Top!;
		var table = OriginalLayoutTables.ParkInfoWindow;
		Assert.IsNotNull( screen.Find( "thoughtsFrame" ) );
		Assert.AreEqual( table.Control( 0x4A68D1 ).Rect, screen.Find( "thoughtsFrame" )!.Bounds );
		// slot 0 is the rightmost icon (0x4A68D4), slot 2 the leftmost (0x4A68D2)
		CollectionAssert.AreEqual( new[] { table.Control( 0x4A68D4 ).Rect, table.Control( 0x4A68D3 ).Rect, table.Control( 0x4A68D2 ).Rect }, new[] { "thought0", "thought1", "thought2" }.Select( id => screen.Find( id )!.Bounds ).ToArray() );
		var shown = hud.ParkInfoTopThoughts;
		Assert.AreEqual( (GuestThought.Scared, GuestThought.Thirsty), (shown.ThoughtAt( 0 ), shown.ThoughtAt( 2 )), "the window shows the ranking of the guests it was built with: thirsty keeps its starting slot 2" );
		Assert.AreEqual( (14, 1), (shown.FrameAt( 0 ), shown.FrameAt( 2 )) );
		Assert.IsNull( shown.ThoughtAt( 1 ), "slot 1 still holds thought 1, which nobody has" );

		// a change shows only on the next one-second tick of the window's timer
		SetGuests( simulation, new Guest( 1, 0 ) { State = GuestState.WalkingAround, Thought = GuestThought.Bored } );
		screen.Update( Context( 0.5f ), UiInput.Idle( new System.Numerics.Vector2( -1 ) ) );
		Assert.AreEqual( GuestThought.Scared, hud.ParkInfoTopThoughts.ThoughtAt( 0 ), "half a second: not yet" );
		screen.Update( Context( 0.5f ), UiInput.Idle( new System.Numerics.Vector2( -1 ) ) );
		Assert.AreEqual( GuestThought.Bored, hud.ParkInfoTopThoughts.ThoughtAt( 0 ), "the timer fired after one second" );
		Assert.AreEqual( 1f, ParkHud.ParkInfoRefreshSeconds );
	}

	[TestMethod]
	public void AnIconHiddenByAnEmptySlotStaysHiddenAndTheHoverTextNamesItsOwnThought()
	{
		var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ );
		var strings = new UiStringTable( "English", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
			thoughtNames: Enumerable.Range( 0, 23 ).Select( index => $"thought{index}" ).ToArray() );
		typeof( ParkHud ).GetField( "strings", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( hud, strings );
		SetGuests( simulation );
		hud.OpenParkInfoWindow();
		Assert.IsTrue( Enumerable.Range( 0, 3 ).All( slot => hud.ParkInfoIconFrame( slot ) == -1 ), "an empty park shows no icon" );
		SetGuests( simulation, new Guest( 1, 0 ) { State = GuestState.WalkingAround, Thought = GuestThought.Thirsty } );
		var screen = hud.Stack.Top!;
		screen.Update( Context( 1f ), UiInput.Idle( new System.Numerics.Vector2( -1 ) ) );
		Assert.AreEqual( GuestThought.Thirsty, hud.ParkInfoTopThoughts.ThoughtAt( 2 ), "the ranking moved on" );
		Assert.AreEqual( -1, hud.ParkInfoIconFrame( 2 ), "but the original never shows a hidden icon again while the window is open (0x65D9DD(1) is not called)" );
		Assert.IsNull( screen.Find( "thought2" )!.CurrentHelp );
		hud.Stack.Pop();
		hud.OpenParkInfoWindow();
		Assert.AreEqual( 1, hud.ParkInfoIconFrame( 2 ), "a new window shows it" );
		Assert.AreEqual( "thought2", hud.Stack.Top!.Find( "thought2" )!.CurrentHelp, "the hover text is THOUGHTS.str entry frame + 1: thirsty (id 2, frame 1)" );

		// each icon names its own thought: the getter 0x66DAD2 returns the frame the setter 0x66DA4D stored at +0x13C
		hud.Stack.Pop();
		SetGuests( simulation, new Guest( 1, 0 ) { State = GuestState.WalkingAround, Thought = GuestThought.Thirsty }, new Guest( 2, 0 ) { State = GuestState.WalkingAround, Thought = GuestThought.Thirsty },
			new Guest( 3, 0 ) { State = GuestState.WalkingAround, Thought = GuestThought.Scared } );
		hud.OpenParkInfoWindow();
		Assert.AreEqual( (14, -1, 1), (hud.ParkInfoIconFrame( 0 ), hud.ParkInfoIconFrame( 1 ), hud.ParkInfoIconFrame( 2 )) );
		Assert.AreEqual( ("thought15", null, "thought2"), (hud.Stack.Top!.Find( "thought0" )!.CurrentHelp, hud.Stack.Top!.Find( "thought1" )!.CurrentHelp, hud.Stack.Top!.Find( "thought2" )!.CurrentHelp),
			"scared (frame 14) names entry 15, thirsty (frame 1) entry 2, and the empty slot has no text" );
	}

	// ---- The guest-insights counters -------------------------------------------------------------

	private static ModSet InsightsOn() => ModSet.Resolve( Array.Empty<DiscoveredMod>(),
		new ModSettings { Enabled = new Dictionary<string, bool> { [OfficialExtensions.GuestInsights.Id] = true } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );

	[TestMethod]
	public void EachRefusalReasonCountsItsQueueEndsAndNothingElse()
	{
		var counters = new GuestRefusalCounters();
		var guest = new Guest( 1, 0 );
		foreach ( var reason in Enum.GetValues<QueueEndReason>() )
		{
			counters.QueueRejected( guest, null!, reason );
			counters.QueueEnded( guest, 1, reason, 5 );
		}
		Assert.AreEqual( 2, counters.Count( RefusalReason.TooExpensive ), "Price" );
		Assert.AreEqual( 4, counters.Count( RefusalReason.QueueTooLong ), "Full and QueueLimit" );
		Assert.AreEqual( 2, counters.Count( RefusalReason.TooScary ) );
		Assert.AreEqual( 2, counters.Count( RefusalReason.TooBoring ) );
		Assert.AreEqual( 4, counters.Count( RefusalReason.RideUnavailable ), "RideClosed and RideBroken" );
		Assert.AreEqual( 14, counters.Total, "the guest's own leaving tests, queue edits and removals are not refusals" );
		Assert.IsNull( GuestRefusalCounters.Classify( QueueEndReason.NotAccepting ) );
		Assert.IsNull( GuestRefusalCounters.Classify( QueueEndReason.Unhappy ) );
	}

	[TestMethod]
	public void OffByDefaultTheSimulationHasNoCountersAndNoObserver()
	{
		Assert.IsFalse( FeatureGates.GuestInsights );
		Assert.IsFalse( OfficialExtensions.GuestInsights.DefaultEnabled );
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
		Assert.IsNull( simulation.RefusalCounters );
		Assert.IsNull( simulation.Observer );
	}

	[TestMethod]
	public void ARunWithTheCountersOnIsTheSameRunWithTheSameSaveAndNothingIsRecorded()
	{
		var off = new ParkScenario( new WorldSeed( 1 ) );
		off.Run( 9000 );
		ParkScenario on;
		using ( OpenTpwExtensions.Use( InsightsOn() ) )
		{
			Assert.IsTrue( FeatureGates.GuestInsights );
			on = new ParkScenario( new WorldSeed( 1 ) );
			Assert.IsNotNull( on.Guests.RefusalCounters );
			on.Run( 9000 );
			Assert.IsNull( ParkResumeMods.Capture( OpenTpwExtensions.Active ), "a presentation-only extension is never recorded in a save" );
		}
		Assert.IsFalse( FeatureGates.GuestInsights );
		Assert.AreEqual( off.Hash, on.Hash, "the guests' state hash" );
		Assert.AreEqual( off.Guests.RandomState, on.Guests.RandomState, "no draw" );
		Assert.AreEqual( JsonSerializer.Serialize( off.Guests.CaptureSnapshot() ), JsonSerializer.Serialize( on.Guests.CaptureSnapshot() ), "the saved guests are byte for byte the same" );
		var counters = on.Guests.RefusalCounters!;
		Assert.IsTrue( counters.Total > 0, "the scenario's rides and shops turn guests away" );
		Assert.IsTrue( counters.Count( RefusalReason.TooScary ) + counters.Count( RefusalReason.TooBoring ) + counters.Count( RefusalReason.TooExpensive ) > 0, "some of the five reasons occur in the pinned run" );
	}

	[TestMethod]
	public void ACollectorAndTheCountersShareTheObserverSlot()
	{
		using var scope = OpenTpwExtensions.Use( InsightsOn() );
		var collector = new GuestMetrics();
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 ) { Observer = collector };
		Assert.IsInstanceOfType( simulation.Observer, typeof( GuestObserverChain ) );
		simulation.Observer = null;
		Assert.AreSame( simulation.RefusalCounters, simulation.Observer, "clearing the collector leaves the counters" );
	}

	[TestMethod]
	public void AssigningTheObserverAgainDoesNotChainTheCountersTwice()
	{
		using var scope = OpenTpwExtensions.Use( InsightsOn() );
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 ) { Observer = new GuestMetrics() };
		var chain = simulation.Observer;
		simulation.Observer = simulation.Observer;
		Assert.AreSame( chain, simulation.Observer );
		simulation.Observer = chain;
		Assert.AreSame( chain, simulation.Observer );
		var counters = simulation.RefusalCounters!;
		simulation.Observer = counters;
		Assert.AreSame( counters, simulation.Observer );
		simulation.Observer = new GuestMetrics();
		simulation.DisableRefusalCounters();
		Assert.IsNull( simulation.RefusalCounters );
		Assert.IsInstanceOfType( simulation.Observer, typeof( GuestMetrics ), "the collector stays when the counters are taken off" );
	}

	[TestMethod]
	public void TheWindowShowsTheCountersOnlyWhileTheExtensionIsOn()
	{
		var hudOff = VisitorWorldSelectionTests.Fixture( out _, out _ );
		hudOff.OpenParkInfoWindow();
		Assert.IsNull( hudOff.Stack.Top!.Find( "refusalsTitle" ), "off by default: no counters in the window" );
		Assert.IsNull( hudOff.Stack.Top!.Find( "refusalCount0" ) );

		using var scope = OpenTpwExtensions.Use( InsightsOn() );
		var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ );
		var counters = simulation.RefusalCounters!;
		var guest = new Guest( 1, 0 );
		counters.QueueEnded( guest, 1, QueueEndReason.Price, 3 );
		counters.QueueEnded( guest, 1, QueueEndReason.Price, 3 );
		counters.QueueRejected( guest, null!, QueueEndReason.Full );
		counters.QueueRejected( guest, null!, QueueEndReason.TooScary );
		counters.QueueRejected( guest, null!, QueueEndReason.TooBoring );
		counters.QueueRejected( guest, null!, QueueEndReason.TooBoring );
		counters.QueueRejected( guest, null!, QueueEndReason.TooBoring );
		counters.QueueEnded( guest, 1, QueueEndReason.RideBroken, 3 );
		hud.OpenParkInfoWindow();
		var screen = hud.Stack.Top!;
		Assert.IsNotNull( screen.Find( "refusalsTitle" ) );
		var shown = Enumerable.Range( 0, 5 ).Select( index => ((UiLabel)screen.Find( $"refusalCount{index}" )!).Text() ).ToArray();
		CollectionAssert.AreEqual( new[] { "2", "1", "1", "3", "1" }, shown, "too expensive, queue too long, too scary, too boring, ride broken or closed" );
		var names = Enumerable.Range( 0, 5 ).Select( index => ((UiLabel)screen.Find( $"refusal{index}" )!).Text() ).ToArray();
		Assert.AreEqual( 5, names.Distinct().Count() );
		Assert.IsTrue( names.All( name => name.Length > 0 ) );
		// the counters stand clear of the graph's plot and the toggles column
		var graph = screen.Find( "graph" )!.Bounds;
		foreach ( var id in new[] { "refusalsTitle" }.Concat( Enumerable.Range( 0, 5 ).SelectMany( index => new[] { $"refusal{index}", $"refusalCount{index}" } ) ) )
		{
			var box = screen.Find( id )!.Bounds;
			var overlaps = box.X < graph.X + graph.Width && graph.X < box.X + box.Width && box.Y < graph.Y + graph.Height && graph.Y < box.Y + box.Height;
			Assert.IsFalse( overlaps, $"{id} must not overlap the graph" );
		}
	}

	[TestMethod]
	public void TheRefusalStringsHaveAnOwnTextInEveryLanguage()
	{
		foreach ( var key in new[] { OpenTpwText.RefusalsTitle, OpenTpwText.RefusalTooExpensive, OpenTpwText.RefusalQueueTooLong, OpenTpwText.RefusalTooScary, OpenTpwText.RefusalTooBoring, OpenTpwText.RefusalRideUnavailable } )
		{
			var texts = SupplementaryStrings.SupportedLanguages.Select( language => SupplementaryStrings.Get( key, language ) ).ToList();
			Assert.AreEqual( texts.Count, texts.Distinct().Count(), $"{key}: a different text per language" );
			Assert.IsTrue( texts.All( text => text.Length is > 0 and < 60 ), $"{key}: short enough for the narrow column" );
		}
	}
}
