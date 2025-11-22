# views/evolution_view.py
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QPushButton, 
    QGroupBox, QLabel, QFormLayout, QSpinBox, QComboBox,
    QLineEdit, QCheckBox, QScrollArea, QMessageBox, QFrame
)
from PySide6.QtCore import Qt, Signal
from PySide6.QtGui import QFont, QColor
import pyqtgraph as pg
import random

class EvolutionView(QWidget):
    # Signals to communicate with MainWindow/Worker
    start_clicked = Signal(dict)
    stop_clicked = Signal()
    load_gen_clicked = Signal(int)
    export_csv_clicked = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self._setup_ui()
        self._reset_graphs()

    def _setup_ui(self):
        main_layout = QHBoxLayout(self)
        main_layout.setContentsMargins(10, 10, 10, 10)
        
        # ============================================================
        # LEFT PANEL: Configuration & Controls
        # ============================================================
        left_scroll = QScrollArea()
        left_scroll.setWidgetResizable(True)
        left_scroll.setFixedWidth(380)
        left_scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        
        left_widget = QWidget()
        left_layout = QVBoxLayout(left_widget)
        left_layout.setSpacing(15)

        # --- 1. Run Configuration Group ---
        grp_cfg = QGroupBox("Experiment Configuration")
        form = QFormLayout(grp_cfg)
        form.setLabelAlignment(Qt.AlignmentFlag.AlignRight)
        
        self.inp_run_name = QLineEdit("Evo_Run_001")
        
        self.inp_gens = QSpinBox()
        self.inp_gens.setRange(1, 100000)
        self.inp_gens.setValue(100)
        self.inp_gens.setSingleStep(10)
        
        self.inp_pop = QSpinBox()
        self.inp_pop.setRange(2, 10000)
        self.inp_pop.setValue(50)
        
        self.inp_mut = QSpinBox()
        self.inp_mut.setRange(0, 100)
        self.inp_mut.setValue(1)
        self.inp_mut.setSuffix("%")
        self.inp_mut.setToolTip("Probability per byte to mutate.")
        
        self.combo_strategy = QComboBox()
        self.combo_strategy.addItems(["BasicMutation", "RandomSearch"])
        self.combo_strategy.setToolTip("RandomSearch ignores parents (Baseline). BasicMutation uses EA.")
        
        self.combo_activity = QComboBox()
        self.combo_activity.addItems(["XorGate", "CartPole", "TicTacToe", "DelayedMatchToSample"])
        self.combo_activity.currentTextChanged.connect(self._on_activity_changed)
        
        # Persistence Controls
        self.chk_save_best = QCheckBox("Save Best Organism (Per Gen)")
        self.chk_save_best.setChecked(True)
        self.chk_save_best.setToolTip("Keeps the .db file for the highest fitness organism of each generation.")

        self.chk_save_all = QCheckBox("Save All Organisms")
        self.chk_save_all.setToolTip("Warning: Consumes significant disk space. Saves every single attempt.")
        
        form.addRow("Run Name:", self.inp_run_name)
        form.addRow("Max Generations:", self.inp_gens)
        form.addRow("Population Size:", self.inp_pop)
        form.addRow("Mutation Rate:", self.inp_mut)
        form.addRow("Strategy:", self.combo_strategy)
        form.addRow("Benchmark:", self.combo_activity)
        form.addRow(self.chk_save_best)
        form.addRow(self.chk_save_all)
        
        left_layout.addWidget(grp_cfg)
        
        # --- 2. Action Buttons ---
        btn_layout = QHBoxLayout()
        
        self.btn_start = QPushButton("START EVOLUTION")
        self.btn_start.setFixedHeight(40)
        self.btn_start.setStyleSheet("""
            QPushButton { background-color: #4CAF50; color: white; font-weight: bold; border-radius: 4px; }
            QPushButton:hover { background-color: #66BB6A; }
            QPushButton:disabled { background-color: #2E3B2F; color: #888; }
        """)
        self.btn_start.clicked.connect(self._on_start)
        
        self.btn_stop = QPushButton("STOP")
        self.btn_stop.setFixedHeight(40)
        self.btn_stop.setStyleSheet("""
            QPushButton { background-color: #F44336; color: white; font-weight: bold; border-radius: 4px; }
            QPushButton:hover { background-color: #EF5350; }
            QPushButton:disabled { background-color: #4A2E2E; color: #888; }
        """)
        self.btn_stop.clicked.connect(self.stop_clicked.emit)
        self.btn_stop.setEnabled(False)
        
        btn_layout.addWidget(self.btn_start)
        btn_layout.addWidget(self.btn_stop)
        left_layout.addLayout(btn_layout)

        # --- 3. Live Status Group ---
        grp_status = QGroupBox("Live Telemetry")
        s_layout = QVBoxLayout(grp_status)
        
        self.lbl_status = QLabel("State: Idle")
        self.lbl_status.setStyleSheet("font-size: 14px; font-weight: bold; color: #DDD;")
        
        self.lbl_gen_progress = QLabel("Generation: - / -")
        self.lbl_fitness_best = QLabel("Best Fitness: -")
        self.lbl_fitness_best.setStyleSheet("color: #4CAF50; font-weight: bold;")
        
        s_layout.addWidget(self.lbl_status)
        s_layout.addWidget(self.lbl_gen_progress)
        s_layout.addWidget(self.lbl_fitness_best)
        left_layout.addWidget(grp_status)
        
        # --- 4. Analysis Tools ---
        grp_tools = QGroupBox("Analysis Tools")
        t_layout = QVBoxLayout(grp_tools)
        
        # Load Specimen
        h_load = QHBoxLayout()
        self.inp_load_gen = QSpinBox()
        self.inp_load_gen.setPrefix("Gen: ")
        self.inp_load_gen.setRange(0, 0)
        self.inp_load_gen.setToolTip("Select a generation to visualize its best organism.")
        
        btn_load = QPushButton("Visualize Best")
        btn_load.clicked.connect(lambda: self.load_gen_clicked.emit(self.inp_load_gen.value()))
        
        h_load.addWidget(self.inp_load_gen)
        h_load.addWidget(btn_load)
        t_layout.addLayout(h_load)
        
        # Separator
        line = QFrame()
        line.setFrameShape(QFrame.Shape.HLine)
        line.setFrameShadow(QFrame.Shadow.Sunken)
        t_layout.addWidget(line)
        
        # CSV Export
        btn_export = QPushButton("Export Data to CSV")
        btn_export.clicked.connect(self._on_export_csv)
        t_layout.addWidget(btn_export)
        
        left_layout.addWidget(grp_tools)
        
        left_layout.addStretch()
        left_scroll.setWidget(left_widget)
        main_layout.addWidget(left_scroll)
        
        # ============================================================
        # RIGHT PANEL: Real-time Graphs
        # ============================================================
        right_layout = QVBoxLayout()
        
        # Setup PyQtGraph
        pg.setConfigOption('background', 'k')
        pg.setConfigOption('foreground', 'd')
        pg.setConfigOptions(antialias=True)
        
        self.plot_widget = pg.PlotWidget(title="Fitness History (Max / Avg / Min)")
        self.plot_widget.showGrid(x=True, y=True, alpha=0.3)
        self.plot_widget.setLabel('left', 'Fitness Score')
        self.plot_widget.setLabel('bottom', 'Generation')
        self.plot_widget.addLegend()
        
        # Curves
        self.curve_max = self.plot_widget.plot(name='Max', pen=pg.mkPen('#4CAF50', width=2))
        self.curve_avg = self.plot_widget.plot(name='Avg', pen=pg.mkPen('#FFC107', width=2))
        self.curve_min = self.plot_widget.plot(name='Min', pen=pg.mkPen('#F44336', width=1, style=Qt.PenStyle.DashLine))
        
        right_layout.addWidget(self.plot_widget)
        main_layout.addLayout(right_layout)
        
        # Trigger initial UI state update
        self._on_activity_changed(self.combo_activity.currentText())

    def _reset_graphs(self):
        self.curve_max.setData([], [])
        self.curve_avg.setData([], [])
        self.curve_min.setData([], [])
        self.lbl_status.setText("State: Idle")
        self.lbl_gen_progress.setText("Generation: - / -")
        self.lbl_fitness_best.setText("Best Fitness: -")
        self.btn_start.setEnabled(True)
        self.btn_stop.setEnabled(False)

    def _on_start(self):
        """Constructs the EvolutionRunConfig and emits start signal."""
        run_name = self.inp_run_name.text().strip()
        if not run_name:
            QMessageBox.warning(self, "Invalid Input", "Please enter a Run Name.")
            return

        activity_type = self.combo_activity.currentText()
        
        # 1. Get Mappings based on activity
        input_map = self._get_default_inputs(activity_type)
        output_map = self._get_default_outputs(activity_type)
        
        # 2. Derive ID lists for the Organism Config
        input_ids = sorted(list(input_map.values()))
        output_ids = sorted(list(output_map.values()))

        # 3. Build JSON Payload
        config = {
            "runName": run_name,
            "maxGenerations": self.inp_gens.value(),
            "saveAllExperiments": self.chk_save_all.isChecked(),
            "saveBestPerGeneration": self.chk_save_best.isChecked(), # Now dynamic
            
            "geneticAlgorithm": {
                "strategy": self.combo_strategy.currentText(),
                "populationSize": self.inp_pop.value(),
                "elitismRate": 0.1,
                "mutationRate": self.inp_mut.value() / 100.0,
                "baseGenomeTemplate": "" # Empty = Start from scratch
            },
            
            "activity": {
                "type": activity_type,
                "maxTicksPerAttempt": self._get_max_ticks(activity_type),
                "trialsPerOrganism": 3 if activity_type == "TicTacToe" else 1,
                "inputMapping": input_map,
                "outputMapping": output_map,
                "customParameters": self._get_custom_params(activity_type)
            },
            
            "organismConfig": {
                "seed0": 12345,
                "seed1": 67890
            },
            
            "inputNodeIds": input_ids,
            "outputNodeIds": output_ids
        }
        
        self._reset_graphs()
        self.start_clicked.emit(config)

    def _on_export_csv(self):
        """Triggers the CSV export in MainWindow."""
        self.export_csv_clicked.emit()

    def update_status(self, status: dict):
        """Called by MainWindow when the worker receives a status update."""
        state = status.get("state", "Idle")
        
        # Update Buttons
        is_running = (state == "Running")
        self.btn_start.setEnabled(not is_running)
        self.btn_stop.setEnabled(is_running)
        
        # Update Labels
        self.lbl_status.setText(f"State: {state}")
        
        gen = status.get("currentGeneration", 0)
        total = status.get("totalGenerations", 0)
        best_all_time = status.get("bestFitnessAllTime", 0.0)
        
        self.lbl_gen_progress.setText(f"Generation: {gen} / {total}")
        
        # FIX: Check for uninitialized huge negative float
        if best_all_time < -1e9:
            self.lbl_fitness_best.setText("Best Fitness: -")
        else:
            self.lbl_fitness_best.setText(f"Best Fitness: {best_all_time:.4f}")
        
        # Update Loader Limit
        if gen > 0:
            self.inp_load_gen.setMaximum(gen - 1)
            self.inp_load_gen.setValue(gen - 1) # Auto-follow latest

        # Update Graphs
        history = status.get("history", [])
        if history:
            gens = [h['generationIndex'] for h in history]
            maxs = [h['maxFitness'] for h in history]
            avgs = [h['avgFitness'] for h in history]
            mins = [h['minFitness'] for h in history]
            
            self.curve_max.setData(gens, maxs)
            self.curve_avg.setData(gens, avgs)
            self.curve_min.setData(gens, mins)

    def _on_activity_changed(self, text):
        """Update UI hints based on selected benchmark."""
        suffix = str(random.randint(100, 999))
        if text == "XorGate":
            self.inp_run_name.setText(f"XOR_Run_{suffix}")
        elif text == "CartPole":
            self.inp_run_name.setText(f"CartPole_Run_{suffix}")
        elif text == "TicTacToe":
            self.inp_run_name.setText(f"TTT_Run_{suffix}")
        elif text == "DelayedMatchToSample":
            self.inp_run_name.setText(f"DMTS_Run_{suffix}")

    # =========================================================================
    # Benchmark Definitions (Mappings)
    # =========================================================================

    def _get_default_inputs(self, activity):
        if activity == "XorGate":
            return {"In_A": 1, "In_B": 2}
        
        if activity == "CartPole":
            return {
                "X": 1, 
                "X_Dot": 2, 
                "Theta": 3, 
                "Theta_Dot": 4
            }
            
        if activity == "DelayedMatchToSample":
            return {
                "Sample": 1,
                "Recall_Signal": 2
            }

        if activity == "TicTacToe":
            # Maps Board_0_0 ... Board_2_2 to IDs 1..9
            inputs = {}
            idx = 1
            for r in range(3):
                for c in range(3):
                    inputs[f"Board_{r}_{c}"] = idx
                    idx += 1
            return inputs
            
        return {}

    def _get_default_outputs(self, activity):
        if activity == "XorGate":
            return {"Out_Z": 10}
        
        if activity == "CartPole":
            return {"Force_Left": 10, "Force_Right": 11}
            
        if activity == "DelayedMatchToSample":
            return {"Memory_Out": 10}

        if activity == "TicTacToe":
            return {
                "Move_X": 10, 
                "Move_Y": 11, 
                "Place_Trigger": 12
            }
            
        return {}
        
    def _get_max_ticks(self, activity):
        if activity == "XorGate": return 20
        if activity == "CartPole": return 1000
        if activity == "TicTacToe": return 100
        if activity == "DelayedMatchToSample": return 200 
        return 100

    def _get_custom_params(self, activity):
        if activity == "DelayedMatchToSample":
            # DelayTicks = 10, Trials = 5
            return {"DelayTicks": 10, "Trials": 5}
        return {}