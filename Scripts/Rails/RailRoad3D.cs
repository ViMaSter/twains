using Godot;

public partial class RailRoad3D : Node3D
{
	[Signal]
	public delegate void TrainDepartedEventHandler();

	[Export]
	public bool RequiredApproval = false;

	private Train3D _currentTrain;

	public Train3D CurrentTrain => _currentTrain;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	/// <summary>
	/// Approves the current train on this rail to proceed beyond the center.
	/// Returns false if no train is on the rail or if the train is still in motion.
	/// </summary>
	public bool Approve()
	{
		if (_currentTrain == null)
		{
			GD.PushWarning($"RailRoad3D '{Name}': Approve() called but no train is currently on this rail.");
			return false;
		}

		PhysicsDirectSpaceState3D spaceState = GetWorld3D().DirectSpaceState;
		if (spaceState == null)
		{
			GD.PushWarning($"RailRoad3D '{Name}': Cannot raycast - physics space state is not available.");
			return false;
		}

		Vector3 from = GlobalPosition;
		Vector3 to = from + Vector3.Up * 100.0f; // Raycast upward

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to);
		query.CollideWithAreas = false;
		query.CollideWithBodies = true;

		Godot.Collections.Dictionary hit = spaceState.IntersectRay(query);
		if (hit.Count == 0)
		{
			GD.PushWarning($"RailRoad3D '{Name}': Upward raycast did not hit any train.");
			return false;
		}

		if (!hit.TryGetValue("collider", out Variant colliderVariant))
		{
			GD.PushWarning($"RailRoad3D '{Name}': Upward raycast hit had no collider field.");
			return false;
		}

		CollisionObject3D colliderObject = colliderVariant.AsGodotObject() as CollisionObject3D;
		Node colliderNode = colliderVariant.AsGodotObject() as Node;

		if (colliderObject == null)
		{
			GD.PushWarning($"RailRoad3D '{Name}': Upward raycast collider is not a CollisionObject3D.");
			return false;
		}

		// Try to find Train3D in the parent chain
		Train3D hitTrain = null;
		if (hit.TryGetValue("shape", out Variant shapeVariant))
		{
			int shapeIndex = shapeVariant.AsInt32();
			uint ownerId = colliderObject.ShapeFindOwner(shapeIndex);
			Node ownerNode = colliderObject.ShapeOwnerGetOwner(ownerId) as Node;
			
			// parent.parent should be Train3D
			if (ownerNode != null)
			{
				Node parent = ownerNode.GetParent();
				if (parent != null)
				{
					Node grandParent = parent.GetParent();
					if (grandParent is Train3D train)
					{
						hitTrain = train;
					}
				}
			}
		}

		if (hitTrain == null)
		{
			hitTrain = FindTrainAncestor(colliderNode);
		}

		if (hitTrain == null)
		{
			GD.PushWarning($"RailRoad3D '{Name}': Upward raycast did not resolve to a Train3D object.");
			return false;
		}

		if (hitTrain != _currentTrain)
		{
			GD.PushWarning($"RailRoad3D '{Name}': Upward raycast hit a different train than the one currently on this rail.");
			return false;
		}

		if (hitTrain.IsInMotion)
		{
			GD.PushWarning($"RailRoad3D '{Name}': Cannot approve train - it is still in motion.");
			return false;
		}

		return hitTrain.ApproveProceedingBeyondCenter();
	}

	internal void SetCurrentTrain(Train3D train)
	{
		_currentTrain = train;
	}

	internal void ClearCurrentTrain()
	{
		_currentTrain = null;
		EmitSignal(SignalName.TrainDeparted);
	}

	private static Train3D FindTrainAncestor(Node node)
	{
		Node current = node;
		while (current != null)
		{
			if (current is Train3D train)
			{
				return train;
			}

			current = current.GetParent();
		}

		return null;
	}
}
