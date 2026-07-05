using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class Train3D : Node3D
{
	[Signal]
	public delegate void CantMoveAnymoreEventHandler(Train3D train);

	[Export] public float Speed = 8.0f;
	[Export] public float ForwardRayLength = 40.0f;
	[Export] public float DownRayLength = 20.0f;
	[Export] public float GapTolerance = 0.05f;
	[Export] public float StopEpsilon = 0.02f;
	[Export] public bool ShowRoutePreviewInEditorRun = true;
	[Export] public bool VerboseLogging = false;

	private const float SearchStartRadius = 1.0f;
	private const float SearchStep = 2.0f;
	private const float SearchMaxRadius = 300.0f;
	private const float RayStartLift = 0.2f;
	private const float PreviewArrowLength = 6.0f;
	private const float PreviewArrowHeadLength = 1.5f;
	private const float PreviewArrowHeadWidth = 0.75f;
	private static readonly Color PreviewCurrentRailYellow = new(1.0f, 0.9f, 0.1f, 1.0f);
	private static readonly Color PreviewCurrentRailGreen = new(0.2f, 1.0f, 0.2f, 1.0f);
	private const uint PHYSICS_LAYER__BLOCKER = 1u << 2; // Layer 3
	private const uint RayCollisionMask = uint.MaxValue & ~PHYSICS_LAYER__BLOCKER;

	private RailRoad3D _currentRail;
	private RailRoad3D _nextRail;
	private Vector3 _forwardDirection = Vector3.Forward;
	private bool _isMoving;
	private Vector3 _stopTargetWorld = Vector3.Zero;
	private bool _initializationCompleted;
	private bool _hasEmittedInitialPlacement;
	private bool _hasEmittedFinalStop;
	private Node3D _runtimePreviewRoot;
	private MeshInstance3D _previewArrowMesh;
	private MeshInstance3D _previewCurrentRailMesh;
	private MeshInstance3D _previewNextRailMesh;
	private StandardMaterial3D _previewArrowMaterial;
	private StandardMaterial3D _previewCurrentRailMaterial;
	private StandardMaterial3D _previewNextRailMaterial;
	private bool _currentRailApproved;
	private bool _isStoppingDueToDeadEnd;
	private bool _isWaitingForNextRailClear;
	private RailRoad3D _waitingOnRail;

	private MovementTask _currentTask;

	private struct MovementTask
	{
		public Vector3 Start;
		public Vector3 End;
		public float Distance;
		public float Traveled;

		public bool IsValid => Distance > 0f;
		public float Progress => Distance > 0 ? Traveled / Distance : 0f;

		public Vector3 GetPosition()
		{
			if (!IsValid) return Start;
			return Start.Lerp(End, Mathf.Min(Progress, 1f));
		}

		public bool IsComplete => Traveled >= Distance;

		public static MovementTask Create(Vector3 start, Vector3 end)
		{
			return new MovementTask
			{
				Start = start,
				End = end,
				Distance = start.DistanceTo(end),
				Traveled = 0f
			};
		}
	}

	public bool IsInMotion
	{
		get { return _isMoving; }
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_isMoving = false;
		_initializationCompleted = false;
		_hasEmittedInitialPlacement = false;
		_hasEmittedFinalStop = false;
		_isWaitingForNextRailClear = false;
		_waitingOnRail = null;
		_currentTask = default;
		EnsureRuntimePreviewNodes();
		UpdateRuntimePreviewVisuals();
	}

	public override void _ExitTree()
	{
		// Clean up any pending wait listeners
		if (_waitingOnRail != null)
		{
			_waitingOnRail.TrainDeparted -= OnWaitedRailCleared;
			_waitingOnRail = null;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_initializationCompleted)
		{
			return;
		}

		if (!IsWorldReady())
		{
			return;
		}

		_initializationCompleted = true;
		if (!TryInitializeOnClosestRail())
		{
			_isMoving = false;
			return;
		}

		_isMoving = true;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		UpdateRuntimePreviewVisuals();

		if (!_isMoving)
		{
			return;
		}

		if (_isWaitingForNextRailClear)
		{
			// Still waiting for the next rail to be cleared; don't update task
			return;
		}

		// Update current task
		if (_currentTask.IsValid)
		{
			_currentTask.Traveled += Speed * (float)delta;
			GlobalPosition = _currentTask.GetPosition();

			if (_currentTask.IsComplete)
			{
				LogTrain($"Task complete: arrived at destination (traveled {_currentTask.Traveled:F2}m / {_currentTask.Distance:F2}m).");
				// Task complete - snapped to end position
				GlobalPosition = _currentTask.End;
				_currentTask = default; // Clear task

				// Evaluate next state
				EvaluateNextMovement();
			}
		}
		else
		{
			LogTrain($"No valid task to execute (IsValid={_currentTask.IsValid}, Distance={_currentTask.Distance:F2}, IsMoving={_isMoving}).");
			_isMoving = false;
		}
	}

	private void EvaluateNextMovement()
	{
		LogTrain("Evaluating next movement...");

		// Find which rail we're on
		RailRoad3D underRail = FindRailBelowByDownwardRay();
		if (underRail == null)
		{
			LogWarning("Train3D: Downward ray did not hit any RailRoad3D after task completion.");
			LogTrain("Cannot evaluate movement: Downward ray missed all rails.");
			_isMoving = false;
			return;
		}

		// Check if we're on a new rail
		if (underRail != _currentRail)
		{
			// Notify old rail
			if (_currentRail != null)
			{
				LogTrain($"Leaving rail '{_currentRail.Name}'.");
				_currentRail.ClearCurrentTrain();
			}

			_currentRail = underRail;
			_currentRailApproved = false;
			_isStoppingDueToDeadEnd = false;

			// Notify new rail
			if (_currentRail != null)
			{
				LogTrain($"Entered rail '{_currentRail.Name}'.");
				_currentRail.SetCurrentTrain(this);
			}
		}

		// Check if current rail requires approval
		if (_currentRail != null && _currentRail.RequiredApproval && !_currentRailApproved)
		{
			LogTrain($"Rail '{_currentRail.Name}' requires approval; stopping at center and waiting.");
			_isMoving = false;
			return;
		}

		// Try to find next rail
		if (!TryFindAndValidateNextRail())
		{
			// No next rail - dead-end
			LogTrain($"No next rail available; reached dead-end at '{_currentRail.Name}'.");
			_isStoppingDueToDeadEnd = true;
			_isMoving = false;
			TryEmitFinalStop();
			return;
		}

		LogTrain($"Next rail found: '{_nextRail.Name}'");

		// Check if next rail is occupied
		if (_nextRail != null && _nextRail.CurrentTrain != null)
		{
			LogTrain($"Next rail '{_nextRail.Name}' is occupied by another train; waiting for it to clear.");
			StartWaitingForRailClear(_nextRail);
			return;
		}

		// Next rail is free - create task to move to its center
		RailData nextRailData;
		if (!TryGetRailData(_nextRail, _forwardDirection, out nextRailData))
		{
			LogWarning("Train3D: Unable to get next rail data for task.");
			LogTrain("Cannot create movement task: Unable to read next rail data.");
			_isMoving = false;
			return;
		}

		LogTrain($"Creating movement task to '{_nextRail.Name}': from {GlobalPosition} to {nextRailData.TopCenter}");
		_currentTask = MovementTask.Create(GlobalPosition, nextRailData.TopCenter);
		LogTrain($"Task created: distance={_currentTask.Distance:F2}m");
		
		if (!_currentTask.IsValid)
		{
			GD.PushError($"Train '{Name}': Created movement task is invalid.");
			_isMoving = false;
			return;
		}
	}

	private bool TryInitializeOnClosestRail()
	{
		RailRoad3D nearestRail = FindNearestRailRoadBySphereQuery();
		if (nearestRail == null)
		{
			LogWarning("Train3D: No RailRoad3D found in search radius.");
			LogTrain("Initialization failed: No RailRoad3D found in search radius.");
			return false;
		}

		RailData data;
		if (!TryGetRailData(nearestRail, GlobalTransform.Basis.Z.Normalized(), out data))
		{
			LogWarning($"Train3D: Failed to read rail data from '{nearestRail.Name}'.");
			LogTrain($"Initialization failed: Could not read rail data from '{nearestRail.Name}'.");
			return false;
		}

		_currentRail = nearestRail;
		_currentRail.SetCurrentTrain(this);
		_forwardDirection = data.Forward;
		GlobalPosition = data.TopCenter;
		TryEmitInitialPlacement();
		LogTrain($"Initialized on rail '{_currentRail.Name}'; starting movement.");
		LogTrain($"Position set to center: {GlobalPosition}");

		// Start moving immediately by evaluating next movement
		// This will find the next rail and create the first real task
		_isMoving = true;
		EvaluateNextMovement();

		return true;
	}

	private void TryEmitInitialPlacement()
	{
		if (_hasEmittedInitialPlacement || _currentRail == null)
		{
			return;
		}

		GlobalEvents events = GlobalEvents.Instance;
		if (events == null)
		{
			LogWarning("Train3D: GlobalEvents autoload was not found when emitting initial placement event.");
			return;
		}

		events.EmitTrainPlacedOnTrack(this, _currentRail);
		_hasEmittedInitialPlacement = true;
	}

	private void TryEmitFinalStop()
	{
		if (_hasEmittedFinalStop || _currentRail == null || !_isStoppingDueToDeadEnd)
		{
			return;
		}

		GlobalEvents events = GlobalEvents.Instance;
		if (events == null)
		{
			LogWarning("Train3D: GlobalEvents autoload was not found when emitting final stop event.");
			return;
		}

		events.EmitTrainStoppedOnFinalRail(this, _currentRail);
		EmitSignal(SignalName.CantMoveAnymore, this);
		_hasEmittedFinalStop = true;
	}

	private RailRoad3D FindNearestRailRoadBySphereQuery()
	{
		World3D world = GetWorld3D();
		PhysicsDirectSpaceState3D spaceState = world.DirectSpaceState;
		Vector3 center = GlobalPosition;

		SphereShape3D sphere = new();
		PhysicsShapeQueryParameters3D query = new()
		{
			Shape = sphere,
			Transform = Transform3D.Identity,
			CollideWithAreas = false,
			CollideWithBodies = true,
			CollisionMask = uint.MaxValue
		};

		for (float radius = SearchStartRadius; radius <= SearchMaxRadius; radius += SearchStep)
		{
			sphere.Radius = radius;
			query.Transform = new Transform3D(Basis.Identity, center);

			Godot.Collections.Array<Godot.Collections.Dictionary> hits = spaceState.IntersectShape(query, 256);
			if (hits.Count == 0)
			{
				continue;
			}

			HashSet<RailRoad3D> rails = new();
			foreach (Godot.Collections.Dictionary hit in hits)
			{
				if (!hit.TryGetValue("collider", out Variant colliderVariant))
				{
					continue;
				}

				Node colliderNode = colliderVariant.AsGodotObject() as Node;
				RailRoad3D rail = FindRailRoadAncestor(colliderNode);
				if (rail != null)
				{
					rails.Add(rail);
				}
			}

			if (rails.Count == 0)
			{
				continue;
			}

			RailRoad3D closestRail = null;
			float closestDistanceSq = float.MaxValue;
			foreach (RailRoad3D rail in rails)
			{
				float distSq = center.DistanceSquaredTo(rail.GlobalPosition);
				if (distSq < closestDistanceSq)
				{
					closestDistanceSq = distSq;
					closestRail = rail;
				}
			}

			if (closestRail != null)
			{
				return closestRail;
			}
		}

		return null;
	}

	private bool TryFindAndValidateNextRail(bool emitWarnings = true)
	{
		if (_currentRail == null)
		{
			if (emitWarnings)
			{
				LogWarning("Train3D: Current rail is null when finding next rail.");
			}
			return false;
		}

		RailRoad3D candidate = FindNextRailByForwardRay();
		if (candidate == null)
		{
			if (emitWarnings)
			{
				LogWarning($"Train3D: No next RailRoad3D found ahead of '{_currentRail.Name}'.");
			}
			_nextRail = null;
			return false;
		}

		if (!AreRailsGapless(_currentRail, candidate))
		{
			if (emitWarnings)
			{
				LogWarning($"Train3D: Rails '{_currentRail.Name}' and '{candidate.Name}' are not edge-aligned gapless.");
			}
			_nextRail = null;
			return false;
		}

		_nextRail = candidate;
		return true;
	}

	private bool IsWorldReady()
	{
		if (!IsInsideTree())
		{
			return false;
		}

		World3D world = GetWorld3D();
		if (world == null)
		{
			return false;
		}

		return world.DirectSpaceState != null;
	}

	private RailRoad3D FindNextRailByForwardRay()
	{
		RailData currentData;
		if (!TryGetRailData(_currentRail, _forwardDirection, out currentData))
		{
			LogWarning("Train3D: Could not calculate current rail edge before forward raycast.");
			return null;
		}

		PhysicsDirectSpaceState3D spaceState = GetWorld3D().DirectSpaceState;
		Vector3 from = currentData.Center;
		Vector3 to = from + _forwardDirection * ForwardRayLength;

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to);
		query.CollideWithAreas = false;
		query.CollideWithBodies = true;
		query.CollisionMask = RayCollisionMask;
		query.HitFromInside = true;

		Godot.Collections.Array<Rid> excluded = new();
		for (int i = 0; i < 8; i++)
		{
			query.Exclude = excluded;
			Godot.Collections.Dictionary hit = spaceState.IntersectRay(query);
			if (hit.Count == 0)
			{
				return null;
			}

			CollisionObject3D colliderObject;
			RailRoad3D rail = ResolveRailRoadFromRayHit(hit, "Forward ray", out colliderObject);
			if (rail == null)
			{
				return null;
			}

			if (rail != _currentRail)
			{
				return rail;
			}

			if (colliderObject == null)
			{
				LogWarning("Train3D: Forward ray self-hit collider is not a CollisionObject3D.");
				return null;
			}

			excluded.Add(colliderObject.GetRid());
		}

		LogWarning("Train3D: Forward ray exceeded retry budget while skipping current rail hits.");
		return null;
	}

	private RailRoad3D FindRailBelowByDownwardRay()
	{
		PhysicsDirectSpaceState3D spaceState = GetWorld3D().DirectSpaceState;
		Vector3 from = GlobalPosition + Vector3.Up * RayStartLift;
		Vector3 to = from + Vector3.Down * DownRayLength;

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to);
		query.CollideWithAreas = false;
		query.CollideWithBodies = true;
		query.CollisionMask = RayCollisionMask;

		Godot.Collections.Dictionary hit = spaceState.IntersectRay(query);
		if (hit.Count == 0)
		{
			return null;
		}

		CollisionObject3D _;
		RailRoad3D rail = ResolveRailRoadFromRayHit(hit, "Downward ray", out _);
		if (rail == null)
		{
			LogWarning("Train3D: Downward ray could not resolve RailRoad3D via CollisionShape3D parent chain.");
		}

		return rail;
	}

	private bool AreRailsGapless(RailRoad3D fromRail, RailRoad3D toRail)
	{
		RailData fromData;
		RailData toData;
		if (!TryGetRailData(fromRail, _forwardDirection, out fromData))
		{
			LogWarning($"Train3D: Failed to read rail data for '{fromRail.Name}' when checking gap.");
			return false;
		}

		if (!TryGetRailData(toRail, _forwardDirection, out toData))
		{
			LogWarning($"Train3D: Failed to read rail data for '{toRail.Name}' when checking gap.");
			return false;
		}

		float forwardAlignment = Mathf.Abs(fromData.Forward.Dot(toData.Forward));
		if (forwardAlignment < 0.98f)
		{
			LogWarning($"Train3D: Rail forward axes differ too much ({forwardAlignment:0.000}).");
			return false;
		}

		float gap = fromData.ForwardEdge.DistanceTo(toData.BackwardEdge);
		if (gap > GapTolerance)
		{
			LogWarning($"Train3D: Gap between rails is {gap:0.000}, tolerance is {GapTolerance:0.000}.");
			return false;
		}

		return true;
	}

	private bool TryGetRailData(RailRoad3D rail, Vector3 preferredForward, out RailData data)
	{
		data = default;
		if (rail == null)
		{
			LogWarning("Train3D: TryGetRailData received null rail.");
			return false;
		}

		CollisionShape3D collisionShape = FindFirstCollisionShape(rail);
		if (collisionShape == null)
		{
			LogWarning($"Train3D: Rail '{rail.Name}' has no CollisionShape3D child.");
			return false;
		}

		if (collisionShape.Shape is not BoxShape3D box)
		{
			LogWarning($"Train3D: Rail '{rail.Name}' CollisionShape3D is not a BoxShape3D.");
			return false;
		}

		Vector3 centerWorld = collisionShape.ToGlobal(Vector3.Zero);
		float halfY = box.Size.Y * 0.5f;
		Vector3 localCenterTop = new Vector3(0.0f, halfY, 0.0f);

		float halfX = box.Size.X * 0.5f;
		float halfZ = box.Size.Z * 0.5f;
		Vector3 topPlusX = collisionShape.ToGlobal(new Vector3(halfX, halfY, 0.0f));
		Vector3 topMinusX = collisionShape.ToGlobal(new Vector3(-halfX, halfY, 0.0f));
		Vector3 topPlusZ = collisionShape.ToGlobal(new Vector3(0.0f, halfY, halfZ));
		Vector3 topMinusZ = collisionShape.ToGlobal(new Vector3(0.0f, halfY, -halfZ));

		Vector3 forward = preferredForward;
		if (forward.LengthSquared() < 0.0001f)
		{
			forward = -GlobalTransform.Basis.Z;
		}
		forward.Y = 0.0f;
		if (forward.LengthSquared() < 0.0001f)
		{
			forward = Vector3.Forward;
		}
		forward = forward.Normalized();

		Vector3 xAxis = (topPlusX - topMinusX).Normalized();
		Vector3 zAxis = (topPlusZ - topMinusZ).Normalized();
		float xScore = Mathf.Abs(xAxis.Dot(forward));
		float zScore = Mathf.Abs(zAxis.Dot(forward));

		Vector3 forwardEdge;
		Vector3 backwardEdge;
		Vector3 towardPlus;
		if (xScore >= zScore)
		{
			forwardEdge = topPlusX;
			backwardEdge = topMinusX;
			towardPlus = xAxis;
		}
		else
		{
			forwardEdge = topPlusZ;
			backwardEdge = topMinusZ;
			towardPlus = zAxis;
		}

		if (towardPlus.Dot(forward) < 0.0f)
		{
			towardPlus = -towardPlus;
			Vector3 temp = forwardEdge;
			forwardEdge = backwardEdge;
			backwardEdge = temp;
		}

		data = new RailData
		{
			Rail = rail,
			Center = centerWorld,
			TopCenter = collisionShape.ToGlobal(localCenterTop),
			Forward = towardPlus,
			ForwardEdge = forwardEdge,
			BackwardEdge = backwardEdge
		};

		return true;
	}

	private bool ShouldShowRuntimePreview()
	{
		// In editor build runs (F5/F6), OS.HasFeature("editor") is true while Engine.IsEditorHint() is false.
		return ShowRoutePreviewInEditorRun && OS.HasFeature("editor") && !Engine.IsEditorHint();
	}

	private void EnsureRuntimePreviewNodes()
	{
		if (!ShouldShowRuntimePreview() || _runtimePreviewRoot != null)
		{
			return;
		}

		_previewArrowMaterial = CreatePreviewMaterial(new(0.2f, 0.85f, 1.0f, 1.0f));
		_previewCurrentRailMaterial = CreatePreviewMaterial(PreviewCurrentRailYellow);
		_previewNextRailMaterial = CreatePreviewMaterial(new(1.0f, 0.2f, 0.2f, 1.0f));

		_runtimePreviewRoot = new()
		{
			Name = "TrainRoutePreview",
			TopLevel = true
		};
		AddChild(_runtimePreviewRoot);

		_previewArrowMesh = CreatePreviewMeshNode("DirectionArrow", _previewArrowMaterial);
		_previewCurrentRailMesh = CreatePreviewMeshNode("CurrentRail", _previewCurrentRailMaterial);
		_previewNextRailMesh = CreatePreviewMeshNode("NextRail", _previewNextRailMaterial);

		_runtimePreviewRoot.AddChild(_previewArrowMesh);
		_runtimePreviewRoot.AddChild(_previewCurrentRailMesh);
		_runtimePreviewRoot.AddChild(_previewNextRailMesh);
	}

	private static StandardMaterial3D CreatePreviewMaterial(Color color)
	{
		return new()
		{
			AlbedoColor = color,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			NoDepthTest = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
		};
	}

	private static MeshInstance3D CreatePreviewMeshNode(string name, Material material)
	{
		return new()
		{
			Name = name,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = material
		};
	}

	private void UpdateRuntimePreviewVisuals()
	{
		if (!ShouldShowRuntimePreview())
		{
			if (_runtimePreviewRoot != null)
			{
				_runtimePreviewRoot.Visible = false;
			}
			return;
		}

		EnsureRuntimePreviewNodes();
		if (_runtimePreviewRoot == null)
		{
			return;
		}

		_runtimePreviewRoot.Visible = true;
		_runtimePreviewRoot.GlobalTransform = Transform3D.Identity;

		bool isStoppedOnDeadEnd = _nextRail == null && _initializationCompleted && !_isMoving;
		_previewCurrentRailMaterial.AlbedoColor = isStoppedOnDeadEnd ? PreviewCurrentRailGreen : PreviewCurrentRailYellow;
		_previewArrowMesh.Mesh = BuildArrowPreviewMesh();
		_previewCurrentRailMesh.Mesh = BuildRailWirePreviewMesh(_currentRail);

		if (_nextRail == null)
		{
			_previewNextRailMesh.Visible = false;
			_previewNextRailMesh.Mesh = null;
		}
		else
		{
			_previewNextRailMesh.Visible = true;
			_previewNextRailMesh.Mesh = BuildRailWirePreviewMesh(_nextRail);
		}
	}

	private Mesh BuildArrowPreviewMesh()
	{
		Vector3 forward = _forwardDirection;
		forward.Y = 0.0f;
		if (forward.LengthSquared() < 0.0001f)
		{
			forward = -GlobalTransform.Basis.Z;
			forward.Y = 0.0f;
		}
		if (forward.LengthSquared() < 0.0001f)
		{
			forward = Vector3.Forward;
		}

		forward = forward.Normalized();
		Vector3 start = GlobalPosition;
		CollisionShape3D ownCollisionShape = FindFirstCollisionShape(this);
		if (TryGetForwardEdgeCenterForShape(ownCollisionShape, forward, out Vector3 forwardEdgeCenter))
		{
			start = forwardEdgeCenter;
		}

		Vector3 tip = start + forward * PreviewArrowLength;

		Vector3 right = forward.Cross(Vector3.Up);
		if (right.LengthSquared() < 0.0001f)
		{
			right = Vector3.Right;
		}
		right = right.Normalized();

		Vector3 headBase = tip - forward * PreviewArrowHeadLength;
		Vector3 headLeft = headBase + right * PreviewArrowHeadWidth;
		Vector3 headRight = headBase - right * PreviewArrowHeadWidth;
		Vector3 wingNormal = right.Cross(forward);
		if (wingNormal.LengthSquared() < 0.0001f)
		{
			wingNormal = Vector3.Up;
		}
		wingNormal = wingNormal.Normalized();
		Vector3 headUp = headBase + wingNormal * PreviewArrowHeadWidth;
		Vector3 headDown = headBase - wingNormal * PreviewArrowHeadWidth;

		return BuildLineMesh(new[]
		{
			start, tip,
			tip, headLeft,
			tip, headRight,
			tip, headUp,
			tip, headDown
		});
	}

	private static bool TryGetForwardEdgeCenterForShape(CollisionShape3D collisionShape, Vector3 preferredForward, out Vector3 forwardEdgeCenter)
	{
		forwardEdgeCenter = Vector3.Zero;
		if (collisionShape == null)
		{
			return false;
		}

		if (collisionShape.Shape is BoxShape3D box)
		{
			return TryGetForwardEdgeCenterForBoxShape(collisionShape, preferredForward, box, out forwardEdgeCenter);
		}
		else if (collisionShape.Shape is CylinderShape3D cylinder)
		{
			return TryGetForwardEdgeCenterForCylinderHeightAxis(collisionShape, preferredForward, cylinder.Height, out forwardEdgeCenter);
		}
		else if (collisionShape.Shape is CapsuleShape3D capsule)
		{
			return TryGetForwardEdgeCenterForRadialShape(collisionShape, preferredForward, capsule.Radius, out forwardEdgeCenter);
		}
		else if (collisionShape.Shape is SphereShape3D sphere)
		{
			return TryGetForwardEdgeCenterForRadialShape(collisionShape, preferredForward, sphere.Radius, out forwardEdgeCenter);
		}

		return false;
	}

	private static bool TryGetForwardEdgeCenterForBoxShape(
		CollisionShape3D collisionShape,
		Vector3 preferredForward,
		BoxShape3D box,
		out Vector3 forwardEdgeCenter)
	{
		forwardEdgeCenter = Vector3.Zero;
		if (collisionShape == null || box == null)
		{
			return false;
		}

		Vector3 forward = preferredForward;
		forward.Y = 0.0f;
		if (forward.LengthSquared() < 0.0001f)
		{
			forward = Vector3.Forward;
		}
		forward = forward.Normalized();

		float halfX = box.Size.X * 0.5f;
		float halfZ = box.Size.Z * 0.5f;
		Vector3 plusXCenter = collisionShape.ToGlobal(new Vector3(halfX, 0.0f, 0.0f));
		Vector3 minusXCenter = collisionShape.ToGlobal(new Vector3(-halfX, 0.0f, 0.0f));
		Vector3 plusZCenter = collisionShape.ToGlobal(new Vector3(0.0f, 0.0f, halfZ));
		Vector3 minusZCenter = collisionShape.ToGlobal(new Vector3(0.0f, 0.0f, -halfZ));

		Vector3 xAxis = (plusXCenter - minusXCenter).Normalized();
		Vector3 zAxis = (plusZCenter - minusZCenter).Normalized();
		float xScore = Mathf.Abs(xAxis.Dot(forward));
		float zScore = Mathf.Abs(zAxis.Dot(forward));

		Vector3 towardPlus;
		Vector3 positiveFaceCenter;
		Vector3 negativeFaceCenter;
		if (xScore >= zScore)
		{
			towardPlus = xAxis;
			positiveFaceCenter = plusXCenter;
			negativeFaceCenter = minusXCenter;
		}
		else
		{
			towardPlus = zAxis;
			positiveFaceCenter = plusZCenter;
			negativeFaceCenter = minusZCenter;
		}

		forwardEdgeCenter = towardPlus.Dot(forward) >= 0.0f ? positiveFaceCenter : negativeFaceCenter;

		return true;
	}

	private static bool TryGetForwardEdgeCenterForRadialShape(
		CollisionShape3D collisionShape,
		Vector3 preferredForward,
		float radius,
		out Vector3 forwardEdgeCenter)
	{
		forwardEdgeCenter = Vector3.Zero;
		if (collisionShape == null || radius <= 0.0f)
		{
			return false;
		}

		Vector3 worldForward = preferredForward;
		worldForward.Y = 0.0f;
		if (worldForward.LengthSquared() < 0.0001f)
		{
			worldForward = Vector3.Forward;
		}
		worldForward = worldForward.Normalized();

		Vector3 center = collisionShape.ToGlobal(Vector3.Zero);
		Vector3 localForward = collisionShape.ToLocal(center + worldForward);
		localForward.Y = 0.0f;
		if (localForward.LengthSquared() < 0.0001f)
		{
			localForward = Vector3.Forward;
		}
		localForward = localForward.Normalized();

		Vector3 localEdge = new Vector3(localForward.X * radius, 0.0f, localForward.Z * radius);
		forwardEdgeCenter = collisionShape.ToGlobal(localEdge);
		return true;
	}

	private static bool TryGetForwardEdgeCenterForCylinderHeightAxis(
		CollisionShape3D collisionShape,
		Vector3 preferredForward,
		float height,
		out Vector3 forwardEdgeCenter)
	{
		forwardEdgeCenter = Vector3.Zero;
		if (collisionShape == null || height <= 0.0f)
		{
			return false;
		}

		Vector3 worldForward = preferredForward;
		if (worldForward.LengthSquared() < 0.0001f)
		{
			worldForward = Vector3.Forward;
		}
		worldForward = worldForward.Normalized();

		Vector3 axisPlus = collisionShape.ToGlobal(new Vector3(0.0f, height * 0.5f, 0.0f));
		Vector3 axisMinus = collisionShape.ToGlobal(new Vector3(0.0f, -height * 0.5f, 0.0f));
		Vector3 axisDirection = (axisPlus - axisMinus).Normalized();

		forwardEdgeCenter = axisDirection.Dot(worldForward) >= 0.0f ? axisPlus : axisMinus;
		return true;
	}

	private Mesh BuildRailWirePreviewMesh(RailRoad3D rail)
	{
		if (rail == null)
		{
			return null;
		}

		CollisionShape3D collisionShape = FindFirstCollisionShape(rail);
		if (collisionShape == null || collisionShape.Shape is not BoxShape3D box)
		{
			return null;
		}

		Vector3 h = box.Size * 0.5f;
		Vector3[] corners = new[]
		{
			collisionShape.ToGlobal(new Vector3(-h.X, -h.Y, -h.Z)),
			collisionShape.ToGlobal(new Vector3(h.X, -h.Y, -h.Z)),
			collisionShape.ToGlobal(new Vector3(h.X, -h.Y, h.Z)),
			collisionShape.ToGlobal(new Vector3(-h.X, -h.Y, h.Z)),
			collisionShape.ToGlobal(new Vector3(-h.X, h.Y, -h.Z)),
			collisionShape.ToGlobal(new Vector3(h.X, h.Y, -h.Z)),
			collisionShape.ToGlobal(new Vector3(h.X, h.Y, h.Z)),
			collisionShape.ToGlobal(new Vector3(-h.X, h.Y, h.Z))
		};

		int[,] edges = new int[,]
		{
			{ 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
			{ 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
			{ 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
		};

		Vector3[] points = new Vector3[edges.GetLength(0) * 2];
		int write = 0;
		for (int i = 0; i < edges.GetLength(0); i++)
		{
			points[write++] = corners[edges[i, 0]];
			points[write++] = corners[edges[i, 1]];
		}

		return BuildLineMesh(points);
	}

	private static Mesh BuildLineMesh(IReadOnlyList<Vector3> points)
	{
		if (points == null || points.Count < 2)
		{
			return null;
		}

		ImmediateMesh mesh = new();
		mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
		for (int i = 0; i < points.Count; i++)
		{
			mesh.SurfaceAddVertex(points[i]);
		}
		mesh.SurfaceEnd();
		return mesh;
	}

	private void MoveForward(float delta)
	{
		GlobalPosition += _forwardDirection * Speed * delta;
	}

	private bool MoveToMiddleAndStop(float delta)
	{
		Vector3 toTarget = _stopTargetWorld - GlobalPosition;
		float distance = toTarget.Length();
		if (distance <= StopEpsilon)
		{
			GlobalPosition = _stopTargetWorld;
			return true; // Reached target
		}

		Vector3 step = toTarget.Normalized() * Speed * delta;
		if (step.Length() >= distance)
		{
			GlobalPosition = _stopTargetWorld;
			return true; // Reached target
		}

		GlobalPosition += step;
		return false; // Still moving
	}

	private static RailRoad3D FindRailRoadAncestor(Node node)
	{
		Node current = node;
		while (current != null)
		{
			if (current is RailRoad3D rail)
			{
				return rail;
			}

			current = current.GetParent();
		}

		return null;
	}

	private RailRoad3D ResolveRailRoadFromRayHit(
		Godot.Collections.Dictionary hit,
		string context,
		out CollisionObject3D colliderObject)
	{
		colliderObject = null;
		if (!hit.TryGetValue("collider", out Variant colliderVariant))
		{
			LogWarning($"Train3D: {context} hit had no collider field.");
			return null;
		}

		colliderObject = colliderVariant.AsGodotObject() as CollisionObject3D;
		Node colliderNode = colliderVariant.AsGodotObject() as Node;
		if (colliderObject == null)
		{
			LogWarning($"Train3D: {context} collider is not a CollisionObject3D.");
			return FindRailRoadAncestor(colliderNode);
		}

		if (hit.TryGetValue("shape", out Variant shapeVariant))
		{
			int shapeIndex = shapeVariant.AsInt32();
			uint ownerId = colliderObject.ShapeFindOwner(shapeIndex);
			Node ownerNode = colliderObject.ShapeOwnerGetOwner(ownerId) as Node;
			if (ownerNode is CollisionShape3D)
			{
				Node parent = ownerNode.GetParent();
				Node grandParent = parent != null ? parent.GetParent() : null;
				if (grandParent is RailRoad3D rail)
				{
					return rail;
				}

				LogWarning($"Train3D: {context} resolved CollisionShape3D, but its two-level parent is not RailRoad3D.");
			}
			else
			{
				LogWarning($"Train3D: {context} shape owner is not CollisionShape3D.");
			}
		}
		else
		{
			LogWarning($"Train3D: {context} hit had no shape index.");
		}

		return FindRailRoadAncestor(colliderNode);
	}

	private static CollisionShape3D FindFirstCollisionShape(Node node)
	{
		if (node is CollisionShape3D ownCollision)
		{
			return ownCollision;
		}

		foreach (Node child in node.GetChildren())
		{
			CollisionShape3D found = FindFirstCollisionShape(child);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}

	internal bool ApproveProceedingBeyondCenter()
	{
		if (_currentRail == null)
		{
			LogWarning("Train3D: Approve called but train is not on any rail.");
			LogTrain("Cannot approve: Train is not on any rail.");
			return false;
		}

		if (!_currentRail.RequiredApproval)
		{
			LogWarning("Train3D: Approve called on a rail that does not require approval.");
			LogTrain($"Cannot approve: Rail '{_currentRail.Name}' does not require approval.");
			return false;
		}

		if (IsInMotion)
		{
			LogWarning("Train3D: Cannot approve train proceeding - it is still in motion.");
			LogTrain("Cannot approve: Train is still in motion.");
			return false;
		}

		LogTrain($"Approved to proceed beyond '{_currentRail.Name}'; resuming movement.");
		_currentRailApproved = true;
		_currentTask = default; // Clear current task
		_isMoving = true;
		_isStoppingDueToDeadEnd = false;
		
		// Immediately evaluate next movement to create new task
		EvaluateNextMovement();
		return true;
	}

	private void StartWaitingForRailClear(RailRoad3D rail)
	{
		if (rail == null)
		{
			LogWarning("Train3D: StartWaitingForRailClear called with null rail.");
			return;
		}

		_isWaitingForNextRailClear = true;
		_waitingOnRail = rail;
		_waitingOnRail.TrainDeparted += OnWaitedRailCleared;
		LogTrain($"Waiting for '{rail.Name}' to clear...");
	}

	private void OnWaitedRailCleared()
	{
		if (_waitingOnRail != null)
		{
			string waitedRailName = _waitingOnRail.Name;
			_waitingOnRail.TrainDeparted -= OnWaitedRailCleared;

			// Check if the next rail is still occupied; if so, resume waiting
			if (_nextRail != null && _nextRail.CurrentTrain != null)
			{
				LogTrain($"'{waitedRailName}' cleared, but still occupied; resuming wait...");
				StartWaitingForRailClear(_nextRail);
			}
			else if (_isMoving)
			{
				LogTrain($"'{waitedRailName}' is now clear; resuming movement.");
				// Next rail is now free - resume movement with new task
				EvaluateNextMovement();
			}
		}

		_isWaitingForNextRailClear = false;
		_waitingOnRail = null;
	}

	private void LogTrain(string message)
	{
		if (!VerboseLogging)
		{
			return;
		}

		GD.Print($"Train '{Name}': {message}");
	}

	private void LogWarning(string message)
	{
		if (!VerboseLogging)
		{
			return;
		}

		GD.PushWarning(message);
	}

	private struct RailData
	{
		public RailRoad3D Rail;
		public Vector3 Center;
		public Vector3 TopCenter;
		public Vector3 Forward;
		public Vector3 ForwardEdge;
		public Vector3 BackwardEdge;
	}
}
