# simulation_worker.py
import queue
import traceback
from PySide6.QtCore import QObject, Signal, Slot

from simulation_controller import SimulationController
from hidra_api_client import HidraApiClient, HidraApiException

class WorkerSignals(QObject):
    # Connection & Lifecycle
    connection_result = Signal(bool, str, str) 
    replay_loaded = Signal(str, str)
    replay_saved = Signal(str, str)
    
    # HGL Tools
    assembly_result = Signal(bool, str)
    decompilation_result = Signal(bool, str)
    
    # Status Updates
    live_status_update = Signal(dict) 
    
    # Experiment Management
    experiment_list = Signal(list)
    experiment_children = Signal(str, list)
    experiment_created = Signal(dict)
    experiment_deleted = Signal(str)
    experiment_selected = Signal(str)
    
    # Simulation Data
    new_frame_data = Signal(object) 
    logs_updated = Signal(list)
    step_failed = Signal()
    history_refreshed = Signal(int, int) # count, max_tick
    run_execution_result = Signal(bool, str)
    
    # General UI Feedback
    status_update = Signal(str, str)
    connection_lost = Signal()


class SimulationWorker(QObject):
    def __init__(self, command_q: queue.Queue):
        super().__init__()
        self.command_q = command_q
        self.signals = WorkerSignals()
        self.controller: SimulationController | None = None
        self._is_running = True
        self._initial_log_level = "Info"

    @Slot()
    def run(self):
        print("INFO: API worker thread started.")
        while self._is_running:
            try:
                command = self.command_q.get(timeout=0.1)
                cmd_type = command.get("type")

                if cmd_type == "STOP":
                    self._is_running = False
                    continue

                # --- Connection ---
                if cmd_type == "CONNECT":
                    try:
                        api_client = HidraApiClient(base_url=command["url"])
                        # Test connection
                        api_client.hgl.get_specification()
                        
                        self.controller = SimulationController(api_client=api_client)
                        self._initial_log_level = command.get("log_level", "Info")
                        self.signals.connection_result.emit(True, command["url"], "")
                    except (HidraApiException, Exception) as e:
                        self.signals.connection_result.emit(False, command["url"], str(e))
                
                elif cmd_type == "LOAD_FILE":
                    try:
                        api_client = HidraApiClient(base_url="http://localhost:5000") 
                        self.controller = SimulationController(api_client=api_client)
                        result = self.controller.load_from_file(command["path"])
                        if result:
                            exp_id, exp_name = result
                            self.signals.replay_loaded.emit(exp_id, exp_name)
                        else:
                            self.signals.status_update.emit("Failed to load replay: Invalid file format.", "error")
                    except (IOError, Exception) as e:
                        self.signals.status_update.emit(f"Failed to load replay file: {e}", "error")

                # --- Experiment Management ---
                elif cmd_type == "REFRESH_EXPERIMENTS":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        experiments = self.controller.api_client.experiments.list()
                        self.signals.experiment_list.emit(experiments)
                    except HidraApiException as e:
                        self.signals.status_update.emit(f"Failed to fetch experiments: {e}", "error")

                elif cmd_type == "FETCH_EXP_CHILDREN":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        parent_id = command["parent_id"]
                        children = self.controller.api_client.experiments.list(parent_id=parent_id)
                        self.signals.experiment_children.emit(parent_id, children)
                    except HidraApiException as e:
                        self.signals.status_update.emit(f"Failed to fetch children for {parent_id}: {e}", "error")

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

                elif cmd_type == "CLONE_EXPERIMENT":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        new_exp = self.controller.api_client.experiments.clone(
                            exp_id=command["source_id"],
                            name=command["name"],
                            tick=command["tick"]
                        )
                        self.signals.experiment_created.emit(new_exp)
                    except HidraApiException as e:
                        self.signals.status_update.emit(f"Failed to clone experiment: {e}", "error")

                elif cmd_type == "DELETE_EXPERIMENT":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        exp_id_to_delete = command["exp_id"]
                        self.controller.api_client.experiments.delete(exp_id_to_delete)
                        self.signals.experiment_deleted.emit(exp_id_to_delete)
                    except HidraApiException as e:
                        self.signals.status_update.emit(f"Failed to delete experiment: {e}", "error")

                elif cmd_type == "RENAME_EXPERIMENT":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        self.controller.api_client.experiments.rename(command["exp_id"], command["new_name"])
                        self.signals.status_update.emit(f"Renamed to {command['new_name']}", "success")
                        experiments = self.controller.api_client.experiments.list()
                        self.signals.experiment_list.emit(experiments)
                    except HidraApiException as e:
                        self.signals.status_update.emit(f"Rename failed: {e}", "error")

                elif cmd_type == "SAVE_REPLAY":
                    if not self.controller: continue
                    try:
                        self.controller.save_replay_to_file(command["exp_id"], command["path"])
                        self.signals.replay_saved.emit(command["path"], "Replay saved successfully.")
                    except (IOError, ValueError) as e:
                        self.signals.status_update.emit(f"Failed to save replay: {e}", "error")

                # --- Selection & Initialization ---
                elif cmd_type == "SELECT_EXPERIMENT":
                     if self.controller and self.controller.connect(command["exp_id"]):
                        if not self.controller.is_offline and self._initial_log_level:
                            try:
                                self.controller.api_client.logging.set_minimum_log_level(command["exp_id"], self._initial_log_level)
                                self.signals.status_update.emit(f"Initial log level set to {self._initial_log_level}", "info")
                                self._initial_log_level = None 
                            except HidraApiException as e:
                                self.signals.status_update.emit(f"Failed to set initial log level: {e}", "error")

                        self.signals.experiment_selected.emit(command["exp_id"])
                     else:
                        self.signals.status_update.emit(f"Failed to connect to {command['exp_id']}", "error")

                # --- Live Control & Sync ---
                elif cmd_type == "REFRESH_HISTORY":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        exp_id = command["exp_id"]
                        count = self.controller.refresh_history(exp_id)
                        
                        latest = self.controller.get_latest_frame(exp_id)
                        max_tick = latest.tick if latest else 0
                        
                        # Emits count and max_tick, but DOES NOT emit new_frame_data automatically.
                        # The UI must request the specific frame it wants to see.
                        self.signals.history_refreshed.emit(count, max_tick)
                            
                    except HidraApiException as e:
                        self.signals.status_update.emit(f"Failed to refresh history: {e}", "error")

                elif cmd_type == "ATOMIC_STEP":
                    if not self.controller or self.controller.is_offline: continue
                    
                    exp_id = command["exp_id"]
                    new_frame = self.controller.step_with_inputs(
                        exp_id, command["inputs"], command["outputs_to_read"]
                    )
                    
                    if new_frame:
                        self.signals.new_frame_data.emit(new_frame)
                        try:
                            logs = self.controller.api_client.query.get_logs(exp_id)
                            self.signals.logs_updated.emit(logs)
                        except HidraApiException:
                            pass 
                    else:
                        self.signals.step_failed.emit()

                elif cmd_type == "EXECUTE_RUN":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        resp = self.controller.api_client.run_control.create_run(
                            exp_id=command["exp_id"],
                            run_type=command["run_type"],
                            parameters=command["params"]
                        )
                        self.signals.status_update.emit(f"Run started: {resp.get('id')}", "info")
                        self.signals.run_execution_result.emit(True, "Run started successfully.")
                    except Exception as e:
                        self.signals.run_execution_result.emit(False, str(e))
                        self.signals.status_update.emit(f"Run execution failed: {e}", "error")

                # --- HGL Tools ---
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
                
                # --- Evolution Controls ---
                elif cmd_type == "EVO_START":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        self.controller.api_client.evolution.start(command["config"])
                        self.signals.status_update.emit("Evolution started successfully.", "success")
                    except Exception as e:
                        self.signals.status_update.emit(f"Evolution start failed: {e}", "error")

                elif cmd_type == "EVO_STOP":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        self.controller.api_client.evolution.stop()
                        self.signals.status_update.emit("Evolution stopped.", "info")
                    except Exception as e:
                        self.signals.status_update.emit(f"Stop failed: {e}", "error")

                elif cmd_type == "GET_EVO_STATUS":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        status = self.controller.api_client.evolution.get_status()
                        self.signals.live_status_update.emit(status)
                    except Exception:
                        pass

                elif cmd_type == "EVO_LOAD_GEN":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        resp = self.controller.api_client.evolution.load_generation(command["index"])
                        new_exp_id = resp.get("experimentId")
                        self.signals.status_update.emit(f"Created standalone experiment from Gen {command['index']}: {new_exp_id}", "success")
                        experiments = self.controller.api_client.experiments.list()
                        self.signals.experiment_list.emit(experiments)
                    except Exception as e:
                        self.signals.status_update.emit(f"Load gen failed: {e}", "error")

                elif cmd_type == "EVO_EXPORT_CSV":
                    if not self.controller or self.controller.is_offline: continue
                    try:
                        csv_data = self.controller.api_client.evolution.get_csv_export()
                        path = command["path"]
                        with open(path, "w", encoding="utf-8") as f:
                            f.write(csv_data)
                        self.signals.status_update.emit(f"Exported CSV to {path}", "success")
                    except Exception as e:
                        self.signals.status_update.emit(f"CSV Export failed: {e}", "error")

                elif cmd_type == "GET_LIVE_STATUS":
                    if not self.controller or self.controller.is_offline or not command.get("exp_id"): continue
                    try:
                        status = self.controller.api_client.query.get_status(command["exp_id"])
                        self.signals.live_status_update.emit(status)
                    except HidraApiException as e:
                        self.signals.status_update.emit(f"Failed to get live status: {e}", "error")

            except queue.Empty:
                continue
            except Exception as e:
                print(f"CRITICAL: Worker loop crashed: {e}")
                traceback.print_exc()
                self.signals.status_update.emit(f"Worker crashed: {e}", "critical")
        
        print("INFO: API worker thread finished.")