using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>UI-091: role-list navigation owns roster IDs independently of the global thing chain, without changing simulation order.</summary>
[TestClass]
public class StaffRoleOrderTests
{
	private static readonly StaffType[] Roles = Enum.GetValues<StaffType>();

	[TestInitialize]
	public void Setup() => Log ??= new();

	private static StaffMember Add( ParkStaff staff, StaffType type, int thingId = 0 ) =>
		staff.AddImported( type, 1, 0, 90, StaffState.Idle, 0, thingId: thingId );

	private static int[][] EmptyOrder() => Roles.Select( _ => Array.Empty<int>() ).ToArray();

	private static void OrderIs( ParkStaff staff, StaffType type, params int[] expected )
	{
		var current = 0;
		foreach ( var id in expected )
		{
			current = staff.NextInRole( type, current );
			Assert.AreEqual( id, current, $"{type}: role-list successor" );
			Assert.IsTrue( staff.Members.Any( member => member.Id == current && member.Type == type ), "navigation returns a roster ID" );
		}
		Assert.AreEqual( 0, staff.NextInRole( type, current ), $"{type}: the tail does not wrap" );
		Assert.AreEqual( 0, staff.NextInRole( type, int.MaxValue ), $"{type}: an unknown selection does not restart at the head" );
		foreach ( var other in staff.Members.Where( member => member.Type != type ) )
			Assert.AreEqual( 0, staff.NextInRole( type, other.Id ), $"{type}: another role's selection is stale" );
		CollectionAssert.AreEqual( expected, staff.CaptureRoleOrder()[(int)type], $"{type}: captured order agrees with navigation" );
	}

	private static void OrdersEqual( int[][] expected, int[][] actual )
	{
		Assert.AreEqual( BalanceSettings.StaffTypeCount, actual.Length );
		foreach ( var role in Roles )
			CollectionAssert.AreEqual( expected[(int)role], actual[(int)role], role.ToString() );
	}

