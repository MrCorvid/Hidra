# brain_renderers/base_renderer.py
from abc import ABC, abstractmethod
from PySide6.QtGui import QPainter
from PySide6.QtCore import QRect

class BaseBrainRenderer(ABC):
    """
    Abstract base class for all 2D brain renderers.
    It defines the contract for rendering different brain types.
    """

    @property
    @abstractmethod
    def supports_animation(self) -> bool:
        """
        Returns True if this renderer can provide a visual animation/simulation.
        """
        pass

    @abstractmethod
    def get_brain_type(self) -> str:
        """
        Returns the specific brain type string this renderer handles.
        This must match the 'Type' field from the VisualizationBrainDto in the API.
        Example: "NeuralNetworkBrain"
        """
        pass

    @abstractmethod
    def render(self, painter: QPainter, brain_data: dict, rect: QRect, animation_state: dict | None = None):
        """
        Performs the actual drawing onto the QPainter's device.

        Args:
            painter: The QPainter instance to use for drawing.
            brain_data: The specific 'Data' dictionary for the brain type.
            rect: The bounding rectangle of the widget, for layout calculations.
            animation_state: Optional dictionary containing state for visual effects.
        """
        pass

    @abstractmethod
    def prepare_animation_state(self, brain_data: dict, initial_value: float) -> dict | None:
        """
        Creates the initial state for a firing animation based on the brain's data.

        Args:
            brain_data: The specific 'Data' dictionary for the brain type.
            initial_value: The input value for the simulation.

        Returns:
            A dictionary representing the starting state of the animation, or None.
        """
        pass

    @abstractmethod
    def advance_animation_state(self, state: dict) -> dict | None:
        """
        Calculates the next step of the animation.

        Args:
            state: The current animation state dictionary.

        Returns:
            The updated state dictionary, or None if the animation is complete.
        """
        pass