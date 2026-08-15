using System.Text;

namespace StarRunnerPrototype;

public sealed class ReplayForm : Form
{
    private readonly BoardControl _board = new();
    private readonly ComboBox _gameCombo = new();
    private readonly ListBox _moves = new();
    private readonly Button _firstButton = new();
    private readonly Button _previousButton = new();
    private readonly Button _nextButton = new();
    private readonly Button _lastButton = new();
    private readonly Label _fileLabel = new();
    private readonly Label _positionLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _strategyLabel = new();

    private readonly TabControl _lowerTabs = new();
    private readonly TabPage _movesTab = new("棋譜");
    private readonly TabPage _analysisTab = new("局面解析");
    private readonly NumericUpDown _analysisDepth = new();
    private readonly NumericUpDown _analysisNodeMillions = new();
    private readonly Button _analyzeButton = new();
    private readonly Button _cancelAnalysisButton = new();
    private readonly Label _analysisStateLabel = new();
    private readonly TextBox _analysisOutput = new();
    private readonly System.Windows.Forms.Timer _analysisRefreshTimer = new();

    private readonly IReadOnlyList<ReplayGame> _games;
    private ReplayGame? _currentReplay;
    private IReadOnlyList<GameEngine> _snapshots = Array.Empty<GameEngine>();
    private int _positionIndex;
    private bool _updatingMoveSelection;

    private CancellationTokenSource? _analysisCts;
    private CpuSearchMonitor? _analysisMonitor;
    private CpuDecision? _analysisDecision;
    private GameEngine? _analysisPosition;
    private PlayerId _analysisPerspective;
    private int _analysisPositionIndex = -1;
    private int _analysisGeneration;
    private int _analysisRequestedDepth;
    private long _analysisMaxNodes;
    private bool _analysisRunning;

    public ReplayForm(string path)
    {
        _games = ReplayLoader.Load(path);

        Text = "Star Runner - 棋譜並べ / 局面解析";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1040, 780);
        ClientSize = new Size(1200, 800);
        Font = new Font("Yu Gothic UI", 9f);
        KeyPreview = true;

