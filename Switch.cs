using Godot;
using System;
using Twains;

public partial class Switch : Node3D
{
	[Export]
	public Area3D TriggerVolume;

	[Export]
	public Node Target;

	[Export]
	public string TargetMethodName;

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
		pawn.ClearInteractable();
	}

	public void Press()
	{
		if (Target is null)
		{
			GD.PushWarning($"Switch '{Name}': Press() called, but no Target is configured.");
			return;
		}

		if (string.IsNullOrWhiteSpace(TargetMethodName))
		{
			GD.PushWarning($"Switch '{Name}': Press() called, but TargetMethodName is empty.");
			return;
		}

		StringName method = new StringName(TargetMethodName);
		if (!Target.HasMethod(method))
		{
			GD.PushWarning($"Switch '{Name}': Target '{Target.Name}' does not have method '{TargetMethodName}'.");
			return;
		}

		try
		{
			Target.Call(method);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"Switch '{Name}': Failed to call '{TargetMethodName}' on '{Target.Name}': {ex.Message}");
		}
	}
}
