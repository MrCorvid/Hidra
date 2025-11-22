# main_window.py
import sys
import queue
from typing import Optional

from PySide6.QtWidgets import (
    QMainWindow, QStackedWidget, QMessageBox, QFileDialog, QInputDialog,
    QTreeWidgetItem, QTreeWidgetItemIterator, QPushButton
)
from PySide6.QtCore import Qt, QThread, Slot, QTimer
from PySide6.QtGui import QAction, QActionGroup, QBrush, QColor

from simulation_worker import SimulationWorker
from render_worker import RenderWorker
from connection_dialog import ConnectionDialog

# Import the modular Views
from views.simulation_view import SimulationView
from views.evolution_view import EvolutionView

class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("HidraViz - PySide6 Edition")
        self.setGeometry(100, 100, 1600, 900)

        # --- Backend Infrastructure ---
        self.command_queue = queue.Queue()
        self.render_command_queue = queue.Queue()
        
        self.worker_thread = QThread()
        self.worker = SimulationWorker(self.command_queue)
        self.worker.moveToThread(self.worker_thread)
        
        self.render_worker_thread = QThread()
        self.render_worker = RenderWorker(self.render_command_queue)
        self.render_worker.moveToThread(self.render_worker_thread)

        # --- Local State ---
        self.selected_exp_id: Optional[str] = None
        self.selected_exp_type: str = "Standalone"
        self.current_display_tick: int = 0
        self.is_playing: bool = False
        self.last_api_url = "http://localhost:5000"
        self.id_to_select_after_refresh: Optional[str] = None 
        
        # Playback State
        self.target_stop_tick: Optional[int] = None
        self.is_waiting_for_run_completion: bool = False
        
        # Selection State
        self.inspected_neuron_id: Optional[int] = None
        self.inspected_io_node: Optional[tuple] = None

        # --- Timers ---
        self.playback_timer = QTimer(self)
        self.playback_timer.timeout.connect(self._on_playback_tick)

        self.evo_poll_timer = QTimer(self)
        self.evo_poll_timer.setInterval(1000) 
        self.evo_poll_timer.timeout.connect(self._poll_evolution_status)
        
        # Sync Poller (for waiting on server runs)
        self.sync_poll_timer = QTimer(self)
        self.sync_poll_timer.setInterval(1000) 
        self.sync_poll_timer.timeout.connect(self._poll_history_sync)

        # --- UI Setup ---
        self.central_stack = QStackedWidget()
        self.setCentralWidget(self.central_stack)

        # Initialize Views
        self.sim_view = SimulationView(self)
        self.evo_view = EvolutionView(self)
        
        self.central_stack.addWidget(self.sim_view) 
        self.central_stack.addWidget(self.evo_view)

        self._setup_menus()
        self._connect_worker_signals()
        self._connect_view_signals()

        # --- Start Threads ---
        self.worker_thread.start()
        self.render_worker_thread.start()
        self.evo_poll_timer.start()

        QTimer.singleShot(0, self.show_connection_dialog)

    def _setup_menus(self):
        menu_bar = self.menuBar()
        
        file_menu = menu_bar.addMenu("&File")
        conn_action = QAction("Connect...", self)
        conn_action.triggered.connect(self.show_connection_dialog)
        file_menu.addAction(conn_action)
        
        view_menu = menu_bar.addMenu("&View")
        view_group = QActionGroup(self)
        view_group.setExclusive(True)
        
        act_sim = QAction("Simulation & Playback", self, checkable=True)
        act_sim.setChecked(True)
        act_sim.triggered.connect(lambda: self.central_stack.setCurrentIndex(0))
        view_group.addAction(act_sim)
        view_menu.addAction(act_sim)

        act_evo = QAction("Evolution Dashboard", self, checkable=True)
        act_evo.triggered.connect(lambda: self.central_stack.setCurrentIndex(1))
        view_group.addAction(act_evo)
        view_menu.addAction(act_evo)

        view_menu.addSeparator()
        for action in self.sim_view.get_view_menu_actions():
            view_menu.addAction(action)

    def _connect_worker_signals(self):
        self.worker_thread.started.connect(self.worker.run)
        self.render_worker_thread.started.connect(self.render_worker.run)
        
        # Basic Status
        self.worker.signals.status_update.connect(self._log_status)
        self.worker.signals.connection_result.connect(self._on_connection_result)
        
        # Simulation Data
        self.worker.signals.logs_updated.connect(self._on_server_logs)
        self.worker.signals.new_frame_data.connect(self._on_new_frame)
        self.worker.signals.step_failed.connect(self._on_step_failed)
        self.worker.signals.history_refreshed.connect(self._on_history_refreshed)
        self.worker.signals.run_execution_result.connect(self._on_run_execution_result)
        
        # Experiment Management
        self.worker.signals.experiment_list.connect(self._on_exp_root_list)
        self.worker.signals.experiment_children.connect(self._on_exp_children)
        self.worker.signals.experiment_created.connect(self._on_exp_created)
        self.worker.signals.experiment_deleted.connect(self._on_exp_deleted)
        self.worker.signals.experiment_selected.connect(self._on_exp_selected)
        
        # Replay
        self.worker.signals.replay_loaded.connect(self._on_replay_loaded)
        self.worker.signals.replay_saved.connect(lambda p, m: self._log_status(f"{m} ({p})", "success"))
        
        # HGL
        self.worker.signals.assembly_result.connect(self._on_assembly_result)
        self.worker.signals.decompilation_result.connect(self._on_decompilation_result)

        # Evolution
        self.worker.signals.live_status_update.connect(self._on_evo_status)

        # Render Pipeline
        self.render_worker.signals.render_ready.connect(self.sim_view.renderer_3d.display_payload)

    def _connect_view_signals(self):
        # --- Simulation View Signals ---
        cp = self.sim_view.controls_panel
        
        cp.refresh_clicked.connect(lambda: self.command_queue.put({"type": "REFRESH_EXPERIMENTS"}))
        cp.create_exp_clicked.connect(self._request_create_exp)
        cp.delete_exp_clicked.connect(self._request_delete_exp)
        cp.clone_exp_clicked.connect(self._request_clone_exp)
        cp.save_replay_clicked.connect(self._request_save_replay)
        cp.rename_exp_clicked.connect(self._request_rename_exp)
        
        cp.exp_expanded.connect(self._on_ui_tree_expanded)
        cp.exp_selected.connect(self._on_ui_exp_selected)

        cp.assemble_clicked.connect(lambda src: self.command_queue.put({"type": "ASSEMBLE_HGL", "source": src}))
        cp.decompile_clicked.connect(lambda hex: self.command_queue.put({"type": "DECOMPILE_HGL", "bytecode": hex}))

        cp.playback_toggle_clicked.connect(self._toggle_playback)
        cp.playback_stop_clicked.connect(self._stop_playback)
        cp.step_fwd_clicked.connect(self._step_fwd)
        cp.step_back_clicked.connect(self._step_back)
        cp.scrubber_released.connect(lambda: self._jump_to_tick(cp.scrubber.value()))
        
        # Updated Signals
        cp.jump_clicked.connect(self._on_jump_clicked) 
        cp.play_until_latest_clicked.connect(self._play_until_latest)
        cp.speed_changed.connect(self._on_speed_changed)
        cp.play_until_spec_clicked.connect(self._on_play_until_spec)
        
        cp.input_set_clicked.connect(self._request_input_set)

        # Connect Worker List/Children signals to ControlsPanel slots
        self.worker.signals.experiment_list.connect(cp._on_exp_root_list)
        self.worker.signals.experiment_children.connect(cp._on_exp_children)

        self.sim_view.renderer_3d.object_selected.connect(self._on_3d_object_selected)

        # --- Evolution View Signals ---
        self.evo_view.start_clicked.connect(lambda cfg: self.command_queue.put({"type": "EVO_START", "config": cfg}))
        self.evo_view.stop_clicked.connect(lambda: self.command_queue.put({"type": "EVO_STOP"}))
        self.evo_view.load_gen_clicked.connect(lambda gen: self.command_queue.put({"type": "EVO_LOAD_GEN", "index": gen}))
        self.evo_view.export_csv_clicked.connect(self._on_export_csv_requested)

    # ==========================================================================
    #   Worker Signal Handlers
    # ==========================================================================

    @Slot(str, str)
    def _log_status(self, msg, level):
        self.sim_view.append_log(f"[{level.upper()}] {msg}")

    @Slot(bool, str, str)
    def _on_connection_result(self, success, url, err):
        if success:
            self.last_api_url = url
            self.sim_view.controls_panel.setEnabled(True)
            self.sim_view.append_log(f"Connected to {url}")
            self.command_queue.put({"type": "REFRESH_EXPERIMENTS"})
        else:
            self.sim_view.append_log(f"Connection failed: {err}")
            self.sim_view.controls_panel.setEnabled(False)

    @Slot(list)
    def _on_server_logs(self, server_logs: list):
        if not server_logs: return
        log_lines = []
        for entry in server_logs:
            timestamp = entry.get('timestamp', '00:00:00')
            time_part = timestamp.split('T')[1].split('.')[0] if 'T' in timestamp else timestamp
            level = entry.get('level', 'INFO').upper()
            tag = entry.get('tag', 'DEFAULT')
            msg = entry.get('message', '')
            log_lines.append(f"{time_part} [{level:<7}] [{tag}] {msg}")
        
        self.sim_view.log_widget.setText("\n".join(log_lines))
        self.sim_view.log_widget.verticalScrollBar().setValue(self.sim_view.log_widget.verticalScrollBar().maximum())

    @Slot(object)
    def _on_new_frame(self, frame):
        if not frame: return
        self.current_display_tick = frame.tick
        
        cp = self.sim_view.controls_panel
        cp.scrubber.blockSignals(True)
        current_max = cp.scrubber.maximum()
        if frame.tick > current_max:
            cp.scrubber.setMaximum(frame.tick)
        cp.scrubber.setValue(frame.tick)
        cp.scrubber.blockSignals(False)
        
        self.setWindowTitle(f"HidraViz - Tick: {frame.tick}")
        
        self.sim_view.renderer_3d.update_layout(frame.snapshot)
        self._trigger_render_update()
        self.sim_view.controls_panel.update_io_display(frame)
        self.sim_view.update_details(frame)
        
        if self.inspected_neuron_id is not None and self.worker.controller:
             brain_data = self.worker.controller.get_brain_details(
                 self.selected_exp_id, frame.tick, self.inspected_neuron_id
             )
             self.sim_view.brain_renderer_2d.update_data(brain_data)
        else:
             self.sim_view.brain_renderer_2d.update_data(None)

    @Slot()
    def _on_step_failed(self):
        self.sim_view.append_log("Step failed (Server conflict or error). Stopping playback.")
        self._stop_playback()

    @Slot(bool, str)
    def _on_run_execution_result(self, success, message):
        if not success:
            self.sim_view.append_log(f"Run Aborted: {message}")
            # Reset UI state
            self.is_waiting_for_run_completion = False
            self.sync_poll_timer.stop()
            self.sim_view.controls_panel.btn_run_to.setEnabled(True)
            self.sim_view.controls_panel.btn_run_to.setText("Run to Tick")

    @Slot(int, int)
    def _on_history_refreshed(self, count, max_tick):
        # Update slider range
        cp = self.sim_view.controls_panel
        cp.scrubber.setMaximum(max(cp.scrubber.maximum(), max_tick))
        
        # Logic for "Run to Tick" buffering
        if self.is_waiting_for_run_completion:
            # If we have new frames that cover our current play head, resume!
            needed = self.current_display_tick + 1
            if max_tick >= needed and self.is_playing and not self.playback_timer.isActive():
                self.playback_timer.start(self._get_current_delay())
            
            # Check target
            if self.target_stop_tick is not None and max_tick >= self.target_stop_tick:
                self.sim_view.append_log(f"Target reached (Max: {max_tick}).")
                self.is_waiting_for_run_completion = False
                self.sync_poll_timer.stop()
                self.sim_view.controls_panel.btn_run_to.setEnabled(True)
                self.sim_view.controls_panel.btn_run_to.setText("Run to Tick")
        
        else:
            # Manual Sync
            self.sim_view.append_log(f"History Refreshed. New frames: {count}, Max Tick: {max_tick}")
            # Jump to latest tick only if not playing, to show effect of sync
            if not self.is_playing:
                self._jump_to_tick(max_tick)

    # --- Tree Signal Handlers ---
    @Slot(list)
    def _on_exp_root_list(self, experiments): pass
    @Slot(str, list)
    def _on_exp_children(self, parent_id, children): pass

    @Slot(dict)
    def _on_exp_created(self, new_exp):
        self.sim_view.append_log(f"Created: {new_exp['name']}")
        self.central_stack.setCurrentIndex(0) 
        self.id_to_select_after_refresh = new_exp['id']
        self.command_queue.put({"type": "REFRESH_EXPERIMENTS"})

    @Slot(str)
    def _on_exp_deleted(self, exp_id):
        self.sim_view.append_log(f"Deleted: {exp_id}")
        if self.selected_exp_id == exp_id:
            self.selected_exp_id = None
            self.sim_view.renderer_3d.clear_scene()
            self.sim_view.controls_panel.playback_box.setEnabled(False)
        self.command_queue.put({"type": "REFRESH_EXPERIMENTS"})

    @Slot(str)
    def _on_exp_selected(self, exp_id):
        self.selected_exp_id = exp_id
        
        if self.worker.controller:
            hist = self.worker.controller.get_full_history(exp_id)
            if hist:
                max_tick = max(f.tick for f in hist)
                self.sim_view.controls_panel.scrubber.setRange(0, max_tick)
                self._jump_to_tick(0) 
            else:
                self._jump_to_tick(0)

    @Slot(str, str)
    def _on_replay_loaded(self, exp_id, name):
        self.selected_exp_id = exp_id
        self.sim_view.append_log(f"Replay loaded: {name}")
        cp = self.sim_view.controls_panel
        cp.exp_tree.clear()
        item = QTreeWidgetItem(cp.exp_tree)
        item.setText(0, name)
        item.setData(0, Qt.ItemDataRole.UserRole, exp_id)
        item.setText(2, "Offline")
        self._on_exp_selected(exp_id)

    @Slot(bool, str)
    def _on_assembly_result(self, success, result):
        if success:
            self.sim_view.append_log("Assembly Successful.")
            self.sim_view.controls_panel.txt_hgl_byte.setText(result)
            self.sim_view.controls_panel.inp_new_genome.setText(result)
        else:
            self.sim_view.append_log(f"Assembly Error: {result}")
            QMessageBox.critical(self, "Assembly Failed", result)

    @Slot(bool, str)
    def _on_decompilation_result(self, success, result):
        if success:
            self.sim_view.append_log("Decompilation Successful.")
            self.sim_view.controls_panel.txt_hgl_source.setText(result)
        else:
            self.sim_view.append_log(f"Decompilation Error: {result}")

    @Slot(dict)
    def _on_evo_status(self, status):
        self.evo_view.update_status(status)

    # ==========================================================================
    #   UI Action Handlers
    # ==========================================================================

    @Slot()
    def show_connection_dialog(self):
        dialog = ConnectionDialog(self, self.last_api_url)
        if dialog.exec():
            details = dialog.connection_details
            if details:
                self.sim_view.renderer_3d.clear_scene()
                self.sim_view.clear_logs()
                self.command_queue.put(details)

    @Slot(str, str)
    def _on_ui_tree_expanded(self, exp_id):
        self.command_queue.put({"type": "FETCH_EXP_CHILDREN", "parent_id": exp_id})

    @Slot(str, str)
    def _on_ui_exp_selected(self, exp_id, exp_type):
        """Triggered when user clicks a row."""
        self.selected_exp_id = exp_id
        self.selected_exp_type = exp_type
        self.sim_view.append_log(f"Selected: {exp_id} ({exp_type})")
        
        is_locked = (exp_type == "GenerationOrganism")
        is_folder = (exp_type == "EvolutionRun")
        
        self.sim_view.controls_panel.playback_box.setEnabled(not is_folder)
        
        btn_fwd = self.sim_view.controls_panel.btn_fwd
        btn_run_to = self.sim_view.controls_panel.btn_run_to
        btn_sync = self.sim_view.controls_panel.btn_sync
        
        btn_fwd.setEnabled(True)
        btn_run_to.setEnabled(True)
        btn_sync.setEnabled(True)
        
        if is_locked:
            btn_fwd.setText("▶| (Locked)")
            self.sim_view.controls_panel.io_box.setEnabled(False)
        else:
            btn_fwd.setText("▶|")
            self.sim_view.controls_panel.io_box.setEnabled(True)

        if is_folder:
            self.sim_view.renderer_3d.clear_scene()
            self.sim_view.clear_logs()
            return

        self.command_queue.put({"type": "SELECT_EXPERIMENT", "exp_id": exp_id})

    @Slot(str, str, str, str)
    def _request_create_exp(self, name, genome, inputs_str, outputs_str):
        def parse_ids(text):
            return [int(x) for x in text.replace(",", " ").split() if x.isdigit()]
        try:
            io_config = { "inputNodeIds": parse_ids(inputs_str), "outputNodeIds": parse_ids(outputs_str) }
            self.command_queue.put({
                "type": "CREATE_EXPERIMENT", "name": name, "genome": genome, "io_config": io_config
            })
        except Exception:
            self.sim_view.append_log("Invalid I/O format.")

    @Slot()
    def _request_delete_exp(self):
        if self.selected_exp_id:
            reply = QMessageBox.question(
                self, "Confirm Delete", 
                f"Are you sure you want to delete '{self.selected_exp_id}'?",
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No
            )
            if reply == QMessageBox.StandardButton.Yes:
                self.command_queue.put({"type": "DELETE_EXPERIMENT", "exp_id": self.selected_exp_id})

    @Slot()
    def _request_clone_exp(self):
        if self.selected_exp_id:
            self.command_queue.put({
                "type": "CLONE_EXPERIMENT", "source_id": self.selected_exp_id,
                "name": "cloned", "tick": self.current_display_tick
            })
            
    @Slot(str, str)
    def _request_rename_exp(self, exp_id, new_name):
        self.command_queue.put({
            "type": "RENAME_EXPERIMENT", "exp_id": exp_id, "new_name": new_name
        })

    @Slot()
    def _request_save_replay(self):
        if not self.selected_exp_id: return
        path, _ = QFileDialog.getSaveFileName(self, "Save Replay", f"{self.selected_exp_id}.json", "JSON (*.json)")
        if path:
            self.command_queue.put({"type": "SAVE_REPLAY", "exp_id": self.selected_exp_id, "path": path})
            
    @Slot(int, float)
    def _request_input_set(self, node_id, val):
        if not self.selected_exp_id: return
        self.command_queue.put({
            "type": "ATOMIC_STEP", 
            "exp_id": self.selected_exp_id, 
            "inputs": {node_id: val}, 
            "outputs_to_read": []
        })
        self.sim_view.append_log(f"Requesting step with Input {node_id} = {val}")

    @Slot()
    def _on_export_csv_requested(self):
        path, _ = QFileDialog.getSaveFileName(self, "Export Evolution Data", "evolution_data.csv", "CSV Files (*.csv)")
        if path:
            self.command_queue.put({"type": "EVO_EXPORT_CSV", "path": path})

    def _poll_history_sync(self):
        """Periodically triggered when waiting for a server run to complete."""
        if self.selected_exp_id:
            self.command_queue.put({"type": "REFRESH_HISTORY", "exp_id": self.selected_exp_id})

    @Slot(int)
    def _on_jump_clicked(self, target_tick):
        """Immediate jump (no running)."""
        self._toggle_playback(False)
        self._jump_to_tick(target_tick)

    @Slot(int)
    def _on_play_until_spec(self, target_tick):
        """Run to Tick Logic."""
        if not self.selected_exp_id or not self.worker.controller: return
        
        self.target_stop_tick = target_tick
        latest = self.worker.controller.get_latest_frame(self.selected_exp_id)
        current_server_max = latest.tick if latest else 0
        
        delta = target_tick - current_server_max
        
        if delta > 0:
            if self.selected_exp_type == "GenerationOrganism":
                self.sim_view.append_log("Locked Experiment: Cannot run new ticks. Playing existing history.")
                self.target_stop_tick = current_server_max
            else:
                # Send Run Command
                self.command_queue.put({
                    "type": "EXECUTE_RUN",
                    "exp_id": self.selected_exp_id,
                    "run_type": "runFor",
                    "params": {"ticks": delta}
                })
                self.sim_view.append_log(f"Requesting {delta} ticks from server... Buffering.")
                
                # Enter Wait Mode
                self.is_waiting_for_run_completion = True
                self.sim_view.controls_panel.btn_run_to.setEnabled(False)
                self.sim_view.controls_panel.btn_run_to.setText("Buffering...")
                self.sync_poll_timer.start()
            
            # Start Playback immediately (will pause if it hits end of buffer)
            self._toggle_playback(True)
        else:
            # Already exists, just play
            self.sim_view.append_log(f"Playing local history to tick {target_tick}...")
            self._toggle_playback(True)

    # --- Playback Logic ---

    def _get_delay_ms(self, speed_text):
        return {"0.25x":400, "0.5x":200, "1x":100, "1.5x":66, "2x":50}.get(speed_text, 100)
    
    def _get_current_delay(self):
        txt = self.sim_view.controls_panel.combo_speed.currentText()
        return self._get_delay_ms(txt)

    def _toggle_playback(self, playing):
        self.is_playing = playing
        if playing:
            delay = self._get_current_delay()
            self.playback_timer.start(delay)
            self.sim_view.controls_panel.btn_play.setChecked(True)
        else:
            self.playback_timer.stop()
            self.sim_view.controls_panel.btn_play.setChecked(False)
            self.target_stop_tick = None 
            
            # Reset waiting state if manually stopped
            if self.is_waiting_for_run_completion:
                self.is_waiting_for_run_completion = False
                self.sync_poll_timer.stop()
                self.sim_view.controls_panel.btn_run_to.setEnabled(True)
                self.sim_view.controls_panel.btn_run_to.setText("Run to Tick")

    def _on_speed_changed(self, speed_text):
        if self.is_playing:
            self.playback_timer.stop()
            delay = self._get_delay_ms(speed_text)
            self.playback_timer.start(delay)

    def _stop_playback(self):
        self._toggle_playback(False)
        self._jump_to_tick(0)

    def _on_playback_tick(self):
        # 1. Calculate next tick
        next_tick = self.current_display_tick + 1
        
        # 2. Check Stop Condition
        if self.target_stop_tick is not None and next_tick > self.target_stop_tick:
            self._toggle_playback(False)
            self.sim_view.append_log(f"Reached target tick {self.target_stop_tick}.")
            return

        # 3. Try to get frame locally
        if not self.worker.controller: return
        frame = self.worker.controller.get_frame(self.selected_exp_id, next_tick)
        
        if frame:
            self._on_new_frame(frame)
        else:
            # Frame missing. Pause Playback Timer but stay in "Playing" mode (Buffering)
            self.playback_timer.stop()
            
            # If we aren't already polling for a big run, trigger a single sync
            if not self.is_waiting_for_run_completion:
                self.command_queue.put({"type": "REFRESH_HISTORY", "exp_id": self.selected_exp_id})

    def _step_fwd(self):
        self._toggle_playback(False)
        can_step_remote = (self.selected_exp_type != "GenerationOrganism")
        self._jump_to_tick(self.current_display_tick + 1, allow_remote_step=can_step_remote)

    def _step_back(self):
        self._toggle_playback(False)
        target = max(0, self.current_display_tick - 1)
        self._jump_to_tick(target)
        
    def _play_until_latest(self):
        if self.selected_exp_id:
            self.sim_view.append_log("Syncing history from live experiment...")
            self._toggle_playback(False)
            # Manual sync jumps to end logic handled in _on_history_refreshed else block
            self.command_queue.put({"type": "REFRESH_HISTORY", "exp_id": self.selected_exp_id})

    def _jump_to_tick(self, tick, allow_remote_step=False):
        """Jumps to a specific tick."""
        if not self.selected_exp_id or not self.worker.controller: return
        
        frame = self.worker.controller.get_frame(self.selected_exp_id, tick)
        
        if frame:
            self._on_new_frame(frame)
        else:
            latest = self.worker.controller.get_latest_frame(self.selected_exp_id)
            current_server_max = latest.tick if latest else 0
            
            if tick <= current_server_max:
                # Missing locally but on server -> Sync
                self.command_queue.put({"type": "REFRESH_HISTORY", "exp_id": self.selected_exp_id})
                return

            if self.selected_exp_type == "GenerationOrganism":
                if self.is_playing:
                    self._toggle_playback(False)
                    self.sim_view.append_log("Playback finished (Locked).")
                return 

            if not self.worker.controller.is_offline and allow_remote_step:
                # This is atomic stepping (Manual), not "Run to"
                if tick > current_server_max:
                    self.command_queue.put({
                        "type": "ATOMIC_STEP", 
                        "exp_id": self.selected_exp_id, 
                        "inputs": {}, 
                        "outputs_to_read": []
                    })
                    return
            
            if self.is_playing:
                self._toggle_playback(False)

    # --- 3D Interaction ---

    @Slot(str, int)
    def _on_3d_object_selected(self, obj_type, obj_id):
        if obj_type == "neuron":
            self.inspected_neuron_id = obj_id
            self.inspected_io_node = None
            self.sim_view.append_log(f"Selected Neuron {obj_id}")
            self._trigger_render_update()
            if self.worker.controller:
                brain_data = self.worker.controller.get_brain_details(
                    self.selected_exp_id, self.current_display_tick, obj_id
                )
                self.sim_view.brain_renderer_2d.update_data(brain_data)
                
        elif obj_type in ["input", "output"]:
            self.inspected_neuron_id = None
            self.inspected_io_node = (obj_type, obj_id)
            self.sim_view.append_log(f"Selected {obj_type} Node {obj_id}")
            self._trigger_render_update()
            self.sim_view.brain_renderer_2d.update_data(None)

    def _trigger_render_update(self):
        if not self.worker.controller or not self.selected_exp_id: return
        frame = self.worker.controller.get_frame(self.selected_exp_id, self.current_display_tick)
        if not frame: return

        selected_obj = None
        if self.inspected_neuron_id is not None: selected_obj = ('neuron', self.inspected_neuron_id)
        elif self.inspected_io_node is not None: selected_obj = self.inspected_io_node

        self.render_command_queue.put({
            "type": "PROCESS_FRAME",
            "frame": frame,
            "positions": self.sim_view.renderer_3d._node_positions,
            "input_ids": self.sim_view.renderer_3d.input_ids_cache,
            "output_ids": self.sim_view.renderer_3d.output_ids_cache,
            "selected_obj": selected_obj
        })
        
    def _poll_evolution_status(self):
        if self.central_stack.currentIndex() == 1:
            self.command_queue.put({"type": "GET_EVO_STATUS"})

    # --- Tree Signal Handlers ---
    @Slot(str)
    def _on_ui_tree_expanded(self, exp_id):
        self.command_queue.put({"type": "FETCH_EXP_CHILDREN", "parent_id": exp_id})

    def closeEvent(self, event):
        self.command_queue.put({"type": "STOP"})
        self.render_command_queue.put({"type": "STOP"})
        self.worker_thread.quit()
        self.render_worker_thread.quit()
        self.worker_thread.wait()
        self.render_worker_thread.wait()
        event.accept()