namespace OpenTPW;

/// <summary>A texture request of the page allocator: the fields of the native texture record the allocator reads or writes (<see cref="Id"/> stands for the record pointer, nonzero and unique).</summary>
internal sealed class PageTexture
{
	/// <summary>Identity of the native record; the original compares and lists pointers.</summary>
	public uint Id;

	/// <summary>Record +0x08. Bits 0x30 count the downsample steps left, 0x30000 mark a device dependent texture, 0x4000 and 0x80000 feed the rank key.</summary>
	public uint Flags;

	/// <summary>Record +0x14, a signed 16 bit field.</summary>
	public short Width;

	/// <summary>Record +0x16, a signed 16 bit field.</summary>
	public short Height;

	/// <summary>Record +0x30.</summary>
	public float Priority;

	/// <summary>Record +0x38: the rank key; the allocator only compares it signed and rewrites it after a downsample.</summary>
	public uint SortKey;
}

/// <summary>The allocator object (<c>this</c> of 0x58ECA0): the fields it reads are inputs, the fields it resets and fills are outputs.</summary>
internal sealed class PageAllocatorCache
{
	/// <summary>+0x18CC: bit 1 allows any request to be downsampled while no placement has been written yet; bit 2 pads every placement to the alignment mask.</summary>
	public uint Flags;

	/// <summary>+0x18E4.</summary>
	public int AlignmentMask;

	/// <summary>+0x18E8: nonzero makes the fit test use dimensions rounded up to the mask.</summary>
	public bool AlignmentFits;

	/// <summary>+0x1918, reset to 0 on entry: the largest request width the call looked at.</summary>
	public int MaxWidth;

	/// <summary>+0x191C, reset to 0 on entry: the largest request height the call looked at.</summary>
	public int MaxHeight;

	/// <summary>+0x1924, reset to 0 on entry: set when a request was downsampled.</summary>
	public bool Retry;
}

/// <summary>A placement record of the output array (20 bytes native): position and size on the page, and the request it belongs to.</summary>
internal readonly record struct PagePlacement( int X, int Y, int Width, int Height, uint Id );

/// <summary>A free rectangle of the allocator (20 bytes native): <see cref="Key"/> is <c>(Width &lt;&lt; 16) + Height</c>, compared unsigned.</summary>
internal struct PageFreeRect
{
	public int X, Y, Width, Height;
	public uint Key;
}

/// <summary>What 0x58ECA0 hands back: the return value, the placements and their count, and the request list as the call leaves it.</summary>
internal sealed class PageAllocation
{
	/// <summary>The return value: every request fit and the walk reached the end of the list.</summary>
	public bool Success;

	/// <summary>The placement records (the count is native out-parameter).</summary>
	public PagePlacement[] Placements = Array.Empty<PagePlacement>();

	/// <summary>The id of the record after the last placement, which the original stores as zero.</summary>
	public uint Terminator => 0;

	/// <summary>The request ids in the order the list holds them after the call (a downsample moves the request behind greater ranks).</summary>
	public uint[] Order = Array.Empty<uint>();
}

/// <summary>
/// The rectangle allocator tp.exe packs textures into a page with (<c>0x58ECA0</c>) and the integer key sort it keeps its free rectangles in (<c>0x57D090</c> into <c>0x57CED0</c>), ported instruction for instruction.
/// A building block for the page orchestration (0x58EAE0 / 0x58F190): nothing in the renderer calls it yet. ORACLE.md X91 checks the port against isolated native execution.
/// </summary>
// [BIN:TPW-EXE:0x58ECA0 allocator] free list starts as the whole page; each request takes the LAST free rectangle it fits (rounded to the mask when 0x18E8 is set); any request that fits nowhere may be downsampled and moved behind greater ranks while no placement has been written yet; leftovers above 8 stay free
// [BIN:TPW-EXE:0x590FB0 device test] flags & 0x30000 and the device kind global 0x8BD524 equal to 1
// [BIN:TPW-EXE:0x58CB40 rank key] ((ftol((1.5 - priority) * 2047) & 0xFFF) * 256 + width) * 256 + height, | 0x20000000 for flags 0x4000, | 0x40000000 for flags 0x80000; x87 control word 0x7F (single precision)
// [BIN:TPW-EXE:0x57CED0 sort] the OriginalDepthSort quicksort on 20 byte records by an unsigned dword key; the tie permutation is part of the result
internal static class OriginalPageAllocator
{
	/// <summary>The placement cap: the loop ends once 127 records are written (<c>cmp edx, 0x7f</c>).</summary>
	public const int MaxPlacements = 127;

