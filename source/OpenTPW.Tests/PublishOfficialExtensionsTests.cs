using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.Mods;
using OpenTPW.Online;
using OpenTPW.Online.Packages;
using OpenTPW.UI;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// Parks that use OpenTPW's official extensions are published and recorded in their package; community mods stay refused (docs/MODS.md, "Parks that use extensions"). The package format, the visit's question and
/// the online menu's reasons. Headless: no original game data.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
[TestClass]
[DoNotParallelize]
public class PublishOfficialExtensionsTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private string root = null!;
	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize()
	{
		root = Directory.CreateTempSubdirectory( "opentpw-publish-extensions-" ).FullName;
		previousEdition = GameEditions.Current;
		GameEditions.Select( GameEdition.ThemeParkWorld );
	}

	[TestCleanup]
	public void Cleanup()
	{
		GameEditions.Select( previousEdition );
		Directory.Delete( root, true );
	}

	private static ModSet With( params (IOpenTpwExtension Extension, bool Enabled)[] choices ) =>
		ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = choices.ToDictionary( choice => choice.Extension.Id, choice => choice.Enabled ) }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );

	/// <summary>The official extensions with the given choices and one community mod that changes the simulation (an object mod).</summary>
	private ModSet WithCommunityMod( params (IOpenTpwExtension Extension, bool Enabled)[] choices )
	{
		var directory = Path.Combine( root, "community.tpi-rides" );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ),
			"""{ "schema": 1, "id": "community.tpi-rides", "name": "Rides", "version": "1.0.0", "category": "community", "content": { "objects": "objects.json" } }""" );
		File.WriteAllText( Path.Combine( directory, "objects.json" ),
			"""{ "schema": 1, "idRange": { "first": 20000, "last": 20009 }, "objects": [{ "archive": "objects/zoom", "theme": "jungle", "category": "rides", "infoId": 20001, "names": { "English": "rides" } }] }""" );
		var mods = ModDiscovery.Discover( new[] { directory }, Array.Empty<string>(), null, null ).ToList();
		var enabled = choices.ToDictionary( choice => choice.Extension.Id, choice => choice.Enabled );
		enabled["community.tpi-rides"] = true;
		return ModSet.Resolve( mods, new ModSettings { Enabled = enabled }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
	}

	private static ExtensionUse Use( IOpenTpwExtension extension, string? version = null ) => new( extension.Id, version ?? extension.Version.ToString() );

	internal static ParkPackage Package( params ExtensionUse[] extensions ) =>
		ParkPackage.Create( SharedParkTests.Sealed().ToSnapshot( new[] { new RequiredContent( ContentKinds.Level, "jungle", null ) } ) with { Extensions = extensions },
			new ParkInfo( "Built Park", "Owner's description", "Alice", "jungle" ), new GameInfo( "Theme Park World", "English" ), null, OnlineFormatTests.FixedId, OnlineFormatTests.FixedTime );

	private static void Set( object target, string name, object? value ) => target.GetType().GetField( name, Private )!.SetValue( target, value );

	// ---- Official or community -----------------------------------------------------------------------------------

	[TestMethod]
	public void OnlyBuiltInExtensionsAreOfficial()
	{
		var rain = With( (OfficialExtensions.RainMood, true) );
		var mods = ParkSharing.ModsOf( null, rain );
		CollectionAssert.Contains( mods.Official.Select( mod => mod.Id ).ToArray(), OfficialExtensions.RainMood.Id );
		Assert.AreEqual( 0, mods.Community.Count );
		Assert.IsNull( ParkSharing.ModsRefusal( mods ), "an official extension alone refuses nothing: it is recorded in the package" );
		Assert.IsTrue( mods.Official.All( mod => OfficialExtensions.Find( mod.Id ) != null ) );
	}

	[TestMethod]
	public void ACommunityModStillRefusesAndNamesOnlyItself()
	{
		var set = WithCommunityMod( (OfficialExtensions.RainMood, true), (OfficialExtensions.SmarterGuests, true) );
		var mods = ParkSharing.ModsOf( null, set );
		Assert.AreEqual( "community.tpi-rides", mods.Community.Single().Id );
		Assert.IsTrue( mods.Official.Count >= 2, "the official extensions are listed apart" );
		var refusal = ParkSharing.ModsRefusal( mods )!;
		StringAssert.Contains( refusal, "community.tpi-rides 1.0.0" );
		StringAssert.DoesNotMatch( refusal, new System.Text.RegularExpressions.Regex( "opentpw\\." ), "the refusal does not blame OpenTPW's own extensions" );
		using ( OpenTpwExtensions.Use( set ) )
		{
			var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			StringAssert.Contains( ParkSharing.ModsRefusal( level ), "community.tpi-rides 1.0.0" );
			Assert.ThrowsException<InvalidOperationException>( () => ParkSharing.ExportLevel( level, "Park", "Me" ) );
		}
	}

	[TestMethod]
	public void ExtensionUsesRecordIdAndVersionAndNothingForAParkWithout()
	{
		// only content extensions are recorded: a rule extension (rain mood) or the cheats extension makes no difference to a visit
		using ( OpenTpwExtensions.Use( With( (OfficialExtensions.RainMood, true), (OfficialExtensions.SmarterGuests, true), (OfficialExtensions.Cheats, true) ) ) )
		{
			Assert.IsTrue( ParkSharing.ModsOf( null, OpenTpwExtensions.Active ).Official.Count > 0, "they are official simulation extensions" );
			Assert.IsNull( ParkSharing.ExtensionUses( (Level?)null ), "and still leave the package version 2 without the member" );
		}
		var winter = new SimulationMod( OfficialExtensions.Winter.Id, OfficialExtensions.Winter.Version, ModSet.NoFilesSha256 );
		var rain = new SimulationMod( OfficialExtensions.RainMood.Id, OfficialExtensions.RainMood.Version, ModSet.NoFilesSha256 );
		var recorded = ParkSharing.ExtensionUses( new ParkSharing.ParkMods( new[] { winter, rain }, Array.Empty<SimulationMod>() ) )!;
		CollectionAssert.AreEqual( new[] { Use( OfficialExtensions.Winter ) }, recorded.ToArray() );
		// a used-only extension counts only while the park uses it: winter on, no winter object in the park
		using ( OpenTpwExtensions.Use( With( (OfficialExtensions.Winter, true) ) ) )
			Assert.IsFalse( ( ParkSharing.ExtensionUses( (Level?)null ) ?? Array.Empty<ExtensionUse>() ).Any( use => use.Id == OfficialExtensions.Winter.Id ) );
		var none = ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = OfficialExtensions.All.ToDictionary( extension => extension.Id, _ => false ) }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		Assert.IsNull( ParkSharing.ExtensionUses( ParkSharing.ModsOf( null, none ) ), "no official simulation extension on: no member in the manifest" );
	}

	// ---- The package -------------------------------------------------------------------------------------------

	[TestMethod]
	public void APackageWithoutExtensionsIsStillVersionTwoWithoutTheMember()
	{
		var package = SharedParkTests.LayoutPackage();
		Assert.AreEqual( 2, package.Manifest.FormatVersion );
		Assert.IsNull( package.Manifest.Extensions );
		Assert.IsFalse( Encoding.UTF8.GetString( StrictJson.Serialize( package.Manifest ) ).Contains( "extensions", StringComparison.Ordinal ), "an older reader sees the manifest it always saw" );
		// an empty list is the same as none
		Assert.IsNull( Package().Manifest.Extensions );
		Assert.AreEqual( 2, Package().Manifest.FormatVersion );
	}

	[TestMethod]
	public void APackageWithExtensionsIsVersionThreeAndRoundTrips()
	{
		var package = Package( Use( OfficialExtensions.Winter ), Use( OfficialExtensions.RainMood ) );
		Assert.AreEqual( 3, package.Manifest.FormatVersion );
		Assert.AreEqual( ParkPackage.CurrentFormatVersion, package.Manifest.FormatVersion );
		var read = ParkPackage.Read( package.ToBytes() );
		CollectionAssert.AreEqual( package.Manifest.Extensions!.ToList(), read.Manifest.Extensions!.ToList() );
		CollectionAssert.AreEqual( package.ToBytes(), Package( Use( OfficialExtensions.Winter ), Use( OfficialExtensions.RainMood ) ).ToBytes(), "deterministic" );
		CollectionAssert.AreEqual( read.Manifest.Extensions!.ToList(), read.ToSnapshot().Extensions!.ToList() );
		StringAssert.Contains( Encoding.UTF8.GetString( StrictJson.Serialize( read.Manifest ) ), "\"extensions\":[{\"id\":\"opentpw.winter\"" );
	}

	[TestMethod]
	public void AnInvalidExtensionListIsRefused()
	{
		var good = Package( Use( OfficialExtensions.Winter ) );
		void Refused( ParkManifest manifest, string because )
		{
			try { (good with { Manifest = manifest }).Validate(); Assert.Fail( because ); }
			catch ( InvalidDataException ) { }
		}
		Refused( good.Manifest with { FormatVersion = 2 }, "extensions need version 3" );
		Refused( good.Manifest with { Extensions = Array.Empty<ExtensionUse>() }, "an empty list is no list" );
		Refused( good.Manifest with { Extensions = new[] { new ExtensionUse( "community.tpi-rides", "1.0.0" ) } }, "only official ids" );
		Refused( good.Manifest with { Extensions = new[] { new ExtensionUse( "opentpw.Winter", "1.0.0" ) } }, "lowercase ids" );
		Refused( good.Manifest with { Extensions = new[] { new ExtensionUse( "opentpw.winter", "one" ) } }, "a version such as 1.0.0" );
		Refused( good.Manifest with { Extensions = new[] { new ExtensionUse( "opentpw.winter", "1.0.0" ), new ExtensionUse( "opentpw.winter", "1.0.0" ) } }, "listed twice" );
		Refused( good.Manifest with { Extensions = Enumerable.Range( 0, ParkPackage.MaximumExtensions + 1 ).Select( index => new ExtensionUse( $"opentpw.x{index}", "1.0.0" ) ).ToArray() }, "bounded" );
		Refused( good.Manifest with { Extensions = new ExtensionUse[] { null! } }, "no empty entry" );
		// version 3 without extensions is fine, and an older version 2 manifest has none
		(good with { Manifest = good.Manifest with { Extensions = null } }).Validate();
	}

	[TestMethod]
	public void AManifestOfANewerVersionIsRefusedByItsVersionEvenWithNewMembers()
	{
		// the version is read before the strict parse, which would call a later version's new members malformed
		var json = Encoding.UTF8.GetString( StrictJson.Serialize( Package( Use( OfficialExtensions.Winter ) ).Manifest ) ).Replace( "\"formatVersion\":3", "\"formatVersion\":4" ).Replace( "{\"format\"", "{\"futureMember\":1,\"format\"" );
		var package = Package( Use( OfficialExtensions.Winter ) );
		var entries = OnlineFormatTests.Zip( (ParkPackage.ManifestEntry, Encoding.UTF8.GetBytes( json )), (ParkPackage.PayloadEntry, package.Payload) );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkPackage.Read( entries ) ).Message, "version 4 is not supported" );
		// a version this build reads with an unknown member is still malformed
		var malformed = Encoding.UTF8.GetString( StrictJson.Serialize( package.Manifest ) ).Replace( "{\"format\"", "{\"futureMember\":1,\"format\"" );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkPackage.Read( OnlineFormatTests.Zip( (ParkPackage.ManifestEntry, Encoding.UTF8.GetBytes( malformed )), (ParkPackage.PayloadEntry, package.Payload) ) ) ).Message, "malformed" );
	}

	// ---- The visit ------------------------------------------------------------------------------------------------

	private static IReadOnlyList<ExtensionUse> Winter => new[] { Use( OfficialExtensions.Winter ) };

	[TestMethod]
	public void AVisitAsksOnlyForContentExtensionsThatAreOff()
	{
		Assert.IsNull( ParkVisitExtensions.Plan( null, With() ), "a package without extensions asks nothing" );
		Assert.IsNull( ParkVisitExtensions.Plan( Array.Empty<ExtensionUse>(), With() ) );
		Assert.IsNull( ParkVisitExtensions.Plan( Winter, With( (OfficialExtensions.Winter, true) ) ), "on: the visit goes ahead" );
		Assert.IsNull( ParkVisitExtensions.Plan( new[] { Use( OfficialExtensions.Winter, "0.9.0" ) }, With( (OfficialExtensions.Winter, true) ) ), "another version is drawn, not played" );
		var off = ParkVisitExtensions.Plan( Winter, With( (OfficialExtensions.Winter, false) ), Array.Empty<string>() )!;
		CollectionAssert.AreEqual( new[] { OfficialExtensions.Winter.Id }, off.TurnOn.Select( extension => extension.Id ).ToArray() );
		Assert.AreEqual( 0, off.Blocked.Count );
		Assert.IsTrue( off.Changes[OfficialExtensions.Winter.Id] );
		Assert.IsTrue( off.ToSwitch().CanSwitch );
		// rule-only extensions and the cheats extension change how a park plays: a visitor does not need them
		foreach ( var extension in new[] { OfficialExtensions.RainMood, OfficialExtensions.WinterCold, OfficialExtensions.SmarterGuests, OfficialExtensions.Cheats } )
			Assert.IsNull( ParkVisitExtensions.Plan( new[] { Use( extension ) }, With( (extension, false) ), Array.Empty<string>() ), extension.Id );
		CollectionAssert.AreEquivalent( new[] { "opentpw.pools", "opentpw.umbrellas", "opentpw.winter", "opentpw.evening" }, ParkVisitExtensions.RequiredIds.ToArray() );
	}

	[TestMethod]
	public void AVisitIsRefusedWhenARequiredExtensionCannotBeTurnedOn()
	{
		var off = With( (OfficialExtensions.Winter, false) );
		// switched off for this run on the command line
		var blocked = ParkVisitExtensions.Plan( Winter, off, new[] { OfficialExtensions.Winter.Id } )!;
		CollectionAssert.AreEqual( new[] { OfficialExtensions.Winter.Id }, blocked.Blocked.ToArray() );
		Assert.AreEqual( 0, blocked.TurnOn.Count );
		// an extension this build does not know
		CollectionAssert.AreEqual( new[] { "opentpw.future" }, ParkVisitExtensions.Plan( new[] { new ExtensionUse( "opentpw.future", "1.0.0" ) }, off, Array.Empty<string>() )!.Blocked.ToArray() );
		// a Theme Park Inc run has no pools, umbrellas or winter, but the evening extension has no gate there and can be turned on
		GameEditions.Select( GameEdition.ThemeParkInc );
		CollectionAssert.AreEqual( new[] { OfficialExtensions.Winter.Id }, ParkVisitExtensions.Plan( Winter, off, Array.Empty<string>() )!.Blocked.ToArray() );
		var evening = ParkVisitExtensions.Plan( new[] { Use( OfficialExtensions.Evening ) }, With( (OfficialExtensions.Evening, false) ), Array.Empty<string>() )!;
		CollectionAssert.AreEqual( new[] { OfficialExtensions.Evening.Id }, evening.TurnOn.Select( extension => extension.Id ).ToArray() );
		Assert.AreEqual( 0, evening.Blocked.Count );
	}

	[TestMethod]
	public void TheStartupVisitIsRefusedWithTheWayToSwitchTheExtensionOn()
	{
		var package = Package( Use( OfficialExtensions.Winter ) );
		ParkVisitExtensions.RequireOn( package, With( (OfficialExtensions.Winter, true) ) );
		ParkVisitExtensions.RequireOn( SharedParkTests.LayoutPackage(), With( (OfficialExtensions.Winter, false) ) );
		var error = Assert.ThrowsException<InvalidDataException>( () => ParkVisitExtensions.RequireOn( package, With( (OfficialExtensions.Winter, false) ) ) );
		StringAssert.Contains( error.Message, "opentpw.winter" );
		StringAssert.Contains( error.Message, "--enable-mod opentpw.winter" );
	}

	private GameFlow Flow( out UiScreenStack stack, string language = "English" )
	{
		var previous = (SaveFileSystem, FileSystem, Log);
		Log = new Logger();
		Directory.CreateDirectory( Path.Combine( root, "data" ) );
		SaveFileSystem = new BaseFileSystem( root );
		FileSystem = new BaseFileSystem( Path.Combine( root, "data" ) );
		// a confirmed visit switches extensions for the run: the catalog's content roots and the player's choices go back with the rest
		var roots = ObjectCatalog.ContentRoots;
		var choices = ChoicesProperty.GetValue( null );
		restore = () =>
		{
			(SaveFileSystem, FileSystem, Log) = previous;
			ObjectCatalog.SetContentRoots( roots );
			ChoicesProperty.SetValue( null, choices );
		};
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		Set( flow, "<Strings>k__BackingField", OriginalUiTests.FakeStrings( language ) );
		Set( flow, "<ModSettingsPath>k__BackingField", Path.Combine( root, ModSettings.FileName ) );
		Set( flow, "<PersistSettings>k__BackingField", true );
		stack = new UiScreenStack();
		stack.Push( new UiScreen( "menu" ) );
		return flow;
	}

	private Action restore = () => { };

	private static readonly PropertyInfo ChoicesProperty = typeof( ModStartup ).GetProperty( "Choices", BindingFlags.Static | BindingFlags.NonPublic )!;

	private static ParkVisitInfo Visit( ParkPackage package ) => new( package, MinimalParkPayload.FromSnapshot( package.ToSnapshot() ), Array.Empty<RequiredContent>(), Array.Empty<string>(), false );

	[TestMethod]
	public void AParkWithOffExtensionsAsksToTurnThemOnAndVisitsOnlyWhenConfirmed()
	{
		var flow = Flow( out var stack );
		var previous = OpenTpwExtensions.Active;
		try
		{
			OpenTpwExtensions.Activate( With( (OfficialExtensions.Winter, false) ) );
			var visits = 0;
			flow.ConfirmVisitExtensions( Visit( Package( Use( OfficialExtensions.Winter ) ) ), () => visits++, stack );
			Assert.AreEqual( 0, visits, "nothing starts before the player answers" );
			var dialog = stack.Top!;
			Assert.AreEqual( "extensionSwitch", dialog.Name );
			var text = ((UiLabel)dialog.Find( "message" )!).Text();
			StringAssert.Contains( text, "This park uses Winter, which is off. Turn Winter on and visit the park?" );
			StringAssert.DoesNotMatch( text, new System.Text.RegularExpressions.Regex( "From here on" ), "a visit continues nothing" );
			Assert.AreEqual( "Turn Winter on and visit", ((UiButton)dialog.Find( "choice0" )!).HelpSource!() );
			// the X goes back without a visit and without a change
			((UiButton)dialog.Find( "choice1" )!).Clicked!();
			Assert.AreEqual( 0, visits );
			Assert.IsFalse( OpenTpwExtensions.Active.IsEnabled( OfficialExtensions.Winter.Id ) );
			Assert.IsFalse( File.Exists( Path.Combine( root, ModSettings.FileName ) ) );
			// the tick turns the extension on for the run, remembers it in mods.json and visits
			flow.ConfirmVisitExtensions( Visit( Package( Use( OfficialExtensions.Winter ) ) ), () => visits++, stack );
			((UiButton)stack.Top!.Find( "choice0" )!).Clicked!();
			Assert.AreEqual( 1, visits );
			Assert.IsTrue( OpenTpwExtensions.Active.IsEnabled( OfficialExtensions.Winter.Id ), "on for the visit" );
			StringAssert.Contains( File.ReadAllText( Path.Combine( root, ModSettings.FileName ) ), OfficialExtensions.Winter.Id );
			// now it is on: the next visit goes ahead without a question
			flow.ConfirmVisitExtensions( Visit( Package( Use( OfficialExtensions.Winter ) ) ), () => visits++, stack );
			Assert.AreEqual( 2, visits );
		}
		finally { OpenTpwExtensions.Activate( previous ); restore(); }
	}

	[TestMethod]
	public void AVisitNeverRunsWithoutAnExtensionThatCannotBeTurnedOn()
	{
		var flow = Flow( out var stack );
		var previous = OpenTpwExtensions.Active;
		try
		{
			OpenTpwExtensions.Activate( With( (OfficialExtensions.Winter, false) ) );
			var visits = 0;
			flow.ConfirmVisitExtensions( Visit( Package( new ExtensionUse( "opentpw.future", "1.0.0" ) ) ), () => visits++, stack );
			Assert.AreEqual( 0, visits );
			Assert.AreEqual( "visitExtensionsRefused", stack.Top!.Name );
			var text = ((UiLabel)stack.Top.Find( "message" )!).Text();
			StringAssert.Contains( text, "opentpw.future" );
			StringAssert.Contains( text, "cannot be turned on" );
			// no package extensions, and a package of rule-only extensions, visit at once
			stack.Pop();
			flow.ConfirmVisitExtensions( Visit( SharedParkTests.LayoutPackage() ), () => visits++, stack );
			flow.ConfirmVisitExtensions( Visit( Package( Use( OfficialExtensions.RainMood ) ) ), () => visits++, stack );
			Assert.AreEqual( 2, visits );
		}
		finally { OpenTpwExtensions.Activate( previous ); restore(); }
	}

	[TestMethod]
	public void TheVisitTextsExistInEveryLanguage()
	{
		foreach ( var key in new[] { OpenTpwText.ExtensionVisitTurnOn, OpenTpwText.ExtensionVisitTurnOnQuestion } )
			foreach ( var language in SupplementaryStrings.SupportedLanguages )
			{
				StringAssert.Contains( SupplementaryStrings.Get( key, language ), "{0}", $"{key} in {language}" );
				if ( language != "English" )
					Assert.AreNotEqual( SupplementaryStrings.Get( key, "English" ), SupplementaryStrings.Get( key, language ), $"{key} is translated into {language}" );
			}
		foreach ( var label in new[] { OnlineLabel.VisitNeedsExtensions, OnlineLabel.VisitEnableOnCommandLine, OnlineLabel.PublishNoPark, OnlineLabel.PublishReadOnly, OnlineLabel.PublishCoopGuest, OnlineLabel.OutboxReadyToSend, OnlineLabel.ServerNeedsExtensionsUpdate } )
			foreach ( var (language, table) in OnlineStrings.Supplementary )
			{
				Assert.IsTrue( table.TryGetValue( label, out var text ) && text.Length > 0, $"{label} in {language}" );
				if ( language != "English" )
					Assert.AreNotEqual( OnlineStrings.Supplementary["English"][label], text, $"{label} is translated into {language}" );
			}
		StringAssert.Contains( OnlineStrings.Supplementary["Dutch"][OnlineLabel.VisitEnableOnCommandLine], "{1}" );
		var dutch = ExtensionSwitchDialog.Text( OriginalUiTests.FakeStrings( "Dutch" ), ParkVisitExtensions.Plan( Winter, With( (OfficialExtensions.Winter, false) ), Array.Empty<string>() )!.ToSwitch(), visit: true );
		StringAssert.Contains( dutch, "bezoeken?" );
	}

	// ---- The menus --------------------------------------------------------------------------------------------------

	private (OnlineScreens Screens, UiScreenStack Stack, OnlineSession Session) Screens( Func<Level?> level )
	{
		var session = new OnlineSession( new OnlineFolders( Path.Combine( root, "online" ) ) );
		var stack = new UiScreenStack();
		stack.Push( new UiScreen( "menu" ) );
		var screens = new OnlineScreens( stack, OriginalUiTests.FakeStrings(), new UiModels( name => throw new FileNotFoundException( name ) ), new OnlineHost { Session = session, Level = level } );
		return (screens, stack, session);
	}

	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 2048, 1536 ) };

	/// <summary>The help the pointer resting on the middle of the element <paramref name="id"/> would show.</summary>
	private static string? Hover( UiScreen screen, string id )
	{
		var rect = screen.Find( id )!.ScreenRect( new UiCanvas( 2048, 1536 ) );
		return screen.DisabledHelpAt( new UiCanvas( 2048, 1536 ), new System.Numerics.Vector2( rect.X + rect.Width / 2, rect.Y + rect.Height / 2 ) );
	}

	private static Level LevelOf( ParkVisitInfo? visit = null, bool coopGuest = false )
	{
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "<Visit>k__BackingField", visit );
		level.IsCoopGuestPark = coopGuest;
		return level;
	}

	[TestMethod]
	public void TheDisabledPublishLineSaysWhy()
	{
		var previous = OpenTpwExtensions.Active;
		try
		{
			OpenTpwExtensions.Activate( WithCommunityMod() );
			var communityRefusal = ParkSharing.ModsRefusal( LevelOf() )!;
			OpenTpwExtensions.Activate( With( (OfficialExtensions.RainMood, true) ) );
			var cases = new (string Name, Func<Level?> Level, string? Why)[]
			{
				("no park", () => null, OnlineStrings.Get( OnlineLabel.PublishNoPark )),
				("a visit", () => LevelOf( Visit( SharedParkTests.LayoutPackage() ) ), OnlineStrings.Get( OnlineLabel.PublishReadOnly )),
				("a shared park's guest", () => LevelOf( coopGuest: true ), OnlineStrings.Get( OnlineLabel.PublishCoopGuest )),
				("an official extension", () => LevelOf(), null),
			};
			foreach ( var (name, level, why) in cases )
			{
				var (screens, stack, session) = Screens( level );
				using ( session )
				{
					screens.ShowMenu();
					var menu = stack.Top!;
					menu.Updating!( Context() );
					var publish = (UiButton)menu.Find( "publish" )!;
					Assert.AreEqual( why == null, publish.Enabled, name );
					Assert.AreEqual( why, why == null ? null : Hover( menu, "publish" ), $"{name}: the greyed line says why on hover" );
					if ( why != null )
					{
						Assert.AreEqual( $"{OnlineStrings.Ui( OnlineStrings.PublishPark, "Publish park" )}: {why}", ((UiLabel)menu.Find( "status" )!).Text!(), $"{name}: the status line gives the reason too, for a keyboard or a gamepad" );
						// through the screen's own input path, as a pointer resting on the line reaches it
						var context = Context();
						var rect = publish.ScreenRect( context.Canvas );
						stack.Update( context, UiInput.Idle( new System.Numerics.Vector2( rect.X + rect.Width / 2, rect.Y + rect.Height / 2 ) ) );
						Assert.AreEqual( why, context.HoverHelp, $"{name}: the pointer on the greyed line sets the hover help" );
					}
					// the other lines have no reason to give while they are enabled
					Assert.IsNull( Hover( menu, "sendPostcard" ) );
				}
			}
			// community mods in the park: the refusal text itself, naming the mod
			OpenTpwExtensions.Activate( WithCommunityMod() );
			var (modScreens, modStack, modSession) = Screens( () => LevelOf() );
			using ( modSession )
			{
				modScreens.ShowMenu();
				var menu = modStack.Top!;
				menu.Updating!( Context() );
				Assert.IsFalse( ((UiButton)menu.Find( "publish" )!).Enabled );
				Assert.AreEqual( communityRefusal, Hover( menu, "publish" ) );
				StringAssert.Contains( communityRefusal, "community.tpi-rides" );
			}
		}
		finally { OpenTpwExtensions.Activate( previous ); }
	}

	[TestMethod]
	public void ThePauseMenusPublishLineSaysWhyOnHover()
	{
		var previous = OpenTpwExtensions.Active;
		try
		{
			var level = LevelOf();
			OpenTpwExtensions.Activate( WithCommunityMod() );
			var refusal = ParkSharing.PublishRefusal( level );
			Assert.IsNotNull( refusal );
			var hud = BareHud( level, new HudHost { PublishPark = _ => { }, PublishRefusal = () => ParkSharing.PublishRefusal( level ) } );
			hud.OpenPauseMenu();
			var line = (UiMenuItem)hud.Stack.Top!.Find( nameof( UIStrings.PublishPark ) )!;
			Assert.IsFalse( line.Enabled );
			Assert.IsTrue( line.HelpWhenDisabled );
			Assert.AreEqual( refusal, line.CurrentHelp );
			hud.ClosePauseMenu();
			// an official extension alone: the line is enabled
			OpenTpwExtensions.Activate( With( (OfficialExtensions.RainMood, true) ) );
			var open = BareHud( level, new HudHost { PublishPark = _ => { }, PublishRefusal = () => ParkSharing.PublishRefusal( level ) } );
			open.OpenPauseMenu();
			Assert.IsTrue( ((UiMenuItem)open.Stack.Top!.Find( nameof( UIStrings.PublishPark ) )!).Enabled );
		}
		finally { OpenTpwExtensions.Activate( previous ); }
	}

	private static ParkHud BareHud( Level level, HudHost host )
	{
		var hud = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		Set( hud, "level", level ); Set( hud, "strings", OriginalUiTests.FakeStrings() ); Set( hud, "host", host ); Set( hud, "<Stack>k__BackingField", new UiScreenStack() );
		var screen = new UiScreen( "hud" ) { Modal = false };
		Set( hud, "hud", screen );
		hud.Stack.Push( screen );
		return hud;
	}

	[TestMethod]
	public void TheMenusPostcardLineNeverOverlapsAButton()
	{
		var (screens, stack, session) = Screens( () => null );
		using ( session )
		{
			screens.ShowMenu();
			var menu = stack.Top!;
			var buttons = menu.Elements.OfType<UiButton>().Where( button => button.Id != "back" ).ToArray();
			Assert.AreEqual( 10, buttons.Length );
			var waiting = menu.Find( "waiting" )!;
			var window = UiDialogs.CenteredWindow( 1200, 1460 );
			var lowest = buttons.Max( button => button.Bounds.Bottom );
			Assert.IsTrue( waiting.Bounds.Y >= lowest, $"the postcard line starts at {waiting.Bounds.Y}, below the lowest button ({lowest})" );
			Assert.IsTrue( waiting.Bounds.Bottom <= menu.Find( "status" )!.Bounds.Y + 1, "and ends above the status line" );
			Assert.IsTrue( waiting.Bounds.Bottom <= window.Bottom - 187, "and above the back button" );
			for ( var index = 1; index < buttons.Length; index++ )
				Assert.IsTrue( buttons[index].Bounds.Y >= buttons[index - 1].Bounds.Bottom, "the buttons do not overlap each other" );
			Assert.IsTrue( buttons.All( button => button.Bounds.Bottom <= waiting.Bounds.Y ) );
		}
	}
}
