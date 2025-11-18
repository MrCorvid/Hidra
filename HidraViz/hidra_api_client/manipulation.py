# hidra_api_client/manipulation.py

from typing import Dict, Any, Optional, TYPE_CHECKING

# Use a forward reference to avoid circular imports at runtime
if TYPE_CHECKING:
    from .core import HidraApiClient

class ManipulationClient:
    """
    Provides methods for the ManipulationController endpoints.
    
    This client allows for direct manipulation of an experiment's world graph
    (e.g., creating/deleting neurons/synapses, and setting I/O values).
    """
    def __init__(self, api_client: 'HidraApiClient'):
        self._api_client = api_client
        
    # --- I/O and Hormone Manipulation ---

    def set_input_values(self, exp_id: str, values: Dict[int, float]) -> Dict[str, Any]:
        """Sets the values for multiple input nodes simultaneously."""
        payload = {str(k): v for k, v in values.items()}
        return self._api_client._request("PUT", f"api/experiments/{exp_id}/manipulate/inputs", json=payload)

    def set_hormones(self, exp_id: str, values: Dict[int, float]) -> Dict[str, Any]:
        """Sets the values for multiple global hormones (gvars). Requires the simulation to be paused or idle."""
        payload = {str(k): v for k, v in values.items()}
        return self._api_client._request("PATCH", f"api/experiments/{exp_id}/manipulate/hormones", json=payload)

    def add_input_node(self, exp_id: str, node_id: int, initial_value: float = 0.0) -> Dict[str, Any]:
        """Adds a new input node to the experiment."""
        payload = {"id": node_id, "initialValue": initial_value}
        return self._api_client._request("POST", f"api/experiments/{exp_id}/manipulate/inputs", json=payload)

    def add_output_node(self, exp_id: str, node_id: int) -> Dict[str, Any]:
        """Adds a new output node to the experiment."""
        payload = {"id": node_id}
        return self._api_client._request("POST", f"api/experiments/{exp_id}/manipulate/outputs", json=payload)

    def delete_input_node(self, exp_id: str, node_id: int) -> None:
        """Deletes an input node from the experiment."""
        self._api_client._request("DELETE", f"api/experiments/{exp_id}/manipulate/inputs/{node_id}")

    def delete_output_node(self, exp_id: str, node_id: int) -> None:
        """Deletes an output node from the experiment."""
        self._api_client._request("DELETE", f"api/experiments/{exp_id}/manipulate/outputs/{node_id}")
        
    # --- Neuron Manipulation ---

    def create_neuron(self, exp_id: str, position: Dict[str, float]) -> Dict[str, Any]:
        """
        Creates a new neuron at a specified position.
        
        Args:
            exp_id (str): The ID of the experiment.
            position (Dict[str, float]): A dictionary with 'x', 'y', 'z' keys.
        """
        payload = {"position": position}
        return self._api_client._request("POST", f"api/experiments/{exp_id}/manipulate/neurons", json=payload)
    
    def perform_mitosis(self, exp_id: str, parent_id: int, offset: Dict[str, float]) -> Dict[str, Any]:
        """
        Performs mitosis on a parent neuron, creating a child neuron at an offset.
        
        Args:
            exp_id (str): The ID of the experiment.
            parent_id (int): The ID of the neuron to divide.
            offset (Dict[str, float]): The positional offset for the child, with 'x', 'y', 'z' keys.
        """
        payload = {"offset": offset}
        return self._api_client._request("POST", f"api/experiments/{exp_id}/manipulate/neurons/{parent_id}/mitosis", json=payload)

    def delete_neuron(self, exp_id: str, neuron_id: int) -> None:
        """Deletes a neuron and all its connected synapses from the experiment."""
        self._api_client._request("DELETE", f"api/experiments/{exp_id}/manipulate/neurons/{neuron_id}")

    def deactivate_neuron(self, exp_id: str, neuron_id: int) -> Dict[str, Any]:
        """Queues a neuron for deactivation at the end of the next tick."""
        return self._api_client._request("POST", f"api/experiments/{exp_id}/manipulate/neurons/{neuron_id}:deactivate")

    def patch_local_variables(self, exp_id: str, neuron_id: int, lvars: Dict[int, float]) -> Dict[str, Any]:
        """Sets or updates local variables (lvars) for a specific neuron."""
        payload = {"localVariables": {str(k): v for k, v in lvars.items()}}
        return self._api_client._request("PATCH", f"api/experiments/{exp_id}/manipulate/neurons/{neuron_id}/lvars", json=payload)
        
    # --- Synapse Manipulation ---
    
    def create_synapse(self, exp_id: str, source_id: int, target_id: int, signal_type: str, weight: float, parameter: float = 0.0) -> Dict[str, Any]:
        """
        Creates a new synapse between two neurons.
        
        Args:
            exp_id (str): The ID of the experiment.
            source_id (int): The ID of the source neuron.
            target_id (int): The ID of the target neuron.
            signal_type (str): The type of signal (e.g., 'Excitatory', 'Inhibitory').
            weight (float): The weight of the synapse.
            parameter (float): An optional parameter for the synapse.
        """
        payload = {
            "sourceId": source_id,
            "targetId": target_id,
            "signalType": signal_type,
            "weight": weight,
            "parameter": parameter
        }
        return self._api_client._request("POST", f"api/experiments/{exp_id}/manipulate/synapses", json=payload)
        
    def modify_synapse(self, exp_id: str, synapse_id: int, weight: Optional[float] = None, parameter: Optional[float] = None, signal_type: Optional[str] = None, condition: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
        """Modifies properties of an existing synapse."""
        payload = {}
        if weight is not None:
            payload["weight"] = weight
        if parameter is not None:
            payload["parameter"] = parameter
        if signal_type is not None:
            payload["signalType"] = signal_type
        if condition is not None:
            payload["condition"] = condition
        
        return self._api_client._request("PATCH", f"api/experiments/{exp_id}/manipulate/synapses/{synapse_id}", json=payload)
        
    def delete_synapse(self, exp_id: str, synapse_id: int) -> None:
        """Deletes a synapse from the experiment."""
        self._api_client._request("DELETE", f"api/experiments/{exp_id}/manipulate/synapses/{synapse_id}")