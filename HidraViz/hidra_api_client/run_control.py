# hidra_api_client/run_control.py

from typing import Dict, List, Any, Optional, TYPE_CHECKING

# Use a forward reference to avoid circular imports at runtime
if TYPE_CHECKING:
    from .core import HidraApiClient

class RunControlClient:
    """
    Provides methods for the RunControlController endpoints.
    
    This client allows for controlling the execution of a specific experiment.
    """
    def __init__(self, api_client: 'HidraApiClient'):
        self._api_client = api_client
        
    # --- Continuous (Non-Audited) Lifecycle Control ---

    def start(self, exp_id: str) -> Dict[str, Any]:
        """Starts a continuous run for an experiment."""
        return self._api_client._request("POST", f"api/experiments/{exp_id}/start")

    def pause(self, exp_id: str) -> Dict[str, Any]:
        """Pauses a continuous run for an experiment."""
        return self._api_client._request("POST", f"api/experiments/{exp_id}/pause")

    def resume(self, exp_id: str) -> Dict[str, Any]:
        """Resumes a paused continuous run for an experiment."""
        return self._api_client._request("POST", f"api/experiments/{exp_id}/resume")

    def stop(self, exp_id: str) -> Dict[str, Any]:
        """Stops a continuous run for an experiment."""
        return self._api_client._request("POST", f"api/experiments/{exp_id}/stop")

    def step(self, exp_id: str) -> Dict[str, Any]:
        """Advances the experiment by a single tick."""
        return self._api_client._request("POST", f"api/experiments/{exp_id}/step")

    def atomic_step(self, 
                    exp_id: str, 
                    inputs: Dict[int, float], 
                    output_ids_to_read: List[int]) -> Dict[str, Any]:
        """Applies inputs, advances one tick, and reads outputs atomically."""
        payload = {
            "inputs": {str(k): v for k, v in inputs.items()},
            "outputIdsToRead": output_ids_to_read
        }
        return self._api_client._request("POST", f"api/experiments/{exp_id}/atomicStep", json=payload)

    def save_state(self, exp_id: str, experiment_name: str) -> Dict[str, Any]:
        """Saves the current world state of the experiment to a file on the server."""
        payload = {"experimentName": experiment_name}
        return self._api_client._request("POST", f"api/experiments/{exp_id}/save", json=payload)

    # --- Audited Run Endpoints ---

    def create_run(self, 
                   exp_id: str, 
                   run_type: str, 
                   parameters: Dict[str, Any],
                   staged_inputs: Optional[Dict[int, float]] = None,
                   staged_hormones: Optional[Dict[int, float]] = None) -> Dict[str, Any]:
        """Creates and starts a new audited run for an experiment."""
        payload = {
            "type": run_type,
            "parameters": parameters,
        }
        if staged_inputs is not None:
            payload["stagedInputs"] = {str(k): v for k, v in staged_inputs.items()}
        if staged_hormones is not None:
            payload["stagedHormones"] = {str(k): v for k, v in staged_hormones.items()}
            
        return self._api_client._request("POST", f"api/experiments/{exp_id}/runs", json=payload)

    def get_run(self, exp_id: str, run_id: str) -> Dict[str, Any]:
        """Retrieves the status and results of a specific audited run."""
        return self._api_client._request("GET", f"api/experiments/{exp_id}/runs/{run_id}")