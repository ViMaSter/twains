using Godot;
using System;
using Twains;

public partial class PickupBox3D : Node3D, IInteractable
{
	private Area3D _triggerVolume;


	public override void _Ready()
	{
		_triggerVolume = this.FindChildOfType<Area3D>();
		
		if (_triggerVolume is null)
		{
			throw new InvalidOperationException($"PickupBox3D '{Name}': No Area3D child found. Please add an Area3D as a child node.");
		}

		_triggerVolume.BodyEntered += OnTriggerBodyEntered;
		_triggerVolume.BodyExited += OnTriggerBodyExited;
	}

	public override void _ExitTree()
	{
		if (_triggerVolume is not null)
		{
			_triggerVolume.BodyEntered -= OnTriggerBodyEntered;
			_triggerVolume.BodyExited -= OnTriggerBodyExited;
		}
	}

	private void OnTriggerBodyEntered(Node3D body)
	{
		CharacterBodyPawn3D pawn = body as CharacterBodyPawn3D;
		if (pawn is null)
		{
			throw new InvalidOperationException($"PickupBox3D: Could not find CharacterBodyPawn3D in parent-parent chain for entering body '{body.Name}'.");
		}

		GD.Print($"PickupBox3D: Something entered trigger volume: {body.Name}");
		pawn.SetInteractable(this);
	}

	private void OnTriggerBodyExited(Node3D body)
	{
		CharacterBodyPawn3D pawn = body as CharacterBodyPawn3D;
		if (pawn is null)
		{
			throw new InvalidOperationException($"PickupBox3D: Could not find CharacterBodyPawn3D for exiting body '{body.Name}'.");
		}

		GD.Print($"PickupBox3D: Something left trigger volume: {body.Name}");
		pawn.ClearInteractable();
	}

	bool IInteractable.Use(Node3D user)
	{
		GD.Print($"PickupBox3D: Use() called on '{Name}' by '{user.Name}'");
		PickupPawn3D pickupPawn = user.FindChildOfType<PickupPawn3D>();
		if (pickupPawn is null)
		{
			GD.PushWarning($"PickupBox3D '{Name}': Use() called by '{user.Name}', but no PickupPawn3D found in parent-parent chain.");
			return false;
		}

		// Pick up this object
		pickupPawn.PickUp(this);
		return true;
	}

	string IInteractable.Name
	{
		get { return Name; }
	}
}
