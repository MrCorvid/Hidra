# controls_panel.py
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QLabel, QLineEdit, 
    QScrollArea, QTreeWidget, QTreeWidgetItem, QTabWidget, QTextEdit, 
    QSlider, QComboBox, QSpinBox, QHeaderView, QInputDialog, QMessageBox,
    QTreeWidgetItemIterator, QToolButton, QStyle
)
from PySide6.QtCore import Qt, Signal
from PySide6.QtGui import QFont, QColor, QBrush, QIcon

from collapsible_box import CollapsibleBox

class ControlsPanel(QWidget):
    # --- Signals ---
    refresh_clicked = Signal()
    create_exp_clicked = Signal(str, str, str, str)
    delete_exp_clicked = Signal()
    clone_exp_clicked = Signal()
    save_replay_clicked = Signal()
    rename_exp_clicked = Signal(str, str) 
    
    # Tree Signals
    exp_expanded = Signal(str) 
    exp_selected = Signal(str, str)
    
    # HGL / IO / Playback signals
    assemble_clicked = Signal(str)
    decompile_clicked = Signal(str)
    
    # Emits (NodeID, Value)
    input_set_clicked = Signal(int, float) 
    
    input_clear_selected_clicked = Signal()
    input_clear_all_clicked = Signal()
    playback_toggle_clicked = Signal(bool)
    playback_stop_clicked = Signal()
    step_fwd_clicked = Signal()
    step_back_clicked = Signal()
    
    # Navigation Signals (carrying target tick)
    jump_clicked = Signal(int)
    play_until_spec_clicked = Signal(int)
    
    play_until_latest_clicked = Signal()
    scrubber_changed = Signal(int)
    scrubber_released = Signal()
    
    # Signal for Speed Change
    speed_changed = Signal(str)

    def __init__(self, parent=None):
        super().__init__(parent)
        self._current_input_ids = []
        self._selected_input_id = None
        self.setup_ui()

    def setup_ui(self):
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(0, 0, 0, 0)
        
        scroll_area = QScrollArea()
        scroll_area.setWidgetResizable(True)
        main_layout.addWidget(scroll_area)
        
        content_widget = QWidget()
        scroll_area.setWidget(content_widget)
        self.content_layout = QVBoxLayout(content_widget)

        self._init_exp_mgmt_box()
        self._init_io_box()
        self._init_playback_box()
        
        self.content_layout.addStretch()

    def update_io_display(self, frame):
        """Updates the Input Grid and Output Text based on the frame snapshot."""
        snapshot = frame.snapshot
        input_ids = sorted(snapshot.get("inputNodeIds", []))
        
        if input_ids != self._current_input_ids:
            self._current_input_ids = input_ids
            self._selected_input_id = None
            self.lbl_selected_input.setText("None")
            
            while self.input_grid_layout.count():
                child = self.input_grid_layout.takeAt(0)
                if child.widget(): child.widget().deleteLater()
            
            if not input_ids:
                self.input_grid_layout.addWidget(QLabel("No Inputs"))
            else:
                for nid in input_ids:
                    btn = QPushButton(str(nid))
                    btn.setFixedSize(40, 30)
                    btn.clicked.connect(lambda checked, n=nid: self._on_input_node_clicked(n))
                    self.input_grid_layout.addWidget(btn)
        
        output_ids = sorted(snapshot.get("outputNodeIds", []))
        output_values = snapshot.get("outputNodeValues", {})
        lines = [f"ID {nid:<3} : {output_values.get(str(nid), 0.0):.4f}" for nid in output_ids]
        self.txt_outputs.setText("\n".join(lines))

    def _on_input_node_clicked(self, node_id):
        self._selected_input_id = node_id
        self.lbl_selected_input.setText(f"ID: {node_id}")

    def _on_set_clicked(self):
        if self._selected_input_id is not None:
            try:
                val = float(self.inp_val.text())
                self.input_set_clicked.emit(self._selected_input_id, val)
            except ValueError:
                pass

    def _init_exp_mgmt_box(self):
        self.exp_mgmt_box = CollapsibleBox("Experiment Management", collapsed=False)
        layout = QVBoxLayout()
        self.exp_tabs = QTabWidget()

        # --- Active Experiments ---
        tab_active = QWidget()
        l_active = QVBoxLayout(tab_active)
        l_active.setContentsMargins(4, 4, 4, 4)
        
        # Tool Bar for Tree Controls
        tree_toolbar = QHBoxLayout()
        btn_collapse = QToolButton()
        btn_collapse.setToolTip("Collapse All Groups")
        btn_collapse.setIcon(self.style().standardIcon(QStyle.StandardPixmap.SP_TitleBarMinButton))
        btn_collapse.clicked.connect(self._on_collapse_all)
        
        tree_toolbar.addWidget(btn_collapse)
        tree_toolbar.addWidget(QLabel("Experiments List"))
        tree_toolbar.addStretch()
        l_active.addLayout(tree_toolbar)
        
        self.exp_tree = QTreeWidget()
        self.exp_tree.setHeaderLabels(["Name", "Activity", "State/Gen"])
        self.exp_tree.header().setSectionResizeMode(0, QHeaderView.ResizeMode.Stretch)
        self.exp_tree.header().setSectionResizeMode(1, QHeaderView.ResizeMode.ResizeToContents)
        self.exp_tree.header().setSectionResizeMode(2, QHeaderView.ResizeMode.ResizeToContents)
        
        self.exp_tree.itemExpanded.connect(self._on_item_expanded)
        self.exp_tree.currentItemChanged.connect(self._on_item_selected)
        
        l_active.addWidget(self.exp_tree)
        
        btn_layout = QHBoxLayout()
        self.btn_refresh = QPushButton("Refresh")
        self.btn_refresh.clicked.connect(self.refresh_clicked.emit)
        
        self.btn_rename = QPushButton("Rename")
        self.btn_rename.clicked.connect(self._on_rename_clicked)
        
        self.btn_clone = QPushButton("Clone")
        self.btn_clone.clicked.connect(self.clone_exp_clicked.emit)
        
        self.btn_delete = QPushButton("Delete")
        self.btn_delete.clicked.connect(self.delete_exp_clicked.emit)
        
        btn_layout.addWidget(self.btn_refresh)
        btn_layout.addWidget(self.btn_rename)
        btn_layout.addWidget(self.btn_clone)
        btn_layout.addWidget(self.btn_delete)
        l_active.addLayout(btn_layout)
        self.exp_tabs.addTab(tab_active, "Active")

        # --- Create New ---
        tab_create = QWidget()
        l_create = QVBoxLayout(tab_create)
        self.inp_new_name = QLineEdit("new-experiment")
        self.inp_new_genome = QLineEdit("G")
        self.inp_new_inputs = QLineEdit("0, 1")
        self.inp_new_outputs = QLineEdit("10")
        
        l_create.addWidget(QLabel("Name:"))
        l_create.addWidget(self.inp_new_name)
        l_create.addWidget(QLabel("Genome (HGL):"))
        l_create.addWidget(self.inp_new_genome)
        l_create.addWidget(QLabel("Inputs (IDs):"))
        l_create.addWidget(self.inp_new_inputs)
        l_create.addWidget(QLabel("Outputs (IDs):"))
        l_create.addWidget(self.inp_new_outputs)
        
        btn_create = QPushButton("Create Standalone")
        btn_create.clicked.connect(lambda: self.create_exp_clicked.emit(
            self.inp_new_name.text(), self.inp_new_genome.text(),
            self.inp_new_inputs.text(), self.inp_new_outputs.text()
        ))
        l_create.addWidget(btn_create)
        l_create.addStretch()
        self.exp_tabs.addTab(tab_create, "Create")
        
        # --- HGL Tools ---
        tab_hgl = QWidget()
        l_hgl = QVBoxLayout(tab_hgl)
        self.txt_hgl_source = QTextEdit()
        self.txt_hgl_source.setPlaceholderText("Source Code...")
        self.btn_assemble = QPushButton("Assemble ->")
        self.btn_assemble.clicked.connect(lambda: self.assemble_clicked.emit(self.txt_hgl_source.toPlainText()))
        
        self.txt_hgl_byte = QTextEdit()
        self.txt_hgl_byte.setPlaceholderText("Hex Bytecode...")
        self.btn_decompile = QPushButton("<- Decompile")
        self.btn_decompile.clicked.connect(lambda: self.decompile_clicked.emit(self.txt_hgl_byte.toPlainText()))

        l_hgl.addWidget(self.txt_hgl_source)
        l_hgl.addWidget(self.btn_assemble)
        l_hgl.addWidget(self.txt_hgl_byte)
        l_hgl.addWidget(self.btn_decompile)
        self.exp_tabs.addTab(tab_hgl, "HGL Tools")

        layout.addWidget(self.exp_tabs)
        self.exp_mgmt_box.setContentLayout(layout)
        self.content_layout.addWidget(self.exp_mgmt_box)

    def _on_collapse_all(self):
        self.exp_tree.collapseAll()

    def _on_item_expanded(self, item: QTreeWidgetItem):
        if item.childCount() == 1 and item.child(0).text(0) == "__dummy__":
            exp_id = item.data(0, Qt.ItemDataRole.UserRole)
            self.exp_expanded.emit(exp_id)

    def _on_item_selected(self, current: QTreeWidgetItem, previous: QTreeWidgetItem):
        if not current: return
        if current.text(0).startswith("Generation") and not current.data(0, Qt.ItemDataRole.UserRole):
            return
            
        exp_id = current.data(0, Qt.ItemDataRole.UserRole)
        exp_type = current.data(0, Qt.ItemDataRole.UserRole + 1)
        if exp_id:
            self.exp_selected.emit(exp_id, exp_type)

    def _on_rename_clicked(self):
        item = self.exp_tree.currentItem()
        if not item: return
        
        exp_id = item.data(0, Qt.ItemDataRole.UserRole)
        if not exp_id: return 
        
        old_name = item.text(0)
        new_name, ok = QInputDialog.getText(self, "Rename Experiment", "New Name:", text=old_name)
        if ok and new_name.strip():
            self.rename_exp_clicked.emit(exp_id, new_name.strip())

    def _configure_tree_item(self, item: QTreeWidgetItem, data: dict):
        exp_id = data['id']
        name = data['name']
        exp_type = data.get('type', 'Standalone')
        activity = data.get('activity', 'Manual')
        
        item.setData(0, Qt.ItemDataRole.UserRole, exp_id)
        item.setData(0, Qt.ItemDataRole.UserRole + 1, exp_type)
        item.setText(0, name)
        item.setText(1, activity)
        
        if exp_type == "EvolutionRun":
            count = data.get('childrenCount', 0)
            item.setText(2, f"Run ({count} items)")
            font = item.font(0); font.setBold(True); item.setFont(0, font)
            item.setForeground(0, QBrush(QColor("#FFB74D"))) 
            item.setForeground(2, QBrush(QColor("#FFB74D")))
            
        elif exp_type == "GenerationOrganism":
            fit = data.get('fitness')
            fit_str = f"{fit:.4f}" if fit is not None else "?"
            item.setText(2, f"Fit: {fit_str}")
            item.setForeground(0, QBrush(QColor("#40C4FF"))) 
            item.setForeground(2, QBrush(QColor("#40C4FF")))
            
        else: 
            state = data.get('state', 'Unknown').title()
            tick = data.get('tick', 0)
            item.setText(2, f"{state} (T:{tick})")
            item.setForeground(0, QBrush(QColor("#40C4FF")))
            item.setForeground(2, QBrush(QColor("#40C4FF")))

    def _on_exp_root_list(self, experiments):
        self.exp_tree.clear()
        for exp in experiments:
            item = QTreeWidgetItem(self.exp_tree)
            self._configure_tree_item(item, exp)
            if exp.get('childrenCount', 0) > 0:
                QTreeWidgetItem(item, ["__dummy__"])

    def _on_exp_children(self, parent_id, children):
        iterator = QTreeWidgetItemIterator(self.exp_tree)
        parent_item = None
        while iterator.value():
            item = iterator.value()
            if item.data(0, Qt.ItemDataRole.UserRole) == parent_id:
                parent_item = item; break
            iterator += 1
            
        if not parent_item: return
        parent_item.takeChildren() 
        
        children.sort(key=lambda x: (x.get('generation') or 0, -(x.get('fitness') or 0.0)))
        
        gen_groups = {}
        for child in children:
            gen = child.get('generation', 0)
            if gen not in gen_groups: gen_groups[gen] = []
            gen_groups[gen].append(child)
            
        sorted_gens = sorted(gen_groups.keys())
        
        for gen in sorted_gens:
            group_items = gen_groups[gen]
            
            if len(group_items) > 1:
                gen_folder = QTreeWidgetItem(parent_item)
                gen_folder.setText(0, f"Generation {gen}")
                gen_folder.setText(2, f"{len(group_items)} Organisms")
                gen_folder.setForeground(0, QBrush(QColor("#81C784"))) 
                gen_folder.setForeground(2, QBrush(QColor("#81C784")))
                
                for child in group_items:
                    child_item = QTreeWidgetItem(gen_folder)
                    self._configure_tree_item(child_item, child)
            else:
                child = group_items[0]
                child_item = QTreeWidgetItem(parent_item)
                self._configure_tree_item(child_item, child)
                child_item.setText(0, f"[G{gen}] {child['name']}")

    def _init_io_box(self):
        self.io_box = CollapsibleBox("I/O Control", collapsed=False)
        layout = QVBoxLayout()
        
        layout.addWidget(QLabel("Input Nodes:"))
        self.input_node_grid_group = QWidget()
        self.input_grid_layout = QHBoxLayout(self.input_node_grid_group)
        self.input_grid_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)
        self.input_grid_layout.setContentsMargins(0, 0, 0, 0)
        
        inp_scroll = QScrollArea()
        inp_scroll.setWidgetResizable(True)
        inp_scroll.setFixedHeight(60)
        inp_scroll.setWidget(self.input_node_grid_group)
        layout.addWidget(inp_scroll)

        set_layout = QHBoxLayout()
        self.lbl_selected_input = QLabel("None")
        self.inp_val = QLineEdit("0.0")
        self.btn_set_val = QPushButton("Set")
        self.btn_set_val.clicked.connect(self._on_set_clicked)
        
        set_layout.addWidget(self.lbl_selected_input)
        set_layout.addWidget(self.inp_val)
        set_layout.addWidget(self.btn_set_val)
        layout.addLayout(set_layout)

        self.txt_outputs = QTextEdit()
        self.txt_outputs.setReadOnly(True)
        self.txt_outputs.setFixedHeight(80)
        layout.addWidget(QLabel("Outputs:"))
        layout.addWidget(self.txt_outputs)

        self.io_box.setContentLayout(layout)
        self.content_layout.addWidget(self.io_box)

    def _init_playback_box(self):
        self.playback_box = CollapsibleBox("Playback", collapsed=False)
        layout = QVBoxLayout()
        
        # Scrubber
        self.scrubber = QSlider(Qt.Orientation.Horizontal)
        self.scrubber.sliderReleased.connect(self.scrubber_released.emit)
        self.scrubber.valueChanged.connect(self.scrubber_changed.emit)
        layout.addWidget(self.scrubber)

        # Playback Buttons
        btns = QHBoxLayout()
        self.btn_stop = QPushButton("■")
        self.btn_stop.clicked.connect(self.playback_stop_clicked.emit)
        self.btn_play = QPushButton("▶")
        self.btn_play.setCheckable(True)
        self.btn_play.toggled.connect(self.playback_toggle_clicked.emit)
        self.btn_back = QPushButton("◀")
        self.btn_back.clicked.connect(self.step_back_clicked.emit)
        
        self.btn_fwd = QPushButton("▶|")
        self.btn_fwd.clicked.connect(self.step_fwd_clicked.emit)
        
        btns.addWidget(self.btn_stop)
        btns.addWidget(self.btn_play)
        btns.addWidget(self.btn_back)
        btns.addWidget(self.btn_fwd)
        layout.addLayout(btns)
        
        # Speed Control
        speed_layout = QHBoxLayout()
        self.combo_speed = QComboBox()
        self.combo_speed.addItems(["0.25x", "0.5x", "1x", "1.5x", "2x"])
        self.combo_speed.setCurrentText("1x")
        self.combo_speed.currentTextChanged.connect(self.speed_changed.emit)
        speed_layout.addWidget(QLabel("Speed:"))
        speed_layout.addWidget(self.combo_speed)
        speed_layout.addStretch()
        layout.addLayout(speed_layout)
        
        layout.addSpacing(10)
        
        # Unified Target Tick Controls
        target_box = QHBoxLayout()
        target_box.addWidget(QLabel("Target Tick:"))
        
        self.spin_tick_target = QSpinBox()
        self.spin_tick_target.setRange(0, 999999)
        self.spin_tick_target.setMinimumWidth(80)
        target_box.addWidget(self.spin_tick_target)
        target_box.addStretch()
        layout.addLayout(target_box)
        
        # Action Buttons for Target Tick
        actions_layout = QHBoxLayout()
        
        self.btn_jump = QPushButton("Jump to Tick")
        self.btn_jump.setToolTip("Navigates local history to the target tick.")
        # Emit the spinbox value directly
        self.btn_jump.clicked.connect(lambda: self.jump_clicked.emit(self.spin_tick_target.value()))
        
        self.btn_run_to = QPushButton("Run to Tick")
        self.btn_run_to.setToolTip("Executes simulation on server until target tick.")
        # Emit the spinbox value directly
        self.btn_run_to.clicked.connect(lambda: self.play_until_spec_clicked.emit(self.spin_tick_target.value()))
        
        self.btn_sync = QPushButton("Sync Live")
        self.btn_sync.clicked.connect(self.play_until_latest_clicked.emit)
        
        actions_layout.addWidget(self.btn_jump)
        actions_layout.addWidget(self.btn_run_to)
        actions_layout.addWidget(self.btn_sync)
        layout.addLayout(actions_layout)

        self.playback_box.setContentLayout(layout)
        self.content_layout.addWidget(self.playback_box)