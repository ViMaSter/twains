using Godot;
using System;
using Twains;

public partial class Switch : Node3D
{
	[Export]
	public Area3D TriggerVolume;

	public override void _Ready()
	{
		if (TriggerVolume is null)
		{
			throw new InvalidOperationException("Switch: TriggerVolume is not assigned. Please assign an Area3D in the Inspector.");
		}

		TriggerVolume.BodyEntered += OnTriggerBodyEntered;
		TriggerVolume.BodyExited += OnTriggerBodyExited;
	}

	public override void _ExitTree()
	{
		if (TriggerVolume is not null)
		{
			TriggerVolume.BodyEntered -= OnTriggerBodyEntered;
			TriggerVolume.BodyExited -= OnTriggerBodyExited;
		}
	}

	private void OnTriggerBodyEntered(Node3D body)
	{
		CharacterBodyPawn3D pawn = body as CharacterBodyPawn3D;
		if (pawn is null)
		{
			throw new InvalidOperationException($"Switch: Could not find CharacterBodyPawn3D in parent-parent chain for entering body '{body.Name}'.");
		}

		GD.Print($"Switch: Something entered trigger volume: {body.Name}");
		pawn.SetInteractable(this);
	}

	private void OnTriggerBodyExited(Node3D body)
	{
		CharacterBodyPawn3D pawn = body as CharacterBodyPawn3D;
		if (pawn is null)
		{
			throw new InvalidOperationException($"Switch: Could not find CharacterBodyPawn3D for exiting body '{body.Name}'.");
		}

		GD.Print($"Switch: Something left trigger volume: {body.Name}");
		pawn.SetInteractable(null);
	}
}
