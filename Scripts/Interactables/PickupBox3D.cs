using Godot;
using System;
using System.Collections.Generic;
using Twains;

public partial class PickupBox3D : RigidBody3D, IInteractable
{
	private const int SlotPhysicsLayer = 5;
	private const string SlotOccupiedMetaKey = "slot_occupied_by";
	private const float DefaultSlotPitch = 1.2f;

	private Area3D _triggerVolume;
	private readonly HashSet<Area3D> _slotCandidates = new();
	private readonly List<Area3D> _currentSlots = new();
	private Node _slotOriginalParent;
	private Node _slotAnchorParent;
	private Vector3 _slotAnchorLocalPosition = Vector3.Zero;
	private Basis _slotAnchorLocalBasis = Basis.Identity;

	private CollisionShape3D _boxCollisionShape;
	private Vector2I _slotFootprint = Vector2I.One;

	[Export] public float SlotSnapDistanceThreshold { get; set; } = 0.75f;
	[Export] public float SlotCoverageTolerance { get; set; } = 0.3f;

	public override void _EnterTree()
	{
		EnsureTriggerSignalsConnected();
	}


	public override void _Ready()
	{
		_triggerVolume = this.FindChildOfType<Area3D>();
		_boxCollisionShape = FindSlotCollisionShape();
		_slotFootprint = CalculateSlotFootprint();
		
		if (_triggerVolume is null)
		{
			throw new InvalidOperationException($"PickupBox3D '{Name}': No Area3D child found. Please add an Area3D as a child node.");
		}

		EnsureTriggerSignalsConnected();
	}

	public override void _ExitTree()
	{
		if (_triggerVolume is not null)
		{
			var bodyEnteredCallable = Callable.From<Node3D>(OnTriggerBodyEntered);
			var bodyExitedCallable = Callable.From<Node3D>(OnTriggerBodyExited);
			var areaEnteredCallable = Callable.From<Area3D>(OnTriggerAreaEntered);
			var areaExitedCallable = Callable.From<Area3D>(OnTriggerAreaExited);

			if (_triggerVolume.IsConnected(Area3D.SignalName.BodyEntered, bodyEnteredCallable))
			{
				_triggerVolume.Disconnect(Area3D.SignalName.BodyEntered, bodyEnteredCallable);
			}

			if (_triggerVolume.IsConnected(Area3D.SignalName.BodyExited, bodyExitedCallable))
			{
				_triggerVolume.Disconnect(Area3D.SignalName.BodyExited, bodyExitedCallable);
			}

			if (_triggerVolume.IsConnected(Area3D.SignalName.AreaEntered, areaEnteredCallable))
			{
				_triggerVolume.Disconnect(Area3D.SignalName.AreaEntered, areaEnteredCallable);
			}

			if (_triggerVolume.IsConnected(Area3D.SignalName.AreaExited, areaExitedCallable))
			{
				_triggerVolume.Disconnect(Area3D.SignalName.AreaExited, areaExitedCallable);
			}
		}

		ReleaseCurrentSlot();
		_slotCandidates.Clear();
	}

	public override void _PhysicsProcess(double delta)
	{
		TrySnapIntoSlot();
	}

