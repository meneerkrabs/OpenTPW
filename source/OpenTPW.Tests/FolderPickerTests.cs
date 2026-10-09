using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class FolderPickerTests
{
	[TestMethod]
	public void AppleScriptWithoutInitialDirectoryOmitsDefaultLocation()
	{
		Assert.AreEqual( "POSIX path of (choose folder with prompt \"Pick a folder\")", FolderPicker.BuildAppleScript( "Pick a folder", null ) );
		Assert.AreEqual( "POSIX path of (choose folder with prompt \"Pick a folder\")", FolderPicker.BuildAppleScript( "Pick a folder", "  " ) );
	}

	[TestMethod]
	public void AppleScriptWithInitialDirectoryAddsDefaultLocation()
	{
		Assert.AreEqual(
			"POSIX path of (choose folder with prompt \"Pick\" default location (POSIX file \"/Applications/Theme Park World\"))",
			FolderPicker.BuildAppleScript( "Pick", "/Applications/Theme Park World" ) );
	}

	[TestMethod]
	public void AppleScriptEscapesQuotesAndBackslashesInTitleAndPath()
	{
		var script = FolderPicker.BuildAppleScript( "Say \"hi\" \\ there", "/tmp/a\\b\"c" );
		Assert.AreEqual(
			"POSIX path of (choose folder with prompt \"Say \\\"hi\\\" \\\\ there\" default location (POSIX file \"/tmp/a\\\\b\\\"c\"))",
			script );
	}

	[TestMethod]
	public void ZenityArgumentsUseDirectoryModeAndTrailingSlashForInitialFolder()
	{
		CollectionAssert.AreEqual(
			new[] { "--file-selection", "--directory", "--title=Choose Data", "--filename=/games/TPW/" },
			FolderPicker.BuildLinuxArguments( "zenity", "Choose Data", "/games/TPW" ).ToArray() );
		CollectionAssert.AreEqual(
			new[] { "--file-selection", "--directory", "--title=Choose Data", "--filename=/" },
			FolderPicker.BuildLinuxArguments( "zenity", "Choose Data", "/" ).ToArray() );
	}

	[TestMethod]
	public void ZenityArgumentsOmitFilenameWithoutInitialDirectory()
	{
		CollectionAssert.AreEqual(
			new[] { "--file-selection", "--directory", "--title=Choose Data" },
			FolderPicker.BuildLinuxArguments( "zenity", "Choose Data", null ).ToArray() );
	}

	[TestMethod]
	public void KdialogArgumentsPassInitialFolderOrHomeAndTitle()
	{
		CollectionAssert.AreEqual(
			new[] { "--getexistingdirectory", "/games/TPW", "--title", "Choose Data" },
			FolderPicker.BuildLinuxArguments( "kdialog", "Choose Data", "/games/TPW" ).ToArray() );

		var arguments = FolderPicker.BuildLinuxArguments( "kdialog", "Choose Data", null ).ToArray();
		Assert.AreEqual( "--getexistingdirectory", arguments[0] );
		Assert.AreEqual( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), arguments[1] );
		CollectionAssert.AreEqual( new[] { "--title", "Choose Data" }, arguments.Skip( 2 ).ToArray() );
	}

	[TestMethod]
	public void UnknownLinuxToolIsRejected()
	{
		Assert.ThrowsException<ArgumentException>( () => FolderPicker.BuildLinuxArguments( "xdg-open", "Choose", null ) );
	}

	[TestMethod]
	public void ParseOutputTrimsNewlineAndTrailingSlash()
	{
		Assert.AreEqual( "/Users/me/Games/TPW", FolderPicker.ParseOutput( "/Users/me/Games/TPW/\n" ) );
		Assert.AreEqual( "/Users/me/Games/TPW", FolderPicker.ParseOutput( "/Users/me/Games/TPW\r\n" ) );
		Assert.AreEqual( "/Users/me/Games/TPW", FolderPicker.ParseOutput( "/Users/me/Games/TPW//\n" ) );
	}

	[TestMethod]
	public void ParseOutputKeepsRoot()
	{
		Assert.AreEqual( "/", FolderPicker.ParseOutput( "/\n" ) );
		Assert.AreEqual( "/", FolderPicker.ParseOutput( "///\n" ) );
	}

	[TestMethod]
	public void ParseOutputReturnsNullForEmptyOutput()
	{
		Assert.IsNull( FolderPicker.ParseOutput( "" ) );
		Assert.IsNull( FolderPicker.ParseOutput( "\n" ) );
	}

	[TestMethod]
	public void IsAvailableDoesNotThrowOnThisHost()
	{
		// Pick is deliberately not called: it would open a native dialog and block the test.
		var available = FolderPicker.IsAvailable;
		if ( !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux() && !OperatingSystem.IsWindows() )
			Assert.IsFalse( available );
	}
}
