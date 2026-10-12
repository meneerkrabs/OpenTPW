namespace OpenTPW.UI.Original;

/// <summary>Drains known image requests between frames, leaving first-draw loading as the fallback.</summary>
internal sealed class UiImagePreparation
{
	private readonly Queue<string> paths;
	internal UiImagePreparation( IEnumerable<string> paths ) => this.paths = new( paths.Distinct( StringComparer.Ordinal ) );
	internal int Remaining => paths.Count;

	internal void Pump( Action<string> load, Func<double> elapsedMilliseconds, double budgetMilliseconds )
	{
		while ( paths.Count > 0 && elapsedMilliseconds() < budgetMilliseconds )
		{
			load( paths.Peek() );
			paths.Dequeue();
		}
	}
}
