# connection_dialog.py
from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QLabel, QLineEdit,
    QPushButton, QFileDialog, QDialogButtonBox, QRadioButton, QButtonGroup
)
from PySide6.QtCore import Qt

class ConnectionDialog(QDialog):
    """
    A modal dialog to handle both connecting to a live server
    and loading an offline replay file.
    """
    def __init__(self, parent=None, last_url="http://localhost:5000"):
        super().__init__(parent)
        self.setWindowTitle("Connect to Source")
        self.setMinimumWidth(400)
        self.setModal(True)

        self.connection_details = None

        # --- Widgets ---
        self.url_input = QLineEdit(last_url)
        self.url_input.setPlaceholderText("Enter Hidra API server URL")
        
        self.connect_button = QPushButton("Connect to Server")
        self.connect_button.clicked.connect(self._on_connect)
        
        self.load_file_button = QPushButton("Load Replay From File...")
        self.load_file_button.clicked.connect(self._on_load_file)

        # --- Logging Level Radio Buttons ---
        self.log_level_group = QButtonGroup(self)
        self.radio_info = QRadioButton("Info")
        self.radio_debug = QRadioButton("Debug")
        self.radio_trace = QRadioButton("Trace")
        self.radio_info.setChecked(True) # Default level
        
        self.log_level_group.addButton(self.radio_info)
        self.log_level_group.addButton(self.radio_debug)
        self.log_level_group.addButton(self.radio_trace)
        
        # Use a standard button box for cancellation
        button_box = QDialogButtonBox(QDialogButtonBox.StandardButton.Cancel)
        button_box.rejected.connect(self.reject)

        # --- Layout ---
        layout = QVBoxLayout(self)
        layout.addWidget(QLabel("Live Connection:"))
        
        connect_layout = QHBoxLayout()
        connect_layout.addWidget(self.url_input)
        connect_layout.addWidget(self.connect_button)
        layout.addLayout(connect_layout)

        # Add logging controls to the layout
        log_level_layout = QHBoxLayout()
        log_level_layout.addWidget(QLabel("Initial Log Level:"))
        log_level_layout.addWidget(self.radio_info)
        log_level_layout.addWidget(self.radio_debug)
        log_level_layout.addWidget(self.radio_trace)
        log_level_layout.addStretch()
        layout.addLayout(log_level_layout)

        layout.addSpacing(15)
        
        or_separator = QLabel("— OR —")
        or_separator.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(or_separator)

        layout.addSpacing(15)

        layout.addWidget(QLabel("Offline Replay:"))
        layout.addWidget(self.load_file_button)
        
        layout.addSpacing(20)
        layout.addWidget(button_box)

        self.url_input.setFocus()

    def _on_connect(self):
        """Accepts the dialog and sets the connection details for a live connection."""
        url = self.url_input.text().strip()
        if url:
            # Get the selected log level from the checked radio button
            log_level = self.log_level_group.checkedButton().text()
            
            self.connection_details = {
                "type": "CONNECT", 
                "url": url,
                "log_level": log_level # Add the selected log level
            }
            self.accept()

    def _on_load_file(self):
        """
        Opens a file dialog, and if a file is selected, accepts the dialog
        and sets the connection details for loading a replay.
        """
        file_path, _ = QFileDialog.getOpenFileName(
            self,
            "Load Hidra Replay File",
            "", # Start directory
            "JSON Files (*.json);;All Files (*)"
        )
        if file_path:
            self.connection_details = {"type": "LOAD_FILE", "path": file_path}
            self.accept()