	private static ParkEconomy Restore( ParkEconomy source, string json ) =>
		ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), source.Settings, source.Catalog );

	private static ParkEconomy Restore( ParkEconomy source ) => Restore( source, ParkSaveFile.Serialize( source ) );

	[TestMethod]
	public void EveryRolePrependsImportedAndHiredMembersAndSplicesHeadMiddleAndTail()
	{
		var staff = new ParkStaff( EconomyTestData.Settings() );
		var random = new WorldRng( 17 );
		staff.CreateInitialPool( random, 0 );
		var randomBefore = random.State;
		foreach ( var role in Roles )
		{
			OrderIs( staff, role );
			var first = Add( staff, role );
			var second = Add( staff, role );
			var third = Add( staff, role );
			var hired = staff.Hire( staff.Candidates.First( candidate => candidate.Type == role ).Id, 0 );
			OrderIs( staff, role, hired.Id, third.Id, second.Id, first.Id );
			CollectionAssert.AreEqual( new[] { first.Id, second.Id, third.Id, hired.Id }, staff.Members.Where( member => member.Type == role ).Select( member => member.Id ).ToArray(), "the simulation roster remains insertion ordered" );
			staff.Fire( second.Id );
			OrderIs( staff, role, hired.Id, third.Id, first.Id );
			Assert.AreEqual( 0, staff.NextInRole( role, second.Id ), "a removed interior member is stale" );
			staff.Fire( hired.Id );
			OrderIs( staff, role, third.Id, first.Id );
			staff.Fire( first.Id );
			OrderIs( staff, role, third.Id );
			staff.Fire( third.Id );
			OrderIs( staff, role );
		}
		Assert.AreEqual( randomBefore, random.State, "role-list mutations draw no random numbers" );
	}

	private static ParkStaff LegacyStaff()
	{
		var staff = new ParkStaff( EconomyTestData.Settings() );
		staff.Restore( new[]
		{
			new StaffMember { Id = 7, ThingId = 70, Type = StaffType.Handyman, NameIndex = 0, Grade = 1 },
			new StaffMember { Id = 2, ThingId = 20, Type = StaffType.Handyman, NameIndex = 0, Grade = 1 },
			new StaffMember { Id = 9, ThingId = 90, Type = StaffType.Handyman, NameIndex = 0, Grade = 1 },
		}, Array.Empty<StaffCandidate>(), new long[5], 10, 0 );
		staff.RestoreRoleOrder( null );
		return staff;
	}

	private static ThingPool GlobalPool( params int[] thingIds )
	{
		var pool = new ThingPool();
		pool.Restore( thingIds.Select( id => new ThingSlot( id, SaveThingModel.Handyman ) ) );
		return pool;
	}

	[TestMethod]
	public void ExplicitOrderUsesRosterIdentityAndSurvivesRepeatedOrDifferentPoolAttachment()
	{
		var staff = LegacyStaff();
		var explicitOrder = EmptyOrder();
		explicitOrder[(int)StaffType.Handyman] = new[] { 2, 9, 7 };
		staff.RestoreRoleOrder( explicitOrder );
		var members = staff.Members.ToArray();
		var nextId = staff.NextId;
		var pool = GlobalPool( 70, 20, 90 );
		staff.AttachThings( pool );
		OrderIs( staff, StaffType.Handyman, 2, 9, 7 );
		Assert.AreEqual( 0, staff.NextInRole( StaffType.Handyman, 20 ), "a thing ID is not a roster ID" );
		staff.AttachThings( pool );
		staff.AttachThings( GlobalPool( 90, 70, 20 ) );
		OrderIs( staff, StaffType.Handyman, 2, 9, 7 );
		CollectionAssert.AreEqual( members, staff.Members.ToArray() );
		Assert.AreEqual( nextId, staff.NextId );
	}

	[TestMethod]
	public void LegacyCaptureIsReadOnlyUntilAPoolCanSupplyItsGlobalProjection()
	{
		var staff = LegacyStaff();
		CollectionAssert.AreEqual( new[] { 9, 2, 7 }, staff.CaptureRoleOrder()[0], "without a pool the legacy projection reverses the roster" );
		var captured = staff.CaptureRoleOrder();
		captured[0][0] = int.MaxValue;
		staff.AttachThings( GlobalPool( 70, 20, 90 ) );
		OrderIs( staff, StaffType.Handyman, 7, 2, 9 );
		staff.AttachThings( GlobalPool( 90, 20, 70 ) );
		OrderIs( staff, StaffType.Handyman, 9, 2, 7 );
	}

	[TestMethod]
	public void APreAttachmentMutationMaterializesLegacyOrderBeforeChangingTheRoster()
	{
		foreach ( var remove in new[] { false, true } )
		{
			var staff = LegacyStaff();
			int[] expected;
			if ( remove )
			{
				staff.Fire( 2 );
				expected = new[] { 9, 7 };
			}
			else
			{
				var added = Add( staff, StaffType.Handyman, 100 );
				expected = new[] { added.Id, 9, 2, 7 };
			}
			staff.AttachThings( GlobalPool( remove ? new[] { 70, 90 } : new[] { 70, 20, 90, 100 } ) );
			OrderIs( staff, StaffType.Handyman, expected );
		}
	}

	[TestMethod]
	public void HiringBeforeAttachmentMaterializesTheLegacyOrderBeforePrependingTheHire()
	{
		var staff = LegacyStaff();
		var random = new WorldRng( 31 );
		staff.CreateInitialPool( random, 0 );
		var randomBefore = random.State;
		var hired = staff.Hire( staff.Candidates.First( item => item.Type == StaffType.Handyman ).Id, 0 );
		Assert.AreEqual( 0, hired.ThingId, "the hire has no pool slot until attachment" );
		staff.AttachThings( GlobalPool( 70, 20, 90 ) );
		OrderIs( staff, StaffType.Handyman, hired.Id, 9, 2, 7 );
		Assert.IsTrue( hired.ThingId > 0 );
		Assert.AreEqual( randomBefore, random.State );
	}

	[TestMethod]
	public void FailedHiresDoNotMutateOrMaterializeAnUnresolvedLegacyOrder()
	{
		var staff = LegacyStaff();
		staff.CreateInitialPool( new WorldRng( 31 ), 0 );
		var candidates = staff.Candidates.ToArray();
		var members = staff.Members.ToArray();
		Assert.ThrowsException<InvalidOperationException>( () => staff.Hire( int.MaxValue, 0 ) );
		var full = new ThingPool();
		full.Restore( Enumerable.Range( 1, ThingPool.Capacity ).Select( id => new ThingSlot( id,
			id is 20 or 70 or 90 ? SaveThingModel.Handyman : SaveThingModel.Guest ) ) );
		staff.AttachThings( full );
		OrderIs( staff, StaffType.Handyman, 2, 7, 9 );
		Assert.ThrowsException<InvalidOperationException>( () => staff.Hire( candidates.First( item => item.Type == StaffType.Handyman ).Id, 0 ) );
		Assert.AreEqual( ThingPool.Capacity, full.Count );
		Assert.AreEqual( 0, full.NextId );
		CollectionAssert.AreEqual( candidates, staff.Candidates.ToArray() );
		CollectionAssert.AreEqual( members, staff.Members.ToArray() );
		staff.AttachThings( GlobalPool( 90, 20, 70 ) );
		OrderIs( staff, StaffType.Handyman, 9, 2, 7 );
	}

	[TestMethod]
	public void PickupRestStrikeJobAndRenameNeverReorderAnyRole()
	{
		var park = AllRolesPark();
		var expected = park.Staff.CaptureRoleOrder();
		var roster = park.Staff.Members.ToArray();
		var random = park.World.State;
		foreach ( var role in Roles )
		{
			var member = park.Staff.Members.First( item => item.Type == role );
			foreach ( var state in new[] { StaffState.PickedUp, StaffState.Resting, StaffState.OnStrike, StaffState.Working, StaffState.Patrolling, StaffState.Idle } )
			{
				member.State = state;
				member.Job = state == StaffState.PickedUp ? StaffJob.PickedUp : StaffJob.Idle;
				member.CellX = state == StaffState.PickedUp ? null : 2.5f;
				member.CellY = state == StaffState.PickedUp ? null : 1.5f;
				OrdersEqual( expected, park.Staff.CaptureRoleOrder() );
			}
			Assert.IsTrue( park.Staff.Rename( member, $"Renamed {role}" ) );
			OrderIs( park.Staff, role, expected[(int)role] );
		}
		CollectionAssert.AreEqual( roster, park.Staff.Members.ToArray() );
		Assert.AreEqual( random, park.World.State );
	}

	[TestMethod]
	public void RemovalAndThingSlotReuseNeverResurrectTheRemovedRosterIdentity()
	{
		var staff = LegacyStaff();
		var order = EmptyOrder();
		order[0] = new[] { 2, 9, 7 };
		staff.RestoreRoleOrder( order );
		var pool = GlobalPool( 70, 20, 90 );
		staff.AttachThings( pool );
		staff.Fire( 2 );
		OrderIs( staff, StaffType.Handyman, 9, 7 );
		Assert.IsTrue( pool.IsQueued( 20 ) );
		pool.DestroyQueued();
		var newMember = Add( staff, StaffType.Handyman );
		Assert.AreEqual( 20, newMember.ThingId, "the reused slot is lower than both surviving slots, 90 and 70" );
		Assert.AreNotEqual( 2, newMember.Id );
		OrderIs( staff, StaffType.Handyman, newMember.Id, 9, 7 );
		Assert.AreEqual( 0, staff.NextInRole( StaffType.Handyman, 2 ) );
	}

	[TestMethod]
	public void CapturesAndRestoresDefensivelyCopyEveryRoleArray()
	{
		var staff = new ParkStaff( EconomyTestData.Settings() );
		foreach ( var role in Roles )
		{
			Add( staff, role );
			Add( staff, role );
		}
		var expected = staff.CaptureRoleOrder();
		var input = expected.Select( row => row.ToArray() ).ToArray();
		staff.RestoreRoleOrder( input );
		foreach ( var role in Roles )
		{
			input[(int)role][0] = int.MaxValue;
			input[(int)role] = Array.Empty<int>();
		}
		OrdersEqual( expected, staff.CaptureRoleOrder() );
		var captured = staff.CaptureRoleOrder();
		foreach ( var role in Roles )
		{
			captured[(int)role][0] = int.MaxValue;
			captured[(int)role] = Array.Empty<int>();
		}
		OrdersEqual( expected, staff.CaptureRoleOrder() );
	}

	private static ParkEconomy AllRolesPark()
	{
		var park = EconomyTestData.Park();
		foreach ( var role in Roles )
			for ( var index = 0; index < 3; index++ )
				Add( park.Staff, role, 100 + (int)role * 10 + index );
		var order = park.Staff.CaptureRoleOrder();
		foreach ( var role in Roles )
		{
			var row = order[(int)role];
			order[(int)role] = new[] { row[1], row[0], row[2] };
		}
		park.Staff.RestoreRoleOrder( order );
		return park;
	}

	[TestMethod]
	public void NativeSaveKeepsIndependentOrdersAndContinuesHiringFiringAndSlotReuse()
	{
		var live = AllRolesPark();
		var pool = new ThingPool();
		pool.Restore( live.Staff.Members.Select( member => new ThingSlot( member.ThingId, ParkStaff.ThingModelOf( member.Type ) ) ) );
		live.Staff.AttachThings( pool );
		var restored = Restore( live );
		var resumedPool = new ThingPool();
		resumedPool.RestoreSnapshot( pool.CaptureSnapshot() );
		restored.Staff.AttachThings( resumedPool );
		OrdersEqual( live.Staff.CaptureRoleOrder(), restored.Staff.CaptureRoleOrder() );
		Assert.AreEqual( live.World.State, restored.World.State );
		foreach ( var role in Roles )
		{
			var removed = live.Staff.CaptureRoleOrder()[(int)role][1];
			live.Staff.Fire( removed );
			restored.Staff.Fire( removed );
			pool.DestroyQueued();
			resumedPool.DestroyQueued();
			var candidate = live.Staff.Candidates.First( item => item.Type == role ).Id;
			var hired = live.Staff.Hire( candidate, live.Tick );
			var resumedHire = restored.Staff.Hire( candidate, restored.Tick );
			Assert.AreEqual( (hired.Id, hired.ThingId), (resumedHire.Id, resumedHire.ThingId) );
			Assert.AreEqual( hired.Id, live.Staff.NextInRole( role, 0 ) );
			OrdersEqual( live.Staff.CaptureRoleOrder(), restored.Staff.CaptureRoleOrder() );
			CollectionAssert.AreEqual( pool.Capture(), resumedPool.Capture() );
		}
		OrdersEqual( live.Staff.CaptureRoleOrder(), Restore( restored ).Staff.CaptureRoleOrder() );
		CollectionAssert.AreEqual( live.Staff.Members.Select( member => member.Id ).ToArray(), restored.Staff.Members.Select( member => member.Id ).ToArray() );
	}


	[TestMethod]
	[DoNotParallelize]
	public void WorldEnvelopeRoundTripsRoleOrdersAndValidatesItsEmbeddedStaffOrder()
	{
		var original = ParkResumeSaveFileTests.Envelope();
		var settings = EconomyTestData.Settings();
		var catalog = EconomyTestData.Catalog();
		var park = ParkSaveFile.Restore( original.Economy, settings, catalog );
		var pool = new ThingPool();
		pool.Restore( original.Guests.Guests.Reverse().Select( guest => new ThingSlot( guest.Id, SaveThingModel.Guest ) ) );
		pool.AllocateWorldSetup();
		var objectId = pool.Allocate( SaveThingModel.Attraction );
		foreach ( var role in Roles )
			for ( var index = 0; index < 3; index++ ) Add( park.Staff, role );
		park.Staff.AttachThings( pool );
		var expected = park.Staff.CaptureRoleOrder();
		foreach ( var role in Roles )
		{
			var row = expected[(int)role];
			expected[(int)role] = new[] { row[1], row[0], row[2] };
		}
		park.Staff.RestoreRoleOrder( expected );
		var snapshot = original with
		{
			Economy = ParkSaveFile.Capture( park, ParkSaveFile.ToWorldState( original.Economy ) ),
			Things = pool.CaptureSnapshot(),
			Guests = original.Guests with { Things = null },
			Layout = original.Layout with { Objects = original.Layout.Objects.Select( item => item with { ThingId = objectId } ).ToList() }
		};
		var bytes = ParkResumeSaveFile.Serialize( snapshot );
		var decoded = ParkResumeSaveFile.Deserialize( bytes );
		CollectionAssert.AreEqual( bytes, ParkResumeSaveFile.Serialize( decoded ), "the full world envelope is byte-stable" );
		OrdersEqual( expected, decoded.Economy.Staff.RoleOrder! );
		var restored = ParkSaveFile.Restore( decoded.Economy, settings, catalog );
		var resumedPool = new ThingPool();
		resumedPool.RestoreSnapshot( decoded.Things! );
		restored.Staff.AttachThings( resumedPool );
		OrdersEqual( expected, restored.Staff.CaptureRoleOrder() );
		decoded.Economy.Staff.RoleOrder![0][1] = decoded.Economy.Staff.RoleOrder[0][0];
		Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( decoded ) );
	}

	[DataTestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void NativeLegacyNullOrAbsentOrderUsesTheAvailablePoolOrReverseRoster( bool omit, bool attach )
	{
		var source = AllRolesPark();
		var root = JsonNode.Parse( ParkSaveFile.Serialize( source ) )!.AsObject();
		var staffNode = root["Staff"]!.AsObject();
		if ( omit ) staffNode.Remove( "RoleOrder" );
		else staffNode["RoleOrder"] = null;
		var restored = Restore( source, root.ToJsonString() );
		if ( attach )
		{
			var pool = new ThingPool();
			pool.Restore( restored.Staff.Members.Select( member => new ThingSlot( member.ThingId, ParkStaff.ThingModelOf( member.Type ) ) ) );
			restored.Staff.AttachThings( pool );
		}
		foreach ( var role in Roles )
		{
			var members = restored.Staff.Members.Where( member => member.Type == role ).Select( member => member.Id );
			OrderIs( restored.Staff, role, (attach ? members : members.Reverse()).ToArray() );
		}
	}

	[TestMethod]
	public void SavingAndHashingAnUnresolvedLegacyOwnerDoNotFreezeItsPreAttachmentProjection()
	{
		var source = AllRolesPark();
		var json = JsonNode.Parse( ParkSaveFile.Serialize( source ) )!;
		json["Staff"]!.AsObject().Remove( "RoleOrder" );
		var park = Restore( source, json.ToJsonString() );
		var random = park.World.State;
		var before = WorldStateHash.Compute( new WorldStateSources { Economy = park } );
		var captured = ParkSaveFile.Capture( park );
		ParkSaveFile.Serialize( park );
		foreach ( var role in Roles )
			CollectionAssert.AreEqual( park.Staff.Members.Where( member => member.Type == role ).Reverse().Select( member => member.Id ).ToArray(), captured.Staff.RoleOrder![(int)role] );
		var pool = new ThingPool();
		pool.Restore( park.Staff.Members.Select( member => new ThingSlot( member.ThingId, ParkStaff.ThingModelOf( member.Type ) ) ) );
		park.Staff.AttachThings( pool );
		foreach ( var role in Roles )
			OrderIs( park.Staff, role, park.Staff.Members.Where( member => member.Type == role ).Select( member => member.Id ).ToArray() );
		Assert.AreEqual( before, WorldStateHash.Compute( new WorldStateSources { Economy = park } ) );
		Assert.AreEqual( random, park.World.State );
	}

	private static IEnumerable<(string Name, int[][] Order)> InvalidOrders( int[][] valid )
	{
		int[][] Copy() => valid.Select( row => row.ToArray() ).ToArray();
		yield return ("too few roles", valid.Take( 4 ).ToArray());
		yield return ("too many roles", valid.Concat( new[] { Array.Empty<int>() } ).ToArray());
		var order = Copy(); order[0] = null!; yield return ("null role", order);
		order = Copy(); order[0][1] = order[0][0]; yield return ("duplicate roster member", order);
		order = Copy(); (order[0][0], order[1][0]) = (order[1][0], order[0][0]); yield return ("wrong role", order);
		order = Copy(); order[0] = order[0].Skip( 1 ).ToArray(); yield return ("missing roster member", order);
		order = Copy(); order[0][0] = int.MaxValue; yield return ("unknown roster member", order);
		order = Copy(); order[0][0] = 0; yield return ("sentinel in the order", order);
		order = Copy(); order[0][0] = -1; yield return ("negative roster member", order);
		order = Copy(); order[0] = Enumerable.Repeat( order[0][0], ThingPool.Capacity + 1 ).ToArray(); yield return ("over the thing-pool capacity", order);
	}

	[TestMethod]
	public void MalformedNativeOrdersFailBothShapeValidationAndOwnerRestoreWithoutChangingExistingOrder()
	{
		var park = AllRolesPark();
		var valid = park.Staff.CaptureRoleOrder();
		var snapshot = ParkSaveFile.Capture( park );
		snapshot.Staff.RoleOrder![0][0] = int.MaxValue;
		OrdersEqual( valid, park.Staff.CaptureRoleOrder() );
		foreach ( var (name, invalid) in InvalidOrders( valid ) )
		{
			Assert.ThrowsException<InvalidDataException>( () => park.Staff.RestoreRoleOrder( invalid ), name );
			OrdersEqual( valid, park.Staff.CaptureRoleOrder() );
			var root = JsonNode.Parse( ParkSaveFile.Serialize( park ) )!;
			root["Staff"]!["RoleOrder"] = JsonSerializer.SerializeToNode( invalid );
			Assert.ThrowsException<InvalidDataException>( () => Restore( park, root.ToJsonString() ), name );
		}
	}

	[TestMethod]
	public void RosterIdsMayExceedThingCapacityBecauseOnlyTheLiveMemberCountIsBounded()
	{
		var staff = new ParkStaff( EconomyTestData.Settings() );
		var id = ThingPool.Capacity + 100;
		staff.Restore( new[] { new StaffMember { Id = id, Type = StaffType.Guard, NameIndex = 0, Grade = 1 } }, Array.Empty<StaffCandidate>(), new long[5], id + 1, 0 );
		var order = EmptyOrder();
		order[(int)StaffType.Guard] = new[] { id };
		staff.RestoreRoleOrder( order );
		OrderIs( staff, StaffType.Guard, id );
	}

	[TestMethod]
	public void RoleOrderAloneIsUnhashedAndReadOnlyWhileOrdinaryStaffBudgetsAndTrainingFlagRemainHashed()
	{
		var park = AllRolesPark();
		ulong Hash() => WorldStateHash.Compute( new WorldStateSources { Economy = park } );
		var before = Hash();
		var members = park.Staff.Members.ToArray();
		var candidates = park.Staff.Candidates.ToArray();
		var nextId = park.Staff.NextId;
		var random = park.World.State;
		var saved = ParkSaveFile.Serialize( park );
		var reordered = park.Staff.CaptureRoleOrder().Select( row => row.Reverse().ToArray() ).ToArray();
		park.Staff.RestoreRoleOrder( reordered );
		Assert.AreEqual( before, Hash(), "UI order alone is excluded" );
		Assert.AreEqual( 21, WorldStateHash.SchemaVersion, "no simulation-schema change" );
		Assert.AreNotEqual( saved, ParkSaveFile.Serialize( park ), "the order is nevertheless saved" );
		foreach ( var role in Roles ) OrderIs( park.Staff, role, reordered[(int)role] );
		CollectionAssert.AreEqual( members, park.Staff.Members.ToArray() );
		CollectionAssert.AreEqual( candidates, park.Staff.Candidates.ToArray() );
		Assert.AreEqual( nextId, park.Staff.NextId );
		Assert.AreEqual( random, park.World.State );
		members[0].Happiness -= 1;
		Assert.AreNotEqual( before, Hash(), "ordinary staff state is still hashed" );
		members[0].Happiness += 1;
		park.Staff.SetTrainingBudget( StaffType.Handyman, 10 );
		park.Staff.RestoreHaveEverTrained( false );
		Assert.AreNotEqual( before, Hash(), "training budgets are still hashed" );
		park.Staff.SetTrainingBudget( StaffType.Handyman, 0 );
		Assert.AreEqual( before, Hash() );
		park.Staff.RestoreHaveEverTrained( true );
		Assert.AreNotEqual( before, Hash(), "HaveEverTrained is still hashed" );
	}

	private static int[] OriginalGlobal( StaffType role ) => role == StaffType.Handyman
		? new[] { 7, 2, 9 } : new[] { 47 + (int)role * 10, 42 + (int)role * 10, 49 + (int)role * 10 };

	/// <summary>A complete synthetic original park, using the same binary fixtures as OriginalStaffImportTests.</summary>
	private static (ParkEconomy Park, OriginalParkImport Save, OriginalEconomyImport Import) Original( StaffType? brokenRole = null, string? defect = null )
	{
		var settings = OriginalSaveImportTests.Settings();
		var map = OriginalParkImportTests.CreateMap( 3, 3, ( _, _ ) => 0 );
		var things = SaveTestPayloads.ParkThings().Where( item => item.Model != (uint)SaveThingModel.Researcher ).ToList();
		// These three unused singleton references are zero in ParkVariables, leaving staff free to use literal IDs 7, 2 and 9.
		for ( var index = 0; index < things.Count; index++ )
		{
			var item = things[index];
			if ( item.Id is 2 or 7 or 9 ) things[index] = (item.Id + 100, item.Model, item.Body);
			if ( item.Model == (uint)SaveThingModel.Bank ) things[index] = (item.Id, item.Model, SaveTestPayloads.Bank( loans: slot =>
			{
				var offer = settings.Loans[slot];
				return new[] { 0, (int)offer.Amount, offer.AprPercent, offer.Months, (int)LoanMath.MonthlyRepayment( offer.Amount, offer.AprPercent, offer.Months ), 0, 0, offer.LenderNameIndex };
			} ));
			if ( item.Model == (uint)SaveThingModel.ChallengeSystem ) things[index] = (item.Id, item.Model, OriginalSaveImportTests.Challenges( settings ));
		}
		var variables = SaveTestPayloads.ParkVariables();
		var staffThings = new List<(uint Id, uint Model, byte[] Body)>();
		foreach ( var role in Roles )
		{
			var global = OriginalGlobal( role );
			// Global [7, 2, 9] and role [2, 9, 7] deliberately disagree, for every role.
			var next = new Dictionary<int, int> { [global[1]] = global[2], [global[2]] = global[0], [global[0]] = 0 };
			var head = global[1];
			if ( brokenRole == role )
			{
				next[global[2]] = defect switch
				{
					"cycle" => global[1],
					"wrong role" => OriginalGlobal( (StaffType)(((int)role + 1) % Roles.Length) )[1],
					"nonstaff" => 12,
					"missing" => 500,
					"orphan" => 0,
					"bounds" => ThingPool.Capacity + 1,
					_ => next[global[2]]
				};
				if ( defect == "empty head" ) head = 0;
				if ( defect == "missing head" ) head = 500;
				if ( defect == "wrong head" ) head = 12;
			}
			BinaryPrimitives.WriteUInt16LittleEndian( variables.AsSpan( 52 + (int)role * 2 ), (ushort)head );
			var model = ParkStaff.ThingModelOf( role );
			foreach ( var id in global )
				staffThings.Add( ((uint)id, (uint)model, SaveTestPayloads.Staff( model, roleNext: (ushort)next[id] )) );
		}
		things.InsertRange( 0, staffThings );
		var grid = SaveTestPayloads.Grid( map, ( x, y ) => OriginalParkImportTests.CreateCellRecord( 0, occupied: (x, y) == (1, 1) ) );
		var rideSystem = SaveTestPayloads.RideSystem( new[] { SaveTestPayloads.RideSystemRecord( 1100, 1, 1, 1, 1, SaveObject.PlacedObjectKind, 2 ) } );
		var save = OriginalParkImport.Import( SaveTestPayloads.Payload( SaveTestPayloads.World( variables, grid, SaveTestPayloads.Things( things ) ), rideSystem ), map );
		var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		return (park, save, OriginalEconomyImport.Apply( park, save ));
	}

	[TestMethod]
	public void OriginalRoleHeadsAndLinksMapThingIdsToIndependentRosterOrdersForAllFiveRoles()
	{
		var (park, save, import) = Original();
		foreach ( var role in Roles )
		{
			var global = OriginalGlobal( role );
			var byThing = park.Staff.Members.Where( member => member.Type == role ).ToDictionary( member => member.ThingId );
			CollectionAssert.AreEqual( global, park.Staff.Members.Where( member => member.Type == role ).Select( member => member.ThingId ).ToArray(), "the simulation roster keeps the saved global order" );
			OrderIs( park.Staff, role, byThing[global[1]].Id, byThing[global[2]].Id, byThing[global[0]].Id );
			Assert.IsTrue( byThing.Values.All( member => member.Id != member.ThingId ), "the fixture exercises mismatched identity spaces" );
		}
		Assert.IsFalse( import.Evidence.Any( line => line.StartsWith( "Staff role order fallback for ", StringComparison.Ordinal ) ) );
		var pool = new ThingPool();
		pool.Restore( save.World.Things.Select( thing => new ThingSlot( thing.Id, thing.Model ) ) );
		var expected = park.Staff.CaptureRoleOrder();
		park.Staff.AttachThings( pool );
		park.Staff.AttachThings( pool );
		OrdersEqual( expected, park.Staff.CaptureRoleOrder() );
		OrdersEqual( expected, Restore( park ).Staff.CaptureRoleOrder() );
	}

	[DataTestMethod]
	[DataRow( "cycle" )]
	[DataRow( "wrong role" )]
	[DataRow( "nonstaff" )]
	[DataRow( "missing" )]
	[DataRow( "orphan" )]
	[DataRow( "bounds" )]
	[DataRow( "empty head" )]
	public void EachMalformedOriginalRoleFallsBackInFullWithoutDisturbingTheOtherFourRoles( string defect )
	{
		foreach ( var brokenRole in Roles )
		{
			var (park, _, import) = Original( brokenRole, defect );
			foreach ( var role in Roles )
			{
				var global = OriginalGlobal( role );
				var expected = role == brokenRole ? global : new[] { global[1], global[2], global[0] };
				var byThing = park.Staff.Members.Where( member => member.Type == role ).ToDictionary( member => member.ThingId );
				OrderIs( park.Staff, role, expected.Select( id => byThing[id].Id ).ToArray() );
			}
			var warning = import.Evidence.Single( line => line.StartsWith( "Staff role order fallback for ", StringComparison.Ordinal ) );
			StringAssert.StartsWith( warning, $"Staff role order fallback for {brokenRole}:" );
			StringAssert.Contains( warning, "using the saved global thing-chain projection for this role." );
			var ids = OriginalGlobal( brokenRole );
			var otherRole = (StaffType)(((int)brokenRole + 1) % Roles.Length);
			var reason = defect switch
			{
				"cycle" => $"cycle at thing {ids[1]}",
				"wrong role" => $"thing {OriginalGlobal( otherRole )[1]} is a {ParkStaff.ThingModelOf( otherRole )}, expected {ParkStaff.ThingModelOf( brokenRole )}",
				"nonstaff" => $"thing 12 is a Attraction, expected {ParkStaff.ThingModelOf( brokenRole )}",
				"missing" => "missing staff thing 500",
				"orphan" => $"chain covers 2 of 3 {brokenRole} members",
				"bounds" => $"thing {ThingPool.Capacity + 1} is outside 1..{ThingPool.Capacity}",
				_ => $"chain covers 0 of 3 {brokenRole} members"
			};
			StringAssert.Contains( warning, reason );
		}
	}

	[DataTestMethod]
	[DataRow( "missing head", 500 )]
	[DataRow( "wrong head", 12 )]
	public void TheFilesReaderStillRejectsInvalidOriginalRoleHeads( string defect, int head )
	{
		foreach ( var role in Roles )
		{
			var error = Assert.ThrowsException<InvalidDataException>( () => Original( role, defect ) );
			StringAssert.Contains( error.Message, $"Save World variable First{role} names thing {head}" );
			StringAssert.Contains( error.Message, defect == "missing head" ? "not in the thing list" : $"not a {ParkStaff.ThingModelOf( role )}" );
		}
	}
}
