# state.py
"""
Holds the shared global state for the Hidra GUI application.
"""
from __future__ import annotations

import threading
import queue
from typing import TYPE_CHECKING, Callable, Optional, List, Dict

# Use TYPE_CHECKING block for forward declarations to avoid circular imports.
if TYPE_CHECKING:
    from simulation_controller import SimulationController
    from renderer import Renderer3D
    from brain_renderer_2d import BrainRenderer2D

# --- API Core & Controller ---
controller: Optional[SimulationController] = None

# --- Threading & Communication ---
simulation_thread: Optional[threading.Thread] = None
ui_to_sim_queue: queue.Queue = queue.Queue()
sim_to_ui_queue: queue.Queue = queue.Queue()
shutdown_flag: threading.Event = threading.Event()

# --- Window Positioning Flags ---
needs_brain_viewer_positioning: bool = False
needs_event_viewer_positioning: bool = False

# --- Connection State ---
is_connected: bool = False
last_connection_url: str = "http://localhost:5000"

# --- Rendering & Visualization ---
renderer: Optional[Renderer3D] = None # <-- USE THE NEW RENDERER
brain_renderer: Optional[BrainRenderer2D] = None 

# --- Shared Camera State ---
camera_azimuth: float = 0.0
camera_elevation: float = 0.0
camera_radius: float = 150.0
camera_center_x: float = 0.0
camera_center_y: float = 0.0
camera_center_z: float = 0.0
mouse_last_x: int = 0
mouse_last_y: int = 0
mouse_left_button_down: bool = False
mouse_right_button_down: bool = False

# --- UI Interaction State ---
selected_exp_id: Optional[str] = None
inspected_neuron_id: Optional[int] = None
action_to_confirm: Optional[Callable] = None 

# --- I/O & Atomic Step State ---
available_input_ids: List[int] = []
available_output_ids: List[int] = []
staged_input_values: Dict[int, float] = {}
selected_input_node_id: Optional[int] = None

# --- Event Viewer State ---
show_future_events: bool = False

# --- Application Mode State ---
is_offline_mode: bool = False

# --- Playback-Specific UI State ---
current_display_tick: int = 0
playback_is_playing_ui: bool = False
target_tick: Optional[int] = None
last_playback_frame_time: float = 0.0
last_status_poll_time: float = 0.0

# --- Playback Speed Map & Application Paths ---
playback_speed_map = {
    "0.25x": 0.4, "0.5x": 0.2, "1x": 0.1, "1.5x": 0.066, "2x": 0.05
}
settings_dir: str = ""
default_ini_path: str = ""
user_ini_path: str = ""