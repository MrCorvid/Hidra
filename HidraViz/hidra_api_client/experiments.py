# hidra_api_client/experiments.py

from typing import Dict, List, Any, Optional, TYPE_CHECKING

# Use a forward reference to avoid circular imports at runtime
if TYPE_CHECKING:
    from .core import HidraApiClient

class ExperimentsClient:
    """
    Provides methods for the ExperimentsController endpoints.
    
    This client allows for creating, listing, retrieving, deleting, cloning,
    and renaming experiments. It supports the hierarchical Registry system 
    (Evolution Runs -> Generations).
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
        Creates a new standalone (Manual) experiment from scratch.
        
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
        The new experiment becomes a 'Standalone' entry in the registry.
        """
        payload = {"name": name, "tick": tick}
        return self._api_client._request("POST", f"api/experiments/{exp_id}/clone", json=payload)

    def rename(self, exp_id: str, new_name: str) -> Dict[str, Any]:
        """
        Renames an existing experiment.
        
        Args:
            exp_id (str): The ID of the experiment to rename.
            new_name (str): The new name to assign.
        """
        payload = {"name": new_name}
        return self._api_client._request("PATCH", f"api/experiments/{exp_id}", json=payload)
        
    def list(self, parent_id: Optional[str] = None) -> List[Dict[str, Any]]:
        """
        Lists experiments from the Master Registry.
        
        Args:
            parent_id (str, optional): If provided, lists the children of this group 
                                       (e.g., Organisms within an Evolution Run).
                                       If None, lists Root items (Standalone Experiments + Evolution Runs).
        
        Returns:
            List[Dict[str, Any]]: A list of experiment metadata objects, including:
                                  - id, name, type (Standalone/EvolutionRun/GenerationOrganism)
                                  - activity, generation, fitness, childrenCount
        """
        params = {}
        if parent_id:
            params["parentId"] = parent_id
            
        return self._api_client._request("GET", "api/experiments", params=params)

    def get(self, exp_id: str) -> Dict[str, Any]:
        """
        Retrieves detailed information for a single experiment by its ID.
        Note: This requires the experiment to be loaded in the ExperimentManager.
        If an Evolution Organism is not loaded, this may return 404 until you call load().
        """
        return self._api_client._request("GET", f"api/experiments/{exp_id}")

    def delete(self, exp_id: str) -> None:
        """
        Stops and deletes an experiment.
        If it is a Group (Evolution Run), this will recursively delete all children.
        """
        self._api_client._request("DELETE", f"api/experiments/{exp_id}")