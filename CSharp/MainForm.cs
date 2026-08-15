using System.Diagnostics;

namespace StarRunnerPrototype;

public sealed class MainForm : Form
{
    private enum MatchMode
    {
        HumanP1VsCpuP2,
        CpuP1VsHumanP2,
        CpuVsCpu,
        HumanVsHuman
    }

    private readonly BoardControl _board = new();
    private readonly ComboBox _modeCombo = new();
    private readonly ComboBox _p1SkillCombo = new();
    private readonly ComboBox _p2SkillCombo = new();
    private IReadOnlyList<CpuSkillProfile> _cpuSkillProfiles = CpuSkillProfiles.BuiltInStandard;
    private readonly NumericUpDown _cpuSearchParallelism = new();
    private readonly NumericUpDown _cpuDelay = new();
    private readonly Button _newGameButton = new();
    private readonly Button _cpuStartPauseButton = new();
    private readonly Button _cpuStepButton = new();
    private readonly ToolStripMenuItem _saveAsMenuItem = new("名前をつけて保存");
    private readonly ToolStripMenuItem _openGameMenuItem = new("開く");
    private readonly ToolStripMenuItem _openLogsMenuItem = new("ログを開く");
    private readonly ToolStripMenuItem _replayMenuItem = new("棋譜並べ");
    private readonly ToolStripMenuItem _headlessMenuItem = new("高速ヘッドレス");
    private readonly ToolStripMenuItem _benchmarkMenuItem = new("現在局面 深度ベンチ");
    private readonly ToolStripMenuItem _verifierMenuItem = new("Bitboard整合性検証");
    private readonly ToolStripMenuItem _cpuSearchVerifierMenuItem = new("CPU探索整合性検証");
    private readonly ToolStripMenuItem _rulesMenuItem = new("ルール");
    private readonly Label _turnLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _blockerLabel = new();
    private readonly Label _evaluationValueLabel = new();
    private readonly Label _evaluationDetailLabel = new();
    private readonly Label _logLabel = new();
    private readonly ListBox _history = new();
    private readonly System.Windows.Forms.Timer _evaluationTimer = new() { Interval = 1000 };

    private GameEngine _game = new();
    private GameLogger? _logger;
    private MatchMode _mode;
    private Position? _selected;
    private IReadOnlyList<Move> _selectedMoves = Array.Empty<Move>();
    private CancellationTokenSource _aiCts = new();
    private bool _aiBusy;
    private bool _verificationBusy;
    private bool _cpuAutoRunning;
    private int _sessionSerial;
    private CpuSearchMonitor? _activeSearchMonitor;
    private PlayerId? _activeSearchPlayer;
    private int? _lastFinalEvaluationP1Score;
    private string? _lastFinalEvaluationDetail;

    public MainForm()
    {
        Text = "Star Runner Prototype - A案 v0.2.36.2";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 780);
        ClientSize = new Size(1120, 780);
        Font = new Font("Yu Gothic UI", 9f);

        BuildUi();
        WireEvents();
        StartNewGame();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _evaluationTimer.Stop();
        CancelAiWork();
        if (_logger is not null)
        {
            if (_game.Outcome == GameOutcome.Ongoing)
            {
                _logger.WriteSessionStopped(_game, "application_closed");
            }
            _logger.Dispose();
            _logger = null;
        }

        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        Controls.Add(shell);

