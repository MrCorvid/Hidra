# ui_utils.py
"""
GUI helper functions for the Hidra application.
"""
import dearpygui.dearpygui as dpg
import time
import os

import state
import callbacks
from simulation_controller import ReplayFrame

MAX_LOG_MESSAGES = 200

# --- Logging & Status Bar ---
def log_message(message: str, level: str = "info"):
    """Adds a timestamped, wrapped message to the log window and prunes old messages."""
    if not dpg.does_item_exist("log_group"): return
    
    timestamp = time.strftime("%H:%M:%S", time.localtime())
    log_entry = f"[{timestamp}] [{level.upper()}] {message}"
    
    try:
        width = dpg.get_item_rect_size("log_child_window")[0]
        wrap_width = width - 25 if width > 30 else 0
    except Exception:
        wrap_width = 0

    dpg.add_text(log_entry, parent="log_group", wrap=wrap_width)

    children = dpg.get_item_children("log_group", 1)
    if len(children) > MAX_LOG_MESSAGES:
        dpg.delete_item(children[0])
    
    if dpg.does_item_exist("log_child_window"):
        dpg.set_y_scroll("log_child_window", -1.0)

def update_status_bar(message: str, level: str = "info"):
    """Updates the main status bar at the bottom of the controls window."""
    log_message(message, level)
    if dpg.does_item_exist("status_bar"):
        dpg.set_value("status_bar", f"Status: {message}")


# --- Window & Widget Positioning ---
def reposition_all_visualizer_overlays(sender=None, app_data=None, user_data=None):
    """
    Positions the toolbar and any undocked, floating visualizer-related windows
    (like the brain viewer or event viewer) relative to the main visualizer window.
    This function is intended to be called by the visualizer's resize handler.
    """
    parent_window_tag = "visualizer_window"
    content_area_tag = "visualizer_content_child"
    toolbar_tag = "visualizer_toolbar_group"

    if not dpg.does_item_exist(parent_window_tag) or not dpg.is_item_shown(parent_window_tag):
        return

    # 1. Position the main toolbar inside the content area
    if dpg.does_item_exist(toolbar_tag) and dpg.does_item_exist(content_area_tag):
        try:
            content_width = dpg.get_item_rect_size(content_area_tag)[0]
            toolbar_width = dpg.get_item_rect_size(toolbar_tag)[0]
            margin = 5
            
            new_x = content_width - toolbar_width - margin
            new_y = margin
            
            dpg.set_item_pos(toolbar_tag, (new_x, new_y))
        except Exception:
            pass

    # 2. Position floating, undocked overlay windows
    windows_to_position = ["brain_visualizer_window", "event_viewer_window"]
    
    try:
        parent_pos = dpg.get_item_pos(parent_window_tag)
        parent_size = dpg.get_item_rect_size(parent_window_tag)
        margin = 10
        
        for window_tag in windows_to_position:
            if not dpg.does_item_exist(window_tag) or not dpg.is_item_shown(window_tag):
                continue
            
            # THE CRITICAL CHECK: Only position if it's a floating window (not docked)
            # DPG returns parent=0 for windows that are not docked.
            if dpg.get_item_state(window_tag)['resized'] is False:
                window_size = dpg.get_item_rect_size(window_tag)
                
                if window_size[0] == 0 or window_size[1] == 0:
                    continue

                new_x = parent_pos[0] + parent_size[0] - window_size[0] - margin
                new_y = parent_pos[1] + margin
                
                dpg.set_item_pos(window_tag, (new_x, new_y))

    except Exception:
        pass


# --- (The rest of the file is unchanged) ---
def update_io_panel_selection():
    node_id = state.selected_input_node_id
    if node_id is None:
        dpg.set_value("selected_input_node_display", "Selected: None")
        dpg.configure_item("staged_value_input", enabled=False)
        dpg.configure_item("commit_staged_value_button", enabled=False)
    else:
        dpg.set_value("selected_input_node_display", f"Selected: {node_id}")
        dpg.configure_item("staged_value_input", enabled=True)
        dpg.configure_item("commit_staged_value_button", enabled=True)
        dpg.set_value("staged_value_input", state.staged_input_values.get(node_id, 0.0))

