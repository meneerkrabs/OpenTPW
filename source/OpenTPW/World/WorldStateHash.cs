using System.Buffers;
using System.Text.Json;

namespace OpenTPW;

/// <summary>FNV-1a 64 over little-endian values; floats are hashed by bit pattern, never with a tolerance.</summary>
public sealed class StateHasher
{
	private const ulong Prime = 1099511628211UL;
	/// <summary>The prime to the 7th and the 8th power: zero bytes only multiply by the prime (x ^ 0 = x), so a value's upper zero bytes hash as one multiplication.</summary>
	private const ulong Prime7 = 0xC5527B8A51D3D2DBUL, Prime8 = 0x1EFAC7090AEF4A21UL;

	public ulong Value { get; private set; } = 14695981039346656037UL;

	public void Add( long value )
	{
		for ( var shift = 0; shift < 64; shift += 8 )
			AddByte( (byte)(value >> shift) );
	}

	public void Add( ulong value ) => Add( unchecked((long)value) );
	public void Add( bool value ) => Add( value ? 1L : 0L );
	public void Add( float value ) => Add( BitConverter.SingleToInt32Bits( value ) );
	public void Add( double value ) => Add( BitConverter.DoubleToInt64Bits( value ) );

	/// <summary>The length, then each character as <see cref="Add(long)"/> hashes it: its two bytes and six zero bytes.</summary>
	public void Add( string value )
	{
		Add( value.Length );
		var hash = Value;
		foreach ( var character in value )
			hash = (((hash ^ (byte)character) * Prime) ^ (byte)(character >> 8)) * Prime7;
		Value = hash;
	}

	/// <summary>ASCII text given as its bytes, hashed as <see cref="Add(string)"/> hashes the same characters (a character below 128: its byte and seven zero bytes).</summary>
	internal void AddAscii( ReadOnlySpan<byte> text )
	{
		Add( text.Length );
		var hash = Value;
		foreach ( var character in text )
			hash = (hash ^ character) * Prime8;
		Value = hash;
	}

	private void AddByte( byte value )
	{
		Value ^= value;
		Value *= Prime;
	}
}

/// <summary>The parts of a running park the canonical hash reads; absent parts are hashed as absent.</summary>
public sealed record WorldStateSources
{
	public WorldSeed Seed { get; init; } = WorldSeed.Default;
	public ParkEconomy? Economy { get; init; }
	public GuestSimulation? Guests { get; init; }
	public RideScriptWorld? Scripts { get; init; }
}

/// <summary>
/// Canonical state hash of a park run (docs/DETERMINISM.md): the subset of the replay fields of
/// docs/reverse/DET-plan.md §4.5 that OpenTPW has today, in that order. Same world seed and same inputs give
/// the same hash, in one process or in separate ones; a different seed gives a different one. Fields the
/// original has but OpenTPW lacks (clock A, substep and manager-pass counters, the C-library stream, world
/// state, calendar rate, input log) and OpenTPW state outside the hash (sound seed, object
/// placements and run flags, the clock's pending time) are listed in docs/DETERMINISM.md.
/// </summary>
public static class WorldStateHash
{
	/// <summary>Bumped whenever the hashed fields or their order change.</summary>
	public const int SchemaVersion = 21;

