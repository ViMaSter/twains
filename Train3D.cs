using Godot;
using System;
using System.Collections.Generic;

public partial class Train3D : Node3D
{
	private const float SearchStartRadius = 1.0f;
	private const float SearchStep = 2.0f;
	private const float SearchMaxRadius = 300.0f;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		SnapToNearestRailTopCenter();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void SnapToNearestRailTopCenter()
	{
		RailRoad3D nearestRail = FindNearestRailRoadBySphereQuery();
		if (nearestRail == null)
		{
			GD.PushWarning("Train3D: No RailRoad3D found in search radius.");
			return;
		}

		CollisionShape3D collisionShape = FindFirstCollisionShape(nearestRail);
		if (collisionShape == null)
		{
			GD.PushWarning($"Train3D: Rail '{nearestRail.Name}' has no CollisionShape3D child.");
			return;
		}

		if (collisionShape.Shape is not BoxShape3D box)
		{
			GD.PushWarning($"Train3D: Rail '{nearestRail.Name}' CollisionShape3D is not a BoxShape3D.");
			return;
		}

		Vector3 topCenterLocal = new Vector3(0.0f, box.Size.Y * 0.5f, 0.0f);
		Vector3 topCenterWorld = collisionShape.ToGlobal(topCenterLocal);
		GlobalPosition = topCenterWorld;
	}

	private RailRoad3D FindNearestRailRoadBySphereQuery()
	{
		World3D world = GetWorld3D();
		PhysicsDirectSpaceState3D spaceState = world.DirectSpaceState;
		Vector3 center = GlobalPosition;

		SphereShape3D sphere = new SphereShape3D();
		PhysicsShapeQueryParameters3D query = new PhysicsShapeQueryParameters3D
		{
			Shape = sphere,
			Transform = Transform3D.Identity,
			CollideWithAreas = false,
			CollideWithBodies = true,
			CollisionMask = uint.MaxValue
		};

		for (float radius = SearchStartRadius; radius <= SearchMaxRadius; radius += SearchStep)
		{
			sphere.Radius = radius;
			query.Transform = new Transform3D(Basis.Identity, center);

			Godot.Collections.Array<Godot.Collections.Dictionary> hits = spaceState.IntersectShape(query, 256);
			if (hits.Count == 0)
			{
				continue;
			}

			HashSet<RailRoad3D> rails = new HashSet<RailRoad3D>();
			foreach (Godot.Collections.Dictionary hit in hits)
			{
				if (!hit.TryGetValue("collider", out Variant colliderVariant))
				{
					continue;
				}

				Node colliderNode = colliderVariant.AsGodotObject() as Node;
				RailRoad3D rail = FindRailRoadAncestor(colliderNode);
				if (rail != null)
				{
					rails.Add(rail);
				}
			}

			if (rails.Count == 0)
			{
				continue;
			}

			RailRoad3D closestRail = null;
			float closestDistanceSq = float.MaxValue;
			foreach (RailRoad3D rail in rails)
			{
				float distSq = center.DistanceSquaredTo(rail.GlobalPosition);
				if (distSq < closestDistanceSq)
				{
					closestDistanceSq = distSq;
					closestRail = rail;
				}
			}

			if (closestRail != null)
			{
				return closestRail;
			}
		}

		return null;
	}

	private static RailRoad3D FindRailRoadAncestor(Node node)
	{
		Node current = node;
		while (current != null)
		{
			if (current is RailRoad3D rail)
			{
				return rail;
			}

			current = current.GetParent();
		}

		return null;
	}

	private static CollisionShape3D FindFirstCollisionShape(Node node)
	{
		if (node is CollisionShape3D ownCollision)
		{
			return ownCollision;
		}

		foreach (Node child in node.GetChildren())
		{
			CollisionShape3D found = FindFirstCollisionShape(child);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}
}
