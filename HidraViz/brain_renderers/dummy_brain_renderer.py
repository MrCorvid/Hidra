# brain_renderers/dummy_brain_renderer.py
from PySide6.QtGui import QPainter, QColor, QPen, QFont
from PySide6.QtCore import QRect, Qt, QPoint
from .base_renderer import BaseBrainRenderer

class DummyBrainRenderer(BaseBrainRenderer):
    """Renders a DummyBrain and its multi-step pass-through animation."""

    @property
    def supports_animation(self) -> bool:
        return True

    def get_brain_type(self) -> str:
        return "DummyBrain"

    def prepare_animation_state(self, brain_data: dict, initial_value: float) -> dict | None:
        """Initializes the state for the 5-step animation."""
        return {
            'step': -1,                  # Start before the first step
            'value': initial_value,
            'activated_parts': set(),    # A set of keys like 'input_node', 'body', etc.
        }

    def advance_animation_state(self, state: dict) -> dict | None:
        """Progresses the animation by one step, following the user's storyboard."""
        state['step'] += 1
        step = state['step']

        if step == 0: state['activated_parts'].add('input_node')
        elif step == 1: state['activated_parts'].add('input_connection')
        elif step == 2: state['activated_parts'].add('body')
        elif step == 3: state['activated_parts'].add('output_connection')
        elif step == 4: state['activated_parts'].add('output_node')
        else: return None  # Animation is done

        return state

    def render(self, painter: QPainter, data: dict, rect: QRect, animation_state: dict | None = None):
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        
        activated = animation_state['activated_parts'] if animation_state else set()

        # --- 1. Define Layout Positions ---
        y_center = rect.center().y()
        input_pos = QPoint(rect.left() + 40, y_center)
        body_center_x = rect.center().x()
        output_pos = QPoint(rect.right() - 40, y_center)
        
        body_rect = QRect(0, 0, 100, 70)
        body_rect.moveCenter(QPoint(body_center_x, y_center))

        node_radius = 15
        
        # --- 2. Draw Connections (so they are behind nodes) ---
        # Default style
        painter.setPen(QPen(QColor(100, 100, 120), 2))
        
        # Highlighted style
        if 'input_connection' in activated:
            painter.setPen(QPen(QColor(255, 255, 0), 3))
        painter.drawLine(input_pos.x() + node_radius, y_center, body_rect.left(), y_center)

        painter.setPen(QPen(QColor(100, 100, 120), 2))
        if 'output_connection' in activated:
            painter.setPen(QPen(QColor(255, 255, 0), 3))
        painter.drawLine(body_rect.right(), y_center, output_pos.x() - node_radius, y_center)

        # --- 3. Draw Main Body ---
        painter.setBrush(QColor(90, 90, 90))
        painter.setPen(QPen(QColor(180, 180, 180), 2))
        if 'body' in activated:
            painter.setPen(QPen(QColor(255, 255, 100), 3))
        painter.drawRoundedRect(body_rect, 10, 10)
        
        font = QFont("Arial", 11, QFont.Weight.Bold)
        painter.setFont(font)
        painter.setPen(QColor(220, 220, 220))
        painter.drawText(body_rect, Qt.AlignmentFlag.AlignCenter, "Pass-Through")

        # --- 4. Draw I/O Nodes ---
        def draw_node(pos, label, is_active, value_to_show=None):
            painter.setBrush(QColor(60, 60, 90))
            painter.setPen(QPen(QColor(150, 150, 180), 2))
            if is_active:
                painter.setBrush(QColor(255, 255, 100)) # Bright yellow highlight
                painter.setPen(QPen(QColor(255, 255, 255), 2))
            
            painter.drawEllipse(pos, node_radius, node_radius)
            painter.setPen(QColor(220, 220, 220))
            font.setPointSize(9)
            painter.setFont(font)
            painter.drawText(QRect(pos.x() - node_radius, pos.y() - node_radius, node_radius*2, node_radius*2), Qt.AlignmentFlag.AlignCenter, label)

            if is_active and value_to_show is not None:
                font.setPointSize(8)
                painter.setFont(font)
                value_text = f"{value_to_show:.2f}"
                painter.drawText(QRect(pos.x() - 20, pos.y() + node_radius, 40, 20), Qt.AlignmentFlag.AlignCenter, value_text)

        input_val = animation_state['value'] if animation_state else 0.0
        draw_node(input_pos, "IN", 'input_node' in activated, input_val)
        draw_node(output_pos, "OUT", 'output_node' in activated, input_val)