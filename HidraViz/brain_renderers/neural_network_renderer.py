# brain_renderers/neural_network_renderer.py
import math
from PySide6.QtGui import QPainter, QColor, QPen, QFont
from PySide6.QtCore import QRect, QPoint, Qt
from .base_renderer import BaseBrainRenderer

class NeuralNetworkRenderer(BaseBrainRenderer):
    """Renders a NeuralNetworkBrain and its multi-step animation."""

    @property
    def supports_animation(self) -> bool:
        return True

    def get_brain_type(self) -> str:
        return "NeuralNetworkBrain"
        
    def prepare_animation_state(self, data: dict, initial_value: float) -> dict | None:
        """Creates the initial state for a firing animation."""
        nodes = data.get("nodes", [])
        if not nodes: return None
        
        # Topologically sort nodes into layers for animation
        in_degree = {node['id']: 0 for node in nodes}
        graph = {node['id']: [] for node in nodes}
        for conn in data.get("connections", []):
            if conn['toNodeId'] not in in_degree or conn['fromNodeId'] not in graph: continue
            in_degree[conn['toNodeId']] += 1
            graph[conn['fromNodeId']].append(conn['toNodeId'])

        queue = [node for node in nodes if in_degree[node['id']] == 0]
        sorted_layers = []
        while queue:
            sorted_layers.append(list(queue))
            next_queue = []
            for node in queue:
                for neighbor_id in graph.get(node['id'], []):
                    in_degree[neighbor_id] -= 1
                    if in_degree[neighbor_id] == 0:
                        # Find the full neighbor node object
                        neighbor_node = next((n for n in nodes if n['id'] == neighbor_id), None)
                        if neighbor_node: next_queue.append(neighbor_node)
            queue = next_queue
        
        return {
            'step': -1,
            'input_value': initial_value,
            'output_value': 0.0,
            'layers': sorted_layers,
            'connections': data.get("connections", []),
            'node_values': {node['id']: node.get('bias', 0.0) for node in nodes},
            'activated_parts': set(), # Will contain IDs of nodes and tuples for connections
        }

    def advance_animation_state(self, state: dict) -> dict | None:
        """Calculates the next step of the animation, layer by layer."""
        state['step'] += 1
        step = state['step']
        
        # Step 0: Activate the main input node
        if step == 0:
            state['activated_parts'].add('input_node')
            # Apply initial value to all of the NN's actual input nodes
            for node in state['layers'][0]:
                state['node_values'][node['id']] += state['input_value']
            return state

        # Step 1...N: Process each layer of the network
        layer_index = step - 1
        if layer_index < len(state['layers']):
            current_layer_nodes = state['layers'][layer_index]
            for node in current_layer_nodes:
                node_id = node['id']
                # Activate the node and connections originating from it
                state['activated_parts'].add(node_id)
                
                node_value = state['node_values'].get(node_id, 0.0)
                activated_value = 1.0 / (1.0 + math.exp(-node_value)) # Sigmoid

                # If this is an output-type node, accumulate its value for the final display
                if node.get('nodeType', 'Hidden') in [2, 'Output']:
                    state['output_value'] += activated_value

                for conn in state['connections']:
                    if conn['fromNodeId'] == node_id:
                        state['activated_parts'].add( (conn['fromNodeId'], conn['toNodeId']) )
                        state['node_values'][conn['toNodeId']] += activated_value * conn.get('weight', 1.0)
            return state

        # Final step: Activate the main output node
        if layer_index == len(state['layers']):
             state['activated_parts'].add('output_node')
             return state

        return None # Animation complete

    def render(self, painter: QPainter, data: dict, rect: QRect, animation_state: dict | None = None):
        nodes, connections = data.get("nodes", []), data.get("connections", [])
        if not nodes:
            painter.setPen(QColor(200, 200, 200)); painter.drawText(rect, Qt.AlignmentFlag.AlignCenter, "Empty Neural Network"); return
        
        activated = animation_state['activated_parts'] if animation_state else set()
        
        # --- Layout ---
        y_center = rect.center().y()
        input_pos = QPoint(rect.left() + 40, y_center)
        output_pos = QPoint(rect.right() - 40, y_center)
        node_radius = 15
        
        # Define a sub-rectangle for the actual network drawing
        network_rect = QRect(input_pos.x() + 30, rect.top(), output_pos.x() - input_pos.x() - 60, rect.height())
        
        # --- Draw Network ---
        layers = {'Input': [], 'Hidden': [], 'Output': []}
        for node in nodes:
            node_type = node.get('nodeType', 'Hidden')
            if node_type in [0, 'Input']: layers['Input'].append(node)
            elif node_type in [2, 'Output']: layers['Output'].append(node)
            else: layers['Hidden'].append(node)

        node_positions = self._calculate_node_positions(layers, network_rect)
        self._draw_network_connections(painter, connections, node_positions, activated)
        self._draw_network_nodes(painter, nodes, node_positions, activated)

        # --- Draw I/O connections to the network ---
        self._draw_io_connections(painter, layers['Input'], node_positions, input_pos, activated, is_input=True)
        self._draw_io_connections(painter, layers['Output'], node_positions, output_pos, activated, is_input=False)
        
        # --- Draw I/O Nodes ---
        def draw_io_node(pos, label, is_active, value_to_show=None):
            painter.setBrush(QColor(60, 60, 90)); painter.setPen(QPen(QColor(150, 150, 180), 2))
            if is_active:
                painter.setBrush(QColor(255, 255, 100)); painter.setPen(QPen(QColor(255, 255, 255), 2))
            painter.drawEllipse(pos, node_radius, node_radius)
            font = QFont("Arial", 9); painter.setFont(font); painter.setPen(QColor(220, 220, 220))
            painter.drawText(QRect(pos.x() - node_radius, pos.y() - node_radius, node_radius*2, node_radius*2), Qt.AlignmentFlag.AlignCenter, label)
            if is_active and value_to_show is not None:
                font.setPointSize(8); painter.setFont(font)
                painter.drawText(QRect(pos.x() - 20, pos.y() + node_radius, 40, 20), Qt.AlignmentFlag.AlignCenter, f"{value_to_show:.2f}")

        input_val = animation_state['input_value'] if animation_state else 0.0
        output_val = animation_state['output_value'] if animation_state else 0.0
        draw_io_node(input_pos, "IN", 'input_node' in activated, input_val)
        draw_io_node(output_pos, "OUT", 'output_node' in activated, output_val)


    def _calculate_node_positions(self, layers: dict, rect: QRect) -> dict:
        # This function now calculates positions within the provided sub-rectangle
        positions = {}
        margin_x, margin_y = 20, 20
        drawable_width = rect.width() - 2 * margin_x
        drawable_height = rect.height() - 2 * margin_y
        num_layers = len([l for l in layers.values() if l])

        if num_layers == 1:
            layer_x_map = {'Input': rect.left() + rect.width() / 2, 'Hidden': -1, 'Output': -1}
        elif num_layers == 2 and not layers['Hidden']:
             layer_x_map = {'Input': rect.left() + margin_x, 'Hidden': -1, 'Output': rect.left() + margin_x + drawable_width}
        else:
            layer_x_map = {'Input': rect.left() + margin_x, 'Hidden': rect.left() + margin_x + drawable_width / 2, 'Output': rect.left() + margin_x + drawable_width}

        for layer_name, nodes in layers.items():
            if not nodes: continue
            x = layer_x_map[layer_name]
            for i, node in enumerate(nodes):
                y_spacing = drawable_height / (len(nodes) + 1) if len(nodes) > 0 else 0
                y = rect.top() + margin_y + (i + 1) * y_spacing
                positions[node['id']] = QPoint(int(x), int(y))
        return positions

    def _draw_network_connections(self, painter: QPainter, connections: list, positions: dict, activated: set):
        for conn in connections:
            from_pos, to_pos = positions.get(conn['fromNodeId']), positions.get(conn['toNodeId'])
            if not from_pos or not to_pos: continue
            is_active = (conn['fromNodeId'], conn['toNodeId']) in activated
            weight = conn.get('weight', 1.0)
            
            if is_active: pen = QPen(QColor(255, 255, 0), 2.5)
            else:
                color = QColor(255, 100, 100, 150) if weight < 0 else QColor(100, 200, 255, 150)
                pen = QPen(color, max(0.5, min(abs(weight), 1.5)))
            painter.setPen(pen)
            painter.drawLine(from_pos, to_pos)

    def _draw_network_nodes(self, painter: QPainter, nodes: list, positions: dict, activated: set):
        radius, font = 10, QFont("Arial", 7)
        painter.setFont(font)
        for node in nodes:
            pos = positions.get(node['id'])
            if not pos: continue
            is_active = node['id'] in activated
            
            node_type = node.get('nodeType')
            if node_type in [0, 'Input']: base_color = QColor("#33CC33")
            elif node_type in [2, 'Output']: base_color = QColor("#CC3333")
            else: base_color = QColor("#6666CC")
            
            if is_active:
                painter.setBrush(QColor(255, 255, 100)); painter.setPen(Qt.PenStyle.NoPen)
                painter.drawEllipse(pos, radius + 2, radius + 2)

            painter.setBrush(base_color); painter.setPen(QPen(QColor(200, 200, 220), 1))
            painter.drawEllipse(pos, radius, radius)
            
            painter.setPen(QColor(0, 0, 0) if is_active else QColor(255, 255, 255))
            text_rect = QRect(pos - QPoint(radius, radius), QPoint(pos.x() + radius, pos.y() + radius))
            painter.drawText(text_rect, Qt.AlignmentFlag.AlignCenter, str(node['id']))
            
    def _draw_io_connections(self, painter: QPainter, network_nodes: list, node_positions: dict, io_node_pos: QPoint, activated: set, is_input: bool):
        for node in network_nodes:
            net_node_pos = node_positions.get(node['id'])
            if not net_node_pos: continue

            # The connection is active if the network node it connects to is active
            is_active = node['id'] in activated
            painter.setPen(QPen(QColor(255, 255, 0), 3) if is_active else QPen(QColor(100, 100, 120), 2))
            
            if is_input:
                painter.drawLine(io_node_pos.x() + 15, io_node_pos.y(), net_node_pos.x() - 10, net_node_pos.y())
            else: # is_output
                painter.drawLine(net_node_pos.x() + 10, net_node_pos.y(), io_node_pos.x() - 15, io_node_pos.y())