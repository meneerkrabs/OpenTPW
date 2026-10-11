using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class LanguageSettingsTests
{
	[TestMethod]
	public void TheChoiceIsKeptBesideTheOtherSettings()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-language-{Guid.NewGuid():N}" );
		try
		{
			var path = Path.Combine( directory, LanguageSettings.FileName );
			var diagnostics = new List<string>();
			Assert.AreEqual( new LanguageSettings(), LanguageSettings.Load( path, diagnostics ), "no file is no choice" );
			new LanguageSettings( "Dutch", "/cd" ).Save( path );
			Assert.AreEqual( new LanguageSettings( "Dutch", "/cd" ), LanguageSettings.Load( path, diagnostics ) );
			File.WriteAllText( path, "{ \"Language\": \"  \", }" );
			Assert.AreEqual( new LanguageSettings(), LanguageSettings.Load( path, diagnostics ), "blank values are no choice" );
			File.WriteAllText( path, "not json" );
			Assert.AreEqual( new LanguageSettings(), LanguageSettings.Load( path, diagnostics ) );
			Assert.AreEqual( 1, diagnostics.Count );
			StringAssert.EndsWith( LanguageSettings.GetDefaultPath(), Path.Combine( "OpenTPW", LanguageSettings.FileName ) );
		}
		finally
		{
			if ( Directory.Exists( directory ) )
				Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public void CommandLineThenEnvironmentThenFileThenOldSetting()
	{
		Assert.AreEqual( "German", LanguageSettings.Choose( "German", "French", "Dutch", "Danish" ) );
		Assert.AreEqual( "French", LanguageSettings.Choose( null, "French", "Dutch", "Danish" ) );
		Assert.AreEqual( "Dutch", LanguageSettings.Choose( null, " ", "Dutch", "Danish" ) );
		Assert.AreEqual( "Danish", LanguageSettings.Choose( null, null, "", "Danish" ) );
		Assert.IsNull( LanguageSettings.Choose( null, null, null, "" ) );
	}
}
