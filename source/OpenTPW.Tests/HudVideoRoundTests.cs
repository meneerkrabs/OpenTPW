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

	[TestMethod]
	public void TheGaugeGradientRunsFromRedToGreen()
	{
		var red = UiGauge.ColourAt( 0 );
		var green = UiGauge.ColourAt( 1 );
		Assert.IsTrue( red.R > 200 && red.G < 80, "the low end is red" );
		Assert.IsTrue( green.G > 180 && green.R < 150, "the high end is green" );
		var middle = UiGauge.ColourAt( 0.45f );
		Assert.IsTrue( middle.R > 200 && middle.G > 190, "the middle is yellow" );
	}

	[TestMethod]
	public void AGaugeFillsFromTheLeftAndTheEmptyTrackHasNoFill()
	{
		var batch = new UiBatch();
		UiGauge.Draw( batch, new UiRect( 0, 0, 100, 10 ), null );
		Assert.AreEqual( 6, batch.Draws.Sum( draw => draw.Vertices.Count ), "only the track quad (two triangles)" );
		batch.Clear();
		UiGauge.Draw( batch, new UiRect( 0, 0, 100, 10 ), 0.5f );
		var maximumX = batch.Draws.SelectMany( draw => draw.Vertices ).Max( vertex => vertex.Position.X );
		Assert.AreEqual( 100f, maximumX, 0.01f, "the track spans the whole width" );
		var fill = batch.Draws.SelectMany( draw => draw.Vertices ).Where( vertex => vertex.Color != UiGauge.Track ).Max( vertex => vertex.Position.X );
		Assert.AreEqual( 50f, fill, 0.01f, "half the value fills half the track" );
	}

	[TestMethod]
	public void TheMessageTubeHoldsFiveMessagesAndOpeningOrDeletingRemovesThem()
	{
		var hud = Hud();
		for ( var index = 0; index < 7; index++ )
			hud.PostGameMessage( $"m{index}" );
		Assert.AreEqual( ParkHud.MessageTubeCapacity, hud.PendingMessages.Count );
		Assert.AreEqual( "m2", hud.PendingMessages[0], "the oldest messages go when the tube is full" );
		Assert.IsTrue( hud.OpenPendingMessage( 1 ) );
		Assert.AreEqual( "m3", hud.OpenedMessage, "a left click opens that message" );
		Assert.AreEqual( 4, hud.PendingMessages.Count, "and it leaves the tube" );
		Assert.IsTrue( hud.DeletePendingMessage( 0 ) );
		Assert.AreEqual( 3, hud.PendingMessages.Count );
		Assert.AreEqual( "m3", hud.OpenedMessage, "deleting a pending message leaves the open one alone" );
		Assert.IsFalse( hud.DeletePendingMessage( 9 ) );
		hud.CloseMessagePanel();
		Assert.IsNull( hud.OpenedMessage );
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
	public void TheSkillGaugeIsTheGradeOverFive()
	{
		Assert.IsNull( ParkHud.SkillGauge( null ) );
		Assert.AreEqual( 0.2f, ParkHud.SkillGauge( new StaffCandidate( 1, StaffType.Guard, 0, 0, 0 ) )!.Value, 1e-6f );
		Assert.AreEqual( 1f, ParkHud.SkillGauge( new StaffCandidate( 1, StaffType.Guard, 0, 4, 0 ) )!.Value, 1e-6f );
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
