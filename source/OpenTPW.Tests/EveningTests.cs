using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenTPW.Mods;
using OpenTPW.UI.Original;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:evening] The evening and night extension (docs/EVENING.md): the time-of-day curves, the play-time clock, the options value and its file, the lamp table, the lamps' resolution against
/// a model, the uniform and shader layout, the extension's registration and the guarantee that the cycle touches no simulation (the torches are tested in EveningTorchTests). No GPU; the tests that need the game's data are
/// inconclusive without OPENTPW_GAME_PATH, and everything else runs on the repository's own files.
/// </summary>
[TestClass]
[DoNotParallelize]
public class EveningTests
{
	[TestMethod]
	public void ThePerfHookTurnsTheLookOffForTheRunAndNeverChangesTheSessionsModSet()
	{
		// a session with non-default choices: smarter guests on, restored advice off
		var session = ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = new Dictionary<string, bool>
		{
			[OfficialExtensions.SmarterGuests.Id] = true, [OfficialExtensions.RestoredAdvice.Id] = false,
		} }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		var saved = Level.EveningPaceOverride;
		try
		{
			using ( OpenTpwExtensions.Use( session ) )
			{
				Assert.IsTrue( Level.EveningOn, "on by default" );
				Assert.IsTrue( Level.PerfEvening( "off" ) );
				Assert.IsFalse( Level.EveningOn, "the cycle and the lamps are skipped" );
				Assert.IsTrue( FeatureGates.Evening, "but the extension stays on: it changes the simulation (its torches), so the run's set never changes" );
				Assert.AreSame( session, OpenTpwExtensions.Active, "the session's set, its mods, refusals and order, untouched" );
				Assert.IsNull( Level.EveningPaceOverride, "off clears the held look" );
				Assert.IsTrue( Level.PerfEvening( "night" ) );
				Assert.IsTrue( Level.EveningOn );
				Assert.AreEqual( EveningPace.AlwaysNight, Level.EveningPaceOverride );
				Assert.IsTrue( Level.PerfEvening( "off" ) && Level.PerfEvening( "on" ) );
				Assert.IsTrue( Level.EveningOn );
				Assert.IsNull( Level.EveningPaceOverride, "on clears the held look too" );
				Assert.AreSame( session, OpenTpwExtensions.Active );
				Assert.IsFalse( Level.PerfEvening( "noon" ), "an unknown mode is refused" );
				Assert.IsTrue( Level.EveningOn, "and changes nothing" );
			}
			using ( OpenTpwExtensions.Use( Set( false ) ) )
				Assert.IsFalse( Level.EveningOn, "the extension off: nothing to show" );
		}
		finally
		{
			Level.EveningPaceOverride = saved;
			Level.EveningLookOff = false;
		}
	}

	[TestMethod]
	public void OnlyAPresentationExtensionCanBeSwitchedForARunAndTheEveningExtensionNoLongerIsOne()
	{
		var set = ModSet.Defaults( OfficialExtensions.All );
		var advice = OfficialExtensions.RestoredAdvice;
		Assert.ThrowsException<ArgumentException>( () => set.WithPresentationExtension( OfficialExtensions.SmarterGuests, true ) );
		Assert.ThrowsException<ArgumentException>( () => set.WithPresentationExtension( OfficialExtensions.Evening, false ), "the torches make the evening extension a simulation extension (used only while a park holds one)" );
		Assert.AreSame( set, set.WithPresentationExtension( advice, set.IsEnabled( advice.Id ) ), "no change, the same set" );
		Assert.ThrowsException<ArgumentException>( () => set.WithPresentationExtension( new OpenTpwExtension( "test.unknown", "Unknown", new ModVersion( 1, 0, 0 ), false, false ), true ), "not an extension of this set" );
		var off = set.WithPresentationExtension( advice, false );
		Assert.IsFalse( off.IsEnabled( advice.Id ) );
		Assert.IsFalse( off.Extensions.Single( item => item.Extension == advice ).Enabled, "the resolved extension row follows" );
		CollectionAssert.AreEqual( set.SimulationMods.ToArray(), off.SimulationMods.ToArray() );
		CollectionAssert.AreEqual( set.Order.ToArray(), off.WithPresentationExtension( advice, true ).Order.ToArray(), "on again takes Resolve's place" );
	}

	internal static ModSet Set( bool evening ) => ModSet.Resolve( Array.Empty<DiscoveredMod>(),
		new ModSettings { Enabled = new Dictionary<string, bool> { [OfficialExtensions.Evening.Id] = evening } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );

	// the checkout's root (the source files are not copied to the test output, unlike content/, which RepositoryRoot finds in the output first)
	private static string SourceRoot()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "source", "OpenTPW", "World", "Pools", "PoolSpray.cs" ) ) )
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException( "source/OpenTPW" );
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !Directory.Exists( Path.Combine( directory.FullName, "content", "shaders" ) ) )
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException( "content/shaders" );
	}

	private static float MaxOf( NVector3 value ) => Math.Max( value.X, Math.Max( value.Y, value.Z ) );

	private static float MinOf( NVector3 value ) => Math.Min( value.X, Math.Min( value.Y, value.Z ) );

	internal static Level BareLevel() => (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );

	// ---- the curve ----

	[TestMethod]
	public void DaylightIsFullAroundNoonZeroAtNightAndSymmetricAroundMidnight()
	{
		Assert.AreEqual( 1f, EveningClock.Daylight( 0.0 ) );
		Assert.AreEqual( 1f, EveningClock.Daylight( EveningClock.FullDayHalf ) );
		Assert.AreEqual( 1f, EveningClock.Daylight( 1.0 - EveningClock.FullDayHalf ) );
		Assert.AreEqual( 0f, EveningClock.Daylight( EveningClock.FullNightFrom ) );
		Assert.AreEqual( 0f, EveningClock.Daylight( 0.5 ) );
		Assert.AreEqual( 0f, EveningClock.Daylight( 1.0 - EveningClock.FullNightFrom ) );
		Assert.AreEqual( 0.5f, EveningClock.Daylight( 0.225 ), 1e-5f, "the middle of the dusk" );
		Assert.AreEqual( 0.5f, EveningClock.Daylight( 0.775 ), 1e-5f, "the middle of the dawn" );
		for ( var step = 0; step <= 100; step++ )
		{
			var phase = step / 200.0;
			Assert.AreEqual( EveningClock.Daylight( phase ), EveningClock.Daylight( 1.0 - phase ), 1e-6f, $"mirrored at {phase}" );
		}
	}

	[TestMethod]
	public void DaylightFallsMonotonicallyFromNoonToMidnightWithoutJumps()
	{
		var previous = 1f;
		for ( var step = 0; step <= 1000; step++ )
		{
			var value = EveningClock.Daylight( step / 2000.0 );
			Assert.IsTrue( value <= previous + 1e-6f, $"daylight never rises towards midnight ({step})" );
			Assert.IsTrue( previous - value < 0.02f, $"no jump at step {step}: {previous} to {value}" );
			previous = value;
		}
		Assert.AreEqual( EveningClock.Daylight( 0.3 ), EveningClock.Daylight( 1.3 ), "the phase wraps" );
		Assert.AreEqual( 1f, EveningClock.Daylight( double.NaN ), "a non-finite phase is noon" );
	}

	[TestMethod]
	public void FullDaylightIsTheOriginalsLookExactlyAndEveryUniformStaysAsItWas()
	{
		var day = EveningLook.At( 1f );
		Assert.AreEqual( EveningLook.Day, day );
		Assert.IsTrue( day.IsDay );
		Assert.AreEqual( NVector3.One, day.SunColour, "the sun is the white the level always had" );
		Assert.AreEqual( 0f, day.AmbientUniform, "0 means unchanged in the shaders" );
		Assert.AreEqual( 1f, day.AmbientLevel );
		Assert.AreEqual( NVector3.One, day.SkyTint );
		Assert.AreEqual( NVector3.One, day.FogTint );
		Assert.AreEqual( NVector3.One, day.SpriteTint );
		Assert.AreEqual( NVector3.One, day.WaterTint );
		Assert.AreEqual( 0f, day.LampLevel, "no lamp at day" );
		var colour = new NVector3( 0.4f, 0.6f, 0.9f );
		Assert.AreEqual( colour, day.TintFog( colour ) );
		Assert.AreEqual( EveningLook.Day, EveningLook.At( 2f ), "daylight is clamped" );
		Assert.AreEqual( EveningLook.Day, EveningLook.At( float.NaN ) );
		Assert.AreEqual( EveningLook.Day, EveningClock.LookFor( EveningPace.AlwaysDay, 0.5 ), "always day ignores the phase" );
		Assert.AreEqual( EveningLook.Day, EveningClock.LookFor( EveningPace.Minutes20, 0.0 ) );
	}

	[TestMethod]
	public void TheShadersKeepTheOriginalAmbientWhenTheUniformIsZero()
	{
		// the shaders' expression: 0.4 x (level > 0 ? level : 1); the day uniform is 0, so the product is the original's 0.4 bit for bit
		float Ambient( float uniform ) => 0.4f * ( uniform > 0f ? uniform : 1f );
		Assert.AreEqual( 0.4f, Ambient( EveningLook.Day.AmbientUniform ) );
		Assert.AreEqual( 0.4f, Ambient( 0f ) );
		Assert.AreEqual( 0.4f * EveningLook.NightAmbient, Ambient( EveningLook.At( 0f ).AmbientUniform ), 1e-7f );
		Assert.IsTrue( Ambient( EveningLook.At( 0.5f ).AmbientUniform ) < 0.4f, "the dusk is dimmer than the day" );
	}

	[TestMethod]
	public void TheTintedAmbientIsZeroAtDayBlueAtNightAndWarmAtDusk()
	{
		Assert.AreEqual( NVector3.Zero, EveningLook.Day.AmbientColourUniform, "all zero reads as unchanged in test.shader" );
		var night = EveningLook.At( 0f ).AmbientColourUniform;
		Assert.IsTrue( night.Z > night.Y && night.Y > night.X && MinOf( night ) > 0f && MaxOf( night ) < 0.6f, "a dim blue ambient" );
		var dusk = EveningLook.At( 0.5f ).AmbientColourUniform;
		Assert.IsTrue( dusk.X > dusk.Z && MinOf( dusk ) > 0f, "a warm ambient" );
		Assert.IsTrue( dusk.X > night.X, "dusk is brighter than night" );
		for ( var step = 0; step < 100; step++ )
			Assert.IsTrue( MinOf( EveningLook.At( step / 100f ).AmbientColourUniform ) > 0f, "a channel is never 0 outside full day (0 would read as unchanged)" );
	}

	[TestMethod]
	public void NightIsDarkBlueAndTheLampsAreFullDuskIsWarmAndTheLampsFade()
	{
		var night = EveningLook.At( 0f );
		Assert.IsFalse( night.IsDay );
		Assert.AreEqual( EveningLook.NightAmbient, night.AmbientLevel, 1e-6f );
		Assert.AreEqual( 1f, night.LampLevel );
		Assert.IsTrue( night.SunColour.Z > night.SunColour.X, "the moon is blue" );
		Assert.IsTrue( night.SkyTint.Z > night.SkyTint.X && MaxOf( night.SkyTint ) < 0.5f, "a dark blue sky" );
		Assert.IsTrue( MaxOf( night.FogTint ) < 0.3f );
		Assert.IsTrue( MaxOf( night.SpriteTint ) < 0.8f && MinOf( night.SpriteTint ) > 0.2f, "the guests are darkened but never black" );
		var dusk = EveningLook.At( 0.5f );
		Assert.IsTrue( dusk.SunColour.X > dusk.SunColour.Z, "a low sun is warm" );
		Assert.IsTrue( dusk.SkyTint.X > dusk.SkyTint.Z, "an orange sky" );
		Assert.AreEqual( 1f, EveningLook.Glow( 0.5f ), 1e-6f );
		Assert.AreEqual( 0f, EveningLook.Glow( 0f ) );
		Assert.AreEqual( 0f, EveningLook.Glow( 1f ) );
		// the lamps: off while it is light, growing as it gets dark, full at night
		Assert.AreEqual( 0f, EveningLook.LampLevelAt( 1f ) );
		Assert.AreEqual( 0f, EveningLook.LampLevelAt( 0.75f ) );
		Assert.AreEqual( 1f, EveningLook.LampLevelAt( 0.35f ) );
		Assert.AreEqual( 1f, EveningLook.LampLevelAt( 0f ) );
		var previous = 1f;
		for ( var step = 0; step <= 100; step++ )
		{
			var level = EveningLook.LampLevelAt( step / 100f );
			Assert.IsTrue( level <= previous + 1e-6f, "the lamps never brighten as the day returns" );
			previous = level;
		}
		Assert.IsTrue( EveningLook.At( 0.5f ).LampLevel is > 0.4f and < 1f, "the lamps are on at dusk" );
	}

	[TestMethod]
	public void TheFixedPacesGiveDayDuskAndNight()
	{
		Assert.AreEqual( 1f, EveningClock.FixedDaylight( EveningPace.AlwaysDay ) );
		Assert.AreEqual( 0.5f, EveningClock.FixedDaylight( EveningPace.AlwaysDusk ) );
		Assert.AreEqual( 0f, EveningClock.FixedDaylight( EveningPace.AlwaysNight ) );
		Assert.AreEqual( EveningLook.At( 0.5f ), EveningClock.LookFor( EveningPace.AlwaysDusk, 0.0 ) );
		Assert.AreEqual( EveningLook.At( 0f ), EveningClock.LookFor( EveningPace.AlwaysNight, 0.0 ) );
		Assert.IsTrue( EveningPace.AlwaysDusk.IsFixed() && EveningPace.AlwaysDay.IsFixed() && EveningPace.AlwaysNight.IsFixed() );
		Assert.IsFalse( EveningPace.Minutes20.IsFixed() );
		var clock = new EveningClock();
		clock.Advance( 10000, EveningPace.AlwaysNight );
		Assert.AreEqual( 0.0, clock.Phase, "a fixed look never advances the phase" );
	}

	// ---- the clock ----

	[TestMethod]
	public void TheClockAdvancesWithPlayTimeAtTheChosenPaceAndWraps()
	{
		CollectionAssert.AreEqual( new[] { 600.0, 1200.0, 2400.0, 0.0, 0.0, 0.0 }, EveningPaces.Cycle.Select( pace => pace.CycleSeconds() ).ToArray() );
		var clock = new EveningClock();
		clock.Advance( 300, EveningPace.Minutes20 );
		Assert.AreEqual( 0.25, clock.Phase, 1e-12 );
		clock.Advance( 300, EveningPace.Minutes10 );
		Assert.AreEqual( 0.75, clock.Phase, 1e-12, "ten minutes is twice as fast" );
		clock.Advance( 600, EveningPace.Minutes40 );
		Assert.AreEqual( 0.0, clock.Phase, 1e-12, "wrapped to noon again" );
		clock.Advance( 1200.0 * 3 + 120.0, EveningPace.Minutes20 );
		Assert.AreEqual( 0.1, clock.Phase, 1e-9, "whole cycles drop out" );
		var before = clock.Phase;
		clock.Advance( -5, EveningPace.Minutes20 );
		clock.Advance( double.NaN, EveningPace.Minutes20 );
		clock.Advance( double.PositiveInfinity, EveningPace.Minutes20 );
		Assert.AreEqual( before, clock.Phase, "a negative or non-finite step changes nothing" );
		clock.SetPhase( -0.25 );
		Assert.AreEqual( 0.75, clock.Phase, 1e-12 );
		clock.SetPhase( double.NaN );
		Assert.AreEqual( 0.0, clock.Phase );
	}

	[TestMethod]
	public void TheLevelCycleFollowsPlayTimeStandsStillWhenPausedAndIgnoresTheParkSpeed()
	{
		var level = BareLevel();
		Assert.IsTrue( level.Look.IsDay, "a level built without its constructor draws at day" );
		// off: nothing is created and the look stays day
		level.AdvanceEvening( enabled: false, EveningPace.Minutes20, 0.2, playing: true );
		Assert.IsNull( level.EveningDayClock );
		Assert.IsTrue( level.Look.IsDay );
		// on: 0.2 s frames advance the clock; a paused park does not (whatever the speed setting is: the step does not read it)
		level.AdvanceEvening( enabled: true, EveningPace.Minutes20, 0.2, playing: true );
		var phase = level.EveningDayClock!.Phase;
		Assert.AreEqual( 0.2 / 1200.0, phase, 1e-12 );
		level.AdvanceEvening( enabled: true, EveningPace.Minutes20, 0.2, playing: false );
		Assert.AreEqual( phase, level.EveningDayClock.Phase, "paused: the cycle stands still" );
		// a long hitch counts as at most a quarter second of play
		level.AdvanceEvening( enabled: true, EveningPace.Minutes20, 30.0, playing: true );
		Assert.AreEqual( phase + Level.EveningMaximumFrameSeconds / 1200.0, level.EveningDayClock.Phase, 1e-12 );
		// well into the night
		level.EveningDayClock.SetPhase( 0.5 );
		level.AdvanceEvening( enabled: true, EveningPace.Minutes20, 0.0, playing: true );
		Assert.IsFalse( level.Look.IsDay );
		Assert.AreEqual( EveningLook.At( 0f ).AmbientLevel, level.Look.AmbientLevel, 1e-6f );
		// turning the extension off puts the park back to day with the sun white again
		level.AdvanceEvening( enabled: false, EveningPace.Minutes20, 0.0, playing: true );
		Assert.IsTrue( level.Look.IsDay );
		Assert.IsNull( level.EveningDayClock );
	}

	// ---- the option ----

	[TestMethod]
	public void TheDayCycleOptionDefaultsToTwentyMinutesRoundTripsAndNeverBreaksTheFile()
	{
		Assert.AreEqual( EveningPace.Minutes20, new GameOptions().Evening );
		Assert.AreEqual( EveningPace.Minutes20, EveningPaces.Default );
		var path = Path.Combine( Path.GetTempPath(), $"opentpw-options-evening-{Guid.NewGuid():N}.json" );
		try
		{
			foreach ( var pace in Enum.GetValues<EveningPace>() )
			{
				new GameOptions { Evening = pace, MusicVolume = 33 }.Save( path );
				var loaded = GameOptions.Load( path );
				Assert.AreEqual( pace, loaded.Evening );
				Assert.AreEqual( 33, loaded.MusicVolume );
			}
			new GameOptions { Evening = EveningPace.AlwaysNight }.Save( path );
			StringAssert.Contains( File.ReadAllText( path ), "\"Evening\": \"AlwaysNight\"", "stored by name" );
			// a file from before the option, an unknown name, a number and a wrong type: the default, and the rest of the file is kept
			foreach ( var value in new[] { "", ", \"Evening\": \"Midnight\"", ", \"Evening\": 99", ", \"Evening\": 3", ", \"Evening\": true", ", \"Evening\": null", ", \"Evening\": { \"a\": [1, 2] }" } )
			{
				File.WriteAllText( path, "{ \"VolumeFormat\": 2, \"MusicVolume\": 21" + value + " }" );
				var loaded = GameOptions.Load( path );
				Assert.AreEqual( EveningPace.Minutes20, loaded.Evening, $"{value} reads as the default" );
				Assert.AreEqual( 21, loaded.MusicVolume, $"{value} keeps the other options" );
			}
			var options = new GameOptions { Evening = (EveningPace)77 }.Normalized();
			Assert.AreEqual( EveningPace.Minutes20, options.Evening, "an out-of-range value is validated" );
			Assert.AreEqual( EveningPace.AlwaysDusk, EveningPaces.Validated( EveningPace.AlwaysDusk ) );
			var copy = new GameOptions();
			copy.CopyFrom( new GameOptions { Evening = EveningPace.Minutes40 } );
			Assert.AreEqual( EveningPace.Minutes40, copy.Evening, "Cancel restores it" );
			Assert.AreEqual( EveningPace.Minutes40, new GameOptions { Evening = EveningPace.Minutes40 }.Clone().Evening );
		}
		finally
		{
			File.Delete( path );
		}
	}

	[TestMethod]
	public void TheOpenTpwPageHasADayCycleRowThatCyclesAndOkSavesAndCancelRestores()
	{
		var stack = new UiScreenStack();
		var options = new GameOptions();
		var display = new StubDisplaySettings( DisplaySettings.Default with { Width = 1280, Height = 720 } );
		var saved = 0;
		UiScreen Open()
		{
			var screen = OptionsScreen.Create( stack, OriginalUiTests.FakeStrings(), OriginalUiTests.OptionsServicesFor( options, display, () => saved++ ), () => { } );
			stack.Push( screen );
			return screen;
		}
		var main = Open();
		main.Find( "openTpw" )!.Activate();
		var page = stack.Top!;
		var label = (UiLabel)page.Find( "eveningLabel" )!;
		StringAssert.Contains( label.Text(), Text( OpenTpwText.EveningOption ) );
		StringAssert.Contains( label.Text(), "20" );
		var seen = new List<EveningPace> { options.Evening };
		for ( var step = 0; step < EveningPaces.Cycle.Count; step++ )
		{
			((UiButton)page.Find( "evening" )!).Activate();
			seen.Add( options.Evening );
		}
		CollectionAssert.AreEqual( new[] { EveningPace.Minutes20, EveningPace.Minutes40, EveningPace.AlwaysDay, EveningPace.AlwaysDusk, EveningPace.AlwaysNight, EveningPace.Minutes10, EveningPace.Minutes20 }, seen, "the row cycles through every value in order and wraps" );
		((UiButton)page.Find( "evening" )!).Adjust( -1 );
		Assert.AreEqual( EveningPace.Minutes10, options.Evening, "the wheel and the left key go back" );
		stack.Update( OriginalUiTests.FakeContext(), UiInput.Key( UiKeys.Back ) );
		main.Find( "ok" )!.Activate();
		Assert.AreEqual( (1, EveningPace.Minutes10), (saved, options.Evening) );
		main = Open();
		main.Find( "openTpw" )!.Activate();
		((UiButton)stack.Top!.Find( "evening" )!).Activate();
		Assert.AreEqual( EveningPace.Minutes20, options.Evening );
		stack.Update( OriginalUiTests.FakeContext(), UiInput.Key( UiKeys.Back ) );
		main.Find( "cancel" )!.Activate();
		Assert.AreEqual( EveningPace.Minutes10, options.Evening, "Cancel restores the saved pace" );
	}

	private static string Text( OpenTpwText key ) => SupplementaryStrings.Get( key, "English" );

	[TestMethod]
	public void TheExtensionDescriptionIsShortEnoughForTheDetailsLabelInEveryLanguage()
	{
		// the mods screen's details label is a small-font wrapped label of 1934 authored units that also holds the name, version and flags: about 300 characters of description fit
		foreach ( var language in SupplementaryStrings.SupportedLanguages )
			Assert.IsTrue( SupplementaryStrings.Get( OpenTpwText.ExtensionEveningDescription, language ).Length <= 300, $"{language}: {SupplementaryStrings.Get( OpenTpwText.ExtensionEveningDescription, language ).Length} characters" );
	}

	[TestMethod]
	public void TheRowLabelsNameTheMinutesAndTheFixedLooksInEveryLanguage()
	{
		foreach ( var language in SupplementaryStrings.SupportedLanguages )
		{
			var minutes = SupplementaryStrings.Get( OpenTpwText.EveningMinutes, language );
			StringAssert.Contains( minutes, "{0}", $"{language}: the minutes text takes the number" );
			var words = new[] { OpenTpwText.EveningOption, OpenTpwText.EveningAlwaysDay, OpenTpwText.EveningAlwaysDusk, OpenTpwText.EveningAlwaysNight }.Select( key => SupplementaryStrings.Get( key, language ) ).ToArray();
			Assert.AreEqual( 4, words.Distinct().Count(), $"{language}: the row's words differ from each other" );
			Assert.IsTrue( words.All( word => !string.IsNullOrWhiteSpace( word ) ) );
		}
	}

	// ---- the extension ----

	[TestMethod]
	public void TheExtensionIsRegisteredOnByDefaultPresentationOnlyAndSelectable()
	{
		var evening = OfficialExtensions.Evening;
		Assert.AreEqual( "opentpw.evening", evening.Id );
		Assert.IsTrue( evening.DefaultEnabled, "on by default: the user decided on 2026-10-18 after the tuning captures" );
		Assert.IsTrue( evening.AffectsSimulation, "its torches are objects with a price (user-approved 2026-10-19, part of the evening extension)" );
		Assert.IsTrue( ParkResumeMods.UsedOnlyExtensionIds.Contains( evening.Id ), "but a save records it only while the park holds a torch" );
		Assert.IsTrue( evening.PlayerSelectable );
		Assert.AreEqual( new ModVersion( 1, 1, 0 ), evening.Version, "1.1.0: the first version a save can record (the torches)" );
		Assert.AreSame( evening, OfficialExtensions.Find( "opentpw.evening" ) );
		CollectionAssert.Contains( OfficialExtensions.All.ToArray(), evening );
		Assert.IsTrue( ModSet.Defaults( OfficialExtensions.All ).IsEnabled( evening.Id ) );
		Assert.IsTrue( FeatureGates.Evening, "the process default" );
		using ( OpenTpwExtensions.Use( Set( true ) ) )
			Assert.IsTrue( FeatureGates.Evening );
		using ( OpenTpwExtensions.Use( Set( false ) ) )
			Assert.IsFalse( FeatureGates.Evening );
	}

	[TestMethod]
	public void TheExtensionIsRecordedInAParkSaveOnlyWhileTheParkHoldsATorch()
	{
		Assert.IsNull( ParkResumeMods.Capture( Set( true ) ), "a park without a torch records no mod for it, as before the torches" );
		Assert.IsNull( ParkResumeMods.Capture( Set( true ), ParkResumeMods.InUse( null ) ) );
		Assert.IsNull( ParkResumeMods.Capture( Set( false ) ) );
		var record = ParkResumeMods.Capture( Set( true ), ParkResumeMods.InUse( null, evening: true ) )!.Single();
		Assert.AreEqual( (OfficialExtensions.Evening.Id, "1.1.0", ModSet.NoFilesSha256), (record.Id, record.Version, record.ContentSha256) );
		Assert.IsNull( ParkResumeMods.Capture( Set( false ), ParkResumeMods.InUse( null, evening: true ) ), "an extension that is off records nothing" );
		ParkResumeMods.Require( null, Set( true ) );
		ParkResumeMods.Require( null, Set( false ) );
		ParkResumeMods.Require( new[] { record }, Set( true ) );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( new[] { record }, Set( false ) ) ).Message, "needs opentpw.evening 1.1.0" );
	}

	[TestMethod]
	public void TheModsScreenListsTheExtensionWithItsNameAndDescriptionInEveryLanguage()
	{
		foreach ( var key in new[] { OpenTpwText.ExtensionEveningName, OpenTpwText.ExtensionEveningDescription, OpenTpwText.EveningOption, OpenTpwText.EveningHelp } )
		{
			var texts = SupplementaryStrings.SupportedLanguages.Select( language => SupplementaryStrings.Get( key, language ) ).ToList();
			Assert.AreEqual( SupplementaryStrings.SupportedLanguages.Count, texts.Count, $"{key}: every interface language" );
			Assert.IsTrue( texts.All( text => !string.IsNullOrWhiteSpace( text ) ), $"{key} has a text in every language" );
			Assert.AreEqual( texts.Count, texts.Distinct().Count(), $"{key}: every language has its own text" );
		}
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), new ModsServices
		{
			Discovered = () => Array.Empty<DiscoveredMod>(),
			SettingsPath = Path.Combine( Path.GetTempPath(), $"opentpw-mods-evening-{Guid.NewGuid():N}.json" ),
			Active = () => ModSet.Defaults( OfficialExtensions.All ),
			OpenTpwVersion = null,
			Edition = () => GameEdition.ThemeParkWorld,
			Inspect = ( path, _ ) => System.Threading.Tasks.Task.FromResult( new InstallationDiscoveryResult( new[] { GameInstallation.Inspect( path ) }, false, null ) ),
		} );
		stack.Push( screen );
		var rows = ((UiScrollList)screen.Find( "extensions" )!).Rows();
		CollectionAssert.Contains( rows.ToArray(), Text( OpenTpwText.ExtensionEveningName ) + "  1.1.0" );
		stack.Clear();
	}

	// ---- the world is untouched ----

	[TestMethod]
	public void TheExtensionChangesNoWorldStateHashOfAnEconomyRun()
	{
		// the economy beside a bare level's presentation step: this proves only that the cycle shares no state with the economy; the real-level proof is the --perf-hash-tick run (docs/EVENING.md, "Determinism")
		ulong Hash( bool extensionOn )
		{
			using var scope = OpenTpwExtensions.Use( Set( extensionOn ) );
			var economy = EconomyTestData.Park();
			var level = BareLevel();
			for ( var turn = 1; turn <= 60; turn++ )
			{
				EconomyTestData.AdvanceToTurn( economy, turn );
				// the presentation side runs beside the simulation, as Level.Update does: the cycle advances with the frame time and never reads the park
				level.AdvanceEvening( FeatureGates.Evening, EveningPace.Minutes10, 0.1, playing: true );
			}
			Assert.AreEqual( extensionOn, level.EveningDayClock != null, "the cycle ran only with the extension on" );
			return WorldStateHash.Compute( new WorldStateSources { Economy = economy } );
		}
		Assert.AreEqual( Hash( extensionOn: false ), Hash( extensionOn: true ), "the same park hashes the same with and without the extension" );
	}

	// ---- the uniform and the shaders ----

	[TestMethod]
	public void TheAmbientLevelRidesInThePaddingAfterTheSunPositionAndADayDrawIsByteForByteAsBefore()
	{
		Assert.AreEqual( 204, (int)Marshal.OffsetOf<ObjectUniformBuffer>( nameof( ObjectUniformBuffer._padding0 ) ), "a float after a vec3 at 192 packs at 204 in std140" );
		Assert.AreEqual( 272, Unsafe.SizeOf<ObjectUniformBuffer>(), "the block did not grow" );
		Assert.AreEqual( 244, (int)Marshal.OffsetOf<ObjectUniformBuffer>( nameof( ObjectUniformBuffer._padding3 ) ), "test.shader's tinted ambient: three floats after the view distance at 240" );
		var plain = new ObjectUniformBuffer { g_mModel = Matrix4x4.CreateTranslation( 1, 2, 3 ), g_vLightPos = new NVector3( 4, 5, 6 ), g_vLightColor = NVector3.One, g_flTime = 7 }.WithFog();
		var day = plain;
		day.AmbientLevel = EveningLook.Day.AmbientUniform;
		Assert.AreEqual( 0f, day.AmbientLevel );
		CollectionAssert.AreEqual( MemoryMarshal.AsBytes( new ReadOnlySpan<ObjectUniformBuffer>( in plain ) ).ToArray(), MemoryMarshal.AsBytes( new ReadOnlySpan<ObjectUniformBuffer>( in day ) ).ToArray() );
		var night = plain;
		night.AmbientLevel = EveningLook.At( 0f ).AmbientUniform;
		Assert.AreEqual( EveningLook.NightAmbient, night.AmbientLevel, 1e-6f );
		// the sprite tint rides in the light colour the sprite shader never read: untouched at day
		var sprite = new ObjectUniformBuffer { g_flTime = 1 }.WithFog();
		var spriteDay = sprite.WithSpriteTint( EveningLook.Day );
		CollectionAssert.AreEqual( MemoryMarshal.AsBytes( new ReadOnlySpan<ObjectUniformBuffer>( in sprite ) ).ToArray(), MemoryMarshal.AsBytes( new ReadOnlySpan<ObjectUniformBuffer>( in spriteDay ) ).ToArray() );
		Assert.AreEqual( EveningLook.At( 0f ).SpriteTint, sprite.WithSpriteTint( EveningLook.At( 0f ) ).g_vLightColor );
	}

	private static string ShaderText( string name ) => File.ReadAllText( Path.Combine( RepositoryRoot(), "content", "shaders", name ) );

	[TestMethod]
	public void TheLitShadersDeclareTheAmbientLevelAtByte204AndKeepTheDayExpression()
	{
		foreach ( var name in new[] { "test.shader", "pool-deck.shader", "pool-basin.shader" } )
		{
			var text = ShaderText( name );
			var declared = System.Text.RegularExpressions.Regex.Matches( text, @"vec3 g_vLightPos;\s*float g_flAmbientLevel;[^\n]*\n\s*vec3 g_vLightColor;" ).Count;
			Assert.AreEqual( name == "test.shader" ? 2 : 1, declared, $"{name}: the float sits between the sun position and the sun colour in every block" );
			StringAssert.Contains( text, "g_oUbo.g_flAmbientLevel > 0.0 ? g_oUbo.g_flAmbientLevel : 1.0", $"{name}: 0 keeps the original ambient" );
		}
		StringAssert.Contains( ShaderText( "test.shader" ), "vec3 vAmbient = vec3(0.4) * (" );
		Assert.AreEqual( 2, System.Text.RegularExpressions.Regex.Matches( ShaderText( "test.shader" ), @"float g_flViewDistanceScale;\s*float g_flAmbientR;[^\n]*\n\s*float g_flAmbientG;\s*float g_flAmbientB;\s*vec3 g_vFogColor;" ).Count, "the tinted ambient fills the padding before the fog colour in both blocks" );
		// the water keeps the slot for its wave strength and does not read an ambient level
		Assert.IsFalse( ShaderText( "pool-water.shader" ).Contains( "g_flAmbientLevel" ) );
		// the tints ride in the light colour of the sprite and particle shaders, which no draw ever filled
		foreach ( var name in new[] { "sprite.shader", "particle.shader" } )
			StringAssert.Contains( ShaderText( name ), "g_oUbo.g_vLightColor.x + g_oUbo.g_vLightColor.y + g_oUbo.g_vLightColor.z > 0.0" );
	}

	[TestMethod]
	public void DuskIsAClearlyWarmerAndDarkerEveningThanTheDay()
	{
		var dusk = EveningLook.At( 0.5f );
		// the low sun is strongly orange: red well above green well above blue, so green ground turns olive and paths and bricks glow
		Assert.IsTrue( dusk.SunColour.X > 1.5f * dusk.SunColour.Y && dusk.SunColour.Y > 1.5f * dusk.SunColour.Z, "an orange low sun" );
		Assert.IsTrue( dusk.SunColour.X < 0.75f, "the dusk sun is dimmer than the day's white" );
		var ambient = dusk.AmbientColourUniform;
		Assert.IsTrue( ambient.X > ambient.Y && ambient.Y > ambient.Z, "a warm tinted ambient" );
		Assert.IsTrue( ambient.X < 0.6f && ambient.Z < 0.4f, "well below the day's 1 in every channel" );
		// the middle of the dusk is darker than its first half and brighter than night
		Assert.IsTrue( EveningLook.At( 0.75f ).SunColour.X > dusk.SunColour.X && dusk.SunColour.X > EveningLook.At( 0f ).SunColour.X * 0.9f );
		// night is exactly as approved: the moon and 0.35 of the ambient
		var night = EveningLook.At( 0f );
		Assert.AreEqual( new NVector3( 0.08f, 0.14f, 0.40f ), night.SunColour );
		Assert.AreEqual( 0.35f, night.AmbientLevel, 1e-6f );
	}

	[TestMethod]
	public void TheLitShaderHasASoftShoulderForLampLitWhiteSurfacesOnlyOutsideFullDay()
	{
		var text = ShaderText( "test.shader" );
		StringAssert.Contains( text, "peak > 0.4 ?" );
		// guarded by the tinted ambient, which is all zero at full day and with the extension off
		var peak = text.IndexOf( "peak > 0.4 ?", StringComparison.Ordinal );
		StringAssert.Contains( text.Substring( Math.Max( 0, peak - 300 ), 300 ), "vAmbientTint.x + vAmbientTint.y + vAmbientTint.z > 0.0 )", "the shoulder sits behind the dusk and night check" );
	}

	/// <summary>The shoulder of <c>test.shader</c> in C#: weighted by the ambient that has fallen away, so the first dusk frame looks like the last day frame.</summary>
	private static NVector3 Shouldered( NVector3 colour, EveningLook look )
	{
		if ( look.AmbientColourUniform == NVector3.Zero )
			return colour;
		var peak = MaxOf( colour );
		var shouldered = colour * ( peak > 0.4f ? ( 0.4f + ( peak - 0.4f ) * 0.25f ) / peak : 1f );
		return NVector3.Lerp( colour, shouldered, Math.Clamp( ( 1f - look.AmbientUniform ) / 0.65f, 0f, 1f ) );
	}

	[TestMethod]
	public void TheShoulderFadesInWithTheDuskSoALitSurfaceNeverJumps()
	{
		var text = ShaderText( "test.shader" );
		StringAssert.Contains( text, "clamp( ( 1.0 - g_oUbo.g_flAmbientLevel ) / 0.65, 0.0, 1.0 )", "the shader weights the shoulder like this test" );
		foreach ( var brightness in new[] { 0.5f, 0.8f, 1.0f } )
		{
			var lit = new NVector3( brightness );
			var day = Shouldered( lit, EveningLook.At( 1f ) );
			var firstDusk = Shouldered( lit, EveningLook.At( 0.99f ) );
			Assert.AreEqual( brightness, day.X, 1e-6f, "full day is untouched" );
			Assert.IsTrue( Math.Abs( firstDusk.X - day.X ) < 0.02f, $"{brightness}: {day.X} at day, {firstDusk.X} on the first dusk frame" );
			// whole at full night: the ambient left is NightAmbient there
			var night = Shouldered( lit, EveningLook.At( 0f ) );
			var full = brightness > 0.4f ? 0.4f + ( brightness - 0.4f ) * 0.25f : brightness;
			Assert.AreEqual( full, night.X, 1e-4f, $"{brightness}: the whole shoulder at night" );
			// and never a step anywhere along the way
			var previous = day.X;
			for ( var step = 99; step >= 0; step-- )
			{
				var value = Shouldered( lit, EveningLook.At( step / 100f ) ).X;
				Assert.IsTrue( Math.Abs( value - previous ) < 0.03f, $"{brightness}: {previous} to {value} at daylight {step / 100f}" );
				previous = value;
			}
		}
	}

	[TestMethod]
	public void TheSplashAndSmokeParticlesTakeTheSpriteTint()
	{
		var night = EveningLook.At( 0f );
		var tinted = new ObjectUniformBuffer().WithSpriteTint( night );
		Assert.AreEqual( night.SpriteTint, tinted.g_vLightColor );
		var day = new ObjectUniformBuffer().WithSpriteTint( EveningLook.Day );
		Assert.AreEqual( NVector3.Zero, day.g_vLightColor, "full day leaves the block as it was" );
		var root = SourceRoot();
		StringAssert.Contains( File.ReadAllText( Path.Combine( root, "source", "OpenTPW", "World", "Pools", "PoolSpray.cs" ) ), "WithSpriteTint" );
		StringAssert.Contains( File.ReadAllText( Path.Combine( root, "source", "OpenTPW", "World", "Particles", "ParticleRenderer.cs" ) ), "tinted: kind == 0" );
	}

	[TestMethod]
	public void TheWebShadersAreRegeneratedForTheAmbientLevel()
	{
		foreach ( var name in new[] { "test", "pool-deck", "pool-basin", "sprite", "particle" } )
		{
			var json = File.ReadAllText( Path.Combine( RepositoryRoot(), "content", "shaders", "web", name + ".json" ) );
			if ( name is "test" or "pool-deck" or "pool-basin" )
				StringAssert.Contains( json, "g_flAmbientLevel", $"{name}.json carries the new uniform" );
		}
	}

	// ---- the lamp table ----

	private static EveningLampTable Shipped() => EveningLampTable.Parse( File.ReadAllText( Path.Combine( RepositoryRoot(), EveningLampTable.RelativePath ) ) );

	[TestMethod]
	public void TheShippedLampTableLoadsAndEveryRowIsWellFormed()
	{
		var table = Shipped();
		Assert.IsTrue( table.Rules.Count >= 15, "the table lights the pools extension, the original lamps and a few rides" );
		Assert.IsTrue( table.Rules.Any( rule => rule.PoolsExtra == 5 ) && table.Rules.Any( rule => rule.PoolsExtra == 6 ), "the pool lamp and the underwater light" );
		Assert.IsTrue( table.Rules.Any( rule => rule.Category == ObjectCategory.Ride ), "a few ride lights" );
		Assert.IsTrue( table.Rules.Any( rule => rule.Archive == "lights" ), "the original lamp posts" );
		foreach ( var rule in table.Rules )
		{
			Assert.IsTrue( rule.Lights.Count is >= 1 and <= EveningLampTable.MaximumLightsPerRule );
			foreach ( var light in rule.Lights )
			{
				Assert.IsTrue( light.RadiusModelUnits is >= PointLightMath.MinimumRadiusModelUnits and <= EveningLampTable.MaximumRadiusModelUnits );
				Assert.IsTrue( light.Colour.X >= 0 && light.Colour.Y >= 0 && light.Colour.Z >= 0 && light.Colour != NVector3.Zero );
			}
		}
		Assert.IsTrue( table.Rules.Sum( rule => rule.Lights.Count ) <= 64, "modest: the whole table is a few dozen lights" );
	}

	[TestMethod]
	public void ABadLampTableIsRefusedWithAReasonAndTheShippedLoaderFallsBackToNoLamps()
	{
		const string light = "{ \"node\": \"n\", \"colour\": [1, 1, 1], \"radius\": 40 }";
		string Table( string match, string lights ) => "{ \"schema\": 1, \"lamps\": [ { \"match\": " + match + ", \"lights\": [" + lights + "] } ] }";
		var good = EveningLampTable.Parse( Table( "{ \"theme\": \"jungle\", \"category\": \"feature\", \"archive\": \"lights\" }", light ) );
		Assert.AreEqual( 1, good.Rules.Count );
		Assert.AreEqual( ObjectCategory.Feature, good.Rules[0].Category );
		var bad = new Dictionary<string, string>
		{
			["schema"] = "{ \"schema\": 2, \"lamps\": [] }",
			["lamps"] = "{ \"schema\": 1 }",
			["empty match"] = Table( "{}", light ),
			["theme"] = Table( "{ \"theme\": \"moon\" }", light ),
			["category"] = Table( "{ \"category\": \"upgrade\" }", light ),
			["poolsExtra"] = Table( "{ \"poolsExtra\": 9 }", light ),
			["match key"] = Table( "{ \"colour\": 1 }", light ),
			["no lights"] = Table( "{ \"archive\": \"a\" }", "" ),
			["no node"] = Table( "{ \"archive\": \"a\" }", "{ \"colour\": [1, 1, 1], \"radius\": 40 }" ),
			["negative colour"] = Table( "{ \"archive\": \"a\" }", "{ \"node\": \"n\", \"colour\": [-1, 1, 1], \"radius\": 40 }" ),
			["black"] = Table( "{ \"archive\": \"a\" }", "{ \"node\": \"n\", \"colour\": [0, 0, 0], \"radius\": 40 }" ),
			["big colour"] = Table( "{ \"archive\": \"a\" }", "{ \"node\": \"n\", \"colour\": [9, 1, 1], \"radius\": 40 }" ),
			["radius 0"] = Table( "{ \"archive\": \"a\" }", "{ \"node\": \"n\", \"colour\": [1, 1, 1], \"radius\": 0 }" ),
			["radius huge"] = Table( "{ \"archive\": \"a\" }", "{ \"node\": \"n\", \"colour\": [1, 1, 1], \"radius\": 5000 }" ),
			["anchor"] = Table( "{ \"archive\": \"a\" }", "{ \"node\": \"n\", \"anchor\": \"side\", \"colour\": [1, 1, 1], \"radius\": 40 }" ),
			["light key"] = Table( "{ \"archive\": \"a\" }", "{ \"node\": \"n\", \"colour\": [1, 1, 1], \"radius\": 40, \"flicker\": 1 }" ),
			["offset"] = Table( "{ \"archive\": \"a\" }", "{ \"node\": \"n\", \"offset\": [1, 2], \"colour\": [1, 1, 1], \"radius\": 40 }" ),
			["too many lights"] = Table( "{ \"archive\": \"a\" }", string.Join( ",", Enumerable.Repeat( light, EveningLampTable.MaximumLightsPerRule + 1 ) ) )
		};
		foreach ( var (name, text) in bad )
			Assert.ThrowsException<InvalidDataException>( () => EveningLampTable.Parse( text ), name );
		try
		{
			EveningLampTable.Parse( "{ nonsense" );
			Assert.Fail( "broken JSON is refused" );
		}
		catch ( JsonException )
		{
			// the shipped loader catches JsonException too and then has no lamps
		}
	}

	/// <summary>A model with a post and a lamp head that stands on it: the head's mesh box is 2 wide (X), 1 tall (MD2 Y) and 2 deep (MD2 Z) at (1..3, 5..6, 0..2), the post has no light.</summary>
	private static ModelFile LampModel()
	{
		var builder = new ModelBuilder { SourceName = "lamppost" };
		var post = builder.AddMesh( "post" );
		var head = builder.AddMesh( "head", post );
		var material = head.Mesh!.AddMaterial( "quad_tex", wrap: true );
		var normal = new NVector3( 0, 1, 0 );
		var a = material.AddVertex( new NVector3( 1, 5, 0 ), normal, new Vector2( 0, 0 ) );
		var b = material.AddVertex( new NVector3( 3, 5, 0 ), normal, new Vector2( 1, 0 ) );
		var c = material.AddVertex( new NVector3( 3, 6, 2 ), normal, new Vector2( 1, 1 ) );
		var d = material.AddVertex( new NVector3( 1, 6, 2 ), normal, new Vector2( 0, 1 ) );
		material.AddQuad( a, b, c, d );
		var postMaterial = post.Mesh!.AddMaterial( "quad_tex", wrap: true );
		var p0 = postMaterial.AddVertex( new NVector3( 0, 0, 0 ), normal, new Vector2( 0, 0 ) );
		var p1 = postMaterial.AddVertex( new NVector3( 1, 0, 0 ), normal, new Vector2( 1, 0 ) );
		var p2 = postMaterial.AddVertex( new NVector3( 1, 5, 0 ), normal, new Vector2( 1, 1 ) );
		var p3 = postMaterial.AddVertex( new NVector3( 0, 5, 0 ), normal, new Vector2( 0, 1 ) );
		postMaterial.AddQuad( p0, p1, p2, p3 );
		builder.AddDummy( "flame", head, Matrix4x4.CreateTranslation( 0, 1, 0 ) );
		return builder.Build();
	}

	private static EveningLampRule Rule( params EveningLampLight[] lights ) => new( null, null, "lamppost", null, lights );

	[TestMethod]
	public void ALampResolvesToItsNodesMeshBoxInTheEnginesFrameWithItsOffset()
	{
		var model = LampModel();
		var head = model.Nodes.Single( node => node.Name == "head" );
		var colour = new NVector3( 1f, 0.8f, 0.5f );
		var lamps = EveningLampResolver.Resolve( model, Rule(
			new EveningLampLight( "head", EveningAnchor.Centre, NVector3.Zero, colour, 40 ),
			new EveningLampLight( "HEAD", EveningAnchor.Top, new NVector3( 0, 0, -0.5f ), colour, 30 ),
			new EveningLampLight( "head", EveningAnchor.Pivot, new NVector3( 7, 8, 9 ), colour, 20 ),
			new EveningLampLight( "flame", EveningAnchor.Top, NVector3.Zero, colour, 10 ) ) );
		Assert.AreEqual( 4, lamps.Count );
		Assert.IsTrue( lamps.Take( 3 ).All( lamp => lamp.NodeIndex == head.Index ), "the node is found by name, in any case" );
		// the box is 1..3 in X, 5..6 in MD2 Y (up) and 0..2 in MD2 Z (depth): the engine turns Y and Z over (ground XY, up Z)
		Assert.AreEqual( new NVector3( 2f, 1f, 5.5f ), lamps[0].Local, "the middle of the box, up on engine Z" );
		Assert.AreEqual( new NVector3( 2f, 1f, 6f - 0.5f ), lamps[1].Local, "the middle of the top, then the offset" );
		Assert.AreEqual( new NVector3( 7, 8, 9 ), lamps[2].Local, "the pivot is the node's origin plus the offset" );
		Assert.AreEqual( NVector3.Zero, lamps[3].Local, "a node without a mesh stands at its origin whatever the anchor" );
		Assert.AreEqual( (colour, 40f), (lamps[0].Colour, lamps[0].RadiusModelUnits) );
	}

	[TestMethod]
	public void AMissingNodeLeavesOutThatLightAndNamesIt()
	{
		var missing = new List<string>();
		var lamps = EveningLampResolver.Resolve( LampModel(), Rule(
			new EveningLampLight( "head", EveningAnchor.Centre, NVector3.Zero, NVector3.One, 40 ),
			new EveningLampLight( "bulb", EveningAnchor.Centre, NVector3.Zero, NVector3.One, 40 ) ), missing );
		Assert.AreEqual( 1, lamps.Count );
		CollectionAssert.AreEqual( new[] { "bulb" }, missing );
	}

	[TestMethod]
	public void ThePoolsExtensionsLampAndUnderwaterLightResolveOnTheirShippedModels()
	{
		var table = Shipped();
		var root = Path.Combine( RepositoryRoot(), "content", "pools", "objects" );
		foreach ( var (extra, name) in new[] { (5, "lamp"), (6, "light") } )
		{
			var rule = table.Rules.Single( candidate => candidate.PoolsExtra == extra );
			foreach ( var theme in ObjectCatalog.Themes )
			{
				var model = new ModelFile( File.OpenRead( Path.Combine( root, theme, name, name + ".MD2" ) ) );
				var missing = new List<string>();
				var lamps = EveningLampResolver.Resolve( model, rule, missing );
				Assert.AreEqual( 0, missing.Count, $"{theme}/{name}: every node of the row exists" );
				Assert.AreEqual( rule.Lights.Count, lamps.Count );
				foreach ( var lamp in lamps )
				{
					// inside the object's 10 x 10 cell and not under the ground, in model units: where the light stands is a place the object occupies
					// (the underwater light stands a little below the object's ground level on purpose: it is in the water, so it lights the basin and the water's edge and little of the dry ground)
					Assert.IsTrue( lamp.Local.X is >= 0 and <= 10 && lamp.Local.Y is >= 0 && lamp.Local.Y <= ( extra == 6 ? 25f : 10f ) && lamp.Local.Z >= ( extra == 6 ? -8f : 0f ) && lamp.Local.Z <= 14, $"{theme}/{name}: {lamp.Local}" );
				}
			}
		}
	}

	[TestMethod]
	public void EveryRowOfTheShippedTableMatchesAnOriginalObjectAndEveryNodeExists()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the lamp table's rows." );
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			var table = Shipped();
			var catalogs = ObjectCatalog.Themes.Select( theme => ObjectCatalog.Load( theme ) ).ToList();
			var checkedLights = 0;
			// the pools extension's rows and the evening extension's own torches (EveningTorchTests) are not original objects
			foreach ( var rule in table.Rules.Where( candidate => candidate.PoolsExtra == null && candidate.Mod == null ) )
			{
				var matches = catalogs.SelectMany( catalog => catalog.Entries ).Where( rule.Matches ).ToList();
				Assert.IsTrue( matches.Count > 0, $"a row for {rule.Theme}/{rule.Category}/{rule.Archive} matches an original object" );
				foreach ( var entry in matches )
				{
					var missing = new List<string>();
					var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
					var lamps = EveningLampResolver.Resolve( model, rule, missing );
					Assert.AreEqual( 0, missing.Count, $"{entry.Theme}/{entry.ArchiveName}: missing nodes {string.Join( ", ", missing )}" );
					foreach ( var lamp in lamps )
					{
						Assert.IsTrue( lamp.Local.X is > -100 and < 100 && lamp.Local.Y is > -100 and < 100 && lamp.Local.Z is > -100 and < 100, $"{entry.ArchiveName}: {lamp.Local}" );
						checkedLights++;
					}
				}
			}
			Console.WriteLine( $"evening lamp table: {table.Rules.Count} rows, {checkedLights} lights resolved on original models" );
			Assert.IsTrue( checkedLights >= 30 );
		}
		finally
		{
			FileSystem = previous;
		}
	}

	[TestMethod]
	public void TheLampSourceAddsNothingAtDayAndTheLevelRegistersItOnlyWithTheExtension()
	{
		var source = new EveningLampSource( new EveningLampTable( Array.Empty<EveningLampRule>() ), () => Array.Empty<OriginalObject>(), () => 0f );
		var lights = new List<PointLight>();
		source.AppendPointLights( lights );
		Assert.AreEqual( 0, lights.Count );
		Assert.AreEqual( 0, source.LastCount );
		var set = new PointLightSet();
		set.Add( source );
		set.Gather( null );
		Assert.AreEqual( 0, set.Count, "no lamp lights at day: the lights path does no work" );
		// a level with neither objects nor point lights (built without its constructor) registers nothing and never throws
		var level = BareLevel();
		level.AdvanceEvening( enabled: true, EveningPace.AlwaysNight, 0.1, playing: true );
		Assert.IsNull( level.EveningLampLights );
		Assert.IsFalse( level.Look.IsDay );
	}

	// ---- The lamp cache ----------------------------------------------------------------------------------------------

	// an object needs a graphics device to be built, so the cache is tested on uninitialized objects with a counting resolver
	private static OriginalObject Stub() => (OriginalObject)RuntimeHelpers.GetUninitializedObject( typeof( OriginalObject ) );

	private static readonly IReadOnlyList<ResolvedEveningLamp> OneLamp = new[] { (ResolvedEveningLamp)RuntimeHelpers.GetUninitializedObject( typeof( ResolvedEveningLamp ) ) };

	[TestMethod]
	public void TheLampCacheFollowsPlacementAndRemovalAndResolvesEachObjectOnlyOncePerRebuild()
	{
		var placed = new List<OriginalObject>();
		var version = 0;
		var resolves = 0;
		var bearing = new HashSet<OriginalObject>();
		var source = new EveningLampSource( new EveningLampTable( Array.Empty<EveningLampRule>() ), () => placed, () => 1f, () => version )
		{
			ResolverOverride = item => { resolves++; return bearing.Contains( item ) ? OneLamp : Array.Empty<ResolvedEveningLamp>(); }
		};
		source.Refresh();
		Assert.AreEqual( 0, source.BearerCount );

		var (a, b, c) = (Stub(), Stub(), Stub());
		bearing.Add( a );
		bearing.Add( c );
		placed.AddRange( new[] { a, b, c } );
		version++;
		source.Refresh();
		Assert.AreEqual( 2, source.BearerCount, "a placed object that bears a lamp joins the cache; one without does not" );
		Assert.AreEqual( 3, resolves );

		for ( var frame = 0; frame < 100; frame++ )
			source.Refresh();
		Assert.AreEqual( 3, resolves, "an unchanged set is not walked again" );

		placed.Remove( a );
		version++;
		source.Refresh();
		Assert.AreEqual( 1, source.BearerCount, "a removed object leaves the cache" );

		// a removal and a placement between two frames keep the length: only the counter catches it
		var d = Stub();
		bearing.Add( d );
		placed.Remove( b );
		placed.Insert( 0, d );
		version++;
		source.Refresh();
		Assert.AreEqual( 2, source.BearerCount );
		Assert.AreEqual( 2, placed.Count, "the swap kept the length" );

		// by day the cache is dropped (it would keep removed objects alive) and rebuilt at the next night frame
		var level = 1f;
		var dayNight = new EveningLampSource( new EveningLampTable( Array.Empty<EveningLampRule>() ), () => placed, () => level, () => version ) { ResolverOverride = item => OneLamp };
		dayNight.Refresh();
		Assert.AreEqual( 2, dayNight.BearerCount );
		level = 0f;
		dayNight.AppendPointLights( new List<PointLight>() );
		Assert.AreEqual( 0, dayNight.BearerCount, "dropped by day" );
		level = 1f;
		dayNight.Refresh();
		Assert.AreEqual( 2, dayNight.BearerCount, "rebuilt at night" );

		// a host without a change counter is caught by the list length
		var plain = new EveningLampSource( new EveningLampTable( Array.Empty<EveningLampRule>() ), () => placed, () => 1f ) { ResolverOverride = item => OneLamp };
		plain.Refresh();
		Assert.AreEqual( 2, plain.BearerCount );
		placed.Add( Stub() );
		plain.Refresh();
		Assert.AreEqual( 3, plain.BearerCount );
	}

	[TestMethod]
	public void TheLampCacheAllocatesNothingPerFrameWhileTheSetIsUnchanged()
	{
		var placed = Enumerable.Range( 0, 500 ).Select( _ => Stub() ).ToList();
		var source = new EveningLampSource( new EveningLampTable( Array.Empty<EveningLampRule>() ), () => placed, () => 0f, () => 7 ) { ResolverOverride = item => Array.Empty<ResolvedEveningLamp>() };
		source.Refresh();
		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( var frame = 0; frame < 1000; frame++ )
			source.Refresh();
		Assert.IsTrue( GC.GetAllocatedBytesForCurrentThread() - before < 4096 );
	}
}
