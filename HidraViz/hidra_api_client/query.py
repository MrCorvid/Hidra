# hidra_api_client/query.py

from typing import Dict, List, Any, Optional, TYPE_CHECKING

# Use a forward reference to avoid circular imports at runtime
if TYPE_CHECKING:
    from .core import HidraApiClient

class QueryClient:
    """
    Provides methods for the QueryController endpoints.
    
    This client allows for querying the state of an experiment's world,
    including neurons, synapses, logs, and general status.
    """
    def __init__(self, api_client: 'HidraApiClient'):
        self._api_client = api_client

    def get_full_history(self, exp_id: str) -> List[Dict[str, Any]]:
        """
        Retrieves the complete history of an experiment as a series of replay frames.

        The server deserializes compressed snapshots and returns a list of
        ReplayFrameDto objects containing:
        - tick: The simulation tick.
        - snapshot: The full VisualizationSnapshotDto (neurons, synapses, values).
        - events: A list of events processed during that tick.

        Args:
            exp_id (str): The ID of the experiment.

        Returns:
            List[Dict[str, Any]]: A list of ReplayFrame dictionaries.
        """
        return self._api_client._request("GET", f"api/experiments/{exp_id}/query/history")

    def get_status(self, exp_id: str) -> Dict[str, Any]:
        """Retrieves the high-level status of an experiment."""
        return self._api_client._request("GET", f"api/experiments/{exp_id}/query/status")

    def get_neuron(self, exp_id: str, neuron_id: int) -> Dict[str, Any]:
        """Retrieves a single neuron by its ID."""
        return self._api_client._request("GET", f"api/experiments/{exp_id}/query/neurons/{neuron_id}")

    def get_neurons(self, exp_id: str, page: int = 1, page_size: int = 100) -> List[Dict[str, Any]]:
        """Retrieves a paginated list of all neurons in the experiment."""
        params = {"page": page, "pageSize": page_size}
        return self._api_client._request("GET", f"api/experiments/{exp_id}/query/neurons", params=params)

    def get_neighbors(self, exp_id: str, center_id: int, radius: float) -> List[Dict[str, Any]]:
        """Finds all neurons within a given radius of a center neuron."""
        params = {"centerId": center_id, "radius": radius}
        return self._api_client._request("GET", f"api/experiments/{exp_id}/query/neighbors", params=params)

    def get_synapse(self, exp_id: str, synapse_id: int) -> Dict[str, Any]:
        """Retrieves a single synapse by its ID."""
        return self._api_client._request("GET", f"api/experiments/{exp_id}/query/synapses/{synapse_id}")

    def get_synapses(self, exp_id: str, page: int = 1, page_size: int = 100) -> List[Dict[str, Any]]:
        """Retrieves a paginated list of all synapses in the experiment."""
        params = {"page": page, "pageSize": page_size}
        return self._api_client._request("GET", f"api/experiments/{exp_id}/query/synapses", params=params)

    def get_events_for_tick(self, exp_id: str, tick: int) -> List[Dict[str, Any]]:
        """Retrieves all events that were processed at a specific tick."""
        params = {"tick": tick}
        return self._api_client._request("GET", f"api/experiments/{exp_id}/query/events", params=params)

    def get_input_nodes(self, exp_id: str) -> List[Dict[str, Any]]:
        """Retrieves all configured input nodes for the experiment."""
        return self._api_client._request("GET", f"api/experiments/{exp_id}/query/inputs")

    def get_output_nodes(self, exp_id: str) -> List[Dict[str, Any]]:
        """Retrieves all configured output nodes for the experiment."""
        return self._api_client._request("GET", f"api/experiments/{exp_id}/query/outputs")

    def get_global_hormones(self, exp_id: str) -> List[float]:
        """Retrieves the current values of all global hormones as a list."""
        return self._api_client._request("GET", f"api/experiments/{exp_id}/query/hormones")

    def get_visualization_snapshot(self, exp_id: str) -> Dict[str, Any]:
        """
        Retrieves a complete snapshot of the world's state for visualization.
        """
        return self._api_client._request("GET", f"api/experiments/{exp_id}/query/visualize")

    # --- Log Endpoints ---

    def get_logs(self, exp_id: str, level: Optional[str] = None, tag: Optional[str] = None) -> List[Dict[str, Any]]:
        """
        Retrieves experiment logs as structured JSON objects (Timestamp, Level, Tag, Message).
        
        Args:
            exp_id (str): The ID of the experiment.
            level (str, optional): The minimum log level to retrieve (e.g., 'Info', 'Warning').
            tag (str, optional): A specific tag to filter logs by.
        """
        params = {}
        if level:
            params["level"] = level
        if tag:
            params["tag"] = tag
        return self._api_client._request("GET", f"api/experiments/{exp_id}/query/logs", params=params)

    def get_logs_as_text(self, exp_id: str, level: Optional[str] = None, tag: Optional[str] = None) -> str:
        """
        Retrieves experiment logs as a single plain text string.
        """
        params = {}
        if level:
            params["level"] = level
        if tag:
            params["tag"] = tag
        return self._api_client._request_text("GET", f"api/experiments/{exp_id}/query/logs/text", params=params)