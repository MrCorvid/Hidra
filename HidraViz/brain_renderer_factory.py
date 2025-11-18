# brain_renderer_factory.py
import os
import importlib
import inspect
from typing import Dict
from brain_renderers.base_renderer import BaseBrainRenderer

# --- Caches to avoid re-importing and re-inspecting on every call ---
_renderer_instances: Dict[str, BaseBrainRenderer] | None = None

def get_renderers() -> Dict[str, BaseBrainRenderer]:
    """
    Discovers, imports, and instantiates all brain renderer classes.
    Results are cached for performance.

    Returns:
        A dictionary mapping brain type names to their renderer instances.
    """
    global _renderer_instances
    if _renderer_instances is not None:
        return _renderer_instances

    print("INFO: Discovering 2D brain renderers...")
    _renderer_instances = {}
    renderer_dir = os.path.dirname(__file__) + '/brain_renderers'
    
    for filename in os.listdir(renderer_dir):
        if filename.endswith('.py') and not filename.startswith('__') and not filename.startswith('base_'):
            module_name = f"brain_renderers.{filename[:-3]}"
            try:
                module = importlib.import_module(module_name)
                
                # Find all classes in the module that are subclasses of BaseBrainRenderer
                for name, obj in inspect.getmembers(module, inspect.isclass):
                    if issubclass(obj, BaseBrainRenderer) and obj is not BaseBrainRenderer:
                        instance = obj()
                        brain_type = instance.get_brain_type()
                        if brain_type in _renderer_instances:
                            print(f"WARNING: Duplicate renderer for brain type '{brain_type}'. Overwriting.")
                        
                        _renderer_instances[brain_type] = instance
                        print(f"  -> Found and registered renderer for '{brain_type}'")

            except Exception as e:
                print(f"ERROR: Could not load renderer from {module_name}: {e}")

    return _renderer_instances