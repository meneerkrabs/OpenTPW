using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Tests;

/// <summary>The HUD video round: gauges, the message tube, the skill gauge and the help box placement.</summary>
[TestClass]
public class HudVideoRoundTests
{
	private static ParkHud Hud() => (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );

	/// <summary>Gives a bare HUD a level whose park is <paramref name="park"/>: the HUD's edits go through that level's commands.</summary>
	private static void Attach( ParkHud hud, ParkEconomy park ) =>
		typeof( ParkHud ).GetField( "level", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( hud, CommandTestLevel.Create( park ) );

	[TestMethod]
	public void AGaugeDrawsTheArtOfRidestatbarCutOffAtTheValue()
	{
		// [BIN:TPW-EXE:0x478400] the bar is the art's top half cut off at value / 1024 of its width, the track its bottom half behind the whole bar
		var batch = new UiBatch();
		UiGauge.Draw( batch, new UiRect( 0, 0, 100, 10 ), null );
		Assert.AreEqual( 6, batch.Draws.Sum( draw => draw.Vertices.Count ), "only the track quad (two triangles)" );
		Assert.IsTrue( batch.Draws.All( draw => draw.Texture.ImagePath == UiGauge.ArtPath ), "the gauge draws the art, not flat colours" );
		var track = batch.Draws.SelectMany( draw => draw.Vertices ).ToList();
		Assert.IsTrue( track.All( vertex => vertex.TexCoords.Y > 0.5f ), "the track is the lower half of the art" );
		batch.Clear();
		UiGauge.Draw( batch, new UiRect( 0, 0, 100, 10 ), 0.5f );
		var vertices = batch.Draws.SelectMany( draw => draw.Vertices ).ToList();
		Assert.AreEqual( 12, vertices.Count, "the track quad and the bar quad" );
		Assert.AreEqual( 100f, vertices.Max( vertex => vertex.Position.X ), 0.01f, "the track spans the whole width" );
		var bar = vertices.Skip( 6 ).ToList();
		Assert.AreEqual( 50f, bar.Max( vertex => vertex.Position.X ), 0.01f, "half the value fills half the track" );
		Assert.IsTrue( bar.All( vertex => vertex.TexCoords.Y < 0.5f ), "the bar is the upper half of the art" );
		Assert.AreEqual( 0.5f, bar.Max( vertex => vertex.TexCoords.X ), 0.03f, "the art is cut off at the value, not stretched" );
	}

	[TestMethod]
	public void TheHappinessBarsDrawHappygradAndTheOthersRidestatbar()
	{
		// [BIN:TPW-EXE:0x7508c0] the happiness bars hand the gauge model builder happygrad.wct
		var batch = new UiBatch();
		UiGauge.Draw( batch, new UiRect( 0, 0, 100, 10 ), 0.5f, UiGauge.HappinessArtPath );
		Assert.IsTrue( batch.Draws.All( draw => draw.Texture.ImagePath == UiGauge.HappinessArtPath ) );
		batch.Clear();
		UiGauge.Draw( batch, new UiRect( 0, 0, 100, 10 ), 0.5f );
		Assert.IsTrue( batch.Draws.All( draw => draw.Texture.ImagePath == UiGauge.ArtPath ) );
	}

	[TestMethod]
	public void AFinanceGraphMarkIsAHollowThreeByThreePixelBox()
	{
		// [BIN:TPW-EXE:0x47ffe0 graph sample mark] four one-pixel edges round the sample end
		var batch = new UiBatch();
		ParkHud.HollowMark( batch, 50, 50, 2, new Veldrid.RgbaByte( 255, 0, 0, 255 ) );
		var vertices = batch.Draws.SelectMany( draw => draw.Vertices ).ToList();
		Assert.AreEqual( 24, vertices.Count, "four rectangles" );
		Assert.AreEqual( 47f, vertices.Min( vertex => vertex.Position.X ), 1e-4f );
		Assert.AreEqual( 53f, vertices.Max( vertex => vertex.Position.X ), 1e-4f );
		Assert.IsFalse( vertices.Chunk( 6 ).Any( quad => quad.Min( v => v.Position.X ) < 50 && quad.Max( v => v.Position.X ) > 50 && quad.Min( v => v.Position.Y ) < 50 && quad.Max( v => v.Position.Y ) > 50 ), "the middle pixel is empty" );
	}

	[TestMethod]
	public void TheMessageTubeAdmitsTenRefusesEleventhAndReusesReleasedSlotsAtTheTail()
	{
		var hud = Hud();
		Assert.AreEqual( 10, ParkHud.MessageTubeCapacity );
		Assert.IsFalse( hud.PostGameMessage( "  " ) );
		for ( var index = 0; index < 10; index++ )
			Assert.IsTrue( hud.PostGameMessage( $"m{index}" ) );
		Assert.IsFalse( hud.PostGameMessage( "eleventh" ) );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 10 ).Select( i => $"m{i}" ).ToArray(), hud.PendingMessages.ToArray() );
		Assert.IsTrue( hud.OpenPendingMessage( 1 ) );
		Assert.AreEqual( "m1", hud.OpenedMessage );
		Assert.AreEqual( 9, hud.PendingMessages.Count );
		Assert.IsTrue( hud.DeletePendingMessage( 0 ) );
		Assert.AreEqual( 8, hud.PendingMessages.Count );
		Assert.AreEqual( "m1", hud.OpenedMessage, "deleting a pending message leaves the open one alone" );
		Assert.IsTrue( hud.PostGameMessage( "reuse-first" ) );
		Assert.IsTrue( hud.PostGameMessage( "reuse-second" ) );
		CollectionAssert.AreEqual( Enumerable.Range( 2, 8 ).Select( i => $"m{i}" ).Concat( new[] { "reuse-first", "reuse-second" } ).ToArray(), hud.PendingMessages.ToArray() );
		Assert.IsFalse( hud.DeletePendingMessage( 10 ) );
		hud.CloseMessagePanel();
		Assert.IsNull( hud.OpenedMessage );
	}

	[TestMethod]
	public void AChallengeOffersAcceptAndDeclineThatAnswerTheWaitingOffer()
	{
		var hud = Hud();
		var park = EconomyTestData.Park(); park.OpenPark();
		park.AdvanceDays( 541 );
		Assert.IsNotNull( park.Objectives.Current, "the first offer waits for its answer" );
		Attach( hud, park );
		var actions = hud.ChallengeActions( () => park );
		CollectionAssert.AreEqual( new[] { GameMessageKind.Accept, GameMessageKind.Decline }, actions.Select( action => action.Kind ).ToArray() );
		hud.PostGameMessage( "offer", actions );
		Assert.IsTrue( hud.OpenPendingMessage( 0 ) );
		hud.OpenedActions[0].Clicked();
		Assert.IsTrue( park.Objectives.Current!.Accepted, "Accept calls the accept (0x4d2df0)" );
		Assert.IsNull( hud.OpenedMessage, "the answered message closes" );
		// [BIN:TPW-EXE:0x4d2df0 / 0x4d2ec0] both do nothing unless an offer waits: a second press is ignored
		Assert.IsFalse( hud.AnswerChallenge( park, accept: false ) );
		Assert.IsTrue( park.Objectives.Current!.Accepted );

		var declined = EconomyTestData.Park(); declined.OpenPark();
		declined.AdvanceDays( 541 );
		var index = declined.Objectives.Current!.DefinitionIndex;
		Attach( hud, declined );
		hud.ChallengeActions( () => declined )[1].Clicked();
		Assert.IsNull( declined.Objectives.Current, "Decline calls the decline (0x4d2ec0)" );
		Assert.IsFalse( declined.Objectives.Declines.ContainsKey( index ), "the decline never raises the slot's declined count (0x4d2ec0)" );
	}

	[TestMethod]
	public void AnUnansweredOfferWhoseMessageIsGoneIsPostedAgain()
	{
		var hud = Hud();
		var park = EconomyTestData.Park(); park.OpenPark();
		Attach( hud, park );
		Assert.IsFalse( hud.KeepChallengeOfferPosted( park ), "nothing waits yet" );
		park.AdvanceDays( 541 );
		Assert.IsNotNull( park.Objectives.Current );
		// a loaded park: the tube is empty but the offer waits
		Assert.IsTrue( hud.KeepChallengeOfferPosted( park ) );
		Assert.AreEqual( 1, hud.PendingMessages.Count );
		Assert.IsFalse( hud.KeepChallengeOfferPosted( park ), "once is enough while it is in the tube" );
		// deleted unread
		Assert.IsTrue( hud.DeletePendingMessage( 0 ) );
		Assert.IsTrue( hud.KeepChallengeOfferPosted( park ) );
		// opened, then closed without an answer
		Assert.IsTrue( hud.OpenPendingMessage( 0 ) );
		Assert.IsFalse( hud.KeepChallengeOfferPosted( park ), "the open panel holds it" );
		hud.CloseMessagePanel();
		Assert.IsTrue( hud.KeepChallengeOfferPosted( park ) );
		// Incoming ordinary messages cannot push the waiting offer out.
		for ( var index = 0; index < ParkHud.MessageTubeCapacity; index++ )
			hud.PostGameMessage( $"m{index}" );
		Assert.IsFalse( hud.KeepChallengeOfferPosted( park ) );
		Assert.IsTrue( hud.OpenPendingMessage( 0 ) );
		hud.OpenedActions[0].Clicked();
		Assert.IsTrue( park.Objectives.Current!.Accepted );
		Assert.IsFalse( hud.KeepChallengeOfferPosted( park ), "an answered offer is not posted again" );
	}

	[TestMethod]
	public void AWaitingOfferRetriesAfterAFullTubeFreesASlotWithoutDeclining()
	{
		var hud = Hud();
		var park = EconomyTestData.Park(); park.OpenPark();
		park.AdvanceDays( 541 );
		var offer = park.Objectives.Current;
		for ( var i = 0; i < 10; i++ ) Assert.IsTrue( hud.PostGameMessage( "ordinary-" + i ) );
		Assert.IsFalse( hud.KeepChallengeOfferPosted( park ) );
		Assert.AreSame( offer, park.Objectives.Current );
		Assert.IsFalse( park.Objectives.Current!.Accepted );
		Assert.AreEqual( 10, hud.PendingMessages.Count );
		Assert.IsTrue( hud.DeletePendingMessage( 3 ) );
		Assert.IsTrue( hud.KeepChallengeOfferPosted( park ) );
		Assert.IsFalse( hud.KeepChallengeOfferPosted( park ), "the later update must not duplicate the offer" );
		Assert.AreEqual( 10, hud.PendingMessages.Count );
		Assert.IsTrue( hud.OpenPendingMessage( 9 ) );
		CollectionAssert.AreEqual( new[] { GameMessageKind.Accept, GameMessageKind.Decline }, hud.OpenedActions.Select( action => action.Kind ).ToArray() );
	}

	[TestMethod]
	public void AMessageStaysInTheTubeUntilItIsHandled()
	{
		var hud = Hud();
		hud.PostGameMessage( "stay" );
		hud.PostGameMessage( "  " );
		Assert.AreEqual( 1, hud.PendingMessages.Count, "blank text is no message" );
	}

	[TestMethod]
	public void AMissingOrUndecodableCursorKeepsTheSystemPointer()
	{
		Assert.IsFalse( ParkHud.ShouldDrawSoftwareCursor( null, true, false ), "no art, no game pointer" );
		Assert.IsFalse( ParkHud.ShouldDrawSoftwareCursor( "/x/Cnor.tga", false, false ), "outside the window" );
		Assert.IsFalse( ParkHud.ShouldDrawSoftwareCursor( "/x/Cnor.tga", true, true ), "an ImGui panel wants the mouse" );
		Assert.IsTrue( ParkHud.ShouldDrawSoftwareCursor( "/x/Cnor.tga", true, false ) );
	}

	[TestMethod]
	public void OpeningMessagesOneAfterAnotherShowsEachInTurn()
	{
		var hud = Hud();
		hud.PostGameMessage( "a" );
		hud.PostGameMessage( "b" );
		hud.OpenPendingMessage( 0 );
		hud.OpenPendingMessage( 0 );
		Assert.AreEqual( "b", hud.OpenedMessage );
		Assert.AreEqual( 0, hud.PendingMessages.Count );
	}

	[TestMethod]
	public void TheSkillGaugeIsTheGradeOverFive()
	{
		Assert.IsNull( ParkHud.SkillGauge( null ) );
		Assert.AreEqual( 0f, ParkHud.SkillGauge( new StaffCandidate( 1, StaffType.Guard, 0, 0, 0 ) )!.Value, 1e-6f );
		// the video's guard candidates (12:00, Easy_Standard wages 3, 4, 5, 7 x 15): $60 is grade 1 and shows a fifth, $75 grade 2 and $105 grade 3
		Assert.AreEqual( 204 / 1024f, ParkHud.SkillGauge( new StaffCandidate( 1, StaffType.Guard, 0, 1, 0 ) )!.Value, 1e-6f, "(1 << 10) / 5 of 1024" );
		Assert.AreEqual( 819 / 1024f, ParkHud.SkillGauge( new StaffCandidate( 1, StaffType.Guard, 0, 4, 0 ) )!.Value, 1e-6f );
	}

	[TestMethod]
	public void TheHelpBoxSitsAtTheBottomCentreOnTranslucentBlack()
	{
		var hud = Hud();
		Assert.AreEqual( 0, UiColors.HelpBox.R + UiColors.HelpBox.G + UiColors.HelpBox.B, "black" );
		Assert.IsTrue( UiColors.HelpBox.A is > 100 and < 255, "translucent" );
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { ResolveTexture = _ => null };
		context.HoverHelp = "AA";
		var stack = new UiScreenStack();
		stack.Draw( context, true );
		var box = context.Batch.Draws.SelectMany( draw => draw.Vertices ).Where( vertex => vertex.Color == UiColors.HelpBox ).ToArray();
		Assert.IsTrue( box.Length > 0, "the box is drawn" );
		Assert.IsTrue( box.Min( vertex => vertex.Position.Y ) > context.Canvas.Height / 2f, "in the lower half" );
		Assert.AreEqual( context.Canvas.Width / 2f, (box.Min( vertex => vertex.Position.X ) + box.Max( vertex => vertex.Position.X )) / 2f, 1f, "centred" );
		_ = hud;
	}
}
