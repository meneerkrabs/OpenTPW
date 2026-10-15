namespace OpenTPW;

/// <summary>
/// The camera seat of a ride model: the model node the ride view's eye is taken from.
/// </summary>
public static class RideSeat
{
	/// <summary>Bit 0x1000 of a dummy-attribute record's type: a camera (seat) record.</summary>
	public const uint TypeFlag = 0x1000;

	/// <summary>The seat number the ride view asks for.</summary>
	public const int Number = 1;

	/// <summary>
	/// The node of the first dummy-attribute record whose type has <see cref="TypeFlag"/> and whose value is <paramref name="number"/>; -1 without one.
	/// </summary>
	// [BIN:TPW-EXE:0x44b220 attribute lookup] the first record (in order) whose value equals the argument and whose type has a bit of the mask; the record index plus the u16 at model header +0x46 is the node (a mesh node below the header's mesh count, a dummy node after)
	// [BIN:TPW-EXE:0x42a560 ride camera] the ride camera looks the seat up with 0x44b220(model, 0x1000, n) and keeps the node's matrix pointer (0x790a30) as the camera frame; the ride WADs carry such records, mostly type 0x1031 value 1
	public static int FindNode( ModelFile model, int number = Number )
	{
		ArgumentNullException.ThrowIfNull( model );
		for ( var index = 0; index < model.DummyAttributes.Count; index++ )
		{
			var attribute = model.DummyAttributes[index];
			if ( (attribute.Type & TypeFlag) == 0 || attribute.Value != unchecked((uint)number) )
				continue;
			var node = model.Counts.Unknown46 + index;
			return node < model.Nodes.Count ? node : -1;
		}
		return -1;
	}

	/// <summary>The seat of a placed object's model, or -1 when its model has none or its pose is not available.</summary>
	public static int FindNode( OriginalObject ride )
	{
		ArgumentNullException.ThrowIfNull( ride );
		var node = FindNode( ride.Runtime.Model );
		return node >= 0 && node < ride.NodeTransforms.Count ? node : -1;
	}
}
