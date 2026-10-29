namespace OpenTPW;

/// <summary>
/// Maps one saved coaster of an original Theme Park World save's Coasters block (<see cref="SaveCoaster"/>) onto the coaster runtime, as tp.exe's Coasters loader
/// <c>0x437B60</c> restores it onto the empty controller the RideSystem loader made for the station (docs/TPWS-PAYLOAD.md "Coasters block"). The station's three nodes
/// come from the placed station (the loader builds them from the station model with <c>0x436AC0</c>), every saved section becomes the pylon node the loader rebuilds with
/// <c>0x435020</c>, a complete track gets its end node, the cut and the controller words are restored, and the trains in service keep their saved slots.
/// A block the runtime cannot hold is refused with the reason, never imported half; the checks that only compare a saved word with what the rebuild re-derives are
/// reported as evidence lines.
/// </summary>
internal static class OriginalCoasterImport
{
	/// <summary>The import of one coaster: the mapped track and controller words, ready for <see cref="CoasterRide.ImportOriginal"/>, and the evidence lines.</summary>
	internal sealed record Plan( CoasterTrackSaveData Track, CoasterControllerState State, int BreakdownState, int SeatsPerTrain,
		IReadOnlyList<CoasterRide.OriginalTrain> Trains, IReadOnlyList<string> Evidence );

	/// <summary>The node flags a saved section's wire bits restore that the runtime keeps on a pylon node (the cut's link bits go with its markers).</summary>
	private const CoasterNodeFlags RestoredPylonFlags = CoasterNodeFlags.ManualYaw | CoasterNodeFlags.ManualIncline | CoasterNodeFlags.NoAutoBank | CoasterNodeFlags.CutEnd | CoasterNodeFlags.CutStart;

