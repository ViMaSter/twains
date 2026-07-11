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

		Node3D releasedPickup = ReleaseCurrentPickup(Vector3.Zero, Vector3.Zero);
		if (releasedPickup is IInteractable interactable && GetParent() is CharacterBodyPawn3D pawn)
		{
			// Re-register after place so repeated pickup/place cycles remain interactable.
			pawn.SetInteractable(interactable);
		}
	}

	/// <summary>
	/// Throw the currently held object with the given intensity.
	/// </summary>
	public void Throw(float intensity)
	{
		if (_currentPickup is null)
			return;

		Vector3 forward = GetParent() is CharacterBodyPawn3D pawn
			? -pawn.GetFacingDirection()
			: new Vector3(-GlobalTransform.Basis.Z.X, 0.0f, -GlobalTransform.Basis.Z.Z).Normalized();
		Vector3 throwDirection = (forward + Vector3.Up).Normalized();
		Vector3 linearVelocity = throwDirection * (0.5f + intensity) * ThrowIntensity;
		Vector3 randomAngularVelocity = new Vector3(
			(float)GD.RandRange(-1.0, 1.0),
			(float)GD.RandRange(-1.0, 1.0),
			(float)GD.RandRange(-1.0, 1.0)
		).Normalized() * (float)GD.RandRange(0.1, 0.3);

		ReleaseCurrentPickup(linearVelocity, randomAngularVelocity);
	}

	private Node3D ReleaseCurrentPickup(Vector3 linearVelocity, Vector3 angularVelocity)
	{
		Node3D pickupToRelease = _currentPickup;
		Node3D originalParent = _pickupOriginalParent;

		if (pickupToRelease is null)
		{
			return null;
		}

		if (originalParent is not null)
		{
			pickupToRelease.Reparent(originalParent);
		}

		if (pickupToRelease is RigidBody3D rigidBody)
		{
			rigidBody.Freeze = false;
			rigidBody.Sleeping = false;
			rigidBody.LinearVelocity = linearVelocity;
			rigidBody.AngularVelocity = angularVelocity;
		}

		_currentPickup = null;
		_pickupOriginalParent = null;
		return pickupToRelease;
	}
}
