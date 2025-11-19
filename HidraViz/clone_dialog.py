# clone_dialog.py
from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QLabel, QLineEdit, QSpinBox,
    QDialogButtonBox, QFormLayout
)
from PySide6.QtCore import Qt

class CloneDialog(QDialog):
    def __init__(self, current_tick: int, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Clone Experiment")
        self.setModal(True)
        self.setMinimumWidth(350)

        self.new_name = ""
        self.target_tick = current_tick

        layout = QVBoxLayout(self)
        form_layout = QFormLayout()

        self.name_input = QLineEdit("cloned-experiment")
        self.tick_input = QSpinBox()
        self.tick_input.setRange(0, 999999999)
        self.tick_input.setValue(current_tick)
        self.tick_input.setToolTip("The simulation will include history up to this tick.")

        form_layout.addRow("New Experiment Name:", self.name_input)
        form_layout.addRow("Clone From Tick:", self.tick_input)

        layout.addLayout(form_layout)
        
        info_label = QLabel(
            "Cloning creates a deep copy of the experiment state at the specified tick.\n"
            "All history AFTER this tick will be discarded in the new experiment."
        )
        info_label.setWordWrap(True)
        info_label.setStyleSheet("color: #aaa; font-style: italic;")
        layout.addWidget(info_label)

        buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        buttons.accepted.connect(self._on_accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

    def _on_accept(self):
        self.new_name = self.name_input.text().strip()
        self.target_tick = self.tick_input.value()
        if not self.new_name:
            self.new_name = "cloned-experiment"
        self.accept()

    def get_data(self):
        return self.new_name, self.target_tick