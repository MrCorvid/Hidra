# main_window.py
import sys
import queue
from typing import Any, Optional, Tuple
from PySide6.QtWidgets import (
    QMainWindow, QApplication, QWidget, QVBoxLayout, QHBoxLayout, QPushButton,
    QDockWidget, QTextEdit, QListWidget, QLabel, QLineEdit, QSplitter,
    QListWidgetItem, QMessageBox, QScrollArea, QFileDialog, QSlider, QComboBox,
    QSpinBox, QTabWidget, QFrame
)
from PySide6.QtCore import Qt, QThread, Slot, QTimer
from PySide6.QtGui import QAction, QFont, QActionGroup

from simulation_controller import ReplayFrame
from simulation_worker import SimulationWorker
from render_worker import RenderWorker, RenderPayload
from renderer import Renderer3D
from brain_renderer_2d import BrainRenderer2D
from connection_dialog import ConnectionDialog
from clone_dialog import CloneDialog
from collapsible_box import CollapsibleBox

class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("HidraViz - PySide6 Edition")
        self.setGeometry(100, 100, 1600, 900)

        self.selected_exp_id = None
        self.inspected_neuron_id: Optional[int] = None
        self.inspected_io_node: Optional[Tuple[str, int]] = None
        self.is_offline_mode = False
        self.current_display_tick = 0
        self.last_api_url = "http://localhost:5000"
        self.is_playing = False
        self.target_tick = None
        self.playback_speed_map = { "0.25x": 400, "0.5x": 200, "1x": 100, "1.5x": 66, "2x": 50 }
        self.staged_input_values = {}
        self.selected_input_id = None
        self.id_to_select_after_refresh = None
        self._temp_initial_log_level = "Info" 

        self.command_queue = queue.Queue()
        self.worker_thread = QThread()
        self.worker = SimulationWorker(self.command_queue)
        self.worker.moveToThread(self.worker_thread)
        
        self.render_command_queue = queue.Queue()
        self.render_worker_thread = QThread()
        self.render_worker = RenderWorker(self.render_command_queue)
        self.render_worker.moveToThread(self.render_worker_thread)

        self.playback_timer = QTimer(self)
        self.playback_timer.timeout.connect(self._on_playback_tick)

        self.worker_thread.started.connect(self.worker.run)
        self.worker.signals.status_update.connect(self.log_message)
        self.worker.signals.logs_updated.connect(self.on_server_logs_received)
        self.worker.signals.connection_result.connect(self.on_connection_result)
        self.worker.signals.replay_loaded.connect(self.on_replay_loaded)
        self.worker.signals.replay_saved.connect(self.on_replay_saved)
        self.worker.signals.assembly_result.connect(self.on_assembly_result)
        self.worker.signals.decompilation_result.connect(self.on_decompilation_result)
        self.worker.signals.live_status_update.connect(self.on_live_status_received)
        self.worker.signals.new_frame_data.connect(self.on_new_frame_data)
        self.worker.signals.experiment_list.connect(self.on_experiment_list_received)
        self.worker.signals.experiment_created.connect(self.on_experiment_created)
        self.worker.signals.experiment_deleted.connect(self.on_experiment_deleted)
        self.worker.signals.experiment_selected.connect(self.on_experiment_selected)

        self.render_worker_thread.started.connect(self.render_worker.run)
        self.render_worker.signals.render_ready.connect(self.on_render_ready)
        self.render_worker.signals.status_update.connect(self.log_message)

        self.worker_thread.start()
        self.render_worker_thread.start()

        self.setup_ui()
        self.renderer_3d.object_selected.connect(self.on_3d_object_selected)
        QTimer.singleShot(0, self.show_connection_dialog)

    def setup_ui(self):
        menu_bar = self.menuBar()
        file_menu = menu_bar.addMenu("&File")
        
        connect_action = QAction("Connect to Source...", self)
        connect_action.triggered.connect(self.show_connection_dialog)
        file_menu.addAction(connect_action)
        
        self.save_replay_action = QAction("Save Replay As...", self)
        self.save_replay_action.triggered.connect(self.save_replay)
        file_menu.addAction(self.save_replay_action)
        file_menu.addSeparator()

        self.logging_level_menu = file_menu.addMenu("&Logging Level")
        log_level_group = QActionGroup(self)
        log_level_group.setExclusive(True)
        
        log_levels = ["Trace", "Debug", "Info", "Warning", "Error", "Fatal"]
        for level in log_levels:
            action = QAction(level, self, checkable=True)
            self.logging_level_menu.addAction(action)
            log_level_group.addAction(action)
            if level == "Info": action.setChecked(True)
        
        log_level_group.triggered.connect(self.on_log_level_changed)
        self.logging_level_menu.setEnabled(False)

        view_menu = menu_bar.addMenu("&View")

        self.renderer_3d = Renderer3D()
        self.log_widget = QTextEdit()
        self.log_widget.setReadOnly(True)
        self.log_widget.setFont(QFont("Courier", 9))
        self.brain_renderer_2d = BrainRenderer2D()
        self.event_viewer_widget = QTextEdit()
        self.event_viewer_widget.setReadOnly(True)
        self.event_viewer_widget.setFont(QFont("Courier", 9))

        self.controls_panel = self._create_controls_panel()
        self.controls_panel.setEnabled(False)

        self.setDockNestingEnabled(True)

        self.dock_3d = QDockWidget("3D Visualizer", self)
        self.dock_3d.setWidget(self.renderer_3d)

        self.dock_controls = QDockWidget("Controls", self)
        self.dock_controls.setWidget(self.controls_panel)
        self.dock_controls.setMinimumWidth(400)

        self.dock_logs = QDockWidget("Logs", self)
        self.dock_logs.setWidget(self.log_widget)

        self.tab_widget = QTabWidget()
        self.tab_widget.addTab(self.brain_renderer_2d, "2D Brain View")
        self.tab_widget.addTab(self.event_viewer_widget, "Event Viewer")
        self.dock_details = QDockWidget("Details", self)
        self.dock_details.setWidget(self.tab_widget)
        
        self.addDockWidget(Qt.LeftDockWidgetArea, self.dock_3d)
        self.addDockWidget(Qt.RightDockWidgetArea, self.dock_controls)
        self.splitDockWidget(self.dock_controls, self.dock_details, Qt.Vertical)
        self.splitDockWidget(self.dock_3d, self.dock_logs, Qt.Vertical)
        
        view_menu.addAction(self.dock_3d.toggleViewAction())
        view_menu.addAction(self.dock_controls.toggleViewAction())
        view_menu.addAction(self.dock_details.toggleViewAction())
        view_menu.addAction(self.dock_logs.toggleViewAction())

        QTimer.singleShot(0, self.apply_initial_sizes)

    def apply_initial_sizes(self):
        self.resizeDocks(
            [self.dock_3d, self.dock_controls],
            [int(self.width() * 0.7), int(self.width() * 0.3)],
            Qt.Horizontal
        )
        self.resizeDocks(
            [self.dock_3d, self.dock_logs],
            [int(self.height() * 0.7), int(self.height() * 0.3)],
            Qt.Vertical
        )
        self.resizeDocks(
            [self.dock_controls, self.dock_details],
            [int(self.height() * 0.7), int(self.height() * 0.3)],
            Qt.Vertical
        )

    def _create_controls_panel(self):
        scroll_container = QWidget()
        main_layout = QVBoxLayout(scroll_container)
        main_layout.setContentsMargins(0, 0, 0, 0)
        scroll_area = QScrollArea()
        scroll_area.setWidgetResizable(True)
        main_layout.addWidget(scroll_area)
        content_widget = QWidget()
        scroll_area.setWidget(content_widget)
        content_layout = QVBoxLayout(content_widget)

        # --- Experiment Management Box ---
        self.exp_mgmt_box = CollapsibleBox("Experiment Management", collapsed=False)
        exp_mgmt_layout = QVBoxLayout()
        self.exp_mgmt_tabs = QTabWidget()

        # --- Tab 1: Active Experiments ---
        self.active_exp_tab = QWidget()
        active_layout = QVBoxLayout(self.active_exp_tab)
        self.exp_listbox = QListWidget()
        self.exp_listbox.currentItemChanged.connect(self.select_experiment)
        active_layout.addWidget(self.exp_listbox)
        
        exp_buttons_layout = QHBoxLayout()
        self.refresh_button = QPushButton("Refresh")
        self.refresh_button.clicked.connect(self.refresh_experiment_list)
        self.delete_exp_button = QPushButton("Delete")
        self.delete_exp_button.clicked.connect(self.delete_experiment)
        self.clone_exp_button = QPushButton("Clone")
        self.clone_exp_button.clicked.connect(self.clone_experiment)
        
        exp_buttons_layout.addWidget(self.refresh_button)
        exp_buttons_layout.addWidget(self.clone_exp_button)
        exp_buttons_layout.addWidget(self.delete_exp_button)
        active_layout.addLayout(exp_buttons_layout)
        self.exp_mgmt_tabs.addTab(self.active_exp_tab, "Active Experiments")

        # --- Tab 2: Create New ---
        self.create_exp_tab = QWidget()
        create_layout = QVBoxLayout(self.create_exp_tab)
        create_layout.addWidget(QLabel("Name:"))
        self.new_exp_name_input = QLineEdit("my-new-experiment")
        create_layout.addWidget(self.new_exp_name_input)
        create_layout.addWidget(QLabel("HGL Genome:"))
        self.new_exp_genome_input = QLineEdit("G")
        create_layout.addWidget(self.new_exp_genome_input)
        create_layout.addWidget(QLabel("Input IDs (e.g., 0, 1):"))
        self.new_exp_inputs_input = QLineEdit("0, 1")
        create_layout.addWidget(self.new_exp_inputs_input)
        create_layout.addWidget(QLabel("Output IDs (e.g., 10):"))
        self.new_exp_outputs_input = QLineEdit("10")
        create_layout.addWidget(self.new_exp_outputs_input)
        self.create_exp_button = QPushButton("Create Experiment")
        self.create_exp_button.clicked.connect(self.create_experiment)
        create_layout.addWidget(self.create_exp_button)
        create_layout.addStretch()
        self.exp_mgmt_tabs.addTab(self.create_exp_tab, "Create New")

        # --- Tab 3: HGL Tools ---
        self.hgl_tools_tab = QWidget()
        hgl_tools_layout = QVBoxLayout(self.hgl_tools_tab)
        self.hgl_sub_tabs = QTabWidget()

        self.assemble_tab = QWidget()
        assemble_layout = QVBoxLayout(self.assemble_tab)
        self.hgl_source_input = QTextEdit()
        self.hgl_source_input.setFont(QFont("Courier", 10))
        self.hgl_source_input.setPlaceholderText("HGL Assembly Source...")
        self.hgl_source_input.setPlainText("# Sample\nPUSH_BYTE 0\nCreateNeuron\nGN")
        assemble_layout.addWidget(self.hgl_source_input)
        self.assemble_button = QPushButton("Assemble")
        self.assemble_button.clicked.connect(self.assemble_hgl)
        assemble_layout.addWidget(self.assemble_button)
        self.hgl_sub_tabs.addTab(self.assemble_tab, "Assemble")

        self.decompile_tab = QWidget()
        decompile_layout = QVBoxLayout(self.decompile_tab)
        self.hgl_bytecode_input = QTextEdit()
        self.hgl_bytecode_input.setFont(QFont("Courier", 10))
        self.hgl_bytecode_input.setPlaceholderText("Hex Bytecode...")
        decompile_layout.addWidget(self.hgl_bytecode_input)
        self.decompile_button = QPushButton("Decompile")
        self.decompile_button.clicked.connect(self.decompile_hgl)
        decompile_layout.addWidget(self.decompile_button)
        self.hgl_sub_tabs.addTab(self.decompile_tab, "Decompile")

        hgl_tools_layout.addWidget(self.hgl_sub_tabs)
        self.exp_mgmt_tabs.addTab(self.hgl_tools_tab, "HGL Tools")

        exp_mgmt_layout.addWidget(self.exp_mgmt_tabs)
        self.exp_mgmt_box.setContentLayout(exp_mgmt_layout)
        content_layout.addWidget(self.exp_mgmt_box)

        # --- NEW PARENT CONTAINER: Simulation Controls ---
        self.controls_super_box = CollapsibleBox("Simulation Controls", collapsed=False)
        controls_super_layout = QVBoxLayout()

        # --- I/O Control Box (Child 1) ---
        self.io_box = CollapsibleBox("I/O Control", collapsed=False)
        io_layout = QVBoxLayout()
        io_layout.addWidget(QLabel("Input Staging:"))
        self.input_bubbles_layout = QHBoxLayout()
        self.input_bubbles_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)
        io_layout.addLayout(self.input_bubbles_layout)
        input_setter_layout = QHBoxLayout()
        self.selected_input_label = QLabel("Selected: None")
        self.input_value_edit = QLineEdit("0.0")
        self.input_value_edit.setPlaceholderText("Value")
        self.set_input_button = QPushButton("Set Value")
        self.set_input_button.clicked.connect(self.on_set_input_value)
        input_setter_layout.addWidget(self.selected_input_label)
        input_setter_layout.addWidget(self.input_value_edit)
        input_setter_layout.addWidget(self.set_input_button)
        io_layout.addLayout(input_setter_layout)
        input_clear_layout = QHBoxLayout()
        self.clear_selected_button = QPushButton("Clear Selected")
        self.clear_selected_button.clicked.connect(self.on_clear_selected_input)
        self.clear_all_button = QPushButton("Clear All Inputs")
        self.clear_all_button.clicked.connect(self.on_clear_all_inputs)
        input_clear_layout.addWidget(self.clear_selected_button)
        input_clear_layout.addWidget(self.clear_all_button)
        io_layout.addLayout(input_clear_layout)
        io_layout.addSpacing(15)
        io_layout.addWidget(QLabel("Output Readout (Current Tick):"))
        self.output_display = QTextEdit()
        self.output_display.setReadOnly(True)
        self.output_display.setFont(QFont("Courier", 9))
        self.output_display.setFixedHeight(100)
        self.output_display.setPlaceholderText("Output values will appear here...")
        io_layout.addWidget(self.output_display)
        self.io_box.setContentLayout(io_layout)
        controls_super_layout.addWidget(self.io_box) # Add to Super Layout

        # --- Playback Box (Child 2) ---
        self.playback_box = CollapsibleBox("Timeline & Playback", collapsed=False)
        playback_layout = QVBoxLayout()
        self.timeline_scrubber = QSlider(Qt.Orientation.Horizontal)
        self.timeline_scrubber.sliderReleased.connect(self.on_scrubber_released)
        self.timeline_scrubber.valueChanged.connect(self.on_scrubber_value_changed)
        playback_layout.addWidget(self.timeline_scrubber)
        main_buttons_layout = QHBoxLayout()
        self.stop_button = QPushButton("■ Stop")
        self.stop_button.clicked.connect(self.stop_playback)
        self.play_pause_button = QPushButton("▶ Play")
        self.play_pause_button.setCheckable(True)
        self.play_pause_button.toggled.connect(self.on_play_pause_toggled)
        self.step_back_button = QPushButton("◀ Step Back")
        self.step_back_button.clicked.connect(self.step_backward)
        self.step_button = QPushButton("Step Fwd ▶")
        self.step_button.clicked.connect(self.step_forward)
        main_buttons_layout.addWidget(self.stop_button)
        main_buttons_layout.addWidget(self.play_pause_button)
        main_buttons_layout.addWidget(self.step_back_button)
        main_buttons_layout.addWidget(self.step_button)
        playback_layout.addLayout(main_buttons_layout)
        speed_jump_layout = QHBoxLayout()
        speed_jump_layout.addWidget(QLabel("Speed:"))
        self.speed_combo = QComboBox()
        self.speed_combo.addItems(self.playback_speed_map.keys())
        self.speed_combo.setCurrentText("1x")
        speed_jump_layout.addWidget(self.speed_combo)
        speed_jump_layout.addStretch()
        speed_jump_layout.addWidget(QLabel("Jump to Tick:"))
        self.jump_to_tick_input = QSpinBox()
        self.jump_to_tick_input.setMinimum(0)
        self.jump_to_tick_input.setMaximum(9999999)
        speed_jump_layout.addWidget(self.jump_to_tick_input)
        self.jump_button = QPushButton("Jump")
        self.jump_button.clicked.connect(self.jump_to_tick)
        speed_jump_layout.addWidget(self.jump_button)
        playback_layout.addLayout(speed_jump_layout)
        adv_play_layout = QHBoxLayout()
        self.play_until_button = QPushButton("Play Until Specified")
        self.play_until_button.clicked.connect(self.play_until_specified)
        self.play_until_latest_button = QPushButton("Play Until Latest")
        self.play_until_latest_button.clicked.connect(self.play_until_latest)
        adv_play_layout.addWidget(self.play_until_button)
        adv_play_layout.addWidget(self.play_until_latest_button)
        self.playback_box.setContentLayout(playback_layout)
        self.playback_box.setEnabled(False)
        controls_super_layout.addWidget(self.playback_box) # Add to Super Layout

        # --- Finalize Super Box ---
        self.controls_super_box.setContentLayout(controls_super_layout)
        content_layout.addWidget(self.controls_super_box)

        content_layout.addStretch()
        return scroll_container

    @Slot(str, str)
    def log_message(self, message, level):
        self.log_widget.append(f"[{level.upper()}] {message}")

    @Slot()
    def show_connection_dialog(self):
        if self.is_playing: self.play_pause_button.setChecked(False)
        self.logging_level_menu.setEnabled(False)
        dialog = ConnectionDialog(self, self.last_api_url)
        if dialog.exec():
            details = dialog.connection_details
            if details:
                self.renderer_3d.clear_scene()
                self.exp_listbox.clear()
                self.controls_panel.setEnabled(False)
                self.log_widget.clear() 
                self.log_message(f"Connecting...", "info")
                self.command_queue.put(details)
                if details.get("type") == "CONNECT":
                    self._temp_initial_log_level = details.get("log_level", "Info")

    @Slot(bool, str, str)
    def on_connection_result(self, success, url, error_str):
        if success:
            self.last_api_url = url
            self.is_offline_mode = False
            self.controls_panel.setEnabled(True)
            self.refresh_button.setEnabled(True)
            # create_exp_box logic moved to tabs, handled by global panel enabled state
            self.delete_exp_button.setEnabled(True)
            self.clone_exp_button.setEnabled(False) # Disabled until selection
            self.logging_level_menu.setEnabled(True)
            self.log_message(f"Successfully connected to {url}", "success")

            for action in self.logging_level_menu.actions():
                if action.text().lower() == self._temp_initial_log_level.lower():
                    action.setChecked(True)
                    break
            
            self.refresh_experiment_list()
        else:
            self.log_message(f"Connection failed: {error_str}", "error")
            self.controls_panel.setEnabled(False)
            self.logging_level_menu.setEnabled(False)

    @Slot(str, str)
    def on_replay_loaded(self, exp_id, exp_name):
        self.is_offline_mode = True
        self.controls_panel.setEnabled(True)
        self.refresh_button.setEnabled(False)
        self.delete_exp_button.setEnabled(False)
        self.clone_exp_button.setEnabled(False)
        self.logging_level_menu.setEnabled(False)
        self.log_widget.clear()
        self.log_message(f"Successfully loaded replay '{exp_name}'", "success")
        self.exp_listbox.clear()
        item = QListWidgetItem(f"{exp_name} ({exp_id})")
        item.setData(Qt.ItemDataRole.UserRole, exp_id)
        self.exp_listbox.addItem(item)
        self.exp_listbox.setCurrentItem(item)

    @Slot(str, str)
    def on_replay_saved(self, path, message):
        self.log_message(f"{message} Path: {path}", "success")

    @Slot(bool, str)
    def on_assembly_result(self, success, result_string):
        if success:
            bytecode = result_string
            self.log_message(f"Assembly successful. Bytecode generated.", "success")
            self.hgl_bytecode_input.setText(bytecode)
            self.new_exp_genome_input.setText(bytecode)
            self.hgl_sub_tabs.setCurrentWidget(self.decompile_tab)
        else:
            error_message = result_string
            self.log_message(f"Assembly failed: {error_message}", "error")
            QMessageBox.critical(self, "Assembly Error", error_message)

    @Slot(bool, str)
    def on_decompilation_result(self, success, result_string):
        if success:
            source_code = result_string
            self.log_message("Decompilation successful.", "success")
            self.hgl_source_input.setPlainText(source_code)
            self.hgl_sub_tabs.setCurrentWidget(self.assemble_tab)
        else:
            error_message = result_string
            self.log_message(f"Decompilation failed: {error_message}", "error")
            QMessageBox.critical(self, "Decompilation Error", error_message)

    @Slot(list)
    def on_server_logs_received(self, server_logs: list):
        self.log_widget.clear()
        if not server_logs:
            self.log_widget.setText("No log entries received from server.")
            return

        log_lines = []
        for entry in server_logs:
            timestamp = entry.get('timestamp', '00:00:00')
            level = entry.get('level', 'INFO').upper()
            tag = entry.get('tag', 'DEFAULT')
            message = entry.get('message', '')
            time_part = timestamp.split('T')[1].split('.')[0] if 'T' in timestamp else timestamp
            log_lines.append(f"{time_part} [{level:<7}] [{tag}] {message}")
        
        self.log_widget.setText("\n".join(log_lines))
        self.log_widget.verticalScrollBar().setValue(self.log_widget.verticalScrollBar().maximum())

    @Slot(dict)
    def on_live_status_received(self, status):
        latest_server_tick = status.get('currentTick')
        if latest_server_tick is not None and latest_server_tick > self.current_display_tick:
            self.log_message(f"Playing until server tick {latest_server_tick}.", "info")
            self.target_tick = latest_server_tick
            if not self.is_playing:
                self.play_pause_button.setChecked(True)
        else:
            self.log_message("Already at or ahead of the latest server tick.", "info")

    @Slot(object)
    def on_new_frame_data(self, frame: ReplayFrame):
        self._update_timeline_range()
        self._display_frame(frame)

    @Slot(list)
    def on_experiment_list_received(self, experiments):
        current_item = self.exp_listbox.currentItem()
        current_id = current_item.data(Qt.ItemDataRole.UserRole) if current_item else None
        id_to_select = self.id_to_select_after_refresh if self.id_to_select_after_refresh else current_id

        self.exp_listbox.clear()
        item_to_reselect = None
        for exp in experiments:
            item = QListWidgetItem(f"{exp['name']} ({exp['id']})")
            item.setData(Qt.ItemDataRole.UserRole, exp['id'])
            self.exp_listbox.addItem(item)
            if exp['id'] == id_to_select:
                item_to_reselect = item

        if item_to_reselect:
            self.exp_listbox.setCurrentItem(item_to_reselect)
        else:
            self.selected_exp_id = None
            self.save_replay_action.setEnabled(False)
            self.playback_box.setEnabled(False)
            self.delete_exp_button.setEnabled(False)
            self.clone_exp_button.setEnabled(False)

        self.log_message(f"Refreshed experiment list. Found {len(experiments)}.", "info")
        self.id_to_select_after_refresh = None

    @Slot(dict)
    def on_experiment_created(self, new_exp):
        self.log_message(f"Successfully created/cloned experiment '{new_exp['name']}' ({new_exp['id']})", "success")
        self.id_to_select_after_refresh = new_exp['id']
        self.refresh_experiment_list()
        # Switch back to the active experiments tab to see the new one
        self.exp_mgmt_tabs.setCurrentWidget(self.active_exp_tab)

    @Slot(str)
    def on_experiment_deleted(self, deleted_exp_id):
        self.log_message(f"Successfully deleted experiment {deleted_exp_id}", "success")
        if self.selected_exp_id == deleted_exp_id:
            self.selected_exp_id = None
            self.save_replay_action.setEnabled(False)
            self.renderer_3d.clear_scene()
            self.setWindowTitle("HidraViz - PySide6 Edition")
            self.delete_exp_button.setEnabled(False)
            self.clone_exp_button.setEnabled(False)
        self.refresh_experiment_list()
    
    @Slot(QAction)
    def on_log_level_changed(self, action: QAction):
        if not self.selected_exp_id or self.is_offline_mode: return
        new_level = action.text()
        self.log_message(f"Requesting log level change to '{new_level}' for {self.selected_exp_id}", "info")
        self.command_queue.put({
            "type": "SET_LOG_LEVEL",
            "exp_id": self.selected_exp_id,
            "level": new_level
        })

    @Slot(str, int)
    def on_3d_object_selected(self, obj_type: str, obj_id: int):
        current_selection = None
        if self.inspected_neuron_id is not None:
            current_selection = ('neuron', self.inspected_neuron_id)
        elif self.inspected_io_node is not None:
            current_selection = self.inspected_io_node

        if current_selection == (obj_type, obj_id):
            self.inspected_neuron_id = None
            self.inspected_io_node = None
            self.log_message(f"Deselected {obj_type} {obj_id}.", "info")
        else:
            if obj_type == 'neuron':
                self.log_message(f"Selected neuron {obj_id} for inspection.", "info")
                self.inspected_neuron_id = obj_id
                self.inspected_io_node = None
                self.tab_widget.setCurrentWidget(self.brain_renderer_2d)
                self.dock_details.raise_()
            else:
                self.log_message(f"Selected {obj_type} node {obj_id}.", "info")
                self.inspected_neuron_id = None
                self.inspected_io_node = (obj_type, obj_id)
        
        self._update_brain_viewer()
        self._trigger_render_update()
    
    def _trigger_render_update(self):
        if not self.worker.controller or self.selected_exp_id is None: return
        current_frame = self.worker.controller.get_frame(self.selected_exp_id, self.current_display_tick)
        if not current_frame: return

        selected_obj = None
        if self.inspected_neuron_id is not None: selected_obj = ('neuron', self.inspected_neuron_id)
        elif self.inspected_io_node is not None: selected_obj = self.inspected_io_node

        self.render_command_queue.put({
            "type": "PROCESS_FRAME",
            "frame": current_frame,
            "positions": self.renderer_3d._node_positions,
            "input_ids": self.renderer_3d.input_ids_cache,
            "output_ids": self.renderer_3d.output_ids_cache,
            "selected_obj": selected_obj
        })

    @Slot(QListWidgetItem, QListWidgetItem)
    def select_experiment(self, current_item: QListWidgetItem, previous_item: QListWidgetItem):
        if not current_item:
            self.selected_exp_id = None
            self.delete_exp_button.setEnabled(False)
            self.clone_exp_button.setEnabled(False)
            self.save_replay_action.setEnabled(False)
            self.playback_box.setEnabled(False)
            self.logging_level_menu.setEnabled(False)
            return

        exp_id = current_item.data(Qt.ItemDataRole.UserRole)
        if self.selected_exp_id == exp_id: return

        self.inspected_neuron_id = None
        self.inspected_io_node = None
        self.staged_input_values.clear()
        self.selected_input_id = None
        self._update_brain_viewer()
        while self.input_bubbles_layout.count():
            item = self.input_bubbles_layout.takeAt(0)
            widget = item.widget()
            if widget: widget.deleteLater()
        self.selected_input_label.setText("Selected: None")
        self.output_display.clear()

        self.playback_box.setEnabled(True)
        self.delete_exp_button.setEnabled(True)
        self.clone_exp_button.setEnabled(True)
        self.save_replay_action.setEnabled(True)
        self.logging_level_menu.setEnabled(not self.is_offline_mode)

        if self.is_playing: self.play_pause_button.setChecked(False)

        desired_log_level = "Info"
        if not self.is_offline_mode:
            for action in self.logging_level_menu.actions():
                if action.isChecked():
                    desired_log_level = action.text()
                    break

        if self.is_offline_mode:
            self.on_experiment_selected(exp_id)
        else:
            self.command_queue.put({
                "type": "SELECT_EXPERIMENT", 
                "exp_id": exp_id,
                "log_level": desired_log_level
            })

    @Slot(str)
    def on_experiment_selected(self, exp_id):
        self.selected_exp_id = exp_id
        self.log_message(f"Controller is ready for experiment {self.selected_exp_id}", "info")
        self._update_timeline_range()
        if self.is_offline_mode:
            self.play_until_latest_button.setEnabled(False)
            frame = self.worker.controller.get_first_frame(exp_id)
            if frame: self._display_frame(frame)
            else: self.log_message("Replay file appears to contain no frames.", "error")
        else:
            self.play_until_latest_button.setEnabled(True)
            frame = self.worker.controller.get_latest_frame(exp_id)
            if frame: self._display_frame(frame)
            else: self.log_message(f"No history found for experiment {exp_id}.", "warning")

    @Slot(bool)
    def on_play_pause_toggled(self, is_checked):
        self.is_playing = is_checked
        if self.is_playing:
            self.play_pause_button.setText("❚❚ Pause")
            delay = self.playback_speed_map.get(self.speed_combo.currentText(), 100)
            self.playback_timer.start(delay)
            self._on_playback_tick()
        else:
            self.play_pause_button.setText("▶ Play")
            self.playback_timer.stop()

    @Slot()
    def stop_playback(self):
        if self.is_playing: self.play_pause_button.setChecked(False)
        self.target_tick = None
        if self.worker.controller and self.selected_exp_id:
            first_frame = self.worker.controller.get_first_frame(self.selected_exp_id)
            if first_frame: self._set_and_display_tick(first_frame.tick)

    @Slot()
    def _on_playback_tick(self):
        if not self.is_playing:
            self.playback_timer.stop()
            return
            
        if self.target_tick is not None and self.current_display_tick >= self.target_tick:
            self.play_pause_button.setChecked(False)
            self.target_tick = None
            return
            
        self._execute_step_forward()

    @Slot()
    def on_scrubber_released(self):
        if self.is_playing: self.play_pause_button.setChecked(False)
        self._set_and_display_tick(self.timeline_scrubber.value())

    @Slot(int)
    def on_scrubber_value_changed(self, value):
        self.jump_to_tick_input.setValue(value)

    @Slot()
    def jump_to_tick(self):
        if self.is_playing: self.play_pause_button.setChecked(False)
        self._set_and_display_tick(self.jump_to_tick_input.value())

    @Slot()
    def play_until_specified(self):
        target = self.jump_to_tick_input.value()
        if target > self.current_display_tick:
            self.target_tick = target
            if not self.is_playing: self.play_pause_button.setChecked(True)

    @Slot()
    def play_until_latest(self):
        if self.is_offline_mode or not self.selected_exp_id: return
        self.log_message("Querying server for latest tick...", "info")
        self.command_queue.put({"type": "GET_LIVE_STATUS", "exp_id": self.selected_exp_id})

    def _parse_node_ids(self, text: str) -> list[int]:
        if not text: return []
        items = text.replace(",", " ").split()
        ids = set()
        for item in items:
            if item.isdigit():
                val = int(item)
                if 0 <= val <= 255: ids.add(val)
                else: self.log_message(f"Node ID {val} is outside the valid 0-255 range and was ignored.", "warning")
        return sorted(list(ids))

    @Slot()
    def create_experiment(self):
        name = self.new_exp_name_input.text().strip()
        genome = self.new_exp_genome_input.text().strip()
        input_ids = self._parse_node_ids(self.new_exp_inputs_input.text())
        output_ids = self._parse_node_ids(self.new_exp_outputs_input.text())
        io_config = { "inputNodeIds": input_ids, "outputNodeIds": output_ids }

        if not name or not genome:
            self.log_message("Name and genome cannot be empty.", "warning")
            return
        self.command_queue.put({"type": "CREATE_EXPERIMENT", "name": name, "genome": genome, "io_config": io_config})

    @Slot()
    def delete_experiment(self):
        current_item = self.exp_listbox.currentItem()
        if not current_item:
            self.log_message("No experiment selected to delete.", "warning")
            return
        exp_id = current_item.data(Qt.ItemDataRole.UserRole)
        exp_name = current_item.text()
        reply = QMessageBox.question(self, 'Confirm Deletion',
            f"Are you sure you want to permanently delete this experiment?\n\n{exp_name}",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No, QMessageBox.StandardButton.No)
        if reply == QMessageBox.StandardButton.Yes:
            self.log_message(f"Requesting deletion of {exp_id}...", "info")
            self.command_queue.put({"type": "DELETE_EXPERIMENT", "exp_id": exp_id})

    @Slot()
    def clone_experiment(self):
        if not self.selected_exp_id:
            self.log_message("No experiment selected to clone.", "warning")
            return

        # Defaults: Tick = Current visualizer tick
        dialog = CloneDialog(self.current_display_tick, self)
        if dialog.exec():
            name, tick = dialog.get_data()
            self.log_message(f"Requesting clone of {self.selected_exp_id} at tick {tick}...", "info")
            self.command_queue.put({
                "type": "CLONE_EXPERIMENT",
                "source_id": self.selected_exp_id,
                "name": name,
                "tick": tick
            })

    @Slot()
    def save_replay(self):
        if not self.selected_exp_id:
            self.log_message("No experiment selected to save.", "warning")
            return
        file_path, _ = QFileDialog.getSaveFileName(self, "Save Hidra Replay File",
            f"{self.selected_exp_id}.json", "JSON Files (*.json);;All Files (*)")
        if file_path:
            self.command_queue.put({"type": "SAVE_REPLAY", "exp_id": self.selected_exp_id, "path": file_path})

    @Slot()
    def assemble_hgl(self):
        source = self.hgl_source_input.toPlainText()
        if not source.strip():
            self.log_message("HGL source code cannot be empty.", "warning")
            return
        self.command_queue.put({"type": "ASSEMBLE_HGL", "source": source})

    @Slot()
    def decompile_hgl(self):
        bytecode = self.hgl_bytecode_input.toPlainText().strip()
        if not bytecode:
            self.log_message("HGL bytecode cannot be empty.", "warning")
            return
        self.command_queue.put({"type": "DECOMPILE_HGL", "bytecode": bytecode})

    @Slot(int)
    def on_input_bubble_clicked(self, node_id: int):
        self.selected_input_id = node_id
        self.selected_input_label.setText(f"Selected: {node_id}")
        current_value = self.staged_input_values.get(node_id, 0.0)
        self.input_value_edit.setText(str(current_value))
        self.input_value_edit.setFocus()
        self._update_input_bubble_styles()

    @Slot()
    def on_set_input_value(self):
        if self.selected_input_id is None:
            self.log_message("No input node selected to set value for.", "warning")
            return
        try:
            value = float(self.input_value_edit.text())
            self.staged_input_values[self.selected_input_id] = value
            self.log_message(f"Staged input value {value} for node {self.selected_input_id}.", "info")
            self._update_input_bubble_styles()
        except ValueError:
            self.log_message("Invalid number format for input value.", "error")

    @Slot()
    def on_clear_selected_input(self):
        if self.selected_input_id is not None and self.selected_input_id in self.staged_input_values:
            self.staged_input_values[self.selected_input_id] = 0.0
            self.log_message(f"Cleared staged input for node {self.selected_input_id}.", "info")
            self._update_input_bubble_styles()

    @Slot()
    def on_clear_all_inputs(self):
        for node_id in self.staged_input_values:
            self.staged_input_values[node_id] = 0.0
        self.log_message("Cleared all staged input values.", "info")
        self._update_input_bubble_styles()

    def _set_and_display_tick(self, tick: int):
        if self.worker.controller and self.selected_exp_id:
            frame = self.worker.controller.get_frame(self.selected_exp_id, tick)
            if frame:
                self._display_frame(frame)

    def _display_frame(self, frame: ReplayFrame):
        if not frame: return
        self.current_display_tick = frame.tick
        
        display_title = "Initial State" if self.current_display_tick == 0 else f"State After Tick: {self.current_display_tick - 1}"
        self.setWindowTitle(f"HidraViz - {display_title}")

        self.timeline_scrubber.blockSignals(True); self.timeline_scrubber.setValue(frame.tick); self.timeline_scrubber.blockSignals(False)
        self.jump_to_tick_input.blockSignals(True); self.jump_to_tick_input.setValue(frame.tick); self.jump_to_tick_input.blockSignals(False)
        
        self.renderer_3d.update_layout(frame.snapshot)

        self._update_event_viewer(frame)
        self._update_brain_viewer()
        self._update_io_panel(frame)
        self._update_step_buttons()
        
        self._trigger_render_update()

    @Slot(object)
    def on_render_ready(self, payload: RenderPayload):
        self.renderer_3d.display_payload(payload)

    def _format_event_to_string(self, event: Any) -> str:
        if not isinstance(event, dict): return f"[T:?] MalformedEvent | Data: {str(event)}"
        event_type = event.get('$type', event.get('type', 'UnknownEvent'))
        tick = event.get('executionTick', '?')
        details = ", ".join(f"{k}: {v}" for k, v in event.items() if k not in ['$type', 'type', 'executionTick', 'id'])
        return f"[T:{tick}] {event_type} | {details}"

    def _update_event_viewer(self, frame: ReplayFrame):
        self.event_viewer_widget.clear()
        if not frame or not frame.events:
            self.event_viewer_widget.setText("No events for this tick.")
            return
        all_event_text = "\n".join([self._format_event_to_string(e) for e in frame.events])
        self.event_viewer_widget.setText(all_event_text)
    
    def _update_input_bubble_styles(self):
        base_style = "padding: 5px; border-radius: 8px;"
        style_default = f"{base_style}"
        style_staged = f"{base_style} background-color: #A0C8E0;"
        style_selected = f"{base_style} background-color: #3399CC; color: white; border: 1px solid #FFFFFF;"
        for i in range(self.input_bubbles_layout.count()):
            widget = self.input_bubbles_layout.itemAt(i).widget()
            if not widget: continue
            node_id = widget.property("node_id")
            if node_id == self.selected_input_id: widget.setStyleSheet(style_selected)
            elif self.staged_input_values.get(node_id, 0.0) != 0.0: widget.setStyleSheet(style_staged)
            else: widget.setStyleSheet(style_default)

    def _update_io_panel(self, frame: ReplayFrame):
        if not frame: return
        snapshot = frame.snapshot
        
        input_ids = snapshot.get('inputNodeIds', [])
        current_ui_ids = set()
        for i in range(self.input_bubbles_layout.count()):
             widget = self.input_bubbles_layout.itemAt(i).widget()
             if widget: current_ui_ids.add(widget.property("node_id"))

        if set(input_ids) != current_ui_ids:
            while self.input_bubbles_layout.count():
                item = self.input_bubbles_layout.takeAt(0)
                if item.widget(): item.widget().deleteLater()
            self.staged_input_values = {nid: 0.0 for nid in input_ids}
            if self.selected_input_id not in self.staged_input_values:
                self.selected_input_id = None
                self.selected_input_label.setText("Selected: None")
            for node_id in sorted(input_ids):
                bubble = QPushButton(str(node_id))
                bubble.setProperty("node_id", node_id)
                bubble.clicked.connect(lambda checked=False, nid=node_id: self.on_input_bubble_clicked(nid))
                self.input_bubbles_layout.addWidget(bubble)
        self._update_input_bubble_styles()

        output_ids = snapshot.get('outputNodeIds', [])
        output_values = snapshot.get('outputNodeValues', {})
        output_text = [f"Output {nid}: {output_values.get(str(nid), 0.0):.4f}" for nid in sorted(output_ids)]
        self.output_display.setText("\n".join(output_text) if output_text else "No output nodes for this experiment.")

    def _update_brain_viewer(self):
        if self.worker.controller and self.selected_exp_id and self.inspected_neuron_id is not None:
             brain_data = self.worker.controller.get_brain_details(
                 self.selected_exp_id, 
                 self.current_display_tick,
                 self.inspected_neuron_id
             )
             self.brain_renderer_2d.update_data(brain_data)
        else:
             self.brain_renderer_2d.update_data(None)

    def step_backward(self):
        if self.is_playing: self.play_pause_button.setChecked(False)
        if not self.selected_exp_id: return
        self._set_and_display_tick(self.current_display_tick - 1)

    def step_forward(self):
        if self.is_playing: self.play_pause_button.setChecked(False)
        self._execute_step_forward()

    def _execute_step_forward(self):
        if not self.selected_exp_id or not self.worker.controller: return
        is_at_latest = self.worker.controller.is_latest_tick(self.selected_exp_id, self.current_display_tick)
        if not self.is_offline_mode and is_at_latest:
            self.log_message(f"At latest tick. Requesting step with inputs: {self.staged_input_values}", "info")
            self.command_queue.put({"type": "ATOMIC_STEP", "exp_id": self.selected_exp_id, "inputs": self.staged_input_values, "outputs_to_read": []})
        else:
            self._set_and_display_tick(self.current_display_tick + 1)

    def _update_step_buttons(self):
        if not self.selected_exp_id or not self.worker.controller:
            self.step_button.setEnabled(False); self.step_back_button.setEnabled(False)
            return
        has_next = self.worker.controller.get_frame(self.selected_exp_id, self.current_display_tick + 1) is not None
        is_at_latest = self.worker.controller.is_latest_tick(self.selected_exp_id, self.current_display_tick)
        self.step_button.setEnabled(has_next or (not self.is_offline_mode and is_at_latest))
        self.step_back_button.setEnabled(self.worker.controller.get_frame(self.selected_exp_id, self.current_display_tick - 1) is not None)

    def _update_timeline_range(self):
        if not self.worker.controller or not self.selected_exp_id: return
        history = self.worker.controller.get_full_history(self.selected_exp_id)
        min_tick, max_tick = (min(f.tick for f in history), max(f.tick for f in history)) if history else (0, 0)
        self.timeline_scrubber.setRange(min_tick, max_tick)
        self.jump_to_tick_input.setRange(min_tick, max_tick)

    def refresh_experiment_list(self):
        if not self.is_offline_mode:
            self.command_queue.put({"type": "REFRESH_EXPERIMENTS"})

    def closeEvent(self, event):
        print("INFO: Close event received. Shutting down worker threads.")
        self.command_queue.put({"type": "STOP"})
        self.render_command_queue.put({"type": "STOP"})
        self.worker_thread.quit()
        self.render_worker_thread.quit()
        self.worker_thread.wait()
        self.render_worker_thread.wait()
        event.accept()