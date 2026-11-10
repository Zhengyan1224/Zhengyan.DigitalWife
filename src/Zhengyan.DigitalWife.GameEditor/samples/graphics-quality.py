def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return
    presets = {
        "LowQuality": (1, 512, 256, 4, 1.0, 1, 1),
        "MediumQuality": (2, 1024, 512, 9, 1.5, 2, 2),
        "HighQuality": (4, 2048, 1024, 25, 2.0, 4, 4),
    }
    preset = presets.get(control_name)
    if preset is None:
        return
    samples, directional, local, filtering, radius, mirror, water = preset
    scene.graphics.set_scene_samples(samples)
    scene.graphics.shadows.set_directional_resolution(directional)
    scene.graphics.shadows.set_local_light_resolution(local)
    scene.graphics.shadows.set_filter_samples(filtering)
    scene.graphics.shadows.set_filter_radius(radius)
    # Add the names of other surfaces here when the scene contains several mirrors or water surfaces.
    mirror_names = ("Mirror",)
    water_names = ("Water",)
    for names, reflection_samples in ((mirror_names, mirror), (water_names, water)):
        for name in names:
            surface = scene.get_entity(name)
            if surface is not None:
                surface.set_reflection_samples(reflection_samples)
