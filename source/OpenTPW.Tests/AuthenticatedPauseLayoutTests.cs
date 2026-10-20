using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.Mods;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Client;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class AuthenticatedPauseLayoutTests
{
	private static T Empty<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object target, string name, object? value ) =>
		target.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private static ParkHud BareHud( Level level, HudHost host )
	{
		var hud = Empty<ParkHud>();
		Set( hud, "level", level ); Set( hud, "strings", OriginalUiTests.FakeStrings() ); Set( hud, "host", host );
		Set( hud, "<Stack>k__BackingField", new UiScreenStack() );
		var screen = new UiScreen( "hud" ) { Modal = false };
		Set( hud, "hud", screen ); hud.Stack.Push( screen );
		return hud;
	}

	private static UiContext Context()
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( 1280, 960 ), PopupHelp = false, ResolveTexture = _ => null };
	}

	private static void AssertLayout( UiScreen menu, UiContext context, bool compact )
	{
		var items = menu.Elements.OfType<UiMenuItem>().ToArray();
		Assert.IsTrue( items.Length >= 2 );
		Assert.AreEqual( compact ? 0f : 10f, items[0].Bounds.Y );
		Assert.AreEqual( items[0].Bounds.Bottom + (compact ? 0f : 5f), items[1].Bounds.Y, 1e-4f );
		menu.Update( context, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
		Assert.AreEqual( compact ? 0f : 10f, items[0].Bounds.Y );
		for ( var index = 1; index < items.Length; index++ )
			Assert.AreEqual( items[index - 1].Bounds.Bottom + (compact ? 0f : 5f), items[index].Bounds.Y, 1e-4f );
		foreach ( var item in items.Where( item => item.Focusable ) )
			Assert.AreSame( item, menu.HitTest( context.Canvas, item.ScreenRect( context.Canvas ).Center ), "hit region follows the measured line" );
	}

	[TestMethod]
	public void RealHostDistinguishesNoSessionTransportOnlyAndAuthenticatedSession()
	{
		var edition = GameEditions.Current;
		try
		{
			GameEditions.Select( GameEdition.ThemeParkWorld );
			using var mods = OpenTpwExtensions.Use( OnlineExtensionGateTests.WithOnline( true ) );
			var flow = Empty<GameFlow>(); var level = Empty<Level>();
			Set( flow, "<Strings>k__BackingField", OriginalUiTests.FakeStrings() );
			var hud = BareHud( level, flow.CreateHudHost( level, () => null! ) );
			var context = Context();
			hud.OpenPauseMenu(); AssertLayout( hud.Stack.Top!, context, false ); hud.ClosePauseMenu();
			Assert.IsNull( typeof( GameFlow ).GetField( "onlineSession", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( flow ), "opening pause must not create an online session" );

			// State-only fixtures: no transport, credentials or authentication requests are used.
			var session = Empty<OnlineSession>(); var client = Empty<OnlineClient>();
			Set( session, "<Client>k__BackingField", client ); Set( flow, "onlineSession", session );
			hud.OpenPauseMenu(); AssertLayout( hud.Stack.Top!, context, false ); hud.ClosePauseMenu();
			Set( client, "<Session>k__BackingField", new SessionToken( "synthetic", "layout-test-only", DateTimeOffset.MaxValue ) );
			hud.OpenPauseMenu(); AssertLayout( hud.Stack.Top!, context, true );
			var open = hud.Stack.Top!;

			// A cleared token (the successful-logout state) does not rearrange an already-open menu.
			Set( client, "<Session>k__BackingField", null );
			AssertLayout( open, context, true );
			hud.ClosePauseMenu(); hud.OpenPauseMenu(); AssertLayout( hud.Stack.Top!, context, false );
			Set( client, "<Session>k__BackingField", new SessionToken( "synthetic", "layout-test-only", DateTimeOffset.MaxValue ) );
			using ( OpenTpwExtensions.Use( OnlineExtensionGateTests.WithOnline( false ) ) )
			{
				hud.ClosePauseMenu(); hud.OpenPauseMenu(); AssertLayout( hud.Stack.Top!, context, false );
			}
			Set( session, "<Client>k__BackingField", null );
			hud.ClosePauseMenu(); hud.OpenPauseMenu(); AssertLayout( hud.Stack.Top!, context, false );
		}
		finally { GameEditions.Select( edition ); }
	}

	[TestMethod]
	public void AuthenticationChangesApplyOnlyOnReopenAndResumeStillClicks()
	{
		var loggedIn = false; var samples = 0;
		var hud = BareHud( Empty<Level>(), new HudHost { IsLoggedIn = () => { samples++; return loggedIn; } } );
		var context = Context();
		hud.OpenPauseMenu(); AssertLayout( hud.Stack.Top!, context, false );
		loggedIn = true;
		hud.OpenPauseMenu(); AssertLayout( hud.Stack.Top!, context, false ); Assert.AreEqual( 1, samples );
		hud.ClosePauseMenu(); hud.OpenPauseMenu(); AssertLayout( hud.Stack.Top!, context, true ); Assert.AreEqual( 2, samples );
		var resume = hud.Stack.Top!.Find( nameof( UIStrings.ResumeGame ) )!;
		hud.Stack.Update( context, UiInput.Click( resume.ScreenRect( context.Canvas ).Center ) );
		Assert.AreEqual( 1, hud.Stack.Screens.Count );
	}

	[TestMethod]
	public void DefaultMenusAndMeasuredGeometryAreUnaffectedByCompactMenus()
	{
		var items = new (string Id, Func<string> Text, Action Clicked, bool Enabled)[]
			{ ("one", () => "A", () => { }, true), ("two", () => "AA", () => { }, true) };
		var context = Context();
		AssertLayout( UiDialogs.TextMenu( "park", items, () => { }, compact: true ), context, true );
		AssertLayout( UiDialogs.TextMenu( "lobbyMenu", items, () => { } ), context, false );
		var standard = UiDialogs.TextMenuLines( new[] { (100, 40), (80, 30) }, 2, 1f );
		var compact = UiDialogs.TextMenuLines( new[] { (100, 40), (80, 30) }, 2, 1f, compact: true );
		for ( var index = 0; index < standard.Length; index++ )
		{
			Assert.AreEqual( standard[index].X, compact[index].X );
			Assert.AreEqual( standard[index].Width, compact[index].Width );
			Assert.AreEqual( standard[index].Height, compact[index].Height );
		}
	}
}
