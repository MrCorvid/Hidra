# renderer.py
"""
Manages the 3D PyVista rendering for the Hidra GUI.
This class is responsible for calculating network layout and displaying
pre-computed geometry from the RenderWorker. It should only be called
from the main GUI thread.
"""
from PySide6.QtWidgets import QWidget, QVBoxLayout
from PySide6.QtCore import Signal, Slot
from pyvistaqt import QtInteractor
import pyvista as pv
import numpy as np
from render_worker import RenderPayload

class Renderer3D(QWidget):
    object_selected = Signal(str, int)
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.plotter = QtInteractor(self)
        self.actors = [] 

        layout = QVBoxLayout(self)
        layout.addWidget(self.plotter.interactor)
        layout.setContentsMargins(0, 0, 0, 0)

        self.plotter.add_axes()
        self.plotter.camera_position = 'iso'
        self.plotter.background_color = (0.05, 0.05, 0.1)
        self.plotter.enable_anti_aliasing()

        grid = pv.Plane(center=(0, 0, -0.5), direction=(0, 0, 1), i_size=200, j_size=200, i_resolution=20, j_resolution=20)
        self.plotter.add_mesh(grid, color='white', opacity=0.1, style='wireframe', pickable=False)

        self._node_positions = {}
        self._topology_hash = None
        self.input_ids_cache = set()
        self.output_ids_cache = set()
        
        # Enable point picking. 
        # 'use_mesh' ensures the mesh is passed to the callback.
        # 'show_message=False' prevents the default overlay text.
        self.plotter.enable_point_picking(
            callback=self._on_pick, 
            show_message=False,
            use_mesh=True,
            show_point=False
        )
        
        # Increase tolerance to make clicking neurons (points) easier.
        # 0.02 represents 2% of the render window diagonal.
        if hasattr(self.plotter, 'picker'):
            self.plotter.picker.SetTolerance(0.02)

    def _on_pick(self, mesh, idx):
        """
        Callback triggered by PyVista when a point is picked.
        Args:
            mesh: The PyVista mesh object that was clicked.
            idx: The index of the point within that mesh.
        """
        if mesh is None or idx is None or idx < 0:
            return

        try:
            # Check if this mesh has our custom object IDs
            if 'object_ids' not in mesh.point_data:
                return

            obj_ids_array = mesh.point_data['object_ids']
            
            if idx < len(obj_ids_array):
                encoded_id = int(obj_ids_array[idx])
                
                # Decode the ID
                # 20000+ = Output Node
                # 10000+ = Neuron
                # 0-9999 = Input Node
                
                if encoded_id >= 20000:
                    obj_type = "output"
                    obj_id = encoded_id - 20000
                elif encoded_id >= 10000:
                    obj_type = "neuron"
                    obj_id = encoded_id - 10000
                else:
                    obj_type = "input"
                    obj_id = encoded_id
                
                print(f"INFO: Picked {obj_type} {obj_id}")
                self.object_selected.emit(obj_type, obj_id)

        except Exception as e:
            print(f"ERROR: Picking logic failed: {e}")

    def clear_scene(self):
        for actor in self.actors:
            self.plotter.remove_actor(actor, render=False)
        self.actors.clear()
        self.plotter.render()
    
    def display_payload(self, payload: RenderPayload):
        new_actors = []
        
        def _add_and_track_actor(pickable, *args, **kwargs):
            # Ensure pickable is explicitly passed. 
            # render_points_as_spheres is visual only; the underlying geometry is still points.
            actor = self.plotter.add_mesh(*args, **kwargs, render=False, pickable=pickable)
            new_actors.append(actor)

        # Render Neurons (Pickable)
        if payload.idle_neurons: 
            _add_and_track_actor(True, payload.idle_neurons, color='#6666CC', render_points_as_spheres=True, point_size=36)
        if payload.firing_neurons: 
            _add_and_track_actor(True, payload.firing_neurons, color='yellow', render_points_as_spheres=True, point_size=36)
        if payload.executing_neurons: 
            _add_and_track_actor(True, payload.executing_neurons, color='red', render_points_as_spheres=True, point_size=36)
        if payload.both_neurons: 
            _add_and_track_actor(True, payload.both_neurons, color='white', render_points_as_spheres=True, point_size=36)
        
        # Render I/O Nodes (Pickable)
        if payload.input_nodes: 
            _add_and_track_actor(True, payload.input_nodes, color='#33CC33', render_points_as_spheres=True, point_size=24)
        if payload.output_nodes: 
            _add_and_track_actor(True, payload.output_nodes, color='#CC3333', render_points_as_spheres=True, point_size=24)
        
        # Render Selection Highlight (Not Pickable)
        if payload.selection_highlight:
            _add_and_track_actor(False, payload.selection_highlight, color='white', render_points_as_spheres=True, point_size=42, opacity=0.8)

        # Render Glows (Not Pickable)
        if payload.active_io_glow: 
            _add_and_track_actor(False, payload.active_io_glow, color='yellow', render_points_as_spheres=True, point_size=30, opacity=0.3)
        
        # Render Synapses (Not Pickable for now to avoid clutter)
        if payload.normal_synapses: _add_and_track_actor(False, payload.normal_synapses, color=(0.5, 0.5, 0.6))
        if payload.normal_arrows: _add_and_track_actor(False, payload.normal_arrows, color=(0.5, 0.5, 0.6))
        if payload.firing_synapses: _add_and_track_actor(False, payload.firing_synapses, color='yellow')
        if payload.firing_arrows: _add_and_track_actor(False, payload.firing_arrows, color='yellow')

        # Cleanup old actors to free memory
        for old_actor in self.actors:
            self.plotter.remove_actor(old_actor, render=False)
        
        self.actors = new_actors
        self.plotter.render()

    def _arrange_in_plane(self, ids, node_type, x_coord, spacing=8.0):
        if not ids: return
        count = len(ids)
        grid_dim = int(np.ceil(np.sqrt(count)))
        if grid_dim == 0: return
        plane_size = (grid_dim - 1) * spacing
        for i, nid in enumerate(ids):
            row, col = i // grid_dim, i % grid_dim
            y = row * spacing - plane_size / 2.0
            z = col * spacing - plane_size / 2.0
            self._node_positions[(node_type, nid)] = np.array([x_coord, y, z])

    def _arrange_in_volume(self, ids, node_type, x_start, x_end, spacing=8.0):
        if not ids: return
        count = len(ids)
        grid_dim = int(np.ceil(np.cbrt(count)))
        if grid_dim == 0: return
        volume_size = (grid_dim - 1) * spacing
        for i, nid in enumerate(ids):
            layer, row = i // (grid_dim * grid_dim), (i % (grid_dim * grid_dim)) // grid_dim
            col = i % grid_dim
            x_ratio = (layer / (grid_dim - 1)) if grid_dim > 1 else 0.5
            x = x_start + (x_end - x_start) * x_ratio
            y = row * spacing - volume_size / 2.0
            z = col * spacing - volume_size / 2.0
            self._node_positions[(node_type, nid)] = np.array([x, y, z])
            
    def _apply_force_directed_layout(self, all_node_keys, synapses, iterations=50, k=8.0, temp_initial=10.0):
        if len(all_node_keys) < 2: return
        for i in range(iterations):
            temp = temp_initial * (1.0 - i / iterations)
            displacements = {key: np.zeros(3) for key in all_node_keys}
            for n1_idx in range(len(all_node_keys)):
                for n2_idx in range(n1_idx + 1, len(all_node_keys)):
                    key1, key2 = all_node_keys[n1_idx], all_node_keys[n2_idx]
                    pos1, pos2 = self._node_positions[key1], self._node_positions[key2]
                    delta = pos1 - pos2; delta[0] = 0
                    distance = np.linalg.norm(delta) + 1e-8
                    repulsive_force = (k * k) / distance
                    disp = (delta / distance) * repulsive_force
                    displacements[key1] += disp
                    displacements[key2] -= disp
            for synapse in synapses:
                source_key = ('input' if synapse['sourceId'] in self.input_ids_cache else 'neuron', synapse['sourceId'])
                target_key = ('output' if synapse['targetId'] in self.output_ids_cache else 'neuron', synapse['targetId'])
                if source_key in self._node_positions and target_key in self._node_positions:
                    pos1, pos2 = self._node_positions[source_key], self._node_positions[target_key]
                    delta = pos1 - pos2; delta[0] = 0
                    distance = np.linalg.norm(delta) + 1e-8
                    attractive_force = (distance * distance) / k
                    disp = (delta / distance) * attractive_force
                    displacements[source_key] -= disp
                    displacements[target_key] += disp
            for key in all_node_keys:
                disp = displacements[key]
                disp_norm = np.linalg.norm(disp) + 1e-8
                new_pos = self._node_positions[key] + (disp / disp_norm) * min(disp_norm, temp)
                self._node_positions[key][1], self._node_positions[key][2] = new_pos[1], new_pos[2]

    def update_layout(self, snapshot: dict):
        neurons = snapshot.get('neurons', [])
        synapses = snapshot.get('synapses', [])
        self.input_ids_cache = set(snapshot.get('inputNodeIds', []))
        self.output_ids_cache = set(snapshot.get('outputNodeIds', []))
        all_neuron_ids_set = {n['id'] for n in neurons}
        
        current_hash = (len(neurons), len(synapses), len(self.input_ids_cache), len(self.output_ids_cache))
        
        if current_hash == self._topology_hash and self._topology_hash is not None: return False

        print("INFO: Network topology changed, recalculating structured layout...")
        self._topology_hash = current_hash
        self._node_positions.clear()

        input_neuron_ids = {s['targetId'] for s in synapses if s['sourceId'] in self.input_ids_cache and s['targetId'] in all_neuron_ids_set}
        output_neuron_ids = {s['sourceId'] for s in synapses if s['targetId'] in self.output_ids_cache and s['sourceId'] in all_neuron_ids_set}
        io_neuron_ids = input_neuron_ids.intersection(output_neuron_ids)
        
        final_input_neurons = sorted(list(input_neuron_ids))
        final_output_neurons = sorted(list(output_neuron_ids - io_neuron_ids))
        core_neuron_ids = sorted(list(all_neuron_ids_set - input_neuron_ids - output_neuron_ids))
        
        self._arrange_in_plane(sorted(list(self.input_ids_cache)), 'input', -50.0)
        self._arrange_in_plane(final_input_neurons, 'neuron', -25.0)
        self._arrange_in_volume(core_neuron_ids, 'neuron', -10.0, 10.0)
        self._arrange_in_plane(final_output_neurons, 'neuron', 25.0)
        self._arrange_in_plane(sorted(list(self.output_ids_cache)), 'output', 50.0)
        
        all_node_keys = list(self._node_positions.keys())
        print(f"INFO: Untangling layout for {len(all_node_keys)} nodes...")
        self._apply_force_directed_layout(all_node_keys, synapses)
        print("INFO: Layout untangling complete.")
        return True