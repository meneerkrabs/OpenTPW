using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;

namespace OpenTPW.Tests;

/// <summary>The front end's advisor host (tp.exe 0x54E5EC, 0x54E6DF, 0x54E7F5): the idle line, the speech option, the cut-off at teardown and the ambient. GPU free.</summary>
[TestClass, DoNotParallelize]
public class LobbyAdvisorTests
{
	private Logger previousLog = null!;
	[TestInitialize] public void Initialize() { previousLog = Log; Log = new(); }
	[TestCleanup] public void Cleanup() => Log = previousLog;

	private sealed class Host
	{
		public long Now;
		public bool SpeechOn = true, Busy, Speaking;
		public int Coin;
		public readonly List<int> Said = new();
		public readonly List<uint> Slaps = new();
		public LobbyAdvisor Advisor;
		public Host()
		{
			Advisor = new LobbyAdvisor( () => OpenTPW.Advisor.WithoutModel( () => Now ), () => Now, () => SpeechOn, id => { Said.Add( id ); return true; },
				() => Busy, () => Speaking, Slaps.Add, () => Coin, () => 4 );
		}
	}

	[TestMethod]
	public void TheIdleLineIs395ForAnOddDrawAnd394ForAnEvenOneAfter90SecondsOfTimer()
	{
		foreach ( var (coin, line) in new[] { (1, 395), (2, 394), (32767, 395), (0, 394) } )
		{
			var host = new Host { Coin = coin, Now = 90001 };
			host.Advisor.Update( LobbyAdvisorScreen.Islands );
			Assert.AreEqual( 0, host.Said.Count, "queued in the frame the silence is past" );
			host.Advisor.Update( LobbyAdvisorScreen.Islands );
			CollectionAssert.AreEqual( new[] { line }, host.Said, $"coin {coin}" );
		}
	}

	[TestMethod]
	public void NothingIsSaidBeforeTheTimerIsPast90Seconds()
	{
		var host = new Host { Now = 89000 };
		for ( var frame = 0; frame < 5; frame++ ) host.Advisor.Update( LobbyAdvisorScreen.Islands );
		Assert.AreEqual( 0, host.Said.Count );
		host.Now = 90001;
		host.Advisor.Update( LobbyAdvisorScreen.Islands ); host.Advisor.Update( LobbyAdvisorScreen.Islands );
		Assert.AreEqual( 1, host.Said.Count );
	}

	[TestMethod]
	public void TheIdleLineDrawIsTheLastFramesNotTheOneWhenItWasQueued()
	{
		// [BIN:TPW-EXE:0x5E1830 islands update] the draw is made every frame and the queue says the id the last frame stored
		var host = new Host { Coin = 2, Now = 90001 };
		host.Advisor.Update( LobbyAdvisorScreen.Islands ); // queues 394
		host.Coin = 1;
		host.Advisor.Update( LobbyAdvisorScreen.Islands ); // says what was queued
		CollectionAssert.AreEqual( new[] { 394 }, host.Said );
		Assert.AreEqual( LobbyAdvisorQueue.IdleLineOdd, host.Advisor.Queue.IdleLine );
	}

	[TestMethod]
	public void WithTheAdvisorOptionOffTheLineIsRefusedAndLost()
	{
		// [BIN:TPW-EXE:0x599050 response player] the speech flag 0x78D90C off: the id was already taken from the queue
		var host = new Host { Now = 90001, SpeechOn = false };
		host.Advisor.Update( LobbyAdvisorScreen.Islands ); host.Advisor.Update( LobbyAdvisorScreen.Islands );
		Assert.AreEqual( 0, host.Said.Count ); Assert.AreEqual( 0, host.Advisor.Queue.Count );
	}

	[TestMethod]
	public void AResponseThatPlaysIsNeverInterruptedByTheQueue()
	{
		var host = new Host { Now = 200000, Busy = true };
		host.Advisor.Queue.Push( 9, false );
		for ( var frame = 0; frame < 3; frame++ ) host.Advisor.Update( LobbyAdvisorScreen.Islands );
		Assert.AreEqual( 0, host.Said.Count ); Assert.AreEqual( 1, host.Advisor.Queue.Count );
	}

	[DataTestMethod]
	[DataRow( true, true, 0x27F + 4 % 3 )]
	[DataRow( true, false, -1 )]
	[DataRow( false, true, -1 )]
	public void TheTeardownCutsTheResponseOffWithTheSlapWhileItsSpeechPlays( bool busy, bool speaking, int slap )
	{
		// [BIN:TPW-EXE:0x5994E0 cut-off path] 0x27F + rand() % 3 sounds only while the speech sound still plays; the queue's teardown calls it only with a response active
		var host = new Host { Busy = busy, Speaking = speaking };
		host.Advisor.Queue.Push( 1, false );
		host.Advisor.End();
		Assert.AreEqual( 0, host.Advisor.Queue.Count );
		if ( slap < 0 ) Assert.AreEqual( 0, host.Slaps.Count ); else CollectionAssert.AreEqual( new[] { (uint)slap }, host.Slaps );
	}