	public static ulong Compute( WorldStateSources sources )
	{
		ArgumentNullException.ThrowIfNull( sources );
		var hash = new StateHasher();
		hash.Add( SchemaVersion );
		hash.Add( sources.Seed.Value );
		// substep_counter: OpenTPW's 60 Hz fixed-step count, kept by the guest simulation.
		hash.Add( sources.Guests?.TickCount ?? -1 );
		var economy = sources.Economy;
		hash.Add( economy != null );
		if ( economy != null )
			hash.Add( economy.Turn );
		// world_rng_state: the one world generator (tp.exe 0x00516330) every system draws from; the economy's, else the guests', else the scripts'.
		hash.Add( (economy?.World ?? sources.Guests?.World ?? sources.Scripts?.World)?.State ?? 0 );
		// sound_seed is not hashed: it is presentation state that draws never move (sound_shared 0x100118C8 is
		// its only store); the park save keeps it.
		if ( economy != null )
		{
			hash.Add( (int)economy.Speed );
			hash.Add( economy.Speed == GameSpeed.Paused );
			hash.Add( (int)economy.Mode );
		}
		hash.Add( ParkCalendar.Epoch.Ticks );
		// thing_table_digest: guests in id order, then the registered attractions.
		hash.Add( sources.Guests != null );
		// the sections' work by name, for a measured frame (FrameMarks: a boolean test while off)
		using ( FrameMarks.Measure( "hash.guests" ) )
			sources.Guests?.AddCanonicalState( hash );
		// cell_map_digest: the park cell map (path and queue cells, flags, placement counters, links) guests walk on.
		using ( FrameMarks.Measure( "hash.cells" ) )
			sources.Guests?.Grid.Cells.AddCanonicalState( hash );
		// script_table_digest: every live script in id order.
		hash.Add( sources.Scripts != null );
		if ( sources.Scripts != null )
		{
			hash.Add( sources.Scripts.NextAttractionId );
			hash.Add( sources.Scripts.ClockMilliseconds );
			hash.Add( sources.Scripts.Scripts.Count );
			// the script pass counter (which pass of eight a script's slice falls on) joins the hash once a pass has run, so a world that never ran one keeps its pin
			if ( sources.Scripts.PassCounter != 0 )
				hash.Add( sources.Scripts.PassCounter );
			using ( FrameMarks.Measure( "hash.scripts" ) )
				foreach ( var script in sources.Scripts.Scripts.OrderBy( script => script.ScriptId ) )
					script.AddCanonicalState( hash );
			// coaster_rng_state and the coasters (tracks, samples, ratings, trains and riders) in creation order.
			using ( FrameMarks.Measure( "hash.coasters" ) )
				sources.Scripts.Coasters.AddCanonicalState( hash );
			// the kart and water ride controllers join the hash only while one exists (every other park keeps its pins)
			if ( sources.Scripts.Bumpers.Controllers.Count > 0 )
				sources.Scripts.Bumpers.AddCanonicalState( hash );
			// likewise the tour rides' flying-car controllers
			if ( sources.Scripts.Tours.Controllers.Count > 0 )
				sources.Scripts.Tours.AddCanonicalState( hash );
		}
		// economy_digest: everything the park save stores, with object keys sorted.
		if ( economy != null )
			AddEconomy( hash, economy );
		// [EXT:smarter-guests] the identity state joins the hash only while the extension or a script mod is on, so every park without it keeps its pins and SchemaVersion
		if ( sources.Guests?.SmarterGuests is { } smarter )
			smarter.AddCanonicalState( hash, sources.Guests.Guests );
		// [EXT:script-mods] likewise the script mods' state, counters and flags: only while a script mod is enabled, with no SchemaVersion bump (docs/SCRIPT-MODS.md, "Saves and versioning")
		sources.Guests?.ScriptMods?.AddCanonicalState( hash );
		return hash.Value;
	}

	/// <summary>
	/// Makes the reflection metadata of the economy's digest (<see cref="Compute"/> serializes the park save with the serializer's default options) in steps, one type each
	/// (<see cref="JsonMetadataWarmup"/>): a process's first hash with an economy makes it in one go otherwise. No hash changes.
	/// </summary>
	internal static IEnumerable<bool> WarmupSteps() => JsonMetadataWarmup.Steps( JsonSerializerOptions.Default, typeof( ParkSaveFile.Data ) );

