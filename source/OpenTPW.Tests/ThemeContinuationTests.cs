using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class ThemeContinuationTests
{
	private string root = null!;
	private BaseFileSystem files = null!;
	private GameEdition previousEdition;
	private BaseFileSystem previousSaves = null!;
	private BaseFileSystem previousFiles = null!;
	private Logger previousLog = null!;
	[TestInitialize]
	public void Initialize()
	{
		previousSaves = SaveFileSystem; previousFiles = FileSystem; previousLog = Log; Log = new Logger();
		previousEdition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkWorld );
		root = Directory.CreateTempSubdirectory( "opentpw-theme-continue-" ).FullName;
		files = new BaseFileSystem( root ); SaveFileSystem = files;
		Directory.CreateDirectory( Path.Combine( root, "data" ) ); FileSystem = new BaseFileSystem( Path.Combine( root, "data" ) );
	}
	[TestCleanup]
	public void Cleanup() { GameEditions.Select( previousEdition ); SaveFileSystem = previousSaves; FileSystem = previousFiles; Log = previousLog; Directory.Delete( root, true ); }

	[TestMethod]
	public void ActualThemeEnterOffersDistinctContinueAndNewForAnExistingOwnSave()
	{
		var saved = ParkResumeSaveFileTests.Envelope();
		var path = files.GetAbsolutePath( "opentpw-jungle-world.json" );
		ParkResumeSaveFile.Save( path, saved );
		var original = File.ReadAllBytes( path );
		ParkContinueEntry? continued = null;
		ParkEconomy? restored = null;
		ParkResumeSnapshot? resumed = null;
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		var started = 0;
		var island = new LobbyIslandInfo( 0, "jungle", "", "", "", "jungle", 0, 0, (0, 0, 0), Array.Empty<string>(), false, 0 );
		var menu = new FrontEndMenu( OriginalUiTests.FakeStrings(), new[] { island }, new FrontEndActions
		{
			ContinueEntry = flow.FindContinueEntry,
			ContinuePark = entry =>
			{
				continued = entry;
				Assert.IsTrue( flow.TryContinuePark( entry, snapshot =>
				{
					resumed = snapshot;
					restored = ParkSaveFile.Restore( snapshot.Economy, EconomyTestData.Settings(), EconomyTestData.Catalog() );
				} ) );
			},
			StartPark = ( _, _ ) => started++
		} );
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) );
		menu.Stack.Update( context, UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( "themeStart", menu.Stack.Top!.Name, "The real Enter action must offer Continue rather than always opening new-game modes." );
		menu.Stack.Update( context, UiInput.Click( menu.Stack.Top.Find( "choice0" )!.ScreenRect( context.Canvas ).Center ) );
		Assert.IsNotNull( continued );
		Assert.AreEqual( ParkStartKind.FullSimulation, continued!.StartKind );
		Assert.AreEqual( 0, started );
		Assert.IsNotNull( restored );
		Assert.AreEqual( saved.Economy.Ledger.Balance, restored!.Balance );
		Assert.AreEqual( saved.Clock, resumed!.Clock, "Continue transports the saved clock, rather than constructing new state." );
		Assert.AreEqual( saved.Guests.Guests.Length, resumed.Guests.Guests.Length );
		Assert.AreEqual( saved.Economy.Mode, restored.Mode );
		CollectionAssert.AreEqual( original, File.ReadAllBytes( path ), "Entering/continuing never overwrites the manual slot." );
	}

	[TestMethod]
	public void NewAndBackRemainExplicitAndDoNotTouchTheManualSlot()
	{
		var saved = ParkResumeSaveFileTests.Envelope(); var path = files.GetAbsolutePath( "opentpw-jungle-world.json" );
		ParkResumeSaveFile.Save( path, saved ); var original = File.ReadAllBytes( path );
		var started = 0; var continued = 0;
		var menu = Menu( new FrontEndActions { ContinueEntry = theme => ThemeContinuation.Probe( files, theme ), ContinuePark = _ => continued++, StartPark = ( _, _ ) => started++ } );
		var context = Context();
		Enter( menu, context );
		Click( menu, context, "choice2" ); Assert.AreEqual( "lobby", menu.Stack.Top!.Name );
		Enter( menu, context ); Click( menu, context, "choice1" ); Assert.AreEqual( "gameMode", menu.Stack.Top!.Name );
		Click( menu, context, "instantAction" ); Assert.AreEqual( 1, started ); Assert.AreEqual( 0, continued );
		CollectionAssert.AreEqual( original, File.ReadAllBytes( path ) );
	}

	[DataTestMethod]
	[DataRow( "missing" )]
	[DataRow( "corrupt" )]
	[DataRow( "otherTheme" )]
	[DataRow( "TPI" )]
	public void UnavailableOwnSlotDoesNotCreateOrOfferContinue( string reason )
	{
		var path = files.GetAbsolutePath( "opentpw-jungle-world.json" );
		if ( reason == "corrupt" ) File.WriteAllText( path, "{" );
		if ( reason == "otherTheme" )
		{
			var saved = ParkResumeSaveFileTests.Envelope();
			ParkResumeSaveFile.Save( path, saved with { Theme = "fantasy", Economy = ChangedEconomy( saved.Economy, "Theme", "fantasy" ) } );
		}
		if ( reason == "TPI" ) GameEditions.Select( GameEdition.ThemeParkInc );
		var continued = 0;
		var menu = Menu( new FrontEndActions { ContinueEntry = theme => ThemeContinuation.Probe( files, theme ), ContinuePark = _ => continued++ } );
		Enter( menu, Context() ); Assert.AreEqual( "gameMode", menu.Stack.Top!.Name ); Assert.AreEqual( 0, continued );
		if ( reason == "missing" || reason == "TPI" ) Assert.IsFalse( File.Exists( path ) );
	}

	[DataTestMethod]
	[DataRow( "missing" )]
	[DataRow( "corrupt" )]
	[DataRow( "otherTheme" )]
	[DataRow( "mode" )]
	[DataRow( "easy" )]
	[DataRow( "TPI" )]
	[DataRow( "foreignPath" )]
	[DataRow( "start" )]
	[DataRow( "native" )]
	public void ActivationReReadsAndRefusesStaleMetadataWithoutReplacingTheLiveWorld( string reason )
	{
		var saved = ParkResumeSaveFileTests.Envelope() with { StartKind = ParkStartKind.OriginalSaveReference };
		var path = files.GetAbsolutePath( "opentpw-jungle-world.json" ); ParkResumeSaveFile.Save( path, saved );
		var entry = ThemeContinuation.Probe( files, "jungle" )!;
		if ( reason == "missing" ) File.Delete( path );
		if ( reason == "corrupt" ) File.WriteAllText( path, "{" );
		if ( reason == "otherTheme" ) ParkResumeSaveFile.Save( path, saved with { Theme = "fantasy", Economy = ChangedEconomy( saved.Economy, "Theme", "fantasy" ) } );
		if ( reason == "start" ) ParkResumeSaveFile.Save( path, saved with { StartKind = ParkStartKind.FullSimulation } );
		if ( reason == "native" ) entry = entry with { Park = entry.Park with { OwnSavePath = null } };
		if ( reason == "mode" ) ParkResumeSaveFile.Save( path, saved with { StartKind = ParkStartKind.InstantAction, Economy = ChangedEconomy( saved.Economy, "Mode", ParkGameMode.InstantAction ) } );
		if ( reason == "easy" ) ParkResumeSaveFile.Save( path, saved with { Economy = ChangedEconomy( saved.Economy, "Easy", true ) } );
		if ( reason == "TPI" ) GameEditions.Select( GameEdition.ThemeParkInc );
		if ( reason == "foreignPath" ) entry = entry with { Park = entry.Park with { OwnSavePath = Path.Combine( root, "unrelated.json" ) } };
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		var live = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ); var menu = Menu( new FrontEndActions() );
		Set( flow, "Level", live ); Set( flow, "Menu", menu ); Set( flow, "Strings", OriginalUiTests.FakeStrings() );
		var calls = 0;
		Assert.IsFalse( flow.TryContinuePark( entry, _ => calls++ ) );
		Assert.AreEqual( 0, calls ); Assert.AreSame( live, flow.Level ); Assert.AreEqual( "continueFailed", menu.Stack.Top!.Name );
	}

	[TestMethod]
	public void ContinueUsesTheRealResumeAssetPreflightAndPreservesTheOldWorldOnMissingAssets()
	{
		var saved = ParkResumeSaveFileTests.Envelope(); var path = files.GetAbsolutePath( "opentpw-jungle-world.json" ); ParkResumeSaveFile.Save( path, saved );
		var entry = ThemeContinuation.Probe( files, "jungle" )!; var original = File.ReadAllBytes( path );
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		var live = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ); var menu = Menu( new FrontEndActions() );
		Set( flow, "Level", live ); Set( flow, "Menu", menu ); Set( flow, "Strings", OriginalUiTests.FakeStrings() );
		flow.ContinuePark( entry );
		Assert.AreSame( live, flow.Level ); Assert.AreEqual( "continueFailed", menu.Stack.Top!.Name );
		CollectionAssert.AreEqual( original, File.ReadAllBytes( path ) );
	}

	[DataTestMethod]
	[DataRow( ParkStartKind.FullSimulation )]
	[DataRow( ParkStartKind.InstantAction )]
	[DataRow( ParkStartKind.OriginalSaveReference )]
	public void ContinueHonoursTheSavedStartKindAndMode( ParkStartKind start )
	{
		var saved = ParkResumeSaveFileTests.Envelope() with { StartKind = start };
		if ( start == ParkStartKind.InstantAction ) saved = saved with { Economy = ChangedEconomy( saved.Economy, "Mode", ParkGameMode.InstantAction ) };
		ParkResumeSaveFile.Save( files.GetAbsolutePath( "opentpw-jungle-world.json" ), saved );
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		var entry = flow.FindContinueEntry( "jungle" ); Assert.IsNotNull( entry );
		ParkResumeSnapshot? resumed = null;
		Assert.IsTrue( flow.TryContinuePark( entry!, snapshot => resumed = snapshot ) );
		Assert.AreEqual( start, resumed!.StartKind ); Assert.AreEqual( saved.Economy.Mode, resumed.Economy.Mode );
		Assert.AreEqual( saved.Economy.Easy, resumed.Economy.Easy );
	}

	private static ParkSaveFile.Data ChangedEconomy( ParkSaveFile.Data data, string property, object value )
	{
		var copy = ParkResumeJson.Deserialize<ParkSaveFile.Data>( ParkResumeJson.Serialize( data ) );
		typeof( ParkSaveFile.Data ).GetProperty( property )!.SetValue( copy, value );
		return copy;
	}
	private static void Set( object target, string property, object value ) => target.GetType().GetProperty( property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )!.SetValue( target, value );
	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) );
	private static FrontEndMenu Menu( FrontEndActions actions ) => new( OriginalUiTests.FakeStrings(), new[] { new LobbyIslandInfo( 0, "jungle", "", "", "", "jungle", 0, 0, (0, 0, 0), Array.Empty<string>(), false, 0 ) }, actions );
	private static void Enter( FrontEndMenu menu, UiContext context ) => menu.Stack.Update( context, UiInput.Click( menu.Main.Find( "enterPark" )!.ScreenRect( context.Canvas ).Center ) );
	private static void Click( FrontEndMenu menu, UiContext context, string id ) => menu.Stack.Update( context, UiInput.Click( menu.Stack.Top!.Find( id )!.ScreenRect( context.Canvas ).Center ) );
}
