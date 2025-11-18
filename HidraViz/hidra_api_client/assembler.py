# hidra_api_client/assembler.py

from typing import Dict, Any, TYPE_CHECKING

# Use a forward reference to avoid circular imports at runtime
if TYPE_CHECKING:
    from .core import HidraApiClient

class AssemblerClient:
    """
    Provides methods for the AssemblerController endpoints.
    
    This client allows for compiling HGL assembly language into bytecode and
    decompiling bytecode back into human-readable assembly.
    """
    def __init__(self, api_client: 'HidraApiClient'):
        self._api_client = api_client

    def assemble(self, source_code: str) -> Dict[str, str]:
        """
        Compiles a string of HGL assembly source into a hexadecimal bytecode string.
        
        Args:
            source_code (str): The HGL assembly source code to compile.
            
        Returns:
            A dictionary containing the compiled hex bytecode, e.g., {"hexBytecode": "C0010A..."}.
        """
        payload = {"sourceCode": source_code}
        return self._api_client._request("POST", "api/assembler/assemble", json=payload)

    def decompile(self, hex_bytecode: str) -> Dict[str, str]:
        """
        Decompiles a hexadecimal bytecode string back into human-readable HGL assembly source.
        
        Args:
            hex_bytecode (str): The hexadecimal bytecode string to decompile.
            
        Returns:
            A dictionary containing the decompiled HGL source code, e.g., {"sourceCode": "PUSH_BYTE 10\\n..."}.
        """
        payload = {"hexBytecode": hex_bytecode}
        return self._api_client._request("POST", "api/assembler/decompile", json=payload)