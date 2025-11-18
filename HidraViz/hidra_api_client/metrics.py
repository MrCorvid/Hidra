# hidra_api_client/metrics.py

from typing import Dict, List, Any, TYPE_CHECKING

# Use a forward reference to avoid circular imports at runtime
if TYPE_CHECKING:
    from .core import HidraApiClient

class MetricsClient:
    """
    Provides methods for the MetricsController endpoints.
    
    This client allows for retrieving performance and state metrics from
    a specific experiment's world.
    """
    def __init__(self, api_client: 'HidraApiClient'):
        self._api_client = api_client

    def get_latest(self, exp_id: str) -> Dict[str, Any]:
        """Retrieves the most recent tick metrics for an experiment."""
        return self._api_client._request("GET", f"api/experiments/{exp_id}/metrics/latest")

    def get_timeseries(self, exp_id: str, max_count: int = 256) -> List[Dict[str, Any]]:
        """
        Retrieves a time series of recent tick metrics as a list of JSON objects.
        
        Args:
            exp_id (str): The ID of the experiment.
            max_count (int): The maximum number of historical metric snapshots to return.
        """
        params = {"maxCount": max_count}
        return self._api_client._request("GET", f"api/experiments/{exp_id}/metrics/timeseries", params=params)

    def get_timeseries_as_csv(self, exp_id: str, max_count: int = 4096) -> str:
        """
        Retrieves a time series of recent tick metrics as a CSV formatted string.
        
        Args:
            exp_id (str): The ID of the experiment.
            max_count (int): The maximum number of historical metric snapshots to include.
        """
        params = {"maxCount": max_count}
        return self._api_client._request_text("GET", f"api/experiments/{exp_id}/metrics/timeseries/csv", params=params)