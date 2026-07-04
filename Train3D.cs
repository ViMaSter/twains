using Godot;
using System;
using System.Collections.Generic;

public partial class Train3D : Node3D
{
	[Export] public float Speed = 8.0f;
	[Export] public float ForwardRayLength = 40.0f;
	[Export] public float DownRayLength = 20.0f;
	[Export] public float GapTolerance = 0.05f;
	[Export] public float StopEpsilon = 0.02f;

	private const float SearchStartRadius = 1.0f;
	private const float SearchStep = 2.0f;
	private const float SearchMaxRadius = 300.0f;
	private const float RayStartLift = 0.2f;
	private const uint PHYSICS_LAYER__BLOCKER = 1u << 2; // Layer 3
	private const uint RayCollisionMask = uint.MaxValue & ~PHYSICS_LAYER__BLOCKER;

	private RailRoad3D _currentRail;
	private RailRoad3D _nextRail;
	private Vector3 _forwardDirection = Vector3.Forward;
	private bool _isMoving;
	private bool _moveToMiddleAndStop;
	private Vector3 _stopTargetWorld = Vector3.Zero;
	private bool _initializationCompleted;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_isMoving = false;
		_initializationCompleted = false;
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
		if (!_isMoving)
		{
			return;
		}

		if (_moveToMiddleAndStop)
		{
			MoveToMiddleAndStop((float)delta);
			return;
		}

		MoveForward((float)delta);

		RailRoad3D underRail = FindRailBelowByDownwardRay();
		if (underRail == null)
		{
			GD.PushWarning("Train3D: Downward ray did not hit any RailRoad3D while moving.");
			return;
		}