def update_input_button_themes():
    for node_id in state.available_input_ids:
        button_tag = f"input_node_btn_{node_id}"
        if dpg.does_item_exist(button_tag):
            if state.staged_input_values.get(node_id, 0.0) != 0.0:
                dpg.bind_item_theme(button_tag, "staged_button_theme")
            else:
                dpg.bind_item_theme(button_tag, 0)

def update_io_panel(frame: ReplayFrame):
    snapshot = frame.snapshot
    
    input_ids = snapshot.get("inputNodeIds", [])
    if set(state.available_input_ids) != set(input_ids):
        state.available_input_ids = sorted(input_ids)
        dpg.delete_item("input_node_grid_group", children_only=True)
        
        if not input_ids:
            dpg.add_text("No input nodes defined.", color=(255, 255, 100), parent="input_node_grid_group")
        else:
            for node_id in state.available_input_ids:
                dpg.add_button(label=f"{node_id}", tag=f"input_node_btn_{node_id}",
                               user_data=node_id, callback=callbacks.select_input_node_callback,
                               parent="input_node_grid_group")
        update_input_button_themes()

    output_ids = snapshot.get("outputNodeIds", [])
    output_values = snapshot.get("outputNodeValues", {})
    if set(state.available_output_ids) != set(output_ids):
        state.available_output_ids = sorted(output_ids)
        dpg.delete_item("output_node_display_group", children_only=True)
        if not output_ids:
            dpg.add_text("No output nodes defined.", color=(255, 255, 100), parent="output_node_display_group")
        else:
            for node_id in state.available_output_ids:
                value = output_values.get(str(node_id), 0.0)
                dpg.add_text(f"ID: {node_id} --> Value: {value:.4f}", 
                             tag=f"output_node_text_{node_id}",
                             parent="output_node_display_group")
    else:
        for node_id in state.available_output_ids:
            value = output_values.get(str(node_id), 0.0)
            if dpg.does_item_exist(f"output_node_text_{node_id}"):
                dpg.set_value(f"output_node_text_{node_id}", f"ID: {node_id} --> Value: {value:.4f}")

def update_ui_with_replay_frame(frame: ReplayFrame):
    if not frame or not state.controller or not state.selected_exp_id:
        return

    state.current_display_tick = frame.tick

    if dpg.does_item_exist("timeline_scrubber"):
        latest_frame = state.controller.get_latest_frame(state.selected_exp_id)
        if latest_frame:
            dpg.configure_item("timeline_scrubber", max_value=latest_frame.tick)
        dpg.set_value("timeline_scrubber", frame.tick)
    
    if dpg.does_item_exist("visualizer_tick_display"):
        dpg.set_value("visualizer_tick_display", f"Displaying Tick: {frame.tick}")

    if dpg.does_item_exist("detail_tick_cached"):
        status = state.controller.get_status(state.selected_exp_id, frame.tick)
        if status:
            dpg.set_value("detail_tick_cached", f"Displaying Tick: {status.get('currentTick', 'N/A')}")
            dpg.set_value("detail_neurons_cached", f"Neurons: {status.get('neuronCount', 'N/A')}")
            dpg.set_value("detail_synapses_cached", f"Synapses: {status.get('synapseCount', 'N/A')}")

    update_io_panel(frame)
    update_event_viewer()
    
    # --- FIX: REMOVED THE ERRONEOUS BLOCK ---
    # The new renderer is now updated correctly from callbacks.py, so this call is no longer needed.
    #
    # if state.renderer:
    #     state.renderer.update_render_data(frame.snapshot) # <- THIS WAS THE ERROR
    # --- END FIX ---
    
    if state.inspected_neuron_id:
        update_brain_visualizer()

