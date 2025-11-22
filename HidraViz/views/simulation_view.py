# views/simulation_view.py
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QSplitter, 
    QTextEdit, QTabWidget, QGroupBox, QLabel, QScrollArea, QSizePolicy
)
from PySide6.QtCore import Qt
from PySide6.QtGui import QAction

from renderer import Renderer3D
from brain_renderer_2d import BrainRenderer2D
from controls_panel import ControlsPanel

class SimulationView(QWidget):
    def __init__(self, main_window):
        super().__init__(main_window)
        self.main_window = main_window
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        
        # Main Splitter (Left: 3D/Logs, Right: Controls/Details)
        self.main_splitter = QSplitter(Qt.Orientation.Horizontal)
        layout.addWidget(self.main_splitter)

        # ============================================================
        # LEFT COLUMN (3D World + System Logs)
        # ============================================================
        left_widget = QWidget()
        left_layout = QVBoxLayout(left_widget)
        left_layout.setContentsMargins(0, 0, 0, 0)
        left_layout.setSpacing(0)
        
        # 1. 3D Renderer
        self.renderer_3d = Renderer3D(self)
        left_layout.addWidget(self.renderer_3d, stretch=1)
        
        # 2. System Logs (Bottom of Left Column)
        self.log_widget = QTextEdit()
        self.log_widget.setReadOnly(True)
        self.log_widget.setMaximumHeight(150)
        self.log_widget.setPlaceholderText("System logs will appear here...")
        left_layout.addWidget(self.log_widget)
        
        self.main_splitter.addWidget(left_widget)

        # ============================================================
        # RIGHT COLUMN (Controls + Inspection)
        # ============================================================
        right_widget = QWidget()
        right_layout = QVBoxLayout(right_widget)
        right_layout.setContentsMargins(0, 0, 0, 0)
        
        # Vertical Splitter to separate Controls (Top) from Inspection (Bottom)
        self.right_splitter = QSplitter(Qt.Orientation.Vertical)
        right_layout.addWidget(self.right_splitter)

        # --- Top: Controls Panel ---
        self.controls_panel = ControlsPanel(self)
        self.right_splitter.addWidget(self.controls_panel)

        # --- Bottom: Inspection Tabs ---
        self.inspection_tabs = QTabWidget()
        
        # Tab 1: Brain Inspector (2D Neural View)
        self.brain_renderer_2d = BrainRenderer2D(self)
        self.inspection_tabs.addTab(self.brain_renderer_2d, "Brain Inspector")
        
        # Tab 2: Frame Details & Events
        details_scroll = QScrollArea()
        details_scroll.setWidgetResizable(True)
        self.details_content = QLabel("No Selection")
        self.details_content.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft)
        self.details_content.setWordWrap(True)
        self.details_content.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        self.details_content.setMargin(10)
        details_scroll.setWidget(self.details_content)
        self.inspection_tabs.addTab(details_scroll, "Frame Details")

        self.right_splitter.addWidget(self.inspection_tabs)
        
        self.main_splitter.addWidget(right_widget)
        
        # ============================================================
        # Initial Layout Sizes
        # ============================================================
        # Horizontal Split (Left vs Right)
        self.main_splitter.setSizes([900, 450])
        
        # Vertical Split (Controls vs Inspection)
        # Give Controls more space initially (600px vs 300px)
        self.right_splitter.setSizes([600, 300])

    def append_log(self, text):
        self.log_widget.append(text)

    def clear_logs(self):
        self.log_widget.clear()

    def update_details(self, frame):
        """Updates the 'Frame Details' tab text."""
        snap = frame.snapshot
        
        # Build HTML formatted text
        txt = (f"<h3>Tick: {frame.tick}</h3>"
               f"<b>Neurons:</b> {len(snap.get('neurons', []))}<br>"
               f"<b>Synapses:</b> {len(snap.get('synapses', []))}<br>"
               f"<b>Events Processed:</b> {len(frame.events)}<hr>")
        
        if not frame.events:
            txt += "<i>No events this tick.</i>"
        else:
            txt += "<b>Event Log:</b><br>"
            for i, evt in enumerate(frame.events):
                # Cap long event lists for performance
                if i >= 50: 
                    txt += f"<i>... and {len(frame.events) - 50} more</i>"
                    break
                
                evt_type = evt.get('type', 'Unknown')
                target = evt.get('targetId', 'N/A')
                
                # Simplified detail string
                detail_str = ""
                if evt_type == "Activate":
                    val = evt.get('payload', {}).get('currentValue', 0.0)
                    detail_str = f" (Val: {val:.2f})"
                elif evt_type == "ExecuteGene":
                    gene_idx = evt.get('payload', {}).get('geneIndex', -1)
                    detail_str = f" (Gene: {gene_idx})"
                    
                txt += f"<small>[{i}] <b>{evt_type}</b> -> Target {target}{detail_str}</small><br>"
            
        self.details_content.setText(txt)

    def get_view_menu_actions(self):
        """Returns QActions specific to this view for the View menu."""
        return []