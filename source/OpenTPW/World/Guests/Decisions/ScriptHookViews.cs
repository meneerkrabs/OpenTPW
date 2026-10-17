using OpenTPW.Scripting;

namespace OpenTPW;

/// <summary>
/// The read-only views a script hook receives (docs/SCRIPT-MODS.md, "Hook API v1"): plain host tables built from the seam's immutable views (<see cref="GuestView"/>, <see cref="AttractionView"/>,
/// <see cref="AdmissionView"/>, <see cref="QueuePatienceView"/>). Keys are ASCII camelCase names, every value is an int, a bool or a nested table of ints; floats become the whole-number byte
/// the original tests (<see cref="MeterMath.Byte"/>, 0 to 100 for a meter). Things are named by their stable ids (a guest's thing id and birth id, an attraction's id), never by an index into a list.
/// Each table is far below the view caps (<see cref="ScriptLimits.MaxTableEntries"/>, <see cref="ScriptLimits.MaxStringLength"/>), is built when the hook is called and cannot be changed or kept by the script.
/// </summary>
// [EXT:script-mods] OpenTPW's own scripted mods (TPW Script); the original game had no mods
internal static class ScriptHookViews
{
	private static ScriptValue I( long value ) => ScriptValue.FromInt( value );
	private static ScriptValue B( bool value ) => ScriptValue.FromBool( value );

	private static ScriptTable Record( params (string Key, ScriptValue Value)[] fields ) =>
		ScriptTable.RecordView( fields.Select( field => new KeyValuePair<string, ScriptValue>( field.Key, field.Value ) ) );

	private static ScriptValue History( IReadOnlyList<int> history ) => ScriptValue.FromTable( ScriptTable.ArrayView( history.Select( id => I( id ) ) ) );

	/// <summary>
	/// A guest: <c>id</c> (thing id), <c>birthId</c>, <c>type</c>, the meters <c>happiness</c> <c>thirst</c> <c>hunger</c> <c>toilet</c> <c>nausea</c> (whole numbers), <c>money</c>, <c>cellX</c>, <c>cellY</c>,
	/// <c>usedHistory</c> and <c>temporaryHistory</c> (arrays of attraction ids, 0 for an empty slot, newest first), and the park around the decision: <c>turn</c> (park turn), <c>raining</c>, <c>mapWidth</c>
	/// and <c>mapHeight</c> in cells.
	/// </summary>
	public static ScriptValue Guest( in DecisionContext context, in GuestView guest ) => ScriptValue.FromTable( Record(
		("id", I( guest.Id )), ("birthId", I( unchecked( (long)guest.BirthId ) )), ("type", I( guest.Type )),
		("happiness", I( MeterMath.Byte( guest.Happiness ) )), ("thirst", I( MeterMath.Byte( guest.Thirst ) )), ("hunger", I( MeterMath.Byte( guest.Hunger ) )),
		("toilet", I( MeterMath.Byte( guest.Toilet ) )), ("nausea", I( MeterMath.Byte( guest.Nausea ) )), ("money", I( guest.Money )),
		("cellX", I( guest.CellX )), ("cellY", I( guest.CellY )), ("usedHistory", History( guest.UsedHistory )), ("temporaryHistory", History( guest.TemporaryHistory )),
		("turn", I( context.ParkTurn )), ("raining", B( context.Raining )), ("mapWidth", I( context.GridWidth )), ("mapHeight", I( context.GridHeight )) ) );

	private static (string, ScriptValue)[] AttractionFields( in AttractionView attraction ) => new (string, ScriptValue)[]
	{
		("id", I( attraction.Id )), ("typeId", I( attraction.TypeId )), ("kind", I( (int)attraction.Kind )), ("hasJoinCell", B( attraction.HasJoinCell )),
		("backX", I( attraction.BackCell.X )), ("backY", I( attraction.BackCell.Y )), ("entranceX", I( attraction.EntranceCell.X )), ("entranceY", I( attraction.EntranceCell.Y )),
		("cellModifier", I( attraction.CellModifier )), ("queueLength", I( attraction.QueueLength )), ("queueSizeInCells", I( attraction.QueueSizeInCells )), ("queueLimit", I( attraction.QueueLimit )),
		("excitement", I( attraction.LiveExcitement )), ("excitementLevel", I( attraction.StaticExcitementLevel )), ("thirstEffect", I( attraction.ThirstEffect )), ("hungerEffect", I( attraction.HungerEffect )),
		("providesRelief", B( attraction.ProvidesRelief )), ("ageDays", I( attraction.AgeDays )), ("isIndoors", B( attraction.IsIndoors )), ("goldenTicketCost", I( attraction.GoldenTicketCost )),
		("upgradeCost", I( attraction.UpgradeCost )), ("isOpen", B( attraction.IsOpen )), ("isBroken", B( attraction.IsBrokenDown )), ("sameTypeAsLast", B( attraction.SameTypeAsLastUsed ))
	};

	/// <summary>
	/// An attraction as <c>scoreAttraction</c> sees a candidate: <c>id</c>, <c>typeId</c>, <c>kind</c> (<see cref="RideVisitorKind"/>), <c>hasJoinCell</c>, <c>backX</c> <c>backY</c> (the back of the queue),
	/// <c>entranceX</c> <c>entranceY</c>, <c>cellModifier</c>, <c>queueLength</c>, <c>queueSizeInCells</c>, <c>queueLimit</c>, <c>excitement</c> (live), <c>excitementLevel</c> (the type's word), <c>thirstEffect</c>,
	/// <c>hungerEffect</c>, <c>providesRelief</c>, <c>ageDays</c>, <c>isIndoors</c>, <c>goldenTicketCost</c>, <c>upgradeCost</c>, <c>isOpen</c>, <c>isBroken</c> and <c>sameTypeAsLast</c>.
	/// </summary>
	public static ScriptValue Attraction( in AttractionView attraction ) => ScriptValue.FromTable( Record( AttractionFields( attraction ) ) );

	/// <summary>The attraction of <c>joinQueue</c>: the fields of <see cref="Attraction"/> plus <c>joinDifference</c> (|preferred excitement - the ride's|, which the original refuses from 45), <c>excitementWord</c> and <c>queueCount</c>.</summary>
	public static ScriptValue Admission( in AdmissionView admission ) => ScriptValue.FromTable( Record( AttractionFields( admission.Attraction )
		.Concat( new (string, ScriptValue)[] { ("joinDifference", I( admission.JoinDifference )), ("excitementWord", I( admission.ExcitementLevelWord )), ("queueCount", I( admission.QueueCount )) } ).ToArray() ) );

	/// <summary>The attraction of <c>leaveQueue</c>: the fields of <see cref="Attraction"/> plus <c>joinTurn</c>, <c>waitedTurns</c> (park turns since the guest joined), <c>position</c> and <c>queueCount</c>.</summary>
	public static ScriptValue Queue( in DecisionContext context, in QueuePatienceView queue ) => ScriptValue.FromTable( Record( AttractionFields( queue.Attraction )
		.Concat( new (string, ScriptValue)[] { ("joinTurn", I( queue.JoinTurn )), ("waitedTurns", I( Math.Max( 0, context.ParkTurn - queue.JoinTurn ) )), ("position", I( queue.Position )), ("queueCount", I( queue.QueueCount )) } ).ToArray() ) );
}
