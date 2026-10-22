using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>The window property-table animation (tp.exe 0x65E72D, 0x65E9A4) and the winning banner it drives (table 0x74FCF8).</summary>
[TestClass]
public class UiWindowAnimationTests
{
	[TestMethod]
	public void TheWinningBannerTableIsTheOriginalsTwentyNineSteps()
	{
		var table = UiWindowAnimation.WinningBanner;
		Assert.AreEqual( 29, table.Count );
		Assert.IsTrue( table.Take( 6 ).All( step => step == UiWindowAnimation.Step.Edges( 16, 0, -16, 0 ) ) );
		Assert.IsTrue( table.Skip( 6 ).Take( 4 ).All( step => step == UiWindowAnimation.Step.Edges( 0, -4, 0, -4 ) ) );
		CollectionAssert.AreEqual( Enumerable.Range( 1, 8 ).Select( turn => (int)(short)(512 * turn) ).ToArray(), table.Skip( 10 ).Take( 8 ).Select( step => (int)step.Second ).ToArray() );
		Assert.IsTrue( table.Skip( 10 ).Take( 8 ).All( step => step.First == UiWindowAnimation.SetAngle ) );
		Assert.IsTrue( table.Skip( 18 ).Take( 4 ).All( step => step == UiWindowAnimation.Step.Edges( 0, 4, 0, 4 ) ) );
		Assert.IsTrue( table.Skip( 22 ).Take( 6 ).All( step => step == UiWindowAnimation.Step.Edges( -16, 0, 16, 0 ) ) );
		Assert.AreEqual( UiWindowAnimation.Step.JumpTo( 0 ), table[28] );
		Assert.AreEqual( 35, UiWindowAnimation.WinningBannerInterval );
	}

	[TestMethod]
	public void AStepRunsOnlyWhenStrictlyMoreThanTheIntervalHasPassedSinceTheLast()
	{
		var animation = new UiWindowAnimation( UiWindowAnimation.WinningBanner, 35, 1000 );
		Assert.IsFalse( animation.Update( 1035 ), "35 ms is not more than the interval" );
		Assert.IsTrue( animation.Update( 1036 ) ); Assert.AreEqual( (16, 0, -16, 0), animation.Offset );
		Assert.IsFalse( animation.Update( 1036 ), "a second update at the same time runs nothing" );
		Assert.IsFalse( animation.Update( 1071 ) );
		Assert.IsTrue( animation.Update( 1100 ) ); Assert.AreEqual( (32, 0, -32, 0), animation.Offset );
	}

	[TestMethod]
	public void OneLoopSqueezesRisesSpinsFallsStretchesAndTheJumpTakesAStepOfItsOwn()
	{
		var animation = new UiWindowAnimation( UiWindowAnimation.WinningBanner, 35, 0 );
		long now = 0;
		void Step() => Assert.IsTrue( animation.Update( now += 36 ) );
		for ( var index = 0; index < 6; index++ ) Step();
		Assert.AreEqual( (96, 0, -96, 0), animation.Offset );
		for ( var index = 0; index < 4; index++ ) Step();
		Assert.AreEqual( (96, -16, -96, -16), animation.Offset );
		for ( var index = 0; index < 8; index++ ) { Step(); Assert.AreEqual( 512 * (index + 1), animation.Angle ); }
		for ( var index = 0; index < 10; index++ ) Step();
		Assert.AreEqual( (0, 0, 0, 0), animation.Offset ); Assert.AreEqual( 28, animation.Index );
		Step(); Assert.AreEqual( 0, animation.Index, "the jump only sets the index" ); Assert.AreEqual( (0, 0, 0, 0), animation.Offset );
		Step(); Assert.AreEqual( (16, 0, -16, 0), animation.Offset, "the next loop starts with the next update" );
		Assert.AreEqual( 4096, animation.Angle, "the angle stays at a whole turn until the table sets another" );
		Assert.IsTrue( animation.Active );
	}

	[TestMethod]
	public void AnEndStepStopsAndMovesTheEdgesBack()
	{
		var animation = new UiWindowAnimation( new[] { UiWindowAnimation.Step.Edges( 5, 6, 7, 8 ), new UiWindowAnimation.Step( UiWindowAnimation.End, 0, 0, 0 ) }, 0, 0 );
		Assert.IsTrue( animation.Update( 1 ) ); Assert.AreEqual( (5, 6, 7, 8), animation.Offset );
		Assert.IsTrue( animation.Update( 2 ) ); Assert.AreEqual( (0, 0, 0, 0), animation.Offset ); Assert.IsFalse( animation.Active ); Assert.AreEqual( 0, animation.Index );
		Assert.IsFalse( animation.Update( 100 ) );
	}

	[TestMethod]
	public void TablesOutsideThePortedCommandsAreRefused()
	{
		Assert.ThrowsException<ArgumentException>( () => new UiWindowAnimation( Array.Empty<UiWindowAnimation.Step>(), 35, 0 ) );
		Assert.ThrowsException<ArgumentException>( () => new UiWindowAnimation( new[] { UiWindowAnimation.Step.Edges( 1, 1, 1, 1 ) }, 35, 0 ), "a table must end or loop" );
		Assert.ThrowsException<ArgumentException>( () => new UiWindowAnimation( new[] { new UiWindowAnimation.Step( UiWindowAnimation.Command, 3, 0, 0 ), UiWindowAnimation.Step.JumpTo( 0 ) }, 35, 0 ) );
		Assert.ThrowsException<ArgumentException>( () => new UiWindowAnimation( new[] { UiWindowAnimation.Step.JumpTo( 5 ) }, 35, 0 ) );
	}

	[TestMethod]
	public void TheBannerTurnsClockwiseAboutHalfItsRightAndBottomEdges()
	{
		var rect = OriginalLayoutTables.WinningScreen.Root.Rect;
		Assert.AreEqual( new N.Vector2( 973, 470 ), UiAnimatedModel.PivotOf( rect ) );
		var banner = new UiAnimatedModel { Bounds = rect, Animation = new UiWindowAnimation( UiWindowAnimation.WinningBanner, 35, 0 ) };
		Assert.AreEqual( rect, banner.AnimatedBounds );
		for ( var now = 36; banner.Animation.Index < 11; now += 36 ) banner.Animation.Update( now );
		Assert.AreEqual( new UiRect( 198, 520, 1652, 404 ), banner.AnimatedBounds );
		Assert.AreEqual( MathF.PI / 4, banner.Turn, 1e-6f );
		// a point right of the pivot moves down on the screen: clockwise
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!, UiModels.FromAssets( Array.Empty<UiModel>() ) ) { Canvas = new UiCanvas( 2048, 1536 ), ResolveTexture = _ => null };
		context.DrawFrame( Square(), new UiRect( 1000, 470, 2, 2 ), null, MathF.PI / 2, new N.Vector2( 973, 470 ) );
		var vertices = context.Batch.Draws.SelectMany( draw => draw.Vertices ).Select( vertex => vertex.Position ).ToArray();
		Assert.IsTrue( vertices.All( at => Math.Abs( at.X - 973 ) < 3 && at.Y > 495 ), string.Join( " ", vertices ) );
	}

	private static UiModelFrame Square()
	{
		var vertices = new[] { new UiModelVertex( new N.Vector2( 0, 0 ), 0, default ), new UiModelVertex( new N.Vector2( 2, 0 ), 0, default ), new UiModelVertex( new N.Vector2( 0, 2 ), 0, default ) };
		return new UiModelFrame( "square", new[] { new UiModelPart( "", vertices, new[] { 0, 1, 2 } ) }, 0, 0, 2, 2 );
	}
}
