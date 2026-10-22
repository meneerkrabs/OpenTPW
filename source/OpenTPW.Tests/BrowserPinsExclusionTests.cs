using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The browser determinism job (docs/DETERMINISM.md, "Browser and native") compiles every test file into the page except the ones
/// OpenTPW.Web.csproj lists as DeterminismPinsExcludedTests. A test file that uses the server or ASP.NET Core does not compile for the
/// browser, nor does one that uses a type only an excluded file declares (its helpers), nor one that uses a type or member only a game source file
/// declares that the page leaves out (its <c>Compile ... Exclude</c> list: Client/Game.cs, Client/SignExport.cs and the like), so it must be on that
/// list; this test catches a missing entry in the ordinary desktop run instead of in the browser job.
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
	/// The page compiles the game, Common and Files sources except the ones its <c>Compile</c> items exclude. A test that names a type only those
	/// excluded files declare (<c>SignExport</c>), or a member that only their part of a type declares while the browser has its own stand-in or other
	/// part (<c>Game.ResolveSaveDirectory</c> once Game is split), fails the pins build with CS0103 or CS0117.
	/// </summary>
	[TestMethod]
	public void EveryTestFileUsingADesktopOnlyGameFileIsExcluded()
	{
		var repository = RepositoryFolder();
		var tests = Path.Combine( repository, "source", "OpenTPW.Tests" );
		var web = Path.Combine( repository, "source", "OpenTPW.Web" );
		var projectPath = Path.Combine( web, "OpenTPW.Web.csproj" );
		var excluded = Regex.Matches( File.ReadAllText( projectPath ), "<DeterminismPinsExcludedTests Include=\"../OpenTPW.Tests/([^\"]+)\"" )
			.Select( match => match.Groups[1].Value ).ToHashSet( StringComparer.Ordinal );
		var (desktopFiles, browserFiles) = PageSources( web, XDocument.Load( projectPath ) );
		Assert.IsTrue( desktopFiles.Any( file => file.Name == "OpenTPW/Client/Game.cs" ), "the page's Compile Exclude list was not read" );
		var desktopOnly = DesktopOnly( desktopFiles.Select( file => (file.Name, File.ReadAllText( file.Path )) ), browserFiles.Select( File.ReadAllText ) );
		var missing = Directory.EnumerateFiles( tests, "*.cs", SearchOption.TopDirectoryOnly )
			.Where( path => !excluded.Contains( Path.GetFileName( path ) ) )
			.Select( path => (Name: Path.GetFileName( path ), Uses: DesktopOnlyUses( File.ReadAllText( path ), desktopOnly ).ToArray()) )
			.Where( file => file.Uses.Length > 0 )
			.OrderBy( file => file.Name, StringComparer.Ordinal )
			.Select( file => $"add {file.Name} to DeterminismPinsExcludedTests in OpenTPW.Web.csproj: it uses {string.Join( ", ", file.Uses )}, which the browser build leaves out" )
			.ToArray();
		Assert.AreEqual( 0, missing.Length, string.Join( Environment.NewLine, missing ) );
	}

	[TestMethod]
	public void TheDesktopOnlyGuardTellsGameUsesFromSameNamedMembers()
	{
		var desktopOnly = DesktopOnly(
			[
				("OpenTPW/Client/Game.cs", "namespace OpenTPW;\ninternal static class Game\n{\n\tinternal const string CopiedSavesMarker = \"x\";\n\tinternal static string ResolveSaveDirectory( string game, string settings = \"\" ) => game;\n}"),
				("OpenTPW/UI/Layouts/ParkLayout.cs", "namespace OpenTPW;\npublic sealed class ParkLayout\n{\n\tprivate int level = 2;\n\tpublic static void Draw() { var inner = 1; }\n\tpublic static bool DrawOriginalImport( int count ) => count > 0;\n}"),
			],
			["namespace OpenTPW;\npublic sealed class ParkLayout\n{\n\tpublic static void Draw() { }\n}"] );
		CollectionAssert.AreEqual( new[] { "Game" }, desktopOnly.Types.Keys.ToArray() );
		CollectionAssert.AreEquivalent( new[] { "level", "DrawOriginalImport" }, desktopOnly.Members["ParkLayout"].Keys.ToArray() );

		foreach ( var use in new[] { "var directory = Game.ResolveSaveDirectory( root );", "var marker = Game . CopiedSavesMarker;", "var name = nameof( Game );",
			"using static OpenTPW.Game;", "var drawn = ParkLayout.DrawOriginalImport( 1 );" } )
			Assert.AreNotEqual( 0, DesktopOnlyUses( use, desktopOnly ).Count(), use );
		foreach ( var mention in new[] { "// Game.ResolveSaveDirectory\nvar count = 1;", "var path = \"Client/Game.cs\";", "var game = park.Game;",
			"var options = new Options { Game = 1 };", "private static string? Game() => null;\nvar game = Game();", "ParkLayout.Draw();",
			"private readonly Fixture Game = new();\nvar marker = Game.CopiedSavesMarker;", "var drawn = layout.DrawOriginalImport( 1 );" } )
			Assert.AreEqual( 0, DesktopOnlyUses( mention, desktopOnly ).Count(), mention );
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
	private static IEnumerable<string> TopLevelTypes( string text ) => TopLevelDeclarations( CodeOnly( text ) ).Select( match => match.Groups[1].Value );

	/// <summary>The declarations of <see cref="TopLevelTypes"/> in code without comments and literals; the first group is the type's name.</summary>
	private static IEnumerable<Match> TopLevelDeclarations( string code )
	{
		var namespaceDepth = Regex.IsMatch( code, @"\bnamespace\s+[\w.]+\s*\{" ) ? 1 : 0;
		foreach ( Match match in Regex.Matches( code, @"\b(?:class|struct|record|interface|enum)\s+(?!(?:class|struct|where|new)\b)([A-Za-z_]\w*)" ) )
		{
			var before = code.AsSpan( 0, match.Index );
			if ( before.Count( '{' ) - before.Count( '}' ) == namespaceDepth )
				yield return match;
		}
	}

	/// <summary>
	/// The source files of the page's <c>Compile Include="$(Root)/**/*.cs" ... Exclude="..."</c> items, split into the excluded ones (repository-relative
	/// names) and the compiled ones, which also hold the page's own files (its browser stand-ins). An Exclude entry is a file or a <c>dir/**</c> folder.
	/// </summary>
	private static (List<(string Name, string Path)> Desktop, List<string> Browser) PageSources( string web, XDocument project )
	{
		var properties = project.Descendants( "PropertyGroup" ).Elements().GroupBy( element => element.Name.LocalName ).ToDictionary( group => group.Key, group => group.Last().Value.Trim() );
		var desktop = new List<(string Name, string Path)>();
		var browser = Directory.EnumerateFiles( web, "*.cs", SearchOption.AllDirectories ).Where( path => !InBuildOutput( Path.GetRelativePath( web, path ) ) ).ToList();
		foreach ( var item in project.Descendants( "Compile" ) )
		{
			var include = Regex.Match( (string?)item.Attribute( "Include" ) ?? "", @"^\$\((\w+)\)/\*\*/\*\.cs$" );
			if ( !include.Success )
				continue;
			var prefix = $"$({include.Groups[1].Value})/";
			var root = Path.GetFullPath( Path.Combine( web, properties[include.Groups[1].Value] ) );
			var exclude = ((string?)item.Attribute( "Exclude" ) ?? "").Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).ToArray();
			foreach ( var entry in exclude )
				Assert.IsTrue( entry.StartsWith( prefix, StringComparison.Ordinal ) && !entry[prefix.Length..].TrimEnd( '*' ).Contains( '*' ), $"unsupported Exclude entry {entry}" );
			foreach ( var path in Directory.EnumerateFiles( root, "*.cs", SearchOption.AllDirectories ) )
			{
				var relative = Path.GetRelativePath( root, path ).Replace( '\\', '/' );
				var excluded = exclude.Select( entry => entry[prefix.Length..] )
					.Any( entry => entry.EndsWith( "/**", StringComparison.Ordinal ) ? relative.StartsWith( entry[..^2], StringComparison.Ordinal ) : relative == entry );
				if ( !excluded )
					browser.Add( path );
				else if ( !InBuildOutput( relative ) )
					desktop.Add( ($"{Path.GetFileName( root )}/{relative}", path) );
			}
		}
		return (desktop, browser);
	}

	private static bool InBuildOutput( string relative ) => Regex.IsMatch( relative.Replace( '\\', '/' ), "^(obj|bin)/" );

	/// <summary>What only the excluded files declare: a top-level type no compiled file declares, or a member of a type that compiled files declare too
	/// (a partial type's other part, a browser stand-in) that none of those compiled declarations has. Each points at an excluded file declaring it.</summary>
	private sealed record DesktopOnlyCode( IReadOnlyDictionary<string, string> Types, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Members );

	private static DesktopOnlyCode DesktopOnly( IEnumerable<(string Name, string Text)> desktopFiles, IEnumerable<string> browserFiles )
	{
		var desktop = new SortedDictionary<string, SortedDictionary<string, string>>( StringComparer.Ordinal );
		var files = new SortedDictionary<string, string>( StringComparer.Ordinal );
		foreach ( var (name, text) in desktopFiles )
		{
			var code = CodeOnly( text );
			foreach ( var declaration in TopLevelDeclarations( code ) )
			{
				var type = declaration.Groups[1].Value;
				files.TryAdd( type, name );
				var members = desktop.TryGetValue( type, out var found ) ? found : desktop[type] = new( StringComparer.Ordinal );
				foreach ( var member in DeclaredNames( MemberLevel( code, declaration ) ) )
					members.TryAdd( member, name );
			}
		}
		var browser = new Dictionary<string, HashSet<string>>( StringComparer.Ordinal );
		foreach ( var text in browserFiles )
		{
			var code = CodeOnly( text );
			foreach ( var declaration in TopLevelDeclarations( code ).Where( declaration => desktop.ContainsKey( declaration.Groups[1].Value ) ) )
			{
				var type = declaration.Groups[1].Value;
				var members = browser.TryGetValue( type, out var found ) ? found : browser[type] = new( StringComparer.Ordinal );
				members.UnionWith( DeclaredNames( MemberLevel( code, declaration ) ) );
			}
		}
		var types = desktop.Keys.Where( type => !browser.ContainsKey( type ) ).ToDictionary( type => type, type => files[type], StringComparer.Ordinal );
		var shared = desktop.Where( pair => browser.ContainsKey( pair.Key ) )
			.Select( pair => (Type: pair.Key, Only: (IReadOnlyDictionary<string, string>)pair.Value.Where( member => !browser[pair.Key].Contains( member.Key ) ).ToDictionary( member => member.Key, member => member.Value, StringComparer.Ordinal )) )
			.Where( pair => pair.Only.Count > 0 )
			.ToDictionary( pair => pair.Type, pair => pair.Only, StringComparer.Ordinal );
		return new DesktopOnlyCode( types, shared );
	}

	/// <summary>
	/// The desktop-only types and members a test file uses, read like <see cref="CannotCompileForTheBrowser"/> (code only, no comments or literals). Game type
	/// names are common member names (<c>Settings</c>, <c>Cursor</c>, <c>Game</c>), so a name after a dot (<c>park.Settings</c>), an initializer target
	/// (<c>{ Cursor = 0 }</c>) or a name the file declares itself (a field, method, local or parameter, which C# would resolve first) is no use of the type.
	/// </summary>
	private static IEnumerable<string> DesktopOnlyUses( string text, DesktopOnlyCode desktopOnly )
	{
		var code = CodeOnly( text );
		var own = DeclaredNames( code );
		var uses = new SortedSet<string>( StringComparer.Ordinal );
		foreach ( Match directive in Regex.Matches( code, @"^\s*using\s+(?:static\s+)?(?:\w+\s*=\s*)?(?:global::)?[\w.]*?\b(\w+)\s*;", RegexOptions.Multiline ) )
		{
			if ( desktopOnly.Types.TryGetValue( directive.Groups[1].Value, out var file ) )
				uses.Add( $"{directive.Groups[1].Value} ({file})" );
		}
		foreach ( Match word in Regex.Matches( code, @"(?<![\w.])(?<!\.\s+)[A-Za-z_]\w*\b" ) )
		{
			var name = word.Value;
			if ( own.Contains( name ) )
				continue;
			var after = code.AsSpan( word.Index + word.Length ).TrimStart();
			if ( desktopOnly.Types.TryGetValue( name, out var file ) && !(after.StartsWith( "=" ) && !after.StartsWith( "==" ) && !after.StartsWith( "=>" )) )
				uses.Add( $"{name} ({file})" );
			else if ( desktopOnly.Members.TryGetValue( name, out var members ) )
			{
				var member = Regex.Match( after.ToString(), @"^\.\s*([A-Za-z_]\w*)" );
				if ( member.Success && members.TryGetValue( member.Groups[1].Value, out var memberFile ) )
					uses.Add( $"{name}.{member.Groups[1].Value} ({memberFile})" );
			}
		}
		return uses;
	}

	/// <summary>
	/// The names a piece of code declares: a field, property, method, local, parameter or type, written as a type (or a modifier) and then the name, ahead
	/// of <c>=</c>, <c>;</c>, <c>(</c>, <c>{</c>, <c>,</c>, <c>)</c> or <c>=&gt;</c>. Expression keywords (<c>new</c>, <c>return</c>, <c>out</c> ...) declare nothing.
	/// </summary>
	private static HashSet<string> DeclaredNames( string code )
	{
		var names = new HashSet<string>( StringComparer.Ordinal );
		foreach ( Match match in Regex.Matches( code, @"(?<=\s)(?<![\w.])([A-Za-z_]\w*)\s*(?:<[^<>;{}()=]*>\s*)?(?=(?:[;({,)]|=(?!=)))" ) )
		{
			var name = match.Groups[1].Value;
			var before = code.AsSpan( 0, match.Index ).TrimEnd();
			if ( NotDeclaring.Contains( name ) || before.IsEmpty )
				continue;
			var last = before[^1];
			if ( last is ']' or ')' || (last == '>' && !before.EndsWith( "=>" )) || (last == '?' && before.Length > 1 && !char.IsWhiteSpace( before[^2] )) )
				names.Add( name );
			else if ( char.IsLetterOrDigit( last ) || last == '_' )
			{
				var start = before.Length;
				while ( start > 0 && (char.IsLetterOrDigit( before[start - 1] ) || before[start - 1] == '_') )
					start--;
				if ( !NotDeclaring.Contains( before[start..].ToString() ) )
					names.Add( name );
			}
		}
		return names;
	}

	private static readonly HashSet<string> NotDeclaring = new( StringComparer.Ordinal )
	{
		"new", "return", "throw", "await", "is", "as", "case", "in", "out", "ref", "else", "yield", "goto", "when", "and", "or", "not", "with", "using",
		"typeof", "nameof", "sizeof", "default", "namespace", "lock", "if", "while", "for", "foreach", "switch", "catch", "checked", "unchecked",
	};

	/// <summary>A top-level type's header (type parameters, primary constructor, base list) and its body at member level: everything nested deeper
	/// (method bodies, initializer arguments, attributes) is blanked, so only the type's own member declarations remain.</summary>
	private static string MemberLevel( string code, Match declaration )
	{
		var index = declaration.Index + declaration.Length;
		var parentheses = 0;
		while ( index < code.Length && !(parentheses == 0 && code[index] is '{' or ';') )
		{
			parentheses += code[index] is '(' or '[' ? 1 : code[index] is ')' or ']' ? -1 : 0;
			index++;
		}
		var level = new StringBuilder( code[(declaration.Index + declaration.Length)..index] );
		var braces = 0;
		parentheses = 0;
		for ( ; index < code.Length && (braces > 0 || code[index] == '{'); index++ )
		{
			var current = code[index];
			braces += current == '{' ? 1 : current == '}' ? -1 : 0;
			parentheses += current is '(' or '[' ? 1 : current is ')' or ']' ? -1 : 0;
			var kept = current is '{' or '}' || (braces == 1 && (parentheses == 0 || (parentheses == 1 && current is '(' or '[')));
			level.Append( kept || char.IsWhiteSpace( current ) ? current : ' ' );
			if ( braces == 0 )
				break;
		}
		return level.ToString();
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
