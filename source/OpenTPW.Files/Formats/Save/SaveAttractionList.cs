namespace OpenTPW;

/// <summary>
/// The model-3 things of a save's World thing list, in list order (<see cref="SaveAttraction"/>). The chain walk
/// (<see cref="SaveThingChain"/>) has already parsed every body completely, so there is no signature search.
/// </summary>
public static class SaveAttractionList
{
	public static IReadOnlyList<SaveAttraction> Parse( ReadOnlySpan<byte> payload, SaveWorld world )
	{
		ArgumentNullException.ThrowIfNull( world );
		var result = new List<SaveAttraction>();
		foreach ( var thing in world.OfModel( SaveThingModel.Attraction ) )
			result.Add( SaveAttraction.Read( payload[..thing.End], thing.BodyOffset, thing.Id ) );
		return result.AsReadOnly();
	}
}
