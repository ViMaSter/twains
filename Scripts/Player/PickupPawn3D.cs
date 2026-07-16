using Godot;
using System;
using Twains;

public partial class PickupPawn3D : Node3D
{
	private Node3D _currentPickup;
	private Node3D _pickupOriginalParent;

	// Carry spring state
	private Vector3 _carryPosCurrent;
	private Vector3 _carryPosVelocity;
	private Quaternion _carryRotCurrent;
	private Vector3 _carryAngularVelocity;
	private Quaternion _prevParentGlobalRot;

	[Export]
	public float ThrowIntensity = 10.0f;

	[Export] public float CarryPositionSpringStrength { get; set; } = 150.0f;
	[Export] public float CarryPositionSpringDamping  { get; set; } = 18.0f;
	[Export] public float CarryRotationSpringStrength { get; set; } = 40.0f;
	[Export] public float CarryRotationSpringDamping  { get; set; } = 6.0f;
	/// <summary>How strongly the item sways opposite to the pawn's rotation (0 = no sway).</summary>
	[Export] public float CarryRotationSwayFactor     { get; set; } = 0.5f;

	public bool HasPickup => _currentPickup is not null;

	public override void _Ready()
	{
	}

	public override void _Process(double delta)
	{
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_currentPickup is null)
			return;
		UpdateCarrySpring((float)delta);
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

		// Disable collisions so the carried item doesn't interfere with player movement
		SetBodyCollisionShapesDisabled(rigidBody, true);

		// Initialise spring state from the item's current local transform (post-reparent).
		// The spring will smoothly drive position → zero and rotation → identity.
		_carryPosCurrent       = pickupObject.Position;
		_carryPosVelocity      = Vector3.Zero;
		_carryRotCurrent       = NormalizeQuat(pickupObject.Quaternion);
		_carryAngularVelocity  = Vector3.Zero;
		_prevParentGlobalRot   = GetParentGlobalRotation();

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

		// Use the pawn's actual (smoothed) visual rotation, not the requested target direction.
		Node3D parentNode = GetParent() as Node3D;
		Vector3 forward = parentNode is not null
			? new Vector3(parentNode.GlobalTransform.Basis.Z.X, 0.0f, parentNode.GlobalTransform.Basis.Z.Z).Normalized()
			: new Vector3(GlobalTransform.Basis.Z.X, 0.0f, GlobalTransform.Basis.Z.Z).Normalized();
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
			// Re-enable collisions before releasing back into the world
			SetBodyCollisionShapesDisabled(rigidBody, false);

			rigidBody.Freeze = false;
			rigidBody.Sleeping = false;
			rigidBody.LinearVelocity = linearVelocity;
			rigidBody.AngularVelocity = angularVelocity;
		}

		_currentPickup = null;
		_pickupOriginalParent = null;
		return pickupToRelease;
	}

	// -------------------------------------------------------------------------
	// Carry spring
	// -------------------------------------------------------------------------

	private void UpdateCarrySpring(float dt)
	{
		// --- Rotation sway: item lags opposite to the pawn's yaw ---
		Quaternion parentRot = GetParentGlobalRotation();
		Quaternion parentDelta = parentRot * _prevParentGlobalRot.Inverse();
		_prevParentGlobalRot = parentRot;

		// Convert quaternion delta to axis-angle impulse
		float w = Mathf.Clamp(parentDelta.W, -1.0f, 1.0f);
		float halfAngle = Mathf.Acos(w);
		float sinHalf = Mathf.Sin(halfAngle);
		float deltaAngle = 2.0f * halfAngle;
		// Wrap to [-PI, PI] for shortest path
		if (deltaAngle > Mathf.Pi) deltaAngle -= Mathf.Tau;

		if (Mathf.Abs(deltaAngle) > 0.0001f && sinHalf > 0.0001f)
		{
			Vector3 deltaAxis = new Vector3(parentDelta.X, parentDelta.Y, parentDelta.Z) / sinHalf;
			// Apply as an opposite velocity impulse so the item appears to lag
			_carryAngularVelocity -= deltaAxis * (deltaAngle * CarryRotationSwayFactor);
		}

		// --- Rotation spring: pull toward Quaternion.Identity ---
		// Extract current deviation as axis * angle
		float cw = Mathf.Clamp(_carryRotCurrent.W, -1.0f, 1.0f);
		float cHalf = Mathf.Acos(cw);
		float cSinHalf = Mathf.Sin(cHalf);
		float cAngle = 2.0f * cHalf;
		if (cAngle > Mathf.Pi) cAngle -= Mathf.Tau;

		Vector3 angleVec = (Mathf.Abs(cAngle) > 0.0001f && cSinHalf > 0.0001f)
			? new Vector3(_carryRotCurrent.X, _carryRotCurrent.Y, _carryRotCurrent.Z) / cSinHalf * cAngle
			: Vector3.Zero;

		// Spring-damper: a = -k*x - c*v
		_carryAngularVelocity += (-CarryRotationSpringStrength * angleVec
		                         - CarryRotationSpringDamping * _carryAngularVelocity) * dt;

		// Integrate rotation
		Vector3 rotStep = _carryAngularVelocity * dt;
		float rotStepLen = rotStep.Length();
		if (rotStepLen > 0.0001f)
		{
			_carryRotCurrent = (new Quaternion(rotStep / rotStepLen, rotStepLen) * _carryRotCurrent).Normalized();
		}
		_currentPickup.Quaternion = _carryRotCurrent;

		// --- Position spring: pull toward Vector3.Zero ---
		_carryPosVelocity += (-CarryPositionSpringStrength * _carryPosCurrent
		                      - CarryPositionSpringDamping * _carryPosVelocity) * dt;
		_carryPosCurrent += _carryPosVelocity * dt;
		_currentPickup.Position = _carryPosCurrent;
	}

	private Quaternion GetParentGlobalRotation()
	{
		if (GetParent() is Node3D p)
			return NormalizeQuat(p.GlobalTransform.Basis.GetRotationQuaternion());
		return Quaternion.Identity;
	}

	private static Quaternion NormalizeQuat(Quaternion q)
	{
		float lenSq = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
		if (lenSq < 0.0001f) return Quaternion.Identity;
		float inv = 1.0f / Mathf.Sqrt(lenSq);
		return new Quaternion(q.X * inv, q.Y * inv, q.Z * inv, q.W * inv);
	}

	// -------------------------------------------------------------------------

	/// <summary>
	/// Disables or re-enables all CollisionShape3D nodes that are direct children of the given
	/// body, skipping shapes inside Area3D subtrees (e.g. trigger volumes).
	/// </summary>
	private static void SetBodyCollisionShapesDisabled(PhysicsBody3D body, bool disabled)
	{
		foreach (Node child in body.GetChildren())
		{
			if (child is Area3D)
				continue;

			if (child is CollisionShape3D shape)
				shape.Disabled = disabled;
		}
	}
}
