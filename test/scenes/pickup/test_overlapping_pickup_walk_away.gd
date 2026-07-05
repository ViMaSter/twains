extends GutTest

const OVERLAPPING_SCENE_PATH := "res://test/scenes/pickup/overlapping_pickup.tscn"
const INTERACTABLE_WAIT_TIMEOUT_SEC := 5.0
const WALK_AWAY_TIMEOUT_SEC := 5.0


func test_walking_out_of_pickup_box2_uses_pickup_box1_as_fallback_interactable():
	var scene_res: PackedScene = load(OVERLAPPING_SCENE_PATH) as PackedScene
	assert_not_null(scene_res, "Overlapping pickup scene should load")
	if scene_res == null:
		return

	var scene: Node = add_child_autoqfree(scene_res.instantiate())
	assert_not_null(scene, "Overlapping pickup scene should instantiate")
	if scene == null:
		return

	var pawn: Node = scene.get_node_or_null("Player/Pawn")
	var pickup_pawn: Node = scene.get_node_or_null("Player/Pawn/PickupPawn3D")
	var pickup_box2_interaction_volume: Area3D = scene.get_node_or_null("PlayArea/PickupBox2/InteractionVolume") as Area3D

	assert_true(pawn != null, "Scene should contain Player/Pawn")
	assert_true(pickup_pawn != null, "Scene should contain Player/Pawn/PickupPawn3D")
	assert_true(pickup_box2_interaction_volume != null, "Scene should contain PlayArea/PickupBox2/InteractionVolume")
	if pawn == null or pickup_pawn == null or pickup_box2_interaction_volume == null:
		return

	# Wait until PickupBox2 is the active interactable.
	# PickupBox1 will enter range first; we wait until PickupBox2 takes over.
	var pickup_box2_is_interactable: bool = await wait_until(func() -> bool:
		return _pawn_interactable_status_is(scene, "PickupBox2")
	, INTERACTABLE_WAIT_TIMEOUT_SEC, "Waiting for PickupBox2 to become the interactable")
	assert_true(pickup_box2_is_interactable, "PickupBox2 should eventually become the interactable")
	if !pickup_box2_is_interactable:
		return

	# Simulate leaving only PickupBox2 while still inside PickupBox1.
	pickup_box2_interaction_volume.body_exited.emit(pawn)
	await wait_physics_frames(2)

	var pickup_box1_is_interactable: bool = await wait_until(func() -> bool:
		return _pawn_interactable_status_is(scene, "PickupBox") and !_pawn_interactable_status_is(scene, "PickupBox2")
	, WALK_AWAY_TIMEOUT_SEC, "Waiting for PickupBox1 to become interactable after leaving PickupBox2")

	assert_true(pickup_box1_is_interactable, "PickupBox1 should become interactable after leaving PickupBox2")
	if !pickup_box1_is_interactable:
		return

	# Attempt to interact with fallback interactable still in range.
	pawn.call("UseInteractable")
	await wait_process_frames(2)

	assert_true(bool(pickup_pawn.get("HasPickup")),
		"Interacting after leaving PickupBox2 should pick up from fallback PickupBox1")


func _pawn_interactable_status_is(scene: Node, pickup_name: String) -> bool:
	var status_label: RichTextLabel = scene.get_node_or_null("Player/InteractableUI") as RichTextLabel
	if status_label == null:
		return false

	return status_label.text.contains(pickup_name)


func _pawn_interactable_status_is_empty(scene: Node) -> bool:
	var status_label: RichTextLabel = scene.get_node_or_null("Player/InteractableUI") as RichTextLabel
	if status_label == null:
		return false

	return status_label.text.contains("empty")
