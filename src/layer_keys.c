/*
 * Layer keys: tap F13 + <highest active layer index> on every layer change.
 * A host-side overlay (keymap-overlay/) listens for these keys and shows
 * the keymap of the active layer. Reports go straight into the HID report
 * (not through the keycode event pipeline) so behaviors like caps word
 * and hold-taps never see them.
 *
 * SPDX-License-Identifier: MIT
 */

#include <zephyr/kernel.h>
#include <zephyr/logging/log.h>

#include <zmk/event_manager.h>
#include <zmk/events/layer_state_changed.h>
#include <zmk/keymap.h>
#include <zmk/keys.h>
#include <zmk/hid.h>
#include <zmk/endpoints.h>
#include <dt-bindings/zmk/hid_usage_pages.h>

LOG_MODULE_DECLARE(zmk, CONFIG_ZMK_LOG_LEVEL);

#define MAX_LAYER_KEYS 12 /* F13..F24 */

static int last_layer = -1;

static void layer_keys_work_cb(struct k_work *work) {
    int layer = zmk_keymap_highest_layer_active();

    if (layer == last_layer) {
        return;
    }
    last_layer = layer;

    if (layer < 0 || layer >= MAX_LAYER_KEYS) {
        return;
    }

    zmk_key_t key = CONFIG_ZMK_LAYER_KEYS_BASE_USAGE + layer;

    LOG_DBG("layer keys: layer %d -> usage 0x%02X", layer, key);

    if (zmk_hid_keyboard_press(key) < 0) {
        return;
    }
    zmk_endpoint_send_report(HID_USAGE_KEY);
    zmk_hid_keyboard_release(key);
    zmk_endpoint_send_report(HID_USAGE_KEY);
}

static K_WORK_DEFINE(layer_keys_work, layer_keys_work_cb);

static int layer_keys_listener(const zmk_event_t *eh) {
    /* Defer so the layer change (and the key that caused it) finishes first. */
    k_work_submit(&layer_keys_work);
    return ZMK_EV_EVENT_BUBBLE;
}

ZMK_LISTENER(layer_keys, layer_keys_listener);
ZMK_SUBSCRIPTION(layer_keys, zmk_layer_state_changed);
