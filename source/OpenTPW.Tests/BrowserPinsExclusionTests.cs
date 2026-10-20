using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The browser determinism job (docs/DETERMINISM.md, "Browser and native") compiles every test file into the page except the ones
/// OpenTPW.Web.csproj lists as DeterminismPinsExcludedTests. A test file that uses the server or ASP.NET Core does not compile for the
/// browser, so it must be on that list; this test catches a missing entry in the ordinary desktop run instead of in the browser job.
/// </summary>
[TestClass]
public class BrowserPinsExclusionTests
{
	[TestMethod]
	public void EveryTestFileThatUsesTheServerIsExcludedFromTheBrowserPins()
	{
		var repository = RepositoryFolder();
		var project = File.ReadAllText( Path.Combine( repository, "source", "OpenTPW.Web", "OpenTPW.Web.csproj" ) );
		var excluded = Regex.Matches( project, "<DeterminismPinsExcludedTests Include=\"../OpenTPW.Tests/([^\"]+)\"" )
			.Select( match => match.Groups[1].Value ).ToHashSet( StringComparer.Ordinal );
		var serverOnly = new Regex( @"^\s*using\s+(OpenTPW\.Server|Microsoft\.AspNetCore)\b", RegexOptions.Multiline );
		var missing = Directory.EnumerateFiles( Path.Combine( repository, "source", "OpenTPW.Tests" ), "*.cs", SearchOption.TopDirectoryOnly )
			.Where( path => serverOnly.IsMatch( File.ReadAllText( path ) ) )
			.Select( Path.GetFileName )
			.Where( name => !excluded.Contains( name! ) )
			.OrderBy( name => name, StringComparer.Ordinal )
			.ToArray();
		Assert.AreEqual( 0, missing.Length, $"add to DeterminismPinsExcludedTests in OpenTPW.Web.csproj: {string.Join( ", ", missing )}" );
	}

	private static string RepositoryFolder()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			if ( File.Exists( Path.Combine( directory.FullName, "source", "OpenTPW.Web", "OpenTPW.Web.csproj" ) ) )
				return directory.FullName;
		}
		throw new FileNotFoundException( "source/OpenTPW.Web not found above the test binaries." );
	}
}
