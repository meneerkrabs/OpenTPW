using System.Runtime.InteropServices.JavaScript;
using System.Text.Json.Nodes;
using OpenTPW.Online;
using OpenTPW.Online.Packages;
using OpenTPW.UI;
using OpenTPW.UI.Original;

namespace OpenTPW;

public static partial class Program
{
	/// <summary>
	/// Asset-free regression check for tools/web-postcard-smoke.mjs. Runs the real postcard serialization and Send
	/// handler inside the published browser runtime, before a game starts. All files are temporary and no server is contacted.
	/// </summary>
	[JSExport]
	public static string PostcardSmokeTest()
	{
		if ( !OperatingSystem.IsBrowser() || renderer != null || flow != null || GameLanguage.IsSelected )
			throw new InvalidOperationException( "The postcard smoke check requires a fresh browser runtime before game startup." );
		var checks = new JsonArray();
		void Check( bool passed, string name )
		{
			if ( !passed ) throw new InvalidOperationException( $"Postcard smoke check failed: {name}." );
			checks.Add( name );
		}
		void RejectDuplicate( string compatible, string ordinary )
		{
			try { Postcard.Create( "Sender", new[] { compatible, ordinary }, "Title", "Message", "English" ); }
			catch ( InvalidDataException exception ) when ( exception.Message == "Postcard lists a recipient twice." ) { return; }
			throw new InvalidOperationException( $"Postcard accepted duplicate recipients {compatible} and {ordinary}." );
		}

		Check( OnlineText.NormalizeName( "Ｂｏｂ" ) == "BOB" && OnlineText.NormalizeName( "ﬃab" ) == "FFIAB"
			&& OnlineText.NormalizeName( "Søren" ) == "SØREN", "compatibility-normalization" );
		// JavaScript uppercasing expands sharp s to SS; the server's .NET identity deliberately does not.
		Check( OnlineText.NormalizeName( "Straße" ) == "STRAßE"
			&& OnlineText.NormalizeName( "Straße" ) != OnlineText.NormalizeName( "Strasse" ), "dotnet-identity-casing" );
		Check( !OnlineText.IsValidPlayerName( "Ju\u0308rgen" ) && OnlineText.IsValidPlayerName( "Jürgen" ), "canonical-name-validation" );
		foreach ( var malformed in new[] { "a\uD800b", "a\uDC00b", "a\uFFFEb" } )
		{
			var rejected = false;
			try { OnlineText.NormalizeName( malformed ); }
			catch ( ArgumentException ) { rejected = true; }
			if ( !rejected ) throw new InvalidOperationException( "Postcard identity accepted malformed Unicode." );
		}
		Check( true, "malformed-unicode-rejected" );
		RejectDuplicate( "Ｂｏｂ", "Bob" );
		RejectDuplicate( "ﬃab", "ffiab" );
		Check( true, "compatibility-duplicates-rejected" );

		var recipients = new[] { "Ｂｏｂ", "ﬃab", "Søren", "Straße", "Strasse", "card+browser@example.test" };
		var original = Postcard.Create( "Sender", recipients, "Browser postcard", "First line\r\nSecond line", "English" );
		var restored = Postcard.Read( original.ToBytes() );
		Check( restored.Id == original.Id && restored.Manifest.To.SequenceEqual( recipients )
			&& restored.Manifest.Text == "First line\nSecond line" && restored.Manifest.Title == original.Manifest.Title,
			"postcard-create-write-read" );

		var previousFiles = FileSystem;
		var root = Directory.CreateTempSubdirectory( "opentpw-postcard-smoke-" ).FullName;
		try
		{
			FileSystem = new BaseFileSystem( root );
			var folders = new OnlineFolders( root );
			folders.SaveSettings( new OnlineSettings( null, "Sender" ) );
			using var session = new OnlineSession( folders );
			var stack = new UiScreenStack();
			stack.Push( new UiScreen( "smokeHud" ) );
			var failPhoto = false;
			const string failureMessage = "Postcard smoke test platform failure.";
			var screens = new OnlineScreens( stack,
				new UiStringTable( "English", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>() ),
				new UiModels( name => throw new FileNotFoundException( name ) ),
				new OnlineHost { Session = session, Level = () => failPhoto ? throw new PlatformNotSupportedException( failureMessage ) : null } );
			UiTextField Field( UiScreen screen, string id ) => (UiTextField)screen.Find( id )!;
			void Click( UiScreen screen, string id ) => ((UiButton)screen.Find( id )!).Clicked!();
			UiScreen Compose( string to, string title = "", string message = "" )
			{
				screens.ShowSendPostcard();
				var screen = stack.Top!;
				Field( screen, "to" ).Type( to, 0 );
				if ( title.Length > 0 ) Field( screen, "title" ).Type( title, 0 );
				if ( message.Length > 0 ) Field( screen, "message" ).Type( message, 0 );
				return screen;
			}
			int Stored() => folders.List( folders.Outbox ).Count;

			var draft = Compose( "Alice; Bob Smith, card@example.test" );
			Click( draft, "ok" );
			var saved = folders.List( folders.Outbox ).Single().Card!;
			Check( stack.Top?.Name == "smokeHud" && saved.Manifest.To.SequenceEqual( new[] { "Alice", "Bob Smith", "card@example.test" } )
				&& saved.Manifest.Title == "Postcard from Theme Park World" && saved.Manifest.Text == "Greetings from Theme Park World!",
				"send-handler-defaults-and-outbox" );

			draft = Compose( "Ｂｏｂ; ﬃab; Søren", "Unicode postcard", "Hello from the browser" );
			Click( draft, "ok" );
			Check( stack.Top?.Name == "smokeHud" && Stored() == 2
				&& folders.List( folders.Outbox ).Any( item => item.Card?.Manifest.To.SequenceEqual( new[] { "Ｂｏｂ", "ﬃab", "Søren" } ) == true ),
				"send-handler-unicode-outbox" );

			draft = Compose( "Ｂｏｂ; Bob", "Keep this draft", "Keep this message" );
			Click( draft, "ok" );
			Check( stack.Top?.Name == "postcardRecipients" && Stored() == 2 && !draft.IsRemoved
				&& Field( draft, "to" ).Text == "Ｂｏｂ; Bob" && Field( draft, "title" ).Text == "Keep this draft"
				&& Field( draft, "message" ).Text == "Keep this message", "duplicate-send-preserves-draft" );
			Click( stack.Top!, "choice0" );
			Field( draft, "to" ).Text = "Alice";
			Click( draft, "ok" );
			Check( stack.Top?.Name == "smokeHud" && Stored() == 3, "duplicate-send-can-retry" );

			failPhoto = true;
			draft = Compose( "Alice", "Retry this draft", "Retry this message" );
			Click( draft, "ok" );
			Check( stack.Top?.Name == "postcardFailed" && Stored() == 3 && !draft.IsRemoved
				&& Field( draft, "to" ).Text == "Alice" && Field( draft, "title" ).Text == "Retry this draft"
				&& Field( draft, "message" ).Text == "Retry this message"
				&& ((UiLabel)stack.Top.Find( "message" )!).Text() == failureMessage, "platform-failure-preserves-draft" );
			Click( stack.Top!, "choice0" );
			failPhoto = false;
			Click( draft, "ok" );
			Check( stack.Top?.Name == "smokeHud" && Stored() == 4
				&& folders.List( folders.Outbox ).All( item => item.Card != null && item.Error == null ), "platform-failure-can-retry" );
		}
		finally
		{
			FileSystem = previousFiles;
			Directory.Delete( root, recursive: true );
		}
		Check( !Directory.Exists( root ) && ReferenceEquals( FileSystem, previousFiles ), "temporary-state-cleaned" );
		return new JsonObject { ["runtime"] = "browser", ["checks"] = checks }.ToJsonString();
	}
}
