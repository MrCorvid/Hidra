# hidra_api_client/logging.py

from typing import Dict, TYPE_CHECKING

# Use a forward reference to avoid circular imports at runtime
if TYPE_CHECKING:
    from .core import HidraApiClient

class LoggingClient:
    """
    Provides methods for interacting with the LoggingController endpoints.

    This client allows for dynamic control over the logging verbosity
    of a specific, running experiment.
    """
    def __init__(self, api_client: 'HidraApiClient'):
        """
        Initializes the LoggingClient.

        Args:
            api_client: An instance of the main HidraApiClient.
        """
        self._api_client = api_client

    def set_minimum_log_level(self, exp_id: str, minimum_level: str) -> Dict[str, str]:
        """
        Sets the minimum log level for a specific experiment.

        Any log messages with a severity below this level will be filtered out
        and not stored in the experiment's in-memory log history.

        Args:
            exp_id (str): The unique identifier of the experiment.
            minimum_level (str): The desired minimum log level.
                Valid values (from most to least verbose):
                "Trace", "Debug", "Info", "Warning", "Error", "Fatal".

        Returns:
            A dictionary containing the API response message.
        """
        if not exp_id:
            raise ValueError("Experiment ID cannot be empty.")
            
        valid_levels = {"trace", "debug", "info", "warning", "error", "fatal"}
        if minimum_level.lower() not in valid_levels:
            raise ValueError(f"Invalid log level '{minimum_level}'. Must be one of {valid_levels}.")

        endpoint = f"api/experiments/{exp_id}/logging/level"
        # Matches C# SetLogLevelRequestDto { MinimumLevel } via camelCase serialization
        payload = {"minimumLevel": minimum_level}
        
        return self._api_client._request("PUT", endpoint, json=payload)