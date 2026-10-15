using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The confirm dialog's first choice is the tick: it must be the affirmative choice at every call site.</summary>
[TestClass]
public class DialogChoiceOrderTests
{
	private static string SourceRoot()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var candidate = Path.Combine( directory.FullName, "source", "OpenTPW" );
			if ( File.Exists( Path.Combine( candidate, "OpenTPW.csproj" ) ) )
				return candidate;
		}
		return "";
	}

	[TestMethod]
	public void TheFirstChoiceOfEveryMessageDialogIsNeverACancel()
	{
		var root = SourceRoot();
		if ( root.Length == 0 )
			Assert.Inconclusive( "the source tree is not next to the test binaries" );
		var call = new Regex( @"(?:UiDialogs|ProgressChoiceScreen)\.Message\(\s*(?:name|""[^""]*"")\s*,", RegexOptions.CultureInvariant );
		var cancel = new Regex( @"^\s*\(\s*\(\)\s*=>\s*(?:""(?:Cancel|No|Back)""|strings\.Value\(\s*UIStrings\.No\s*\)|(?:Strings|strings)\.Extra\(\s*OpenTpwText\.Back\s*\))", RegexOptions.CultureInvariant );
		var checkedCalls = 0;
		foreach ( var file in Directory.EnumerateFiles( root, "*.cs", SearchOption.AllDirectories ).Where( path => !path.Contains( $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}" ) ) )
		{
			var text = File.ReadAllText( file );
			foreach ( Match match in call.Matches( text ) )
			{
				// the statement runs to the next line that is not indented deeper than the call's line
				var lineStart = text.LastIndexOf( '\n', match.Index ) + 1;
				var indent = text[lineStart..].TakeWhile( c => c == '\t' || c == ' ' ).Count();
				var lines = text[match.Index..].Split( '\n' );
				var statement = lines[0] + "\n" + string.Join( "\n", lines.Skip( 1 ).TakeWhile( line => line.Trim().Length == 0 || line.TakeWhile( c => c == '\t' || c == ' ' ).Count() > indent ) );
				var rest = statement[match.Length..];
				var firstLabel = Regex.Match( rest, @"(\(\s*\(\)\s*=>[^\n]*)", RegexOptions.CultureInvariant );
				var second = Regex.Matches( rest, @"\(\(\)\s*=>", RegexOptions.CultureInvariant ).Count;
				checkedCalls++;
				// a dialog with one choice may be a Back; with two or more, the first (the tick) must not cancel
				if ( second >= 2 && firstLabel.Success )
					Assert.IsFalse( cancel.IsMatch( firstLabel.Groups[1].Value ), $"{Path.GetFileName( file )}: the tick of '{firstLabel.Groups[1].Value.Trim()}' cancels" );
			}
		}
		Assert.IsTrue( checkedCalls >= 15, $"the scan saw only {checkedCalls} dialog calls" );
	}
}
