@tool
extends EditorPlugin

var _train_gizmo_plugin: EditorNode3DGizmoPlugin

func _enter_tree() -> void:
	var gizmo_script := load("res://addons/rail_gizmos/train_gizmo_plugin.gd") as Script
	if gizmo_script == null:
		push_error("Rail Gizmos: Failed to load train gizmo plugin script.")
		return

	var instance: EditorNode3DGizmoPlugin = gizmo_script.new() as EditorNode3DGizmoPlugin
	if instance == null:
		push_error("Rail Gizmos: Loaded script is not an EditorNode3DGizmoPlugin.")
		return

	_train_gizmo_plugin = instance
	add_node_3d_gizmo_plugin(_train_gizmo_plugin)

func _exit_tree() -> void:
	if _train_gizmo_plugin != null:
		remove_node_3d_gizmo_plugin(_train_gizmo_plugin)
		_train_gizmo_plugin = null