	/// <summary>
	/// The economy's part of the hash: everything the park save stores (its serialization with the serializer's default options), its object keys sorted ordinally and the parts
	/// no simulation reads left out, written compact (<see cref="CanonicalEconomy"/>) and hashed as the text it is.
	/// </summary>
	private static void AddEconomy( StateHasher hash, ParkEconomy economy )
	{
		byte[] save;
		using ( FrameMarks.Measure( "hash.economySave" ) )
			save = JsonSerializer.SerializeToUtf8Bytes( ParkSaveFile.Capture( economy ) );
		using ( FrameMarks.Measure( "hash.economyCanonical" ) )
		{
			var text = new ArrayBufferWriter<byte>( save.Length );
			CanonicalEconomy( save, text );
			// the writer's default encoder escapes every character outside ASCII: the text is its bytes
			if ( text.WrittenSpan.IndexOfAnyExceptInRange( (byte)0, (byte)127 ) < 0 )
				hash.AddAscii( text.WrittenSpan );
			else
				hash.Add( System.Text.Encoding.UTF8.GetString( text.WrittenSpan ) );
		}
	}

	/// <summary>
	/// Writes the canonical form of the park save serialized as <paramref name="save"/> to <paramref name="text"/>: compact JSON, every object's keys in ordinal order, written as the
	/// default writer writes names and values (its default encoder), and without the save's parts no simulation reads:
	/// <list type="bullet">
	/// <item>the analyser's caches, saved for an exact continuation but a reading aid, not park state;</item>
	/// <item>the statistics history, which only feeds the park information window's graph (so the pins keep);</item>
	/// <item>the research's buy-list stars (completion, progress, effort and every other research field remain);</item>
	/// <item>the staff's role order, which feeds only the locator radios (roster order, training, candidates and all staff gameplay state remain);</item>
	/// <item>the current challenge's raw value, which feeds only the localized progress caption (scoring uses its baseline and the live measures);</item>
	/// <item>[EXT:online-coop-commands] each object's count of levels bought, which only a guest's upgrade cancellation reads (C4b-2): the session digest covers it, and an older log that
	/// bought an upgrade replays to the checkpoints it recorded.</item>
	/// </list>
	/// </summary>
	internal static void CanonicalEconomy( ReadOnlyMemory<byte> save, IBufferWriter<byte> text )
	{
		using var document = JsonDocument.Parse( save );
		using var writer = new Utf8JsonWriter( text );
		WriteCanonical( writer, document.RootElement, EconomyPart.Save, new List<(string Name, JsonElement Value)>() );
	}

	/// <summary>Where a value of the park save lies, as far as the parts the digest leaves out need it.</summary>
	private enum EconomyPart { Elsewhere, Save, Research, Staff, Objectives, Challenge, Objects, Object }

	/// <summary>Whether the member <paramref name="name"/> of an object at <paramref name="part"/> is left out (<see cref="CanonicalEconomy"/>).</summary>
	private static bool LeftOut( EconomyPart part, string name ) => part switch
	{
		EconomyPart.Save => name is nameof( ParkSaveFile.Data.AnalyserCaches ) or nameof( ParkSaveFile.Data.StatisticsHistory ),
		EconomyPart.Research => name == nameof( ParkSaveFile.ResearchData.RecentItems ),
		EconomyPart.Staff => name == nameof( ParkSaveFile.StaffData.RoleOrder ),
		EconomyPart.Challenge => name == nameof( ActiveChallenge.RawValue ),
		EconomyPart.Object => name == nameof( ParkSaveFile.ObjectData.UpgradePurchases ),
		_ => false
	};

	/// <summary>Where the member <paramref name="name"/> of an object at <paramref name="part"/> lies.</summary>
	private static EconomyPart PartOf( EconomyPart part, string name ) => (part, name) switch
	{
		(EconomyPart.Save, nameof( ParkSaveFile.Data.Research )) => EconomyPart.Research,
		(EconomyPart.Save, nameof( ParkSaveFile.Data.Staff )) => EconomyPart.Staff,
		(EconomyPart.Save, nameof( ParkSaveFile.Data.Objectives )) => EconomyPart.Objectives,
		(EconomyPart.Objectives, nameof( ParkSaveFile.ObjectivesData.Current )) => EconomyPart.Challenge,
		(EconomyPart.Save, nameof( ParkSaveFile.Data.Objects )) => EconomyPart.Objects,
		_ => EconomyPart.Elsewhere
	};

