using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The screen-space particle effect 0x62 the enter-park routine (tp.exe 0x4b8fd0) starts over the lobby's key panel through the creator 0x521e60. GPU free.</summary>
[TestClass]
[DoNotParallelize]
public class LobbyEnterEffectTests
{
	[TestInitialize]
	public void Init() => Log = new();

	private sealed class RecordingPool : IAdvisorParticlePool
	{
		public readonly List<(int Effect, (int X, int Y, int Z) Position)> Created = new();
		public object? Create( int effectId, (int X, int Y, int Z) native ) { Created.Add( (effectId, native) ); return Created.Count; }
		public void Move( object handle, (int X, int Y, int Z) native ) { }
		public void Kill( object handle ) { }
	}

	[TestMethod]
	public void TheEffectSitsAtTheScreenCentreAboveTheKeyPanelsCentreRow()
	{
		// [BIN:TPW-EXE:0x4B9065] the control 0x1E0EA spans rows 170..437 of the 1536-row canvas: its centre row is (437 - 170) >> 1 + 170 = 303, z = 303 * 75000 / 0x600
		var rect = OriginalLayoutTables.Lobby.Control( LobbyEnterEffect.KeyPanelControl ).Rect;
		Assert.AreEqual( 170f, rect.Y );
		Assert.AreEqual( 437f, rect.Y + rect.Height );
		Assert.AreEqual( 303, ((437 - 170) >> 1) + 170 );
		Assert.AreEqual( (0xC350, 0, 14794), LobbyEnterEffect.Position(), "22725000 / 1536 truncates to 14794" );
		Assert.AreEqual( ParticleScreenSpace.CentreX, LobbyEnterEffect.NativeX );
	}

	[TestMethod]
	public void TheZScaleIsTheSignedTruncatingDivisionOfTheCreatorsArgument()
	{
		// [BIN:TPW-EXE:0x4B9075 z scale] eax * 75000 / 0x600 (imul by 0x2aaaaaab, sar 8, plus the sign bit): rows 0 and 1536 are the screen's top and bottom
		Assert.AreEqual( 0, LobbyEnterEffect.NativeZ( 0, 0 ) );
		Assert.AreEqual( 75000, LobbyEnterEffect.NativeZ( 0, 3072 ) );
		Assert.AreEqual( 37500, LobbyEnterEffect.NativeZ( 768, 768 ) );
		Assert.AreEqual( 48, LobbyEnterEffect.NativeZ( 1, 2 ), "(1 >> 1) + 1 = 1 row, 75000 / 1536 = 48" );
		Assert.AreEqual( -48, LobbyEnterEffect.NativeZ( -1, 0 ), "a negative row truncates toward zero" );
	}

	[TestMethod]
	public void TheEffectLandsAboveTheScreenCentreInTheScreenSpaceTheKeysUse()
	{
		// the creator stores each coordinate >> 4 (0x521E60), the draw list turns it into screen units (0x51F01A)
		var (x, _, z) = LobbyEnterEffect.Position();
		var screen = ParticleScreenSpace.Position( x >> 4, z >> 4 );
		Assert.AreEqual( 0f, screen.X, "x 50000 is the horizontal centre" );
		Assert.AreEqual( -613 / 1024f, screen.Y, "(924 * 16 - 37500) / 37 truncates to -613: the upper fifth of the screen, y down" );
		var panel = OriginalLayoutTables.Lobby.Control( LobbyEnterEffect.KeyPanelControl ).Rect;
		Assert.IsTrue( Math.Abs( screen.Y - ((panel.Y + panel.Height / 2f) / 768f - 1f) ) < 0.01f, "it is the panel's own height on the canvas" );
	}

	[TestMethod]
	public void InstantActionStartsNothingAndTheOtherModeStartsEffect0x62Once()
	{
		var pool = new RecordingPool();
		Assert.IsFalse( LobbyEnterEffect.Starts( instantAction: true ), "[BIN:TPW-EXE:0x4B9028] game mode 2 skips the whole block" );
		Assert.IsNull( LobbyEnterEffect.Start( pool, instantAction: true ) );
		Assert.AreEqual( 0, pool.Created.Count );
		Assert.IsNotNull( LobbyEnterEffect.Start( pool, instantAction: false ) );
		CollectionAssert.AreEqual( new[] { (0x62, (0xC350, 0, 14794)) }, pool.Created );
		Assert.AreEqual( FrontEndMenu.EnterParkSoundPlays( true ), LobbyEnterEffect.Starts( true ), "the effect and the sound share one condition" );
		Assert.AreEqual( FrontEndMenu.EnterParkSoundPlays( false ), LobbyEnterEffect.Starts( false ) );
	}