	private void EnsureTriggerSignalsConnected()
	{
		if (_triggerVolume is null)
		{
			_triggerVolume = this.FindChildOfType<Area3D>();
		}

		if (_triggerVolume is null)
		{
			return;
		}

		var bodyEnteredCallable = Callable.From<Node3D>(OnTriggerBodyEntered);
		var bodyExitedCallable = Callable.From<Node3D>(OnTriggerBodyExited);
		var areaEnteredCallable = Callable.From<Area3D>(OnTriggerAreaEntered);
		var areaExitedCallable = Callable.From<Area3D>(OnTriggerAreaExited);
		_triggerVolume.CollisionMask |= 1u << (SlotPhysicsLayer - 1);

		if (!_triggerVolume.IsConnected(Area3D.SignalName.BodyEntered, bodyEnteredCallable))
		{
			_triggerVolume.Connect(Area3D.SignalName.BodyEntered, bodyEnteredCallable);
		}

		if (!_triggerVolume.IsConnected(Area3D.SignalName.BodyExited, bodyExitedCallable))
		{
			_triggerVolume.Connect(Area3D.SignalName.BodyExited, bodyExitedCallable);
		}

		if (!_triggerVolume.IsConnected(Area3D.SignalName.AreaEntered, areaEnteredCallable))
		{
			_triggerVolume.Connect(Area3D.SignalName.AreaEntered, areaEnteredCallable);
		}

		if (!_triggerVolume.IsConnected(Area3D.SignalName.AreaExited, areaExitedCallable))
		{
			_triggerVolume.Connect(Area3D.SignalName.AreaExited, areaExitedCallable);
		}
	}

	private void OnTriggerBodyEntered(Node3D body)
	{
		CharacterBodyPawn3D pawn = body as CharacterBodyPawn3D;
		if (pawn is null)
		{
			throw new InvalidOperationException($"PickupBox3D: Could not find CharacterBodyPawn3D in parent-parent chain for entering body '{body.Name}'.");
		}

		GD.Print($"{this.Name}: Something entered trigger volume: {body.Name}");
		pawn.SetInteractable(this);
	}

	private void OnTriggerBodyExited(Node3D body)
	{
		CharacterBodyPawn3D pawn = body as CharacterBodyPawn3D;
		if (pawn is null)
		{
			throw new InvalidOperationException($"PickupBox3D: Could not find CharacterBodyPawn3D for exiting body '{body.Name}'.");
		}

		GD.Print($"{this.Name}: Something left trigger volume: {body.Name}");
		pawn.RemoveInteractable(this);
	}

	private void OnTriggerAreaEntered(Area3D area)
	{
		if (IsSlotArea(area))
		{
			_slotCandidates.Add(area);
		}
	}

	private void OnTriggerAreaExited(Area3D area)
	{
		if (area != null)
		{
			_slotCandidates.Remove(area);
		}
	}

	private bool IsSlotArea(Area3D area)
	{
		if (area is null)
		{
			return false;
		}

		return (area.CollisionLayer & (1u << (SlotPhysicsLayer - 1))) != 0;
	}

	private void TrySnapIntoSlot()
	{
		if (MaintainCurrentSlotAnchor())
		{
			return;
		}

		float snapDistance = Mathf.Max(0.0f, SlotSnapDistanceThreshold);
		float maxDistanceSquared = snapDistance * snapDistance;

		if (_currentSlots.Count > 0)
		{
			Vector3 anchoredGlobalPosition = _slotAnchorParent is Node3D anchorParent
				? anchorParent.ToGlobal(_slotAnchorLocalPosition)
				: GlobalPosition;

			if (GlobalPosition.DistanceSquaredTo(anchoredGlobalPosition) > maxDistanceSquared)
			{
				ReleaseCurrentSlot();
			}
		}

		if (_slotCandidates.Count == 0)
		{
			return;
		}

		SlotPlacementCandidate bestPlacement = null;
		float bestDistanceSquared = float.MaxValue;

		foreach (var slot in _slotCandidates)
		{
			if (!GodotObject.IsInstanceValid(slot))
			{
				continue;
			}

			SlotPlacementCandidate placement = BuildPlacementFromAnchor(slot, maxDistanceSquared);
			if (placement == null || placement.DistanceSquared >= bestDistanceSquared)
			{
				continue;
			}

			bestDistanceSquared = placement.DistanceSquared;
			bestPlacement = placement;
		}

		if (bestPlacement != null)
		{
			SnapIntoSlots(bestPlacement);
		}
	}

