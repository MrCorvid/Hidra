// Hidra.API/Activities/Implementations/TicTacToeActivity.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Hidra.Core;

namespace Hidra.API.Activities.Implementations
{
    public class TicTacToeActivity : ISimulationActivity
    {
        // --- State ---
        private int[,] _board = new int[3, 3]; // 0 = Empty, 1 = Bot (X), -1 = Opponent (O)
        private ActivityConfig _config = default!;
        private Random _rng = new Random();
        
        // Mapping Caches for speed
        private ulong _nodeMoveX;
        private ulong _nodeMoveY;
        private ulong _nodePlace; // Trigger signal
        private readonly Dictionary<string, ulong> _boardInputMap = new();

        // Metrics
        private bool _gameOver;
        private string _gameResult = "Pending"; // Win, Loss, Draw, Timeout, Invalid
        private int _movesMade;
        private int _illegalMoves;
        private int _ticksSinceLastMove;
        
        public void Initialize(ActivityConfig config)
        {
            _config = config;
            _board = new int[3, 3];
            _gameOver = false;
            _gameResult = "Pending";
            _movesMade = 0;
            _illegalMoves = 0;
            _ticksSinceLastMove = 0;
            _rng = new Random(); // In a real scenario, seed this for determinism

            // Cache Node IDs from Config
            // We expect specific keys in the mapping like "Move_X", "Move_Y", "Place_Trigger"
            // FIX: Use 0UL to match the ulong value type
            _nodeMoveX = config.OutputMapping.GetValueOrDefault("Move_X", 0UL);
            _nodeMoveY = config.OutputMapping.GetValueOrDefault("Move_Y", 0UL);
            _nodePlace = config.OutputMapping.GetValueOrDefault("Place_Trigger", 0UL);

            _boardInputMap.Clear();
            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    string key = $"Board_{x}_{y}";
                    if (config.InputMapping.ContainsKey(key))
                    {
                        _boardInputMap[key] = config.InputMapping[key];
                    }
                }
            }
        }

        public bool Step(HidraWorld world)
        {
            if (_gameOver) return true;
            _ticksSinceLastMove++;

            // 1. Read Outputs from World
            // We only read if we have valid mappings
            if (_nodeMoveX == 0 || _nodeMoveY == 0) return true; // Configuration error, abort

            var outputs = world.GetOutputValues(new List<ulong> { _nodeMoveX, _nodeMoveY, _nodePlace });
            
            float valX = outputs.GetValueOrDefault(_nodeMoveX);
            float valY = outputs.GetValueOrDefault(_nodeMoveY);
            float trigger = outputs.GetValueOrDefault(_nodePlace);

            // 2. Process Logic
            // We require the 'Place_Trigger' to exceed 0.5 to attempt a move.
            // This allows the network to "think" for a few ticks before committing.
            if (trigger > 0.5f)
            {
                // Map continuous (-1.0 to 1.0 or similar) to 0, 1, 2
                // Assuming generic tanh/sigmoid outputs, we normalize:
                int x = MapCoordinate(valX);
                int y = MapCoordinate(valY);

                AttemptMove(x, y);
                _ticksSinceLastMove = 0; // Reset timeout counter
            }

            // Force end if stagnant (prevent stalling)
            if (_ticksSinceLastMove > 50)
            {
                _gameOver = true;
                _gameResult = "Timeout";
            }

            // 3. Write Inputs to World (Update Board State)
            var inputs = new Dictionary<ulong, float>();
            foreach (var kvp in _boardInputMap)
            {
                // Parse coords from key "Board_x_y"
                var parts = kvp.Key.Split('_');
                if (parts.Length == 3 && int.TryParse(parts[1], out int r) && int.TryParse(parts[2], out int c))
                {
                    // Input Value: 0 for empty, 1 for Self, -1 for Opponent
                    inputs[kvp.Value] = (float)_board[r, c];
                }
            }
            
            // Also could add an input for "Illegal Move Attempted" signal here
            
            world.SetInputValues(inputs);

            return _gameOver;
        }

        private void AttemptMove(int x, int y)
        {
            if (_gameOver) return;

            // Check validity
            if (_board[x, y] != 0)
            {
                _illegalMoves++;
                // Optional: Penalize fitness heavily or end game immediately
                // For now, we just count it and let them try again.
                if (_illegalMoves > 5) 
                {
                    _gameOver = true;
                    _gameResult = "Disqualified (Illegal Moves)";
                }
                return;
            }

            // 1. Bot Move
            _board[x, y] = 1;
            _movesMade++;

            if (CheckWin(1))
            {
                _gameOver = true;
                _gameResult = "Win";
                return;
            }
            if (CheckDraw())
            {
                _gameOver = true;
                _gameResult = "Draw";
                return;
            }

            // 2. Opponent Move (Simple Random for now)
            MakeOpponentMove();
            
            if (CheckWin(-1))
            {
                _gameOver = true;
                _gameResult = "Loss";
            }
            else if (CheckDraw())
            {
                _gameOver = true;
                _gameResult = "Draw";
            }
        }

        private void MakeOpponentMove()
        {
            // Find empty spots
            var empty = new List<(int, int)>();
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    if (_board[i, j] == 0) empty.Add((i, j));

            if (empty.Count > 0)
            {
                // Simple AI: Just pick random. 
                // Future: Use Minimax for "Hard" mode based on _config.CustomParameters
                var move = empty[_rng.Next(empty.Count)];
                _board[move.Item1, move.Item2] = -1;
            }
        }

        private bool CheckWin(int player)
        {
            // Rows & Cols
            for (int i = 0; i < 3; i++)
            {
                if (_board[i, 0] == player && _board[i, 1] == player && _board[i, 2] == player) return true;
                if (_board[0, i] == player && _board[1, i] == player && _board[2, i] == player) return true;
            }
            // Diagonals
            if (_board[0, 0] == player && _board[1, 1] == player && _board[2, 2] == player) return true;
            if (_board[0, 2] == player && _board[1, 1] == player && _board[2, 0] == player) return true;

            return false;
        }

        private bool CheckDraw()
        {
            foreach (int cell in _board) if (cell == 0) return false;
            return true;
        }

        // Helpers
        private int MapCoordinate(float val)
        {
            // Map arbitrary float to 0, 1, 2
            // Assuming range -1 to 1 or similar, we clamp and scale.
            // Heuristic: split range into thirds.
            if (val < -0.33f) return 0;
            if (val > 0.33f) return 2;
            return 1;
        }

        // Interface Impl
        public float GetFitnessScore()
        {
            float score = 0;
            
            // Base score for result
            if (_gameResult == "Win") score += 100.0f;
            else if (_gameResult == "Draw") score += 20.0f;
            else if (_gameResult == "Loss") score += 5.0f; // Participation trophy
            
            // Penalty for illegal moves
            score -= (_illegalMoves * 2.0f);

            // Bonus for valid moves made (encourages playing vs doing nothing)
            score += (_movesMade * 1.0f);
            
            // Penalty for timeout
            if (_gameResult == "Timeout") score -= 10.0f;

            return Math.Max(0, score);
        }

        public Dictionary<string, string> GetRunMetadata()
        {
            return new Dictionary<string, string>
            {
                { "Result", _gameResult },
                { "Moves", _movesMade.ToString() },
                { "IllegalMoves", _illegalMoves.ToString() }
            };
        }
    }
}