	/// <summary>
	/// Maps <paramref name="saved"/> for <paramref name="placed"/>, the coaster of the station the block binds to (its three station nodes placed, no pylon yet).
	/// Throws <see cref="InvalidDataException"/> with the reason when the block holds a shape the original's own editing never leaves or the runtime cannot hold.
	/// </summary>
	internal static Plan Map( SaveCoaster saved, CoasterRide placed )
	{
		ArgumentNullException.ThrowIfNull( saved );
		ArgumentNullException.ThrowIfNull( placed );
		var definition = placed.Definition;
		var track = placed.Track;
		var what = $"The saved coaster of station mesh {saved.StationMeshInstanceId} ({placed.Name})";
		if ( track.DepartCell == null || track.ReturnCell == null || track.Nodes.Count != CoasterTrack.StationNodeCount || !track.Nodes.All( node => node.IsStation ) )
			throw new InvalidDataException( $"{what} needs its station placed with exactly the three station nodes; the station's track holds {track.Nodes.Count} nodes." );
		if ( saved.TrainSlots is not { } slots )
			throw new InvalidDataException( $"{what} has an undecoded train payload: the coaster type's train and car counts were not known." );
		if ( slots.Count != definition.MaxTrains )
			throw new InvalidDataException( $"{what} holds {slots.Count} train slots; the coaster type has {definition.MaxTrains} (uiMaxTrains)." );
		var evidence = new List<string>();

		// [BIN:TPW-EXE:0x437B60 coaster loader] header +0 bit 0x1 is controller +0x3C bit 0 (the end node is built, 0x4380D4) and bit 0x2 bit 1 (the circuit is cut open: +0x60/+0x64 are restored, 0x4382A5); the demolish button that cuts (0x446740) is enabled only on a complete track whose circuit is not cut open (the button mask 0x445910 clears bit 0x40 on any other track), and a cut that reaches the chain's end clears both bits (0x4469FF), so a cut without completion is no shape the original leaves
		var complete = (saved.Flags & 0x1) != 0;
		var cut = (saved.Flags & 0x2) != 0;
		if ( cut && !complete )
			throw new InvalidDataException( $"{what} is cut open (wire 0x2) without a complete track (wire 0x1)." );
		if ( (saved.Flags & ~0x1FFu) != 0 )
			throw new InvalidDataException( $"{what} carries the unknown wire bits 0x{saved.Flags & ~0x1FFu:X} in its flag word." );
		// [APPROX:SAVE-921] the track flags (wire 0x4..0x100, controller +0 0x20..0x800) are not modelled by the coaster runtime beyond the trains-set-up bit (0x800); a scan of tp.exe's coaster module finds no user of the others outside the saver and the loader, and the import reports them
		if ( (saved.Flags & 0x1FC) != 0 )
			evidence.Add( $"{what}: the track flags 0x{(saved.Flags & 0x1FC) >> 2 << 5:X} (wire 0x{saved.Flags & 0x1FC:X}) are not modelled (SAVE-921)." );

		// The station's three nodes as the placement built them.
		var nodes = track.Nodes.Select( Copy ).ToList();

		// [BIN:TPW-EXE:0x442CD0 loop mode] a section with wire 0x8 (the first pylon of a loop pair) starts the loop mode (0x79C658 bit 0x40, the counter 0x79C664 = 2) when it is off and the coaster type has flag 2, which the type loader 0x42FAD0 sets when the LoopPylon control names a mesh (type +0x1D8); 0x435020 then takes the LoopPylon control (type +0x1C8, else the StdPylon +0xA8), decrements the counter after each node and leaves the mode at 0, so a pair's second node is a loop pylon too
		var loopModel = !string.IsNullOrEmpty( definition.LoopPylon.MeshName );
		var loopMode = false;
		var loopLeft = 0;
		var cutEnd = CoasterTrack.StationNodeCount - 1;
		int? cutStart = null;
		for ( var index = 0; index < saved.Sections.Count; index++ )
		{
			var section = saved.Sections[index];
			var wire = section.Wire;
			if ( wire.HasFlag( SaveCoasterSectionWire.LoopFirst ) && !loopMode && loopModel )
			{
				loopMode = true;
				loopLeft = CoasterBuilder.LoopPair;
			}
			var loop = loopMode;
			var pylon = definition.Pylon( loop );
			// [BIN:TPW-EXE:0x434060 loop pair] a node whose pylon control has flag 0x4000 gets node flags 0xE0 as the pair's first (its argument 2 − counter is 0) or 0x60 as the second; a trough control (0x8000) sets neither
			var constructed = loop && pylon.IsLoop
				? CoasterNodeFlags.Loop | CoasterNodeFlags.LoopMark | (loopLeft == CoasterBuilder.LoopPair ? CoasterNodeFlags.LoopFirst : CoasterNodeFlags.None)
				: CoasterNodeFlags.None;
			if ( loopMode && --loopLeft == 0 )
				loopMode = false;

			// [BIN:TPW-EXE:0x437EA6 section wire bits] the loader ORs node flags 1, 2, 4, 0x40000, 0x80000, 0x10000 (remembering the node as the cut's end) and 0x20000 (as its start), sets or clears 0x200000 by wire 0x400 and sets 0x400000 by wire 0x800
			var restored = (CoasterNodeFlags)SaveCoasterSectionFlags.ToNodeFlags( section.Flags );
			if ( wire.HasFlag( SaveCoasterSectionWire.CutEnd ) )
				cutEnd = CoasterTrack.StationNodeCount + index;
			if ( wire.HasFlag( SaveCoasterSectionWire.CutStart ) )
				cutStart = CoasterTrack.StationNodeCount + index;
			// [BIN:TPW-EXE:0x442C70 close circuit] the cut marks the node before it 0x50000 and the one after 0xA0000 (0x446740) and closing the circuit again clears both: a pylon's link bits come only with its cut marker, which the runtime's markers stand for
			if ( wire.HasFlag( SaveCoasterSectionWire.NoNextLink ) && !wire.HasFlag( SaveCoasterSectionWire.CutEnd )
				|| wire.HasFlag( SaveCoasterSectionWire.NoPreviousLink ) && !wire.HasFlag( SaveCoasterSectionWire.CutStart ) )
				throw new InvalidDataException( $"{what}: section {index} carries a link bit without its cut marker (wire 0x{section.Flags:X})." );
			// [APPROX:COAST-004] the self-intersection marks (wire 0x400/0x800, node flags 0x200000/0x400000) and the saved collision lists are what the original's pass re-tests from; the runtime's pass re-derives every list, so they are compared, not imported
			if ( (section.Flags & (uint)(SaveCoasterSectionWire.IntersectionMarked | SaveCoasterSectionWire.IntersectionOverflow)) != 0 )
				evidence.Add( $"{what}: section {index} carries the self-intersection marks 0x{section.Flags & 0xC00:X}; the rebuild re-derives the crossings (COAST-004)." );

			// [BIN:TPW-EXE:0x444270 bank setter] the loader passes (bank, incline) or, for a section with wire 0x10, (loop rotate, 0) (0x437E72); the setter stores them as bank +0xD8 and incline +0xDC with +0xF8 cleared on a node without flag 0x40, and on a loop node clears bank and incline and stores its first argument as the loop rotate +0xF8
			var first = wire.HasFlag( SaveCoasterSectionWire.Loop ) ? section.LoopRotate : section.Bank;
			var second = wire.HasFlag( SaveCoasterSectionWire.Loop ) ? 0f : section.Incline;
			var loopNode = (constructed & CoasterNodeFlags.Loop) != 0;
			var flags = constructed | (restored & RestoredPylonFlags);
			// [BIN:TPW-EXE:0x444150 height setter] the loft parameter t goes to node +0xE8 as saved; the pylon's height is h0 + t(h1 − h0) of its control; [BIN:TPW-EXE:0x4441D0 yaw setter] the yaw goes to +0xE0 as saved
			nodes.Add( new CoasterNodeSaveData( section.CellX, section.CellY, CoasterTrack.PylonHeight( pylon, section.HeightParameter ), section.HeightParameter, loop, section.StackOrdinal,
				IsStation: false, IsStationEnd: false, (uint)flags, loopNode ? 0f : first,
				// a pylon outside a loop pair keeps the runtime's default loop rotate, as a pylon the coaster tool places (the original's +0xF8 of such a node is 0 and nothing reads it)
				LoopRotate: loopNode ? first : CoasterTrack.DefaultLoopRotate,
				Yaw: section.Yaw, Incline: loopNode ? 0f : second ) );
		}

		// [BIN:TPW-EXE:0x4380D4 end node] with wire 0x1 the loader sets controller +0x3C bit 0 (0x4380E7) and, with type flag 0x20 (which the type loader always sets, 0x42F390 ORs 0xA0), builds one pylon-less end node at the station's first node's pylon track dummy with the creation parameters 0x144
		if ( complete )
		{
			var head = nodes[0];
			nodes.Add( new CoasterNodeSaveData( head.X, head.Y, head.Height, 0, false, 0, IsStation: false, IsStationEnd: true, (uint)CoasterTrack.StationEndFlags, 0 ) );
		}

		int? cutAfter = null;
		if ( cut )
		{
			// [BIN:TPW-EXE:0x4382A5 cut restore] the cut's end node (+0x60) is the last section with wire 0x40, else the third station node S2 (0x437CE6; S2 then gets 0x50000, 0x4382C3), and its start (+0x64) the last section with wire 0x80, else the chain's tail as it stood before the sections were added (+0x58 read at 0x437CEC), which is S2 again; the cut 0x446740 always marks the two nodes that border the removed range, so they are neighbours and a cut whose start no section names is no shape the original leaves
			var start = cutStart ?? CoasterTrack.StationNodeCount - 1;
			if ( start != cutEnd + 1 || nodes[start].IsStationEnd )
				throw new InvalidDataException( $"{what} is cut open between chain nodes {cutEnd} and {start}, which are not neighbouring pylon nodes." );
			cutAfter = cutEnd;
		}
		// the cut's markers sit on the two nodes that border it, as the runtime's cut leaves them
		for ( var index = 0; index < nodes.Count; index++ )
		{
			var markers = (CoasterNodeFlags)nodes[index].Flags & ~(CoasterNodeFlags.CutEnd | CoasterNodeFlags.CutStart);
			if ( cutAfter == index )
				markers |= CoasterNodeFlags.CutEnd;
			if ( cutAfter + 1 == index )
				markers |= CoasterNodeFlags.CutStart;
			nodes[index] = nodes[index] with { Flags = (uint)markers };
		}

		// [BIN:TPW-EXE:0x438340 controller words] the state word (+8: the state in the low nibble, the ride mode 16, 32 or 64 above it) and the power factor +0x2C are restored as saved; completion, closing and the tool's entry set state 2 and the cut sets 1 (0x435730), and the mode setter sets the power factor to 0 in mode 64 and to 1 otherwise
		var stateWord = saved.Controller8;
		var state = (CoasterControllerState)(stateWord & 0xF);
		var runnable = complete && !cut;
		if ( !Enum.IsDefined( state ) )
			throw new InvalidDataException( $"{what} holds the state word 0x{stateWord:X}, whose state {stateWord & 0xF} is not 1, 2, 4 or 8." );
		if ( runnable == (state == CoasterControllerState.Incomplete) )
			throw new InvalidDataException( $"{what} holds state {(int)state} for a track that is {(runnable ? "complete and closed" : "not runnable")}." );
		var breakdown = (stateWord & 0x70) switch
		{
			0x10 => 0,
			0x40 => 1,
			0x20 => 2,
			_ => throw new InvalidDataException( $"{what} holds the state word 0x{stateWord:X}, which names no single ride mode 16, 32 or 64." ),
		};
		if ( (stateWord & ~0x7Fu) != 0 )
			throw new InvalidDataException( $"{what} holds the state word 0x{stateWord:X} with bits beyond the state and the mode." );
		// [BIN:TPW-EXE:0x4368F0 never-run power] the constructor's controller is zero-filled (GlobalAlloc 0x40) and its first 0x435730(ctl, 0x11) leaves +0x2C at 0: only the first 1->2 transition
		// (0x43577D, mode 0x40 clear) or a 0x40->0x10/0x20 step (0x435931, 0x43597F) sets 1.0, so a coaster that never ran is saved in state 1, mode 16 or 32, with power 0 or 1
		var neverRunPower = state == CoasterControllerState.Incomplete && breakdown != 1 && saved.PowerFactor == 0f;
		if ( saved.PowerFactor != (breakdown == 1 ? 0f : 1f) && !neverRunPower )
			throw new InvalidDataException( $"{what} holds the power factor {saved.PowerFactor} in ride mode {(stateWord & 0x70)}." );
		// [BIN:TPW-EXE:0x43B330 coaster capacity] the seats per train (+0xF0) are what COAST 6 or the constructor stored: at least 1 and at most one train's and the fleet's head sockets
		var seatBound = Math.Min( definition.HeadsPerTrain, definition.FleetHeads );
		if ( saved.SeatsPerTrain < 1 || saved.SeatsPerTrain > seatBound )
			throw new InvalidDataException( $"{what} holds {saved.SeatsPerTrain} seats per train; COAST 6 keeps them in 1..{seatBound}." );

		// [BIN:TPW-EXE:0x438386 train slots] every one of the type's train slots is read; the train passes run over the trains in service (+0xF4, 0x435784, 0x43CD36), the slots the rating pass set up (bit 0)
		var trains = new List<CoasterRide.OriginalTrain>();
		var inService = saved.TrainsInService;
		if ( runnable )
		{
			if ( inService > slots.Count )
				throw new InvalidDataException( $"{what} holds {inService} trains in service but {slots.Count} train slots." );
			for ( var index = 0; index < inService; index++ )
			{
				var slot = slots[index];
				if ( (slot.Flags & 0x1) == 0 )
					throw new InvalidDataException( $"{what}: train {index} is in service but its slot is not set up (flags 0x{slot.Flags:X})." );
				if ( slot.CarCount != definition.CarCount )
					throw new InvalidDataException( $"{what}: train {index} holds {slot.CarCount} cars; the coaster type's train has {definition.CarCount}." );
				if ( !float.IsFinite( slot.Position ) || slot.Position < 0 || !float.IsFinite( slot.Speed ) || slot.Speed < 0 )
					throw new InvalidDataException( $"{what}: train {index} stands at {slot.Position} with the speed {slot.Speed}." );
				// [BIN:TPW-EXE:0x43CE20 train flags] the train's flags 0x10 (braking), 0x20 (in the station), 0x40 (bumped) and 0x80 (headway) are what the train step and the station step read; bits 0, 1 and 2 mark the slot set up, which every train in service is
				trains.Add( new CoasterRide.OriginalTrain( slot.Position, slot.Speed, (CoasterTrainFlags)(slot.Flags & 0xF0), slot.Word38, (slot.Flags & 0x100) != 0 ) );
			}
			for ( var index = inService; index < slots.Count; index++ )
			{
				if ( (slots[index].Flags & 0x1) != 0 )
					evidence.Add( $"{what}: train slot {index} beyond the {inService} trains in service is still set up (flags 0x{slots[index].Flags:X}); the train passes do not run it." );
			}
		}
		else if ( inService != 0 )
			evidence.Add( $"{what}: {inService} trains in service on a track that does not run; the state change to 1 removed them (0x43BF20)." );
		// [APPROX:SAVE-920] the riders of the saved trains (each car's two guest lists and the train's rider count +0x10) are not restored: the guests riding or seated (states 15 and 16) are not imported (SAVE-002)
		var riders = slots.Take( runnable ? inService : 0 ).Sum( slot => slot.Cars.Sum( car => car.HeadsA.Length + car.HeadsB.Length ) );
		if ( riders > 0 )
			evidence.Add( $"{what}: {riders} riders on its trains are not restored (SAVE-920); their guests are not imported." );

		return new Plan( new CoasterTrackSaveData( track.DepartCell, track.ReturnCell, complete && !cut, complete, track.Version + 1, nodes, cutAfter ),
			state, breakdown, saved.SeatsPerTrain, trains, evidence );
	}