	/// <summary>Every placement adds at most one free rectangle to the first one, and the loop stops at 127 placements.</summary>
	private const int FreeCapacity = MaxPlacements + 2;

	/// <summary>The leftover size above which a remainder stays a free rectangle (<c>cmp, 8</c>) and the width threshold of a device dependent texture's downsample (<c>[0x702434]</c>).</summary>
	private const int Slack = 8;

	/// <summary>
	/// 0x58ECA0. <paramref name="requests"/> is the pointer array (its length is the position of the null terminator) and is reordered in place on a downsample; <paramref name="cache"/> and the requests are
	/// mutated like the native objects. <paramref name="deviceKind"/> is the global 0x8BD524.
	/// </summary>
	public static PageAllocation Allocate( PageAllocatorCache cache, int pageWidth, int pageHeight, PageTexture[] requests, int deviceKind )
	{
		cache.MaxWidth = 0;
		cache.MaxHeight = 0;
		cache.Retry = false;

		var free = new PageFreeRect[FreeCapacity];
		free[0] = new PageFreeRect { X = 0, Y = 0, Width = pageWidth, Height = pageHeight, Key = unchecked((uint)((pageWidth << 16) + pageHeight)) };
		var freeCount = 1;
		var placements = new List<PagePlacement>();
		var allFits = true;
		var at = 0;
		var current = requests.Length > 0 ? requests[0] : null;
		var mask = cache.AlignmentMask;

		while ( current is not null && freeCount != 0 && placements.Count < MaxPlacements )
		{
			var width = (int)current.Width;
			var height = (int)current.Height;

			// the last free rectangle that fits; a descending walk that ends at -1
			var chosen = freeCount - 1;
			for ( ; chosen >= 0; chosen-- )
			{
				var fits = cache.AlignmentFits
					? ((width + mask) & ~mask) <= free[chosen].Width && ((height + mask) & ~mask) <= free[chosen].Height
					: width <= free[chosen].Width && height <= free[chosen].Height;
				if ( fits )
					break;
			}

			if ( placements.Count == 0 && chosen == -1 && (cache.Flags & 1) != 0 )
			{
				var deviceDependent = DeviceDependent( current, deviceKind );
				var shrinks = (current.Flags & 0x30) != 0 && (!deviceDependent || current.Width > Slack);
				if ( shrinks )
				{
					if ( !(deviceDependent && current.Width == Slack) )
					{
						current.Flags = unchecked(current.Flags - 0x10);
						current.Width = (short)((current.Width + 1) >> 1);
						current.Height = (short)((current.Height + 1) >> 1);
						RefreshRank( current );
					}

					// move the request behind the following requests of a strictly greater signed rank, then look at the list position again
					var stop = at + 1;
					while ( stop < requests.Length && unchecked((int)requests[stop].SortKey) > unchecked((int)current.SortKey) )
						stop++;
					for ( var shift = at + 1; shift < stop; shift++ )
						requests[shift - 1] = requests[shift];
					if ( stop > at + 1 )
						requests[stop - 1] = current;
					cache.Retry = true;
					current = requests[at];
					continue;
				}
			}

			if ( cache.MaxWidth < current.Width )
				cache.MaxWidth = current.Width;
			if ( cache.MaxHeight < current.Height )
				cache.MaxHeight = current.Height;

			if ( chosen < 0 )
			{
				allFits = false;
				current = ++at < requests.Length ? requests[at] : null;
				continue;
			}

			// the placement, padded to the mask only by the padding flag (independent of the fit gate)
			ref var slot = ref free[chosen];
			int usedWidth, usedHeight;
			if ( (cache.Flags & 2) != 0 )
			{
				var padWidth = ((width + mask) & ~mask) - width;
				var padHeight = ((height + mask) & ~mask) - height;
				usedWidth = padWidth + width;
				usedHeight = padHeight + height;
				placements.Add( new PagePlacement( (padWidth >> 1) + slot.X, (padHeight >> 1) + slot.Y, usedWidth, usedHeight, current.Id ) );
			}
			else
			{
				usedWidth = width;
				usedHeight = height;
				placements.Add( new PagePlacement( slot.X, slot.Y, usedWidth, usedHeight, current.Id ) );
			}

			var restWidth = slot.Width - usedWidth;
			var restHeight = slot.Height - usedHeight;
			var splitCode = (restWidth > Slack ? 2 : 0) | (restHeight > Slack ? 1 : 0);
			switch ( splitCode )
			{
				case 3:
					// the bottom rectangle is the used width wide and appended; the right one keeps the full old height
					free[freeCount] = new PageFreeRect { X = slot.X, Y = slot.Y + usedHeight, Width = usedWidth, Height = slot.Height - usedHeight };
					free[freeCount].Key = KeyOf( free[freeCount] );
					freeCount++;
					slot.X += usedWidth;
					slot.Width -= usedWidth;
					slot.Key = KeyOf( slot );
					break;
				case 2:
					slot.X += usedWidth;
					slot.Width -= usedWidth;
					slot.Key = KeyOf( slot );
					break;
				case 1:
					slot.Y += usedHeight;
					slot.Height -= usedHeight;
					slot.Key = KeyOf( slot );
					break;
				default:
					freeCount--;
					slot = free[freeCount];
					break;
			}

			if ( freeCount - chosen > 1 )
				SortDescending( free.AsSpan( chosen, freeCount - chosen ) );

			current = ++at < requests.Length ? requests[at] : null;
		}

		return new PageAllocation
		{
			Success = allFits && current is null,
			Placements = placements.ToArray(),
			Order = requests.Select( request => request.Id ).ToArray(),
		};
	}

