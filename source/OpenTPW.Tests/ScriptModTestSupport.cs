using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTPW.Mods;
using OpenTPW.Scripting;

namespace OpenTPW.Tests;

/// <summary>Script mods on disk for the hook tests: community mods built from script text in a temporary folder, resolved into a <see cref="ModSet"/>, and the shipped example.</summary>
internal sealed class TempScriptMods : IDisposable
{
	public const string ExampleId = "opentpw.example.patient-guests";

	private readonly string root = Directory.CreateTempSubdirectory( "opentpw-scripthooks-" ).FullName;
	private readonly List<string> folders = new();

	/// <summary>
	/// Adds the community mod <paramref name="id"/> whose single script is <paramref name="script"/>. <paramref name="hooks"/> are hook names, or <c>hook=function</c> when the function has another name.
	/// </summary>
	public TempScriptMods Add( string id, string script, params string[] hooks )
	{
		var directory = Path.Combine( root, id );
		Directory.CreateDirectory( directory );
		var entries = hooks.Select( hook => hook.Split( '=' ) ).Select( parts => $"\"{parts[0]}\": \"{(parts.Length > 1 ? parts[1] : parts[0])}\"" );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ),
			$$"""{ "schema": 1, "id": "{{id}}", "name": "{{id}}", "version": "1.0.0", "category": "community", "scriptApi": ">=1.0.0 <2.0.0", "content": { "scripts": { "files": ["main.tps"], "hooks": { {{string.Join( ", ", entries )}} } } } }""" );
		File.WriteAllText( Path.Combine( directory, "main.tps" ), script );
		folders.Add( directory );
		return this;
	}

	/// <summary>The mods added so far, discovered as command-line mod folders.</summary>
	public IReadOnlyList<DiscoveredMod> Discover() => ModDiscovery.Discover( folders, Array.Empty<string>(), null, null );

	/// <summary>The mods added so far (and the shipped example when <paramref name="withExample"/>), all enabled, hooks chaining in <paramref name="order"/> (the added order when null).</summary>
	public ModSet Resolve( IReadOnlyList<string>? order = null, bool withExample = false )
	{
		var found = ModDiscovery.Discover( folders, Array.Empty<string>(), null, withExample ? AppContext.BaseDirectory : null );
		var ids = found.Where( mod => mod.IsValid && (mod.Source == ModSource.CommandLine || mod.Id == ExampleId) ).Select( mod => mod.Id ).ToArray();
		var set = ModSet.Resolve( found, new ModSettings { Enabled = ids.ToDictionary( id => id, _ => true ), Order = order ?? ids }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		if ( set.Refused.Count > 0 )
			throw new InvalidOperationException( string.Join( "; ", set.Refused.Select( refusal => $"{refusal.Id}: {refusal.Reason}" ) ) );
		return set;
	}

	/// <summary>The shipped example mod, switched on.</summary>
	public static ModSet Example() => new TempScriptMods().Resolve( withExample: true );

	public void Dispose()
	{
		try { Directory.Delete( root, true ); }
		catch ( IOException ) { }
	}
}
