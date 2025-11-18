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
    Hidra experiments. This class is designed to be controlled by a separate
    application, such as a GUI.
    
    This implementation assumes the C# simulation has been fixed to properly
    archive events under the tick they occurred during (not the next tick).
    """
    def __init__(self, api_client: HidraApiClient):
        self.api_client = api_client
        self._data_store: Dict[str, Dict[int, ReplayFrame]] = {}
        self.is_offline = False

    def connect(self, exp_id: str) -> bool:
        """
        Connects to a live experiment and downloads its full history.
        """
        if exp_id in self._data_store:
            del self._data_store[exp_id]
        try:
            self.api_client.query.get_status(exp_id)
            self.log_message(f"Successfully connected to experiment '{exp_id}'. Downloading full history...")
            self._data_store[exp_id] = {}
            self.is_offline = False
            self._capture_full_history(exp_id)
            return True
        except HidraApiException as e:
            self.log_message(f"Failed to connect to experiment '{exp_id}': {e}", level="error")
            return False

    def disconnect(self, exp_id: str):
        """
        Disconnects from an experiment and clears its data from memory.
        """
        if exp_id in self._data_store:
            del self._data_store[exp_id]
            self.log_message(f"Disconnected from experiment '{exp_id}'. Data cleared.")

    def _parse_events(self, raw_events_data: Any) -> List[Dict[str, Any]]:
        """
        Parses raw event data from the API into a consistent format.
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
                    processed_events.append({"$type": "InvalidEventFormat", "data": event})
            elif isinstance(event, dict): 
                processed_events.append(event)
        
        return processed_events

    def _capture_full_history(self, exp_id: str):
        """
        Downloads and stores the complete history of an experiment.
        """
        history_frames = self.api_client.query.get_full_history(exp_id)
        for frame_data in history_frames:
            frame = ReplayFrame(
                tick=frame_data['tick'],
                snapshot=frame_data['snapshot'],
                events=self._parse_events(frame_data.get('events', []))
            )
            self._data_store[exp_id][frame.tick] = frame
        self.log_message(f"[{exp_id}] Captured {len(history_frames)} frames of history from the server.")

    def _capture_frame(self, exp_id: str) -> Optional[ReplayFrame]:
        """
        Captures a single frame representing the current state of the simulation.
        """
        snapshot = self.api_client.query.get_visualization_snapshot(exp_id)
        current_tick = snapshot['currentTick']
        frame = ReplayFrame(tick=current_tick, snapshot=snapshot, events=[])
        self._data_store[exp_id][current_tick] = frame
        self.log_message(f"[{exp_id}] Captured new frame for Tick {current_tick}.")
        return frame

    def step_with_inputs(self, exp_id: str, inputs: Dict[int, float], outputs_to_read: List[int]) -> Optional[ReplayFrame]:
        """
        Advances the simulation by one step with the provided inputs.
        
        This method assumes the C# simulation properly archives events under the tick
        they occurred during. The returned frame represents the state AFTER the step,
        and contains the events that occurred during that step execution.
        
        Args:
            exp_id: The experiment identifier
            inputs: Dictionary mapping input node IDs to their values
            outputs_to_read: List of output node IDs to read values from
            
        Returns:
            The new frame representing the post-step state with associated events,
            or None if the operation failed.
        """
        if self.is_offline or exp_id not in self._data_store:
            return None
        
        try:
            # Execute the atomic step
            response = self.api_client.run_control.atomic_step(exp_id, inputs, outputs_to_read)
            
            # Capture the new simulation state
            new_frame = self._capture_frame(exp_id)
            
            # With the C# fix, events in the response correspond to the tick that just executed
            # The new_frame.tick represents "next tick to execute", so the events belong to tick (new_frame.tick - 1)
            # However, for user experience, we show these events on the new frame since they caused this state
            if new_frame:
                raw_events_data = response.get("eventsProcessed", [])
                new_frame.events = self._parse_events(raw_events_data)
                
                # Optional: Add semantic context for debugging/logging
                completed_tick = new_frame.tick - 1 if new_frame.tick > 0 else 0
                self.log_message(f"[{exp_id}] Step completed. Events from tick {completed_tick} displayed on frame {new_frame.tick}.")

            return new_frame

        except HidraApiException as e:
            self.log_message(f"API Error during step: {e}", level="error")
            return None

    def is_latest_tick(self, exp_id: str, tick: int) -> bool:
        """
        Checks if the given tick represents the latest available data.
        """
        if exp_id not in self._data_store or not self._data_store[exp_id]:
            return False
        latest_tick = max(self._data_store[exp_id].keys())
        return tick >= latest_tick

    def get_latest_frame(self, exp_id: str) -> Optional[ReplayFrame]:
        """
        Retrieves the most recent frame for an experiment.
        """
        if exp_id not in self._data_store or not self._data_store[exp_id]:
            return None
        latest_tick = max(self._data_store[exp_id].keys())
        return self._data_store[exp_id][latest_tick]
        
    def get_frame(self, exp_id: str, tick: int) -> Optional[ReplayFrame]:
        """
        Retrieves a specific frame by tick number.
        """
        return self._data_store.get(exp_id, {}).get(tick)

    def get_first_frame(self, exp_id: str) -> Optional[ReplayFrame]:
        """
        Retrieves the earliest frame for an experiment.
        """
        if exp_id not in self._data_store or not self._data_store[exp_id]:
            return None
        first_tick = min(self._data_store[exp_id].keys())
        return self._data_store[exp_id][first_tick]

    def get_full_history(self, exp_id: str) -> List[ReplayFrame]:
        """
        Retrieves all frames for an experiment in chronological order.
        """
        if exp_id not in self._data_store: 
            return []
        history = self._data_store[exp_id]
        return [history[tick] for tick in sorted(history.keys())]

    def get_display_context(self, frame: ReplayFrame) -> Dict[str, Any]:
        """
        Provides semantic context for displaying a frame to users.
        
        This helps clarify what the tick number means in relation to the events shown.
        """
        if frame.tick == 0:
            return {
                'semantic_label': 'Initial State',
                'description': 'Starting conditions before any simulation steps',
                'events_description': 'No events yet'
            }
        
        completed_tick = frame.tick - 1
        return {
            'semantic_label': f'After Tick {completed_tick}',
            'description': f'State resulting from tick {completed_tick} execution',
            'events_description': f'Events that occurred during tick {completed_tick}',
            'next_tick': frame.tick
        }

    def save_replay_to_file(self, exp_id: str, filename: str):
        """
        Saves the complete experiment history to a JSON file.
        """
        history = self.get_full_history(exp_id)
        if not history:
            raise ValueError("No history to save for this experiment.")
        
        first_snapshot = history[0].snapshot
        metadata = {
            "experimentId": first_snapshot.get("experimentId"),
            "experimentName": first_snapshot.get("experimentName", "unnamed-experiment"),
            "saved_at": "ISO timestamp could go here",
            "total_frames": len(history)
        }
        
        serializable_frames = [asdict(frame) for frame in history]
        serializable_data = {"metadata": metadata, "frames": serializable_frames}
        
        with open(filename, 'w') as f:
            json.dump(serializable_data, f, indent=2)
        self.log_message(f"Replay data for '{exp_id}' saved to '{filename}'")
        
    def load_from_file(self, filename: str) -> Optional[Tuple[str, str]]:
        """
        Loads experiment data from a previously saved replay file.
        """
        with open(filename, 'r') as f:
            data = json.load(f)
        
        metadata = data.get("metadata", {})
        exp_id = metadata.get("experimentId")
        if not exp_id:
            self.log_message("Replay file is missing 'experimentId' in its data.", level="error")
            return None
        
        exp_name = metadata.get("experimentName", "loaded-replay")
        frames_data = data.get("frames", [])
        
        # Clear existing data and load from file
        self._data_store.clear()
        self._data_store[exp_id] = {}
        
        for frame_data in frames_data:
            frame = ReplayFrame(
                tick=frame_data.get('tick', 0),
                snapshot=frame_data.get('snapshot', {}),
                events=self._parse_events(frame_data.get('events', []))
            )
            self._data_store[exp_id][frame.tick] = frame
        
        self.is_offline = True
        self.log_message(f"Loaded {len(frames_data)} frames for experiment '{exp_name}' ({exp_id})")
        return exp_id, exp_name
    
    def get_experiment_summary(self, exp_id: str) -> Dict[str, Any]:
        """
        Returns a summary of the experiment data for display purposes.
        """
        if exp_id not in self._data_store:
            return {"error": "Experiment not found"}
        
        frames = self._data_store[exp_id]
        if not frames:
            return {"error": "No data available"}
        
        ticks = sorted(frames.keys())
        first_frame = frames[ticks[0]]
        last_frame = frames[ticks[-1]]
        
        return {
            "experiment_id": exp_id,
            "is_offline": self.is_offline,
            "tick_range": {"first": ticks[0], "last": ticks[-1]},
            "total_frames": len(frames),
            "experiment_name": first_frame.snapshot.get("experimentName", "Unknown"),
            "current_state_tick": last_frame.tick if not self.is_offline else "N/A (offline)"
        }
    
    def get_brain_details(self, exp_id: str, tick: int, neuron_id: int) -> Optional[Dict[str, Any]]:
        """
        Extracts the brain details for a specific neuron from a stored frame.
        """
        frame = self.get_frame(exp_id, tick)
        if not frame:
            return None
        
        neurons_list = frame.snapshot.get('neurons', [])
        for neuron_data in neurons_list:
            if neuron_data.get('id') == neuron_id:
                return neuron_data.get('brain') # This is the VisualizationBrainDto
        
        return None
    
    def log_message(self, msg: str, level: str = "info"):
        """
        Logs a message with appropriate formatting.
        """
        print(f"[{level.upper()}] [CONTROLLER] {msg}")