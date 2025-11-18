# hidra_api_client/brain.py

from typing import Dict, Any, Optional, TYPE_CHECKING

# Use a forward reference to avoid circular imports at runtime
if TYPE_CHECKING:
    from .core import HidraApiClient

class BrainClient:
    """
    Provides methods for the BrainController endpoints.
    
    This client allows for manipulating the internal "brain" (e.g., a Neural Network)
    of a single neuron within a specific experiment.
    """
    def __init__(self, api_client: 'HidraApiClient'):
        self._api_client = api_client

    def _base_path(self, exp_id: str, neuron_id: int) -> str:
        """Helper to construct the base URL for this controller."""
        return f"api/experiments/{exp_id}/neurons/{neuron_id}/brain"

    def set_type(self, exp_id: str, neuron_id: int, brain_type: str, gate_type: Optional[str] = None, flip_flop: Optional[str] = None, threshold: float = 0.5) -> Dict[str, Any]:
        """Sets the type of brain for a neuron (e.g., 'NeuralNetwork' or 'LogicGate')."""
        payload = {"type": brain_type}
        if brain_type.lower() == 'logicgate':
            payload.update({
                "gateType": gate_type,
                "flipFlop": flip_flop,
                "threshold": threshold
            })
        path = f"{self._base_path(exp_id, neuron_id)}/type"
        return self._api_client._request("POST", path, json=payload)
        
    def construct(self, exp_id: str, neuron_id: int, constructor_type: str, **kwargs) -> Dict[str, Any]:
        """
        Constructs a brain using a predefined template (e.g., 'SimpleFeedForward').
        
        Args:
            exp_id (str): The ID of the experiment.
            neuron_id (int): The ID of the neuron.
            constructor_type (str): The type of constructor to use.
            **kwargs: Constructor-specific parameters like num_inputs, num_outputs, etc.
                      (e.g., numInputs=2, numOutputs=1)
        """
        payload = {"type": constructor_type, "parameters": kwargs}
        
        # The C# DTO expects parameters directly, not nested. Let's fix that.
        # The provided DTO has NumInputs, NumOutputs directly on the object.
        # Let's match that.
        # A better approach for the Python client:
        payload = {"type": constructor_type}
        
        # Convert python_case kwargs to camelCase for the API
        params = {
            f"{k[0].lower()}{k.title().replace('_', '')[1:]}": v
            for k, v in kwargs.items()
        }
        payload.update(params)

        path = f"{self._base_path(exp_id, neuron_id)}/construct"
        return self._api_client._request("POST", path, json=payload)
        
    def clear(self, exp_id: str, neuron_id: int) -> Dict[str, Any]:
        """Clears all nodes and connections from a neuron's NeuralNetworkBrain."""
        path = f"{self._base_path(exp_id, neuron_id)}/clear"
        return self._api_client._request("POST", path)

    def add_node(self, exp_id: str, neuron_id: int, node_type: str, bias: float = 0.0) -> Dict[str, Any]:
        """Adds a new node to the neuron's brain."""
        payload = {"nodeType": node_type, "bias": bias}
        path = f"{self._base_path(exp_id, neuron_id)}/nodes"
        return self._api_client._request("POST", path, json=payload)

    def delete_node(self, exp_id: str, neuron_id: int, node_id: int) -> None:
        """Deletes a node from the neuron's brain."""
        path = f"{self._base_path(exp_id, neuron_id)}/nodes/{node_id}"
        self._api_client._request("DELETE", path)
        
    def add_connection(self, exp_id: str, neuron_id: int, from_node_id: int, to_node_id: int, weight: float) -> Dict[str, Any]:
        """Adds a connection between two nodes in the neuron's brain."""
        payload = {"fromNodeId": from_node_id, "toNodeId": to_node_id, "weight": weight}
        path = f"{self._base_path(exp_id, neuron_id)}/connections"
        return self._api_client._request("POST", path, json=payload)

    def delete_connection(self, exp_id: str, neuron_id: int, from_node_id: int, to_node_id: int) -> None:
        """Deletes a connection between two nodes in the neuron's brain."""
        params = {"fromNodeId": from_node_id, "toNodeId": to_node_id}
        path = f"{self._base_path(exp_id, neuron_id)}/connections"
        self._api_client._request("DELETE", path, params=params)

    def configure_node(self, exp_id: str, neuron_id: int, node_id: int, **kwargs) -> Dict[str, Any]:
        """
        Configures properties of a specific node in the brain.
        
        Args:
            exp_id (str): The ID of the experiment.
            neuron_id (int): The ID of the neuron.
            node_id (int): The ID of the node within the brain.
            **kwargs: Properties to update (e.g., bias=0.5, activationFunction='Sigmoid').
        """
        # Convert python_case kwargs to camelCase for the API
        payload = {
            f"{k[0].lower()}{k.title().replace('_', '')[1:]}": v
            for k, v in kwargs.items()
        }
        path = f"{self._base_path(exp_id, neuron_id)}/nodes/{node_id}"
        return self._api_client._request("PATCH", path, json=payload)