	[TestMethod]
	public void AfterTheTeardownNothingRunsAndTheEndIsIdempotent()
	{
		var host = new Host { Now = 200000 };
		host.Advisor.End(); host.Advisor.End();
		host.Advisor.Update( LobbyAdvisorScreen.Islands ); host.Advisor.Update( LobbyAdvisorScreen.Islands );
		Assert.AreEqual( 0, host.Said.Count );
		host.Advisor.Dispose();
	}

	[TestMethod]
	public void TheAdvisorIsMadeOnlyWhenItIsNeededAndIsLitByTheScreenItIsShownOver()
	{
		var made = 0;
		long now = 0;
		var host = new LobbyAdvisor( () => { made++; return OpenTPW.Advisor.WithoutModel( () => now ); }, () => now, () => true, _ => true, () => false, () => false, _ => { }, () => 0, () => 0 );
		host.Update( LobbyAdvisorScreen.Islands );
		Assert.AreEqual( 0, made, "no model load at the lobby's entry" );
		var advisor = host.Presentation;
		Assert.AreEqual( 1, made );
		Assert.AreEqual( LobbyAdvisorAmbient.IslandScreens, advisor.Ambient );
		host.Update( LobbyAdvisorScreen.Globe );
		Assert.AreEqual( LobbyAdvisorAmbient.Globe, advisor.Ambient );
		host.Update( LobbyAdvisorScreen.Islands );
		Assert.AreEqual( LobbyAdvisorAmbient.IslandScreens, advisor.Ambient );
		host.Dispose();
	}

	[TestMethod]
	public void TheAmbientWordsAreTheScreensColourBytesOver255()
	{
		// [BIN:TPW-EXE:0x5D9230 ambient setter] channel = byte x 0x702A4C (1/255); 0x444444 on the island screens, 0x202020 on the globe
		Assert.AreEqual( new System.Numerics.Vector3( 68f / 255f ), LobbyAdvisorAmbient.IslandScreens );
		Assert.AreEqual( new System.Numerics.Vector3( 32f / 255f ), LobbyAdvisorAmbient.Globe );
		Assert.AreEqual( 0.2667f, LobbyAdvisorAmbient.IslandScreens.X, 0.0001f ); Assert.AreEqual( 0.1255f, LobbyAdvisorAmbient.Globe.X, 0.0001f );
	}

	[TestMethod]
	public void TheGameFlowHooksSitWhereTheOriginalsFrameAndStatesAre()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "source", "OpenTPW", "Client", "GameFlow.cs" ) ) ) directory = directory.Parent;
		Assert.IsNotNull( directory, "The repository checkout must be available for this test." );
		var flow = File.ReadAllText( Path.Combine( directory.FullName, "source", "OpenTPW", "Client", "GameFlow.cs" ) ).Replace( "\r\n", "\n" );
		Assert.IsTrue( flow.IndexOf( "Camera.SetCameraMode<LobbyCameraMode>();\n\t\tStartLobbyAdvisor();", StringComparison.Ordinal ) > 0, "set up with the front end" );
		Assert.IsTrue( flow.Contains( "Advisor?.DetachLevel();\n\t\tEndLobbyAdvisor();", StringComparison.Ordinal ), "torn down with it" );
		Assert.IsTrue( flow.Contains( "UpdateLobbyEffects();\n\t\tUpdateLobbyAdvisor();", StringComparison.Ordinal ), "updated with the lobby frame" );
		Assert.IsTrue( flow.Contains( "RenderLobbyLayers( entities, lobbyWeather, lobbyEffects.Flash && IncFrontEnd == null, drawLobbyFlash ??= DrawLobbyFlash );\n\t\t\tRenderLobbyAdvisor();", StringComparison.Ordinal ), "rendered after the lobby's layers" );
		Assert.IsTrue( flow.Contains( "Menu?.Stack.Draw( Context );\n\t\tDrawLobbyAdvisor();", StringComparison.Ordinal ), "drawn over the lobby screens" );
		var advisor = File.ReadAllText( Path.Combine( directory.FullName, "source", "OpenTPW", "World", "Advisor.cs" ) ).Replace( "\r\n", "\n" );
		StringAssert.Contains( advisor, "_padding6 = Ambient.HasValue ? 1f : 0f," );
	}
}
