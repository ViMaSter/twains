extends GutTest

const PICKUP_SCENE_PATH := "res://test/scenes/pickup/pickup.tscn"
const INTERACTABLE_WAIT_TIMEOUT_SEC := 5.0


func test_pickup_box_interact_then_place_reports_disconnect_error_twice():
	var scene_res: PackedScene = load(PICKUP_SCENE_PATH) as PackedScene
	assert_not_null(scene_res, "Pickup scene should load")
	if scene_res == null:
		return

	var scene: Node = add_child_autoqfree(scene_res.instantiate())
	assert_not_null(scene, "Pickup scene should instantiate")
	if scene == null:
		return

	var pawn: Node = scene.get_node_or_null("Player/Pawn")
	var pickup_pawn: Node = scene.get_node_or_null("Player/Pawn/PickupPawn3D")
	var pickup_box: Node = scene.get_node_or_null("PlayArea/PickupBox")

	assert_true(pawn != null, "Scene should contain Player/Pawn")
	assert_true(pickup_pawn != null, "Scene should contain Player/Pawn/PickupPawn3D")
	assert_true(pickup_box != null, "Scene should contain PlayArea/PickupBox")
	if pawn == null or pickup_pawn == null or pickup_box == null:
		return

	var interactable_is_set: bool = await wait_until(func() -> bool:
		return _pawn_interactable_status_has_target(scene)
	, INTERACTABLE_WAIT_TIMEOUT_SEC, "Waiting for pickup box to enter interaction range")
	assert_true(interactable_is_set, "Pickup box should fall into range and set pawn _interactable")
	if !interactable_is_set:
		return

	pawn.call("UseInteractable")
	await wait_process_frames(2)

	assert_true(bool(pickup_pawn.get("HasPickup")), "Using interactable should pick up the pickup box")

	pickup_pawn.call("Place")
	await wait_process_frames(2)

	assert_engine_error_count(2, "Exactly two engine disconnect errors should be raised")


func _pawn_interactable_status_has_target(scene: Node) -> bool:
	var status_label: RichTextLabel = scene.get_node_or_null("InteractableUI") as RichTextLabel
	if status_label == null:
		return false

	return status_label.text.contains("PickupBox")