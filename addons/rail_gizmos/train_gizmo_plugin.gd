@tool
extends EditorNode3DGizmoPlugin

const ARROW_LENGTH := 4.0
const ARROW_HEAD_LENGTH := 1.0
const ARROW_HEAD_WIDTH := 0.5

func _init() -> void:
	create_material("train_arrow", Color(0.2, 0.85, 1.0), false, true, false)
	create_material("rail_current", Color(1.0, 0.9, 0.1), false, true, false)
	create_material("rail_next", Color(1.0, 0.2, 0.2), false, true, false)

func _get_gizmo_name() -> String:
	return "Train3DPreview"

func _has_gizmo(for_node: Node3D) -> bool:
	if for_node == null:
		return false

	if for_node is Train3D:
		return true

	var script := for_node.get_script()
	return script != null and script.resource_path == "res://Scripts/Train3D.cs"

func _redraw(gizmo: EditorNode3DGizmo) -> void:
	gizmo.clear()

	var train := gizmo.get_node_3d()
	if train == null:
		return

	if train.has_method("RefreshEditorPreviewState"):
		train.call("RefreshEditorPreviewState")

	_draw_train_arrow(gizmo, train)
	_draw_current_and_next_rails(gizmo, train)

func _draw_train_arrow(gizmo: EditorNode3DGizmo, train: Node3D) -> void:
	var forward: Vector3 = -train.global_transform.basis.z
	forward.y = 0.0
	if forward.length_squared() < 0.0001:
		forward = Vector3.FORWARD

	forward = forward.normalized()
	var start_world := train.global_position
	var tip_world := start_world + forward * ARROW_LENGTH

	var right := forward.cross(Vector3.UP)
	if right.length_squared() < 0.0001:
		right = Vector3.RIGHT
	right = right.normalized()

	var head_base := tip_world - forward * ARROW_HEAD_LENGTH
	var left_head := head_base + right * ARROW_HEAD_WIDTH
	var right_head := head_base - right * ARROW_HEAD_WIDTH

	var points := PackedVector3Array([
		train.to_local(start_world),
		train.to_local(tip_world),
		train.to_local(tip_world),
		train.to_local(left_head),
		train.to_local(tip_world),
		train.to_local(right_head)
	])

	gizmo.add_lines(points, get_material("train_arrow", gizmo), false)

func _draw_current_and_next_rails(gizmo: EditorNode3DGizmo, train: Node3D) -> void:
	var current_rail = train.get("EditorPreviewCurrentRail")
	if current_rail != null and current_rail is Node3D:
		_draw_rail_wire(gizmo, train, current_rail, "rail_current")

	var next_rail = train.get("EditorPreviewNextRail")
	if next_rail != null and next_rail is Node3D:
		_draw_rail_wire(gizmo, train, next_rail, "rail_next")

func _draw_rail_wire(gizmo: EditorNode3DGizmo, train: Node3D, rail: Node3D, material_name: String) -> void:
	var collision_shape := _find_first_collision_shape(rail)
	if collision_shape == null:
		return

	if not (collision_shape.shape is BoxShape3D):
		return

	var box: BoxShape3D = collision_shape.shape
	var h := box.size * 0.5

	var local_corners := [
		Vector3(-h.x, -h.y, -h.z),
		Vector3(h.x, -h.y, -h.z),
		Vector3(h.x, -h.y, h.z),
		Vector3(-h.x, -h.y, h.z),
		Vector3(-h.x, h.y, -h.z),
		Vector3(h.x, h.y, -h.z),
		Vector3(h.x, h.y, h.z),
		Vector3(-h.x, h.y, h.z)
	]

	var world_corners: Array[Vector3] = []
	for corner in local_corners:
		world_corners.append(collision_shape.to_global(corner))

	var edges := [
		Vector2i(0, 1), Vector2i(1, 2), Vector2i(2, 3), Vector2i(3, 0),
		Vector2i(4, 5), Vector2i(5, 6), Vector2i(6, 7), Vector2i(7, 4),
		Vector2i(0, 4), Vector2i(1, 5), Vector2i(2, 6), Vector2i(3, 7)
	]

	var points := PackedVector3Array()
	for edge in edges:
		points.append(train.to_local(world_corners[edge.x]))
		points.append(train.to_local(world_corners[edge.y]))

	gizmo.add_lines(points, get_material(material_name, gizmo), false)

func _find_first_collision_shape(node: Node) -> CollisionShape3D:
	if node is CollisionShape3D:
		return node

	for child in node.get_children():
		var found := _find_first_collision_shape(child)
		if found != null:
			return found

	return null
