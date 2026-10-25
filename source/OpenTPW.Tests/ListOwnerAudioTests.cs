using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.Mods;
using OpenTPW.Online.Api;
using OpenTPW.Online.Packages;
using OpenTPW.UI;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>
/// The list owners' and keyboard paths' sound policy (docs/AUDIO.md, "List owners"; AUDIO-041 is retired). Generated samples distinguish 0x1F from 0xBD in the real mixer;
/// no original data, audio device or network is needed. Keyboard cases check the retained host UX, not original key parity.
/// </summary>
[TestClass, DoNotParallelize]
public class ListOwnerAudioTests
{
	private readonly Dictionary<FieldInfo, object?> saved = new();
	private readonly List<uint> fired = new();
	private AudioMixer mixer = null!;
	private Action<string?, uint, Vector3?>? oldHook;
	private BaseFileSystem previousFiles = null!;
	private string root = null!;

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object target, string property, object value ) => target.GetType()
		.GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-list-owner-audio-" ).FullName;
		previousFiles = FileSystem;
		FileSystem = new BaseFileSystem( root );
		mixer = new AudioMixer( null );
		var system = new SoundEventSystem( mixer, 7 );
		var category = Empty<SoundCategory>();
		var catalog = Empty<SoundCatalog>();
		var decoded = (Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem )
			.GetField( "decoded", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( system )!;
		var events = new List<SoundEvent>();
		for ( var index = 0; index < 2; index++ )
		{
			var id = index == 0 ? GameAudio.ClickEvent : GameAudio.SelectClickEvent;
			var sample = new SoundSample( 1, 65535, 0, (ushort)(index + 1), 0 );
			var sound = new SoundEntry( 100, 100, 0, 0, 0, 0, 1, 100, new[] { sample }, Array.Empty<SoundLink>(), Array.Empty<byte>() );
			events.Add( new SoundEvent( id, 0, 0, 0, new[] { sound } ) );
			decoded[(category, index + 1, 1)] = new Mp2Audio( AudioMixer.OutputRate, 1,
				Enumerable.Repeat( (short)(1000 + index * 1000), index == 0 ? 4 : 8 ).ToArray(), 1, 0 );
		}
		Set( catalog, "Events", events.ToArray() );
		Set( category, "Catalog", catalog );
		Set( category, "Name", "cat_ui" );
		Field( "events", system );
		Field( "<Ui>k__BackingField", category );
		oldHook = SoundEventSystem.Triggered;
		SoundEventSystem.Triggered = ( name, id, _ ) => { if ( name == "cat_ui" ) fired.Add( id ); };
	}

	private void Field( string name, object value )
	{
		var field = typeof( GameAudio ).GetField( name, BindingFlags.Static | BindingFlags.NonPublic )!;
		saved.Add( field, field.GetValue( null ) );
		field.SetValue( null, value );
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var pair in saved ) pair.Key.SetValue( null, pair.Value );
		SoundEventSystem.Triggered = oldHook;
		mixer.Dispose();
		FileSystem = previousFiles;
		Directory.Delete( root, recursive: true );
	}

	private void Sounds( params uint[] expected )
	{
		CollectionAssert.AreEqual( expected, fired.ToArray(), "the exact UI event sequence" );
		CollectionAssert.AreEqual( expected.Select( id => id == GameAudio.ClickEvent ? 4 : 8 ).ToArray(),
			mixer.Voices.Select( voice => voice.Frames ).ToArray(), "each event reaches its distinct synthetic mixer sample" );
		Assert.IsTrue( mixer.Voices.All( voice => voice.Position == null ), "UI clicks remain flat" );
	}

	private static UiContext Context( float time = 0 ) => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) )
		{ Canvas = new UiCanvas( 2048, 1536 ), Time = time };
	private static ParkLoadEntry[] Entries( int count ) => Enumerable.Range( 0, count )
		.Select( index => new ParkLoadEntry( "synthetic", -1, false, "/synthetic/park" + index + ".json", "Synthetic park " + index ) ).ToArray();
	private static UiScrollList List( UiScreen screen ) => (UiScrollList)screen.Find( "parks" )!;
	/// <summary>The middle of a drawn row: a list with column headers draws its rows below the header row.</summary>
	private static NVector2 RowPoint( UiScrollList list, int row )
	{
		var rect = list.ScreenRect( Context().Canvas );
		var headerRows = list.Columns == null ? 0 : 1;
		return new NVector2( rect.Center.X, rect.Y + (list.Margin + (headerRows + row - list.FirstRow + 0.5f) * list.RowHeight) * rect.Height / list.Bounds.Height );
	}

	[DataTestMethod]
	[DataRow( "mouse" )]
	[DataRow( "keyboard" )]
	[DataRow( "direct" )]
	public void LoadSelectionAndRowActivationAreSilentAndLoadTheSelectedEntryOnce( string input )
	{
		var stack = new UiScreenStack();
		var entries = Entries( 2 );
		var loaded = new List<ParkLoadEntry>();
		var screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), entries, loaded.Add );
		stack.Push( screen );
		var list = List( screen );
		Assert.IsFalse( list.ClicksOnActivate );
		stack.Update( Context(), UiInput.Click( RowPoint( list, 1 ) ) );
		Assert.AreEqual( 1, list.Selected );
		Assert.AreEqual( 0, loaded.Count, "the approved first-click selection does not load" );
		Sounds();
		if ( input == "mouse" ) stack.Update( Context(), UiInput.Click( RowPoint( list, 1 ) ) );
		else if ( input == "keyboard" ) stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
		else list.Activate();
		Assert.AreSame( entries[1], loaded.Single() );
		Sounds();
	}

	[TestMethod]
	public void LoadCombinedAndReentrantRowActivationKeepsItsGuardWithoutAnAddedClick()
	{
		var stack = new UiScreenStack();
		var entries = Entries( 1 );
		var loaded = new List<ParkLoadEntry>();
		UiScrollList? list = null;
		var screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), entries, entry =>
		{
			loaded.Add( entry );
			// Fail without unbounded recursion if the per-dispatch guard is ever removed.
			if ( loaded.Count == 1 ) list!.Activate();
		} );
		stack.Push( screen );
		list = List( screen );
		stack.Update( Context(), UiInput.Click( RowPoint( list, 0 ) ) with { Pressed = UiKeys.Accept } );
		Assert.AreSame( entries[0], loaded.Single(), "mouse, reentry and Enter share one dispatch" );
		Sounds();
		stack.Pop();
		list.Activate();
		screen.Update( Context(), UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( 1, loaded.Count, "a removed chooser rejects a stale row activation" );
		Sounds();
	}

	[TestMethod]
	public void LoadInvalidEmptyAndCancelledRowsDoNotLoadOrClick()
	{
		var stack = new UiScreenStack();
		var loaded = new List<ParkLoadEntry>();
		var screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), Entries( 1 ), loaded.Add );
		stack.Push( screen );
		var list = List( screen );
		foreach ( var invalid in new[] { -1, 1, int.MaxValue } ) { list.Selected = invalid; list.Activate(); }
		list.Selected = 0;
		stack.Update( Context(), UiInput.Key( UiKeys.Accept | UiKeys.Back ) );
		Assert.IsTrue( screen.IsRemoved );
		var empty = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), Entries( 0 ), loaded.Add );
		stack.Push( empty );
		Assert.IsFalse( List( empty ).Enabled );
		Assert.IsFalse( empty.Find( "loadSelected" )!.Enabled );
		List( empty ).Activate();
		empty.Find( "loadSelected" )!.Activate();
		Assert.AreEqual( 0, loaded.Count );
		Sounds();
	}

	[TestMethod]
	public void ARefusedLoadRemainsSilentAndTheNextFrameCanRetry()
	{
		var stack = new UiScreenStack();
		var attempts = 0;
		var entries = Entries( 1 );
		ParkLoadEntry? loaded = null;
		var screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), entries, entry =>
		{
			if ( ++attempts == 1 ) throw new InvalidDataException( "Synthetic refusal" );
			loaded = entry;
		} );
		stack.Push( screen );
		Assert.ThrowsException<InvalidDataException>( () => stack.Update( Context(), UiInput.Key( UiKeys.Accept ) ) );
		Sounds();
		stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( 2, attempts );
		Assert.AreSame( entries[0], loaded );
		Sounds();
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void TheSeparateLoadButtonKeepsExactlyOneOrdinaryClick( bool keyboard )
	{
		var stack = new UiScreenStack();
		var loaded = new List<ParkLoadEntry>();
		var entries = Entries( 2 );
		var screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), entries, loaded.Add );
		stack.Push( screen );
		List( screen ).Select( 1 );
		var button = (UiButton)screen.Find( "loadSelected" )!;
		if ( keyboard ) { screen.Focus( button ); stack.Update( Context(), UiInput.Key( UiKeys.Accept ) ); }
		else stack.Update( Context(), UiInput.Click( button.ScreenRect( Context().Canvas ).Center ) );
		Assert.AreSame( entries[1], loaded.Single() );
		Sounds( GameAudio.ClickEvent );
	}

	[TestMethod]
	public void LoadArrowButtonsKeepTheirClickAndWheelTrackAndRepeatedReleaseStaySilent()
	{
		var stack = new UiScreenStack();
		var loaded = new List<ParkLoadEntry>();
		var screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), Entries( 30 ), loaded.Add );
		stack.Push( screen );
		var list = List( screen );
		var arrow = (UiButton)screen.Find( "scrollDown" )!;
		arrow.Activate();
		Assert.AreEqual( 1, list.FirstRow );
		Sounds( GameAudio.SelectClickEvent );
		stack.Update( Context(), UiInput.Idle( RowPoint( list, 1 ) ) with { Wheel = -1 } );
		Assert.AreEqual( 2, list.FirstRow );
		var track = screen.Find( "loadScroll" )!.ScreenRect( Context().Canvas );
		var point = new NVector2( track.Center.X, track.Bottom - 1 );
		stack.Update( Context(), new UiInput( point, true, true, false, false, UiKeys.None ) );
		stack.Update( Context(), new UiInput( point, false, false, true, false, UiKeys.None, NotClick: true ) );
		Assert.IsTrue( list.FirstRow > 2 );
		list.Scroll( -100 );
		point = arrow.ScreenRect( Context().Canvas ).Center;
		stack.Update( Context(), new UiInput( point, true, true, false, false, UiKeys.None ) );
		stack.Update( Context( 0.5f ), new UiInput( point, true, false, false, false, UiKeys.None ) );
		stack.Update( Context( 0.7f ), new UiInput( point, false, false, true, false, UiKeys.None ) );
		Assert.AreEqual( 1, list.FirstRow );
		Assert.AreEqual( 0, loaded.Count );
		Sounds( GameAudio.SelectClickEvent );
	}

	private void WithOnline( bool other, ParkSummary[]? parks, Action<UiScreenStack, UiScreen, List<int>> test, Action<int>? advice = null )
		=> WithOnline( other, parks, ( _, stack, screen, advised ) => test( stack, screen, advised ), advice );

	private void WithOnline( bool other, ParkSummary[]? parks, Action<OnlineSession, UiScreenStack, UiScreen, List<int>> test, Action<int>? advice = null )
	{
		var folders = new OnlineFolders( root );
		folders.SaveSettings( new OnlineSettings( null, "synthetic-player" ) );
		using var session = new OnlineSession( folders );
		Set( session, other ? "CityParks" : "Parks", parks! );
		var stack = new UiScreenStack();
		stack.Push( new UiScreen( "synthetic-parent" ) );
		var advised = new List<int>();
		var screens = new OnlineScreens( stack, OriginalUiTests.FakeStrings(), new UiModels( name => throw new FileNotFoundException( name ) ), new OnlineHost
		{
			Session = session,
			Advise = id => { advised.Add( id ); advice?.Invoke( id ); }
		} );
		if ( other ) screens.ShowOtherParks( new CityInfo( 7, "Synthetic city", "", 0, 0, parks?.Length ?? 0 ) );
		else screens.ShowFindParks();
		test( session, stack, stack.Top!, advised );
	}

	private static ParkSummary[] Parks() => Enumerable.Range( 0, 2 ).Select( index => new ParkSummary(
		"synthetic-" + index, "Synthetic park " + index, "Synthetic description", "synthetic-author", "synthetic-level", "English",
		DateTimeOffset.UnixEpoch, 4, 2, false, false, 0, false, 0, 7 ) ).ToArray();

	private static ParkSummary[] NamedParks( params string[] names ) => names.Select( name => new ParkSummary(
		name, name, "Synthetic description", "synthetic-author", "synthetic-level", "English",
		DateTimeOffset.UnixEpoch, 4, 2, false, false, 0, false, 0, 7 ) ).ToArray();

	private static string RowName( UiScrollList list, int index ) => list.Rows()[index].Split( '\t' )[0];

	private static string DetailsName( UiScreenStack stack ) => ((UiLabel)stack.Top!.Find( "parkName" )!).Text();

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void OnlineSelectionMouseAndKeyboardActivationOpenTheIntendedDetailsSilently( bool other ) => WithOnline( other, Parks(), ( stack, screen, advised ) =>
	{
		var list = List( screen );
		Assert.IsFalse( list.ClicksOnActivate );
		Assert.AreEqual( other ? "otherParks" : "findParks", screen.Name );
		void Details( int index, int opens )
		{
			Assert.AreEqual( "parkDetails", stack.Top!.Name );
			Assert.AreEqual( Parks()[index].Name, ((UiLabel)stack.Top.Find( "parkName" )!).Text() );
			Assert.AreEqual( opens, advised.Count( id => id == RestoredAdvice.ParkDetailsHelp ) );
			Assert.AreEqual( 1, stack.Screens.Count( item => item.Name == "parkDetails" ), "at most one details window" );
			Sounds();
		}
		stack.Update( Context(), UiInput.Click( RowPoint( list, 1 ) ) );
		Details( 1, 1 );
		stack.Pop();
		stack.Update( Context(), UiInput.Click( RowPoint( list, 1 ) ) );
		Details( 1, 2 );
		stack.Pop();
		screen.Focus( list );
		stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
		Details( 1, 3 );
		// Direct repeated activation retains the owner's replace-details semantics, with one help post per call.
		list.Activate();
		Details( 1, 4 );
		list.Select( 0 );
		Details( 0, 5 );
	} );

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void OnlineEmptyPlaceholderAndInvalidSelectionsCannotOpenDetailsOrClick( bool other ) => WithOnline( other, Array.Empty<ParkSummary>(), ( stack, screen, advised ) =>
	{
		var list = List( screen );
		Assert.AreEqual( 1, list.Rows().Count, "the visible placeholder is not a park" );
		stack.Update( Context(), UiInput.Click( RowPoint( list, 0 ) ) );
		stack.Update( Context(), UiInput.Click( RowPoint( list, 0 ) ) );
		stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
		foreach ( var invalid in new[] { -1, 1, int.MaxValue } ) { list.Selected = invalid; list.Activate(); }
		Assert.AreSame( screen, stack.Top );
		Assert.AreEqual( 0, advised.Count( id => id == RestoredAdvice.ParkDetailsHelp ) );
		Sounds();
	} );

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void OtherParksSelectionChangedUsesRowsThatReplaceAnEmptyList( bool initiallyNull )
	{
		WithOnline( true, initiallyNull ? null : Array.Empty<ParkSummary>(), ( session, stack, screen, advised ) =>
		{
			var list = List( screen );
			Assert.AreEqual( 1, list.Rows().Count, "the empty list shows its placeholder" );
			stack.Update( Context(), UiInput.Click( RowPoint( list, 0 ) ) );
			Assert.AreEqual( "otherParks", stack.Top!.Name, "the placeholder is not a park" );
			Assert.AreEqual( 0, advised.Count( id => id == RestoredAdvice.ParkDetailsHelp ) );
			Set( session, "CityParks", NamedParks( "fresh-0", "fresh-1" ) );
			Assert.AreEqual( "fresh-0", RowName( list, 0 ) );
			stack.Update( Context(), UiInput.Click( RowPoint( list, 1 ) ) );
			Assert.AreEqual( "fresh-1", DetailsName( stack ) );
			Assert.AreEqual( 1, advised.Count( id => id == RestoredAdvice.OtherParksHelp ) );
			Assert.AreEqual( 1, advised.Count( id => id == RestoredAdvice.ParkDetailsHelp ) );
			Assert.AreEqual( 1, stack.Screens.Count( item => item.Name == "parkDetails" ) );
			Sounds();
		} );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void OtherParksKeyboardAndMouseActivationUseTheReplacedRowAtTheSelectedIndex( bool mouse )
	{
		WithOnline( true, NamedParks( "old-2", "old-1", "old-0" ), ( session, stack, screen, advised ) =>
		{
			var list = List( screen );
			list.Select( 1 );
			Assert.AreEqual( "old-1", DetailsName( stack ) );
			stack.Pop();
			Set( session, "CityParks", NamedParks( "new-2", "new-1", "new-0" ) );
			Assert.AreEqual( "new-1", RowName( list, 1 ) );
			screen.Focus( list );
			if ( mouse )
				stack.Update( Context(), UiInput.Click( RowPoint( list, 1 ) ) );
			else
				stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
			Assert.AreEqual( "new-1", DetailsName( stack ) );
			Assert.AreEqual( 1, advised.Count( id => id == RestoredAdvice.OtherParksHelp ) );
			Assert.AreEqual( 2, advised.Count( id => id == RestoredAdvice.ParkDetailsHelp ) );
			Assert.AreEqual( 1, stack.Screens.Count( item => item.Name == "parkDetails" ) );
			Sounds();
		} );
	}

	[TestMethod]
	public void OtherParksShrinkAndEmptyPlaceholderUseCurrentRowsAndStaySilent()
	{
		WithOnline( true, NamedParks( "old-0", "old-1", "old-2" ), ( session, stack, screen, advised ) =>
		{
			var list = List( screen );
			list.Select( 1 );
			Assert.AreEqual( "old-1", DetailsName( stack ) );
			stack.Pop();
			Set( session, "CityParks", NamedParks( "new-2", "new-1" ) );
			Assert.AreEqual( "new-1", RowName( list, 1 ) );
			screen.Focus( list );
			stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
			Assert.AreEqual( "new-1", DetailsName( stack ), "a shrink keeps the current row's identity" );
			stack.Pop();
			Set( session, "CityParks", Array.Empty<ParkSummary>() );
			Assert.AreEqual( 1, list.Rows().Count, "the empty list shows its placeholder" );
			Assert.AreNotEqual( "new-1", RowName( list, 0 ) );
			screen.Focus( list );
			stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
			Assert.AreEqual( "otherParks", stack.Top!.Name, "an out-of-range selection must not activate" );
			stack.Update( Context(), UiInput.Click( RowPoint( list, 0 ) ) );
			Assert.AreEqual( "otherParks", stack.Top!.Name, "the placeholder is not a park" );
			Assert.AreEqual( 1, advised.Count( id => id == RestoredAdvice.OtherParksHelp ) );
			Assert.AreEqual( 2, advised.Count( id => id == RestoredAdvice.ParkDetailsHelp ) );
			Assert.AreEqual( 0, stack.Screens.Count( item => item.Name == "parkDetails" ) );
			Sounds();
		} );
	}

	[TestMethod]
	public void SilentRowsDoNotMuteTheLoadersOwnSound()
	{
		var stack = new UiScreenStack();
		var calls = 0;
		var screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), Entries( 1 ), _ =>
		{
			calls++;
			GameAudio.PlayUi( GameAudio.SelectClickEvent );
		} );
		stack.Push( screen );
		stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( 1, calls );
		Sounds( GameAudio.SelectClickEvent );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void SilentOnlineRowsPreserveDownstreamAdvisorSound( bool other ) => WithOnline( other, Parks(), ( stack, screen, advised ) =>
	{
		List( screen ).Select( 0 );
		Assert.AreEqual( "parkDetails", stack.Top!.Name );
		Assert.AreEqual( 1, advised.Count( id => id == RestoredAdvice.ParkDetailsHelp ) );
		Sounds( GameAudio.SelectClickEvent );
	}, id => { if ( id == RestoredAdvice.ParkDetailsHelp ) GameAudio.PlayUi( GameAudio.SelectClickEvent ); } );

	[TestMethod]
	public void ListRowsAreSilentByDefaultAndOnlyAnOptedInScreenClicks()
	{
		var calls = 0;
		var list = new UiScrollList { Rows = () => new[] { "Synthetic row" }, Selected = 0, RowActivated = _ => calls++ };
		Assert.IsFalse( list.ClicksOnActivate, "no original list row posts 0x100 (docs/AUDIO.md, \"List owners\")" );
		list.Activate();
		Assert.AreEqual( 1, calls );
		Sounds();
		list.ClicksOnActivate = true;
		list.Activate();
		Assert.AreEqual( 2, calls );
		Sounds( GameAudio.ClickEvent );
		list.RowActivated = null;
		list.Activate();
		Sounds( GameAudio.ClickEvent );
	}

	[TestMethod]
	public void TheThemeParkIncTextHudOptsItsBuildListIn()
	{
		var source = File.ReadAllText( Path.Combine( RepositoryRoot(), "source", "OpenTPW", "Client", "ThemeParkIncHud.cs" ) );
		var list = source[source.IndexOf( "Id = \"buildList\"", StringComparison.Ordinal )..];
		StringAssert.Contains( list[..list.IndexOf( "} );", StringComparison.Ordinal )], "ClicksOnActivate = true", "the EXT text HUD keeps its build list's click" );
	}

	private static string RepositoryRoot()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
			if ( File.Exists( Path.Combine( directory.FullName, "source", "OpenTPW.sln" ) ) )
				return directory.FullName;
		throw new DirectoryNotFoundException( "repository root" );
	}

	[TestMethod]
	public void FindParksEnterClicksOnceThroughTheSearchButton()
	{
		// focusing the search field turns the global text entry on; restore it so later tests (the wave machine window) can open
		var previousCapture = Input.TextEntryActive;
		try
		{
			WithOnline( false, Parks(), ( stack, screen, _ ) =>
			{
				var search = (UiTextField)screen.Find( "search" )!;
				screen.Focus( search );
				stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
				Assert.AreSame( screen, stack.Top, "the search runs in place" );
				Sounds( GameAudio.ClickEvent );
			} );
		}
		finally { Input.TextEntryActive = previousCapture; }
	}

	[TestMethod]
	public void EmployeeRenameEnterClicksAndAppliesWhileEscapeClosesSilently()
	{
		var previousCapture = Input.TextEntryActive;
		try
		{
			foreach ( var escape in new[] { false, true } )
			{
				Input.TextEntryActive = false;
				var economy = EconomyTestData.Park();
				var member = EconomyTestData.HireBest( economy, StaffType.Handyman );
				var hud = StaffWindowTests.Hud( economy );
				hud.GetType().GetField( "lastLocatedStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( hud, new Dictionary<StaffType, int>() );
				hud.OpenEmployeeWindow( member );
				hud.Stack.Top!.Find( "title" )!.Activate();
				var dialog = hud.Stack.Top!;
				Assert.AreEqual( "employeeRename", dialog.Name );
				fired.Clear();
				dialog.Update( Context(), UiInput.Type( "Synthetic" ) );
				dialog.Update( Context(), UiInput.Key( escape ? UiKeys.Back : UiKeys.Accept ) );
				Assert.AreEqual( "employee", hud.Stack.Top!.Name );
				Assert.AreEqual( escape ? null : "Synthetic", member.Name );
				CollectionAssert.AreEqual( escape ? Array.Empty<uint>() : new[] { GameAudio.ClickEvent }, fired.ToArray(),
					"Enter is the tick's 0x100 (0x4b640a, the window's style 1: 0x1F); Escape closes without one" );
			}
		}
		finally { Input.TextEntryActive = previousCapture; }
	}

	[TestMethod]
	public void ParkRenameEnterMovesToTheSecondLineAndThenClicksAndApplies()
	{
		var previousCapture = Input.TextEntryActive;
		try
		{
			Input.TextEntryActive = false;
			var hud = StaffWindowTests.Hud( EconomyTestData.Park() );
			var level = (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
			typeof( Level ).GetField( "<ParkName>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, new ParkNameLines( "Synthetic", "Park" ) );
			hud.OpenParkRenameDialog();
			var dialog = hud.Stack.Top!;
			Assert.AreEqual( "parkRename", dialog.Name );
			fired.Clear();
			dialog.Update( Context(), UiInput.Key( UiKeys.Accept ) );
			Assert.AreSame( dialog.Find( "line2" ), dialog.Focused, "Enter in the first line moves the focus (0x499711), no 0x100" );
			Sounds();
			dialog.Update( Context(), UiInput.Key( UiKeys.Accept ) );
			Assert.AreNotSame( dialog, hud.Stack.Top, "the tick applied and closed" );
			Assert.AreEqual( new ParkNameLines( "Synthetic", "Park" ), level.ParkName );
			CollectionAssert.AreEqual( new[] { GameAudio.ClickEvent }, fired.ToArray(), "Enter in the second line is the tick's 0x100 (0x49973e): 0x1F" );
		}
		finally { Input.TextEntryActive = previousCapture; }
	}

	[TestMethod]
	public void ARefusedHirePostsItsMessageWithoutAnErrorSound()
	{
		var hud = StaffWindowTests.Hud( EconomyTestData.Park() );
		hud.OpenStaffWindow();
		fired.Clear();
		hud.RefuseHire();
		Assert.IsTrue( hud.Messages.Count > 0, "the OpenTPW message stays" );
		Assert.IsFalse( fired.Contains( GameAudio.ErrorEvent ), "the hire row (0x49b988) plays no cat_ui sound" );
		Sounds();
	}

	[TestMethod]
	public void AStillClickOnTheLoadListThumbIsSilent()
	{
		var stack = new UiScreenStack();
		var screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), Entries( 30 ), _ => { } );
		stack.Push( screen );
		var knob = screen.Find( "loadScroll" )!.ScreenRect( Context().Canvas );
		var point = new NVector2( knob.Center.X, knob.Y + 2 );
		stack.Update( Context(), new UiInput( point, true, true, false, false, UiKeys.None ) );
		stack.Update( Context(), UiInput.Click( point ) );
		Sounds();
	}

	[TestMethod]
	public void TheConcreteExtensionsListStillClicksAndToggles()
	{
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), new ModsServices
		{
			Discovered = () => Array.Empty<DiscoveredMod>(),
			SettingsPath = Path.Combine( root, "synthetic-mods.json" ),
			Active = () => ModSet.Defaults( OfficialExtensions.All ),
			OpenTpwVersion = null,
			Edition = () => GameEdition.ThemeParkWorld,
			ScriptModsOff = () => false,
			Browser = false
		} );
		stack.Push( screen );
		var list = (UiScrollList)screen.Find( "extensions" )!;
		Assert.IsTrue( list.ClicksOnActivate );
		list.Select( 0 );
		var toggle = (UiButton)screen.Find( "extensionsOn" )!;
		var before = toggle.Selected!();
		list.Activate();
		Assert.AreEqual( !before, toggle.Selected!() );
		Sounds( GameAudio.ClickEvent );
		stack.Pop();
	}
}