		if (underRail != _currentRail)
		{
			_currentRail = underRail;
			if (!TryFindAndValidateNextRail())
			{
				// TODO: Emit a signal that the train has reached a dead-end and is stopping.
				RailData currentData;
				if (!TryGetRailData(_currentRail, _forwardDirection, out currentData))
				{
					GD.PushWarning("Train3D: Unable to compute current rail center for stop behavior.");
					_isMoving = false;
					return;
				}

				_stopTargetWorld = currentData.TopCenter;
				_moveToMiddleAndStop = true;
			}
		}
	}

	private bool TryInitializeOnClosestRail()
	{
		RailRoad3D nearestRail = FindNearestRailRoadBySphereQuery();
		if (nearestRail == null)
		{
			GD.PushWarning("Train3D: No RailRoad3D found in search radius.");
			return false;
		}

		RailData data;
		if (!TryGetRailData(nearestRail, -GlobalTransform.Basis.Z.Normalized(), out data))
		{
			GD.PushWarning($"Train3D: Failed to read rail data from '{nearestRail.Name}'.");
			return false;
		}

		_currentRail = nearestRail;
		_forwardDirection = data.Forward;
		GlobalPosition = data.TopCenter;

		bool initialForwardHasNext = TryFindAndValidateNextRail(false);
		if (!initialForwardHasNext)
		{
			// If initial forward points to a dead-end, try the opposite direction once.
			_forwardDirection = -_forwardDirection;
			bool reverseForwardHasNext = TryFindAndValidateNextRail(false);
			if (!reverseForwardHasNext)
			{
				GD.PushWarning($"Train3D: No next RailRoad3D found ahead of '{_currentRail.Name}' in either direction.");
				_stopTargetWorld = data.TopCenter;
				_moveToMiddleAndStop = true;
			}
		}

		return true;
	}

	private RailRoad3D FindNearestRailRoadBySphereQuery()
	{
		World3D world = GetWorld3D();
		PhysicsDirectSpaceState3D spaceState = world.DirectSpaceState;
		Vector3 center = GlobalPosition;

		SphereShape3D sphere = new SphereShape3D();
		PhysicsShapeQueryParameters3D query = new PhysicsShapeQueryParameters3D
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

			HashSet<RailRoad3D> rails = new HashSet<RailRoad3D>();
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
				GD.PushWarning("Train3D: Current rail is null when finding next rail.");
			}
			return false;
		}

		RailRoad3D candidate = FindNextRailByForwardRay();
		if (candidate == null)
		{
			if (emitWarnings)
			{
				GD.PushWarning($"Train3D: No next RailRoad3D found ahead of '{_currentRail.Name}'.");
			}
			_nextRail = null;
			return false;
		}

		if (!AreRailsGapless(_currentRail, candidate))
		{
			if (emitWarnings)
			{
				GD.PushWarning($"Train3D: Rails '{_currentRail.Name}' and '{candidate.Name}' are not edge-aligned gapless.");
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
			GD.PushWarning("Train3D: Could not calculate current rail edge before forward raycast.");
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

		Godot.Collections.Array<Rid> excluded = new Godot.Collections.Array<Rid>();
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
				GD.PushWarning("Train3D: Forward ray self-hit collider is not a CollisionObject3D.");
				return null;
			}

			excluded.Add(colliderObject.GetRid());
		}

		GD.PushWarning("Train3D: Forward ray exceeded retry budget while skipping current rail hits.");
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
			GD.PushWarning("Train3D: Downward ray could not resolve RailRoad3D via CollisionShape3D parent chain.");
		}

		return rail;
	}

	private bool AreRailsGapless(RailRoad3D fromRail, RailRoad3D toRail)
	{
		RailData fromData;
		RailData toData;
		if (!TryGetRailData(fromRail, _forwardDirection, out fromData))
		{
			GD.PushWarning($"Train3D: Failed to read rail data for '{fromRail.Name}' when checking gap.");
			return false;
		}

		if (!TryGetRailData(toRail, _forwardDirection, out toData))
		{
			GD.PushWarning($"Train3D: Failed to read rail data for '{toRail.Name}' when checking gap.");
			return false;
		}

		float forwardAlignment = Mathf.Abs(fromData.Forward.Dot(toData.Forward));
		if (forwardAlignment < 0.98f)
		{
			GD.PushWarning($"Train3D: Rail forward axes differ too much ({forwardAlignment:0.000}).");
			return false;
		}

		float gap = fromData.ForwardEdge.DistanceTo(toData.BackwardEdge);
		if (gap > GapTolerance)
		{
			GD.PushWarning($"Train3D: Gap between rails is {gap:0.000}, tolerance is {GapTolerance:0.000}.");
			return false;
		}

		return true;
	}

	private bool TryGetRailData(RailRoad3D rail, Vector3 preferredForward, out RailData data)
	{
		data = default;
		if (rail == null)
		{
			GD.PushWarning("Train3D: TryGetRailData received null rail.");
			return false;
		}

		CollisionShape3D collisionShape = FindFirstCollisionShape(rail);
		if (collisionShape == null)
		{
			GD.PushWarning($"Train3D: Rail '{rail.Name}' has no CollisionShape3D child.");
			return false;
		}

		if (collisionShape.Shape is not BoxShape3D box)
		{
			GD.PushWarning($"Train3D: Rail '{rail.Name}' CollisionShape3D is not a BoxShape3D.");
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

	private void MoveForward(float delta)
	{
		GlobalPosition += _forwardDirection * Speed * delta;
	}

	private void MoveToMiddleAndStop(float delta)
	{
		Vector3 toTarget = _stopTargetWorld - GlobalPosition;
		float distance = toTarget.Length();
		if (distance <= StopEpsilon)
		{
			GlobalPosition = _stopTargetWorld;
			_isMoving = false;
			_moveToMiddleAndStop = false;
			return;
		}

		Vector3 step = toTarget.Normalized() * Speed * delta;
		if (step.Length() >= distance)
		{
			GlobalPosition = _stopTargetWorld;
			_isMoving = false;
			_moveToMiddleAndStop = false;
			return;
		}

		GlobalPosition += step;
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
			GD.PushWarning($"Train3D: {context} hit had no collider field.");
			return null;
		}

		colliderObject = colliderVariant.AsGodotObject() as CollisionObject3D;
		Node colliderNode = colliderVariant.AsGodotObject() as Node;
		if (colliderObject == null)
		{
			GD.PushWarning($"Train3D: {context} collider is not a CollisionObject3D.");
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

				GD.PushWarning($"Train3D: {context} resolved CollisionShape3D, but its two-level parent is not RailRoad3D.");
			}
			else
			{
				GD.PushWarning($"Train3D: {context} shape owner is not CollisionShape3D.");
			}
		}
		else
		{
			GD.PushWarning($"Train3D: {context} hit had no shape index.");
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
