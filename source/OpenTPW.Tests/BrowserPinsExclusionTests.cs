using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The browser determinism job (docs/DETERMINISM.md, "Browser and native") compiles every test file into the page except the ones
/// OpenTPW.Web.csproj lists as DeterminismPinsExcludedTests. A test file that uses the server or ASP.NET Core does not compile for the
/// browser, nor does one that uses a type only an excluded file declares (its helpers), so it must be on that list; this test catches a missing
/// entry in the ordinary desktop run instead of in the browser job.
/// </summary>
[TestClass]
public class BrowserPinsExclusionTests
{
	[TestMethod]
	public void EveryTestFileTheBrowserPinsCannotCompileIsExcluded()
	{
		var repository = RepositoryFolder();
		var tests = Path.Combine( repository, "source", "OpenTPW.Tests" );
		var project = File.ReadAllText( Path.Combine( repository, "source", "OpenTPW.Web", "OpenTPW.Web.csproj" ) );
		var excluded = Regex.Matches( project, "<DeterminismPinsExcludedTests Include=\"../OpenTPW.Tests/([^\"]+)\"" )
			.Select( match => match.Groups[1].Value ).ToHashSet( StringComparer.Ordinal );
		var excludedTypes = excluded.SelectMany( name => TopLevelTypes( File.ReadAllText( Path.Combine( tests, name ) ) ) ).ToHashSet( StringComparer.Ordinal );
		var missing = Directory.EnumerateFiles( tests, "*.cs", SearchOption.TopDirectoryOnly )
			.Where( path => !excluded.Contains( Path.GetFileName( path ) ) && CannotCompileForTheBrowser( File.ReadAllText( path ), excludedTypes ) )
			.Select( Path.GetFileName )
			.OrderBy( name => name, StringComparer.Ordinal )
			.ToArray();
		Assert.AreEqual( 0, missing.Length, $"add to DeterminismPinsExcludedTests in OpenTPW.Web.csproj: {string.Join( ", ", missing )}" );
	}

	[TestMethod]
	public void TheGuardReadsTheCodeAndTheTopLevelTypesOnly()
	{
		var excludedFile = """
			namespace OpenTPW.Tests;
			// internal sealed class Commented { }
			internal sealed class Helper<T> where T : class
			{
				private sealed record Nested( string Text = "class Quoted" );
			}
			internal readonly record struct Other( int Value );
			""";
		CollectionAssert.AreEqual( new[] { "Helper", "Other" }, TopLevelTypes( excludedFile ).ToArray() );
		CollectionAssert.AreEqual( new[] { "Outer" }, TopLevelTypes( "namespace OpenTPW.Tests { public class Outer { class Inner { } } }" ).ToArray() );

		var types = new HashSet<string>( StringComparer.Ordinal ) { "Helper" };
		foreach ( var use in new[] { "var made = new Helper<int>();", "var name = nameof( Helper );", "sealed class Mine : Helper<string> { }", "using static OpenTPW.Tests.Helper;",
			"var value = Helper.Value;", "using OpenTPW.Server;", "using Microsoft.AspNetCore.Http;" } )
			Assert.IsTrue( CannotCompileForTheBrowser( use, types ), use );
		foreach ( var mention in new[] { "// see Helper.cs\nvar count = 1;", "/* Helper */ var count = 1;", "var text = \"Helper.cs\";", "var path = @\"C:\\Helper\";",
			"var json = \"\"\"\n{ \"Helper\": 1 }\n\"\"\";", "var quote = '\"'; var helper = HelperCount;" } )
			Assert.IsFalse( CannotCompileForTheBrowser( mention, types ), mention );
	}

	/// <summary>
	/// Whether the browser page cannot compile a test file: it uses the server or ASP.NET Core, or names a type only an excluded file declares (in any use: a member, <c>new</c>,
	/// <c>typeof</c>, <c>nameof</c>, a base type, <c>using static</c>). A name in a comment or a string literal is no use.
	/// </summary>
	private static bool CannotCompileForTheBrowser( string text, IReadOnlySet<string> excludedTypes )
	{
		var code = CodeOnly( text );
		return Regex.IsMatch( code, @"^\s*using\s+(static\s+)?(OpenTPW\.Server|Microsoft\.AspNetCore)\b", RegexOptions.Multiline )
			|| Regex.Matches( code, @"\b[A-Za-z_]\w*\b" ).Any( word => excludedTypes.Contains( word.Value ) );
	}

	/// <summary>The types a C# file declares at namespace level: a nested type is only reached through its outer one.</summary>
	private static IEnumerable<string> TopLevelTypes( string text )
	{
		var code = CodeOnly( text );
		var namespaceDepth = Regex.IsMatch( code, @"\bnamespace\s+[\w.]+\s*\{" ) ? 1 : 0;
		foreach ( Match match in Regex.Matches( code, @"\b(?:class|struct|record|interface|enum)\s+(?!(?:class|struct|where|new)\b)([A-Za-z_]\w*)" ) )
		{
			var before = code.AsSpan( 0, match.Index );
			if ( before.Count( '{' ) - before.Count( '}' ) == namespaceDepth )
				yield return match.Groups[1].Value;
		}
	}

	/// <summary>A C# file's code without its comments and its string and character literals (regular, verbatim and raw).</summary>
	private static string CodeOnly( string text )
	{
		var code = new StringBuilder( text.Length );
		var index = 0;
		while ( index < text.Length )
		{
			var current = text[index];
			var next = index + 1 < text.Length ? text[index + 1] : '\0';
			if ( current == '/' && next == '/' )
			{
				while ( index < text.Length && text[index] != '\n' )
					index++;
			}
			else if ( current == '/' && next == '*' )
			{
				var end = text.IndexOf( "*/", index + 2, StringComparison.Ordinal );
				index = end < 0 ? text.Length : end + 2;
				code.Append( ' ' );
			}
			else if ( current == '"' )
			{
				index = AfterString( text, index );
				code.Append( "\"\"" );
			}
			else if ( current == '\'' )
			{
				index = AfterCharacter( text, index );
				code.Append( "' '" );
			}
			else
			{
				code.Append( current );
				index++;
			}
		}
		return code.ToString();
	}

	/// <summary>The index after the string literal whose first quote is at <paramref name="start"/>: a raw one (three or more quotes), a verbatim one (<c>@</c>, doubled quotes) or a regular one.</summary>
	private static int AfterString( string text, int start )
	{
		var quotes = 0;
		while ( start + quotes < text.Length && text[start + quotes] == '"' )
			quotes++;
		if ( quotes >= 3 )
		{
			var end = text.IndexOf( new string( '"', quotes ), start + quotes, StringComparison.Ordinal );
			return end < 0 ? text.Length : end + quotes;
		}
		var verbatim = (start > 0 && text[start - 1] == '@') || (start > 1 && text[start - 2] == '@');
		var index = start + 1;
		while ( index < text.Length )
		{
			if ( verbatim && text[index] == '"' && index + 1 < text.Length && text[index + 1] == '"' )
				index += 2;
			else if ( !verbatim && text[index] == '\\' )
				index += 2;
			else if ( text[index++] == '"' )
				return index;
		}
		return text.Length;
	}

	/// <summary>The index after the character literal whose quote is at <paramref name="start"/>.</summary>
	private static int AfterCharacter( string text, int start )
	{
		var index = start + 1;
		while ( index < text.Length )
		{
			if ( text[index] == '\\' )
				index += 2;
			else if ( text[index++] == '\'' )
				return index;
		}
		return text.Length;
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
