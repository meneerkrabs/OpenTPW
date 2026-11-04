using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The P window's layer lies over the windows that existed when it was made and under every later one (PATH-017, tp.exe 0x47ED80, 0x65DD7B).</summary>
[TestClass]
public class PauseWindowLayerTests
{
	[TestMethod]
	public void TheLayerIsDrawnAboveTheEarlierScreensAndBelowTheLaterOnes()
	{
		var order = new List<string>();
		UiScreen Screen( string name ) => new( name ) { DrawBackground = _ => order.Add( name ) };
		var hud = Screen( "hud" );
		var window = Screen( "window" );
		var stack = new UiScreenStack();
		stack.Push( hud );
		stack.Push( window );
		stack.LayerBelow = stack.Screens.ToArray();
		stack.LayerDraw = _ => order.Add( "layer" );
		stack.Push( Screen( "menu" ) );
		var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) ) ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { ResolveTexture = _ => null };
		stack.Draw( context, false );
		CollectionAssert.AreEqual( new[] { "hud", "window", "layer", "menu" }, order );
		order.Clear();
		stack.Pop();
		stack.Draw( context, false );
		CollectionAssert.AreEqual( new[] { "hud", "window", "layer" }, order, "the layer stays over what it covered" );
	}

	[TestMethod]
	public void TheBannerTableIsTheOriginalsFourStepsAndAJump()
	{
		var banner = OriginalLayoutTables.PauseWindow.Root;
		Assert.AreEqual( (174f, 500f, 1700f, 400f), (banner.Rect.X, banner.Rect.Y, banner.Rect.Width, banner.Rect.Height) );
		Assert.AreEqual( "s_paused", banner.Model );
		var animation = new UiWindowAnimation( UiWindowAnimation.PauseBanner, UiWindowAnimation.PauseBannerInterval, 0 );
		animation.Update( 36 );
		Assert.AreEqual( (-2, -6, 2, 2), animation.Offset );
	}
}