	private bool MaintainCurrentSlotAnchor()
	{
		if (_currentSlots.Count == 0)
		{
			return false;
		}

		for (int i = _currentSlots.Count - 1; i >= 0; i--)
		{
			Area3D slot = _currentSlots[i];
			if (slot == null || !GodotObject.IsInstanceValid(slot))
			{
				ReleaseCurrentSlot();
				return false;
			}
		}

		if (_slotAnchorParent == null || !GodotObject.IsInstanceValid(_slotAnchorParent))
		{
			ReleaseCurrentSlot();
			return false;
		}

		if (GetParent() != _slotAnchorParent)
		{
			return false;
		}

		if (Position != _slotAnchorLocalPosition)
		{
			Position = _slotAnchorLocalPosition;
		}

		Basis = _slotAnchorLocalBasis;

		if (LinearVelocity != Vector3.Zero)
		{
			LinearVelocity = Vector3.Zero;
		}

		if (AngularVelocity != Vector3.Zero)
		{
			AngularVelocity = Vector3.Zero;
		}

		return true;
	}

	private SlotPlacementCandidate BuildPlacementFromAnchor(Area3D anchorSlot, float maxDistanceSquared)
	{
		if (!TryParseSlotCoordinates(anchorSlot, out SlotCoordinate anchorCoordinate))
		{
			return null;
		}

		float slotPitch = EstimateSlotPitch(anchorSlot.GetParent());
		Vector2I requiredFootprint = GetRequiredFootprintForParent(anchorSlot.GetParent());
		int requiredX = Mathf.Max(1, requiredFootprint.X);
		int requiredZ = Mathf.Max(1, requiredFootprint.Y);

		for (int offsetX = 0; offsetX < requiredX; offsetX++)
		{
			for (int offsetZ = 0; offsetZ < requiredZ; offsetZ++)
			{
				int originX = anchorCoordinate.X - offsetX;
				int originZ = anchorCoordinate.Z - offsetZ;
				SlotPlacementCandidate placement = BuildPlacementFromOrigin(anchorSlot, originX, originZ, requiredX, requiredZ, slotPitch, maxDistanceSquared);
				if (placement != null)
				{
					return placement;
				}
			}
		}

		return null;
	}

	private SlotPlacementCandidate BuildPlacementFromOrigin(Area3D anchorSlot, int originX, int originZ, int requiredX, int requiredZ, float slotPitch, float maxDistanceSquared)
	{
		if (anchorSlot.GetParent() is not Node parentNode)
		{
			return null;
		}

		List<Area3D> requiredSlots = new(requiredX * requiredZ);
		for (int x = 0; x < requiredX; x++)
		{
			for (int z = 0; z < requiredZ; z++)
			{
				if (!TryGetSlotByCoordinates(parentNode, originX + x, originZ + z, out Area3D slot))
				{
					return null;
				}

				if (!IsSlotAvailable(slot))
				{
					return null;
				}

				if (!_slotCandidates.Contains(slot))
				{
					return null;
				}

				requiredSlots.Add(slot);
			}
		}

		Vector3 targetCenter = Vector3.Zero;
		for (int i = 0; i < requiredSlots.Count; i++)
		{
			targetCenter += requiredSlots[i].GlobalPosition;
		}

		targetCenter /= requiredSlots.Count;
		float distanceSquared = GlobalPosition.DistanceSquaredTo(targetCenter);
		if (distanceSquared > maxDistanceSquared)
		{
			return null;
		}

		if (!PassesSlotCoverageCheck(requiredSlots, targetCenter, slotPitch))
		{
			return null;
		}

		return new SlotPlacementCandidate(requiredSlots, parentNode, targetCenter, distanceSquared);
	}

