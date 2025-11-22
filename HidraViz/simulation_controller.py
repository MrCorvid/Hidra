# simulation_controller.py
import json
from dataclasses import dataclass, asdict
from typing import Dict, List, Any, Optional, Tuple

from hidra_api_client import HidraApiClient, HidraApiException

@dataclass
class ReplayFrame:
    """Stores all relevant data for a single tick of the simulation replay."""
    tick: int
    snapshot: Dict[str, Any]
    events: List[Dict[str, Any]]

class SimulationController:
    """
    Acts as a high-performance data store and API intermediary for one or more
    Hidra experiments.
    """
    def __init__(self, api_client: HidraApiClient):
        self.api_client = api_client
        self._data_store: Dict[str, Dict[int, ReplayFrame]] = {}
        self.is_offline = False

    def connect(self, exp_id: str) -> bool:
        """
        Connects to a live experiment and downloads its full history.
        """
        # Clear previous data for this ID to ensure a fresh start
        if exp_id in self._data_store:
            del self._data_store[exp_id]
            
        try:
            # Verify existence
            self.api_client.query.get_status(exp_id)
            self.log_message(f"Successfully connected to experiment '{exp_id}'. Downloading full history...")
            
            self._data_store[exp_id] = {}
            self.is_offline = False
            
            # Download history
            self._capture_full_history(exp_id)
            
            return True
        except HidraApiException as e:
            self.log_message(f"Failed to connect to experiment '{exp_id}': {e}", level="error")
            return False

    def refresh_history(self, exp_id: str) -> int:
        """
        Refreshes the history for an existing connection without clearing local state.
        Returns the number of frames fetched.
        """
        if self.is_offline:
            return 0
            
        # Ensure the dict exists
        if exp_id not in self._data_store:
            self._data_store[exp_id] = {}
            
        self.log_message(f"[{exp_id}] Refreshing history...")
        count = self._capture_full_history(exp_id)
        return count

    def disconnect(self, exp_id: str):
        if exp_id in self._data_store:
            del self._data_store[exp_id]
            self.log_message(f"Disconnected from experiment '{exp_id}'. Data cleared.")

    def _parse_events(self, raw_events_data: Any) -> List[Dict[str, Any]]:
        """
        Parses the event list, handling potential serialization wrappers (e.g. $values).
        """
        if not raw_events_data: 
            return []
        
        events_to_process = []
        if isinstance(raw_events_data, dict) and '$values' in raw_events_data:
            events_to_process = raw_events_data.get('$values', [])
        elif isinstance(raw_events_data, list):
            events_to_process = raw_events_data
        
        processed_events = []
        for event in events_to_process:
            if isinstance(event, str):
                try: 
                    processed_events.append(json.loads(event))
                except json.JSONDecodeError: 
                    processed_events.append({"type": "InvalidEventFormat", "data": event})
            elif isinstance(event, dict): 
                processed_events.append(event)
        
        return processed_events

    def _capture_full_history(self, exp_id: str) -> int:
        """
        Downloads and stores the full history. Returns count of frames processed.
        """
        try:
            history_frames = self.api_client.query.get_full_history(exp_id)
        except HidraApiException as e:
            self.log_message(f"[{exp_id}] Failed to download history: {e}", level="error")
            return 0

        count = len(history_frames)
        if count == 0:
            return 0

        for i, frame_data in enumerate(history_frames):
            tick = frame_data.get('tick', frame_data.get('Tick'))
            snapshot = frame_data.get('snapshot', frame_data.get('Snapshot'))
            events_raw = frame_data.get('events', frame_data.get('Events', []))

            if tick is None or snapshot is None:
                continue

            frame = ReplayFrame(
                tick=int(tick),
                snapshot=snapshot,
                events=self._parse_events(events_raw)
            )
            # Overwrite or add
            self._data_store[exp_id][frame.tick] = frame
            
        self.log_message(f"[{exp_id}] History sync complete. {count} frames available.")
        return count

    def _capture_frame(self, exp_id: str) -> Optional[ReplayFrame]:
        """Captures the *current* live state as a new frame."""
        try:
            snapshot = self.api_client.query.get_visualization_snapshot(exp_id)
        except HidraApiException:
            return None
        
        current_tick = snapshot.get('currentTick', snapshot.get('CurrentTick'))
        
        if current_tick is None:
            return None

        frame = ReplayFrame(tick=current_tick, snapshot=snapshot, events=[])
        self._data_store[exp_id][current_tick] = frame
        return frame

    def step_with_inputs(self, exp_id: str, inputs: Dict[int, float], outputs_to_read: List[int]) -> Optional[ReplayFrame]:
        """
        Advances the simulation by one step with the provided inputs using AtomicStep.
        """
        if self.is_offline or exp_id not in self._data_store:
            return None
        
        try:
            response = self.api_client.run_control.atomic_step(exp_id, inputs, outputs_to_read)
            new_frame = self._capture_frame(exp_id)
            
            if new_frame:
                raw_events_data = response.get("eventsProcessed", response.get("EventsProcessed", []))
                new_frame.events = self._parse_events(raw_events_data)
                self.log_message(f"[{exp_id}] Step to Tick {new_frame.tick} successful.")

            return new_frame

        except HidraApiException as e:
            self.log_message(f"API Error during step: {e}", level="error")
            # Raise or return None? Controller usually swallows and logs.
            # But worker might need to know to stop playback.
            return None

    def is_latest_tick(self, exp_id: str, tick: int) -> bool:
        if exp_id not in self._data_store or not self._data_store[exp_id]:
            return False
        latest_tick = max(self._data_store[exp_id].keys())
        return tick >= latest_tick

    def get_latest_frame(self, exp_id: str) -> Optional[ReplayFrame]:
        if exp_id not in self._data_store or not self._data_store[exp_id]:
            return None
        latest_tick = max(self._data_store[exp_id].keys())
        return self._data_store[exp_id][latest_tick]
        
    def get_frame(self, exp_id: str, tick: int) -> Optional[ReplayFrame]:
        return self._data_store.get(exp_id, {}).get(tick)

    def get_full_history(self, exp_id: str) -> List[ReplayFrame]:
        if exp_id not in self._data_store: 
            return []
        history = self._data_store[exp_id]
        return [history[tick] for tick in sorted(history.keys())]

    def save_replay_to_file(self, exp_id: str, filename: str):
        history = self.get_full_history(exp_id)
        if not history:
            raise ValueError("No history to save.")
        
        first_snapshot = history[0].snapshot
        metadata = {
            "experimentId": first_snapshot.get("experimentId"),
            "experimentName": first_snapshot.get("experimentName", "unnamed-experiment"),
            "total_frames": len(history)
        }
        
        serializable_frames = [asdict(frame) for frame in history]
        serializable_data = {"metadata": metadata, "frames": serializable_frames}
        
        with open(filename, 'w') as f:
            json.dump(serializable_data, f, indent=2)
        self.log_message(f"Replay data for '{exp_id}' saved to '{filename}'")
        
    def load_from_file(self, filename: str) -> Optional[Tuple[str, str]]:
        with open(filename, 'r') as f:
            data = json.load(f)
        
        metadata = data.get("metadata", {})
        exp_id = metadata.get("experimentId")
        if not exp_id:
            return None
        
        exp_name = metadata.get("experimentName", "loaded-replay")
        frames_data = data.get("frames", [])
        
        self._data_store.clear()
        self._data_store[exp_id] = {}
        
        for frame_data in frames_data:
            tick = frame_data.get('tick', frame_data.get('Tick', 0))
            snapshot = frame_data.get('snapshot', frame_data.get('Snapshot', {}))
            events = frame_data.get('events', frame_data.get('Events', []))

            frame = ReplayFrame(
                tick=tick,
                snapshot=snapshot,
                events=self._parse_events(events)
            )
            self._data_store[exp_id][frame.tick] = frame
        
        self.is_offline = True
        self.log_message(f"Loaded {len(frames_data)} frames for experiment '{exp_name}' ({exp_id})")
        return exp_id, exp_name
    
    def get_brain_details(self, exp_id: str, tick: int, neuron_id: int) -> Optional[Dict[str, Any]]:
        frame = self.get_frame(exp_id, tick)
        if not frame: return None
        
        neurons_list = frame.snapshot.get('neurons', [])
        for neuron_data in neurons_list:
            if neuron_data.get('id') == neuron_id:
                return neuron_data.get('brain')
        return None
    
    def log_message(self, msg: str, level: str = "info"):
        print(f"[{level.upper()}] [CONTROLLER] {msg}")