	/// <summary>0x590FB0: a device dependent texture on device kind 1.</summary>
	internal static bool DeviceDependent( PageTexture texture, int deviceKind ) => (texture.Flags & 0x30000) != 0 && deviceKind == 1;

	/// <summary>0x58CB40: writes the rank key of <paramref name="texture"/> from its priority, size and flags.</summary>
	internal static void RefreshRank( PageTexture texture )
	{
		// x87 with control word 0x7F: both operations round to single precision; ftol truncates and the low dword is used; NaN and out of range priorities are outside the verified finite domain
		var inverted = 1.5f - texture.Priority;
		var scaled = inverted * 2047f;
		var low = unchecked((uint)(long)scaled);
		var key = unchecked((((low & 0xFFF) << 8) + (uint)(int)texture.Width) << 8) + unchecked((uint)(int)texture.Height);
		if ( (texture.Flags & 0x4000) != 0 )
			key |= 0x20000000;
		if ( (texture.Flags & 0x80000) != 0 )
			key |= 0x40000000;
		texture.SortKey = key;
	}

	private static uint KeyOf( in PageFreeRect rect ) => unchecked((uint)((rect.Width << 16) + rect.Height));

	/// <summary>0x57D090 into 0x57CED0: Hoare quicksort, descending by the unsigned key, not stable; <paramref name="rects"/> is the range [low, high] of the native call.</summary>
	internal static void SortDescending( Span<PageFreeRect> rects )
	{
		if ( rects.Length > 1 )
			SortDescending( rects, 0, rects.Length - 1 );
	}

	private static void SortDescending( Span<PageFreeRect> rects, int low, int high )
	{
		while ( true )
		{
			int i = low, j = high;
			var pivot = rects[(low + high) >> 1].Key;
			while ( true )
			{
				while ( i <= high && rects[i].Key > pivot )
					i++;
				while ( j > low && rects[j].Key < pivot )
					j--;
				if ( i > j )
					break;
				(rects[i], rects[j]) = (rects[j], rects[i]);
				i++;
				j--;
				if ( i > j )
					break;
			}
			if ( low < j )
				SortDescending( rects, low, j );
			if ( i >= high )
				return;
			low = i;
		}
	}
}