	private bool IsSlotAvailable(Area3D slot)
	{
		if (_currentSlots.Contains(slot))
		{
			return true;
		}

		if (!slot.HasMeta(SlotOccupiedMetaKey))
		{
			return true;
		}

		PickupBox3D occupiedBy = slot.GetMeta(SlotOccupiedMetaKey).AsGodotObject() as PickupBox3D;
		if (occupiedBy == null || !GodotObject.IsInstanceValid(occupiedBy))
		{
			slot.RemoveMeta(SlotOccupiedMetaKey);
			return true;
		}

		return occupiedBy == this;
	}

	private bool PassesSlotCoverageCheck(List<Area3D> slots, Vector3 targetCenter, float slotPitch)
	{
		if (_boxCollisionShape?.Shape is not BoxShape3D boxShape)
		{
			return true;
		}

		float tolerance = Mathf.Max(0.0f, SlotCoverageTolerance);
		Vector3 halfExtents = boxShape.Size * 0.5f;
		float requiredHalfX = Mathf.Max(0.5f, halfExtents.X) - tolerance;
		float requiredHalfZ = Mathf.Max(0.5f, halfExtents.Z) - tolerance;
		float maxSlotDeviation = slotPitch * 0.5f + tolerance;

		for (int i = 0; i < slots.Count; i++)
		{
			Vector3 relative = slots[i].GlobalPosition - targetCenter;
			if (Mathf.Abs(relative.X) > requiredHalfX + maxSlotDeviation)
			{
				return false;
			}

			if (Mathf.Abs(relative.Z) > requiredHalfZ + maxSlotDeviation)
			{
				return false;
			}
		}

		return true;
	}

	private void SnapIntoSlots(SlotPlacementCandidate placement)
	{
		ReleaseCurrentSlot();
		FreezeForSlot();
		Basis snappedGlobalBasis = GetSnappedGlobalBasis(placement.Parent);

		_slotOriginalParent = GetParent();
		_slotAnchorParent = placement.Parent;
		Reparent(placement.Parent, false);
		_slotAnchorLocalPosition = placement.Parent is Node3D parentNode
			? parentNode.ToLocal(placement.TargetGlobalPosition)
			: Vector3.Zero;
		_slotAnchorLocalBasis = placement.Parent is Node3D anchorParent
			? anchorParent.GlobalBasis.Inverse() * snappedGlobalBasis
			: Basis.Identity;

		Position = _slotAnchorLocalPosition;
		Basis = _slotAnchorLocalBasis;

		_currentSlots.Clear();
		for (int i = 0; i < placement.Slots.Count; i++)
		{
			Area3D slot = placement.Slots[i];
			slot.SetMeta(SlotOccupiedMetaKey, this);
			SetSlotVisibility(slot, false);
			_currentSlots.Add(slot);
		}
	}

	private void SetSlotVisibility(Area3D slot, bool visible)
	{
		if (slot == null || !GodotObject.IsInstanceValid(slot))
		{
			return;
		}

		MeshInstance3D slotMesh = slot.FindChildOfType<MeshInstance3D>();
		if (slotMesh != null)
		{
			slotMesh.Visible = visible;
		}
	}

	private void FreezeForSlot()
	{
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
		FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
		Freeze = true;
		Sleeping = true;
	}

	private void UnfreezeForPickup()
	{
		Freeze = false;
		Sleeping = false;
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
	}

	private void ReleaseCurrentSlot()
	{
		if (_currentSlots.Count == 0)
		{
			_slotOriginalParent = null;
			_slotAnchorParent = null;
			_slotAnchorLocalBasis = Basis.Identity;
			return;
		}

		for (int i = 0; i < _currentSlots.Count; i++)
		{
			Area3D slot = _currentSlots[i];
			if (slot == null || !GodotObject.IsInstanceValid(slot))
			{
				continue;
			}

			if (!slot.HasMeta(SlotOccupiedMetaKey))
			{
				continue;
			}

			PickupBox3D occupiedBy = slot.GetMeta(SlotOccupiedMetaKey).AsGodotObject() as PickupBox3D;
			if (occupiedBy == this)
			{
				slot.RemoveMeta(SlotOccupiedMetaKey);
				SetSlotVisibility(slot, true);
			}
		}

		if (_slotAnchorParent != null && GetParent() == _slotAnchorParent && _slotOriginalParent != null && GodotObject.IsInstanceValid(_slotOriginalParent))
		{
			Reparent(_slotOriginalParent, true);
		}

		_currentSlots.Clear();
		_slotOriginalParent = null;
		_slotAnchorParent = null;
		_slotAnchorLocalPosition = Vector3.Zero;
		_slotAnchorLocalBasis = Basis.Identity;
	}

