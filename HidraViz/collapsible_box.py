# collapsible_box.py
from PySide6.QtWidgets import QWidget, QToolButton, QVBoxLayout, QFrame
from PySide6.QtCore import Qt, Slot

class CollapsibleBox(QWidget):
    """
    A custom collapsible widget. It consists of a header button that shows or
    hides a content area.
    """
    def __init__(self, title="", parent=None, collapsed=False):
        super().__init__(parent)

        is_expanded = not collapsed
        self.toggle_button = QToolButton(text=title, checkable=True, checked=is_expanded)
        self.toggle_button.setStyleSheet("QToolButton { border: none; font-weight: bold; }")
        self.toggle_button.setToolButtonStyle(Qt.ToolButtonStyle.ToolButtonTextBesideIcon)
        
        # Set the initial arrow based on the initial state
        self.toggle_button.setArrowType(Qt.ArrowType.DownArrow if is_expanded else Qt.ArrowType.RightArrow)

        # --- START: CORRECTED SIGNAL CONNECTION ---
        # Use the 'toggled' signal, which is emitted AFTER the check state has changed.
        self.toggle_button.toggled.connect(self._on_toggled)
        # --- END: CORRECTED SIGNAL CONNECTION ---

        self.content_area = QFrame()
        self.content_area.setFrameShape(QFrame.Shape.StyledPanel)
        self.content_area.setFrameShadow(QFrame.Shadow.Sunken)
        self.content_area.setVisible(is_expanded)

        # The main layout for this CollapsibleBox widget
        main_layout = QVBoxLayout(self)
        main_layout.setSpacing(0)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.addWidget(self.toggle_button)
        main_layout.addWidget(self.content_area)

    # --- START: NEW SLOT FOR THE 'toggled' SIGNAL ---
    @Slot(bool)
    def _on_toggled(self, is_checked):
        """
        Handles the state change of the toggle button.
        'is_checked' is the NEW state provided by the signal.
        """
        self.toggle_button.setArrowType(Qt.ArrowType.DownArrow if is_checked else Qt.ArrowType.RightArrow)
        self.content_area.setVisible(is_checked)
    # --- END: NEW SLOT ---

    def setContentLayout(self, layout):
        """Sets the layout for the content area."""
        old_layout = self.content_area.layout()
        if old_layout is not None:
            while old_layout.count():
                item = old_layout.takeAt(0)
                widget = item.widget()
                if widget is not None:
                    widget.setParent(None)
            del old_layout

        self.content_area.setLayout(layout)