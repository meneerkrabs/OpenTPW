namespace OpenTPW;

/// <summary>
/// The wait for a chosen language's files before it is applied (<see cref="GameFlow.LanguageDataReady"/>, browser build,
/// docs/WEB.md). Text and speech must change together: while the files are on their way the choice stays pending; when their
/// copy failed the choice is dropped with a warning, so the current language (text and speech) stays whole and choosing the
/// language again tries again.
/// </summary>
// [EXT:live-language] where the files of a language come from is an OpenTPW matter
internal static class PendingLanguage
{
	/// <summary>
	/// True when <paramref name="pending"/> is settled: none, ready (apply it now) or dropped because its files could not be
	/// copied (<paramref name="pending"/> is then null). False while the files are still coming.
	/// </summary>
	public static bool Settle( ref string? pending, string current, Func<string, bool>? ready, Func<string, bool>? failed, Action<string> warn )
	{
		var name = pending;
		if ( name == null )
			return true;
		if ( failed?.Invoke( name ) == true )
		{
			warn( $"The files of language {name} could not be copied; keeping {current}. Choose it again to retry." );
			pending = null;
			return true;
		}
		return ready?.Invoke( name ) != false;
	}
}
