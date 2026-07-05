using Godot;
using System;
using Twains;

public partial class PickupBox3D : RigidBody3D, IInteractable
{
	private Area3D _triggerVolume;

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

			if (_triggerVolume.IsConnected(Area3D.SignalName.BodyEntered, bodyEnteredCallable))
			{
				_triggerVolume.Disconnect(Area3D.SignalName.BodyEntered, bodyEnteredCallable);
			}

			if (_triggerVolume.IsConnected(Area3D.SignalName.BodyExited, bodyExitedCallable))
			{
				_triggerVolume.Disconnect(Area3D.SignalName.BodyExited, bodyExitedCallable);
			}
		}
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

		if (!_triggerVolume.IsConnected(Area3D.SignalName.BodyEntered, bodyEnteredCallable))
		{
			_triggerVolume.Connect(Area3D.SignalName.BodyEntered, bodyEnteredCallable);
		}

		if (!_triggerVolume.IsConnected(Area3D.SignalName.BodyExited, bodyExitedCallable))
		{
			_triggerVolume.Connect(Area3D.SignalName.BodyExited, bodyExitedCallable);
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
		pawn.ClearInteractable();
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

		// Pick up this object
		pickupPawn.PickUp(this);
		return true;
	}
}
