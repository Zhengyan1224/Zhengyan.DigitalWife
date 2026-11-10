def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return
    high = control_name == "HighQuality"
    scene.graphics.set_scene_samples(4 if high else 1)
    scene.graphics.shadows.set_directional_resolution(2048 if high else 512)
    scene.graphics.shadows.set_local_light_resolution(1024 if high else 256)
    scene.graphics.shadows.set_filter_samples(25 if high else 4)
    scene.graphics.shadows.set_filter_radius(2.0 if high else 1.0)
    for name, samples in (("Mirror", 4 if high else 1), ("Water", 2 if high else 1)):
        surface = scene.get_entity(name)
        if surface is not None:
            surface.set_reflection_samples(samples)
