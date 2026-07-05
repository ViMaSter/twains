using Godot;
using System;
using System.Collections.Generic;
using Twains;

public partial class PickupBox3D : RigidBody3D, IInteractable
{
	private const int SlotPhysicsLayer = 5;
	private const string SlotOccupiedMetaKey = "slot_occupied_by";

	private Area3D _triggerVolume;
	private readonly HashSet<Area3D> _slotCandidates = new();
	private Area3D _currentSlot;

	[Export] public float SlotSnapDistanceThreshold { get; set; } = 0.75f;

	public override void _EnterTree()
	{
		EnsureTriggerSignalsConnected();
	}


	public override void _Ready()
	{
		_triggerVolume = this.FindChildOfType<Area3D>();
		
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
		float snapDistance = Mathf.Max(0.0f, SlotSnapDistanceThreshold);
		float maxDistanceSquared = snapDistance * snapDistance;

		if (_currentSlot != null && GodotObject.IsInstanceValid(_currentSlot))
		{
			float currentSlotDistanceSquared = GlobalPosition.DistanceSquaredTo(_currentSlot.GlobalPosition);
			if (currentSlotDistanceSquared > maxDistanceSquared)
			{
				ReleaseCurrentSlot();
			}
		}

		if (_slotCandidates.Count == 0)
		{
			return;
		}

		Area3D bestSlot = null;
		float bestDistanceSquared = float.MaxValue;

		foreach (var slot in _slotCandidates)
		{
			if (!GodotObject.IsInstanceValid(slot))
			{
				continue;
			}

			if (!IsSlotAvailable(slot))
			{
				continue;
			}

			float distanceSquared = GlobalPosition.DistanceSquaredTo(slot.GlobalPosition);
			if (distanceSquared > maxDistanceSquared || distanceSquared >= bestDistanceSquared)
			{
				continue;
			}

			bestDistanceSquared = distanceSquared;
			bestSlot = slot;
		}

		if (bestSlot != null)
		{
			SnapIntoSlot(bestSlot);
		}
	}

	private bool IsSlotAvailable(Area3D slot)
	{
		if (slot == _currentSlot)
		{
			return false;
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

	private void SnapIntoSlot(Area3D slot)
	{
		ReleaseCurrentSlot();
		FreezeForSlot();

		Node originalParent = GetParent();
		Reparent(slot, false);
		Position = Vector3.Zero;
		Rotation = Vector3.Zero;

		if (originalParent != null && originalParent != slot)
		{
			Reparent(originalParent, true);
		}

		_currentSlot = slot;
		slot.SetMeta(SlotOccupiedMetaKey, this);
		SetSlotVisibility(slot, false);
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
		Freeze = true;
	}

	private void UnfreezeForPickup()
	{
		Freeze = false;
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
	}

	private void ReleaseCurrentSlot()
	{
		if (_currentSlot == null || !GodotObject.IsInstanceValid(_currentSlot))
		{
			_currentSlot = null;
			return;
		}

		if (_currentSlot.HasMeta(SlotOccupiedMetaKey))
		{
			PickupBox3D occupiedBy = _currentSlot.GetMeta(SlotOccupiedMetaKey).AsGodotObject() as PickupBox3D;
			if (occupiedBy == this)
			{
				_currentSlot.RemoveMeta(SlotOccupiedMetaKey);
				SetSlotVisibility(_currentSlot, true);
			}
		}

		_currentSlot = null;
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
