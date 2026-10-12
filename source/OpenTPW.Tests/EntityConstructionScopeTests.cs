using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public sealed class EntityConstructionScopeTests
{
	private List<Entity> previousEntities = null!;
	private Level? previousLevel;
	private CameraMode previousCamera = null!;
	private float previousExtent;
	private Vector3 previousTarget;

	[TestInitialize]
	public void Initialize()
	{
		previousEntities = Entity.All;
		previousLevel = Level.Current;
		previousCamera = Camera.Mode;
		previousExtent = ParkCameraMode.TargetExtent;
		previousTarget = ParkCameraMode.InitialTarget;
		Entity.All = new();
		Level.Current = null!;
		Camera.Mode = new CameraMode();
		ParkCameraMode.TargetExtent = 50;
		ParkCameraMode.InitialTarget = new Vector3( 1, 2, 0 );
	}

	[TestCleanup]
	public void Cleanup()
	{
		Entity.All = previousEntities;
		Level.Current = previousLevel!;
		Camera.Mode = previousCamera;
		ParkCameraMode.TargetExtent = previousExtent;
		ParkCameraMode.InitialTarget = previousTarget;
	}

	private sealed class OwnedEntity( bool failDelete = false ) : Entity
	{
		public int Deletes { get; private set; }
		protected override void OnDelete()
		{
			Deletes++;
			if ( failDelete ) throw new InvalidOperationException( "injected resource cleanup failure" );
		}
	}

	[TestMethod]
	public void ConstructionFailureRestoresLiveEntitiesAndCamera()
	{
		var live = new OwnedEntity();
		var liveList = Entity.All;
		var liveCamera = Camera.Mode;
		OwnedEntity? candidate = null;
		Assert.ThrowsException<InvalidDataException>( () =>
		{
			using var transaction = new EntityConstructionScope();
			candidate = new OwnedEntity();
			Camera.Mode = new CameraMode();
			ParkCameraMode.TargetExtent = 200;
			ParkCameraMode.InitialTarget = new Vector3( 3, 4, 0 );
			throw new InvalidDataException( "injected late restoration failure" );
		} );
		Assert.AreSame( liveList, Entity.All );
		Assert.AreSame( liveCamera, Camera.Mode );
		Assert.AreEqual( 50f, ParkCameraMode.TargetExtent );
		Assert.AreEqual( new Vector3( 1, 2, 0 ), ParkCameraMode.InitialTarget );
		CollectionAssert.AreEqual( new[] { live }, Entity.All.ToArray() );
		Assert.AreEqual( 0, live.Deletes );
		Assert.AreEqual( 1, candidate!.Deletes );
	}

	[TestMethod]
	public void DetachedCandidateCanBeRejectedWithoutRetiringTheLiveScene()
	{
		var live = new OwnedEntity();
		var liveList = Entity.All;
		var transaction = new EntityConstructionScope();
		var candidate = new OwnedEntity();
		transaction.Detach();
		Assert.AreSame( liveList, Entity.All );
		transaction.Dispose();
		transaction.Dispose();
		Assert.AreSame( liveList, Entity.All );
		Assert.AreEqual( 0, live.Deletes );
		Assert.AreEqual( 1, candidate.Deletes );
	}

	[TestMethod]
	public void ActivationRetiresOnlyTheOldSceneAndReassertsCandidateCamera()
	{
		var live = new OwnedEntity();
		using var transaction = new EntityConstructionScope();
		var candidate = new OwnedEntity();
		var candidateCamera = new CameraMode();
		Camera.Mode = candidateCamera;
		ParkCameraMode.TargetExtent = 200;
		ParkCameraMode.InitialTarget = new Vector3( 3, 4, 0 );
		transaction.Detach();
		Assert.ThrowsException<InvalidOperationException>( transaction.Activate,
			"an activation must not silently orphan the live scene" );
		live.Delete();
		Camera.Mode = new CameraMode(); // An old ride-view's teardown can change the camera.
		transaction.Activate();
		transaction.Dispose();
		CollectionAssert.AreEqual( new[] { candidate }, Entity.All.ToArray() );
		Assert.AreSame( candidateCamera, Camera.Mode );
		Assert.AreEqual( 200f, ParkCameraMode.TargetExtent );
		Assert.AreEqual( new Vector3( 3, 4, 0 ), ParkCameraMode.InitialTarget );
		Assert.AreEqual( 1, live.Deletes );
		Assert.AreEqual( 0, candidate.Deletes );
	}

	[TestMethod]
	public void CleanupFailuresStillRestoreGlobalsAndCleanTheOtherCandidateEntities()
	{
		var live = new OwnedEntity();
		var liveList = Entity.All;
		var transaction = new EntityConstructionScope();
		var first = new OwnedEntity( failDelete: true );
		var second = new OwnedEntity();
		Assert.ThrowsException<AggregateException>( transaction.Dispose );
		Assert.AreSame( liveList, Entity.All );
		Assert.AreEqual( 0, live.Deletes );
		Assert.AreEqual( 1, first.Deletes );
		Assert.AreEqual( 1, second.Deletes );
		using var next = new EntityConstructionScope();
	}

	[TestMethod]
	public void NestedConstructionIsRejectedBeforeChangingTheCandidateRegistry()
	{
		using var transaction = new EntityConstructionScope();
		var list = Entity.All;
		Assert.ThrowsException<InvalidOperationException>( () => new EntityConstructionScope() );
		Assert.AreSame( list, Entity.All );
	}
}
