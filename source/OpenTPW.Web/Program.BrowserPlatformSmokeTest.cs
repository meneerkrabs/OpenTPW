using System.Runtime.InteropServices.JavaScript;
using System.Text.Json.Nodes;
using OpenTPW.Mods;

namespace OpenTPW;

public static partial class Program
{
	/// <summary>Asset-free checks of the browser build's settings boundary and canonical text operations.</summary>
	[JSExport]
	public static string BrowserPlatformSmokeTest()
	{
		if ( !OperatingSystem.IsBrowser() || renderer != null || flow != null || GameLanguage.IsSelected )
			throw new InvalidOperationException( "The platform smoke check requires a fresh browser runtime before game startup." );
		var checks = new JsonArray();
		void Check( bool passed, string name )
		{
			if ( !passed ) throw new InvalidOperationException( $"Browser platform smoke check failed: {name}." );
			checks.Add( name );
		}
		var assembly = typeof( Program ).Assembly;
		Check( LegacySettings.GamePath == null, "no-legacy-game-path" );
		Check( new[] { "OpenTPW.Settings", "OpenTPW.GameDir", "OpenTPW.UI.Cursor" }.All( name => assembly.GetType( name ) == null ),
			"desktop-settings-and-cursor-excluded" );
		Check( !assembly.GetReferencedAssemblies().Any( reference => reference.Name == "System.Configuration.ConfigurationManager" ),
			"no-configuration-manager-reference" );

		// FormD is supported by browser .NET. Keep canonical accent folding separate from online NFKC identity.
		Check( ObjectNames.Normalize( "Café-Éclair 42!" ) == "cafeeclair42"
			&& ObjectNames.Normalize( "Cafe\u0301-E\u0301clair 42!" ) == "cafeeclair42", "catalog-canonical-accent-folding" );
		Check( ObjectNames.Normalize( "ﬃ" ) != ObjectNames.Normalize( "ffi" )
			&& ObjectNames.Normalize( "Ｂｏｂ" ) != ObjectNames.Normalize( "Bob" ), "catalog-keeps-compatibility-distinctions" );
		Check( ObjectNames.Normalize( "Straße" ) == "straße" && ObjectNames.Normalize( "Søren" ) == "søren"
			&& ObjectNames.Normalize( "水 2" ) == "水2", "catalog-keeps-non-decomposing-letters" );
		Check( ModManifest.FoldKey( "Café/File.SAM" ) == ModManifest.FoldKey( "Cafe\u0301/file.sam" )
			&& ModManifest.FoldKey( "ﬃ.sam" ) != ModManifest.FoldKey( "ffi.sam" ), "mod-path-canonical-identity" );

		var root = Directory.CreateTempSubdirectory( "opentpw-platform-smoke-" ).FullName;
		try
		{
			var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Shop, "/synthetic", new ObjectSettings( new[] {
				new ObjectSettingsFile( "Info.Name \"Café Kiosk\"\n", "synthetic.sam" ) } ), ObjectShape.Single,
				"/synthetic.MD2", null, null, Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(),
				Array.Empty<string>(), new BaseFileSystem( root ), null );
			var matches = ObjectNames.Match( new[] { entry }, new[] { "Unrelated", "Cafe\u0301", "Kiosk" } );
			Check( matches.Count == 1 && ReferenceEquals( matches[0].Entry, entry ) && matches[0].Index == 1 && matches[0].Length == 2,
				"catalog-matches-split-canonical-name" );
		}
		finally { Directory.Delete( root, recursive: true ); }
		Check( !Directory.Exists( root ), "platform-temporary-state-cleaned" );
		return new JsonObject { ["runtime"] = "browser", ["checks"] = checks }.ToJsonString();
	}
}
