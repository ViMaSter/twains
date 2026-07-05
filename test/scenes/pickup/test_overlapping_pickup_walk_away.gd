extends GutTest

const OVERLAPPING_SCENE_PATH := "res://test/scenes/pickup/overlapping_pickup.tscn"
const INTERACTABLE_WAIT_TIMEOUT_SEC := 5.0
const WALK_AWAY_TIMEOUT_SEC := 5.0


func test_walking_out_of_pickup_box2_clears_interactable_so_pickup1_is_not_usable():
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

	assert_true(pawn != null, "Scene should contain Player/Pawn")
	assert_true(pickup_pawn != null, "Scene should contain Player/Pawn/PickupPawn3D")
	if pawn == null or pickup_pawn == null:
		return

	var input_sender := GutInputSender.new(Input)

	# Wait until PickupBox2 is the active interactable.
	# PickupBox1 will enter range first; we wait until PickupBox2 takes over.
	var pickup_box2_is_interactable: bool = await wait_until(func() -> bool:
		return _pawn_interactable_status_is(scene, "PickupBox2")
	, INTERACTABLE_WAIT_TIMEOUT_SEC, "Waiting for PickupBox2 to become the interactable")
	assert_true(pickup_box2_is_interactable, "PickupBox2 should eventually become the interactable")
	if !pickup_box2_is_interactable:
		return

	# Emulate move_back action through GUT input sender.
	input_sender.action_down("move_back").wait_frames(10)
	await wait_physics_frames(1)

	var interactable_cleared: bool = await wait_until(func() -> bool:
		return _pawn_interactable_status_is_empty(scene)
	, WALK_AWAY_TIMEOUT_SEC, "Waiting for interactable to clear after walking away from PickupBox2")

	input_sender.action_up("move_back")
	input_sender.release_all()
	await wait_physics_frames(2)

	assert_true(interactable_cleared, "Interactable should be cleared after walking out of PickupBox2's trigger")
	if !interactable_cleared:
		return

	# Attempt to interact with no interactable set.
	pawn.call("UseInteractable")
	await wait_process_frames(2)

	# Edge-case confirmation: walking out of PickupBox2's trigger clears the interactable,
	# so even though the player may still be inside PickupBox1's trigger, UseInteractable
	# does nothing and nothing is picked up.
	assert_false(bool(pickup_pawn.get("HasPickup")),
		"Interacting after leaving PickupBox2 should not pick up anything (interactable was cleared by body_exited, not re-set by PickupBox1)")


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
