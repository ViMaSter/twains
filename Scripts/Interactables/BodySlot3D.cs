using Godot;
using System;
using Twains;

public partial class BodySlot3D : Area3D
{
	[Export] public CharacterBodyPawn3D Pawn { get; set; }

	public override void _Ready()
	{
		ValidateConfigurationOrThrow();
	}

	private void ValidateConfigurationOrThrow()
	{
		if (Pawn is null)
		{
			throw new InvalidOperationException($"BodySlot3D '{Name}': Pawn is not assigned. Assign a CharacterBodyPawn3D in the Inspector.");
		}

		Node sceneRoot = GetTree()?.CurrentScene;
		if (sceneRoot is null)
		{
			return;
		}

		Godot.Collections.Array<Node> allNodes = sceneRoot.FindChildren("*", "BodySlot3D", true, false);
		foreach (Node node in allNodes)
		{
			if (node == this)
			{
				continue;
			}

			if (node is not BodySlot3D otherBodySlot)
			{
				continue;
			}

			if (otherBodySlot.Name == Name && otherBodySlot.Pawn == Pawn)
			{
				throw new InvalidOperationException(
					$"BodySlot3D '{Name}': Duplicate assignment detected. Another BodySlot3D with the same name is assigned to Pawn '{Pawn.Name}'.");
			}
		}
	}

	public void ApplyTintToPawn(Color tint)
	{
		if (Pawn is null)
		{
			GD.PushWarning($"BodySlot3D '{Name}': Pawn is not assigned.");
			return;
		}

		MeshInstance3D meshInstance = Pawn.FindChildOfType<MeshInstance3D>();
		if (meshInstance is null)
		{
			GD.PushWarning($"BodySlot3D '{Name}': Assigned Pawn '{Pawn.Name}' has no MeshInstance3D child.");
			return;
		}

		StandardMaterial3D material = GetOrCreateMaterialOverride(meshInstance);
		material.AlbedoColor = tint;
	}

	private static StandardMaterial3D GetOrCreateMaterialOverride(MeshInstance3D meshInstance)
	{
		if (meshInstance.MaterialOverride is StandardMaterial3D overrideMaterial)
		{
			StandardMaterial3D uniqueOverride = overrideMaterial.Duplicate() as StandardMaterial3D ?? overrideMaterial;
			meshInstance.MaterialOverride = uniqueOverride;
			return uniqueOverride;
		}

		if (meshInstance.Mesh != null && meshInstance.Mesh.GetSurfaceCount() > 0)
		{
			Material surfaceMaterial = meshInstance.Mesh.SurfaceGetMaterial(0);
			if (surfaceMaterial is StandardMaterial3D standardSurfaceMaterial)
			{
				StandardMaterial3D uniqueSurface = standardSurfaceMaterial.Duplicate() as StandardMaterial3D ?? standardSurfaceMaterial;
				meshInstance.MaterialOverride = uniqueSurface;
				return uniqueSurface;
			}
		}

		StandardMaterial3D newMaterial = new();
		meshInstance.MaterialOverride = newMaterial;
		return newMaterial;
	}
}