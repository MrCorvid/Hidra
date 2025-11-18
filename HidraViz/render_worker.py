# render_worker.py
import queue
import traceback
from dataclasses import dataclass
from typing import Dict, Any, List, Tuple, Optional

import numpy as np
import pyvista as pv
from PySide6.QtCore import QObject, Signal, Slot

from simulation_controller import ReplayFrame

@dataclass
class RenderPayload:
    """A bundle of pre-computed PyVista meshes ready for display."""
    idle_neurons: pv.PolyData | None = None
    firing_neurons: pv.PolyData | None = None
    executing_neurons: pv.PolyData | None = None
    both_neurons: pv.PolyData | None = None
    input_nodes: pv.PolyData | None = None
    output_nodes: pv.PolyData | None = None
    active_io_glow: pv.PolyData | None = None
    normal_synapses: pv.PolyData | None = None
    normal_arrows: pv.PolyData | None = None
    firing_synapses: pv.PolyData | None = None
    firing_arrows: pv.PolyData | None = None
    selection_highlight: pv.PolyData | None = None # <-- NEW FIELD

class RenderWorkerSignals(QObject):
    render_ready = Signal(object)
    status_update = Signal(str, str)

class RenderWorker(QObject):
    """
    Performs the heavy lifting of creating 3D geometry in a background thread.
    """
    def __init__(self, command_q: queue.Queue):
        super().__init__()
        self.command_q = command_q
        self.signals = RenderWorkerSignals()
        self._is_running = True

    @Slot()
    def run(self):
        print("INFO: Render worker thread started.")
        while self._is_running:
            try:
                command = self.command_q.get(timeout=0.1)
                cmd_type = command.get("type")

                if cmd_type == "STOP":
                    self._is_running = False
                    continue

                if cmd_type == "PROCESS_FRAME":
                    # --- START: MODIFICATION ---
                    # Unpack the new selected_obj from the command
                    selected_obj: Optional[Tuple[str, int]] = command.get("selected_obj")
                    # --- END: MODIFICATION ---
                    
                    frame: ReplayFrame = command["frame"]
                    node_positions: Dict[Any, np.ndarray] = command["positions"]
                    input_ids: set = command["input_ids"]
                    output_ids: set = command["output_ids"]
                    
                    payload = self.process_frame(frame, node_positions, input_ids, output_ids, selected_obj)
                    self.signals.render_ready.emit(payload)

            except queue.Empty:
                continue
            except Exception as e:
                print(f"CRITICAL: Render worker crashed: {e}")
                traceback.print_exc()
                self.signals.status_update.emit(f"Render worker crashed: {e}", "critical")
        
        print("INFO: Render worker thread finished.")

    def _create_pickable_mesh(self, ids: list, positions: dict, node_type: str, key_prefix: str) -> pv.PolyData | None:
        if not ids: return None
        points_list = [positions.get((key_prefix, nid)) for nid in ids]
        valid_points_with_ids = [(p, ids[i]) for i, p in enumerate(points_list) if p is not None]
        if not valid_points_with_ids: return None
        valid_points, valid_ids = zip(*valid_points_with_ids)
        mesh = pv.PolyData(np.array(valid_points))
        
        # Encode type into the ID using different ranges:
        # input nodes: original_id (0-999)
        # neurons: original_id + 10000 (10000-10999, etc.)
        # output nodes: original_id + 20000 (20000-20999, etc.)
        encoded_ids = []
        for original_id in valid_ids:
            if node_type == 'input':
                encoded_ids.append(original_id)
            elif node_type == 'neuron':
                encoded_ids.append(original_id + 10000)
            elif node_type == 'output':
                encoded_ids.append(original_id + 20000)
            else:
                encoded_ids.append(original_id)  # fallback
        
        mesh.point_data['object_ids'] = np.array(encoded_ids)
        
        # Keep the types array for debugging, but don't rely on it
        mesh.point_data['object_types'] = np.full(len(valid_ids), node_type, dtype='<U10')
        
        return mesh

    def process_frame(self, frame, node_positions, input_ids_cache, output_ids_cache, selected_obj) -> RenderPayload:
        snapshot = frame.snapshot
        active_input_ids = {int(nid) for nid, val in snapshot.get('inputNodeValues', {}).items() if val != 0.0}
        firing_neuron_ids, gene_exec_neuron_ids, active_output_ids = set(), set(), set()

        for event in frame.events:
            event_type, target_id = event.get('type', ''), event.get('targetId')
            if target_id is None: continue
            if event_type == 'Activate': firing_neuron_ids.add(target_id)
            elif event_type in ['ExecuteGene', 'ExecuteGeneFromBrain']: gene_exec_neuron_ids.add(target_id)
            elif event_type == 'PotentialPulse' and event.get('payload', {}).get('pulseValue', 0) != 0:
                active_output_ids.add(target_id)
        
        active_source_ids = active_input_ids | firing_neuron_ids
        neuron_ids = {n['id'] for n in snapshot.get('neurons', [])}

        firing_and_executing = firing_neuron_ids.intersection(gene_exec_neuron_ids)
        firing_only = firing_neuron_ids - gene_exec_neuron_ids
        executing_only = gene_exec_neuron_ids - firing_neuron_ids
        idle_neurons = neuron_ids - firing_neuron_ids - gene_exec_neuron_ids

        payload = RenderPayload()
        payload.idle_neurons = self._create_pickable_mesh(list(idle_neurons), node_positions, 'neuron', 'neuron')
        payload.firing_neurons = self._create_pickable_mesh(list(firing_only), node_positions, 'neuron', 'neuron')
        payload.executing_neurons = self._create_pickable_mesh(list(executing_only), node_positions, 'neuron', 'neuron')
        payload.both_neurons = self._create_pickable_mesh(list(firing_and_executing), node_positions, 'neuron', 'neuron')
        
        payload.input_nodes = self._create_pickable_mesh(list(input_ids_cache), node_positions, 'input', 'input')
        payload.output_nodes = self._create_pickable_mesh(list(output_ids_cache), node_positions, 'output', 'output')

        # --- START: NEW FEATURE LOGIC ---
        # Create the highlight mesh if an object is selected
        if selected_obj:
            obj_type, obj_id = selected_obj
            pos_key = (obj_type, obj_id)
            
            if pos_key in node_positions:
                point_pos = node_positions[pos_key]
                payload.selection_highlight = pv.PolyData([point_pos])
        # --- END: NEW FEATURE LOGIC ---
                
        active_io_keys = {('input', nid) for nid in active_input_ids} | {('output', nid) for nid in active_output_ids}
        if active_io_keys:
            points = np.array([node_positions[key] for key in active_io_keys if key in node_positions])
            if points.size > 0: payload.active_io_glow = pv.PolyData(points)

        normal_lines, firing_lines, normal_arrows, firing_arrows = [], [], [], []
        for synapse in snapshot.get('synapses', []):
            source_id, target_id = synapse['sourceId'], synapse['targetId']
            source_pos = node_positions.get(('input' if source_id in input_ids_cache else 'neuron', source_id))
            target_pos = node_positions.get(('output' if target_id in output_ids_cache else 'neuron', target_id))

            if source_pos is not None and target_pos is not None:
                direction = target_pos - source_pos
                norm = np.linalg.norm(direction)
                if norm < 1e-6: continue
                
                direction_norm, arrow_pos = direction / norm, target_pos - direction / norm * 2.5
                arrow = pv.Cone(center=arrow_pos, direction=direction_norm, height=2.0, radius=0.7)
                
                if source_id in active_source_ids:
                    firing_lines.append(pv.Tube(pointa=source_pos, pointb=target_pos, radius=0.1))
                    firing_arrows.append(arrow)
                else:
                    normal_lines.append(pv.Tube(pointa=source_pos, pointb=target_pos, radius=0.05))
                    normal_arrows.append(arrow)

        if normal_lines:
            payload.normal_synapses = pv.MultiBlock(normal_lines).combine()
            payload.normal_arrows = pv.MultiBlock(normal_arrows).combine()
        if firing_lines:
            payload.firing_synapses = pv.MultiBlock(firing_lines).combine()
            payload.firing_arrows = pv.MultiBlock(firing_arrows).combine()
            
        return payload