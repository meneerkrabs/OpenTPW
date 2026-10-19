using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Mods;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// Loading a park save whose official extensions differ from the active ones (docs/MODS.md, "Loading a park with other extensions"): rule-only extensions switch in either direction, content extensions the
/// park uses are switched on, and everything else stays strict. Headless: the plan, the rewritten save, the switched active set, the dialog and the revert of a failed load, with no original game data.
/// </summary>
// [EXT:save-extension-switch] user-approved 2026-10-19 ("Ook ontbrekende extensie aanzetten"); [EXT:extensions-apply-at-load] the same approval: a switched extension applies without a restart
[TestClass]
[DoNotParallelize]
public class SaveExtensionSwitchTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

	private static ModSet With( params (IOpenTpwExtension Extension, bool Enabled)[] choices ) =>
		ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = choices.ToDictionary( choice => choice.Extension.Id, choice => choice.Enabled ) }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );

	private static ModSet Defaults => ModSet.Defaults( OfficialExtensions.All );

	private static ParkModRecord Record( IOpenTpwExtension extension, string? version = null ) => new( extension.Id, version ?? extension.Version.ToString(), ModSet.NoFilesSha256 );

	private static IEnumerable<IOpenTpwExtension> RuleOnly => OfficialExtensions.All.Where( extension => ParkExtensionSwitch.RuleOnlyExtensionIds.Contains( extension.Id ) );
	private static IEnumerable<IOpenTpwExtension> Content => OfficialExtensions.All.Where( extension => ParkResumeMods.UsedOnlyExtensionIds.Contains( extension.Id ) );

	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize()
	{
		previousEdition = GameEditions.Current;
		GameEditions.Select( GameEdition.ThemeParkWorld );
	}

	[TestCleanup]
	public void Cleanup() => GameEditions.Select( previousEdition );

	// ---- The classification ------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheRuleOnlyAndContentExtensionsAreTheVerifiedOnes()
	{
		CollectionAssert.AreEquivalent( new[] { "opentpw.rain-mood", "opentpw.winter-weather", "opentpw.winter-cold", "opentpw.winter-always", "opentpw.smarter-guests" },
			ParkExtensionSwitch.RuleOnlyExtensionIds.ToArray(), "the rule-only extensions: no content, no state a park needs them for" );
		CollectionAssert.AreEquivalent( new[] { "opentpw.pools", "opentpw.umbrellas", "opentpw.winter", "opentpw.cheats", "opentpw.evening" }, ParkResumeMods.UsedOnlyExtensionIds.ToArray(), "the content extensions a load may switch on" );
		Assert.IsFalse( ParkExtensionSwitch.RuleOnlyExtensionIds.Overlaps( ParkResumeMods.UsedOnlyExtensionIds ) );
		foreach ( var extension in RuleOnly.Concat( Content ) )
			Assert.IsTrue( extension.AffectsSimulation && extension.PlayerSelectable, $"{extension.Id} is recorded in saves and a player can switch it" );
		// every other simulation extension stays strict: the cross-edition objects extension changes which objects a park's catalog holds
		var others = OfficialExtensions.All.Where( extension => extension.AffectsSimulation && !ParkExtensionSwitch.RuleOnlyExtensionIds.Contains( extension.Id ) && !ParkResumeMods.UsedOnlyExtensionIds.Contains( extension.Id ) );
		CollectionAssert.AreEqual( new[] { OfficialExtensions.CrossEditionObjects.Id }, others.Select( extension => extension.Id ).ToArray() );
	}

	[TestMethod]
	public void AParkTheLoadCanSwitchIsNotListedAsNeedingAMod()
	{
		var rain = new[] { Record( OfficialExtensions.RainMood ) };
		Assert.IsNull( ParkExtensionSwitch.RequiresText( rain, Defaults ), "a rule the park played with is asked about at the load" );
		Assert.IsNull( ParkExtensionSwitch.RequiresText( null, Defaults ) );
		var community = new[] { new ParkModRecord( "community.tpi-rides", "1.0.0", new string( 'a', 64 ) ) };
		Assert.AreEqual( "community.tpi-rides 1.0.0", ParkExtensionSwitch.RequiresText( community, Defaults ), "a strict need is listed as before" );
		Assert.AreEqual( ParkResumeMods.MissingText( community.Concat( rain ).ToArray(), Defaults ), ParkExtensionSwitch.RequiresText( community.Concat( rain ).ToArray(), Defaults ) );
	}

	[TestMethod]
	public void AgreeingSetsNeedNoSwitchAndTheStrictReasonsAreUnchanged()
	{
		Assert.IsNull( ParkExtensionSwitch.Plan( null, Defaults ) );
		var on = With( (OfficialExtensions.RainMood, true) );
		Assert.IsNull( ParkExtensionSwitch.Plan( ParkResumeMods.Capture( on ), on ) );
		// the structured reasons carry Unmet's texts in its order
		var saved = new[] { new ParkModRecord( "community.tpi-rides", "1.0.0", new string( 'a', 64 ) ), Record( OfficialExtensions.RainMood ) };
		CollectionAssert.AreEqual( ParkResumeMods.Unmet( saved, Defaults ).ToArray(), ParkResumeMods.UnmetItems( saved, Defaults ).Select( item => item.Reason ).ToArray() );
	}

	// ---- Rule-only extensions in both directions ---------------------------------------------------------------------

	[TestMethod]
	public void ARuleOnlyExtensionTheParkWasPlayedWithoutContinuesWithIt()
	{
		foreach ( var extension in RuleOnly )
		{
			var active = With( (extension, true) );
			var plan = ParkExtensionSwitch.Plan( null, active, Array.Empty<string>() );
			Assert.IsNotNull( plan, extension.Id );
			Assert.IsTrue( plan.CanSwitch, $"{extension.Id}: {string.Join( "; ", plan.Refusals )}" );
			CollectionAssert.AreEqual( new[] { extension.Id }, plan.ContinueWith.Select( item => item.Id ).ToArray() );
			Assert.AreEqual( 0, plan.ContinueWithout.Count + plan.TurnOn.Count + plan.ContinueNewVersion.Count );
			Assert.AreSame( active, plan.ActiveAfter( active ), "a rule-only switch loads under the current set" );
			var switched = plan.Apply( ParkResumeSaveFileTests.Envelope( 300 ), active );
			CollectionAssert.AreEqual( new[] { extension.Id }, switched.Mods!.Select( record => record.Id ).ToArray(), "the park continues recording the rule" );
			Assert.AreEqual( 0, ParkResumeMods.Unmet( switched.Mods, active ).Count, "the switched save passes the strict check" );
			ParkResumeSaveFile.ValidateShape( switched );
			if ( extension == OfficialExtensions.SmarterGuests )
			{
				Assert.IsNotNull( switched.SmarterGuests, "the smarter guests start fresh: the section a new park has" );
				Assert.AreEqual( switched.Seed, switched.SmarterGuests.ParkSeed );
				Assert.IsFalse( switched.SmarterGuests.Bootstrapped );
				Assert.AreEqual( 0, switched.SmarterGuests.Births.Count );
			}
			else
				Assert.IsNull( switched.SmarterGuests );
		}
	}

	[TestMethod]
	public void ARuleOnlyExtensionTheParkWasPlayedWithStopsWhenItIsOff()
	{
		foreach ( var extension in RuleOnly )
		{
			var saved = ParkResumeMods.Capture( With( (extension, true) ) );
			var plan = ParkExtensionSwitch.Plan( saved, Defaults, Array.Empty<string>() )!;
			Assert.IsTrue( plan.CanSwitch, $"{extension.Id}: {string.Join( "; ", plan.Refusals )}" );
			CollectionAssert.AreEqual( new[] { extension.Id }, plan.ContinueWithout.Select( item => item.Id ).ToArray() );
			var envelope = ParkResumeSaveFileTests.Envelope( 300 ) with { Mods = saved };
			if ( extension == OfficialExtensions.SmarterGuests )
				envelope = envelope with { SmarterGuests = new SmarterGuestsSaveData { ParkSeed = envelope.Seed, NextBirthId = 3, Bootstrapped = false, Births = new[] { new SmarterGuestBirth( envelope.Guests.Guests[0].Id, 1 ) },
					Meals = new[] { new MealTime( 1, 0 ) } } };
			ParkResumeSaveFile.ValidateShape( envelope );
			var switched = plan.Apply( envelope, Defaults );
			Assert.IsNull( switched.Mods, "the park continues without the rule, as a park saved without it" );
			Assert.IsNull( switched.SmarterGuests, "the smarter guests' own state goes with the extension (no script mod shares it)" );
			Assert.AreEqual( 0, ParkResumeMods.Unmet( switched.Mods, Defaults ).Count );
			ParkResumeSaveFile.ValidateShape( switched );
		}
	}

	[TestMethod]
	public void ARuleOnlyExtensionOfAnotherVersionContinuesInThisVersion()
	{
		var active = With( (OfficialExtensions.SmarterGuests, true) );
		var plan = ParkExtensionSwitch.Plan( new[] { Record( OfficialExtensions.SmarterGuests, "1.5.0" ) }, active, Array.Empty<string>() )!;
		Assert.IsTrue( plan.CanSwitch );
		Assert.AreEqual( (OfficialExtensions.SmarterGuests, "1.5.0"), plan.ContinueNewVersion.Single() );
		var envelope = ParkResumeSaveFileTests.Envelope( 300 );
		var births = new[] { new SmarterGuestBirth( envelope.Guests.Guests[0].Id, 1 ) };
		var switched = plan.Apply( envelope with { Mods = new[] { Record( OfficialExtensions.SmarterGuests, "1.5.0" ) },
			SmarterGuests = new SmarterGuestsSaveData { ParkSeed = 9, NextBirthId = 2, Bootstrapped = false, Births = births, Meals = new[] { new MealTime( 1, 0 ) }, Complaints = new[] { new MealTime( 1, 0 ) } } }, active );
		Assert.AreEqual( OfficialExtensions.SmarterGuests.Version.ToString(), switched.Mods!.Single().Version );
		Assert.AreEqual( 9UL, switched.SmarterGuests!.ParkSeed, "the guests keep their identity, so their traits" );
		CollectionAssert.AreEqual( births, switched.SmarterGuests.Births.ToArray() );
		Assert.IsNull( switched.SmarterGuests.Meals, "the older version's own state starts fresh in this version" );
		Assert.IsNull( switched.SmarterGuests.Complaints );
		ParkResumeSaveFile.ValidateShape( switched );
	}

	[TestMethod]
	public void SeveralRuleSwitchesInOneLoadKeepTheSetOrder()
	{
		var saved = ParkResumeMods.Capture( With( (OfficialExtensions.WinterCold, true), (OfficialExtensions.RainMood, true) ) );
		var active = With( (OfficialExtensions.WinterAlways, true), (OfficialExtensions.RainMood, true) );
		var plan = ParkExtensionSwitch.Plan( saved, active, Array.Empty<string>() )!;
		Assert.IsTrue( plan.CanSwitch );
		CollectionAssert.AreEqual( new[] { OfficialExtensions.WinterAlways.Id }, plan.ContinueWith.Select( item => item.Id ).ToArray() );
		CollectionAssert.AreEqual( new[] { OfficialExtensions.WinterCold.Id }, plan.ContinueWithout.Select( item => item.Id ).ToArray() );
		var switched = plan.Apply( ParkResumeSaveFileTests.Envelope( 300 ) with { Mods = saved }, active );
		CollectionAssert.AreEqual( ParkResumeMods.Capture( active )!.ToArray(), switched.Mods!.ToArray(), "exactly what a park saved now records" );
	}

	// ---- Content extensions ------------------------------------------------------------------------------------------

	[TestMethod]
	public void AContentExtensionTheParkUsesIsSwitchedOnAndLoads()
	{
		foreach ( var extension in Content )
		{
			var on = With( (extension, true) );
			var off = With( (extension, false) );
			var saved = ParkResumeMods.Capture( on, new HashSet<string> { extension.Id } );
			var plan = ParkExtensionSwitch.Plan( saved, off, Array.Empty<string>() )!;
			Assert.IsTrue( plan.CanSwitch, $"{extension.Id}: {string.Join( "; ", plan.Refusals )}" );
			CollectionAssert.AreEqual( new[] { extension.Id }, plan.TurnOn.Select( item => item.Id ).ToArray() );
			var after = plan.ActiveAfter( off );
			Assert.IsTrue( after.IsEnabled( extension.Id ) );
			CollectionAssert.AreEqual( on.Order.ToArray(), after.Order.ToArray(), "the switched set has the order a new start with the extension on resolves" );
			var switched = plan.Apply( ParkResumeSaveFileTests.Envelope( 300 ) with { Mods = saved }, after );
			CollectionAssert.AreEqual( saved!.ToArray(), switched.Mods!.ToArray(), "the content extension's record is the save's" );
			Assert.AreEqual( 0, ParkResumeMods.Unmet( switched.Mods, after ).Count );
		}
	}

	[TestMethod]
	public void AContentExtensionOnNowButUnusedNeedsNoSwitch()
	{
		foreach ( var extension in Content )
			Assert.IsNull( ParkExtensionSwitch.Plan( null, With( (extension, true) ) ), $"{extension.Id}: a park that does not use it loads with it on, as before" );
	}

	[TestMethod]
	public void AContentExtensionStaysStrictInAnotherVersionOnTheCommandLineOrInAThemeParkIncRun()
	{
		var off = With( (OfficialExtensions.Pools, false) );
		var otherVersion = ParkExtensionSwitch.Plan( new[] { Record( OfficialExtensions.Pools, "0.9.0" ) }, off, Array.Empty<string>() )!;
		Assert.IsFalse( otherVersion.CanSwitch );
		StringAssert.Contains( otherVersion.Refusals.Single(), "needs opentpw.pools 0.9.0" );
		var commandLine = ParkExtensionSwitch.Plan( new[] { Record( OfficialExtensions.Pools ) }, off, new[] { OfficialExtensions.Pools.Id } )!;
		Assert.IsFalse( commandLine.CanSwitch, "--disable-extension keeps it off for the run" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		var tpi = ParkExtensionSwitch.Plan( new[] { Record( OfficialExtensions.Pools ) }, off, Array.Empty<string>() )!;
		Assert.IsFalse( tpi.CanSwitch, "a Theme Park Inc run has none of the content extensions" );
	}

	[TestMethod]
	public void ARuleSwitchAndAContentSwitchGoTogether()
	{
		var saved = ParkResumeMods.Capture( With( (OfficialExtensions.Winter, true), (OfficialExtensions.WinterCold, true) ), new HashSet<string> { OfficialExtensions.Winter.Id } );
		var active = With( (OfficialExtensions.Winter, false), (OfficialExtensions.RainMood, true) );
		var plan = ParkExtensionSwitch.Plan( saved, active, Array.Empty<string>() )!;
		Assert.IsTrue( plan.CanSwitch );
		CollectionAssert.AreEqual( new[] { OfficialExtensions.Winter.Id }, plan.TurnOn.Select( item => item.Id ).ToArray() );
		CollectionAssert.AreEqual( new[] { OfficialExtensions.WinterCold.Id }, plan.ContinueWithout.Select( item => item.Id ).ToArray() );
		CollectionAssert.AreEqual( new[] { OfficialExtensions.RainMood.Id }, plan.ContinueWith.Select( item => item.Id ).ToArray() );
		var after = plan.ActiveAfter( active );
		var switched = plan.Apply( ParkResumeSaveFileTests.Envelope( 300 ) with { Mods = saved }, after );
		Assert.AreEqual( 0, ParkResumeMods.Unmet( switched.Mods, after ).Count );
		CollectionAssert.AreEquivalent( new[] { OfficialExtensions.Winter.Id, OfficialExtensions.RainMood.Id }, switched.Mods!.Select( record => record.Id ).ToArray() );
	}

	// ---- What stays strict -------------------------------------------------------------------------------------------

	[TestMethod]
	public void CommunityModsCrossEditionObjectsAndTheOrderStayStrict()
	{
		var community = new ParkModRecord( "community.tpi-rides", "1.0.0", new string( 'a', 64 ) );
		var missing = ParkExtensionSwitch.Plan( new[] { community }, Defaults, Array.Empty<string>() )!;
		Assert.IsFalse( missing.CanSwitch );
		CollectionAssert.AreEqual( ParkResumeMods.Unmet( new[] { community }, Defaults ).ToArray(), missing.Refusals.ToArray(), "the refusal is the strict load's" );
		// one strict reason refuses the whole load, rule switches included
		var mixed = ParkExtensionSwitch.Plan( new[] { community, Record( OfficialExtensions.RainMood ) }, Defaults, Array.Empty<string>() )!;
		Assert.IsFalse( mixed.CanSwitch );
		Assert.ThrowsException<InvalidOperationException>( () => mixed.Apply( ParkResumeSaveFileTests.Envelope( 300 ), Defaults ) );
		// the cross-edition objects extension is neither rule-only nor a content extension a load switches
		var crossOn = With( (OfficialExtensions.CrossEditionObjects, true) );
		Assert.IsFalse( ParkExtensionSwitch.Plan( null, crossOn, Array.Empty<string>() )!.CanSwitch );
		Assert.IsFalse( ParkExtensionSwitch.Plan( ParkResumeMods.Capture( crossOn ), Defaults, Array.Empty<string>() )!.CanSwitch );
		// a forged digest on an extension record is not an extension's
		Assert.IsFalse( ParkExtensionSwitch.Plan( new[] { Record( OfficialExtensions.RainMood ) with { ContentSha256 = new string( 'b', 64 ) } }, Defaults, Array.Empty<string>() )!.CanSwitch );
		// the order of the mods both have stays strict
		var both = With( (OfficialExtensions.RainMood, true), (OfficialExtensions.WinterCold, true), (OfficialExtensions.WinterWeather, true) );
		var reversed = ParkResumeMods.Capture( With( (OfficialExtensions.RainMood, true), (OfficialExtensions.WinterCold, true) ) )!.Reverse().ToArray();
		var order = ParkExtensionSwitch.Plan( reversed, both, Array.Empty<string>() )!;
		Assert.IsFalse( order.CanSwitch );
		StringAssert.Contains( order.Refusals.Single(), "needs its mods in the order" );
	}

	[TestMethod]
	public void InAThemeParkIncRunTheWeatherRulesAreNotRecordableAndContinueWithout()
	{
		var saved = ParkResumeMods.Capture( With( (OfficialExtensions.RainMood, true) ) );
		GameEditions.Select( GameEdition.ThemeParkInc );
		var active = With( (OfficialExtensions.RainMood, true) );
		Assert.IsFalse( ParkResumeMods.RecordableMods( active ).Any( mod => mod.Id == OfficialExtensions.RainMood.Id ) );
		Assert.IsNull( ParkExtensionSwitch.Plan( null, active ), "a Theme Park Inc run records nothing for it, so nothing differs" );
		var plan = ParkExtensionSwitch.Plan( saved, active, Array.Empty<string>() )!;
		CollectionAssert.AreEqual( new[] { OfficialExtensions.RainMood.Id }, plan.ContinueWithout.Select( item => item.Id ).ToArray() );
	}

	// ---- The switched set without a restart --------------------------------------------------------------------------

	[TestMethod]
	public void WithExtensionsGivesTheOrderAStartResolvesAndRefusesWhatNeedsARestart()
	{
		var choices = new[] { OfficialExtensions.Pools, OfficialExtensions.RainMood, OfficialExtensions.WinterCold, OfficialExtensions.Cheats };
		foreach ( var order in new[] { Array.Empty<string>(), new[] { OfficialExtensions.WinterCold.Id, OfficialExtensions.Cheats.Id }, new[] { "opentpw.rain-mood" } } )
			foreach ( var on in choices )
			{
				var start = ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Order = order }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
				var switched = start.WithExtensions( new Dictionary<string, bool> { [on.Id] = !start.IsEnabled( on.Id ) }, order );
				var restarted = ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Order = order, Enabled = new Dictionary<string, bool> { [on.Id] = !start.IsEnabled( on.Id ) } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
				CollectionAssert.AreEqual( restarted.Order.ToArray(), switched.Order.ToArray(), $"{on.Id} with order [{string.Join( ", ", order )}]" );
				CollectionAssert.AreEqual( restarted.SimulationMods.ToArray(), switched.SimulationMods.ToArray() );
				Assert.AreEqual( restarted.Digest, switched.Digest, "the files-based mods are the same" );
			}
		var defaults = Defaults;
		Assert.AreSame( defaults, defaults.WithExtensions( new Dictionary<string, bool> { [OfficialExtensions.Pools.Id] = true }, Array.Empty<string>() ), "no change: the same set" );
		Assert.ThrowsException<ArgumentException>( () => defaults.WithExtensions( new Dictionary<string, bool> { ["community.mod"] = true }, Array.Empty<string>() ) );
	}

	[TestMethod]
	public void ApplyingASwitchedSetChangesTheGatesAndTheCatalogRootsAndTellsTheHost()
	{
		var previous = OpenTpwExtensions.Active;
		var previousRoots = ObjectCatalog.ContentRoots;
		var previousOverride = PoolContent.DirectoryOverride;
		ModSet? told = null;
		void Told( ModSet set ) => told = set;
		try
		{
			PoolContent.DirectoryOverride = Path.Combine( RepositoryRoot(), "content", PoolContent.FolderName );
			var off = With( (OfficialExtensions.Pools, false) );
			ModStartup.ApplyExtensions( off );
			Assert.IsFalse( FeatureGates.Pools );
			Assert.IsFalse( ObjectCatalog.ContentRoots.Any( root => root.ModId == OfficialExtensions.Pools.Id ) );
			ModStartup.ExtensionsSwitched += Told;
			var on = ModStartup.SwitchExtensions( off, new Dictionary<string, bool> { [OfficialExtensions.Pools.Id] = true } );
			ModStartup.ApplyExtensions( on );
			Assert.IsTrue( FeatureGates.Pools, "the gate reads the switched set at once" );
			Assert.IsTrue( ObjectCatalog.ContentRoots.Any( root => root.ModId == OfficialExtensions.Pools.Id ), "the next catalog load lists the extension's objects" );
			Assert.AreSame( on, told, "the host hears of the switch (the browser fetches the sprite sets)" );
		}
		finally
		{
			ModStartup.ExtensionsSwitched -= Told;
			PoolContent.DirectoryOverride = previousOverride;
			OpenTpwExtensions.Activate( previous );
			ObjectCatalog.SetContentRoots( previousRoots );
		}
	}

	[TestMethod]
	public void SavingTheSwitchedChoiceKeepsTheOtherChoicesAndNeverReplacesAnUnreadableFile()
	{
		var folder = Directory.CreateTempSubdirectory( "opentpw-switch-settings-" ).FullName;
		try
		{
			var path = Path.Combine( folder, ModSettings.FileName );
			new ModSettings { Enabled = new Dictionary<string, bool> { ["community.tpi-rides"] = true, [OfficialExtensions.Evening.Id] = false }, Order = new[] { "community.tpi-rides" } }.Save( path );
			ModStartup.SaveExtensionChoices( path, new Dictionary<string, bool> { [OfficialExtensions.Pools.Id] = true } );
			var saved = ModSettings.Load( path, new List<string>() );
			Assert.IsTrue( saved.IsEnabled( OfficialExtensions.Pools.Id, false ) );
			Assert.IsTrue( saved.IsEnabled( "community.tpi-rides", false ) );
			Assert.IsFalse( saved.IsEnabled( OfficialExtensions.Evening.Id, true ) );
			CollectionAssert.AreEqual( new[] { "community.tpi-rides" }, saved.Order.ToArray() );
			File.WriteAllText( path, "{ not json" );
			Assert.ThrowsException<IOException>( () => ModStartup.SaveExtensionChoices( path, new Dictionary<string, bool> { [OfficialExtensions.Pools.Id] = true } ) );
			Assert.AreEqual( "{ not json", File.ReadAllText( path ), "the player's file is left as it was" );
		}
		finally { Directory.Delete( folder, true ); }
	}

	[TestMethod]
	public void TheCommandLineKeepsAnExtensionOffForTheRun()
	{
		var field = typeof( ModStartup ).GetProperty( "DisabledForRun", BindingFlags.Static | BindingFlags.NonPublic )!;
		var previous = field.GetValue( null );
		try
		{
			field.SetValue( null, new[] { OfficialExtensions.Pools.Id } );
			Assert.ThrowsException<InvalidOperationException>( () => ModStartup.SwitchExtensions( With( (OfficialExtensions.Pools, false) ), new Dictionary<string, bool> { [OfficialExtensions.Pools.Id] = true } ) );
			Assert.IsFalse( ParkExtensionSwitch.Plan( new[] { Record( OfficialExtensions.Pools ) }, With( (OfficialExtensions.Pools, false) ) )!.CanSwitch );
		}
		finally { field.SetValue( null, previous ); }
	}

	// ---- The dialog and the load -------------------------------------------------------------------------------------

	private string root = null!;
	private BaseFileSystem previousSaves = null!, previousFiles = null!;
	private Logger previousLog = null!;

	private GameFlow Flow( out FrontEndMenu menu, string language = "English" )
	{
		root = Directory.CreateTempSubdirectory( "opentpw-extension-switch-" ).FullName;
		previousSaves = SaveFileSystem; previousFiles = FileSystem; previousLog = Log; Log = new Logger();
		SaveFileSystem = new BaseFileSystem( root );
		Directory.CreateDirectory( Path.Combine( root, "data" ) );
		FileSystem = new BaseFileSystem( Path.Combine( root, "data" ) );
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		Set( flow, "<Strings>k__BackingField", OriginalUiTests.FakeStrings( language ) );
		Set( flow, "<ModSettingsPath>k__BackingField", Path.Combine( root, ModSettings.FileName ) );
		Set( flow, "<PersistSettings>k__BackingField", true );
		flow.ProgressCaptureOverride = () => false;
		var island = new LobbyIslandInfo( 0, "jungle", "", "", "", "jungle", 0, 0, (0, 0, 0), Array.Empty<string>(), false, 0 );
		menu = new FrontEndMenu( OriginalUiTests.FakeStrings( language ), new[] { island }, new FrontEndActions() );
		Set( flow, "<Menu>k__BackingField", menu );
		return flow;
	}

	private void Release()
	{
		SaveFileSystem = previousSaves; FileSystem = previousFiles; Log = previousLog;
		Directory.Delete( root, true );
	}

	private static bool Fails( Action load )
	{
		try { load(); return false; }
		catch ( Exception error ) when ( error is IOException or InvalidDataException or ArgumentException or InvalidOperationException ) { return true; }
	}

	private static void Set( object target, string name, object? value ) => target.GetType().GetField( name, Private )!.SetValue( target, value );

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "global.json" ) ) )
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException( "The repository root (global.json) was not found." );
	}

	private string SaveWith( IReadOnlyList<ParkModRecord>? mods )
	{
		var path = SaveFileSystem.GetAbsolutePath( "opentpw-jungle-world.json" );
		ParkResumeSaveFile.Save( path, ParkResumeSaveFileTests.Envelope( 300 ) with { Mods = mods } );
		return path;
	}

	[TestMethod]
	public void ARuleSwitchAsksAndContinuesOnlyWhenConfirmed()
	{
		var flow = Flow( out var menu );
		var previous = OpenTpwExtensions.Active;
		try
		{
			OpenTpwExtensions.Activate( With( (OfficialExtensions.RainMood, true) ) );
			var path = SaveWith( null );
			var asked = new List<ParkExtensionSwitch?>();
			flow.ConfirmExtensions( path, asked.Add );
			Assert.AreEqual( 0, asked.Count, "nothing loads before the player answers" );
			var dialog = menu.Stack.Top!;
			Assert.AreEqual( "extensionSwitch", dialog.Name );
			var text = ((UiLabel)dialog.Find( "message" )!).Text();
			StringAssert.Contains( text, "This park was played without Rain mood. Continue with Rain mood?" );
			StringAssert.Contains( text, "From here on the park plays with the extensions that are on now." );
			Assert.AreEqual( "Continue", ((UiButton)dialog.Find( "choice0" )!).HelpSource!() );
			// the X goes back without loading
			((UiButton)dialog.Find( "choice1" )!).Clicked!();
			Assert.AreNotSame( dialog, menu.Stack.Top );
			Assert.AreEqual( 0, asked.Count );
			flow.ConfirmExtensions( path, asked.Add );
			((UiButton)menu.Stack.Top!.Find( "choice0" )!).Clicked!();
			Assert.AreEqual( 1, asked.Count );
			CollectionAssert.AreEqual( new[] { OfficialExtensions.RainMood.Id }, asked[0]!.ContinueWith.Select( item => item.Id ).ToArray() );
			Assert.AreNotEqual( "extensionSwitch", menu.Stack.Top!.Name, "the dialog is gone once it answered" );
		}
		finally { OpenTpwExtensions.Activate( previous ); Release(); }
	}

	[TestMethod]
	public void AStrictOrAgreeingSaveLoadsAtOnceWithoutAQuestion()
	{
		var flow = Flow( out var menu );
		var previous = OpenTpwExtensions.Active;
		try
		{
			OpenTpwExtensions.Activate( Defaults );
			var asked = new List<ParkExtensionSwitch?>();
			flow.ConfirmExtensions( SaveWith( null ), asked.Add );
			flow.ConfirmExtensions( SaveWith( new[] { new ParkModRecord( "community.tpi-rides", "1.0.0", new string( 'a', 64 ) ) } ), asked.Add );
			flow.ConfirmExtensions( null, asked.Add );
			flow.ConfirmExtensions( Path.Combine( root, "missing.json" ), asked.Add );
			CollectionAssert.AreEqual( new ParkExtensionSwitch?[] { null, null, null, null }, asked.ToArray(), "the strict load runs and says what is unmet, as before" );
			Assert.AreNotEqual( "extensionSwitch", menu.Stack.Top!.Name );
			// a Theme Park Inc run does not ask either
			OpenTpwExtensions.Activate( With( (OfficialExtensions.SmarterGuests, true) ) );
			GameEditions.Select( GameEdition.ThemeParkInc );
			flow.ConfirmExtensions( SaveWith( null ), asked.Add );
			Assert.AreEqual( 5, asked.Count );
			Assert.IsNull( asked[4] );
		}
		finally { OpenTpwExtensions.Activate( previous ); Release(); }
	}

	[TestMethod]
	public void AContentSwitchOffersToTurnTheExtensionOn()
	{
		var flow = Flow( out var menu );
		var previous = OpenTpwExtensions.Active;
		try
		{
			OpenTpwExtensions.Activate( With( (OfficialExtensions.Pools, false) ) );
			flow.ConfirmExtensions( SaveWith( new[] { Record( OfficialExtensions.Pools ) } ), _ => { } );
			var dialog = menu.Stack.Top!;
			StringAssert.Contains( ((UiLabel)dialog.Find( "message" )!).Text(), "This park uses Pools, which is off. Turn Pools on and load the park?" );
			Assert.AreEqual( "Turn Pools on and load", ((UiButton)dialog.Find( "choice0" )!).HelpSource!() );
		}
		finally { OpenTpwExtensions.Activate( previous ); Release(); }
	}

	[TestMethod]
	public void TheDialogTextsExistInEveryInterfaceLanguage()
	{
		var keys = new[] { OpenTpwText.ExtensionSwitchPlayedWithout, OpenTpwText.ExtensionSwitchPlayedWith, OpenTpwText.ExtensionSwitchOtherVersion, OpenTpwText.ExtensionSwitchNeeds,
			OpenTpwText.ExtensionSwitchTurnOn, OpenTpwText.ExtensionSwitchTurnOnQuestion };
		Assert.AreEqual( 11, SupplementaryStrings.SupportedLanguages.Count );
		foreach ( var language in SupplementaryStrings.SupportedLanguages )
		{
			foreach ( var key in keys )
				StringAssert.Contains( SupplementaryStrings.Get( key, language ), "{0}", $"{key} in {language}" );
			foreach ( var key in new[] { OpenTpwText.ExtensionSwitchNewRun, OpenTpwText.ExtensionSwitchContinue, OpenTpwText.ExtensionPoolsName } )
				Assert.IsFalse( string.IsNullOrWhiteSpace( SupplementaryStrings.Get( key, language ) ), $"{key} in {language}" );
			if ( language != "English" )
				foreach ( var key in keys.Append( OpenTpwText.ExtensionSwitchNewRun ) )
					Assert.AreNotEqual( SupplementaryStrings.Get( key, "English" ), SupplementaryStrings.Get( key, language ), $"{key} is translated into {language}" );
		}
		var plan = ParkExtensionSwitch.Plan( null, With( (OfficialExtensions.WinterCold, true) ), Array.Empty<string>() )!;
		var dutch = ExtensionSwitchDialog.Text( OriginalUiTests.FakeStrings( "Dutch" ), plan );
		StringAssert.Contains( dutch, "Dit park is gespeeld zonder" );
	}

	[TestMethod]
	public void AFailedSwitchedLoadSwitchesTheExtensionsBackAndWritesNoChoice()
	{
		var flow = Flow( out _ );
		var previous = OpenTpwExtensions.Active;
		var previousRoots = ObjectCatalog.ContentRoots;
		try
		{
			var off = With( (OfficialExtensions.Pools, false) );
			ModStartup.ApplyExtensions( off );
			var snapshot = ParkResumeSaveFileTests.Envelope( 300 ) with { Mods = new[] { Record( OfficialExtensions.Pools ) } };
			var plan = ParkExtensionSwitch.Plan( snapshot.Mods, off, Array.Empty<string>() )!;
			Assert.IsTrue( plan.CanSwitch );
			// no game data here: the load fails after the switch, which must be undone
			Assert.IsTrue( Fails( () => flow.ResumePark( snapshot, extensionSwitch: plan ) ) );
			Assert.AreSame( off, OpenTpwExtensions.Active, "the set before the load is active again" );
			Assert.IsFalse( FeatureGates.Pools );
			Assert.IsFalse( File.Exists( Path.Combine( root, ModSettings.FileName ) ), "a failed load keeps no choice" );
			// a strict load of the same save never switches
			Assert.IsTrue( Fails( () => flow.ResumePark( snapshot ) ) );
			Assert.AreSame( off, OpenTpwExtensions.Active );
		}
		finally { OpenTpwExtensions.Activate( previous ); ObjectCatalog.SetContentRoots( previousRoots ); Release(); }
	}
}
