# simulation_worker.py
import queue
import traceback
from PySide6.QtCore import QObject, Signal, Slot

from simulation_controller import SimulationController
from hidra_api_client import HidraApiClient, HidraApiException

class WorkerSignals(QObject):
    """
    Defines the signals available from a running worker thread.
    """
    connection_result = Signal(bool, str, str) # success, url, error_str
    replay_loaded = Signal(str, str) # exp_id, exp_name
    replay_saved = Signal(str, str) # path, message
    assembly_result = Signal(bool, str) # success, result_string (bytecode or error)
    decompilation_result = Signal(bool, str) # success, result_string (source code or error)
    live_status_update = Signal(dict) # status dict
    experiment_list = Signal(list)
    experiment_created = Signal(dict)
    experiment_deleted = Signal(str) # exp_id
    experiment_selected = Signal(str)
    new_frame_data = Signal(object) # ReplayFrame object
    logs_updated = Signal(list) # A list of log entry dicts from the server
    step_failed = Signal()
    status_update = Signal(str, str) # message, level
    connection_lost = Signal()


class SimulationWorker(QObject):
    """
    Manages API communication in a separate thread.
    """
    def __init__(self, command_q: queue.Queue):
        super().__init__()
        self.command_q = command_q
        self.signals = WorkerSignals()
        self.controller: SimulationController | None = None
        self._is_running = True
        self._initial_log_level = "Info" # Default

    @Slot()
    def run(self):
        print("INFO: API worker thread started.")
        while self._is_running:
            try:
                command = self.command_q.get(timeout=0.1)
                cmd_type = command.get("type")

                # --- Handle commands ---
                if cmd_type == "STOP":
                    self._is_running = False
                    continue

                if cmd_type == "CONNECT":
                    try:
                        api_client = HidraApiClient(base_url=command["url"])
                        api_client.hgl.get_specification()
                        self.controller = SimulationController(api_client=api_client)
                        self._initial_log_level = command.get("log_level", "Info")
                        self.signals.connection_result.emit(True, command["url"], "")
                    except HidraApiException as e:
                        self.signals.connection_result.emit(False, command["url"], str(e))
                
                elif cmd_type == "LOAD_FILE":
                    try:
                        api_client = HidraApiClient(base_url="http://localhost:5000") # Dummy for offline
                        self.controller = SimulationController(api_client=api_client)
                        result = self.controller.load_from_file(command["path"])
                        if result:
                            exp_id, exp_name = result
                            self.signals.replay_loaded.emit(exp_id, exp_name)
                        else:
                            self.signals.status_update.emit("Failed to load replay: Invalid file format.", "error")
                    except (IOError, Exception) as e:
                        self.signals.status_update.emit(f"Failed to load replay file: {e}", "error")

                elif cmd_type == "CREATE_EXPERIMENT":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        new_exp = self.controller.api_client.experiments.create(
                            name=command["name"], 
                            hgl_genome=command["genome"],
                            io_config=command.get("io_config")
                        )
                        self.signals.experiment_created.emit(new_exp)
                    except HidraApiException as e:
                        self.signals.status_update.emit(f"Failed to create experiment: {e}", "error")

                elif cmd_type == "DELETE_EXPERIMENT":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        exp_id_to_delete = command["exp_id"]
                        self.controller.api_client.experiments.delete(exp_id_to_delete)
                        self.signals.experiment_deleted.emit(exp_id_to_delete)
                    except HidraApiException as e:
                        self.signals.status_update.emit(f"Failed to delete experiment: {e}", "error")

                elif cmd_type == "SAVE_REPLAY":
                    if not self.controller: continue
                    try:
                        self.controller.save_replay_to_file(command["exp_id"], command["path"])
                        self.signals.replay_saved.emit(command["path"], "Replay saved successfully.")
                    except (IOError, ValueError) as e:
                        self.signals.status_update.emit(f"Failed to save replay: {e}", "error")

                elif cmd_type == "ASSEMBLE_HGL":
                    if not self.controller or not self.controller.api_client: continue
                    try:
                        result = self.controller.api_client.assembler.assemble(command["source"])
                        bytecode = result.get("hexBytecode", "")
                        self.signals.assembly_result.emit(True, bytecode)
                    except HidraApiException as e:
                        self.signals.assembly_result.emit(False, str(e))

                elif cmd_type == "DECOMPILE_HGL":
                    if not self.controller or not self.controller.api_client: continue
                    try:
                        result = self.controller.api_client.assembler.decompile(command["bytecode"])
                        source_code = result.get("sourceCode", "")
                        self.signals.decompilation_result.emit(True, source_code)
                    except HidraApiException as e:
                        self.signals.decompilation_result.emit(False, str(e))
                
                elif cmd_type == "SET_LOG_LEVEL":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        exp_id = command["exp_id"]
                        level = command["level"]
                        self.controller.api_client.logging.set_minimum_log_level(exp_id, level)
                        self.signals.status_update.emit(f"Log level for {exp_id} set to {level}.", "success")
                    except HidraApiException as e:
                        self.signals.status_update.emit(f"Failed to set log level: {e}", "error")

                elif cmd_type == "GET_LIVE_STATUS":
                    if not self.controller or self.controller.is_offline or not command.get("exp_id"): continue
                    try:
                        status = self.controller.api_client.query.get_status(command["exp_id"])
                        self.signals.live_status_update.emit(status)
                    except HidraApiException as e:
                        self.signals.status_update.emit(f"Failed to get live status: {e}", "error")

                elif cmd_type == "ATOMIC_STEP":
                    if not self.controller or self.controller.is_offline: continue
                    
                    exp_id = command["exp_id"]
                    new_frame = self.controller.step_with_inputs(
                        exp_id, command["inputs"], command["outputs_to_read"]
                    )
                    
                    if new_frame:
                        self.signals.new_frame_data.emit(new_frame)
                        
                        # Now, automatically fetch logs after the step
                        try:
                            logs = self.controller.api_client.query.get_logs(exp_id)
                            self.signals.logs_updated.emit(logs)
                        except HidraApiException as e:
                            self.signals.status_update.emit(f"Failed to auto-refresh logs: {e}", "error")
                    else:
                        self.signals.step_failed.emit()

                elif cmd_type == "REFRESH_EXPERIMENTS":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        experiments = self.controller.api_client.experiments.list()
                        self.signals.experiment_list.emit(experiments)
                    except HidraApiException as e:
                        self.signals.status_update.emit(f"Failed to fetch experiments: {e}", "error")

                elif cmd_type == "SELECT_EXPERIMENT":
                     if self.controller and self.controller.connect(command["exp_id"]):
                        # If an initial log level was set at connection, apply it now.
                        if not self.controller.is_offline and self._initial_log_level:
                            try:
                                self.controller.api_client.logging.set_minimum_log_level(command["exp_id"], self._initial_log_level)
                                self.signals.status_update.emit(f"Initial log level set to {self._initial_log_level}", "info")
                                self._initial_log_level = None # Clear it so it only applies once
                            except HidraApiException as e:
                                self.signals.status_update.emit(f"Failed to set initial log level: {e}", "error")

                        self.signals.experiment_selected.emit(command["exp_id"])
                     else:
                        self.signals.status_update.emit(f"Failed to connect to {command['exp_id']}", "error")

            except queue.Empty:
                continue
            except Exception as e:
                print(f"CRITICAL: Worker loop crashed: {e}")
                traceback.print_exc()
                self.signals.status_update.emit(f"Worker crashed: {e}", "critical")
        
        print("INFO: API worker thread finished.")