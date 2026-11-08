namespace OpenTPW;

/// <summary>
/// A texture record of the page-list merge (<c>0x58EAE0</c>): the next link at <c>+0x4</c>, the flags and ordering words at <c>+0x8</c>/<c>+0x10</c>/<c>+0x12</c>, and the signed size at <c>+0x14</c>/<c>+0x16</c>.
/// <see cref="Id"/> stands for the native record pointer, nonzero and unique.
/// </summary>
internal sealed class PageTextureNode
{
	/// <summary>Identity of the native record; the original writes record pointers into its output array.</summary>
	public uint Id;

	/// <summary>Record +0x8. The merge itself only tests <c>0x40000000</c>; the device predicate also reads <c>0x30000</c>.</summary>
	public uint Flags;

	/// <summary>Record +0x10, a signed 16-bit ordering word.</summary>
	public short U;

	/// <summary>Record +0x12, a signed 16-bit ordering word.</summary>
	public short V;

	/// <summary>Record +0x14, a signed 16-bit width.</summary>
	public short Width;

	/// <summary>Record +0x16, a signed 16-bit height.</summary>
	public short Height;

	/// <summary>Record +0x4: the next record of its list.</summary>
	public PageTextureNode? Next;
}

/// <summary>
/// The list merge tp.exe applies before it packs a frame's textures onto a page (<c>0x58EAE0</c>), ported instruction for instruction and checked against isolated native execution (ORACLE.md X92).
/// A building block for the page orchestration (<c>0x58F190</c>): nothing in the renderer calls it yet.
/// </summary>
// [BIN:TPW-EXE:0x58EAE0 page-list merge] arg1 is the output array (padded plus a 0 terminator), arg2 the pending head and arg3 a container whose +0x4 is the existing head; `this` is unused and the routine returns 0xC
// [BIN:TPW-EXE:0x58EB06 leading skip] when the existing head is device dependent (0x590FB0) it is skipped once, before the merge starts
// [BIN:TPW-EXE:0x58EB30 existing filter] the existing list contributes only records whose flags have 0x40000000; the others are walked past without being emitted
// [BIN:TPW-EXE:0x58EB48 pending filter] the pending list drops device dependent records (0x590FB0)
// [BIN:TPW-EXE:0x58EB6A ordering] descending by the signed word +0x12, then by the signed word +0x10; a full tie emits the existing record first
// [BIN:TPW-EXE:0x58EC83 return] the sum of width x height of the emitted records, added as unchecked 32-bit products
internal static class OriginalPageListMerge
{
	/// <summary>Record flag <c>0x40000000</c>: an existing-list record is emitted only with it (<c>test dword ptr [esi+8], 0x40000000</c>).</summary>
	public const uint ExistingOnly = 0x40000000;

	/// <summary>
	/// <c>0x58EAE0</c>: merges <paramref name="pendingHead"/> with <paramref name="existingHead"/> into emission order.
	/// Returns the emitted records in the order the original writes them and the unchecked area sum the original returns.
	/// Neither list is modified.
	/// </summary>
	public static (PageTextureNode[] Order, int Area) Merge( PageTextureNode? existingHead, PageTextureNode? pendingHead, int deviceKind )
	{
		var order = new List<PageTextureNode>();
		var area = 0;

		var existing = existingHead;
		if ( existing is not null && OriginalPageAllocator.DeviceDependent( existing.Flags, deviceKind ) )
			existing = existing.Next;

		var pending = pendingHead;

		if ( existing is not null )
		{
			while ( true )
			{
				// the pending list ran out first: only the remaining existing records can still be emitted
				if ( pending is null )
				{
					DrainExisting( existing, order, ref area );
					return (order.ToArray(), area);
				}

				while ( existing is not null && (existing.Flags & ExistingOnly) == 0 )
					existing = existing.Next;
				if ( existing is null )
					break;

				while ( pending is not null && OriginalPageAllocator.DeviceDependent( pending.Flags, deviceKind ) )
					pending = pending.Next;

				if ( pending is not null )
				{
					// descending by (V, U); on a full tie the existing record goes first
					if ( pending.V > existing.V || (pending.V == existing.V && pending.U > existing.U) )
					{
						Emit( pending, order, ref area );
						pending = pending.Next;
					}
					else
					{
						Emit( existing, order, ref area );
						existing = existing.Next;
					}
				}

				if ( existing is null )
					break;
			}
		}

		// the existing list is empty (or ran out): the remaining pending records are emitted unless they are device dependent;
		// the existing drain that follows in the original then has nothing left to walk
		DrainPending( pending, deviceKind, order, ref area );
		return (order.ToArray(), area);
	}

	private static void DrainPending( PageTextureNode? pending, int deviceKind, List<PageTextureNode> order, ref int area )
	{
		for ( ; pending is not null; pending = pending.Next )
			if ( !OriginalPageAllocator.DeviceDependent( pending.Flags, deviceKind ) )
				Emit( pending, order, ref area );
	}

	private static void DrainExisting( PageTextureNode? existing, List<PageTextureNode> order, ref int area )
	{
		for ( ; existing is not null; existing = existing.Next )
			if ( (existing.Flags & ExistingOnly) != 0 )
				Emit( existing, order, ref area );
	}

	private static void Emit( PageTextureNode node, List<PageTextureNode> order, ref int area )
	{
		order.Add( node );
		area = unchecked( area + node.Width * node.Height );
	}
}
