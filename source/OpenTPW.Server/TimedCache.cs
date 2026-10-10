namespace OpenTPW.Server;

/// <summary>A value computed at most once per <see cref="Lifetime"/>, for public routes the website requests often.</summary>
public sealed class TimedCache<T>( TimeSpan lifetime, Func<T> compute, Func<DateTime>? clock = null ) where T : class
{
	private readonly object gate = new();
	private readonly Func<DateTime> now = clock ?? (() => DateTime.UtcNow);
	private T? value;
	private DateTime expires;

	public TimeSpan Lifetime => lifetime;

	public T Get()
	{
		lock ( gate )
		{
			var time = now();
			if ( value == null || time >= expires )
			{
				value = compute();
				expires = time + lifetime;
			}
			return value;
		}
	}
}
