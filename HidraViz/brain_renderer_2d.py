# brain_renderer_2d.py
import math
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QLineEdit, QLabel
)
from PySide6.QtGui import QPainter, QColor, QFont
from PySide6.QtCore import Qt, QTimer

from brain_renderer_factory import get_renderers

class _DrawingCanvas(QWidget):
    """Inner widget dedicated to the painting process."""
    def __init__(self, parent):
        super().__init__(parent)
        self.main_renderer_widget = parent
        self.brain_data = None
    
    def update_data(self, brain_data):
        self.brain_data = brain_data
        self.update()

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        painter.fillRect(self.rect(), QColor(20, 20, 30))
        painter.setFont(QFont("Arial", 10))
        
        if not self.brain_data:
            painter.setPen(QColor(200, 200, 200))
            painter.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, "N/A\n(Select a neuron in the 3D view)")
            painter.end()
            return
            
        brain_type = self.brain_data.get("type")
        specific_renderer = self.main_renderer_widget.renderers.get(brain_type)
        
        if specific_renderer:
            specific_data = self.brain_data.get("data", {})
            try:
                specific_renderer.render(painter, specific_data, self.rect(), self.main_renderer_widget.animation_state)
            except Exception as e:
                print(f"ERROR: Renderer for '{brain_type}' crashed: {e}")
                painter.setPen(QColor(255, 100, 100))
                painter.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, f"Renderer Error for\n{brain_type}")
        else:
            painter.setPen(QColor(255, 255, 100))
            message = f"2D renderer not found for\nbrain type: '{brain_type}'"
            painter.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, message)
        painter.end()

class BrainRenderer2D(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.renderers = get_renderers()
        self.animation_timer = QTimer(self)
        self.animation_timer.timeout.connect(self._animation_step)
        # --- START: CHANGE ---
        self.animation_timer.setInterval(1000) # One tick per second
        # --- END: CHANGE ---
        self.animation_state = None
        
        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(0, 0, 0, 0)
        self.main_layout.setSpacing(4)

        self.controls_widget = QWidget()
        controls_layout = QHBoxLayout(self.controls_widget)
        controls_layout.setContentsMargins(2, 2, 2, 2)
        self.value_input = QLineEdit("1.0")
        self.fire_button = QPushButton("⚡ Fire Pulse")
        self.fire_button.clicked.connect(self._on_fire_pulse)
        controls_layout.addWidget(QLabel("Input:"))
        controls_layout.addWidget(self.value_input)
        controls_layout.addWidget(self.fire_button)
        self.controls_widget.setVisible(False)

        self.canvas = _DrawingCanvas(self)
        self.main_layout.addWidget(self.controls_widget)
        self.main_layout.addWidget(self.canvas)
        self.setMinimumSize(380, 200)

    def _get_active_renderer(self):
        """Helper to get the renderer instance for the currently displayed brain."""
        if self.canvas.brain_data:
            brain_type = self.canvas.brain_data.get("type")
            return self.renderers.get(brain_type)
        return None

    def update_data(self, brain_data: dict | None):
        self._stop_animation()
        self.canvas.update_data(brain_data)
        
        active_renderer = self._get_active_renderer()
        can_fire = active_renderer.supports_animation if active_renderer else False
        self.controls_widget.setVisible(can_fire)

    def _stop_animation(self):
        if self.animation_timer.isActive(): self.animation_timer.stop()
        # Do not clear the state here, so highlights persist
        self.canvas.update()

    def _on_fire_pulse(self):
        self._stop_animation()
        self.animation_state = None # Clear previous state on a new fire
        self.canvas.update() # Repaint to clear old highlights immediately

        active_renderer = self._get_active_renderer()
        if not active_renderer or not self.canvas.brain_data: return

        try:
            value = float(self.value_input.text())
            brain_data = self.canvas.brain_data.get("data", {})
            self.animation_state = active_renderer.prepare_animation_state(brain_data, value)
            
            if self.animation_state:
                self.animation_timer.start()
                self._animation_step() # Trigger first step immediately
        except (ValueError, TypeError) as e:
            print(f"ERROR: Could not start animation. Invalid value or data. Details: {e}")

    def _animation_step(self):
        if not self.animation_state:
            self._stop_animation()
            return
            
        active_renderer = self._get_active_renderer()
        if not active_renderer:
            self._stop_animation()
            return

        self.animation_state = active_renderer.advance_animation_state(self.animation_state)
        self.canvas.update()
        
        if not self.animation_state:
            # Animation is complete, stop the timer but keep the final state for rendering
            self.animation_timer.stop()