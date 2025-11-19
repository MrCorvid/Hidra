# hidra_api_client/experiments.py

from typing import Dict, List, Any, Optional, TYPE_CHECKING

# Use a forward reference to avoid circular imports at runtime
if TYPE_CHECKING:
    from .core import HidraApiClient

class ExperimentsClient:
    """
    Provides methods for the ExperimentsController endpoints.
    
    This client allows for creating, listing, retrieving, deleting, and cloning experiments.
    """
    def __init__(self, api_client: 'HidraApiClient'):
        self._api_client = api_client

    def create(self, 
               hgl_genome: str,
               name: str = "unnamed-experiment",
               config: Optional[Dict[str, Any]] = None,
               io_config: Optional[Dict[str, List[int]]] = None,
               seed: Optional[int] = None) -> Dict[str, Any]:
        """
        Creates a new experiment from scratch.
        
        Args:
            hgl_genome (str): The HGL source code for the experiment's neurons.
            name (str): A descriptive name for the experiment.
            config (dict, optional): Hidra configuration overrides.
            io_config (dict, optional): A dictionary specifying input and output node IDs,
                                        e.g., {"inputNodeIds": [1, 2], "outputNodeIds": [3]}.
            seed (int, optional): A seed for the random number generator.
        """
        payload = {
            "hglGenome": hgl_genome,
            "name": name,
            "config": config or {},
            "ioConfig": io_config or {},
        }
        if seed is not None:
            payload["seed"] = seed
        
        return self._api_client._request("POST", "api/experiments", json=payload)

    def restore(self,
                snapshot_json: str,
                hgl_genome: str,
                config: Dict[str, Any],
                io_config: Dict[str, List[int]],
                name: str = "restored-experiment") -> Dict[str, Any]:
        """
        Restores an experiment from a previously saved JSON snapshot.
        
        Args:
            snapshot_json (str): The JSON content of the saved world state.
            hgl_genome (str): The HGL source code associated with the snapshot.
            config (dict): The Hidra configuration associated with the snapshot.
            io_config (dict): The I/O configuration associated with the snapshot.
            name (str): A name for the newly restored experiment.
        """
        payload = {
            "snapshotJson": snapshot_json,
            "hglGenome": hgl_genome,
            "config": config,
            "ioConfig": io_config,
            "name": name
        }
        return self._api_client._request("POST", "api/experiments/restore", json=payload)

    def clone(self, exp_id: str, name: str, tick: int) -> Dict[str, Any]:
        """
        Clones an existing experiment starting from a specific tick.
        
        Args:
            exp_id (str): The ID of the source experiment.
            name (str): The name for the new experiment.
            tick (int): The specific tick to clone from.
            
        Returns:
            Dict[str, Any]: The details of the newly created experiment.
        """
        payload = {"name": name, "tick": tick}
        return self._api_client._request("POST", f"api/experiments/{exp_id}/clone", json=payload)
        
    def list(self, state: Optional[str] = None) -> List[Dict[str, Any]]:
        """
        Lists all active experiments, with an optional filter by state.
        
        Args:
            state (str, optional): Filter by state (e.g., "Idle", "Running", "Paused").
        """
        params = {}
        if state:
            params["state"] = state
        return self._api_client._request("GET", "api/experiments", params=params)

    def get(self, exp_id: str) -> Dict[str, Any]:
        """Retrieves detailed information for a single experiment by its ID."""
        return self._api_client._request("GET", f"api/experiments/{exp_id}")

    def delete(self, exp_id: str) -> None:
        """Stops and deletes an experiment, freeing all associated resources."""
        self._api_client._request("DELETE", f"api/experiments/{exp_id}")