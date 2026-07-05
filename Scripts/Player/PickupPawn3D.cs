using Godot;
using System;
using Twains;

public partial class PickupPawn3D : Node3D
{
	private Node3D _currentPickup;
	private Node3D _pickupOriginalParent;

	[Export]
	public float ThrowIntensity = 10.0f;

	public bool HasPickup => _currentPickup is not null;

	public override void _Ready()
	{
	}

	public override void _Process(double delta)
	{
	}

	/// <summary>
	/// Pick up an object and make it a child of this pawn.
	/// </summary>
	public void PickUp(Node3D pickupObject)
	{
		if (pickupObject is null)
			return;

		// If already holding something, place it first
		if (_currentPickup is not null)
		{
			Place();
		}

		RigidBody3D rigidBody = pickupObject as RigidBody3D;

		// Store the original parent before reparenting
		_pickupOriginalParent = pickupObject.GetParent() as Node3D;

		// Make it a child of this pawn
		pickupObject.Reparent(this);

		// Find and disable physics
		rigidBody.Freeze = true;
		rigidBody.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;

		// Reset rotation and local position
		pickupObject.RotationDegrees = Vector3.Zero;
		pickupObject.Position = Vector3.Zero;

		_currentPickup = pickupObject;
	}

	/// <summary>
	/// Place (drop) the currently held object.
	/// </summary>
	public void Place()
	{
		if (_currentPickup is null)
			return;

		// Re-enable physics
		RigidBody3D rigidBody = _currentPickup as RigidBody3D;
		if (rigidBody is not null)
		{
			rigidBody.Freeze = false;
		}

		// Return to original parent
		if (_pickupOriginalParent is not null)
		{
			_currentPickup.Reparent(_pickupOriginalParent);
		}

		_currentPickup = null;
		_pickupOriginalParent = null;
	}

	/// <summary>
	/// Throw the currently held object with the given intensity.
	/// </summary>
	public void Throw(float intensity)
	{
		if (_currentPickup is null)
			return;

		// Re-enable physics
		RigidBody3D rigidBody = _currentPickup as RigidBody3D;
		if (rigidBody is not null)
		{
			rigidBody.Freeze = false;

			// Calculate throw direction: up and forward relative to this pawn
			Vector3 forward = GlobalTransform.Basis.Z; // Forward direction
			Vector3 throwDirection = (forward + Vector3.Up).Normalized();

			// Apply velocity
			rigidBody.LinearVelocity = throwDirection * intensity * ThrowIntensity;
		}

		// Return to original parent
		if (_pickupOriginalParent is not null)
		{
			_currentPickup.Reparent(_pickupOriginalParent);
		}

		_currentPickup = null;
		_pickupOriginalParent = null;
	}
}
