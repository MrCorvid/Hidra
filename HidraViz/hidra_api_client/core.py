# hidra_api_client/core.py

import requests
from typing import Any
from .run_control import RunControlClient
from .query import QueryClient
from .metrics import MetricsClient
from .manipulation import ManipulationClient
from .hgl import HglClient
from .experiments import ExperimentsClient
from .brain import BrainClient
from .assembler import AssemblerClient
from .logging import LoggingClient
from .evolution import EvolutionClient

class HidraApiException(Exception):
    """
    Custom exception for errors returned by the Hidra API.
    """
    def __init__(self, status_code: int, error_type: str, message: str):
        self.status_code = status_code
        self.error_type = error_type
        self.message = message
        super().__init__(f"[{status_code} {error_type}] {message}")


class HidraApiClient:
    """
    The main entry point for the Hidra API Python client.
    """
    def __init__(self, base_url: str = "http://localhost:5000"):
        """
        Initializes the API client.
        
        Args:
            base_url (str): The base URL of the Hidra API server.
        """
        if base_url.endswith('/'):
            base_url = base_url[:-1]
        self.base_url = base_url
        self.session = requests.Session()
        self.session.headers.update({
            "Content-Type": "application/json",
            "Accept": "application/json"
        })
        
        # --- Controller Clients ---
        self.experiments = ExperimentsClient(self)
        self.run_control = RunControlClient(self)
        self.query = QueryClient(self)
        self.metrics = MetricsClient(self)
        self.manipulation = ManipulationClient(self)
        self.brain = BrainClient(self)
        self.hgl = HglClient(self)
        self.assembler = AssemblerClient(self)
        self.logging = LoggingClient(self)
        self.evolution = EvolutionClient(self)
        
    def _request(self, method: str, path: str, **kwargs) -> Any:
        url = f"{self.base_url}/{path}"
        try:
            response = self.session.request(method, url, **kwargs)
            
            if not response.ok:
                try:
                    error_data = response.json()
                    
                    error_type = error_data.get("error")
                    message = error_data.get("message")
                    
                    # Fallback to ASP.NET Core ProblemDetails format
                    if error_type is None and "title" in error_data:
                        error_type = error_data.get("title", "ProblemError")
                    if message is None and "detail" in error_data:
                        message = error_data.get("detail", response.text)

                    if error_type is None:
                        error_type = "UnknownError"
                    if message is None:
                        message = response.text

                    raise HidraApiException(
                        status_code=response.status_code,
                        error_type=error_type,
                        message=message
                    )
                except requests.exceptions.JSONDecodeError:
                    response.raise_for_status()

            if response.status_code == 204:
                return None
            
            return response.json()

        except requests.exceptions.RequestException as e:
            raise HidraApiException(
                status_code=503,
                error_type="ConnectionError",
                message=f"Failed to connect to the API at {url}: {e}"
            ) from e

    def _request_text(self, method: str, path: str, **kwargs) -> str:
        url = f"{self.base_url}/{path}"
        try:
            headers = self.session.headers.copy()
            headers.pop("Content-Type", None)
            headers.pop("Accept", None)
            
            response = self.session.request(method, url, headers=headers, **kwargs)

            if not response.ok:
                try:
                    error_data = response.json()
                    error_type = error_data.get("error", error_data.get("title", "UnknownError"))
                    message = error_data.get("message", error_data.get("detail", response.text))
                    raise HidraApiException(
                        status_code=response.status_code,
                        error_type=error_type,
                        message=message
                    )
                except requests.exceptions.JSONDecodeError:
                    response.raise_for_status()
            
            return response.text

        except requests.exceptions.RequestException as e:
            raise HidraApiException(
                status_code=503,
                error_type="ConnectionError",
                message=f"Failed to connect to the API at {url}: {e}"
            ) from e