namespace OpenTPW;

/// <summary>
/// [EXT:park-entry-preparation] What the automatic advisor reads from the game data when the frame that enters a park creates it and says its first line
/// (its settings, the global model, the level's model, the clip lengths and the theme costume), read instead in the park build's preparation steps
/// (docs/BROWSER-PERFORMANCE.md, "Event hitches"). Each prepared result is the one its reader would return: it is taken once, by the reader that would have
/// read it, and only from the file system it was read from and while that file system still reads what it read (its <see cref="BaseFileSystem.LookupGeneration"/>:
/// a mod or language change in between drops it); what it logged is held and written when it is taken, so the log reads as before. A read
/// that fails is not kept, so its reader meets the same failure and reports it as before. Whatever is not taken by the park's first update is dropped, and so is
/// everything when a park build starts its preparation, fails or is abandoned, and when the game tears down.
/// Game thread only.
/// </summary>
internal static class AdvisorEntryPreparation
{
	private sealed record Prepared( BaseFileSystem FileSystem, int Generation, object? Value, List<(Logger.Level Severity, string Text, bool Quiet)> Messages )
	{
		/// <summary>Whether this result is what a read from <paramref name="fileSystem"/> gives now.</summary>
		public bool Current( BaseFileSystem fileSystem ) => ReferenceEquals( FileSystem, fileSystem ) && Generation == fileSystem.LookupGeneration;
	}

	private static readonly Dictionary<string, Prepared> prepared = new( StringComparer.Ordinal );

	internal const string BalanceKey = "balance";
	internal const string GlobalRigKey = "rig:global";
	internal static string LevelRigKey( string level ) => $"rig:{level}";
	internal static string DurationsKey( string archive ) => $"durations:{archive}";
	internal static string ThemeKey( string level ) => $"theme:{level}";

	/// <summary>The number of results prepared and not yet taken or dropped (tests).</summary>
	internal static int Count => prepared.Count;

	/// <summary>
	/// Reads <paramref name="read"/>'s result for the next <see cref="TryTake"/> of <paramref name="key"/> from <paramref name="fileSystem"/>; nothing is kept when it throws. A result
	/// already prepared for the key is kept when it is still current for <paramref name="fileSystem"/>, else dropped and read again.
	/// </summary>
	internal static void Prepare( string key, BaseFileSystem fileSystem, Func<object?> read )
	{
		if ( prepared.TryGetValue( key, out var existing ) )
		{
			if ( existing.Current( fileSystem ) )
				return;
			Remove( key, existing );
		}
		var generation = fileSystem.LookupGeneration;
		object? value;
		List<(Logger.Level Severity, string Text, bool Quiet)> messages;
		using ( var capture = Logger.BeginCapture() )
		{
			try { value = read(); }
			catch ( Exception exception ) when ( exception is not OutOfMemoryException ) { return; }
			messages = capture.Messages;
		}
		prepared[key] = new Prepared( fileSystem, generation, value, messages );
	}

	/// <summary>Takes the result prepared for <paramref name="key"/> from <paramref name="fileSystem"/> and writes what its read logged; false (nothing taken) when there is none.</summary>
	internal static bool TryTake<T>( string key, BaseFileSystem fileSystem, out T? value )
	{
		value = default;
		if ( !prepared.TryGetValue( key, out var item ) )
			return false;
		// read before a change of what the file system reads (a mod or language switch): not what a read now gives
		if ( ReferenceEquals( item.FileSystem, fileSystem ) && item.Generation != fileSystem.LookupGeneration )
		{
			Remove( key, item );
			return false;
		}
		if ( !ReferenceEquals( item.FileSystem, fileSystem ) || item.Value is not (null or T) )
			return false;
		prepared.Remove( key );
		Logger.Replay( item.Messages );
		value = (T?)item.Value;
		return true;
	}

	private static void Remove( string key, Prepared item )
	{
		prepared.Remove( key );
		(item.Value as IDisposable)?.Dispose();
	}

	/// <summary>Drops (and disposes) every result nobody took, with what its read logged.</summary>
	internal static void Drop()
	{
		if ( prepared.Count == 0 )
			return;
		foreach ( var item in prepared.Values )
			(item.Value as IDisposable)?.Dispose();
		prepared.Clear();
	}

	/// <summary>
	/// The steps that read what the advisor's first line in <paramref name="level"/> reads: the global model (when no presentation exists yet), the level's model,
	/// the clip lengths of both and the theme costume. A false value asks for the next step, a long one, to start a frame of its own (GameFlow's park build).
	/// </summary>
	internal static IEnumerable<bool> PresentationSteps( BaseFileSystem fileSystem, string level, bool globalRig )
	{
		if ( globalRig )
		{
			// the advisor shader's program, then the model, each a long step in a frame of its own; the shader is only a head start
			yield return false;
			try { AdvisorRig.WarmShader(); }
			catch ( Exception exception ) when ( exception is not OutOfMemoryException ) { }
			yield return false;
			Prepare( GlobalRigKey, fileSystem, () => AdvisorRig.LoadGlobalModel() );
			yield return true;
		}
		Prepare( LevelRigKey( level ), fileSystem, () => AdvisorRig.LoadLevel( fileSystem, level ) );
		yield return true;
		foreach ( var archive in new[] { AdvisorRig.LevelArchive( level ), Advisor.ArchivePath } )
		{
			Prepare( DurationsKey( archive ), fileSystem, () => AdvisorClipDurations.LoadArchive( fileSystem, archive ) );
			yield return true;
		}
		Prepare( ThemeKey( level ), fileSystem, () => AdvisorThemeCostume.Load( fileSystem, level ) );
		yield return true;
	}
}
