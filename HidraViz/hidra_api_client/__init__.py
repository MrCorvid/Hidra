# hidra_api_client/__init__.py

"""
A Python client for the Hidra Restful API.

This package provides a simple and structured interface for interacting with
all controllers of the Hidra API.
"""

__version__ = "1.0.0"

# Import the main client and the custom exception from the core module.
# This makes them directly accessible when the package is imported, allowing for:
# from hidra_api_client import HidraApiClient, HidraApiException
from .core import HidraApiClient, HidraApiException