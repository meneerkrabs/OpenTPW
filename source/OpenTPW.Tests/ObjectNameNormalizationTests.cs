using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class ObjectNameNormalizationTests
{
	[DataTestMethod]
	[DataRow( "Café-Éclair 42!", "cafeeclair42" )]
	[DataRow( "Cafe\u0301-E\u0301clair 42!", "cafeeclair42" )]
	[DataRow( "Straße", "straße" )]
	[DataRow( "Søren", "søren" )]
	[DataRow( "水 2", "水2" )]
	[DataRow( "! --- ", "" )]
	public void CanonicalMatchingFoldsAccentsAndPunctuationButKeepsLetters( string input, string expected ) =>
		Assert.AreEqual( expected, ObjectNames.Normalize( input ) );

	[DataTestMethod]
	[DataRow( "ﬃ", "ffi" )]
	[DataRow( "Ｂｏｂ", "Bob" )]
	[DataRow( "Straße", "Strasse" )]
	public void CatalogMatchingDoesNotApplyOnlineCompatibilityIdentity( string first, string second ) =>
		Assert.AreNotEqual( ObjectNames.Normalize( first ), ObjectNames.Normalize( second ) );
}
