# hidra_api_client/hgl.py

from typing import Dict, Any, TYPE_CHECKING

# Use a forward reference to avoid circular imports at runtime
if TYPE_CHECKING:
    from .core import HidraApiClient

class HglClient:
    """
    Provides methods for the HglController endpoints.
    
    This client allows for retrieving the Hidra Genesis Language (HGL) specification.
    """
    def __init__(self, api_client: 'HidraApiClient'):
        self._api_client = api_client

    def get_specification(self) -> Dict[str, Any]:
        """
        Gets the complete HGL specification, including all instructions, operators,
        API functions, and system variable names.
        """
        return self._api_client._request("GET", "api/hgl/specification")