	private Vector2I GetRequiredFootprintForParent(Node slotParent)
	{
		int baseX = Mathf.Max(1, _slotFootprint.X);
		int baseZ = Mathf.Max(1, _slotFootprint.Y);

		if (baseX == baseZ)
		{
			return new Vector2I(baseX, baseZ);
		}

		if (slotParent is not Node3D parent3D)
		{
			return new Vector2I(baseX, baseZ);
		}

		int longSlots = Mathf.Max(baseX, baseZ);
		int shortSlots = Mathf.Min(baseX, baseZ);
		Basis snappedGlobalBasis = GetSnappedGlobalBasis(slotParent);
		Vector3 longAxisWorld = baseX >= baseZ
			? snappedGlobalBasis.X.Normalized()
			: snappedGlobalBasis.Z.Normalized();
		Vector3 parentXWorld = parent3D.GlobalTransform.Basis.X.Normalized();
		Vector3 parentZWorld = parent3D.GlobalTransform.Basis.Z.Normalized();

		float alignWithX = Mathf.Abs(longAxisWorld.Dot(parentXWorld));
		float alignWithZ = Mathf.Abs(longAxisWorld.Dot(parentZWorld));
		bool longAlongParentX = alignWithX >= alignWithZ;

		return longAlongParentX
			? new Vector2I(longSlots, shortSlots)
			: new Vector2I(shortSlots, longSlots);
	}

	private Basis GetSnappedGlobalBasis(Node slotParent)
	{
		if (slotParent is not Node3D parent3D)
		{
			return GlobalBasis;
		}

		Basis localBasis = parent3D.GlobalBasis.Inverse() * GlobalBasis;
		Vector3 localEuler = localBasis.GetEuler();
		float quarterTurn = Mathf.Pi * 0.5f;
		float snappedYaw = Mathf.Round(localEuler.Y / quarterTurn) * quarterTurn;
		Basis snappedLocalBasis = Basis.FromEuler(new Vector3(0.0f, snappedYaw, 0.0f));
		return parent3D.GlobalBasis * snappedLocalBasis;
	}

	private CollisionShape3D FindSlotCollisionShape()
	{
		Godot.Collections.Array<Node> allShapes = FindChildren("*", "CollisionShape3D", true, false);
		for (int i = 0; i < allShapes.Count; i++)
		{
			if (allShapes[i] is not CollisionShape3D shape)
			{
				continue;
			}

			if (_triggerVolume != null && _triggerVolume.IsAncestorOf(shape))
			{
				continue;
			}

			if (shape.GetParent() == this && shape.Shape is BoxShape3D)
			{
				return shape;
			}
		}

		return null;
	}

	private Vector2I CalculateSlotFootprint()
	{
		if (_boxCollisionShape?.Shape is not BoxShape3D box)
		{
			return Vector2I.One;
		}

		float xSize = Mathf.Max(1.0f, box.Size.X);
		float zSize = Mathf.Max(1.0f, box.Size.Z);
		int slotsX = CalculateDimensionSlots(xSize, DefaultSlotPitch);
		int slotsZ = CalculateDimensionSlots(zSize, DefaultSlotPitch);
		return new Vector2I(slotsX, slotsZ);
	}