	/// <summary>Orders an object's members by their names, ordinally (a serialized object's names are distinct: the order is the names' alone).</summary>
	private static readonly Comparer<(string Name, JsonElement Value)> ByName = Comparer<(string Name, JsonElement Value)>.Create(
		static ( first, second ) => string.CompareOrdinal( first.Name, second.Name ) );

	/// <summary>
	/// Writes <paramref name="element"/> canonically at <paramref name="part"/>. <paramref name="members"/> holds each open object's members, its name read once: an object's
	/// own lie past its parents' and are taken off when it is written. An object with a name twice is refused (<see cref="ArgumentException"/>), as the JSON tree the text
	/// was built from refused it: the serializer writes two keys that differ only in an unpaired surrogate as one, which the default encoder replaces. The parts left out are
	/// counted, since the tree held them, but never entered, as the tree never read them.
	/// </summary>
	private static void WriteCanonical( Utf8JsonWriter writer, JsonElement element, EconomyPart part, List<(string Name, JsonElement Value)> members )
	{
		switch ( element.ValueKind )
		{
			case JsonValueKind.Object:
			{
				var start = members.Count;
				foreach ( var member in element.EnumerateObject() )
					members.Add( (member.Name, member.Value) );
				var count = members.Count - start;
				members.Sort( start, count, ByName );
				for ( var index = start + 1; index < start + count; index++ )
					if ( members[index].Name == members[index - 1].Name )
						throw new ArgumentException( $"The park save names '{members[index].Name}' twice in one object." );
				writer.WriteStartObject();
				for ( var index = start; index < start + count; index++ )
				{
					var (name, value) = members[index];
					if ( LeftOut( part, name ) )
						continue;
					writer.WritePropertyName( name );
					WriteCanonical( writer, value, PartOf( part, name ), members );
				}
				writer.WriteEndObject();
				members.RemoveRange( start, count );
				break;
			}
			case JsonValueKind.Array:
				writer.WriteStartArray();
				foreach ( var item in element.EnumerateArray() )
					WriteCanonical( writer, item, part == EconomyPart.Objects ? EconomyPart.Object : EconomyPart.Elsewhere, members );
				writer.WriteEndArray();
				break;
			default:
				element.WriteTo( writer );
				break;
		}
	}

	/// <summary>
	/// Runs the economy digest's code once ahead (<see cref="Compute"/>), a part a step: the park save's capture of <paramref name="economy"/>, then each of its parts serialized
	/// with the serializer's default options and written in the digest's canonical form, then the whole save's part of the hash. The code a process's first hash would run for
	/// the first time has then run, compiled ahead of time or not. Nothing is kept, the economy is only read and no hash changes; a save that cannot be captured or a part that
	/// cannot be written is left for the hash to meet.
	/// </summary>
	internal static IEnumerable<bool> SerializationWarmupSteps( ParkEconomy economy )
	{
		ArgumentNullException.ThrowIfNull( economy );
		// a save that cannot be captured is the check's to meet too
		if ( Captured( economy ) is not { } data )
			yield break;
		yield return true;
		foreach ( var property in typeof( ParkSaveFile.Data ).GetProperties( System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance ) )
		{
			if ( property.GetIndexParameters().Length != 0 || property.GetMethod == null )
				continue;
			Warm( () =>
			{
				if ( property.GetValue( data ) is { } value )
					CanonicalEconomy( JsonSerializer.SerializeToUtf8Bytes( value, property.PropertyType ), new ArrayBufferWriter<byte>() );
			} );
			yield return true;
		}
		Warm( () => AddEconomy( new StateHasher(), economy ) );
	}

	private static ParkSaveFile.Data? Captured( ParkEconomy economy )
	{
		try
		{
			return ParkSaveFile.Capture( economy );
		}
		catch ( Exception exception ) when ( exception is not OutOfMemoryException )
		{
			return null;
		}
	}

	/// <summary>Runs <paramref name="work"/>; a failure is the hash's to meet.</summary>
	private static void Warm( Action work )
	{
		try
		{
			work();
		}
		catch ( Exception exception ) when ( exception is not OutOfMemoryException ) { }
	}
}
