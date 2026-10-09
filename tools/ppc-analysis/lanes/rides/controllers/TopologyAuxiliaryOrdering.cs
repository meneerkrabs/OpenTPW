namespace OpenTPW.PpcEvidence;

/// <summary>Supplied native node ordinal and descriptor flags; not a decoded file record.</summary>
public readonly record struct TopologyNodeObservation(int Ordinal, uint DescriptorFlags);
public readonly record struct AuxiliaryOrderingObservation(int AuxiliaryNodePosition, int OrdinaryRecordCount);

/// <summary>
/// Checks only the ordering prerequisite that permits the saver auxiliary block to precede
/// ordinary records as the loader expects. Passing is not proof of a valid native graph,
/// count/storage bounds, initial-node construction, file layout or decoder support.
/// </summary>
public static class TopologyAuxiliaryOrdering
{
    public static PrimitiveResult<AuxiliaryOrderingObservation> Check(IReadOnlyList<TopologyNodeObservation> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var auxiliaryCount = 0; var auxiliaryPosition = -1; var ordinaryCount = 0;
        for (var index = 0; index < nodes.Count; ++index)
        {
            var node = nodes[index];
            if (node.Ordinal < 0)
                return PrimitiveResult<AuxiliaryOrderingObservation>.Reject(PrimitiveStatus.InvalidSnapshot,
                    "negative supplied ordinal is outside the reference observation domain");
            if (node.Ordinal == 2) { ++auxiliaryCount; auxiliaryPosition = index; }
            if ((node.DescriptorFlags & 0x10) == 0) ++ordinaryCount;
        }
        if (auxiliaryCount != 1)
            return PrimitiveResult<AuxiliaryOrderingObservation>.Reject(PrimitiveStatus.UnsupportedSchema,
                $"ordering requires exactly one ordinal2 auxiliary node; observed {auxiliaryCount}");
        for (var index = 0; index < auxiliaryPosition; ++index)
            if ((nodes[index].DescriptorFlags & 0x10) == 0)
                return PrimitiveResult<AuxiliaryOrderingObservation>.Reject(PrimitiveStatus.UnsupportedSchema,
                    $"unfiltered node at position{index} would emit an ordinary record before the auxiliary block");
        return new(PrimitiveStatus.Applied, new(auxiliaryPosition, ordinaryCount),
            "ordering prerequisite only; native constructor invariants and complete decoder remain unsupported");
    }
}