	private static int CalculateDimensionSlots(float size, float slotPitch)
	{
		if (size <= 1.0f)
		{
			return 1;
		}

		float pitch = Mathf.Max(0.001f, slotPitch);
		float extra = Mathf.Max(0.0f, size - 1.0f);
		return Mathf.Max(1, Mathf.RoundToInt(extra / pitch) + 1);
	}

	private float EstimateSlotPitch(Node slotParent)
	{
		if (slotParent == null)
		{
			return DefaultSlotPitch;
		}

		float best = float.MaxValue;
		foreach (Area3D candidate in _slotCandidates)
		{
			if (candidate == null || !GodotObject.IsInstanceValid(candidate) || candidate.GetParent() != slotParent)
			{
				continue;
			}

			if (!TryParseSlotCoordinates(candidate, out SlotCoordinate candidateCoord))
			{
				continue;
			}

			for (int axis = 0; axis < 2; axis++)
			{
				int x = axis == 0 ? candidateCoord.X + 1 : candidateCoord.X;
				int z = axis == 1 ? candidateCoord.Z + 1 : candidateCoord.Z;
				if (!TryGetSlotByCoordinates(slotParent, x, z, out Area3D neighbor))
				{
					continue;
				}

				float distance = candidate.GlobalPosition.DistanceTo(neighbor.GlobalPosition);
				if (distance > 0.001f && distance < best)
				{
					best = distance;
				}
			}
		}

		if (best == float.MaxValue)
		{
			return DefaultSlotPitch;
		}

		return best;
	}

	private bool TryGetSlotByCoordinates(Node slotParent, int x, int z, out Area3D slot)
	{
		slot = null;
		foreach (Area3D candidate in _slotCandidates)
		{
			if (candidate == null || !GodotObject.IsInstanceValid(candidate) || candidate.GetParent() != slotParent)
			{
				continue;
			}

			if (!TryParseSlotCoordinates(candidate, out SlotCoordinate coordinate))
			{
				continue;
			}

			if (coordinate.X == x && coordinate.Z == z)
			{
				slot = candidate;
				return true;
			}
		}

		return false;
	}

	private static bool TryParseSlotCoordinates(Area3D slot, out SlotCoordinate coordinate)
	{
		coordinate = default;
		if (slot == null)
		{
			return false;
		}

		string[] parts = slot.Name.ToString().Split('_');
		if (parts.Length < 3)
		{
			return false;
		}

		if (!int.TryParse(parts[1], out int x) || !int.TryParse(parts[2], out int z))
		{
			return false;
		}

		coordinate = new SlotCoordinate(x, z);
		return true;
	}

	private sealed class SlotPlacementCandidate
	{
		public List<Area3D> Slots { get; }
		public Node Parent { get; }
		public Vector3 TargetGlobalPosition { get; }
		public float DistanceSquared { get; }

		public SlotPlacementCandidate(List<Area3D> slots, Node parent, Vector3 targetGlobalPosition, float distanceSquared)
		{
			Slots = slots;
			Parent = parent;
			TargetGlobalPosition = targetGlobalPosition;
			DistanceSquared = distanceSquared;
		}
	}

	private readonly struct SlotCoordinate
	{
		public int X { get; }
		public int Z { get; }

		public SlotCoordinate(int x, int z)
		{
			X = x;
			Z = z;
		}
	}

	bool IInteractable.Use(Node3D user)
	{
		GD.Print($"{this.Name}: Use() called by '{user.Name}'");
		PickupPawn3D pickupPawn = user.FindChildOfType<PickupPawn3D>();
		if (pickupPawn is null)
		{
			GD.PushWarning($"{this.Name}: Use() called by '{user.Name}', but no PickupPawn3D found in parent-parent chain.");
			return false;
		}

		ReleaseCurrentSlot();
		UnfreezeForPickup();

		// Pick up this object
		pickupPawn.PickUp(this);
		return true;
	}
}
