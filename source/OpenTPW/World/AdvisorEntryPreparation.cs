namespace OpenTPW;

/// <summary>
/// [EXT:park-entry-preparation] What the automatic advisor reads from the game data when the frame that enters a park creates it and says its first line
/// (its settings, the global model, the level's model, the clip lengths and the theme costume), read instead in the park build's preparation steps
/// (docs/BROWSER-PERFORMANCE.md, "Event hitches"). Each prepared result is the one its reader would return: it is taken once, by the reader that would have
/// read it, and only from the file system it was read from and while that file system still reads what it read (its <see cref="BaseFileSystem.LookupGeneration"/>:
/// a mod or language change in between drops it); what it logged is held and written when it is taken, so the log reads as before. A read
/// that fails is not kept, so its reader meets the same failure and reports it as before. Whatever is not taken by the park's first update is dropped, and so is
/// everything when a park build starts its preparation, fails or is abandoned, and when the game tears down. A co-op join's build prepares into a store of its own
/// (<see cref="Store"/>), which the live game's frames do not drop, and its swap hands it over to the shared one (<see cref="Adopt"/>).
/// Game thread only.
/// </summary>
internal static class AdvisorEntryPreparation
{
	private sealed record Prepared( BaseFileSystem FileSystem, int Generation, object? Value, List<(Logger.Level Severity, string Text, bool Quiet)> Messages )
	{
		/// <summary>Whether this result is what a read from <paramref name="fileSystem"/> gives now.</summary>
		public bool Current( BaseFileSystem fileSystem ) => ReferenceEquals( FileSystem, fileSystem ) && Generation == fileSystem.LookupGeneration;
	}

	/// <summary>
	/// Prepared results, each taken once: the shared store a park start's build fills and the advisor's readers take from, or a co-op join build's own, which its swap
	/// hands over to the shared one (<see cref="Adopt"/>).
	/// </summary>
	internal sealed class Store
	{
		private readonly Dictionary<string, Prepared> prepared = new( StringComparer.Ordinal );

		/// <summary>The number of results prepared and not yet taken or dropped.</summary>
		internal int Count => prepared.Count;

		/// <inheritdoc cref="AdvisorEntryPreparation.Prepare"/>
		internal void Prepare( string key, BaseFileSystem fileSystem, Func<object?> read )
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

		/// <inheritdoc cref="AdvisorEntryPreparation.TryTake"/>
		internal bool TryTake<T>( string key, BaseFileSystem fileSystem, out T? value )
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

		/// <summary>Moves every result of <paramref name="from"/> into this store, each in place of (and disposing) one prepared here for its key; <paramref name="from"/> is left empty.</summary>
		internal void Adopt( Store from )
		{
			foreach ( var (key, item) in from.prepared )
			{
				if ( prepared.TryGetValue( key, out var existing ) )
					Remove( key, existing );
				prepared[key] = item;
			}
			from.prepared.Clear();
		}

		private void Remove( string key, Prepared item )
		{
			prepared.Remove( key );
			(item.Value as IDisposable)?.Dispose();
		}

		/// <summary>Drops (and disposes) every result nobody took, with what its read logged.</summary>
		internal void Drop()
		{
			if ( prepared.Count == 0 )
				return;
			foreach ( var item in prepared.Values )
				(item.Value as IDisposable)?.Dispose();
			prepared.Clear();
		}
	}

	private static readonly Store shared = new();

	internal const string BalanceKey = "balance";
	internal const string GlobalRigKey = "rig:global";
	internal static string LevelRigKey( string level ) => $"rig:{level}";
	internal static string DurationsKey( string archive ) => $"durations:{archive}";
	internal static string ThemeKey( string level ) => $"theme:{level}";

	/// <summary>The number of results prepared in the shared store and not yet taken or dropped (tests).</summary>
	internal static int Count => shared.Count;

	/// <summary>
	/// Reads <paramref name="read"/>'s result for the next <see cref="TryTake"/> of <paramref name="key"/> from <paramref name="fileSystem"/>; nothing is kept when it throws. A result
	/// already prepared for the key is kept when it is still current for <paramref name="fileSystem"/>, else dropped and read again.
	/// </summary>
	internal static void Prepare( string key, BaseFileSystem fileSystem, Func<object?> read ) => shared.Prepare( key, fileSystem, read );

	/// <summary>Takes the result prepared for <paramref name="key"/> from <paramref name="fileSystem"/> and writes what its read logged; false (nothing taken) when there is none.</summary>
	internal static bool TryTake<T>( string key, BaseFileSystem fileSystem, out T? value ) => shared.TryTake( key, fileSystem, out value );

	/// <summary>Drops (and disposes) every result of the shared store nobody took, with what its read logged; a join build's own store is its own to drop.</summary>
	internal static void Drop() => shared.Drop();

	/// <summary>Hands a co-op join build's results to the shared store, for the joined park's advisor (<see cref="Store.Adopt"/>).</summary>
	internal static void Adopt( Store from ) => shared.Adopt( from );

	/// <summary>
	/// The steps that read what the advisor's first line in <paramref name="level"/> reads: the global model (when no presentation exists yet), the level's model,
	/// the clip lengths of both and the theme costume. A false value announces that the next step is a long one, which then starts a frame of its own (GameFlow's
	/// park build). With <paramref name="globalRig"/> the first step, the advisor shader's program, is a long one too: the caller's step before announces it. The results go
	/// to <paramref name="into"/>, a co-op join build's own store, or else to the shared one.
	/// </summary>
	internal static IEnumerable<bool> PresentationSteps( BaseFileSystem fileSystem, string level, bool globalRig, Store? into = null )
	{
		var store = into ?? shared;
		if ( globalRig )
		{
			// the advisor shader's program, then the model, each a long step in a frame of its own; the shader is only a head start
			try { AdvisorRig.WarmShader(); }
			catch ( Exception exception ) when ( exception is not OutOfMemoryException ) { }
			yield return false;
			store.Prepare( GlobalRigKey, fileSystem, () => AdvisorRig.LoadGlobalModel() );
			yield return true;
		}
		store.Prepare( LevelRigKey( level ), fileSystem, () => AdvisorRig.LoadLevel( fileSystem, level ) );
		yield return true;
		foreach ( var archive in new[] { AdvisorRig.LevelArchive( level ), Advisor.ArchivePath } )
		{
			store.Prepare( DurationsKey( archive ), fileSystem, () => AdvisorClipDurations.LoadArchive( fileSystem, archive ) );
			yield return true;
		}
		store.Prepare( ThemeKey( level ), fileSystem, () => AdvisorThemeCostume.Load( fileSystem, level ) );
		yield return true;
	}
}
