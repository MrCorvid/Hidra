# brain_renderers/logic_gate_renderer.py
from PySide6.QtGui import QPainter, QColor, QPen, QFont
from PySide6.QtCore import QRect, Qt
from .base_renderer import BaseBrainRenderer
import operator
from functools import reduce

class LogicGateRenderer(BaseBrainRenderer):
    """Renders a LogicGateBrain and its instantaneous logic animation."""
    
    GATE_TYPE_MAP = { 0: "Buffer", 1: "NOT", 2: "AND", 3: "OR", 4: "NAND", 5: "NOR", 6: "XOR", 7: "XNOR" }
    
    @property
    def supports_animation(self) -> bool:
        # Flip-flops have state and are too complex for a simple pulse animation for now
        return True

    def get_brain_type(self) -> str:
        return "LogicGateBrain"

    def _evaluate_combinational(self, gate_type_int, inputs, threshold):
        """Helper to perform the logic gate calculation for the animation."""
        b_inputs = [i >= threshold for i in inputs]
        
        if not b_inputs: return False
        
        gate_type_name = self.GATE_TYPE_MAP.get(gate_type_int, "Buffer")

        if len(b_inputs) == 1:
            return not b_inputs[0] if gate_type_name in ["NOT", "NAND", "NOR"] else b_inputs[0]

        if gate_type_name == "AND": return all(b_inputs)
        if gate_type_name == "OR": return any(b_inputs)
        if gate_type_name == "NAND": return not all(b_inputs)
        if gate_type_name == "NOR": return not any(b_inputs)
        if gate_type_name == "XOR": return reduce(operator.xor, b_inputs)
        if gate_type_name == "XNOR": return not reduce(operator.xor, b_inputs)
        return b_inputs[0] # Default to Buffer

    def prepare_animation_state(self, brain_data: dict, initial_value: float) -> dict | None:
        if brain_data.get('flipFlop') is not None: return None # Animation not supported for flip-flops yet
        
        gate_type = brain_data.get('gateType', 2)
        threshold = brain_data.get('threshold', 0.5)
        # For simplicity, we assume the single input value is applied to all inputs of the gate
        num_inputs = 2 # A reasonable default for visualization
        inputs = [initial_value] * num_inputs
        
        output_bool = self._evaluate_combinational(gate_type, inputs, threshold)
        
        return {'active': True, 'output': 1.0 if output_bool else 0.0}

    def advance_animation_state(self, state: dict) -> dict | None:
        """Logic gates are instantaneous; animation finishes in one step."""
        return None

    def render(self, painter: QPainter, data: dict, rect: QRect, animation_state: dict | None = None):
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        
        is_ff = data.get('flipFlop') is not None
        gate_type_int = data.get('gateType', 2)
        label = "Flip-Flop" if is_ff else self.GATE_TYPE_MAP.get(gate_type_int, "Unknown")

        gate_rect = QRect(0, 0, 120, 80)
        gate_rect.moveCenter(rect.center())
        
        painter.setBrush(QColor(80, 80, 110))
        painter.setPen(QPen(QColor(200, 200, 220), 2))
        painter.drawRoundedRect(gate_rect, 10, 10)

        font = QFont("Arial", 14, QFont.Weight.Bold)
        painter.setFont(font)
        painter.setPen(QColor(255, 255, 255))
        painter.drawText(gate_rect, Qt.AlignmentFlag.AlignCenter, label)
        
        # --- I/O Lines ---
        input_pen = QPen(QColor(150, 150, 180), 2)
        output_pen = QPen(QColor(150, 150, 180), 2)
        
        # --- Animation Highlighting ---
        if animation_state and animation_state.get('active'):
            input_pen = QPen(QColor(255, 255, 0), 3) # Highlight inputs
            output_value = animation_state.get('output', 0.0)
            output_color = QColor(100, 255, 100) if output_value > 0.5 else QColor(255, 100, 100)
            output_pen = QPen(output_color, 3)

        painter.setPen(input_pen)
        painter.drawLine(gate_rect.left() - 20, gate_rect.center().y() - 15, gate_rect.left(), gate_rect.center().y() - 15)
        painter.drawLine(gate_rect.left() - 20, gate_rect.center().y() + 15, gate_rect.left(), gate_rect.center().y() + 15)
        painter.setPen(output_pen)
        painter.drawLine(gate_rect.right(), gate_rect.center().y(), gate_rect.right() + 20, gate_rect.center().y())