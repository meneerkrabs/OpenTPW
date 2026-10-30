namespace OpenTPW;

/// <summary>
/// How many train slots a coaster type has and how many cars each train holds: the two counts the original coaster saver
/// (tp.exe <c>0x437300</c>) and loader (<c>0x437B60</c>) loop over and the file does not carry. The type's <c>+0x2E8</c> is
/// <c>uiMaxTrains</c> (the train slots, allocated by <c>0x436D10</c>) and its train definition's <c>+4</c> (the type's
/// <c>+0x2F4</c>) is <c>sTrainType.uiMaxCars</c>.
/// </summary>
public readonly record struct SaveCoasterTrainLayout( int TrainSlots, int CarsPerTrain )
{
	/// <summary>Bound on the train slots of one coaster (the shipped coasters have at most 20).</summary>
	public const int MaximumTrainSlots = 256;
	/// <summary>Bound on the cars of one train (the shipped trains have at most 6).</summary>
	public const int MaximumCarsPerTrain = 64;

	public bool IsValid => TrainSlots is >= 0 and <= MaximumTrainSlots && CarsPerTrain is >= 0 and <= MaximumCarsPerTrain;
}

/// <summary>
/// One car of a saved train: the 11-byte record of tp.exe's coaster saver (<c>0x4378A5</c>) and the two lists after it. The words keep their
/// native offsets in the car structure. <see cref="MeshInstanceId"/> is car <c>+0xC</c>, the car's entry in the object table <c>0x7A4610</c> that the
/// loader re-attaches (<c>0x463AB0</c> at <c>0x4384CF</c>) instead of creating a model. <see cref="HeadsA"/> and <see cref="HeadsB"/> are the entries of the
/// two per-car buffers (car <c>+0x28</c> and <c>+0x2C</c>, one entry per head socket) counted by the bytes at car <c>+0x30</c> and <c>+0x34</c>: the riders on the
/// lap and the guests seated at the station, who become the riders when the train leaves (<c>0x43B8B0</c>, CoasterTrain).
/// </summary>
public sealed record SaveCoasterCar( int Offset, uint Flags, byte HeadCountA, byte HeadCountB, byte Car44, uint MeshInstanceId, uint[] HeadsA, uint[] HeadsB )
{
	public const int RecordBytes = 11;

	/// <summary>The bytes this car takes in the file: its record and both lists.</summary>
	public int Bytes => RecordBytes + 4 * (HeadsA.Length + HeadsB.Length);
}

/// <summary>
/// One train slot of a saved coaster: the 27-byte record of tp.exe's coaster saver (<c>0x43783B</c>, the loader <c>0x437B60</c>) and its cars. The words keep their native
/// offsets in the train slot structure (128 bytes, array at controller <c>+0x128</c>). <see cref="Cars"/> is the byte at train <c>+0xC</c> (the car count the
/// slot was set up with), <see cref="Riders"/> the word at train <c>+0x10</c> (the riders on the train, COAST-plan §10); the loader sets bit <c>0x4</c> of
/// <see cref="Flags"/> after reading it. The other words (train <c>+0x74</c>, <c>+0x78</c>, <c>+0x38</c>, <c>+0x64</c>, <c>+0x68</c>) are kept raw; the flags word's 0x100 is the train's scream flag.
/// </summary>
// [BIN:TPW-EXE:0x43780A train sound words] the saver writes only +0x38 (the sound state of 0x439780), +0x64 and +0x68 (the two loop handles) next to the flags word (0x4377F4, which holds the scream flag 0x100) and the loader restores exactly those (0x4383DA, 0x4383FE..0x438412); no slot setup follows for a set-up slot, so a loaded train is silent until its next setup. CoasterTrainSounds seeds the state and the flag (CoasterRide.ImportOriginal). The saved handles are numbers of the previous session's sound library; whichever voice carries the same number after the load is reached by the per-tick calls (their returns are stored back, 0x4395C9, 0x43962E, 0x43963D), which is the handle numbering of AUDIO-018
// [BIN:TPW-EXE:0x43BA90 car seat share] car +0x44 is the car's seat share (the seats per train, +0xF0, split over the centre-type cars by 0x43BA90 whenever the seats change, the last share taking the rest) and car +0 its flag word; the loader restores the saved shares raw (0x4384A3) and runs 0x43BA90 without the share pass (0x4385DE), so a loaded coaster's shares are the ones its formula gave, which the import's seats per train reproduce
public sealed record SaveCoasterTrain( int Offset, uint Flags, byte CarCount, uint Word74, uint Word78, uint Word38, uint Word64, uint Word68, ushort Riders,
	IReadOnlyList<SaveCoasterCar> Cars )
{
	public const int RecordBytes = 27;

	/// <summary>Train <c>+0x74</c> as a float: the reference car's position in samples from the station node.</summary>
	public float Position => BitConverter.UInt32BitsToSingle( Word74 );

	/// <summary>Train <c>+0x78</c> as a float: the speed.</summary>
	public float Speed => BitConverter.UInt32BitsToSingle( Word78 );

	/// <summary>The bytes this slot takes in the file: its record and every car.</summary>
	public int Bytes => RecordBytes + Cars.Sum( car => car.Bytes );

	/// <summary>
	/// Reads one coaster's train payload: <paramref name="layout"/>.TrainSlots records, each followed by CarsPerTrain cars (an 11-byte record and then its A and B
	/// lists, counted by the record's two bytes), exactly as the loader (<c>0x437B60</c>) reads them. Throws <see cref="InvalidDataException"/> on truncation
	/// </summary>
	internal static IReadOnlyList<SaveCoasterTrain> ReadAll( ReadOnlySpan<byte> data, ref int position, SaveCoasterTrainLayout layout, int baseOffset, string what )
	{
		if ( !layout.IsValid )
			throw new InvalidDataException( $"Save {what} has an invalid train layout ({layout.TrainSlots} slots of {layout.CarsPerTrain} cars)." );
		var trains = new SaveCoasterTrain[layout.TrainSlots];
		for ( var slot = 0; slot < trains.Length; slot++ )
		{
			var reader = new SaveFieldReader( data, position, $"{what} train slot {slot}" );
			var offset = baseOffset + position;
			var flags = reader.U32();
			var cars = reader.U8();
			var word74 = reader.U32();
			var word78 = reader.U32();
			var word38 = reader.U32();
			var word64 = reader.U32();
			var word68 = reader.U32();
			var riders = reader.U16();
			position = reader.Position;
			var list = new SaveCoasterCar[layout.CarsPerTrain];
			for ( var car = 0; car < list.Length; car++ )
				list[car] = ReadCar( data, ref position, baseOffset, $"{what} train slot {slot} car {car}" );
			trains[slot] = new SaveCoasterTrain( offset, flags, cars, word74, word78, word38, word64, word68, riders, list );
		}
		return trains;
	}

	private static SaveCoasterCar ReadCar( ReadOnlySpan<byte> data, ref int position, int baseOffset, string what )
	{
		var reader = new SaveFieldReader( data, position, what );
		var offset = baseOffset + position;
		var flags = reader.U32();
		var countA = reader.U8();
		var countB = reader.U8();
		var byte44 = reader.U8();
		var mesh = reader.U32();
		var listA = new uint[countA];
		for ( var index = 0; index < listA.Length; index++ )
			listA[index] = reader.U32();
		var listB = new uint[countB];
		for ( var index = 0; index < listB.Length; index++ )
			listB[index] = reader.U32();
		position = reader.Position;
		return new SaveCoasterCar( offset, flags, countA, countB, byte44, mesh, listA, listB );
	}
}