        var menuStrip = new MenuStrip
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(4, 2, 0, 2)
        };
        var fileMenu = new ToolStripMenuItem("ファイル");
        _saveAsMenuItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.S;
        _openGameMenuItem.ShortcutKeys = Keys.Control | Keys.O;
        fileMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            _saveAsMenuItem,
            _openGameMenuItem
        });

        var toolsMenu = new ToolStripMenuItem("ツール");
        toolsMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            _openLogsMenuItem,
            _replayMenuItem,
            new ToolStripSeparator(),
            _headlessMenuItem,
            _benchmarkMenuItem,
            new ToolStripSeparator(),
            _verifierMenuItem,
            _cpuSearchVerifierMenuItem
        });
        var helpMenu = new ToolStripMenuItem("ヘルプ");
        helpMenu.DropDownItems.Add(_rulesMenuItem);
        menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, toolsMenu, helpMenu });
        MainMenuStrip = menuStrip;
        shell.Controls.Add(menuStrip, 0, 0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8),
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340f));
        shell.Controls.Add(root, 0, 1);

        _board.Dock = DockStyle.Fill;
        root.Controls.Add(_board, 0, 0);

        // 右側は上37% / 棋譜63%。固定部の必要高を確保しつつ、
        // 棋譜ListBox自体は通常サイズで画面高の半分以上を維持する。
        var side = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10),
            Margin = new Padding(0),
            BackColor = Color.FromArgb(247, 247, 247)
        };
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 37f));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 63f));
        root.Controls.Add(side, 1, 0);

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 31f));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        side.Controls.Add(top, 0, 0);

        var title = new Label
        {
            Text = "A案 検証用プロトタイプ",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
            Margin = new Padding(0)
        };
        top.Controls.Add(title, 0, 0);

        var modePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 2, 0, 0)
        };
        modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64f));
        modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        modePanel.Controls.Add(new Label
        {
            Text = "対戦モード",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 0, 0);
        _modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeCombo.Dock = DockStyle.Fill;
        _modeCombo.Margin = new Padding(0, 2, 0, 2);
        _modeCombo.Items.AddRange(new object[]
        {
            "人間(P1) vs CPU(P2)",
            "CPU(P1) vs 人間(P2)",
            "CPU vs CPU",
            "人間 vs 人間"
        });
        _modeCombo.SelectedIndex = 0;
        modePanel.Controls.Add(_modeCombo, 1, 0);
        top.Controls.Add(modePanel, 0, 1);

        var settingsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0, 2, 0, 0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29f));
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21f));
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29f));
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21f));
        settingsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        settingsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        ConfigureNumeric(
            _cpuSearchParallelism,
            1,
            Math.Max(1, Environment.ProcessorCount),
            Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1)),
            1);
        ConfigureNumeric(_cpuDelay, 0, 3000, 120, 50);

        _p1SkillCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _p2SkillCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ReloadCpuSkillProfiles(preserveSelection: false);

        void AddSetting(int row, string leftLabel, Control leftControl, string rightLabel, Control rightControl)
        {
            settingsPanel.Controls.Add(new Label
            {
                Text = leftLabel,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(4, 0, 2, 0),
                Font = new Font(Font.FontFamily, 8.2f)
            }, 0, row);
            leftControl.Dock = DockStyle.Fill;
            leftControl.Margin = new Padding(1);
            settingsPanel.Controls.Add(leftControl, 1, row);
            settingsPanel.Controls.Add(new Label
            {
                Text = rightLabel,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(4, 0, 2, 0),
                Font = new Font(Font.FontFamily, 8.2f)
            }, 2, row);
            rightControl.Dock = DockStyle.Fill;
            rightControl.Margin = new Padding(1);
            settingsPanel.Controls.Add(rightControl, 3, row);
        }

        AddSetting(0, "P1 CPU棋力", _p1SkillCombo, "P2 CPU棋力", _p2SkillCombo);
        AddSetting(1, "CPU探索スレッド", _cpuSearchParallelism, "CPU間隔(ms)", _cpuDelay);
        top.Controls.Add(settingsPanel, 0, 2);

        var primaryButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 3, 0, 0)
        };
        primaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        primaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        primaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
        ConfigureButton(_newGameButton, "新規対局", 0);
        ConfigureButton(_cpuStartPauseButton, "CPU開始", 0);
        ConfigureButton(_cpuStepButton, "CPU 1手", 0);
        foreach (Button button in new[] { _newGameButton, _cpuStartPauseButton, _cpuStepButton })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(1);
        }
        primaryButtons.Controls.Add(_newGameButton, 0, 0);
        primaryButtons.Controls.Add(_cpuStartPauseButton, 1, 0);
        primaryButtons.Controls.Add(_cpuStepButton, 2, 0);
        top.Controls.Add(primaryButtons, 0, 3);

        var statusPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 4, 0, 0),
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        _turnLabel.Dock = DockStyle.Fill;
        _turnLabel.TextAlign = ContentAlignment.MiddleLeft;
        _turnLabel.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
        _turnLabel.Margin = new Padding(5, 0, 5, 0);
        statusPanel.Controls.Add(_turnLabel, 0, 0);
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Margin = new Padding(5, 1, 5, 0);
        _statusLabel.Font = new Font(Font.FontFamily, 8.2f);
        statusPanel.Controls.Add(_statusLabel, 0, 1);
        _blockerLabel.Dock = DockStyle.Fill;
        _blockerLabel.Margin = new Padding(5, 0, 5, 1);
        _blockerLabel.Font = new Font(Font.FontFamily, 8.2f);
        statusPanel.Controls.Add(_blockerLabel, 0, 2);
        top.Controls.Add(statusPanel, 0, 4);

        var historyPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0, 4, 0, 0),
            Padding = new Padding(0)
        };
        historyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
        historyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        historyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        historyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        side.Controls.Add(historyPanel, 0, 1);

        var evaluationPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 4),
            Padding = new Padding(6, 2, 6, 2),
            BackColor = SystemColors.Info,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        evaluationPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 65f));
        evaluationPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 35f));
        _evaluationValueLabel.Text = "評価値(P1基準): ---";
        _evaluationValueLabel.Dock = DockStyle.Fill;
        _evaluationValueLabel.TextAlign = ContentAlignment.MiddleLeft;
        _evaluationValueLabel.Margin = new Padding(2, 0, 2, 0);
        _evaluationValueLabel.Font = new Font(Font.FontFamily, 15.5f, FontStyle.Bold);
        evaluationPanel.Controls.Add(_evaluationValueLabel, 0, 0);
        _evaluationDetailLabel.Text = "CPU思考中は1秒ごとに更新 / 思考後は最終評価値を保持";
        _evaluationDetailLabel.Dock = DockStyle.Fill;
        _evaluationDetailLabel.TextAlign = ContentAlignment.MiddleLeft;
        _evaluationDetailLabel.Margin = new Padding(2, 0, 2, 0);
        _evaluationDetailLabel.Font = new Font(Font.FontFamily, 8f);
        evaluationPanel.Controls.Add(_evaluationDetailLabel, 0, 1);
        historyPanel.Controls.Add(evaluationPanel, 0, 0);

        historyPanel.Controls.Add(new Label
        {
            Text = "棋譜",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 0, 1);

        _history.Dock = DockStyle.Fill;
        _history.HorizontalScrollbar = true;
        _history.Margin = new Padding(0);
        historyPanel.Controls.Add(_history, 0, 2);
        _logLabel.Dock = DockStyle.Fill;
        _logLabel.Margin = new Padding(0, 3, 0, 0);
        _logLabel.Font = new Font(Font.FontFamily, 8f);
        historyPanel.Controls.Add(_logLabel, 0, 3);
    }

    private void WireEvents()
    {
        _board.CellClicked += BoardOnCellClicked;
        _newGameButton.Click += (_, _) => StartNewGame();
        _cpuStartPauseButton.Click += async (_, _) => await ToggleCpuAutoAsync();
        _cpuStepButton.Click += async (_, _) => await RunCpuTurnsAsync(singleStep: true);
        _saveAsMenuItem.Click += (_, _) => SaveGameAs();
        _openGameMenuItem.Click += (_, _) => OpenGameDialog();
        _openLogsMenuItem.Click += (_, _) => OpenLogsFolder();
        _replayMenuItem.Click += (_, _) => OpenReplayDialog();
        _headlessMenuItem.Click += (_, _) => OpenHeadlessDialog();
        _benchmarkMenuItem.Click += (_, _) => OpenDepthBenchmark();
        _verifierMenuItem.Click += async (_, _) => await RunBitboardVerifierAsync();
        _cpuSearchVerifierMenuItem.Click += async (_, _) => await RunCpuSearchVerifierAsync();
        _rulesMenuItem.Click += (_, _) => ShowRules();
        _modeCombo.SelectedIndexChanged += (_, _) => OnMatchModeChanged();
        _evaluationTimer.Tick += (_, _) => UpdateLiveEvaluationDisplay();
    }

    private void StartNewGame()
    {
        _evaluationTimer.Stop();
        CancelAiWork();

        if (_logger is not null)
        {
            if (_game.Outcome == GameOutcome.Ongoing)
            {
                _logger.WriteSessionStopped(_game, "new_game_started");
            }
            _logger.Dispose();
        }

        _sessionSerial++;
        _aiCts = new CancellationTokenSource();
        _game = new GameEngine();
        _mode = (MatchMode)Math.Clamp(_modeCombo.SelectedIndex, 0, 3);
        _selected = null;
        _selectedMoves = Array.Empty<Move>();
        _aiBusy = false;
        _cpuAutoRunning = false;
        ClearLiveEvaluationDisplay();
        _history.Items.Clear();
        _history.Items.Add("--- START ---");

        CpuSkillProfile p1Skill = GetSelectedCpuSkillProfile(PlayerId.Player1);
        CpuSkillProfile p2Skill = GetSelectedCpuSkillProfile(PlayerId.Player2);
        _logger = GameLogger.Create(
            CurrentModeLogLabel(),
            p1Skill.Name,
            p2Skill.Name,
            p1Skill.MaxNodes,
            p2Skill.MaxNodes,
            (int)_cpuSearchParallelism.Value,
            _game);

        _board.Game = _game;
        _board.ClearSelection();
        UpdateUi();

        // If CPU is P1 in a human-vs-CPU game, it should make the opening move automatically.
        if (_mode == MatchMode.CpuP1VsHumanP2)
        {
            BeginInvoke(new Action(async () => await RunCpuTurnsAsync(singleStep: false)));
        }
    }

    private async void BoardOnCellClicked(object? sender, Position position)
    {
        if (_aiBusy || _verificationBusy || _game.Outcome != GameOutcome.Ongoing || IsCpu(_game.CurrentPlayer))
        {
            return;
        }

        Piece? clicked = _game.GetPiece(position);

        if (_selected is null)
        {
            if (clicked is { } piece && piece.Owner == _game.CurrentPlayer)
            {
                SelectPiece(position);
            }
            return;
        }

        Move? selectedMove = null;
        foreach (Move candidate in _selectedMoves)
        {
            if (candidate.To == position)
            {
                selectedMove = candidate;
                break;
            }
        }

        if (selectedMove is { } move)
        {
            await ApplyHumanMoveAsync(move);
            return;
        }

        if (clicked is { } ownPiece && ownPiece.Owner == _game.CurrentPlayer)
        {
            SelectPiece(position);
        }
        else
        {
            ClearSelection();
        }
    }

    private void SelectPiece(Position position)
    {
        _selected = position;
        _selectedMoves = _game.GetLegalMoves()
            .Where(move => move.From == position)
            .ToArray();
        _board.SetSelection(_selected, _selectedMoves);
    }

    private void ClearSelection()
    {
        _selected = null;
        _selectedMoves = Array.Empty<Move>();
        _board.ClearSelection();
    }

    private async Task ApplyHumanMoveAsync(Move move)
    {
        PlayerId player = _game.CurrentPlayer;
        GameEngine before = _game.Clone();
        _logger?.WriteHumanMove(player, move, before);

        if (!_game.TryApplyMove(move, out string? error))
        {
            MessageBox.Show(this, error ?? "不正な手です。", "Move error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _logger?.WriteMoveApplied(player, move, _game);
        AddHistory(player, move, "HUMAN");
        ClearSelection();
        UpdateUi();

        if (_game.Outcome == GameOutcome.Ongoing)
        {
            await RunCpuTurnsAsync(singleStep: false);
        }
    }

    private async Task ToggleCpuAutoAsync()
    {
        if (_mode != MatchMode.CpuVsCpu || _game.Outcome != GameOutcome.Ongoing)
        {
            return;
        }

        _cpuAutoRunning = !_cpuAutoRunning;
        UpdateUi();
        if (_cpuAutoRunning)
        {
            await RunCpuTurnsAsync(singleStep: false);
        }
    }

    private async Task RunCpuTurnsAsync(bool singleStep)
    {
        if (_aiBusy || _verificationBusy || _game.Outcome != GameOutcome.Ongoing)
        {
            return;
        }

        if (!IsCpu(_game.CurrentPlayer))
        {
            return;
        }

        int serial = _sessionSerial;
        CancellationToken token = _aiCts.Token;
        _aiBusy = true;
        _evaluationTimer.Start();
        UpdateUi();

        try
        {
            int movesMade = 0;
            while (_game.Outcome == GameOutcome.Ongoing && IsCpu(_game.CurrentPlayer))
            {
                if (_mode == MatchMode.CpuVsCpu && !singleStep && !_cpuAutoRunning)
                {
                    break;
                }

                PlayerId player = _game.CurrentPlayer;
                CpuSkillProfile skill = GetSelectedCpuSkillProfile(player);
                int moveSeed = unchecked((_sessionSerial * 1_000_003) ^ (_game.PlyCount * 7919) ^ ((int)player * 0x45d9f3b));
                GameEngine before = _game.Clone();
                CpuSearchOptions searchOptions = skill.ToSearchOptions(
                    moveSeed,
                    (int)_cpuSearchParallelism.Value,
                    timeLimitMilliseconds: 0,
                    maxNodes: 0,
                    cycleBreakScoreWindow: 10,
                    useBelowNormalThreadPriority: true);

                var searchMonitor = new CpuSearchMonitor();
                _activeSearchMonitor = searchMonitor;
                _activeSearchPlayer = player;
                UpdateLiveEvaluationDisplay();

                CpuDecision decision = await Task.Run(
                    () => CpuPlayer.DecideMove(before.Clone(), player, searchOptions, token, searchMonitor),
                    token);

                if (token.IsCancellationRequested || serial != _sessionSerial)
                {
                    return;
                }

                ShowFinalEvaluation(player, decision);
                _logger?.WriteCpuDecision(player, skill, decision, before);
                if (!_game.TryApplyMove(decision.Move, out string? error))
                {
                    throw new InvalidOperationException(error ?? "CPU generated an illegal move.");
                }

                _logger?.WriteMoveApplied(player, decision.Move, _game);
                string stopTag = decision.NodeLimitReached ? " node-limit" : decision.TimedOut ? " time-limit" : string.Empty;
                string skillTag = $"CPU {skill.Name}";
                AddHistory(player, decision.Move, $"{skillTag} d{decision.Depth}/{decision.RequestedDepth} s{decision.Score} n{decision.Nodes:N0}{stopTag}");
                ClearSelection();
                UpdateUi();
                movesMade++;

                if (singleStep && movesMade >= 1)
                {
                    break;
                }

                if (_mode != MatchMode.CpuVsCpu)
                {
                    // Human-vs-CPU alternates after one CPU move.
                    break;
                }

                if (!_cpuAutoRunning || _game.Outcome != GameOutcome.Ongoing)
                {
                    break;
                }

                int delay = (int)_cpuDelay.Value;
                if (delay > 0)
                {
                    await Task.Delay(delay, token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when starting a new game or closing the app.
        }
        catch (Exception ex)
        {
            _cpuAutoRunning = false;
            _logger?.WriteSessionStopped(_game, $"cpu_error: {ex.GetType().Name}: {ex.Message}");
            MessageBox.Show(this, ex.ToString(), "CPU error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (serial == _sessionSerial)
            {
                _aiBusy = false;
                _evaluationTimer.Stop();
                ReleaseActiveSearchDisplay();
                UpdateUi();
            }
        }
    }

    private void UpdateLiveEvaluationDisplay()
    {
        CpuSearchMonitor? monitor = _activeSearchMonitor;
        PlayerId? player = _activeSearchPlayer;
        if (!_aiBusy || monitor is null || player is null)
        {
            ShowStoredFinalEvaluation();
            return;
        }

        if (!monitor.TryGetSnapshot(out CpuSearchProgress progress))
        {
            _evaluationValueLabel.Text = "評価値(P1基準): 計算中…";
            _evaluationDetailLabel.Text = $"{player.Value.ShortName()} CPU / 探索開始中";
            return;
        }

        int p1Score = player == PlayerId.Player1 ? progress.Score : -progress.Score;
        string signedScore = p1Score > 0
            ? $"+{p1Score:N0}"
            : p1Score.ToString("N0");
        _evaluationValueLabel.Text = $"評価値(P1基準): {signedScore}";

        string depthText;
        if (progress.ScoreDepth <= 0)
        {
            depthText = $"初期評価 / D{progress.TargetDepth}探索中";
        }
        else if (progress.IsProvisional)
        {
            depthText = $"D{progress.ScoreDepth}暫定";
        }
        else if (progress.TargetDepth > progress.ScoreDepth)
        {
            depthText = $"D{progress.ScoreDepth}完了 / D{progress.TargetDepth}探索中";
        }
        else
        {
            depthText = $"D{progress.ScoreDepth}完了";
        }

        _evaluationDetailLabel.Text = $"＋=P1有利 －=P2有利 / {player.Value.ShortName()} CPU / {depthText}";
    }

    private void ShowFinalEvaluation(PlayerId player, CpuDecision decision)
    {
        int p1Score = player == PlayerId.Player1 ? decision.Score : -decision.Score;
        _lastFinalEvaluationP1Score = p1Score;

        string stopText = decision.NodeLimitReached
            ? " / node-limit"
            : decision.TimedOut
                ? " / time-limit"
                : string.Empty;
        _lastFinalEvaluationDetail =
            $"最終 / {player.ShortName()} CPU / D{decision.Depth}/{decision.RequestedDepth} / n{decision.Nodes:N0}{stopText}";

        ShowStoredFinalEvaluation();
    }

    private void ShowStoredFinalEvaluation()
    {
        if (_lastFinalEvaluationP1Score is not int p1Score)
        {
            _evaluationValueLabel.Text = "評価値(P1基準): ---";
            _evaluationDetailLabel.Text = "CPU思考中は1秒ごとに更新 / 思考後は最終評価値を保持";
            return;
        }

        string signedScore = p1Score > 0
            ? $"+{p1Score:N0}"
            : p1Score.ToString("N0");
        _evaluationValueLabel.Text = $"評価値(P1基準): {signedScore}";
        _evaluationDetailLabel.Text =
            $"＋=P1有利 －=P2有利 / {_lastFinalEvaluationDetail ?? "CPU最終評価"}";
    }

    private void ReleaseActiveSearchDisplay()
    {
        _activeSearchMonitor = null;
        _activeSearchPlayer = null;
        ShowStoredFinalEvaluation();
    }

    private void ClearLiveEvaluationDisplay()
    {
        _activeSearchMonitor = null;
        _activeSearchPlayer = null;
        _lastFinalEvaluationP1Score = null;
        _lastFinalEvaluationDetail = null;
        ShowStoredFinalEvaluation();
    }

    private void AddHistory(PlayerId player, Move move, string actor)
    {
        string line = $"{_game.PlyCount,3}. {player.ShortName()} {move.ToNotation(),-16} [{actor}]";
        _history.Items.Add(line);
        _history.TopIndex = Math.Max(0, _history.Items.Count - 1);
    }

    private void UpdateUi()
    {
        _board.Invalidate();

        if (_game.Outcome == GameOutcome.Ongoing)
        {
            string actor = IsCpu(_game.CurrentPlayer) ? "CPU" : "人間";
            _turnLabel.Text = $"手番: {_game.CurrentPlayer.JapaneseName()} / {actor}";
            _statusLabel.Text = _verificationBusy
                ? "Bitboard整合性検証中…"
                : _aiBusy
                    ? "CPU思考中…"
                    : _game.IsRunnerForcedToRetreat(_game.CurrentPlayer)
                        ? $"包囲中: ★を退避してください / 手数: {_game.PlyCount} / 現局面反復: {_game.CurrentPositionRepetitionCount()}回"
                        : $"手数: {_game.PlyCount} / 現局面反復: {_game.CurrentPositionRepetitionCount()}回";
        }
        else
        {
            _turnLabel.Text = ResultText();
            _statusLabel.Text = $"終了理由: {EndReasonText(_game.EndReason)} / 手数: {_game.PlyCount}";
            _cpuAutoRunning = false;
        }

        _blockerLabel.Text = $"残り○  P1: {_game.CountBlockers(PlayerId.Player1)} / P2: {_game.CountBlockers(PlayerId.Player2)}";
        _logLabel.Text = _logger is null
            ? "ログ: -"
            : $"ログ: {Path.GetFileName(_logger.FilePath)}";

        bool cpuVsCpu = _mode == MatchMode.CpuVsCpu;
        _cpuStartPauseButton.Enabled = !_verificationBusy && cpuVsCpu && _game.Outcome == GameOutcome.Ongoing && (!_aiBusy || _cpuAutoRunning);
        _cpuStartPauseButton.Text = _cpuAutoRunning ? "CPU一時停止" : "CPU開始";
        _cpuStepButton.Enabled = !_verificationBusy && cpuVsCpu && !_aiBusy && _game.Outcome == GameOutcome.Ongoing;
        _newGameButton.Enabled = true;
        _saveAsMenuItem.Enabled = _logger is not null && !_aiBusy && !_verificationBusy;
        _openGameMenuItem.Enabled = !_aiBusy && !_verificationBusy;
        _openLogsMenuItem.Enabled = _logger is not null;
        _replayMenuItem.Enabled = !_aiBusy && !_verificationBusy;
        _headlessMenuItem.Enabled = !_aiBusy && !_verificationBusy;
        _benchmarkMenuItem.Enabled = !_aiBusy && !_verificationBusy && _game.Outcome == GameOutcome.Ongoing;
        _verifierMenuItem.Enabled = !_aiBusy && !_verificationBusy;
        _cpuSearchVerifierMenuItem.Enabled = !_aiBusy && !_verificationBusy;
        bool hasP1Cpu = _mode is MatchMode.CpuP1VsHumanP2 or MatchMode.CpuVsCpu;
        bool hasP2Cpu = _mode is MatchMode.HumanP1VsCpuP2 or MatchMode.CpuVsCpu;
        bool hasCpu = hasP1Cpu || hasP2Cpu;
        _modeCombo.Enabled = !_aiBusy && !_verificationBusy;
        _p1SkillCombo.Enabled = !_aiBusy && !_verificationBusy && hasP1Cpu;
        _p2SkillCombo.Enabled = !_aiBusy && !_verificationBusy && hasP2Cpu;
        _cpuSearchParallelism.Enabled = !_aiBusy && !_verificationBusy && hasCpu;
        _cpuDelay.Enabled = !_aiBusy && !_verificationBusy && cpuVsCpu;
    }

    private void ReloadCpuSkillProfiles(bool preserveSelection = true)
    {
        int p1Selected = preserveSelection && _p1SkillCombo.SelectedIndex >= 0
            ? _p1SkillCombo.SelectedIndex
            : 10; // default 5級
        int p2Selected = preserveSelection && _p2SkillCombo.SelectedIndex >= 0
            ? _p2SkillCombo.SelectedIndex
            : 10; // default 5級

        _cpuSkillProfiles = CpuSkillProfiles.BuiltInStandard.Select(p => p with { }).ToArray();
        foreach (ComboBox combo in new[] { _p1SkillCombo, _p2SkillCombo })
        {
            combo.BeginUpdate();
            try
            {
                combo.Items.Clear();
                foreach (CpuSkillProfile profile in _cpuSkillProfiles)
                {
                    combo.Items.Add(profile.Name);
                }
            }
            finally
            {
                combo.EndUpdate();
            }
        }

        _p1SkillCombo.SelectedIndex = Math.Clamp(p1Selected, 0, _p1SkillCombo.Items.Count - 1);
        _p2SkillCombo.SelectedIndex = Math.Clamp(p2Selected, 0, _p2SkillCombo.Items.Count - 1);
    }

    private CpuSkillProfile GetSelectedCpuSkillProfile(PlayerId player)
    {
        ComboBox combo = player == PlayerId.Player1 ? _p1SkillCombo : _p2SkillCombo;
        int index = Math.Clamp(combo.SelectedIndex, 0, _cpuSkillProfiles.Count - 1);
        return _cpuSkillProfiles[index];
    }

    private string CurrentModeLogLabel()
    {
        string mode = _modeCombo.SelectedItem?.ToString() ?? _mode.ToString();
        CpuSkillProfile p1 = GetSelectedCpuSkillProfile(PlayerId.Player1);
        CpuSkillProfile p2 = GetSelectedCpuSkillProfile(PlayerId.Player2);
        return _mode switch
        {
            MatchMode.HumanP1VsCpuP2 => $"{mode} / P2棋力={p2.Name}",
            MatchMode.CpuP1VsHumanP2 => $"{mode} / P1棋力={p1.Name}",
            MatchMode.CpuVsCpu => $"{mode} / P1棋力={p1.Name} / P2棋力={p2.Name}",
            _ => mode
        };
    }

    private void OnMatchModeChanged()
    {
        _mode = (MatchMode)Math.Clamp(_modeCombo.SelectedIndex, 0, 3);
        _cpuAutoRunning = false;
        ClearSelection();
        UpdateUi();

        if (!_aiBusy && !_verificationBusy && _game.Outcome == GameOutcome.Ongoing &&
            _mode != MatchMode.CpuVsCpu && IsCpu(_game.CurrentPlayer))
        {
            BeginInvoke(new Action(async () => await RunCpuTurnsAsync(singleStep: false)));
        }
    }

    private bool IsCpu(PlayerId player)
    {
        return _mode switch
        {
            MatchMode.HumanP1VsCpuP2 => player == PlayerId.Player2,
            MatchMode.CpuP1VsHumanP2 => player == PlayerId.Player1,
            MatchMode.CpuVsCpu => true,
            MatchMode.HumanVsHuman => false,
            _ => false
        };
    }

    private void CancelAiWork()
    {
        try
        {
            _aiCts.Cancel();
            _aiCts.Dispose();
        }
        catch
        {
            // Ignore shutdown races.
        }
    }



    private void SaveGameAs()
    {
        if (_aiBusy || _verificationBusy || _logger is null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "棋譜に名前をつけて保存",
            Filter = "Star Runner 棋譜 (*.jsonl)|*.jsonl|すべてのファイル (*.*)|*.*",
            DefaultExt = "jsonl",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"StarRunner_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl"
        };

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (Directory.Exists(documents))
        {
            dialog.InitialDirectory = documents;
        }
        else if (Directory.Exists(_logger.LogDirectory))
        {
            dialog.InitialDirectory = _logger.LogDirectory;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _logger.SaveCopyAs(dialog.FileName);
            MessageBox.Show(
                this,
                $"現在の局面までの棋譜を保存しました。\r\n\r\n{dialog.FileName}",
                "棋譜を保存",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "棋譜を保存できません", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenGameDialog()
    {
        if (_aiBusy || _verificationBusy)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "対局を再開する棋譜を選択",
            Filter = "Star Runner 棋譜 (*.jsonl)|*.jsonl|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (Directory.Exists(documents))
        {
            dialog.InitialDirectory = documents;
        }
        else if (_logger is not null && Directory.Exists(_logger.LogDirectory))
        {
            dialog.InitialDirectory = _logger.LogDirectory;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            IReadOnlyList<ReplayGame> games = ReplayLoader.Load(dialog.FileName);
            if (games.Count != 1)
            {
                throw new InvalidDataException("複数局を含むヘッドレス棋譜はメイン画面の『開く』では再開できません。『ツール → 棋譜並べ』を使用してください。");
            }

            LoadGameForContinuation(games[0]);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "対局を開けません", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadGameForContinuation(ReplayGame replay)
    {
        // Rebuild from the original start position and every recorded move rather than
        // loading only the visible final board. This restores immediate-backtrack history,
        // fourfold repetition counts, side to move, and all other rule-relevant engine state.
        IReadOnlyList<GameEngine> snapshots = replay.BuildSnapshots();
        GameEngine restored = snapshots[^1].Clone();

        CancelAiWork();
        if (_logger is not null)
        {
            if (_game.Outcome == GameOutcome.Ongoing)
            {
                _logger.WriteSessionStopped(_game, "another_game_opened");
            }
            _logger.Dispose();
        }

        _sessionSerial++;
        _aiCts = new CancellationTokenSource();
        _game = restored;
        _mode = (MatchMode)Math.Clamp(_modeCombo.SelectedIndex, 0, 3);
        _selected = null;
        _selectedMoves = Array.Empty<Move>();
        _aiBusy = false;
        _cpuAutoRunning = false;
        ClearLiveEvaluationDisplay();

        _history.Items.Clear();
        _history.Items.Add("--- START ---");
        foreach (ReplayMove replayMove in replay.Moves)
        {
            string line = $"{replayMove.Ply,3}. {replayMove.Player.ShortName()} {replayMove.Move.ToNotation(),-16} [{replayMove.Detail}]";
            _history.Items.Add(line);
        }
        if (_history.Items.Count > 0)
        {
            _history.TopIndex = Math.Max(0, _history.Items.Count - 1);
        }

        // Continue logging into a fresh automatic log, but seed it with the complete
        // imported move list so any later Save As remains independently reopenable.
        var loggingReplay = new GameEngine(replay.StartConfiguration);
        CpuSkillProfile p1Skill = GetSelectedCpuSkillProfile(PlayerId.Player1);
        CpuSkillProfile p2Skill = GetSelectedCpuSkillProfile(PlayerId.Player2);
        _logger = GameLogger.Create(
            CurrentModeLogLabel(),
            p1Skill.Name,
            p2Skill.Name,
            p1Skill.MaxNodes,
            p2Skill.MaxNodes,
            (int)_cpuSearchParallelism.Value,
            loggingReplay);
        foreach (ReplayMove replayMove in replay.Moves)
        {
            if (!loggingReplay.TryApplyMove(replayMove.Move, out string? error))
            {
                _logger.Dispose();
                _logger = null;
                throw new InvalidDataException($"再開ログの再構築中に{replayMove.Ply}手目を適用できません。{error}");
            }
            _logger.WriteMoveApplied(replayMove.Player, replayMove.Move, loggingReplay, replayMove.Detail);
        }

        _board.Game = _game;
        _board.ClearSelection();
        UpdateUi();

        if (_game.Outcome == GameOutcome.Ongoing && IsCpu(_game.CurrentPlayer))
        {
            BeginInvoke(new Action(async () => await RunCpuTurnsAsync(singleStep: false)));
        }
    }

    private void ShowRules()
    {
        const string rules =
            "A案\n\n" +
            "○ = 周囲8方向へ1マス\n" +
            "★ = 上下左右へ1マス\n" +
            "★は隣接8方向の味方○へ移動して犠牲にできる\n" +
            "敵駒は取れない\n" +
            "★が敵最終列へ到達で勝利\n" +
            "相手の駒に四方を包囲された★は、次の手番で必ず退避する。退避できなければ負けとなる。\n" +
            "手番開始時に★の合法手0なら敗北\n" +
            "直前に動かした自分の駒は次の自分の手で原則元のマスへ戻せない\n" +
            "  ただし、その駒に他の合法手がない場合は戻ってよい\n" +
            "盤面の駒配置が完全に同じ状態が4回出現したら引き分け（千日手）。手番や直前手情報は比較しない";

        MessageBox.Show(this, rules, "A案 ルール", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenReplayDialog()
    {
        if (_aiBusy)
        {
            MessageBox.Show(this, "CPU思考中は棋譜並べを開けません。", "棋譜並べ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "棋譜JSONLを選択",
            Filter = "Star Runner JSONL (*.jsonl)|*.jsonl|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        string analysisDirectory = Path.Combine(AppContext.BaseDirectory, "analysis_logs");
        if (Directory.Exists(analysisDirectory))
        {
            dialog.InitialDirectory = analysisDirectory;
        }
        else if (_logger is not null && Directory.Exists(_logger.LogDirectory))
        {
            dialog.InitialDirectory = _logger.LogDirectory;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var form = new ReplayForm(dialog.FileName);
            form.Show(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "棋譜を開けません", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RunBitboardVerifierAsync()
    {
        if (_aiBusy || _verificationBusy)
        {
            return;
        }

        _verificationBusy = true;
        UpdateUi();
        try
        {
            BitboardVerifierResult result = await Task.Run(() => BitboardCorrectnessVerifier.Run());
            string reportPath = BitboardCorrectnessVerifier.WriteReport(result);
            string message = result.ToUserSummary() + $"\r\n\r\nレポート: {reportPath}";
            MessageBox.Show(
                this,
                message,
                result.Passed ? "Bitboard整合性検証 PASS" : "Bitboard整合性検証 FAIL",
                MessageBoxButtons.OK,
                result.Passed ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "Bitboard整合性検証エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _verificationBusy = false;
            UpdateUi();
        }
    }


    private async Task RunCpuSearchVerifierAsync()
    {
        if (_aiBusy || _verificationBusy)
        {
            return;
        }

        _verificationBusy = true;
        UpdateUi();
        try
        {
            CpuSearchVerifierResult result = await Task.Run(() => CpuSearchCorrectnessVerifier.Run());
            string reportPath = CpuSearchCorrectnessVerifier.WriteReport(result);
            string message = result.ToUserSummary() + $"\r\n\r\nレポート: {reportPath}";
            MessageBox.Show(
                this,
                message,
                result.Passed ? "CPU探索整合性検証 PASS" : "CPU探索整合性検証 FAIL",
                MessageBoxButtons.OK,
                result.Passed ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "CPU探索整合性検証エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _verificationBusy = false;
            UpdateUi();
        }
    }

    private void OpenDepthBenchmark()
    {
        if (_aiBusy)
        {
            MessageBox.Show(this, "CPU思考中はベンチマークを開始できません。", "深度ベンチ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_game.Outcome != GameOutcome.Ongoing)
        {
            MessageBox.Show(this, "終局していない局面で実行してください。", "深度ベンチ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        long suggestedNodeLimit = Math.Max(
            GetSelectedCpuSkillProfile(PlayerId.Player1).MaxNodes,
            GetSelectedCpuSkillProfile(PlayerId.Player2).MaxNodes);
        var form = new DepthBenchmarkForm(
            _game.Clone(),
            CpuSkillProfile.SearchDepthCap,
            0,
            suggestedNodeLimit,
            (int)_cpuSearchParallelism.Value);
        form.Show(this);
    }

    private void OpenHeadlessDialog()
    {
        using var dialog = new HeadlessBatchForm(CpuSkillProfile.SearchDepthCap, CpuSkillProfile.SearchDepthCap);
        dialog.ShowDialog(this);
    }

    private void OpenLogsFolder()
    {
        if (_logger is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_logger.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _logger.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ログフォルダを開けません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string ResultText()
    {
        return _game.Outcome switch
        {
            GameOutcome.Player1Win => "勝者: 青 (P1)",
            GameOutcome.Player2Win => "勝者: 赤 (P2)",
            GameOutcome.Draw => "引き分け",
            _ => "対局中"
        };
    }

    private static string EndReasonText(EndReason reason)
    {
        return reason switch
        {
            EndReason.GoalReached => "★が敵最終列へ到達",
            EndReason.RunnerImmobilized => "手番開始時に★の合法手（退避を含む）が0",
            EndReason.MovePolicyNoMove => "テスト戦略制約内の合法手が0",
            EndReason.FourfoldRepetition => "同一盤面が4回（千日手）",
            _ => "-"
        };
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 4, 3, 2)
    };

    private static void ConfigureNumeric(NumericUpDown control, decimal min, decimal max, decimal value, decimal increment)
    {
        control.Minimum = min;
        control.Maximum = max;
        control.Value = value;
        control.Increment = increment;
        control.Width = 100;
        control.TextAlign = HorizontalAlignment.Right;
    }

    private static void ConfigureButton(Button button, string text, int width)
    {
        button.Text = text;
        if (width > 0)
        {
            button.Width = width;
        }
        button.Height = 32;
        button.Margin = new Padding(3);
    }
}
