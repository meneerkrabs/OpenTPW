using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The server address of the login dialog: a missing or refused address is reported in the game's language, not with the
/// English .NET message ("Invalid URI: The URI is empty.").
/// </summary>
[TestClass, DoNotParallelize]
public class OnlineServerAddressTests
{
	[TestMethod]
	public void TheAddressMessagesAreLocalizedInEveryLanguageWithoutTheTypographicApostrophe()
	{
		var english = OnlineStrings.Supplementary["English"];
		foreach ( var (language, table) in OnlineStrings.Supplementary )
			foreach ( var label in new[] { OnlineLabel.EnterServerAddress, OnlineLabel.InvalidServerAddress } )
			{
				Assert.IsTrue( table.TryGetValue( label, out var text ) && text.Length > 0, $"{language} has {label}" );
				Assert.IsFalse( text.Contains( '’' ), $"{language} {label}: no U+2019" );
				if ( language != "English" )
					Assert.AreNotEqual( english[label], text, $"{label} is translated in {language}" );
			}
		StringAssert.Contains( english[OnlineLabel.InvalidServerAddress], "https://" );
	}

	[TestMethod]
	public void AMissingOrRefusedAddressThrowsTheLocalizedReason()
	{
		var previousSuggestion = OnlineSession.SuggestedServerUrl;
		try
		{
			OnlineSession.SuggestedServerUrl = null;
			var enter = OnlineStrings.Get( OnlineLabel.EnterServerAddress );
			var invalid = OnlineStrings.Get( OnlineLabel.InvalidServerAddress );
			Assert.AreEqual( enter, Assert.ThrowsException<ArgumentException>( () => OnlineSession.ParseServerUrl( "" ) ).Message );
			Assert.AreEqual( enter, Assert.ThrowsException<ArgumentException>( () => OnlineSession.ParseServerUrl( "   " ) ).Message );
			foreach ( var refused in new[] { "server", "localhost:5000", "ftp://example.com/", "http://example.com/", "https://user:secret@example.com/", "https://example.com/?a=1" } )
				Assert.AreEqual( invalid, Assert.ThrowsException<ArgumentException>( () => OnlineSession.ParseServerUrl( refused ) ).Message, refused );
			Assert.AreEqual( new Uri( "https://example.com/" ), OnlineSession.ParseServerUrl( " https://example.com " ) );
			Assert.AreEqual( new Uri( "http://localhost:5000/" ), OnlineSession.ParseServerUrl( "http://localhost:5000" ) );

			// the browser build offers the address it was loaded from: an emptied field takes it
			OnlineSession.SuggestedServerUrl = "https://play.example.com";
			Assert.AreEqual( new Uri( "https://play.example.com/" ), OnlineSession.ParseServerUrl( "" ) );
		}
		finally { OnlineSession.SuggestedServerUrl = previousSuggestion; }
	}

	[TestMethod]
	public void AnEmptyAddressShowsTheDutchMessageInTheLoginDialogAndTheLobby()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-online-address-" ).FullName;
		var previousFiles = OpenTPW.Common.GlobalNamespace.FileSystem;
		var previousLanguage = GameLanguage.IsSelected ? GameLanguage.Current : null;
		var previousSuggestion = OnlineSession.SuggestedServerUrl;
		try
		{
			OpenTPW.Common.GlobalNamespace.FileSystem = new BaseFileSystem( root );
			OnlineSession.SuggestedServerUrl = null;
			var dutch = Path.Combine( root, "Data", "Language", "Dutch" );
			Directory.CreateDirectory( dutch );
			File.WriteAllBytes( Path.Combine( dutch, "MBToUni.dat" ), StringTableTests.CreateCharacterTable( "abc" ) );
			File.WriteAllBytes( Path.Combine( dutch, "UITEXT.str" ), StringTableTests.CreateStringTable( new byte[] { 1, 2, 3 } ) );
			GameLanguage.Current = GameLanguage.Resolve( Path.Combine( root, "Data" ), null, null );
			Assert.AreEqual( "Dutch", GameLanguage.Current.Name );

			var folders = new OnlineFolders( Path.Combine( root, "online" ) );
			folders.SaveSettings( new OnlineSettings( null, "speler" ) );
			using var session = new OnlineSession( folders );
			var stack = new UiScreenStack();
			stack.Push( new UiScreen( "hud" ) );
			var screens = new OnlineScreens( stack, OriginalUiTests.FakeStrings(), new UiModels( name => throw new FileNotFoundException( name ) ),
				new OnlineHost { Session = session, Level = () => null } );
			screens.ShowWorld();
			var lobby = stack.Top!;
			screens.ShowLogin();
			var login = stack.Top!;
			Assert.AreEqual( "onlineLogin", login.Name );
			((UiTextField)login.Find( "server" )!).Text = "";
			((UiTextField)login.Find( "password" )!).Text = "geheim";
			((UiButton)login.Find( "login" )!).Clicked!();

			var expected = OnlineStrings.Supplementary["Dutch"][OnlineLabel.EnterServerAddress];
			Assert.AreEqual( expected, ((UiLabel)login.Find( "status" )!).Text!() );
			Assert.IsNull( session.Client, "no server was chosen" );
			stack.Pop();
			Assert.AreSame( lobby, stack.Top );
			var lobbyStatus = lobby.Elements.OfType<UiLabel>().Select( label => label.Text?.Invoke() ).Where( text => text == expected ).ToArray();
			Assert.AreEqual( 1, lobbyStatus.Length, "the lobby's own status shows the same message" );

			screens.ShowLogin();
			login = stack.Top!;
			((UiTextField)login.Find( "server" )!).Text = "http://example.com";
			((UiButton)login.Find( "login" )!).Clicked!();
			Assert.AreEqual( OnlineStrings.Supplementary["Dutch"][OnlineLabel.InvalidServerAddress], ((UiLabel)login.Find( "status" )!).Text!() );
		}
		finally
		{
			OnlineSession.SuggestedServerUrl = previousSuggestion;
			GameLanguage.Current = previousLanguage!;
			OpenTPW.Common.GlobalNamespace.FileSystem = previousFiles;
			Directory.Delete( root, recursive: true );
		}
	}
}
