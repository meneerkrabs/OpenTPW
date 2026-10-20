using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class ChallengeMessageTextTests
{
	// Independent type-ordered expectations: progress tag, offer entry, formatter family.
	// All four records are checked, including the four exceptional offer replacements.
	private static readonly (int Tag, int Entry, int Family)[] expectedTypes =
	{
		(280, 192, 0), (281, 196, 0), (282, 200, 0), (283, 204, 0), (284, 208, 0), (285, 212, 0),
		(286, 216, 0), (287, 220, 0), (291, 236, 0), (292, 240, 0), (293, 244, 0), (297, 260, 0),
		(298, 264, 0), (300, 276, 0), (301, 280, 0), (303, 288, 0), (288, 224, 1), (306, 304, 1),
		(307, 308, 1), (308, 312, 1), (309, 316, 1), (312, 328, 1), (289, 228, 2), (290, 232, 2),
		(296, 256, 2), (299, 268, 2), (295, 252, 3), (302, 284, 1), (304, 292, 4), (305, 296, 5),
		(310, 320, 5), (311, 324, 5), (294, 248, 6)
	};

	private static ChallengeMessageRecord Expected( int type, ChallengePresentationKind kind )
	{
		var row = expectedTypes[type - 1];
		var offer = kind == ChallengePresentationKind.Offer;
		var replacement = offer ? type switch { 19 => 306, 20 => 307, 31 => 309, 22 => 312, _ => 383 } : row.Tag;
		return new( row.Tag + ((int)kind - 1) * 33, replacement, 4, row.Entry + (int)kind,
			9 + 7 * (int)kind + row.Family, offer ? 26 : 0, offer ? 27 : 0,
			offer ? 90000 : kind == ChallengePresentationKind.Progress ? 0 : 30000 );
	}

	private static ChallengePresentation Payload( int type = 1, ChallengePresentationKind kind = ChallengePresentationKind.Progress ) =>
		new( kind, type, 31, 22, 13, 44, 55, 61, 72, 2 );

	private static StringFile Table( int count = 332 ) => new( new MemoryStream( StringTableTests.CreateStringTable(
		Enumerable.Range( 0, count ).Select( _ => Array.Empty<byte>() ).ToArray() ) ),
		new BFMUReader( new MemoryStream( StringTableTests.CreateCharacterTable( "x" ) ) ) );

	private static StringTemplatePart Literal( string text ) => new( text, -1 );
	private static StringTemplatePart Parameter( int id ) => new( "", id );

	private static StringFile Template( ChallengePresentation value, params StringTemplatePart[] parts )
	{
		var table = Table();
		table.Templates[Expected( value.Type, value.Kind ).Entry] = new( parts );
		return table;
	}

	private static bool Format( StringFile? table, ChallengePresentation value, out string text ) =>
		ChallengeMessageText.TryFormat( table, value, id => "object-" + id, id => "staff-" + id, out text );

	[TestMethod]
	public void All132RecordsMatchEveryNumericPresentationField()
	{
		var tags = new HashSet<int>();
		foreach ( var kind in Enum.GetValues<ChallengePresentationKind>() )
			for ( var type = 1; type <= 33; type++ )
			{
				var expected = Expected( type, kind );
				Assert.IsTrue( ChallengeMessageRecords.TryGet( type, kind, out var record ), $"{kind}/{type}" );
				Assert.AreEqual( expected, record, $"{kind}/{type}" );
				Assert.AreEqual( (0, 0), record.Context );
				Assert.IsTrue( ChallengeMessageRecords.TryGetByTag( expected.Tag, out var byTag ) );
				Assert.AreEqual( expected, byTag, $"Tag lookup {expected.Tag}" );
				Assert.IsTrue( tags.Add( record.Tag ), "Every record has a distinct tag." );
			}
		Assert.AreEqual( 132, tags.Count );
		CollectionAssert.AreEquivalent( Enumerable.Range( 247, 132 ).ToArray(), tags.ToArray() );
	}

	[TestMethod]
	public void UnknownTypesKindsAndNoTextTagsHaveNoRecord()
	{
		foreach ( var type in new[] { int.MinValue, -1, 0, 34, 247, 383, int.MaxValue } )
			foreach ( var kind in Enum.GetValues<ChallengePresentationKind>() )
			{
				Assert.IsFalse( ChallengeMessageRecords.TryGet( type, kind, out var record ) );
				Assert.AreEqual( default, record );
			}
		foreach ( var kind in new[] { (ChallengePresentationKind)(-1), (ChallengePresentationKind)4, (ChallengePresentationKind)int.MaxValue } )
			Assert.IsFalse( ChallengeMessageRecords.TryGet( 1, kind, out _ ) );
		foreach ( var tag in new[] { int.MinValue, -1, 0, 246, 379, 382, 383, int.MaxValue } )
		{
			Assert.IsFalse( ChallengeMessageRecords.TryGetByTag( tag, out var record ) );
			Assert.AreEqual( default, record );
		}
	}

	[TestMethod]
	public void FourIncomingOffersReplaceTheirSpecificProgressTags()
	{
		var actual = new List<(int Tag, int Replacement)>();
		for ( var type = 1; type <= 33; type++ )
		{
			Assert.IsTrue( ChallengeMessageRecords.TryGet( type, ChallengePresentationKind.Offer, out var record ) );
			if ( record.ReplacementTag != ChallengeMessageRecords.NoText )
				actual.Add( (record.Tag, record.ReplacementTag) );
		}
		CollectionAssert.AreEquivalent( new[] { (274, 306), (275, 307), (277, 309), (279, 312) }, actual.ToArray() );
	}

	[TestMethod]
	public void TemplateArgumentsCanBeReorderedRepeatedAndOmitted()
	{
		var value = Payload( 17 );
		var table = Template( value, Parameter( 5 ), Literal( "/" ), Parameter( 6 ), Literal( "/" ), Parameter( 4 ),
			Literal( "/" ), Parameter( 3 ), Literal( "/" ), Parameter( 4 ) );
		var objectReads = 0;
		Assert.IsTrue( ChallengeMessageText.TryFormat( table, value, id => { objectReads++; return "attractie-" + id; },
			_ => throw new AssertFailedException( "This selector uses an object, not a staff entry." ), out var text ) );
		Assert.AreEqual( "55/attractie-61/44/31/44", text );
		Assert.AreEqual( 1, objectReads );
	}

	[TestMethod]
	public void UnusedArgumentsNeedNoNamesAndParameterFreeResultsAreValid()
	{
		foreach ( var kind in Enum.GetValues<ChallengePresentationKind>() )
		{
			var value = Payload( 33, kind );
			var table = Template( value, Literal( "Synthetic result." ) );
			Assert.IsTrue( ChallengeMessageText.TryFormat( table, value,
				_ => throw new AssertFailedException( "Unused names must not be resolved." ),
				_ => throw new AssertFailedException( "Unused staff must not be resolved." ), out var text ) );
			Assert.AreEqual( "Synthetic result.", text );
		}
	}

	private static int[] ExpectedIds( int family, ChallengePresentationKind kind ) => kind switch
	{
		ChallengePresentationKind.Success => new[] { 3 },
		ChallengePresentationKind.Failure => new[] { 1, 4 },
		ChallengePresentationKind.Offer when family == 6 => new[] { 2, 3, 6, 7 },
		ChallengePresentationKind.Offer when family is 0 or 2 => new[] { 1, 2, 3 },
		ChallengePresentationKind.Offer => new[] { 1, 2, 3, 6 },
		ChallengePresentationKind.Progress when family == 6 => new[] { 3, 5, 6, 7 },
		ChallengePresentationKind.Progress when family is 0 or 2 => new[] { 1, 3, 4, 5 },
		_ => new[] { 1, 3, 4, 5, 6 }
	};

	[TestMethod]
	public void All132SelectorsSupplyExactlyTheirKeyedArguments()
	{
		var table = Table();
		foreach ( var kind in Enum.GetValues<ChallengePresentationKind>() )
			for ( var type = 1; type <= 33; type++ )
			{
				var value = Payload( type, kind );
				var expected = Expected( type, kind );
				var family = expectedTypes[type - 1].Family;
				var ids = ExpectedIds( family, kind );
				foreach ( var id in Enumerable.Range( 0, 10 ).Concat( new[] { -2, int.MaxValue } ) )
				{
					table.Templates[expected.Entry] = new( new[] { Parameter( id ) } );
					var valid = Format( table, value, out var text );
					Assert.AreEqual( ids.Contains( id ), valid, $"{kind}/{type}, argument {id}" );
					var expectedText = id switch
					{
						1 => "13", 2 => "22", 3 => "31", 4 => "44", 5 => "55",
						6 when family is 3 or 4 => "staff-5", 6 => "object-61", 7 => "object-72", _ => ""
					};
					Assert.AreEqual( valid ? expectedText : "", text );
				}
			}
	}

	[TestMethod]
	public void ObjectNamesUseTheSelectedLanguageAndTheirCorrectIds()
	{
		var value = Payload( 33 );
		var table = Template( value, Parameter( 7 ), Literal( " / " ), Parameter( 6 ), Literal( " / " ), Parameter( 7 ) );
		var requested = new List<int>();
		Assert.IsTrue( ChallengeMessageText.TryFormat( table, value, id =>
		{
			requested.Add( id );
			return id == 61 ? "Attractie 🌊" : id == 72 ? "第二遊具" : null;
		}, _ => null, out var text ) );
		Assert.AreEqual( "第二遊具 / Attractie 🌊 / 第二遊具", text );
		CollectionAssert.AreEqual( new[] { 72, 61 }, requested );
	}

	[TestMethod]
	public void EveryStaffTypeResolvesItsLocalizedPluralEntry()
	{
		foreach ( var type in new[] { 27, 29 } )
			foreach ( var kind in new[] { ChallengePresentationKind.Offer, ChallengePresentationKind.Progress } )
				for ( var staffType = 0; staffType < 5; staffType++ )
				{
					var value = Payload( type, kind ) with { TargetStaffType = staffType };
					var table = Template( value, Parameter( 6 ), Literal( "/" ), Parameter( 6 ) );
					var requested = new List<int>();
					Assert.IsTrue( ChallengeMessageText.TryFormat( table, value,
						_ => throw new AssertFailedException( "Staff selectors must not resolve object names." ),
						entry => { requested.Add( entry ); return "組é-" + entry; }, out var text ) );
					var entry = staffType * 2 + 1;
					Assert.AreEqual( $"組é-{entry}/組é-{entry}", text );
					CollectionAssert.AreEqual( new[] { entry }, requested );
				}
	}

	[TestMethod]
	public void MissingRequiredNamesAndInvalidStaffTypesFailWithoutPartialText()
	{
		foreach ( var type in new[] { 17, 27, 29, 30, 33 } )
		{
			var value = Payload( type );
			var table = Template( value, Literal( "Prefix " ), Parameter( 6 ) );
			foreach ( var missing in new string?[] { null, "", "   " } )
			{
				Assert.IsFalse( ChallengeMessageText.TryFormat( table, value, _ => missing, _ => missing, out var text ) );
				Assert.AreEqual( "", text );
			}
		}
		var pair = Payload( 33 );
		Assert.IsFalse( ChallengeMessageText.TryFormat( Template( pair, Parameter( 6 ), Parameter( 7 ) ), pair,
			id => id == pair.TargetObject ? "First" : null, _ => null, out var pairText ) );
		Assert.AreEqual( "", pairText );
		foreach ( var staffType in new[] { int.MinValue, -1, 5, int.MaxValue } )
		{
			var value = Payload( 27 ) with { TargetStaffType = staffType };
			Assert.IsFalse( ChallengeMessageText.TryFormat( Template( value, Parameter( 6 ) ), value, _ => null,
				_ => throw new AssertFailedException( "Invalid staff types must not index the table." ), out var text ) );
			Assert.AreEqual( "", text );
		}
	}

	[TestMethod]
	public void MissingOrUnsupportedTemplatesFailExplicitly()
	{
		var value = Payload();
		Assert.IsFalse( Format( null, value, out var text ) );
		Assert.AreEqual( "", text );
		Assert.IsFalse( Format( Table( 193 ), value, out text ) );
		Assert.AreEqual( "", text );
		var table = Table();
		Assert.IsFalse( Format( table, value, out text ), "An empty entry cannot become a generic caption." );
		table.Templates[193] = null!;
		Assert.IsFalse( Format( table, value, out text ) );
		table.Templates[193] = new( Array.Empty<StringTemplatePart>() );
		Assert.IsFalse( Format( table, value, out text ) );
		table.Templates[193] = new( new[] { Literal( "Prefix" ), Parameter( 2 ) } );
		Assert.IsFalse( Format( table, value, out text ), "Progress does not supply target time." );
		Assert.AreEqual( "", text );
		Assert.IsFalse( Format( table, value with { Type = 0 }, out text ) );
		Assert.IsFalse( Format( table, value with { Type = 34 }, out text ) );
		Assert.IsFalse( Format( table, value with { Kind = (ChallengePresentationKind)4 }, out text ) );
	}

	[DataTestMethod]
	[DataRow( 0, "0" )]
	[DataRow( 1, "1" )]
	[DataRow( -1, "1" )]
	[DataRow( 1234, "1234" )]
	[DataRow( -1234, "1234" )]
	[DataRow( int.MinValue, "-2147483648" )]
	[DataRow( int.MaxValue, "2147483647" )]
	public void IntegersUseTheStoredMagnitudeWithSignedMinimumWrap( int number, string expected )
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			var culture = (CultureInfo)CultureInfo.GetCultureInfo( "fr-FR" ).Clone();
			culture.NumberFormat.NegativeSign = "negative";
			CultureInfo.CurrentCulture = culture;
			foreach ( var kind in Enum.GetValues<ChallengePresentationKind>() )
			{
				var value = Payload( 1, kind ) with { Prize = number, TargetTime = number, TargetValue = number, RawValue = number, DaysLeft = number };
				var ids = ExpectedIds( 0, kind );
				var parts = ids.SelectMany( id => new[] { Parameter( id ), Literal( "|" ) } ).ToArray();
				Assert.IsTrue( Format( Template( value, parts ), value, out var text ) );
				Assert.AreEqual( string.Concat( Enumerable.Repeat( expected + "|", ids.Length ) ), text );
			}
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	[TestMethod]
	public void OnlyTheTemplateAddsCurrencyAndUnitDecorations()
	{
		var value = Payload();
		var table = Template( value, Literal( "€ " ), Parameter( 3 ), Literal( "; " ), Parameter( 4 ), Literal( "% / " ),
			Parameter( 5 ), Literal( " dagen" ) );
		Assert.IsTrue( Format( table, value, out var text ) );
		Assert.AreEqual( "€ 31; 44% / 55 dagen", text );
	}

	[TestMethod]
	public void VisibleTextIsBoundedButAllArgumentsAreStillValidated()
	{
		var value = Payload();
		Assert.IsTrue( Format( Template( value, Literal( new string( 'x', 254 ) ), Parameter( 3 ), Literal( "tail" ) ), value, out var text ) );
		Assert.AreEqual( new string( 'x', 254 ) + "3", text );
		Assert.AreEqual( 255, text.Length );
		Assert.IsTrue( Format( Template( value, Literal( new string( 'x', 50000 ) ) ), value, out text ) );
		Assert.AreEqual( 255, text.Length );
		Assert.IsFalse( Format( Template( value, Literal( new string( 'x', 50000 ) ), Parameter( 7 ) ), value, out text ) );
		Assert.AreEqual( "", text, "An unsupported argument after truncation still invalidates the whole caption." );
		var named = Payload( 17 );
		Assert.IsFalse( ChallengeMessageText.TryFormat( Template( named, Literal( new string( 'x', 255 ) ), Parameter( 6 ) ),
			named, _ => null, _ => null, out text ) );
		Assert.AreEqual( "", text );
	}

	[TestMethod]
	public void ObjectNamesHaveTheirOwnBoundAndTruncationDoesNotSplitSurrogatePairs()
	{
		var named = Payload( 17 );
		Assert.IsTrue( ChallengeMessageText.TryFormat( Template( named, Parameter( 6 ), Literal( "!" ) ), named,
			_ => new string( 'a', 80 ), _ => null, out var text ) );
		Assert.AreEqual( new string( 'a', 63 ) + "!", text );
		Assert.IsTrue( ChallengeMessageText.TryFormat( Template( named, Parameter( 6 ), Literal( "!" ) ), named,
			_ => new string( 'a', 62 ) + "🌊", _ => null, out text ) );
		Assert.AreEqual( new string( 'a', 62 ) + "!", text );
		var value = Payload();
		Assert.IsTrue( Format( Template( value, Literal( new string( 'x', 254 ) + "🌊" ) ), value, out text ) );
		Assert.AreEqual( new string( 'x', 254 ), text );
	}

	[DataTestMethod]
	[DynamicData( nameof( LanguageTests.ShippedLanguages ), typeof( LanguageTests ) )]
	public void InstalledLanguageTemplatesBindAll132RecordsWithoutFallback( string languageName )
	{
		var language = LanguageTests.OriginalLanguagePublic( languageName );
		foreach ( var file in new[] { "TAG_SYSTEM.str", "OBJECT_NAMES.str", "STAFF_TYPES.str" } )
			if ( language.FindFile( file ) == null )
				Assert.Inconclusive( $"The installed {languageName} {file} table is unavailable." );
		var table = language.LoadStrings( "TAG_SYSTEM.str" );
		var objects = language.LoadStrings( "OBJECT_NAMES.str" ).Entries;
		var staff = language.LoadStrings( "STAFF_TYPES.str" ).Entries;
		var namedObjects = Enumerable.Range( 0, objects.Length ).Where( i => !string.IsNullOrWhiteSpace( objects[i] ) ).Take( 2 ).ToArray();
		Assert.AreEqual( 2, namedObjects.Length, "Two localized object names are required for the two-object challenge." );
		foreach ( var kind in Enum.GetValues<ChallengePresentationKind>() )
			for ( var type = 1; type <= 33; type++ )
			{
				var value = Payload( type, kind ) with { TargetObject = namedObjects[0], TargetObject2 = namedObjects[1] };
				Assert.IsTrue( ChallengeMessageText.TryFormat( table, value, id => id >= 0 && id < objects.Length ? objects[id] : null,
					entry => entry >= 0 && entry < staff.Length ? staff[entry] : null, out var text ), $"{languageName}: {kind}/{type}" );
				Assert.IsTrue( text.Length is > 0 and <= 255, $"{languageName}: {kind}/{type}" );
			}
	}
}
