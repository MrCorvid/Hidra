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
        
        # Enable point picking with a callback that should work with QtInteractor
        # Note: By default, point picking uses right-click. We can change this behavior.
        self.plotter.enable_point_picking(callback=self._on_point_picked, use_airtight=True, show_point=False)
        
        # Change picking to left-click instead of right-click
        self.plotter.picker.SetTolerance(0.005)  # Make picking a bit more forgiving
        
        # Also try adding a direct VTK observer as backup
        self.plotter.iren.add_observer("LeftButtonPressEvent", self._on_mouse_click)

    def _on_mouse_click(self, obj, event):
        """
        Basic mouse click detection - this should always fire.
        """
        print("DEBUG: Mouse click detected via VTK observer!")
        
    def _on_point_picked(self, picked_point):
        """
        PyVista's callback for point picking.
        """
        print("DEBUG: Point picked via PyVista callback!")
        self._process_pick_result()

    def _process_pick_result(self, picked_point=None):
        """
        Processes the result of a pick operation using PyVista's built-in picking.
        """
        print("DEBUG: Processing pick result...")
        
        # Check different possible attributes for QtInteractor
        picked_mesh = getattr(self.plotter, 'picked_mesh', None)
        picked_actor = getattr(self.plotter, 'picked_actor', None)
        picked_point_coords = getattr(self.plotter, 'picked_point', None)
        picked_point_id = getattr(self.plotter, 'picked_point_id', None)
        
        print(f"DEBUG: picked_mesh: {picked_mesh}")
        print(f"DEBUG: picked_actor: {picked_actor}")
        print(f"DEBUG: picked_point_coords: {picked_point_coords}")
        print(f"DEBUG: picked_point_id: {picked_point_id}")
        
        # Try to get the mesh from the actor if mesh is None
        if picked_mesh is None and picked_actor is not None:
            # Get the mapper from the actor to find the mesh
            mapper = picked_actor.GetMapper()
            if mapper:
                picked_mesh = mapper.GetInput()
                print(f"DEBUG: Got mesh from actor: {picked_mesh}")
        
        # Check the plotter's picker for additional info
        if hasattr(self.plotter, 'picker') and self.plotter.picker:
            picker = self.plotter.picker
            picked_point_id = getattr(picker, 'GetPointId', lambda: -1)()
            print(f"DEBUG: Picker point ID: {picked_point_id}")
            
            # Try to get the picked data set
            picked_dataset = getattr(picker, 'GetDataSet', lambda: None)()
            if picked_dataset:
                picked_mesh = picked_dataset
                print(f"DEBUG: Got mesh from picker: {picked_mesh}")

        if picked_mesh and hasattr(picked_mesh, 'GetPointData') and picked_point_id is not None and picked_point_id >= 0:
            try:
                point_data = picked_mesh.GetPointData()
                print(f"DEBUG: Point data arrays: {[point_data.GetArrayName(i) for i in range(point_data.GetNumberOfArrays())]}")
                
                # Try different ways to get the arrays
                obj_ids_array = None
                obj_types_array = None
                
                # Method 1: By name
                for i in range(point_data.GetNumberOfArrays()):
                    array_name = point_data.GetArrayName(i)
                    array = point_data.GetArray(i)
                    print(f"DEBUG: Array {i}: name='{array_name}', array={array}")
                    
                    if array_name == 'object_ids':
                        obj_ids_array = array
                        if obj_ids_array:
                            print(f"DEBUG: Found object_ids array with {obj_ids_array.GetNumberOfTuples()} values")
                    elif array_name == 'object_types':
                        obj_types_array = array
                        if obj_types_array:
                            print(f"DEBUG: Found object_types array with {obj_types_array.GetNumberOfTuples()} values")
                        else:
                            print("DEBUG: object_types array is None")
                
                if obj_ids_array and picked_point_id < obj_ids_array.GetNumberOfTuples():
                    encoded_id = int(obj_ids_array.GetValue(picked_point_id))
                    
                    # Decode the ID to get original type and ID
                    if encoded_id >= 20000:
                        obj_type = "output"
                        obj_id = encoded_id - 20000
                    elif encoded_id >= 10000:
                        obj_type = "neuron"
                        obj_id = encoded_id - 10000
                    else:
                        obj_type = "input"
                        obj_id = encoded_id
                    
                    # Try to get object_types array if available, but don't rely on it
                    if obj_types_array and picked_point_id < obj_types_array.GetNumberOfTuples():
                        try:
                            obj_type_raw = obj_types_array.GetValue(picked_point_id)
                            if hasattr(obj_type_raw, 'decode'):
                                obj_type_from_array = obj_type_raw.decode('utf-8')
                            else:
                                obj_type_from_array = str(obj_type_raw)
                            print(f"DEBUG: Type from array: {obj_type_from_array}, Type from ID: {obj_type}")
                        except Exception as e:
                            print(f"DEBUG: Error getting object type from array: {e}")
                    
                    print(f"DEBUG: Selected object - Type: {obj_type}, ID: {obj_id} (encoded as {encoded_id})")
                    print("DEBUG: About to emit object_selected signal")
                    self.object_selected.emit(obj_type, obj_id)
                    print("DEBUG: Signal emitted successfully")
                else:
                    print(f"DEBUG: Arrays not found or invalid point ID")
                    print(f"DEBUG: obj_ids_array: {obj_ids_array is not None}")
                    print(f"DEBUG: obj_types_array: {obj_types_array is not None}")
                    print(f"DEBUG: picked_point_id: {picked_point_id}")
                    if obj_ids_array:
                        print(f"DEBUG: Array size: {obj_ids_array.GetNumberOfTuples()}")
            except Exception as e:
                print(f"DEBUG: Error retrieving pick data: {e}")
                import traceback
                traceback.print_exc()
        else:
            print("DEBUG: No valid pick result found")
            print(f"DEBUG: picked_mesh valid: {picked_mesh is not None}")
            print(f"DEBUG: picked_point_id valid: {picked_point_id is not None and picked_point_id >= 0}")

    def clear_scene(self):
        for actor in self.actors:
            self.plotter.remove_actor(actor, render=False)
        self.actors.clear()
        self.plotter.render()
    
    def display_payload(self, payload: RenderPayload):
        new_actors = []

        def _add_and_track_actor(pickable, *args, **kwargs):
            actor = self.plotter.add_mesh(*args, **kwargs, render=False, pickable=pickable)
            new_actors.append(actor)

        if payload.idle_neurons: _add_and_track_actor(True, payload.idle_neurons, color='#6666CC', render_points_as_spheres=True, point_size=36)
        if payload.firing_neurons: _add_and_track_actor(True, payload.firing_neurons, color='yellow', render_points_as_spheres=True, point_size=36)
        if payload.executing_neurons: _add_and_track_actor(True, payload.executing_neurons, color='red', render_points_as_spheres=True, point_size=36)
        if payload.both_neurons: _add_and_track_actor(True, payload.both_neurons, color='white', render_points_as_spheres=True, point_size=36)
        
        if payload.input_nodes: _add_and_track_actor(True, payload.input_nodes, color='#33CC33', render_points_as_spheres=True, point_size=24)
        if payload.output_nodes: _add_and_track_actor(True, payload.output_nodes, color='#CC3333', render_points_as_spheres=True, point_size=24)
        
        if payload.selection_highlight:
            _add_and_track_actor(False, payload.selection_highlight, color='white', render_points_as_spheres=True, point_size=42, opacity=0.8)

        if payload.active_io_glow: _add_and_track_actor(False, payload.active_io_glow, color='yellow', render_points_as_spheres=True, point_size=30, opacity=0.3)
        
        if payload.normal_synapses: _add_and_track_actor(False, payload.normal_synapses, color=(0.5, 0.5, 0.6))
        if payload.normal_arrows: _add_and_track_actor(False, payload.normal_arrows, color=(0.5, 0.5, 0.6))
        if payload.firing_synapses: _add_and_track_actor(False, payload.firing_synapses, color='yellow')
        if payload.firing_arrows: _add_and_track_actor(False, payload.firing_arrows, color='yellow')

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