	[TestMethod]
	public void TheMenusTickStartsTheEffectOnlyForFullSimulation()
	{
		var previousLog = Log;
		Log = new();
		try
		{
			var island = new LobbyIslandInfo( 0, "jungle", "data\\lobby\\terrain", "jun_isle", "jun_gate", "jungle", 0, 10, (0, 0, 0), Array.Empty<string>(), false, 0 );
			var effects = 0; var started = new List<GameMode>();
			var menu = new FrontEndMenu( OriginalUiTests.FakeStrings(), new[] { island }, new FrontEndActions
			{
				StartPark = ( _, mode ) => started.Add( mode ),
				StartEnterParkEffect = () => effects++,
			} );
			var context = new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1024, 768 ) };
			menu.ShowGameMode();
			var screen = menu.Stack.Top!;
			menu.Stack.Update( context, UiInput.Click( screen.Find( "instantAction" )!.ScreenRect( context.Canvas ).Center ) );
			menu.Stack.Update( context, UiInput.Click( screen.Find( "ok" )!.ScreenRect( context.Canvas ).Center ) );
			CollectionAssert.AreEqual( new[] { GameMode.InstantAction }, started );
			Assert.AreEqual( 0, effects, "Instant Action skips the effect" );
			menu.ShowGameMode();
			screen = menu.Stack.Top!;
			menu.Stack.Update( context, UiInput.Click( screen.Find( "fullSimulation" )!.ScreenRect( context.Canvas ).Center ) );
			menu.Stack.Update( context, UiInput.Click( screen.Find( "ok" )!.ScreenRect( context.Canvas ).Center ) );
			Assert.AreEqual( GameMode.FullSimulation, started[^1] );
			Assert.AreEqual( 1, effects, "Full Simulation starts it once, with the park start" );
		}
		finally { Log = previousLog; }
	}

	private static ParticleTemplateFile? ShippedFile()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			return null;
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		var particle = Directory.EnumerateDirectories( dataPath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "particle", StringComparison.OrdinalIgnoreCase ) );
		var plb = particle == null ? null : Directory.EnumerateFiles( particle ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "Tp2.plb", StringComparison.OrdinalIgnoreCase ) );
		if ( plb == null )
			return null;
		using var stream = File.OpenRead( plb );
		return new ParticleTemplateFile( stream );
	}

	[TestMethod]
	public void TheShippedTemplateIsAScreenSpaceEffectThatDrawsOnTheScreenAndEnds()
	{
		var file = ShippedFile();
		if ( file == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the shipped Tp2.plb." );
		Assert.IsTrue( file!.Templates[LobbyEnterEffect.EffectId].ScreenSpace, "template 0x62 has byte +5 set" );
		var system = new ParticleSystem( file.Templates, 1024, effectors: file.EffectorTemplates );
		var pool = new ParticleSystemPool( system );
		var handle = LobbyEnterEffect.Start( pool, instantAction: false );
		Assert.IsNotNull( handle, "the pool accepts the effect" );
		Assert.AreEqual( 1, system.LiveEmitters );
		var sprites = new List<ParticleSprite>();
		var drawn = 0;
		for ( var tick = 0; tick < 3000 && system.LiveEmitters + system.LiveEffectors + system.LiveParticles > 0; tick++ )
		{
			system.Tick();
			sprites.Clear();
			system.Snapshot( sprites );
			foreach ( var sprite in sprites )
			{
				Assert.IsTrue( sprite.ScreenSpace, "every particle of the effect is drawn in screen space" );
				drawn++;
			}
		}
		Assert.IsTrue( drawn > 0, "the effect draws particles" );
		Assert.AreEqual( 0, system.LiveEmitters + system.LiveEffectors + system.LiveParticles, "the effect runs out by itself" );
		Assert.IsTrue( system.TicksRun < 3000 );
	}
}
