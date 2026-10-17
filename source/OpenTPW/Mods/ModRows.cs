namespace OpenTPW.Mods;

/// <summary>How a row of the Extensions and mods screen stands, in the player's terms.</summary>
public enum ModRowStatus
{
	/// <summary>Switched on and accepted.</summary>
	On,
	/// <summary>Switched off.</summary>
	Off,
	/// <summary>Switched on, but for another OpenTPW version or game edition.</summary>
	Incompatible,
	/// <summary>Switched on, but an input folder is not chosen or no longer exists.</summary>
	MissingInput,
	/// <summary>Switched on, but another enabled mod overrides the same file.</summary>
	Conflict,
	/// <summary>Switched on, but a mod or extension it needs is missing, off or refused.</summary>
	NeedsOther,
	/// <summary>Switched on, and the screen is still resolving what that means.</summary>
	Pending,
	/// <summary>Switched on, but its scripts are off for this run (safe mode).</summary>
	SafeMode,
	/// <summary>Cannot be used at all: an invalid or shadowed mod folder, or unreadable files.</summary>
	Unusable,
}

/// <summary>One extension or mod as the Extensions and mods screen shows it.</summary>
public sealed record ModRow( string Id, string Name, ModVersion? Version, bool IsExtension, bool Enabled, ModRowStatus Status, string? Reason,
	ModManifest? Manifest, string? Directory, string? Digest, IOpenTpwExtension? Extension )
{
	/// <summary>Whether the row's switch can be turned: every row except a mod folder that cannot be read.</summary>
	public bool CanToggle => Manifest != null || Extension != null;
}

/// <summary>
/// The rows of the Extensions and mods screen ([EXT:mods-ui]): the official extensions first, then the mods found at
/// startup, each with the status its pending choice would get. The status comes from resolving the real
/// <see cref="ModSet"/>, so the screen never invents a rule the startup does not apply.
/// </summary>
// [EXT:mods-ui] OpenTPW's own mods screen; the original game had no mods
public static class ModRows
{
	public static IReadOnlyList<ModRow> Build( IReadOnlyList<DiscoveredMod> discovered, ModSettings pending, ModSet resolved, IReadOnlyList<IOpenTpwExtension> extensions, bool current = true )
	{
		var rows = new List<ModRow>();
		// An extension is never refused, so its row follows the pending choice at once.
		foreach ( var extension in extensions.Where( extension => extension.PlayerSelectable ) )
		{
			var enabled = pending.IsEnabled( extension.Id, extension.DefaultEnabled );
			rows.Add( new ModRow( extension.Id, extension.Name, extension.Version, true, enabled,
				enabled ? ModRowStatus.On : ModRowStatus.Off, null, null, null, null, extension ) );
		}
		// The refusals of invalid or shadowed folders carry the folder in front of their reason; they belong to those folders, not to a valid mod of the same id.
		var invalidPrefixes = discovered.Where( mod => !mod.IsValid ).Select( mod => mod.Directory + ": " ).ToList();
		foreach ( var mod in discovered.OrderBy( mod => mod.Id, StringComparer.Ordinal ).ThenBy( mod => mod.Directory, StringComparer.Ordinal ) )
		{
			if ( !mod.IsValid )
			{
				rows.Add( new ModRow( mod.Id, Path.GetFileName( mod.Directory.TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar ) ), null, false, false,
					ModRowStatus.Unusable, mod.Error, null, mod.Directory, null, null ) );
				continue;
			}
			var manifest = mod.Manifest!;
			var enabled = pending.IsEnabled( manifest.Id, false );
			var status = ModRowStatus.Off;
			string? reason = null;
			string? digest = null;
			if ( enabled && !current )
				status = ModRowStatus.Pending;
			else if ( enabled )
			{
				if ( resolved.Mods.FirstOrDefault( candidate => candidate.Id == manifest.Id && candidate.Directory == mod.Directory ) is { } active )
				{
					status = ModRowStatus.On;
					digest = active.ContentSha256;
				}
				else
				{
					var refusal = resolved.Refused.LastOrDefault( candidate => candidate.Id == manifest.Id && !invalidPrefixes.Any( prefix => candidate.Reason.StartsWith( prefix, StringComparison.Ordinal ) ) );
					reason = refusal?.Reason;
					status = refusal?.Kind switch
					{
						ModRefusalKind.Incompatible => ModRowStatus.Incompatible,
						ModRefusalKind.MissingInput => ModRowStatus.MissingInput,
						ModRefusalKind.Conflict => ModRowStatus.Conflict,
						ModRefusalKind.Dependency => ModRowStatus.NeedsOther,
						ModRefusalKind.SafeMode => ModRowStatus.SafeMode,
						_ => ModRowStatus.Unusable,
					};
				}
			}
			rows.Add( new ModRow( manifest.Id, manifest.Name, manifest.Version, false, enabled, status, reason, manifest, mod.Directory, digest, null ) );
		}
		return rows;
	}
}
