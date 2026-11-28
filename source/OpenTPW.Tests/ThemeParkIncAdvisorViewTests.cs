using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's advisor on screen (docs/reverse/TPI-ADVISOR.md §4.4): the accessory selector <c>0x715CF0</c>, the clip-start update <c>0x715ED0</c>, the clip timeline, the mouth the frames
/// left and the display clock, all GPU-free, and with the retail data (OPENTPW_TPI_PATH) the Science advisor's model and the run hash with and without the client's reads.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ThemeParkIncAdvisorViewTests : ThemeParkIncScienceTestBase
{
	// meshes 0..33 (0 the bug head, 32 the body), the dummies after them; attribute k names node 34 + k
	private const int Antenna1 = 34, Antenna2 = 35, Hand = 36, Hat = 37, AddOn = 38, SubOff = 39, Ribbon = 40;

	private static AdvisorNodeMap Map()
	{
		var names = Enumerable.Range( 0, 34 ).Select( index => index switch { 0 => "Bug Head", 32 => "Body", _ => $"mesh {index}" } )
			.Concat( new[] { "Left Antennae", "Right Antennae", "Right hand", "Hat", "Helmet", "Collar", "Ribbon" } ).ToArray();
		var attributes = new (uint, uint)[] { (0x411, 0x13), (0x411, 0x14), (0x401, 0x15), (0x411, 5), (0x401, 30), (0x411, 31), (0x411, 0x31) };
		return new AdvisorNodeMap( names.Length, 34, attributes, names.Select( ( name, index ) => (name, index) ).ToArray() );
	}

	private static readonly AdvisorThemeCostume Theme = new( new[] { 30, 0, 0, 0 }, new[] { 31, 0, 0, 0 } );

	private static HashSet<int> Selected( int selector, IEnumerable<int>? hiddenBefore = null, bool suppressed = false )
	{
		var hidden = new HashSet<int>( hiddenBefore ?? Array.Empty<int>() );
		ThemeParkIncAdvisorAccessories.ApplySelector( Map(), selector, Theme, suppressed, hidden );
		return hidden;
	}

	[TestMethod]
	public void TheSelectorFollowsTheIncTableNotTheWorldOne()
	{
		var antennae = new[] { Antenna1, Antenna2 };
		foreach ( var selector in new[] { 0, 2 } )
			Assert.IsFalse( Selected( selector, antennae ).Overlaps( antennae ), $"selector {selector} shows the antennae (0x715DB5)" );
		foreach ( var selector in new[] { 3, 4, 5, 6, 7, 9, 10, 11, 13, 24 } )
			Assert.IsTrue( Selected( selector ).IsSupersetOf( antennae ), $"selector {selector} hides the antennae (0x715D82)" );
		foreach ( var selector in new[] { 1, 8, 12, 15, 23, 25 } )
			Assert.AreEqual( 0, Selected( selector ).Count, $"selector {selector} hides nothing (0x715E1A)" );
		Assert.IsTrue( Selected( 14 ).SetEquals( new[] { Hand } ), "selector 14 hides the right hand (0x715DA2)" );
		// where Theme Park World's 0x598CA0 hides the antennae for 1 and leaves them for 3
		var world = new HashSet<int>();
		AdvisorAccessories.ApplySelector( Map(), 1, Theme, false, world );
		Assert.IsTrue( world.IsSupersetOf( antennae ) );
		Assert.IsFalse( Selected( 5, new[] { Hat } ).Contains( Hat ), "a nonzero selector shows its own node" );
		Assert.IsTrue( Selected( 5, new[] { Hat }, suppressed: true ).Contains( Hat ), "unless the clip hides the costume" );
	}

	[TestMethod]
	public void TheAwardSelectorsSwapTexturesAndShowNothing()
	{
		for ( var selector = ThemeParkIncAdvisorAccessories.FirstAwardSelector; selector <= ThemeParkIncAdvisorAccessories.LastAwardSelector; selector++ )
			Assert.IsTrue( Selected( selector, new[] { Ribbon, Antenna1 } ).SetEquals( new[] { Ribbon, Antenna1 } ), $"selector {selector:X}" );
	}

	[TestMethod]
	public void TheThemeCostumeShowsItsAddOnsAndHidesItsSubOffs()
	{
		Assert.IsTrue( Selected( -1, new[] { AddOn } ).SetEquals( new[] { SubOff } ) );
		Assert.IsTrue( Selected( -1, new[] { AddOn }, suppressed: true ).SetEquals( new[] { AddOn, SubOff } ), "a clip that hides the costume keeps the add-ons hidden" );
	}

	[TestMethod]
	public void AClipStartHidesItsNodesShowsTheBodyAndDressesTheHead()
	{
		var clip = new ModelAnimation { NodeList = new ushort[] { 32, 5, (ushort)AddOn, 200 } };
		var hidden = ThemeParkIncAdvisorAccessories.ClipStartHidden( Map(), clip, -1, 0, Theme, 0, headSuppressed: false );
		Assert.IsTrue( hidden.SetEquals( new[] { 5, SubOff } ), "the node list less the body (mesh record 32) and the theme's add-on, plus its sub-off; a node past the model is ignored" );
		var suppressedHead = ThemeParkIncAdvisorAccessories.ClipStartHidden( Map(), clip, -1, 5, Theme, 0, headSuppressed: true );
		Assert.IsTrue( suppressedHead.SetEquals( new[] { 32, 5, AddOn } ), "a head without its parts (0xBF7241) hides the body and takes no selector" );
		var costumeHidden = ThemeParkIncAdvisorAccessories.ClipStartHidden( Map(), clip, -1, 0, Theme, AdvisorAccessories.SuppressCostumeBit, headSuppressed: false );
		Assert.IsTrue( costumeHidden.SetEquals( new[] { 0, 5, AddOn, SubOff } ), "bit 0x10000 hides the bug head and the costume" );
		var second = ThemeParkIncAdvisorAccessories.ClipStartHidden( Map(), new ModelAnimation { NodeList = new ushort[] { (ushort)Hat } }, 0, 5, Theme, 0, headSuppressed: false );
		Assert.IsTrue( second.SetEquals( new[] { Antenna1, Antenna2 } ), "the second selector (+0x18) applies after the first" );
	}

	[TestMethod]
	public void AModelWithoutMeshRecord32KeepsItsNodes()
	{
		var map = new AdvisorNodeMap( 37, 29, Array.Empty<(uint, uint)>(), Enumerable.Range( 0, 29 ).Select( index => ($"mesh {index}", index) ).ToArray() );
		var hidden = ThemeParkIncAdvisorAccessories.ClipStartHidden( map, new ModelAnimation { NodeList = new ushort[] { 30 } }, 0, 0, AdvisorThemeCostume.None, 0, headSuppressed: true );
		Assert.IsTrue( hidden.SetEquals( new[] { 30 } ), "the global advisor's 29 meshes have no record 32: the update touches none" );
	}

	[TestMethod]
	public void TheMouthMeshIsThePresentationsMouth()
	{
		Assert.AreEqual( (null, "Mouth - Normal", "Mouth - Sss", null, null),
			(ThemeParkIncAdvisorAccessories.MouthMesh( 0, 0 ), ThemeParkIncAdvisorAccessories.MouthMesh( 1, 0 ), ThemeParkIncAdvisorAccessories.MouthMesh( 5, 0 ),
				ThemeParkIncAdvisorAccessories.MouthMesh( 3, AdvisorAccessories.SuppressCostumeBit ), ThemeParkIncAdvisorAccessories.MouthMesh( 6, 0 )) );
	}

	private static uint[] Lengths( params (int Clip, uint Milliseconds)[] clips )
	{
		var lengths = new uint[ThemeParkIncAdvisorTimeline.MaxClip + 1];
		foreach ( var (clip, milliseconds) in clips )
			lengths[clip] = milliseconds;
		return lengths;
	}

	[TestMethod]
	public void TheClipsPlayBackToBackForTheirOwnLengths()
	{
		var lengths = Lengths( (14, 1000), (5, 300), (15, 666) );
		int[] clips = { 14, 5, 7, 15 }, bits = { 0, 2, 0, 0 };
		ThemeParkIncAdvisorClip? At( uint elapsed, uint? ended = null ) => ThemeParkIncAdvisorTimeline.At( clips, bits, lengths, elapsed, ended );
		Assert.AreEqual( new ThemeParkIncAdvisorClip( 14, 0, 0, 0 ), At( 0 ) );
		Assert.AreEqual( new ThemeParkIncAdvisorClip( 14, 0, 15, 0 ), At( 500 ), "30 ticks a second" );
		Assert.AreEqual( new ThemeParkIncAdvisorClip( 5, 1, 0, 2 ), At( 1000 ), "clip 14 plays its own length, which the sequence table zeroes" );
		Assert.AreEqual( 15, At( 1300 )!.Value.Clip, "a clip the model lacks (7) ends at once" );
		Assert.IsNull( At( 1966 ), "the model hides after the last clip" );
		Assert.AreEqual( 5, At( 1200, ended: 1100 )!.Value.Clip, "a clip that started plays to its end" );
		Assert.IsNull( At( 1300, ended: 1100 ), "no frame starts a clip once the speech has ended" );
		Assert.AreEqual( 15, At( 1300, ended: 1301 )!.Value.Clip );
		Assert.AreEqual( 15, At( 1300, ended: 1300 )!.Value.Clip, "the frame that ends the speech still starts the next clip (0x716F99 falls through to 0x717028)" );
	}

	[TestMethod]
	public void TheTurnFractionRunsFromEachTurnsFirstTick()
	{
		var first = ParkCalendar.TickOfTurn( 5 );
		var next = ParkCalendar.TickOfTurn( 6 );
		Assert.AreEqual( 0, ParkCalendar.TurnFraction( first ) );
		Assert.AreEqual( (double)(next - 1 - first) / (next - first), ParkCalendar.TurnFraction( next - 1 ) );
		Assert.IsTrue( Enumerable.Range( 0, 200 ).All( tick => ParkCalendar.TurnFraction( tick ) is >= 0 and < 1 ) );
	}

	private sealed class Speech( uint? voice = null, uint[]? lips = null ) : IThemeParkIncAdvisorSpeechData
	{
		public uint? VoiceLength( ThemeParkIncAdviceResponse response ) => voice;
		public IReadOnlyList<uint>? LipMarks( ThemeParkIncAdviceResponse response ) => lips;
		public AdvisorClipDurations? ClipDurations => new( ThemeParkIncAdvisorPresentationTests.TrailerWords, AdvisorClipDurations.ThemeParkIncCount );
	}

	private static ThemeParkIncAdvisor Advisor( IThemeParkIncAdvisorSpeechData speech ) =>
		new( ThemeParkIncAdvisorSettings.Bind( System.Text.Encoding.ASCII.GetBytes( "GeneralAdvisor.MinScoreForConsideration 10\n" ) ), ThemeParkIncAdvisorTable.Shipped, speech, 1 );

	[TestMethod]
	public void TheDisplayClockRunsThroughTheTicksSpan()
	{
		var advisor = Advisor( new Speech() );
		advisor.AdvanceClock( 1 );
		advisor.AdvanceClock( 2 );
		Assert.AreEqual( (248_000L, 372_000L), (advisor.PreviousClockMicroseconds, advisor.ClockMicroseconds) );
		Assert.AreEqual( (248u, 310u, 372u, 248u), (advisor.DisplayMilliseconds( 0 ), advisor.DisplayMilliseconds( 0.5 ), advisor.DisplayMilliseconds( 1 ), advisor.DisplayMilliseconds( double.NaN )) );
		advisor.Restore( advisor.Capture() );
		Assert.AreEqual( advisor.ClockMicroseconds, advisor.PreviousClockMicroseconds, "a load starts the display at the saved clock" );
	}

	[TestMethod]
	public void TheMouthAtAMomentIsTheOneTheLastFrameBeforeItLeft()
	{
		// a talking LIP (marks far apart) and a voice that plays on: the mouth changes every 100 ms or more
		var presentation = new ThemeParkIncAdvisorPresentation( new Speech( 9_000, new uint[] { 8_000_000 } ), 1 );
		Assert.AreNotEqual( 0u, presentation.Speak( 645, 1_000, false, true, false ) );
		Assert.AreEqual( (true, (uint?)null), (presentation.ModelShown, presentation.EndedAt) );
		var frames = new List<(uint Time, int Mouth)>();
		for ( var time = 1_000u; time < 3_000; time += 31 )
		{
			presentation.Frame( time );
			frames.Add( (time, presentation.Mouth) );
		}
		Assert.IsTrue( frames.Select( frame => frame.Mouth ).Distinct().Count() > 1, "the mouth moved" );
		foreach ( var (time, mouth) in frames.TakeLast( ThemeParkIncAdvisorPresentation.TraceLength ) )
		{
			Assert.AreEqual( mouth, presentation.MouthAt( time ) );
			Assert.AreEqual( mouth, presentation.MouthAt( time + 30 ), "a moment between two frames shows the earlier one's mouth" );
		}
		Assert.AreEqual( presentation.Mouth, presentation.MouthAt( 0 ), "before every kept frame: the current mouth" );
		var state = presentation.RandomState;
		_ = frames.Select( frame => presentation.MouthAt( frame.Time ) ).ToArray();
		Assert.AreEqual( state, presentation.RandomState, "reading the mouth draws nothing" );
		presentation.Stop();
		Assert.AreEqual( (false, (uint?)null), (presentation.ModelShown, presentation.EndedAt), "the stop kills the model (0x4650B0)" );
		presentation.Reset( 1 );
		Assert.AreEqual( (false, 0), (presentation.ModelShown, presentation.MouthAt( 2_000 )), "the init clears the trace and the mouth" );
	}

	[TestMethod]
	public void ASpeechThatEndsInAFrameRecordsWhen()
	{
		var presentation = new ThemeParkIncAdvisorPresentation( new Speech( 100 ), 1 );
		presentation.Speak( 645, 1_000, false, true, false );
		var time = 1_000u;
		while ( presentation.Speaking )
			presentation.Frame( time += 31 );
		Assert.AreEqual( (true, (uint?)time), (presentation.ModelShown, presentation.EndedAt), "the clips already started play on after the speech" );
	}

	private static ThemeParkIncAdvisorPresentation Shown( bool headHidden = false, uint start = 1_000 )
	{
		var presentation = new ThemeParkIncAdvisorPresentation( new Speech(), 1 );
		presentation.Restore( new ThemeParkIncAdvisorPresentationState( 1, true, 645, start, start - 200, start + 2_357, 0, false, 0, headHidden, false, false, 0, 0, false, 1, 0, 0,
			new[] { 25, 25, 25, 25, 26 }, new int[5], true ) );
		return presentation;
	}

	[TestMethod]
	public void TheViewShowsAShownHeadsClipsFromTheSpeechsStart()
	{
		var lengths = Lengths( (25, 633), (26, 2_966) );
		Assert.AreEqual( new ThemeParkIncAdvisorClip( 25, 2, 9.99f, 0 ), ThemeParkIncAdvisorView.Playing( Shown(), lengths, 1_000 + 2 * 633 + 333 ) );
		Assert.IsNull( ThemeParkIncAdvisorView.Playing( Shown(), lengths, 999 ), "a frame before the speech's start shows nothing" );
		Assert.IsNull( ThemeParkIncAdvisorView.Playing( Shown( headHidden: true ), lengths, 1_100 ), "a hidden head moves the models out of sight (0x464E00)" );
		Assert.IsNull( ThemeParkIncAdvisorView.Playing( Shown(), null, 1_100 ), "no model, nothing to show" );
		var start = uint.MaxValue - 100;
		Assert.AreEqual( 25, ThemeParkIncAdvisorView.Playing( Shown( start: start ), lengths, unchecked( start + 300 ) )!.Value.Clip, "the 32-bit clock wraps" );
	}

	[TestMethod]
	public void TheIncAdvisorBoxReachesPastTheRightEdge()
	{
		Assert.AreEqual( (1080, 720, 640, 480), ThemeParkIncAdvisorView.BoxRectangle( new Point2( 1600, 1200 ) ) );
		Assert.AreEqual( (960, 720, 640, 480), Advisor_BoxRectangle( new Point2( 1600, 1200 ) ), "Theme Park World's box ends at the screen's edge" );
		Assert.AreEqual( OpenTPW.Advisor.ModelTransform( new Point2( 1600, 1200 ) ), OpenTPW.Advisor.ModelTransform( new Point2( 1600, 1200 ), OpenTPW.Advisor.Depth ) );
		Assert.AreEqual( 0.9f, OpenTPW.Advisor.ModelTransform( new Point2( 1600, 1200 ), ThemeParkIncAdvisorView.Depth ).M43 );
	}

	private static (int, int, int, int) Advisor_BoxRectangle( Point2 target ) => OpenTPW.Advisor.BoxRectangle( target );

	[TestMethod]
	public void TheResponseRowsAreReadOnly()
	{
		Assert.IsInstanceOfType( ThemeParkIncAdviceResponse.All, typeof( System.Collections.ObjectModel.ReadOnlyCollection<ThemeParkIncAdviceResponse> ) );
		Assert.IsTrue( ThemeParkIncAdviceResponse.All.All( row => row.SecondAccessory == 0 ), "no played row has a second accessory" );
	}

	// with the retail data

	private static string ScienceAdvisor( string member ) => $"/levels/science/advisor/{member}";

	[TestMethod]
	public void TheScienceForecastAdvisorWearsTheThemeHelmetAndHoldsTheLetter()
	{
		ScienceEconomy();
		var model = new ModelFile( ScienceAdvisor( "Advisor_SCI.MD2" ) );
		var map = AdvisorNodeMap.From( model );
		var clips = new Dictionary<int, ModelAnimation?>();
		ModelAnimation? Clip( int number ) => clips.TryGetValue( number, out var clip ) ? clip
			: clips[number] = FileSystem.GetFiles( "/levels/science/advisor" ).Select( Path.GetFileName ).FirstOrDefault( name => string.Equals( name, $"Advisor_SCIm{number}.MD2", StringComparison.OrdinalIgnoreCase ) ) is { } member
				? new ModelFile( ScienceAdvisor( member ) ).Clip : null;
		var lengths = ThemeParkIncAdvisorTimeline.Lengths( Clip );
		Assert.AreEqual( (633u, 2_966u, 1_000u, 666u), (lengths[25], lengths[26], lengths[14], lengths[15]), "the clips' own lengths: clip 26 plays 2966 ms where the sequence table reads 25 ms" );
		var theme = AdvisorThemeCostume.Load( FileSystem, "science" );
		var hidden = ThemeParkIncAdvisorAccessories.ClipStartHidden( map, Clip( 25 ), -1, 0, theme, 0, headSuppressed: false );
		var shown = map.Meshes.Where( mesh => !hidden.Contains( mesh.Node ) ).Select( mesh => mesh.Name ).ToHashSet();
		Assert.IsTrue( shown.IsSupersetOf( new[] { "Bug Head", "Body", "NGon05", "letter", "Left Eye", "Right Eye", "Left Hand i", "Right hand i" } ), string.Join( ", ", shown ) );
		Assert.IsFalse( shown.Overlaps( new[] { "Left Antennae", "Right Antennae", "Ticket", "Spatula", "Left Hand", "Right hand" } ), string.Join( ", ", shown ) );
		Assert.IsTrue( OpenTPW.Advisor.Mouths.All( mouth => map.Meshes.Any( mesh => mesh.Name == mouth ) ), "the five mouths are the level advisor's" );
	}

	private static void Run( ParkEconomy economy, int turns, Action? read = null )
	{
		var end = ParkCalendar.TickOfTurn( economy.Turn + turns );
		while ( economy.Tick < end )
		{
			economy.Advance( 1 );
			read?.Invoke();
		}
	}

	[TestMethod]
	public void TheClientsReadsLeaveTheRunAsItIs()
	{
		var lengths = Lengths( (25, 633), (26, 2_966) );
		string Played( bool reads )
		{
			var economy = ScienceEconomy();
			Run( economy, 8 );
			Assert.IsTrue( economy.PostThemeParkIncAdvice( 0x157 ) );
			var advisor = economy.TpiAdvisor!;
			var shown = 0;
			Run( economy, 30, reads ? () =>
			{
				var now = advisor.DisplayMilliseconds( ParkCalendar.TurnFraction( economy.Tick ) );
				if ( ThemeParkIncAdvisorView.Playing( advisor.Presentation, lengths, now ) != null )
					shown++;
				_ = advisor.Presentation.MouthAt( now );
			} : null );
			Assert.IsTrue( !reads || shown > 0, "the view saw the advisor" );
			return ParkSaveFile.Serialize( economy );
		}
		Assert.AreEqual( Played( false ), Played( true ) );
	}

	[TestMethod]
	public void TheWorldGeneratorIsTheSameWithAndWithoutTheSpeechData()
	{
		(uint World, uint Window, uint Presentation) Played( bool speech )
		{
			var economy = ScienceEconomy();
			if ( !speech )
			{
				// the speech data are read while the files are an empty folder, so the advisor keeps none: no voice, no LIP, no clip lengths
				var data = FileSystem;
				var empty = Directory.CreateTempSubdirectory( "opentpw-no-speech-" );
				try
				{
					FileSystem = new BaseFileSystem( empty.FullName );
					economy.TpiAdvisor!.PreloadSpeech();
				}
				finally
				{
					FileSystem = data;
					empty.Delete( recursive: true );
				}
			}
			Run( economy, 8 );
			Assert.IsTrue( economy.PostThemeParkIncAdvice( 0x157 ) );
			Run( economy, 60 );
			return (economy.World.State, economy.TpiAdvisor!.LastWindow, economy.TpiAdvisor.Presentation.RandomState);
		}
		var with = Played( true );
		var without = Played( false );
		Assert.AreEqual( with.World, without.World, "the presentation never draws from the world generator" );
		Assert.AreNotEqual( with.Window, without.Window, "the speech data decide the window (and so the presentation stream and the run hash)" );
	}
}
