# Layer-key reporter: lets the host overlay app (keymap-overlay/) know the active layer.
target_sources_ifdef(CONFIG_ZMK_LAYER_KEYS app PRIVATE src/layer_keys.c)