def _format_event_to_string(event: dict) -> str:
    # --- START: CRITICAL FIX ---
    # Use the correct keys from the event data based on experiment1.json
    event_type = event.get('type', 'UnknownEvent')
    tick = event.get('executionTick', '?')
    
    # Also update the keys to exclude from the details string
    details = ", ".join(f"{k}: {v}" for k, v in event.items() if k not in ['type', 'executionTick', 'id'])
    return f"[T:{tick}] {event_type} | {details}"
    # --- END: CRITICAL FIX ---

def update_event_viewer():
    if not state.controller or not state.selected_exp_id:
        return
        
    dpg.delete_item("event_viewer_content_group", children_only=True)

    current_frame = state.controller.get_frame(state.selected_exp_id, state.current_display_tick)
    if not current_frame:
        dpg.add_text("No data for current tick.", parent="event_viewer_content_group")
        return

    all_events = list(current_frame.events)

    if state.show_future_events:
        next_frame = state.controller.get_frame(state.selected_exp_id, state.current_display_tick + 1)
        if next_frame:
            all_events.extend(next_frame.events)

    if not all_events:
        dpg.add_text("No events for this tick.", parent="event_viewer_content_group")
    else:
        for event in all_events:
            formatted_event = _format_event_to_string(event)
            dpg.add_text(formatted_event, parent="event_viewer_content_group", wrap=550)

def update_brain_visualizer():
    if not state.brain_renderer or not state.controller or not state.selected_exp_id:
        return
    if not state.inspected_neuron_id:
        state.brain_renderer.update_data(None)
        return
    brain_data = state.controller.get_brain_details(
        state.selected_exp_id, state.current_display_tick, state.inspected_neuron_id
    )
    state.brain_renderer.update_data(brain_data)
    if brain_data:
        log_message(f"Updated 2D brain view for neuron {state.inspected_neuron_id}", "info")
    else:
        log_message(f"No brain data found for neuron {state.inspected_neuron_id}", "warning")

def reset_all_state():
    if state.selected_exp_id and not state.is_offline_mode:
        state.ui_to_sim_queue.put({"type": "DISCONNECT_EXPERIMENT", "exp_id": state.selected_exp_id})

    state.is_connected = False
    state.is_offline_mode = False
    state.selected_exp_id = None
    state.inspected_neuron_id = None
    state.playback_is_playing_ui = False
    state.current_display_tick = 0
    state.target_tick = None
    state.staged_input_values.clear()
    state.selected_input_node_id = None
    
    if state.controller:
        state.controller._data_store.clear()
        
    dpg.configure_item("main_controls_group", show=False)
    dpg.configure_item("connection_group", show=True)
    dpg.configure_item("reconnect_button", show=False)
    dpg.configure_item("exp_listbox", items=[])
    dpg.set_value("exp_listbox", "")
    
    if state.brain_renderer:
        state.brain_renderer.update_data(None)
        
    update_io_panel(ReplayFrame(tick=0, snapshot={}, events=[]))
    update_event_viewer()
    update_status_bar("Ready. Please connect to a source.", "info")

def update_ui_for_mode():
    is_live = not state.is_offline_mode
    
    live_elements = [
        "create_exp_button", "delete_exp_button", "save_state_button", "save_replay_button",
        "world_manip_content_group", "brain_manipulation_group", "play_until_latest_button",
        "save_replay_menu_item", "refresh_exp_button", "statistics_group"
    ]
    
    for item in live_elements:
        if dpg.does_item_exist(item):
            try:
                dpg.configure_item(item, enabled=is_live)
            except SystemError:
                try:
                    dpg.configure_item(item, show=is_live)
                except Exception as e:
                    log_message(f"Could not configure item {item}: {e}", "debug")

    if dpg.does_item_exist("step_forward_button"):
        dpg.set_value("step_forward_button", "▶| Step Forward" if is_live else "▶| Next Frame")

def is_at_latest_tick() -> bool:
    if not state.controller or not state.selected_exp_id:
        return False
    
    latest_frame = state.controller.get_latest_frame(state.selected_exp_id)
    if not latest_frame:
        return False
        
    return state.current_display_tick == latest_frame.tick