        BuildUi(path);
        WireEvents();
        PopulateGames();
    }

    private void BuildUi(string path)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8),
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420f));
        Controls.Add(root);

        _board.Dock = DockStyle.Fill;
        root.Controls.Add(_board, 0, 0);

        // 上37% / 下63%。下側は「棋譜」「局面解析」をタブで切り替えるので、
        // 棋譜ListBoxの高さは従来どおり画面高の半分以上を維持できる。
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
            RowCount = 6,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 31f));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        side.Controls.Add(top, 0, 0);

        var title = new Label
        {
            Text = "棋譜並べ / JSONL Replay",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
            Margin = new Padding(0)
        };
        top.Controls.Add(title, 0, 0);

        _fileLabel.Text = $"ファイル: {Path.GetFileName(path)}";
        _fileLabel.Dock = DockStyle.Fill;
        _fileLabel.Margin = new Padding(0, 1, 0, 0);
        _fileLabel.Font = new Font(Font.FontFamily, 8.2f);
        top.Controls.Add(_fileLabel, 0, 1);

        var gamePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 1, 0, 0)
        };
        gamePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));
        gamePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        gamePanel.Controls.Add(new Label
        {
            Text = "対局",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 0, 0);
        _gameCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _gameCombo.Dock = DockStyle.Fill;
        _gameCombo.Margin = new Padding(0, 1, 0, 1);
        gamePanel.Controls.Add(_gameCombo, 1, 0);
        top.Controls.Add(gamePanel, 0, 2);

        var navigation = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 2, 0, 0)
        };
        for (int i = 0; i < 4; i++)
        {
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        }
        ConfigureButton(_firstButton, "|◀ 先頭", 0);
        ConfigureButton(_previousButton, "◀ 前手", 0);
        ConfigureButton(_nextButton, "次手 ▶", 0);
        ConfigureButton(_lastButton, "末尾 ▶|", 0);
        foreach (Button button in new[] { _firstButton, _previousButton, _nextButton, _lastButton })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(1);
        }
        navigation.Controls.Add(_firstButton, 0, 0);
        navigation.Controls.Add(_previousButton, 1, 0);
        navigation.Controls.Add(_nextButton, 2, 0);
        navigation.Controls.Add(_lastButton, 3, 0);
        top.Controls.Add(navigation, 0, 3);

        var statusPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 3, 0, 0),
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        statusPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _positionLabel.Dock = DockStyle.Fill;
        _positionLabel.TextAlign = ContentAlignment.MiddleLeft;
        _positionLabel.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
        _positionLabel.Margin = new Padding(5, 0, 5, 0);
        statusPanel.Controls.Add(_positionLabel, 0, 0);
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Margin = new Padding(5, 1, 5, 1);
        _statusLabel.Font = new Font(Font.FontFamily, 8.2f);
        statusPanel.Controls.Add(_statusLabel, 0, 1);
        top.Controls.Add(statusPanel, 0, 4);

        _strategyLabel.Dock = DockStyle.Fill;
        _strategyLabel.BorderStyle = BorderStyle.FixedSingle;
        _strategyLabel.Padding = new Padding(5);
        _strategyLabel.Margin = new Padding(0, 3, 0, 0);
        _strategyLabel.Font = new Font(Font.FontFamily, 8.2f);
        top.Controls.Add(_strategyLabel, 0, 5);

        BuildLowerTabs(side);
    }

    private void BuildLowerTabs(TableLayoutPanel side)
    {
        _lowerTabs.Dock = DockStyle.Fill;
        _lowerTabs.Margin = new Padding(0, 4, 0, 0);
        _lowerTabs.TabPages.Add(_movesTab);
        _lowerTabs.TabPages.Add(_analysisTab);
        side.Controls.Add(_lowerTabs, 0, 1);

        var movesPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(5)
        };
        movesPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        movesPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        movesPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        _movesTab.Controls.Add(movesPanel);

        movesPanel.Controls.Add(new Label
        {
            Text = "棋譜（クリックでその局面へ）",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 0, 0);
        _moves.Dock = DockStyle.Fill;
        _moves.HorizontalScrollbar = true;
        _moves.Margin = new Padding(0);
        movesPanel.Controls.Add(_moves, 0, 1);

        var help = new Label
        {
            Text = "← 前手 / → 次手 / Home 先頭 / End 末尾\n黄=移動元 / 水色=移動先",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 0),
            Font = new Font(Font.FontFamily, 8.2f)
        };
        movesPanel.Controls.Add(help, 0, 2);

        var analysisPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(5)
        };
        analysisPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
        analysisPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        analysisPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _analysisTab.Controls.Add(analysisPanel);

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        controls.Controls.Add(MakeLabel("最大D"));
        _analysisDepth.Minimum = 1;
        _analysisDepth.Maximum = 14;
        _analysisDepth.Value = 12;
        _analysisDepth.Width = 46;
        _analysisDepth.Margin = new Padding(0, 3, 6, 0);
        controls.Controls.Add(_analysisDepth);

        controls.Controls.Add(MakeLabel("Node上限(M)"));
        _analysisNodeMillions.Minimum = 1;
        _analysisNodeMillions.Maximum = 500;
        _analysisNodeMillions.Value = 50;
        _analysisNodeMillions.Width = 58;
        _analysisNodeMillions.Margin = new Padding(0, 3, 6, 0);
        controls.Controls.Add(_analysisNodeMillions);

        ConfigureButton(_analyzeButton, "この局面を解析", 96);
        _analyzeButton.Height = 28;
        _analyzeButton.Margin = new Padding(0, 1, 4, 0);
        controls.Controls.Add(_analyzeButton);

        ConfigureButton(_cancelAnalysisButton, "中止", 44);
        _cancelAnalysisButton.Height = 28;
        _cancelAnalysisButton.Margin = new Padding(0, 1, 0, 0);
        _cancelAnalysisButton.Enabled = false;
        controls.Controls.Add(_cancelAnalysisButton);
        controls.MinimumSize = new Size(0, _cancelAnalysisButton.Bottom + 2);
        analysisPanel.Controls.Add(controls, 0, 0);

        _analysisStateLabel.Text = "P1基準 / 未解析";
        _analysisStateLabel.Dock = DockStyle.Fill;
        _analysisStateLabel.TextAlign = ContentAlignment.MiddleLeft;
        _analysisStateLabel.Font = new Font(Font.FontFamily, 8.5f, FontStyle.Bold);
        _analysisStateLabel.Margin = new Padding(0);
        analysisPanel.Controls.Add(_analysisStateLabel, 0, 1);

        _analysisOutput.Dock = DockStyle.Fill;
        _analysisOutput.Multiline = true;
        _analysisOutput.ReadOnly = true;
        _analysisOutput.ScrollBars = ScrollBars.Both;
        _analysisOutput.WordWrap = false;
        _analysisOutput.Font = new Font("Consolas", 9f);
        _analysisOutput.BackColor = Color.White;
        _analysisOutput.Text = "棋譜から局面を選び、「この局面を解析」を押してください。\r\n" +
                               "深度ごとの評価値・最善手・最終候補手・静的評価内訳を表示します。";
        analysisPanel.Controls.Add(_analysisOutput, 0, 2);
    }

    private void WireEvents()
    {
        _gameCombo.SelectedIndexChanged += (_, _) => LoadSelectedGame();
        _moves.SelectedIndexChanged += (_, _) =>
        {
            if (!_updatingMoveSelection && _moves.SelectedIndex >= 0)
            {
                NavigateTo(_moves.SelectedIndex);
            }
        };
        _firstButton.Click += (_, _) => NavigateTo(0);
        _previousButton.Click += (_, _) => NavigateTo(_positionIndex - 1);
        _nextButton.Click += (_, _) => NavigateTo(_positionIndex + 1);
        _lastButton.Click += (_, _) => NavigateTo(_snapshots.Count - 1);
        _analyzeButton.Click += async (_, _) => await AnalyzeCurrentPositionAsync();
        _cancelAnalysisButton.Click += (_, _) => RequestAnalysisCancellation();
        _analysisRefreshTimer.Interval = 500;
        _analysisRefreshTimer.Tick += (_, _) => RefreshAnalysisProgress();
        FormClosed += (_, _) =>
        {
            _analysisGeneration++;
            _analysisRefreshTimer.Stop();
            _analysisRunning = false;
            DetachAndCancelAnalysis();
        };
        KeyDown += ReplayFormOnKeyDown;
    }

    private void PopulateGames()
    {
        _gameCombo.BeginUpdate();
        try
        {
            _gameCombo.Items.Clear();
            foreach (ReplayGame game in _games)
            {
                _gameCombo.Items.Add(game.DisplayName);
            }
        }
        finally
        {
            _gameCombo.EndUpdate();
        }

        if (_gameCombo.Items.Count > 0)
        {
            _gameCombo.SelectedIndex = 0;
        }
    }

    private void LoadSelectedGame()
    {
        int index = _gameCombo.SelectedIndex;
        if (index < 0 || index >= _games.Count)
        {
            return;
        }

        ResetAnalysisForPositionChange();
        try
        {
            UseWaitCursor = true;
            _currentReplay = _games[index];
            _snapshots = _currentReplay.BuildSnapshots();
            PopulateMoveList(_currentReplay);
            ScenarioMovePolicy? policy = _currentReplay.MovePolicy;
            _strategyLabel.Text =
                $"Rules: {_currentReplay.Rules.Id} / Start: {_currentReplay.StartConfiguration.CurrentPlayer.ShortName()} / {_currentReplay.StartConfiguration.Name}\n" +
                $"P1: {policy?.P1.Id ?? "Free"}\n" +
                $"P2: {policy?.P2.Id ?? "Free"}";
            NavigateTo(0);
        }
        catch (Exception ex)
        {
            _snapshots = Array.Empty<GameEngine>();
            _board.Game = null;
            MessageBox.Show(this, ex.Message, "棋譜を再構築できません", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void PopulateMoveList(ReplayGame replay)
    {
        _moves.BeginUpdate();
        try
        {
            _moves.Items.Clear();
            _moves.Items.Add("  0. --- START ---");
            foreach (ReplayMove move in replay.Moves)
            {
                string detail = string.IsNullOrWhiteSpace(move.Detail) ? string.Empty : $" [{move.Detail}]";
                _moves.Items.Add($"{move.Ply,3}. {move.Player.ShortName()} {move.Move.ToNotation(),-16}{detail}");
            }
        }
        finally
        {
            _moves.EndUpdate();
        }
    }

    private void NavigateTo(int requestedIndex)
    {
        if (_currentReplay is null || _snapshots.Count == 0)
        {
            return;
        }

        int newIndex = Math.Clamp(requestedIndex, 0, _snapshots.Count - 1);
        if (newIndex != _positionIndex)
        {
            ResetAnalysisForPositionChange();
        }
        _positionIndex = newIndex;
        GameEngine position = _snapshots[_positionIndex];
        _board.Game = position;

        Move? lastMove = _positionIndex > 0 && _positionIndex <= _currentReplay.Moves.Count
            ? _currentReplay.Moves[_positionIndex - 1].Move
            : null;
        _board.SetLastMoveHighlight(lastMove);

        _updatingMoveSelection = true;
        try
        {
            _moves.SelectedIndex = _positionIndex;
            if (_moves.SelectedIndex >= 0)
            {
                _moves.TopIndex = Math.Max(0, _moves.SelectedIndex - 5);
            }
        }
        finally
        {
            _updatingMoveSelection = false;
        }

        string turnOrResult = position.Outcome == GameOutcome.Ongoing
            ? $"手番 {position.CurrentPlayer.ShortName()}"
            : $"{position.Outcome} / {position.EndReason}";
        _positionLabel.Text = $"局面: {_positionIndex} / {_currentReplay.Moves.Count}手  ({turnOrResult})";

        string recorded = _positionIndex == _currentReplay.Moves.Count
            ? $"\n記録上の終局: {_currentReplay.RecordedOutcome} / {_currentReplay.RecordedEndReason}"
            : string.Empty;
        _statusLabel.Text =
            $"残り○ P1:{position.CountBlockers(PlayerId.Player1)} / P2:{position.CountBlockers(PlayerId.Player2)}" +
            $"\n反復回数: {position.CurrentPositionRepetitionCount()}" + recorded;

        _firstButton.Enabled = _positionIndex > 0;
        _previousButton.Enabled = _positionIndex > 0;
        _nextButton.Enabled = _positionIndex < _snapshots.Count - 1;
        _lastButton.Enabled = _positionIndex < _snapshots.Count - 1;

        if (_analysisPositionIndex != _positionIndex && !_analysisRunning)
        {
            _analysisStateLabel.Text = "P1基準 / 未解析";
        }
    }

    private async Task AnalyzeCurrentPositionAsync()
    {
        if (_snapshots.Count == 0 || _positionIndex < 0 || _positionIndex >= _snapshots.Count)
        {
            return;
        }

        if (_analysisRunning)
        {
            return;
        }

        GameEngine source = _snapshots[_positionIndex];
        if (source.Outcome != GameOutcome.Ongoing)
        {
            EvaluationBreakdown terminal = CpuPlayer.EvaluateDetailed(source, PlayerId.Player1);
            _analysisStateLabel.Text = "終局局面 / P1基準";
            _analysisOutput.Text = $"この局面はすでに終局しています。\r\n評価値(P1基準): {FormatSigned(terminal.Total)}";
            _lowerTabs.SelectedTab = _analysisTab;
            return;
        }

        _analysisGeneration++;
        int generation = _analysisGeneration;
        _analysisPositionIndex = _positionIndex;
        _analysisPosition = source.Clone();
        _analysisPerspective = source.CurrentPlayer;
        _analysisRequestedDepth = (int)_analysisDepth.Value;
        _analysisMaxNodes = (long)_analysisNodeMillions.Value * 1_000_000L;
        _analysisDecision = null;
        _analysisMonitor = new CpuSearchMonitor();
        var analysisCts = new CancellationTokenSource();
        CancellationTokenSource? previousCts = Interlocked.Exchange(ref _analysisCts, analysisCts);
        TryCancel(previousCts);
        CancellationToken token = analysisCts.Token;
        GameEngine analysisPosition = source.Clone();
        PlayerId analysisPerspective = _analysisPerspective;
        CpuSearchMonitor analysisMonitor = _analysisMonitor;
        _analysisRunning = true;

        _analyzeButton.Enabled = false;
        _cancelAnalysisButton.Enabled = true;
        _analysisDepth.Enabled = false;
        _analysisNodeMillions.Enabled = false;
        _analysisStateLabel.Text = $"解析中... 局面{_analysisPositionIndex} / {_analysisPerspective.ShortName()}手番 / P1基準";
        _analysisOutput.Text = BuildAnalysisReport(running: true);
        _lowerTabs.SelectedTab = _analysisTab;
        _analysisRefreshTimer.Start();

        var options = new CpuSearchOptions(
            MaxDepth: _analysisRequestedDepth,
            TimeLimitMilliseconds: 0,
            MaxNodes: _analysisMaxNodes,
            UseTranspositionTable: true,
            CollectExactRootScores: false,
            RandomTopK: 1,
            RandomScoreWindow: 0,
            RandomSelectionTemperature: 0,
            RandomMoveProbability: 0,
            RandomSeed: 0,
            CycleBreakScoreWindow: 0,
            MaxParallelism: 1,
            UseBelowNormalThreadPriority: true);

        try
        {
            CpuDecision decision = await Task.Run(
                () => CpuPlayer.DecideMove(analysisPosition, analysisPerspective, options, token, analysisMonitor),
                token);

            if (generation != _analysisGeneration || _analysisPositionIndex != _positionIndex)
            {
                return;
            }

            _analysisDecision = decision;
            CpuSearchTelemetry finalTelemetry = decision.SearchTelemetry;
            string scoutSuffix = finalTelemetry.MateDistanceScoutProofScore == 0
                ? string.Empty
                : finalTelemetry.MateDistanceScoutProofExact
                    ? $" / Scout {(finalTelemetry.MateDistanceScoutProofScore > 0 ? "+" : "-")}M{finalTelemetry.MateDistanceScoutProofDepth}"
                    : $" / Scout {(finalTelemetry.MateDistanceScoutProofScore > 0 ? "+" : "-")}<=M{finalTelemetry.MateDistanceScoutProofDepth}";
            _analysisStateLabel.Text =
                $"解析完了 / 通常D{decision.Depth}/{decision.RequestedDepth}{scoutSuffix} / {decision.Nodes:N0} nodes / P1基準";
            _analysisOutput.Text = BuildAnalysisReport(running: false);
        }
        catch (OperationCanceledException)
        {
            if (generation == _analysisGeneration && _analysisPositionIndex == _positionIndex)
            {
                _analysisStateLabel.Text = "解析中止 / P1基準";
                _analysisOutput.Text = BuildAnalysisReport(running: false) + "\r\n\r\n[解析を中止しました]";
            }
        }
        catch (Exception ex)
        {
            if (generation == _analysisGeneration && _analysisPositionIndex == _positionIndex)
            {
                _analysisStateLabel.Text = "解析エラー";
                _analysisOutput.Text = ex.ToString();
            }
        }
        finally
        {
            if (generation == _analysisGeneration)
            {
                _analysisRunning = false;
                _analysisRefreshTimer.Stop();
                _analyzeButton.Enabled = true;
                _cancelAnalysisButton.Enabled = false;
                _analysisDepth.Enabled = true;
                _analysisNodeMillions.Enabled = true;
            }

            // The analysis task owns disposal.  The shared field is only a non-owning
            // reference to the currently active source.  Clear it regardless of the
            // generation so a position-change cancellation can never leave a disposed
            // CancellationTokenSource reachable from the UI event handlers.
            Interlocked.CompareExchange(ref _analysisCts, null, analysisCts);
            analysisCts.Dispose();
        }
    }

    private void RefreshAnalysisProgress()
    {
        if (!_analysisRunning || _analysisMonitor is null || _analysisPositionIndex != _positionIndex)
        {
            return;
        }

        IReadOnlyList<CpuCompletedDepth> completed = _analysisMonitor.GetCompletedDepths();
        string latest = completed.Count == 0 ? "探索開始" : $"D{completed[^1].Depth}完了";
        if (_analysisMonitor.TryGetMateScoutSnapshot(out CpuMateScoutProgress scoutProgress))
        {
            latest += $" / Mate Scout D{scoutProgress.Depth} {(scoutProgress.ProbingWin ? "勝ち" : "負け")}{(scoutProgress.Refining ? "距離確定" : "証明")}中";
        }
        else if (_analysisMonitor.TryGetSnapshot(out CpuSearchProgress progress) && progress.IsProvisional)
        {
            latest += $" / D{progress.TargetDepth}探索中";
        }
        _analysisStateLabel.Text = $"解析中... {latest} / 局面{_analysisPositionIndex} / P1基準";
        _analysisOutput.Text = BuildAnalysisReport(running: true);
    }

    private string BuildAnalysisReport(bool running)
    {
        if (_analysisPosition is null || _analysisMonitor is null)
        {
            return "未解析です。";
        }

        var sb = new StringBuilder();
        EvaluationBreakdown staticEval = CpuPlayer.EvaluateDetailed(_analysisPosition, PlayerId.Player1);
        IReadOnlyList<CpuCompletedDepth> depths = _analysisMonitor.GetCompletedDepths();

        sb.AppendLine($"局面 {_analysisPositionIndex} / 手番 {_analysisPerspective.ShortName()} / 評価値はP1基準");
        sb.AppendLine($"設定: 最大D{_analysisRequestedDepth} / Node上限 {_analysisMaxNodes:N0} / 1 thread / Mate Scout最大D{Math.Min(99, _analysisRequestedDepth + 8)}");
        sb.AppendLine($"静的評価(P1): {FormatSigned(staticEval.Total)}");
        sb.AppendLine();
        sb.AppendLine("Depth   評価(P1)       最善手              累計Nodes       経過");
        sb.AppendLine("-----   ------------   ------------------   -------------   --------");

        foreach (CpuCompletedDepth depth in depths)
        {
            int p1Score = ToP1Score(depth.Score, _analysisPerspective);
            sb.AppendLine($"D{depth.Depth,-4}   {FormatSigned(p1Score),12}   {depth.BestMove.ToNotation(),-18}   {depth.Nodes,13:N0}   {depth.ElapsedMilliseconds,6:N0}ms");
        }

        if (running && _analysisMonitor.TryGetMateScoutSnapshot(out CpuMateScoutProgress scoutProgress))
        {
            sb.AppendLine();
            sb.AppendLine($"Mate Scout: D{scoutProgress.Depth} / {(scoutProgress.ProbingWin ? "手番側の強制勝ち" : "手番側の強制負け")}を{(scoutProgress.Refining ? "距離確定" : "証明")}中");
        }
        else if (running && _analysisMonitor.TryGetSnapshot(out CpuSearchProgress progress) && progress.IsProvisional)
        {
            int provisionalP1 = ToP1Score(progress.Score, _analysisPerspective);
            const string pendingNodes = "---";
            sb.AppendLine($"D{progress.TargetDepth,-4}   {FormatSigned(provisionalP1),12}   {progress.BestMove.ToNotation(),-18}   {pendingNodes,13}   暫定");
        }

        if (_analysisDecision is CpuDecision decision)
        {
            sb.AppendLine();
            string stopReason = decision.NodeLimitReached ? "node-limit" : decision.TimedOut ? "time-limit" : "completed";
            sb.AppendLine($"最終: D{decision.Depth}/{decision.RequestedDepth}  {decision.Nodes:N0} nodes  {decision.ElapsedMilliseconds:N0}ms  {stopReason}");
            sb.AppendLine($"最善手: {decision.Move.ToNotation()}  評価(P1): {FormatSigned(ToP1Score(decision.Score, _analysisPerspective))}");

            int provenLossMoves = decision.Candidates.Count(ProvesForcedLossForPerspective);
            int provenWinMoves = decision.Candidates.Count(ProvesForcedWinForPerspective);
            sb.AppendLine($"候補手: {decision.Candidates.Count} / 読み範囲内で勝ち確定 {provenWinMoves} / 負け確定 {provenLossMoves} （手番側基準）");
            CpuSearchTelemetry telemetry = decision.SearchTelemetry;
            sb.AppendLine(
                $"探索選択性: PVS probe {telemetry.PvsNullWindowProbes:N0} / re-search {telemetry.PvsResearches:N0}, " +
                $"LMR {telemetry.LmrReducedSearches:N0} / verify {telemetry.LmrVerificationResearches:N0}, " +
                $"mate-prune {telemetry.MateDistancePrunes:N0}, only-survival ext {telemetry.OnlySurvivalExtensions:N0}, " +
                $"adaptive-root {telemetry.AdaptiveRootDeepeningPasses:N0} pass / +{telemetry.MaxAdaptiveRootDeepeningPlyReached}ply");

            if (telemetry.MateDistanceScoutProbes > 0)
            {
                string scoutResult = telemetry.MateDistanceScoutProofScore > 0
                    ? telemetry.MateDistanceScoutProofExact
                        ? $"強制勝ち M{telemetry.MateDistanceScoutProofDepth}"
                        : $"強制勝ち <=M{telemetry.MateDistanceScoutProofDepth}（距離未確定）"
                    : telemetry.MateDistanceScoutProofScore < 0
                        ? telemetry.MateDistanceScoutProofExact
                            ? $"強制負け M{telemetry.MateDistanceScoutProofDepth}"
                            : $"強制負け <=M{telemetry.MateDistanceScoutProofDepth}（距離未確定）"
                        : "未証明";
                string scoutMode = telemetry.MateDistanceScoutDirection > 0 ? "win-high-first" : "loss-high-first";
                sb.AppendLine(
                    $"Mate Scout: {telemetry.MateDistanceScoutProbes:N0} probes / {telemetry.MateDistanceScoutNodes:N0} nodes / " +
                    $"mode {scoutMode} / 完了最大D{telemetry.MateDistanceScoutMaxCompletedDepth} / " +
                    $"着手最大D{telemetry.MateDistanceScoutMaxDepthReached} / {scoutResult}");

                IReadOnlyList<CpuMateScoutProbeTelemetry>? probeDetails = telemetry.MateDistanceScoutProbeDetails;
                if (probeDetails is { Count: > 0 })
                {
                    foreach (CpuMateScoutProbeTelemetry probe in probeDetails)
                    {
                        string directionLabel = probe.ProbingWin ? "Win " : "Loss";
                        string phaseLabel = probe.Refining ? "距離確定" : "存在確認";
                        string resultLabel = !probe.Completed ? "node-limit" : probe.Proven ? "証明" : "未証明";
                        sb.AppendLine(
                            $"  {directionLabel} D{probe.Depth,-2}  {resultLabel,-10}  {probe.Nodes,13:N0} nodes  ({phaseLabel})");
                    }
                }
            }
            sb.AppendLine();
            sb.AppendLine($"最終候補手（通常探索D{decision.Depth} / 表示はP1基準）");
            int rank = 1;
            foreach (CpuCandidate candidate in decision.Candidates.Take(24))
            {
                int p1Score = ToP1Score(candidate.SearchScore, _analysisPerspective);
                sb.AppendLine($"{rank,2}. {candidate.Move.ToNotation(),-18} {FormatSigned(p1Score),12}  {candidate.Bound}");
                rank++;
            }
        }

        sb.AppendLine();
        sb.AppendLine($"静的評価内訳（現在局面 / P1基準 / profile={CpuEvaluationProfileProvider.Current.Name} / phase={staticEval.PhasePermille / 10.0:0.0}%）");
        sb.AppendLine($"  ★進行度             {FormatSigned(staticEval.RunnerProgress),8}");
        sb.AppendLine($"  ★可動性             {FormatSigned(staticEval.RunnerMobility),8}");
        sb.AppendLine($"  ○実効戦力           {FormatSigned(staticEval.BlockerMaterial),8}");
        sb.AppendLine($"  ★周辺支援           {FormatSigned(staticEval.FriendlyRunnerSupport),8}");
        sb.AppendLine($"  前方圧力             {FormatSigned(staticEval.FrontPressure),8}");
        sb.AppendLine($"  ゴール防御           {FormatSigned(staticEval.GoalDefense),8}");
        sb.AppendLine($"  即時ゴール脅威       {FormatSigned(staticEval.ImmediateGoalThreats),8}");
        sb.AppendLine($"  ○前進               {FormatSigned(staticEval.BlockerAdvancement),8}");
        sb.AppendLine($"  橋頭堡接続           {FormatSigned(staticEval.BridgeheadConnection),8}");
        sb.AppendLine($"  ★ゴール経路         {FormatSigned(staticEval.RunnerGoalPath),8}");
        sb.AppendLine($"  準備済ゴール脅威     {FormatSigned(staticEval.PreparedGoalThreat),8}");
        sb.AppendLine($"  無応答ゴール脅威     {FormatSigned(staticEval.UnansweredGoalThreat),8}");
        sb.AppendLine($"  接続ゴール脅威       {FormatSigned(staticEval.ConnectedGoalThreat),8}");
        sb.AppendLine($"  有効★進行           {FormatSigned(staticEval.ViableRunnerProgress),8}");
        sb.AppendLine($"  犠牲負債             {FormatSigned(staticEval.SacrificeDebt),8}");
        sb.AppendLine($"  TOTAL                {FormatSigned(staticEval.Total),8}");

        return sb.ToString();
    }

    private void ResetAnalysisForPositionChange()
    {
        _analysisGeneration++;
        DetachAndCancelAnalysis();
        _analysisRefreshTimer.Stop();
        _analysisRunning = false;
        _analysisMonitor = null;
        _analysisDecision = null;
        _analysisPosition = null;
        _analysisPositionIndex = -1;
        _analyzeButton.Enabled = true;
        _cancelAnalysisButton.Enabled = false;
        _analysisDepth.Enabled = true;
        _analysisNodeMillions.Enabled = true;
        _analysisStateLabel.Text = "P1基準 / 未解析";
        _analysisOutput.Text = "この局面は未解析です。\r\n「この局面を解析」を押してください。";
    }


    private void RequestAnalysisCancellation()
    {
        // The button does not take ownership.  The running analysis disposes its own CTS.
        TryCancel(Volatile.Read(ref _analysisCts));
    }

    private void DetachAndCancelAnalysis()
    {
        // Position changes/form close invalidate the current analysis immediately.
        // Detach first so its async finally block may dispose the CTS without leaving
        // a disposed instance reachable by a later navigation event.
        CancellationTokenSource? cts = Interlocked.Exchange(ref _analysisCts, null);
        TryCancel(cts);
    }

    private static void TryCancel(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A cancellation request can race the analysis finally block.  Cancellation
            // is best-effort here; a disposed source already cannot be used by new work.
        }
    }

    private static bool ProvesForcedWinForPerspective(CpuCandidate candidate) =>
        candidate.SearchScore >= 997_952 &&
        (candidate.Bound.Equals("exact", StringComparison.OrdinalIgnoreCase) ||
         candidate.Bound.Equals("lower", StringComparison.OrdinalIgnoreCase));

    private static bool ProvesForcedLossForPerspective(CpuCandidate candidate) =>
        candidate.SearchScore <= -997_952 &&
        (candidate.Bound.Equals("exact", StringComparison.OrdinalIgnoreCase) ||
         candidate.Bound.Equals("upper", StringComparison.OrdinalIgnoreCase));

    private static int ToP1Score(int score, PlayerId perspective) =>
        perspective == PlayerId.Player1 ? score : -score;

    private static string FormatSigned(int value) =>
        value > 0 ? $"+{value:N0}" : $"{value:N0}";

    private void ReplayFormOnKeyDown(object? sender, KeyEventArgs e)
    {
        // NumericUpDown/TextBox操作中は棋譜移動ショートカットを奪わない。
        if (ActiveControl is NumericUpDown || ActiveControl == _analysisOutput)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Left:
                NavigateTo(_positionIndex - 1);
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            case Keys.Right:
                NavigateTo(_positionIndex + 1);
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            case Keys.Home:
                NavigateTo(0);
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            case Keys.End:
                NavigateTo(_snapshots.Count - 1);
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
        }
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 4, 3, 2)
    };

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
