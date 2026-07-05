@tool
extends EditorNode3DGizmoPlugin

const SCRIPT_PATH := "res://Levels/SlotZone3D.cs"

func _init() -> void:
	create_material("slot_wire", Color(0.2, 0.85, 1.0), false, true, false)

func _get_gizmo_name() -> String:
	return "SlotZone3DPreview"

func _has_gizmo(for_node: Node3D) -> bool:
	if for_node == null:
		return false

	var script := for_node.get_script()
	if script != null and script.resource_path == SCRIPT_PATH:
		return true

	return for_node.has_method("get") and for_node.get("Slots") != null and for_node.get("Margin") != null

func _redraw(gizmo: EditorNode3DGizmo) -> void:
	gizmo.clear()

	var slot_zone := gizmo.get_node_3d()
	if slot_zone == null:
		return

	var slots: Vector2i = slot_zone.get("Slots")
	var margin: float = max(0.0, float(slot_zone.get("Margin")))

	var cols := max(1, slots.x)
	var rows := max(1, slots.y)
	var spacing := 1.0 + (margin * 2.0)

	var start_x := -((float(cols) - 1.0) * spacing) * 0.5
	var start_z := -((float(rows) - 1.0) * spacing) * 0.5

	var half := Vector3(0.5, 0.5, 0.5)
	var edges := [
		Vector2i(0, 1), Vector2i(1, 2), Vector2i(2, 3), Vector2i(3, 0),
		Vector2i(4, 5), Vector2i(5, 6), Vector2i(6, 7), Vector2i(7, 4),
		Vector2i(0, 4), Vector2i(1, 5), Vector2i(2, 6), Vector2i(3, 7)
	]

	var points := PackedVector3Array()
	for z in rows:
		for x in cols:
			var center_local := Vector3(start_x + (float(x) * spacing), 0.0, start_z + (float(z) * spacing))
			var corners := [
				center_local + Vector3(-half.x, -half.y, -half.z),
				center_local + Vector3(half.x, -half.y, -half.z),
				center_local + Vector3(half.x, -half.y, half.z),
				center_local + Vector3(-half.x, -half.y, half.z),
				center_local + Vector3(-half.x, half.y, -half.z),
				center_local + Vector3(half.x, half.y, -half.z),
				center_local + Vector3(half.x, half.y, half.z),
				center_local + Vector3(-half.x, half.y, half.z)
			]

			for edge in edges:
				points.append(corners[edge.x])
				points.append(corners[edge.y])

	if points.size() > 0:
		gizmo.add_lines(points, get_material("slot_wire", gizmo), false)
