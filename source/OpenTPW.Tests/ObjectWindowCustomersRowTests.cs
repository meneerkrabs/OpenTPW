using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The customers row of the shop and sideshow windows (tp.exe 0x4b01d0, 0x4b1914): UITEXT record 0x1CC, "{0x19} of {0x1A}" with the customers and the customers plus the walk-aways (ECON-057).
/// </summary>
[TestClass]
public class ObjectWindowCustomersRowTests
{
	private static UiStringTable Table( params StringTemplatePart[] parts ) =>
		new( "Test", new string[ParkHud.CustomersOfTotalRecord + 1], Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
			uiTemplates: Enumerable.Range( 0, ParkHud.CustomersOfTotalRecord + 1 ).Select( index => index == ParkHud.CustomersOfTotalRecord ? new StringTemplate( parts ) : new StringTemplate( Array.Empty<StringTemplatePart>() ) ).ToArray() );

	[TestMethod]
	public void TheRowShowsTheCustomersOfTheCustomersPlusTheWalkAways()
	{
		var strings = Table( new StringTemplatePart( "", 0x19 ), new StringTemplatePart( " of ", -1 ), new StringTemplatePart( "", 0x1A ) );
		Assert.AreEqual( "12 of 17", ParkHud.CustomersText( strings, 12, 5 ) );
		Assert.AreEqual( "0 of 0", ParkHud.CustomersText( strings, 0, 0 ) );
	}

	[TestMethod]
	public void AnotherLanguagesSlotOrderKeepsEachNumberInItsPlaceBecauseTheFormatterMatchesByType()
	{
		var strings = Table( new StringTemplatePart( "von ", -1 ), new StringTemplatePart( "", 0x1A ), new StringTemplatePart( ": ", -1 ), new StringTemplatePart( "", 0x19 ) );
		Assert.AreEqual( "von 17: 12", ParkHud.CustomersText( strings, 12, 5 ) );
	}

	[TestMethod]
	public void TheEnglishRecordIsTheOnlyOneWithThoseTextVariables()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !Directory.Exists( dataPath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original UITEXT.str." );
		var original = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		try
		{
			var file = LanguageTests.OriginalLanguagePublic( "English" ).LoadStrings( "UITEXT.str" );
			var holders = Enumerable.Range( 0, file.Templates.Length ).Where( index => file.Templates[index].Parts.Any( part => part.IsParameter && part.ParameterType is 0x19 or 0x1A ) ).ToList();
			CollectionAssert.AreEqual( new[] { ParkHud.CustomersOfTotalRecord }, holders, "record 0x1CC, one-based" );
			Assert.AreEqual( "12 of 17", ParkHud.CustomersText( UiStringTable.Load( LanguageTests.OriginalLanguagePublic( "English" ) ), 12, 5 ) );
		}
		finally
		{
			FileSystem = original;
		}
	}
}
