using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.Online.Api;
using OpenTPW.Online.Chat;
using OpenTPW.UI;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NMatrix4x4 = System.Numerics.Matrix4x4;

namespace OpenTPW.Tests;

/// <summary>Custom pointer policy must survive routed dispatch without replaying a screen's frame work.</summary>
[TestClass, DoNotParallelize]
public class CustomRoutedPointerTests
{
	private bool rmbCancel;
	private bool textEntryActive;
	private static UiContext Context() => OriginalUiTests.FakeContext( 2048, 1536 );
	private static UiButton Button( UiScreen screen, Action clicked ) => screen.Add( new UiButton
		{ Id = "test", Bounds = new UiRect( 100, 100, 100, 100 ), Anchor = UiAnchor.TopLeft, Clicked = clicked } );
	private static UiInput Right( NVector2 point ) => UiInput.Idle( point ) with { RightPressed = true };

	[TestInitialize]
	public void Initialize()
	{
		rmbCancel = GameOptions.Current.RmbCancel;
		textEntryActive = Input.TextEntryActive;
		GameOptions.Current.RmbCancel = true;
	}

	[TestCleanup]
	public void Cleanup()
	{
		GameOptions.Current.RmbCancel = rmbCancel;
		Input.TextEntryActive = textEntryActive;
	}

	[TestMethod]
	public void ProgressChoiceRoutedActivationIsGuardedWithoutReplayingFrameOrAccept()
	{
		var stack = new UiScreenStack();
		var screen = new ProgressChoiceScreen( "choice" );
		stack.Push( screen );
		var clicks = 0; var updating = 0; var accepts = 0;
		screen.Updating = _ => updating++;
		screen.Accept = () => accepts++;
		var button = Button( screen, () =>
		{
			if ( screen.TryActivate( stack ) ) clicks++;
			if ( screen.TryActivate( stack ) ) clicks++;
		} );
		var context = Context(); var point = button.ScreenRect( context.Canvas ).Center;
		screen.Update( context, UiInput.Idle( point ) with { PointerSuppressed = true } );
		screen.UpdatePointer( context, UiInput.Click( point ) with { Pressed = UiKeys.Accept }, button );
		Assert.AreEqual( 1, clicks ); Assert.AreEqual( 1, updating ); Assert.AreEqual( 0, accepts );
		screen.UpdatePointer( context, UiInput.Click( point ), button );
		Assert.AreEqual( 2, clicks, "A fresh pointer dispatch may activate again." );
		var backs = 0; screen.Back = () => backs++;
		screen.UpdatePointer( context, UiInput.Click( point ) with { RightPressed = true }, button );
		Assert.AreEqual( 1, backs ); Assert.AreEqual( 2, clicks, "Cancellation precedes an activation in the same delivery." );
	}