	/// <summary>The saved words the rebuild re-derives, compared with what it derived: the self-intersection count and the collision lists (the auxiliary block is station node S2's).</summary>
	internal static IReadOnlyList<string> Compare( SaveCoaster saved, CoasterRide ride )
	{
		var lines = new List<string>();
		var what = $"The saved coaster of station mesh {saved.StationMeshInstanceId} ({ride.Name})";
		if ( !ride.Track.IsRunnable )
			return lines;
		if ( saved.IntersectionCount != ride.IntersectionCount )
			lines.Add( $"{what} saved {saved.IntersectionCount} self-intersections (controller +0x140); the rebuild finds {ride.IntersectionCount}." );
		var savedLists = new List<IReadOnlyList<int>>();
		for ( var index = 0; index < ride.Track.Nodes.Count; index++ )
		{
			var ordinals = index == CoasterTrack.StationNodeCount - 1 ? saved.AuxiliaryOrdinals
				: index >= CoasterTrack.StationNodeCount && index - CoasterTrack.StationNodeCount < saved.Sections.Count ? saved.Sections[index - CoasterTrack.StationNodeCount].CollisionOrdinals
				: Array.Empty<ushort>();
			savedLists.Add( ordinals.Select( ordinal => (int)ordinal ).Order().ToList() );
		}
		var derived = ride.CollisionLists;
		var differ = Enumerable.Range( 0, savedLists.Count ).Count( index => !savedLists[index].SequenceEqual( index < derived.Count ? derived[index].Order() : Enumerable.Empty<int>() ) );
		if ( differ > 0 )
			lines.Add( $"{what}: the saved collision lists of {differ} nodes differ from the rebuild's (COAST-004)." );
		return lines;
	}

	private static CoasterNodeSaveData Copy( CoasterNode node ) => new( node.X, node.Y, node.Height, node.T, node.LoopPylon, node.StackIndex, node.IsStation, node.IsStationEnd, (uint)node.Flags, node.Bank, node.LoopRotate,
		node.ExactPosition?.X, node.ExactPosition?.Y, node.ExactPosition?.Z, node.Yaw, node.Incline );
}
