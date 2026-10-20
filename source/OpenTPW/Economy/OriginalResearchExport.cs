using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// Writes an OpenTPW park's research into the research lab thing's body and the object controls of an original-format park (the twin of the research part of
/// <see cref="OriginalEconomyImport"/>): the 256 project records, the five group records, the project count, the work load, the most expensive project and the lab-active byte of the lab
/// (serializers <c>0x504210</c>, <c>0x504760</c>, <c>0x502DC0</c>), and the researched byte (<c>+0x10</c>) and upgrade level (<c>+0x14</c>) of each researchable object's control record.
/// </summary>
public static class OriginalResearchExport
{
	private const int ProjectsOffset = SaveThingBase.Bytes;
	private const int GroupsOffset = ProjectsOffset + SaveResearchLab.ProjectRecords * SaveResearchLab.ProjectBytes;
	private const int TailOffset = GroupsOffset + SaveResearchLab.GroupRecords * SaveResearchLab.GroupBytes;
	private const int ControlResearchedOffset = 0x10, ControlLevelOffset = 0x14;

	/// <summary>
	/// Replaces the lab and controls of <paramref name="world"/> with <paramref name="research"/>'s state. The projects written are those of the park's lab table that the template's lab also
	/// holds (a normal game's lab is the whole table, ECON-017; the shipped Easymode save's is a subset), so a template of the same level keeps the projects the original listed.
	/// A template lab without projects (an empty lab) gets them all. Throws <see cref="InvalidDataException"/> when the table does not fit the lab's 256 records.
	/// </summary>
	public static void Apply( OriginalSaveWorldData world, ParkResearch research, int researchers, List<string> lines )
	{
		ArgumentNullException.ThrowIfNull( world );
		ArgumentNullException.ThrowIfNull( research );
		var thing = world.Things.FirstOrDefault( entry => entry.Id == world.Variables.ResearchLab && entry.Model == SaveThingModel.ResearchLab )
			?? throw new InvalidDataException( "The template names no research lab thing; the research has nowhere to go." );
		if ( thing.Body.Length != SaveResearchLab.Bytes )
			throw new InvalidDataException( $"The template's research lab holds {thing.Body.Length} bytes, not {SaveResearchLab.Bytes}." );
		var body = (byte[])thing.Body.Clone();
		var templateUsed = Math.Clamp( BinaryPrimitives.ReadInt32LittleEndian( body.AsSpan( TailOffset ) ), 0, SaveResearchLab.ProjectRecords );
		var templateKeys = Enumerable.Range( 0, templateUsed ).Select( index => (Id: (int)BinaryPrimitives.ReadUInt16LittleEndian( body.AsSpan( ProjectsOffset + index * SaveResearchLab.ProjectBytes ) ),
			Level: (int)body[ProjectsOffset + index * SaveResearchLab.ProjectBytes + 7]) ).ToHashSet();
		var all = research.LabTable( out var walkCount );
		var walk = all.Take( walkCount ).ToHashSet();
		var table = all.Where( item => templateKeys.Count == 0 || templateKeys.Contains( (item.InfoId, item.Level) ) ).ToList();
		if ( table.Count > SaveResearchLab.ProjectRecords )
			throw new InvalidDataException( $"The research table holds {table.Count} projects; the lab holds {SaveResearchLab.ProjectRecords}." );
		// [BIN:TPW-EXE:0x005031A0 most expensive project] the walk's mMostExpensiveProject: the index of the first project of the walk with the highest cost (a later one replaces it only when strictly dearer)
		var most = -1;
		for ( var index = 0; index < table.Count; index++ )
			if ( walk.Contains( table[index] ) && ( most < 0 || table[most].Cost < table[index].Cost ) )
				most = index;
		for ( var index = 0; index < table.Count; index++ )
		{
			var item = table[index];
			var record = body.AsSpan( ProjectsOffset + index * SaveResearchLab.ProjectBytes, SaveResearchLab.ProjectBytes );
			BinaryPrimitives.WriteUInt16LittleEndian( record, checked( (ushort)item.InfoId ) );
			BinaryPrimitives.WriteUInt32LittleEndian( record[2..], (uint)item.Category );
			record[6] = checked( (byte)item.Group );
			record[7] = checked( (byte)item.Level );
			BinaryPrimitives.WriteSingleLittleEndian( record[8..], item.Cost );
			// a finished project holds its whole cost (the overshoot of its last step is not kept), a started one its points
			var done = research.Completed.Contains( (item.InfoId, item.Level) ) ? (float)item.Cost : (float)(research.GetProgress( item ) / (double)ParkResearch.PointScale);
			BinaryPrimitives.WriteSingleLittleEndian( record[12..], done );
		}
		// [BIN:TPW-EXE:0x005031A0 research group outputs] a group's output is (effort x 100 / the sum of the efforts) x the work load / 100 in unsigned integers (0 when the sum is 0); 0x00503330 computes the same
		var efforts = Enumerable.Range( 0, SaveResearchLab.GroupRecords ).Select( index => (uint)research.Effort[index] ).ToArray();
		var sum = efforts.Aggregate( 0u, ( total, effort ) => total + effort );
		for ( var category = 0; category < SaveResearchLab.GroupRecords; category++ )
		{
			var group = body.AsSpan( GroupsOffset + category * SaveResearchLab.GroupBytes, SaveResearchLab.GroupBytes );
			BinaryPrimitives.WriteUInt32LittleEndian( group, efforts[category] );
			BinaryPrimitives.WriteUInt32LittleEndian( group[4..], sum == 0 ? 0 : efforts[category] * 100 / sum * (uint)research.Workload / 100 );
			var current = research.Current( (ResearchCategory)category );
			BinaryPrimitives.WriteInt32LittleEndian( group[8..], current == null ? -1 : table.IndexOf( current ) );
			group[12] = checked( (byte)research.HighestOpenGroup( (ResearchCategory)category ) );
		}
		// [BIN:TPW-EXE:0x00502DC0 lab serializer] after the projects and groups: mNumProjects, mWorkLoad, mMostExpensiveProject and mLabActive (one byte; 0x00504090 sets it and 0x00504140 clears it on the researchers' messages, the constructor 0x00502D60 starts it at 0 < the researcher count)
		BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( TailOffset ), table.Count );
		BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( TailOffset + 4 ), research.Workload );
		BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( TailOffset + 8 ), most );
		body[TailOffset + 12] = researchers > 0 ? (byte)1 : (byte)0;
		thing.Body = body;

		// [BIN:TPW-EXE:0x004D3AA0 object controls] a record's researched byte (+0x10) is 1 for a type without a research cost and, once the project is finished, the completion 0x00504630 writes 1 and the project's level into +0x14 (ParkResearch.ResearchedLevel)
		var changed = 0;
		var controls = world.Controls;
		var projectIds = table.Select( item => item.InfoId ).ToHashSet();
		for ( var index = 0; index < controls.Count && index < SaveObjectControls.Slots; index++ )
		{
			var record = controls.Records.AsSpan( index * SaveObjectControl.Bytes, SaveObjectControl.Bytes );
			var id = (int)BinaryPrimitives.ReadUInt32LittleEndian( record );
			// a type with no project in the lab keeps its record: the fill wrote it once (researched, level moved past the free rows) or skipped the type (ECON-017, e.g. the Mystery ride, whose level stays 0)
			if ( !projectIds.Contains( id ) )
				continue;
			var level = research.ResearchedLevel( id );
			var before = record.ToArray();
			// [BIN:TPW-EXE:0x503FF0 research cheat] an object the research cheat made available is marked researched, its project left as it is
			record[ControlResearchedOffset] = level.HasValue || research.CheatAvailable.Contains( id ) ? (byte)1 : (byte)0;
			if ( level.HasValue )
				BinaryPrimitives.WriteUInt32LittleEndian( record[ControlLevelOffset..], (uint)level.Value );
			if ( !record.SequenceEqual( before ) )
				changed++;
		}
		lines.Add( $"Research lab: {table.Count} projects ({table.Count( item => research.Completed.Contains( (item.InfoId, item.Level) ) )} finished), work load {research.Workload}, efforts {string.Join( "/", efforts )}, most expensive project {most}; {changed} object controls changed." );
	}
}