	[TestMethod]
	public void LoadChooserRoutedActivationRejectsReentrantLoadAndResetsAfterTheDelivery()
	{
		var stack = new UiScreenStack(); var calls = 0; var updating = 0;
		UiScreen screen = null!;
		screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), new[] { new ParkLoadEntry( "jungle", 0, false ) }, _ =>
		{
			calls++;
			if ( calls == 1 ) screen.Find( "loadSelected" )!.Activate();
		} );
		stack.Push( screen ); screen.Updating = _ => updating++;
		var context = Context(); var button = screen.Find( "loadSelected" )!;
		var input = UiInput.Click( button.ScreenRect( context.Canvas ).Center );
		screen.UpdatePointer( context, input, button );
		Assert.AreEqual( 1, calls ); Assert.AreEqual( 0, updating );
		screen.UpdatePointer( context, input, button );
		Assert.AreEqual( 2, calls );
		screen.UpdatePointer( context, input with { RightPressed = true }, button );
		Assert.AreEqual( 2, calls ); Assert.IsTrue( screen.IsRemoved );
	}

	[DataTestMethod]
	[DataRow( "ShopPriceScreen" )]
	[DataRow( "RideUpgradeScreen" )]
	public void PriceAndUpgradeRoutedCancellationPrecedesClickWithoutFrameWork( string name )
	{
		// The upgrade owner is deliberately absent: pointer dispatch must not rerun owner-dependent frame work.
		var screen = name == "ShopPriceScreen" ? ParkHud.NewPriceScreenForTests( "price" ) : ParkHud.NewUpgradeScreenForTests();
		var clicks = 0; var backs = 0; var updating = 0;
		screen.Back = () => backs++;
		screen.Updating = _ => updating++;
		var button = Button( screen, () => clicks++ );
		var context = Context(); var input = UiInput.Click( button.ScreenRect( context.Canvas ).Center );
		screen.UpdatePointer( context, input with { RightPressed = true }, button );
		Assert.AreEqual( 1, backs ); Assert.AreEqual( 0, clicks ); Assert.AreEqual( 0, updating );
		GameOptions.Current.RmbCancel = false;
		screen.UpdatePointer( context, input with { RightPressed = true }, button );
		Assert.AreEqual( 1, backs ); Assert.AreEqual( 1, clicks );
		var stack = new UiScreenStack(); stack.Push( screen ); stack.Pop();
		screen.UpdatePointer( context, input with { RightPressed = true }, button );
		Assert.AreEqual( 1, backs ); Assert.AreEqual( 1, clicks, "Retired screens do nothing." );
	}

	[TestMethod]
	public void TextMenuRoutedInputDoesNotRepeatMeasuredLayoutOrFrameCallback()
	{
		var measures = 0; var clicks = 0; var updates = 0;
		var screen = UiDialogs.TextMenu( "menu", new (string Id, Func<string> Text, Action Clicked, bool Enabled)[]
			{ ("line", () => { measures++; return "A"; }, () => clicks++, true) }, () => { } );
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( 2048, 1536 ) };
		screen.Updating = _ => updates++;
		var line = screen.Find( "line" )!;
		var fallback = line.Bounds;
		screen.PreparePointerGeometry( context );
		Assert.AreNotEqual( fallback, line.Bounds, "The first admission must see measured bounds, not fallback geometry." );
		Assert.AreEqual( 0, updates, "Geometry preparation must not run the frame callback." );
		screen.Update( context, UiInput.Idle( new NVector2( 3, 3 ) ) with { PointerSuppressed = true } );
		var measured = measures;
		var point = line.ScreenRect( context.Canvas ).Center;
		screen.UpdatePointer( context, UiInput.Click( point ), line );
		screen.UpdatePointer( context, UiInput.Idle( point ), line );
		Assert.AreEqual( 1, clicks ); Assert.AreEqual( measured, measures ); Assert.AreEqual( 1, updates );
	}

	[TestMethod]
	public void AttractionsAdmissionGeometryDoesNotAdvanceTimersOrRefreshParkData()
	{
		// An owner without a park makes any accidental data refresh fail instead of hiding it behind a valid fixture.
		var owner = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		owner.SetAttractionStateForTests( BuildCategory.Rides, 1.5f );
		var screen = ParkHud.NewAttractionsScreenForTests( owner );
		var updates = 0; screen.Updating = _ => updates++;
		var list = screen.Add( new UiButton { Id = "attractionList" } );
		UiElement Header( BuildCategory value ) => screen.Add( ParkHud.NewAttractionHeaderForTests( owner, value, 0, UIStrings.AllRides ) );
		var rides = Header( BuildCategory.Rides ); var features = Header( BuildCategory.Features );
		var context = Context();
		owner.SetAttractionStateForTests( BuildCategory.Rides, owner.AttractionValueAgeForTests ); screen.PreparePointerGeometry( context );
		Assert.AreEqual( OriginalLayoutTables.RideShopList.Root.Inner!.Value, list.Bounds );
		Assert.IsTrue( rides.Visible ); Assert.IsFalse( features.Visible );
		owner.SetAttractionStateForTests( BuildCategory.Features, owner.AttractionValueAgeForTests ); screen.PreparePointerGeometry( context );
		Assert.AreEqual( OriginalLayoutTables.FeatureList.Root.Inner!.Value, list.Bounds );
		Assert.IsFalse( rides.Visible ); Assert.IsTrue( features.Visible );
		Assert.AreEqual( 1.5f, owner.AttractionValueAgeForTests ); Assert.AreEqual( 0, updates );
	}

	private sealed class ChatSource : IChatPanelSource
	{
		public string? PlayerName => "Ann";
		public bool ChatOpen => true;
		public IReadOnlyList<string> RoomPlayers => new[] { "Ann", "Bob" };
		public IReadOnlyList<(string Name, bool Online)> Buddies => Array.Empty<(string, bool)>();
		public IReadOnlyCollection<string> Ignored => Array.Empty<string>();
		public bool Earmuffs => false;
		public bool CanVote => true;
		public IReadOnlyList<string> ChatLines => Array.Empty<string>();
		public void SendChatLine( string line ) { }
		public void SendCommand( ChatCommand command, string argument ) { }
		public void AskBuddies() { }
	}

	[TestMethod]
	public void PopupWaitsForTheRoutedReleaseAndNeverRepeatsPanelMaintenance()
	{
		var stack = new UiScreenStack(); var parent = new UiScreen( "chat" ); stack.Push( parent );
		var source = new ChatSource(); var sourceReads = 0;
		var panel = new ChatPanel( OriginalUiTests.FakeStrings(), stack, () => { sourceReads++; return source; } );
		var context = Context();
		panel.OpenPopup( new ChatPerson( "Bob", ChatPersonFlags.None, false ), context.Canvas, new NVector2( 100, 100 ) );
		var popup = stack.Top!; var outside = new NVector2( 1800, 1300 );
		popup.Update( context, UiInput.Idle( outside ) with { PointerSuppressed = true } );
		var reads = sourceReads;
		popup.UpdatePointer( context, UiInput.Idle( outside ) with { LeftDown = true, LeftPressed = true }, null );
		Assert.AreSame( popup, stack.Top ); Assert.AreEqual( reads, sourceReads );
		popup.Update( context, UiInput.Idle( outside ) with { PointerSuppressed = true } );
		Assert.AreSame( popup, stack.Top, "A suppressed snapshot must not stand in for the routed release." );
		reads = sourceReads;
		popup.UpdatePointer( context, UiInput.Click( outside ), null );
		Assert.AreSame( parent, stack.Top ); Assert.AreEqual( reads, sourceReads );
	}

	[TestMethod]
	public void PopupRightClosesEvenWhenRmbCancellationIsDisabled()
	{
		GameOptions.Current.RmbCancel = false;
		var stack = new UiScreenStack(); var parent = new UiScreen( "chat" ); stack.Push( parent );
		var panel = new ChatPanel( OriginalUiTests.FakeStrings(), stack, () => new ChatSource() );
		var context = Context(); var point = new NVector2( 100, 100 );
		panel.OpenPopup( new ChatPerson( "Bob", ChatPersonFlags.None, false ), context.Canvas, point );
		stack.Top!.UpdatePointer( context, Right( point ), null );
		Assert.AreSame( parent, stack.Top );
	}

	[TestMethod]
	public void ChatRoutedHoverPreservesTypingHelpSuppressionWithoutUpdatingThePanel()
	{
		var sourceReads = 0; var source = new ChatSource(); var stack = new UiScreenStack();
		var panel = new ChatPanel( OriginalUiTests.FakeStrings(), stack, () => { sourceReads++; return source; } );
		var screen = new ChatPanel.ChatScreen( "chat", panel ); panel.AddTo( screen ); stack.Push( screen );
		var context = Context();
		screen.Update( context, UiInput.Idle( new NVector2( -1, -1 ) ) with { PointerSuppressed = true } );
		screen.Focus( panel.Entry ); panel.Entry.Text = "hello";
		var button = panel.Elements.Single( element => element.Id == "chatPeopleTab" );
		var reads = sourceReads;
		screen.UpdatePointer( context, UiInput.Idle( button.ScreenRect( context.Canvas ).Center ), button );
		Assert.AreSame( panel.Entry, screen.Focused ); Assert.IsNull( context.HoverHelp ); Assert.AreEqual( reads, sourceReads );
	}

	[TestMethod]
	public void GlobeRoutedReleasePicksAtItsOwnPositionAndDoesNotReplayTheFrameEdge()
	{
		var oldView = Camera.ViewMatrix; var oldProjection = Camera.ProjMatrix;
		try
		{
			// Pick-only globe: no constructor, models, rendering, or original game data.
			var scene = (GlobeScene)RuntimeHelpers.GetUninitializedObject( typeof( GlobeScene ) );
			var city = new CityInfo( 1, "Synthetic", "", 0, 0, 0 );
			scene.UseCityPointsForTests( new List<(CityInfo City, NVector3 Point)> { (city, new NVector3( 0, 0, -GlobeScene.PickRadius )) } );
			var centre = GlobeScene.Centre.GetSystemVector3();
			Camera.ViewMatrix = NMatrix4x4.CreateTranslation( -centre.X, -centre.Y, 200 - centre.Z );
			Camera.ProjMatrix = NMatrix4x4.CreateScale( 0.01f );
			var clicks = new List<CityInfo>();
			var screen = new OnlineGlobeScreen( () => scene ) { CityClicked = clicks.Add };
			var context = Context(); var point = new NVector2( 1024, 768 );
			screen.Update( context, UiInput.Click( point ) with { PointerSuppressed = true } );
			Assert.AreEqual( 0, clicks.Count );
			screen.UpdatePointer( context, UiInput.Click( point ) with { NotClick = true }, null );
			Assert.AreEqual( 0, clicks.Count, "An unqualified or inexact blur release never enters a city." );
			screen.UpdatePointer( context, UiInput.Click( point ), null );
			Assert.AreSame( city, clicks.Single() );
			screen.UpdatePointer( context, UiInput.Click( new NVector2( 100, 100 ) ), null );
			Assert.IsNull( screen.Hovered ); Assert.AreEqual( 1, clicks.Count, "The previous pin must not be reused at another position." );
			var button = Button( screen, () => { } );
			screen.UpdatePointer( context, UiInput.Click( point ), button );
			Assert.AreEqual( 1, clicks.Count, "The admitted UI owner excludes city activation even over a pin." );
			// The frame path qualifies the click the same way as the routed one.
			screen.Update( context, UiInput.Click( point ) with { NotClick = true } );
			Assert.AreEqual( 1, clicks.Count, "A release that is no click never enters a city on the frame path either." );
			screen.Update( context, UiInput.Click( point ) );
			Assert.AreEqual( 2, clicks.Count );
		}
		finally { Camera.ViewMatrix = oldView; Camera.ProjMatrix = oldProjection; }
	}

	[TestMethod]
	public void GlobeRoutedRightNeverCancelsOrRunsFrameKeyboardAndSteering()
	{
		var picks = 0; var backs = 0; var updates = 0;
		var screen = new OnlineGlobeScreen( () => { picks++; return null; } ) { Back = () => backs++, Updating = _ => updates++ };
		var context = Context(); var point = new NVector2( 100, 100 );
		screen.UpdatePointer( context, Right( point ) with { Pressed = UiKeys.Back | UiKeys.Left }, null );
		Assert.AreEqual( 0, backs ); Assert.AreEqual( 0, updates ); Assert.AreEqual( 1, picks );
		var button = Button( screen, () => { } );
		screen.UpdatePointer( context, Right( point ), button );
		Assert.AreEqual( 1, picks, "An admitted UI recipient excludes globe picking." );
	}
}
