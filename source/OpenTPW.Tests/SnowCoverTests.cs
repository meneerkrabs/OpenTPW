using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using OpenTPW.Mods;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:winter-snow] The settling snow extension (docs/WINTER.md, phase W1): the cover's accumulate and melt, its pause and speed behaviour, the optional unhashed save field, the uniform slot and the
/// shader declarations, the registration, and the guarantee that none of it touches the simulation. No GPU; everything runs on the repository's own files.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SnowCoverTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static SamSettings Hallow() => new( new[] { SamDocument.Parse(
		"WeatherEffects.QualityForSnowLow\t0\nWeatherEffects.QualityForSnowHigh\t25\nWeatherEffects.QualityForRainLow\t15\nWeatherEffects.QualityForRainHigh\t45\n" +
		"WeatherEffects.QualityForLightningLow\t0\nWeatherEffects.QualityForLightningHigh\t25\nWeatherEffects.MaxRaindrops\t600\nWeather.DaysOfWarning\t4\nWeather.DaysBetweenChanges\t7\nWeather.SpeedOfChange\t20\n", "test" ) } );

	private static ModSet Set( bool snow ) => ModSet.Resolve( Array.Empty<DiscoveredMod>(),
		new ModSettings { Enabled = new Dictionary<string, bool> { [OfficialExtensions.WinterSnow.Id] = snow } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !Directory.Exists( Path.Combine( directory.FullName, "content", "shaders" ) ) )
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException( "content/shaders" );
	}

	private static string ShaderText( string name ) => File.ReadAllText( Path.Combine( RepositoryRoot(), "content", "shaders", name ) );

	// ---- the cover ----

	[TestMethod]
	public void CoverRisesWithTheDropsWhileItSnowsAndIsClamped()
	{
		var cover = new SnowCover();
		Assert.AreEqual( 0.0, cover.Cover );
		cover.Step( snowing: true, raining: false, drops: 600, maximumDrops: 600 );
		Assert.AreEqual( SnowCover.Accumulate, cover.Cover, 1e-12, "the full drop count gains the full step" );
		var half = new SnowCover();
		half.Step( true, false, 300, 600 );
		Assert.AreEqual( SnowCover.Accumulate / 2, half.Cover, 1e-12, "half the drops, half the step" );
		for ( var turn = 0; turn < 200; turn++ )
			cover.Step( true, false, 600, 600 );
		Assert.AreEqual( 1.0, cover.Cover, "clamped at full cover" );
		// full cover after about 1.5 game days (23.04 turns each) of the heaviest snow
		var heavy = new SnowCover();
		var turns = 0;
		while ( heavy.Cover < 1.0 )
		{
			heavy.Step( true, false, 600, 600 );
			turns++;
		}
		Assert.AreEqual( 35, turns );
		Assert.IsTrue( turns / 23.04 is > 1.4 and < 1.6 );
	}

	[TestMethod]
	public void SnowWithNoDropsYetDoesNotBuildAndTheCoverMeltsAfterwards()
	{
		var cover = new SnowCover( 0.5 );
		cover.Step( snowing: true, raining: false, drops: 0, maximumDrops: 600 );
		Assert.AreEqual( 0.5 - SnowCover.Melt, cover.Cover, 1e-12, "a snow flag without falling drops is a dry turn" );
		var dry = new SnowCover( 1.0 );
		var turns = 0;
		while ( dry.Cover > 0 )
		{
			dry.Step( false, false, 0, 600 );
			turns++;
		}
		Assert.IsTrue( turns is 70 or 71, $"{turns} dry turns (70 plus a rounding step)" );
		Assert.IsTrue( turns / 23.04 is > 2.9 and < 3.2, "gone after about three game days" );
		Assert.AreEqual( 0.0, dry.Cover, "clamped at bare" );
	}

	[TestMethod]
	public void RainMeltsThreeTimesFasterThanADrySky()
	{
		var dry = new SnowCover( 1.0 );
		var wet = new SnowCover( 1.0 );
		dry.Step( false, false, 0, 600 );
		wet.Step( false, true, 300, 600 );
		Assert.AreEqual( ( 1.0 - dry.Cover ) * SnowCover.RainMeltFactor, 1.0 - wet.Cover, 1e-12 );
		Assert.AreEqual( 3.0, SnowCover.RainMeltFactor );
	}

	[TestMethod]
	public void TheCoverStepsOnlyWithTheWeathersTurnsSoItFreezesWhenPausedAndScalesWithSpeed()
	{
		var weather = new ParkWeather( Hallow(), 1, 10, 10 );
		var cover = new SnowCover();
		var steps = 0;
		weather.TurnStepped += w => { steps++; cover.Step( w ); };
		weather.AdvanceTo( 0 ); // the first call starts the weather: no turn is stepped
		Assert.AreEqual( 0, steps );
		weather.ApplyQuality( 8 );
		Assert.IsTrue( weather.Snow );
		// a park that does not advance its tick (paused) steps no turn: the cover cannot move however many frames pass
		for ( var frame = 0; frame < 1000; frame++ )
			weather.AdvanceTo( 0 );
		Assert.AreEqual( 0, steps );
		Assert.AreEqual( 0.0, cover.Cover );
		// the weather steps once per park turn; a faster park gets more turns in the same real time, and the cover follows turn for turn
		var tick = 0L;
		for ( var turn = 1; turn <= 100; turn++ )
		{
			tick = ParkCalendar.TickOfTurn( turn );
			weather.AdvanceTo( tick );
		}
		Assert.AreEqual( 100, steps );
		Assert.IsTrue( cover.Cover > 0.5, $"{cover.Cover}" );
		var atHundred = cover.Cover;
		for ( var frame = 0; frame < 500; frame++ )
			weather.AdvanceTo( tick );
		Assert.AreEqual( atHundred, cover.Cover, "paused again: frozen" );
		// the same turn sequence gives the same cover (a pure function of the weather's turns)
		var again = new ParkWeather( Hallow(), 1, 10, 10 );
		var second = new SnowCover();
		again.TurnStepped += w => second.Step( w );
		again.AdvanceTo( 0 );
		again.ApplyQuality( 8 );
		for ( var turn = 1; turn <= 100; turn++ )
			again.AdvanceTo( ParkCalendar.TickOfTurn( turn ) );
		Assert.AreEqual( atHundred, second.Cover );
	}

	[TestMethod]
	public void TheDrawnCoverEasesTowardTheTurnValueAndNeverOvershoots()
	{
		var cover = new SnowCover();
		cover.Step( true, false, 600, 600 );
		cover.Set( 0 );
		var target = new SnowCover( 0 );
		target.Step( true, false, 600, 600 );
		Assert.AreEqual( 0.0, target.Drawn, "a step does not move the drawn value" );
		var previous = 0.0;
		for ( var frame = 0; frame < 600; frame++ )
		{
			target.Ease( 1.0 / 60.0 );
			Assert.IsTrue( target.Drawn >= previous && target.Drawn <= target.Cover, "monotone, never past the target" );
			previous = target.Drawn;
		}
		Assert.AreEqual( target.Cover, target.Drawn, "settled on the turn value" );
		target.Ease( 0 );
		target.Ease( -1 );
		target.Ease( double.NaN );
		Assert.AreEqual( target.Cover, target.Drawn );
		Assert.AreEqual( 0.3, new SnowCover( 0.3 ).Drawn, "a resumed cover starts drawn" );
		Assert.AreEqual( 0.0, new SnowCover( double.NaN ).Cover );
		Assert.AreEqual( 1.0, new SnowCover( 7 ).Cover );
	}

	[TestMethod]
	public void SavedCoversAreValidatedAboveZeroAndAtMostOne()
	{
		Assert.IsTrue( SnowCover.IsValidSavedCover( 0.25 ) );
		Assert.IsTrue( SnowCover.IsValidSavedCover( 1.0 ) );
		foreach ( var bad in new[] { 0.0, -0.1, 1.0001, double.NaN, double.PositiveInfinity } )
			Assert.IsFalse( SnowCover.IsValidSavedCover( bad ), $"{bad}" );
	}

	// ---- the save ----

	[TestMethod]
	public void TheSaveCarriesTheCoverOptionallyAndOldSavesLoad()
	{
		var plain = ParkResumeSaveFileTests.Envelope();
		var plainJson = Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( plain ) );
		Assert.IsFalse( plainJson.Contains( "SnowCover" ), "no cover, no member: a park without snow saves the same bytes as before" );
		Assert.IsNull( ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( plain ) ).SnowCover );
		var snowy = plain with { SnowCover = 0.625 };
		var restored = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snowy ) );
		Assert.AreEqual( 0.625, restored.SnowCover );
		ParkResumeSaveFile.ValidateShape( snowy );
		foreach ( var bad in new[] { 0.0, -1.0, 1.5, double.NaN } )
		{
			var exception = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( plain with { SnowCover = bad } ) );
			StringAssert.Contains( exception.Message, "snow cover" );
		}
		// an older save has no member at all
		var json = JsonNode.Parse( Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( snowy ) ) )!.AsObject();
		json.Remove( "SnowCover" );
		Assert.IsNull( ParkResumeSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ).SnowCover );
	}

	[TestMethod]
	public void TheCoverIsSavedOnlyWhileTheExtensionIsOnAndItIsAboveZero()
	{
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Assert.IsNull( level.CaptureSnowCover(), "no cover yet" );
		typeof( Level ).GetField( "snow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( level, new SnowCover( 0 ) );
		Assert.IsNull( level.CaptureSnowCover(), "a bare park saves no member" );
		level.Snow!.Set( 0.4 );
		Assert.AreEqual( 0.4, level.CaptureSnowCover() );
		Assert.AreEqual( 0.4f, level.SnowUniform, 1e-6f );
		Assert.IsNull( ParkResumeMods.Capture( Set( true ) ), "presentation only: a save records no mod for it" );
		Assert.IsNull( ParkResumeMods.Capture( Set( false ) ) );
	}

	// ---- the world is untouched ----

	[TestMethod]
	public void TheCoverChangesNoWorldStateHashAtAnyCover()
	{
		// the economy beside a cover that is stepped by a forced snowfall: this proves only that the cover shares no state with the economy; the real-level proof is the --perf-hash-tick run (docs/WINTER.md)
		ulong Hash( bool extensionOn, bool forcedSnow )
		{
			using var scope = OpenTpwExtensions.Use( Set( extensionOn ) );
			var economy = EconomyTestData.Park();
			var weather = new ParkWeather( Hallow(), 5, 10, 10 );
			SnowCover? cover = FeatureGates.WinterSnow ? new SnowCover() : null;
			if ( cover != null )
				weather.TurnStepped += w => cover.Step( w );
			weather.AdvanceTo( 0 );
			if ( forcedSnow )
				weather.ApplyQuality( 8 );
			for ( var turn = 1; turn <= 80; turn++ )
			{
				EconomyTestData.AdvanceToTurn( economy, turn );
				weather.AdvanceTo( ParkCalendar.TickOfTurn( turn ) );
				cover?.Ease( 0.1 );
			}
			Assert.AreEqual( extensionOn, cover != null );
			if ( extensionOn && forcedSnow )
				Assert.IsTrue( cover!.Cover > 0.5, "the cover ran" );
			return WorldStateHash.Compute( new WorldStateSources { Economy = economy } );
		}
		var off = Hash( extensionOn: false, forcedSnow: true );
		Assert.AreEqual( off, Hash( extensionOn: true, forcedSnow: true ) );
		Assert.AreEqual( Hash( extensionOn: false, forcedSnow: false ), Hash( extensionOn: true, forcedSnow: false ) );
	}

	[TestMethod]
	public void TheWeatherAndItsSnapshotAreIdenticalWithAndWithoutTheCoverSubscribed()
	{
		// the weather is what the simulation reads (arrivals, the gate): a subscribed cover must leave its whole state and every later random draw as they were
		string Run( bool withCover )
		{
			var weather = new ParkWeather( Hallow(), 5, 10, 10 );
			SnowCover? cover = withCover ? new SnowCover() : null;
			if ( cover != null )
				weather.TurnStepped += w => cover.Step( w );
			weather.AdvanceTo( 0 );
			weather.ApplyQuality( 8 );
			var trace = new StringBuilder();
			for ( var turn = 1; turn <= 400; turn++ )
			{
				weather.AdvanceTo( ParkCalendar.TickOfTurn( turn ) );
				cover?.Ease( 0.1 );
				trace.Append( weather.Snow ).Append( weather.Rain ).Append( weather.CurrentDrops ).Append( ',' );
			}
			return trace + System.Text.Json.JsonSerializer.Serialize( weather.CaptureSnapshot() );
		}
		Assert.AreEqual( Run( false ), Run( true ) );
	}

	[TestMethod]
	public void NoSimulationSourceReadsTheSnowCover()
	{
		// the cover may be read by the renderer and the save only; a reference from the economy, guests, rides, staff or the weather itself would let it into the simulation
		var root = new DirectoryInfo( AppContext.BaseDirectory );
		while ( root != null && !Directory.Exists( Path.Combine( root.FullName, "source", "OpenTPW", "World" ) ) )
			root = root.Parent;
		var world = Path.Combine( root?.FullName ?? throw new DirectoryNotFoundException( "source/OpenTPW/World" ), "source", "OpenTPW", "World" );
		var allowed = new[] { "Winter", "Entity", "Objects", "Original", "Tracks", "Coasters" };
		var offenders = Directory.EnumerateFiles( world, "*.cs", SearchOption.AllDirectories )
			.Select( file => (file, relative: Path.GetRelativePath( world, file )) )
			.Where( item => !allowed.Any( folder => item.relative.StartsWith( folder + Path.DirectorySeparatorChar ) ) )
			.Where( item => !new[] { "Level.Resume.cs", "ParkResumeSaveFile.cs", "ParkResumeSnapshot.cs", "Level.Simulation.cs", "Level.cs" }.Contains( Path.GetFileName( item.file ) ) )
			.Where( item => File.ReadAllLines( item.file ).Where( line => !line.TrimStart().StartsWith( "//" ) ).Any( line => line.Contains( "SnowCover" ) || line.Contains( "SnowUniform" ) ) )
			.Select( item => item.relative ).ToList();
		CollectionAssert.AreEqual( Array.Empty<string>(), offenders, "files outside the presentation and save paths that mention the snow cover: " + string.Join( ", ", offenders ) );
	}

	// ---- the uniform and the shaders ----

	[TestMethod]
	public void TheCoverRidesInTheLastPaddingAtByte268AndABareDrawIsByteForByteAsBefore()
	{
		Assert.AreEqual( 268, (int)Marshal.OffsetOf<ObjectUniformBuffer>( nameof( ObjectUniformBuffer._padding6 ) ), "a float after the fog colour's vec3 at 256 packs at 268 in std140" );
		Assert.AreEqual( 272, Unsafe.SizeOf<ObjectUniformBuffer>(), "the block did not grow" );
		var plain = new ObjectUniformBuffer { g_mModel = Matrix4x4.CreateTranslation( 1, 2, 3 ), g_flTime = 7 }.WithFog();
		var bare = plain;
		bare.SnowCover = 0f;
		CollectionAssert.AreEqual( MemoryMarshal.AsBytes( new ReadOnlySpan<ObjectUniformBuffer>( in plain ) ).ToArray(), MemoryMarshal.AsBytes( new ReadOnlySpan<ObjectUniformBuffer>( in bare ) ).ToArray() );
		var snowy = plain;
		snowy.SnowCover = -0.2f;
		Assert.AreEqual( -0.2f, snowy._padding6 );
		Assert.AreEqual( SnowCover.GroundSurface, 1f );
		Assert.IsTrue( SnowCover.PathSurface < 0f, "paths read as slush" );
	}

	[TestMethod]
	public void TestShaderDeclaresTheCoverAfterTheFogColourInBothBlocksAndSkipsItAtZero()
	{
		var text = ShaderText( "test.shader" );
		Assert.AreEqual( 2, System.Text.RegularExpressions.Regex.Matches( text, @"vec3 g_vFogColor;\s*float g_flSnowCover;[^\n]*\n\s*vec4 g_vPointLight\[32\];" ).Count, "the float fills the padding before the point lights at byte 272 in both blocks" );
		StringAssert.Contains( text, "if ( g_oUbo.g_flSnowCover != 0.0 && ( outMatFlags & FLAG_TRANSPARENT ) == 0u )", "cover 0 runs none of the snow code" );
		// the pools keep the slot for their glow and are not touched
		foreach ( var name in new[] { "pool-deck.shader", "pool-basin.shader", "pool-water.shader" } )
			Assert.IsFalse( ShaderText( name ).Contains( "g_flSnowCover" ), name );
	}

	[TestMethod]
	public void TheSnowHashHasNoSinAndTheNoiseCellsWrapSoLargeWorldPositionsDoNotBand()
	{
		var text = ShaderText( "test.shader" );
		var start = text.IndexOf( "float snowHash", StringComparison.Ordinal );
		var end = text.IndexOf( "vec3 snowBlend", StringComparison.Ordinal );
		Assert.IsTrue( start > 0 && end > start );
		var body = text[start..end];
		Assert.IsFalse( body.Contains( "sin(", StringComparison.Ordinal ) || body.Contains( "sin (", StringComparison.Ordinal ), "a sin( dot ) hash bands on ANGLE/D3D and mobile GPUs at world positions around 1e5" );
		StringAssert.Contains( body, "mod( floor( p ), 2048.0 )" );
	}

	[TestMethod]
	public void PathLikeMaterialsAndMovingPartsSelectSlushAndModelSpaceNoise()
	{
		foreach ( var name in new[] { "js_path", "ss_PATH", "grd_pav4", "GRD_PAV1", "grd_ctr1", "grd_krb1" } )
			Assert.IsTrue( TextureSlotAddressing.IsPathLikeTexture( name ), name );
		foreach ( var name in new[] { "hpa_ctr", "hrk_bas1", "jgr_bas1", "grd_top1", "gte_wal1", null } )
			Assert.IsFalse( TextureSlotAddressing.IsPathLikeTexture( name ), name ?? "null" );
		Assert.AreEqual( 0u, TextureSlotAddressing.SlushFlag & ( TextureSlotAddressing.ClampFlag | 0xFFu ), "the flag shares no bit with the original slot flags or the clamp flag" );
		StringAssert.Contains( ShaderText( "test.shader" ), "const uint FLAG_SLUSH = 0x40000000u;" );
		var entity = (ModelEntity)RuntimeHelpers.GetUninitializedObject( typeof( ModelEntity ) );
		var uniform = typeof( ModelEntity ).GetMethod( "SnowUniformFor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!;
		Assert.AreEqual( 0f, (float)uniform.Invoke( entity, new object[] { 0.6f } )!, "no surface factor: no snow" );
		entity.SnowSurface = SnowCover.GroundSurface;
		Assert.AreEqual( 0.6f, (float)uniform.Invoke( entity, new object[] { 0.6f } )!, 1e-6f );
		entity.SnowNoiseInModelSpace = true;
		Assert.AreEqual( 2.6f, (float)uniform.Invoke( entity, new object[] { 0.6f } )!, 1e-6f, "a moving part adds 2 so the shader takes its noise in model space" );
		Assert.AreEqual( 0f, (float)uniform.Invoke( entity, new object[] { 0f } )!, "no cover stays exactly 0, whatever the noise space" );
		entity.SnowSurface = SnowCover.PathSurface;
		Assert.AreEqual( -0.21f, (float)uniform.Invoke( entity, new object[] { 0.6f } )!, 1e-6f, "slush never moves to model space" );
	}

	[TestMethod]
	public void TheWebShaderIsRegeneratedForTheCover()
	{
		var json = File.ReadAllText( Path.Combine( RepositoryRoot(), "content", "shaders", "web", "test.json" ) );
		StringAssert.Contains( json, "g_flSnowCover" );
		StringAssert.Contains( json, "snowBlend" );
	}

	[TestMethod]
	public void OnlyTheEntitiesOfTestShaderAreGivenASurfaceFactor()
	{
		Assert.AreEqual( 0f, ((ModelEntity)RuntimeHelpers.GetUninitializedObject( typeof( ModelEntity ) )).SnowSurface, "every other entity (cars, markers, signs, lobby scenes, water) shows no snow" );
	}

	// ---- the extension ----

	[TestMethod]
	public void TheExtensionIsRegisteredOnByDefaultPresentationOnlyAndSelectable()
	{
		var snow = OfficialExtensions.WinterSnow;
		Assert.AreEqual( "opentpw.winter-snow", snow.Id );
		Assert.IsTrue( snow.DefaultEnabled, "on by default: the user decided on 2026-10-18" );
		Assert.IsFalse( snow.AffectsSimulation );
		Assert.IsTrue( snow.PlayerSelectable );
		Assert.AreEqual( new ModVersion( 1, 0, 0 ), snow.Version );
		Assert.AreSame( snow, OfficialExtensions.Find( "opentpw.winter-snow" ) );
		CollectionAssert.Contains( OfficialExtensions.All.ToArray(), snow );
		Assert.IsTrue( FeatureGates.WinterSnow, "the process default" );
		using ( OpenTpwExtensions.Use( Set( false ) ) )
			Assert.IsFalse( FeatureGates.WinterSnow );
		ParkResumeMods.Require( null, Set( true ) );
		ParkResumeMods.Require( null, Set( false ) );
	}

	[TestMethod]
	public void TheModsScreenListsTheExtensionWithItsNameAndDescriptionInEveryLanguage()
	{
		foreach ( var key in new[] { OpenTpwText.ExtensionWinterSnowName, OpenTpwText.ExtensionWinterSnowDescription } )
		{
			var texts = SupplementaryStrings.SupportedLanguages.Select( language => SupplementaryStrings.Get( key, language ) ).ToList();
			Assert.AreEqual( SupplementaryStrings.SupportedLanguages.Count, texts.Count );
			Assert.IsTrue( texts.All( text => !string.IsNullOrWhiteSpace( text ) ), $"{key} has a text in every language" );
			Assert.AreEqual( texts.Count, texts.Distinct().Count(), $"{key}: every language has its own text" );
		}
		foreach ( var language in SupplementaryStrings.SupportedLanguages )
			Assert.IsTrue( SupplementaryStrings.Get( OpenTpwText.ExtensionWinterSnowDescription, language ).Length <= 300, $"{language}: fits the details label" );
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), new ModsServices
		{
			Discovered = () => Array.Empty<DiscoveredMod>(),
			SettingsPath = Path.Combine( Path.GetTempPath(), $"opentpw-mods-snow-{Guid.NewGuid():N}.json" ),
			Active = () => ModSet.Defaults( OfficialExtensions.All ),
			OpenTpwVersion = null,
			Edition = () => GameEdition.ThemeParkWorld,
			Inspect = ( path, _ ) => System.Threading.Tasks.Task.FromResult( new InstallationDiscoveryResult( new[] { GameInstallation.Inspect( path ) }, false, null ) ),
		} );
		stack.Push( screen );
		var rows = ((UiScrollList)screen.Find( "extensions" )!).Rows();
		CollectionAssert.Contains( rows.ToArray(), SupplementaryStrings.Get( OpenTpwText.ExtensionWinterSnowName, "English" ) + "  1.0.0" );
		stack.Clear();
	}
}
