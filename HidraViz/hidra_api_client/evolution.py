# hidra_api_client/evolution.py

from typing import Dict, Any, TYPE_CHECKING

# Use a forward reference to avoid circular imports at runtime
if TYPE_CHECKING:
    from .core import HidraApiClient

class EvolutionClient:
    """
    Provides methods for the EvolutionController endpoints.
    
    This client allows for managing evolutionary runs, including starting,
    stopping, monitoring status, loading specific generations into
    visualizable experiments, and exporting data.
    """
    def __init__(self, api_client: 'HidraApiClient'):
        self._api_client = api_client

    def start(self, config: Dict[str, Any]) -> Dict[str, Any]:
        """
        Starts a new evolutionary run.
        
        Args:
            config (Dict[str, Any]): The Master Configuration dictionary matching 
                                     EvolutionRunConfig in C# (including 'geneticAlgorithm', 
                                     'activity', 'organismConfig', etc.).
        
        Returns:
            Dict[str, Any]: The API response message.
            
        Raises:
            HidraApiException: If a run is already active (409 Conflict).
        """
        return self._api_client._request("POST", "api/evolution/start", json=config)

    def stop(self) -> Dict[str, Any]:
        """
        Stops the currently active evolutionary run.
        
        Returns:
            Dict[str, Any]: The API response message.
        """
        return self._api_client._request("POST", "api/evolution/stop")

    def get_status(self) -> Dict[str, Any]:
        """
        Gets the current status of the evolutionary service.
        
        Returns:
            Dict[str, Any]: A dictionary matching EvolutionStatusDto, containing:
                            - state (Idle, Running, Paused, Finished)
                            - currentGeneration, totalGenerations
                            - bestFitnessAllTime
                            - history (List[GenerationStats])
        """
        return self._api_client._request("GET", "api/evolution/status")

    def load_generation(self, gen_index: int) -> Dict[str, Any]:
        """
        Creates a new standard Experiment from the best organism of a specific generation.
        
        Args:
            gen_index (int): The index of the generation to load (e.g., 0 for Gen 0).
            
        Returns:
            Dict[str, Any]: A dictionary containing the new 'experimentId' and message.
        """
        return self._api_client._request("POST", f"api/evolution/load-generation/{gen_index}")

    def get_csv_export(self) -> str:
        """
        Downloads the full run history as a CSV formatted string.
        
        Returns:
            str: The CSV data containing generation statistics and genome lengths.
        """
        # Uses _request_text because the response content-type is text/csv, not application/json
        return self._api_client._request_text("GET", "api/evolution/export/csv")