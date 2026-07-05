using Godot;
using System;
using Twains;

public partial class PickupPawn3D : Node3D
{
	private Node3D _currentPickup;

	[Export]
	public float ThrowIntensity = 10.0f;

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

		// Find and disable physics
		RigidBody3D rigidBody = pickupObject.FindChildOfType<RigidBody3D>();
		if (rigidBody is not null)
		{
			rigidBody.Freeze = true;
			rigidBody.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
		}

		// Make it a child of this pawn
		pickupObject.Reparent(this);

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
		RigidBody3D rigidBody = _currentPickup.FindChildOfType<RigidBody3D>();
		if (rigidBody is not null)
		{
			rigidBody.Freeze = false;
		}

		// Remove from parent (detach from this pawn)
		_currentPickup.Reparent(GetParent());

		_currentPickup = null;
	}

	/// <summary>
	/// Throw the currently held object with the given intensity.
	/// </summary>
	public void Throw(float intensity)
	{
		if (_currentPickup is null)
			return;

		// Re-enable physics
		RigidBody3D rigidBody = _currentPickup.FindChildOfType<RigidBody3D>();
		if (rigidBody is not null)
		{
			rigidBody.Freeze = false;

			// Calculate throw direction: up and forward relative to this pawn
			Vector3 forward = -GlobalTransform.Basis.Z; // Forward direction
			Vector3 throwDirection = (forward + Vector3.Up).Normalized();

			// Apply velocity
			rigidBody.LinearVelocity = throwDirection * intensity * ThrowIntensity;
		}

		// Remove from parent
		_currentPickup.Reparent(GetParent());

		_currentPickup = null;
	}
}
