using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online;
using OpenTPW.UI;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The online menu's button labels in every UI language's own fonts (the native front-end smoke checks the same on one language per run).</summary>
[TestClass]
public class OnlineMenuLabelFitTests
{
	/// <summary>
	/// Every online menu button label, logged in or out, keeps the menu's own font family in every UI language (the localisation pack
	/// <c>OPENTPW_LANGUAGE_PACK</c> over <c>OPENTPW_GAME_PATH</c>) at the smoke's 1280x720 and at larger screens: French "Boîte de réception", Swedish
	/// "Logga in på TPW", Danish "Læg park ud på nettet", Polish "Usuń konto" and Hebrew lamed put their marks in the rows the MENU fonts keep above
	/// their capitals, which once dropped those labels to a plain body font, and Hebrew's login line is the widest label.
	/// </summary>
	[TestMethod]
	public void EveryLabelKeepsTheMenuFontFamilyInEveryLanguage()
	{
		var pack = Environment.GetEnvironmentVariable( "OPENTPW_LANGUAGE_PACK" );
		var game = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		var data = string.IsNullOrWhiteSpace( game ) ? null : GameLanguage.FindEntry( game, "Data", true );
		if ( string.IsNullOrWhiteSpace( pack ) || !Directory.Exists( pack ) || data == null )
			Assert.Inconclusive( "Set OPENTPW_LANGUAGE_PACK and OPENTPW_GAME_PATH for the languages' own fonts." );
		var previous = GameLanguage.IsSelected ? GameLanguage.Current : null;
		var failures = new List<string>();
		try
		{
			foreach ( var name in SupplementaryStrings.SupportedLanguages )
			{
				var language = GameLanguage.Resolve( data!, name, pack );
				GameLanguage.Current = language;
				var fonts = UiFonts.Load( language );
				var window = UiDialogs.CenteredWindow( OnlineScreens.MenuWindowWidth, OnlineScreens.MenuWindowHeight );
				foreach ( var (width, height, density) in new[] { (1280, 720, 1), (1920, 1080, 1), (2560, 1440, 1), (3024, 1686, 2) } )
				{
					var context = new UiContext( OriginalUiTests.FakeStrings(), fonts, new UiModels( model => throw new FileNotFoundException( model ) ) )
					{
						Canvas = new UiCanvas( width, height, UiScaling.Automatic( new Point2( width, height ), density ), density )
					};
					var family = context.Fonts.Smaller( context.Fonts.Menu ).Prepend( context.Fonts.Menu ).ToList();
					for ( var index = 0; index < OnlineScreens.MenuEntries.Length; index++ )
					{
						var button = new UiButton { Bounds = OnlineScreens.MenuButtonBounds( window, index ), Anchor = UiAnchor.Center };
						var box = UiButton.PurpleLabelRect( button.ScreenRect( context.Canvas ) );
						foreach ( var label in new[] { false, true }.Select( OnlineScreens.MenuEntries[index].Label ).Distinct() )
						{
							context.DrawFittedText( context.Fonts.Menu, label, box, Veldrid.RgbaByte.White );
							var draw = context.Batch.TextDraws.Last( entry => entry.Text == label );
							if ( !family.Any( font => ReferenceEquals( font, context.Batch.Glyphs[draw.FirstGlyph].Atlas ) ) )
								failures.Add( $"{name} at {width}x{height}: {OnlineScreens.MenuEntries[index].Id} '{label}' ({context.Measure( context.Fonts.Menu, label ).Width} px in a {box.Width:0}x{box.Height:0} box)" );
						}
					}
				}
			}
		}
		finally
		{
			GameLanguage.Current = previous!;
		}
		Assert.AreEqual( 0, failures.Count, string.Join( Environment.NewLine, failures ) );
	}
}
