using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace StarRunnerPrototype;

public sealed class HeadlessBatchForm : Form
{
    private const string ScenarioLabVersion = "v0.2.36.2";
    private readonly NumericUpDown _games = new();
    private readonly NumericUpDown _p1Depth = new();
    private readonly NumericUpDown _p2Depth = new();
    private readonly NumericUpDown _timeLimit = new();
    private readonly NumericUpDown _nodeLimit = new();
    private readonly NumericUpDown _parallelism = new();
    private readonly NumericUpDown _searchParallelism = new();
    private readonly NumericUpDown _progressIntervalSeconds = new();
    private readonly NumericUpDown _maxPlies = new();
    private readonly NumericUpDown _openingPlies = new();
    private readonly NumericUpDown _openingTopK = new();
    private readonly NumericUpDown _openingWindow = new();
    private readonly NumericUpDown _seed = new();
    private readonly CheckBox _saveMoves = new();
    private readonly TextBox _scenarioPath = new();
    private readonly Button _scenarioBrowseButton = new();
    private readonly Button _scenarioClearButton = new();
    private readonly Label _scenarioStatus = new();
    private IReadOnlyList<HeadlessScenarioCase>? _loadedScenarioCases;
    private readonly Button _startButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _openButton = new();
    private readonly Button _evaluationTunerButton = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _progressLabel = new();
    private readonly TextBox _resultBox = new();
    private CancellationTokenSource? _cts;
    private bool _running;

    public HeadlessBatchForm(int suggestedP1Depth, int suggestedP2Depth)
    {
        Text = $"超高速ヘッドレス対戦 - Scenario Lab - {ScenarioLabVersion}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 900);
        ClientSize = new Size(840, 960);
        Font = new Font("Yu Gothic UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildUi(suggestedP1Depth, suggestedP2Depth);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running)
        {
            _cts?.Cancel();
        }
        base.OnFormClosing(e);
    }

    private void BuildUi(int suggestedP1Depth, int suggestedP2Depth)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(14),
            AutoScroll = true
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var intro = new Label
        {
            AutoSize = true,
            Text = "盤面描画と通常の対局ログを止め、CPU同士を低優先度で並列実行します。\n" +
                   "Scenario JSONから任意盤面・手番・Depth等の設定・Rush制約を読み込み、複数ケースを連続実行できます。\n" +
                   "多数局は『同時対局を増やす / 1局内探索=1thread』がthroughput向け初期値です。進捗には概算残り時間も表示します。\n" +
                   "対局ルールは製品版と同じStandardルールで固定です。",
            Margin = new Padding(0, 0, 0, 12)
        };
        root.Controls.Add(intro, 0, 0);

        var settings = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 4,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 10)
        };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        root.Controls.Add(settings, 0, 1);

        ConfigureNumeric(_games, 1, 100000, 20, 20);
        ConfigureNumeric(_p1Depth, 1, 10, Math.Clamp(suggestedP1Depth, 1, 10), 1);
        ConfigureNumeric(_p2Depth, 1, 10, Math.Clamp(suggestedP2Depth, 1, 10), 1);
        ConfigureNumeric(_timeLimit, 0, 120000, 0, 50);
        ConfigureNumeric(_nodeLimit, 0, 2_000_000_000m, 0, 100_000);
        int defaultSearchThreads = 1;
        int defaultConcurrentGames = Math.Min(8, Math.Max(1, Environment.ProcessorCount * 2));
        ConfigureNumeric(_parallelism, 1, Math.Max(1, Environment.ProcessorCount * 2), defaultConcurrentGames, 1);
        ConfigureNumeric(_searchParallelism, 1, Math.Max(1, Environment.ProcessorCount), defaultSearchThreads, 1);
        ConfigureNumeric(_progressIntervalSeconds, 1, 60, 2, 1);
        ConfigureNumeric(_maxPlies, 20, 5000, 300, 20);
        ConfigureNumeric(_openingPlies, 0, 40, 4, 1);
        ConfigureNumeric(_openingTopK, 1, 12, 3, 1);
        ConfigureNumeric(_openingWindow, 0, 100000, 120, 20);
        ConfigureNumeric(_seed, int.MinValue, int.MaxValue, 12345, 1);

        AddSetting(settings, 0, "対局数 / 条件", _games, "P1 探索深度", _p1Depth);
        AddSetting(settings, 1, "P2 探索深度", _p2Depth, "1手上限(ms / 0=無制限)", _timeLimit);
        AddSetting(settings, 2, "1手Node上限 (0=無制限)", _nodeLimit, "同時対局数", _parallelism);
        AddSetting(settings, 3, "1局内探索スレッド", _searchParallelism, "進捗更新間隔(秒)", _progressIntervalSeconds);
        AddSetting(settings, 4, "最大手数", _maxPlies, "ランダム序盤手数", _openingPlies);
        AddSetting(settings, 5, "序盤 上位K候補", _openingTopK, "序盤Score幅", _openingWindow);
        AddSetting(settings, 6, "Seed", _seed, "", new Label { AutoSize = true });

        var scenarioGroup = new GroupBox
        {
            Text = "Scenario Lab - 盤面＋設定JSON（任意）",
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 0, 10)
        };
        var scenarioLayout = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2
        };
        scenarioLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scenarioLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        scenarioLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        scenarioGroup.Controls.Add(scenarioLayout);

        _scenarioPath.Dock = DockStyle.Fill;
        _scenarioPath.ReadOnly = true;
        _scenarioPath.PlaceholderText = "未読込（通常の初期盤面を使用）";
        ConfigureButton(_scenarioBrowseButton, "JSON読込", 100);
        ConfigureButton(_scenarioClearButton, "解除", 80);
        _scenarioStatus.AutoSize = true;
        _scenarioStatus.Text = "通常初期盤面 / Strategy: Free vs Free";
        _scenarioStatus.Margin = new Padding(0, 5, 0, 0);
        scenarioLayout.Controls.Add(_scenarioPath, 0, 0);
        scenarioLayout.Controls.Add(_scenarioBrowseButton, 1, 0);
        scenarioLayout.Controls.Add(_scenarioClearButton, 2, 0);
        scenarioLayout.Controls.Add(_scenarioStatus, 0, 1);
        scenarioLayout.SetColumnSpan(_scenarioStatus, 3);
        root.Controls.Add(scenarioGroup, 0, 2);

        _scenarioBrowseButton.Click += (_, _) => LoadScenarioFile();
        _scenarioClearButton.Click += (_, _) => ClearScenarioFile();

        _saveMoves.Text = "各ゲームの全着手列も batch JSONL に保存（各条件ごとにJSONL生成）";
        _saveMoves.AutoSize = true;
        _saveMoves.Margin = new Padding(0, 2, 0, 10);
        root.Controls.Add(_saveMoves, 0, 3);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };
        ConfigureButton(_startButton, "実行", 100);
        ConfigureButton(_cancelButton, "中止", 100);
        ConfigureButton(_openButton, "解析ログを開く", 140);
        ConfigureButton(_evaluationTunerButton, "評価関数チューナー", 150);
        _cancelButton.Enabled = false;
        buttonPanel.Controls.AddRange(new Control[] { _startButton, _cancelButton, _openButton, _evaluationTunerButton });
        root.Controls.Add(buttonPanel, 0, 4);

        var outputPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1
        };
        outputPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outputPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outputPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(outputPanel, 0, 5);

        _progress.Dock = DockStyle.Top;
        _progress.Minimum = 0;
        _progress.Maximum = 100;
        _progress.Height = 24;
        outputPanel.Controls.Add(_progress, 0, 0);

        _progressLabel.AutoSize = true;
        _progressLabel.Text = "待機中";
        _progressLabel.Margin = new Padding(0, 6, 0, 6);
        outputPanel.Controls.Add(_progressLabel, 0, 1);

        _resultBox.Dock = DockStyle.Fill;
        _resultBox.Multiline = true;
        _resultBox.ReadOnly = true;
        _resultBox.ScrollBars = ScrollBars.Both;
        _resultBox.WordWrap = false;
        outputPanel.Controls.Add(_resultBox, 0, 2);

        _startButton.Click += async (_, _) => await StartBatchAsync();
        _cancelButton.Click += (_, _) => _cts?.Cancel();
        _openButton.Click += (_, _) => OpenAnalysisFolder();
        _evaluationTunerButton.Click += (_, _) => OpenEvaluationTuner();

        // WinFormsのAutoSize子要素は、一度広い幅でPreferredSizeが確定すると
        // フォームを縮めた際に親TableLayoutPanelを横へ押し出すことがある。
        // 長文ラベル/チェックボックスの最大幅を現在のクライアント幅へ追従させ、
        // 拡大→縮小でも再ラップされるようにする。AutoScrollは極端に狭い場合の保険。
        bool applyingResponsiveWidths = false;
        void ApplyResponsiveWidths()
        {
            if (applyingResponsiveWidths) return;
            applyingResponsiveWidths = true;
            try
            {
                int available = Math.Max(320, root.ClientSize.Width - root.Padding.Horizontal - 24);
                int groupTextWidth = Math.Max(280, available - 36);

                intro.MaximumSize = new Size(available, 0);
                _scenarioStatus.MaximumSize = new Size(groupTextWidth, 0);
                _saveMoves.MaximumSize = new Size(available, 0);

                // MaximumSize変更後にPreferredSizeを再計算させる。
                scenarioGroup.PerformLayout();
                root.PerformLayout();
            }
            finally
            {
                applyingResponsiveWidths = false;
            }
        }

        root.ClientSizeChanged += (_, _) => ApplyResponsiveWidths();
        Shown += (_, _) => ApplyResponsiveWidths();
        ApplyResponsiveWidths();
    }

    private async Task StartBatchAsync()
    {
        if (_running) return;

        var uiBaseOptions = new HeadlessBatchOptions(
            Games: (int)_games.Value,
            P1Depth: (int)_p1Depth.Value,
            P2Depth: (int)_p2Depth.Value,
            PerMoveTimeMilliseconds: (int)_timeLimit.Value,
            PerMoveNodeLimit: decimal.ToInt64(_nodeLimit.Value),
            Parallelism: (int)_parallelism.Value,
            SearchParallelism: (int)_searchParallelism.Value,
            ProgressIntervalMilliseconds: (int)_progressIntervalSeconds.Value * 1000,
            MaxPlies: (int)_maxPlies.Value,
            OpeningRandomPlies: (int)_openingPlies.Value,
            OpeningTopK: (int)_openingTopK.Value,
            OpeningScoreWindow: (int)_openingWindow.Value,
            CycleBreakScoreWindow: 10,
            Seed: decimal.ToInt32(_seed.Value),
            SaveMoveSequences: _saveMoves.Checked,
            StartConfiguration: null);

        IReadOnlyList<HeadlessRunPlan> plans = BuildRunPlans(uiBaseOptions);
        int totalPlannedGames = plans.Sum(p => p.Options.Normalize().Games);
        var batchWallClock = Stopwatch.StartNew();
        var progressStates = new ConcurrentDictionary<int, HeadlessProgress>();
        var perConfigProgress = new IProgress<HeadlessProgress>[plans.Count];

        void RefreshProgressLabel()
        {
            int overallCompleted = progressStates.Values.Sum(p => p.CompletedGames);
            _progress.Value = Math.Clamp(overallCompleted, _progress.Minimum, _progress.Maximum);

            string[] active = progressStates
                .OrderBy(pair => pair.Key)
                .Where(pair => pair.Value.ActiveGames > 0)
                .SelectMany(pair => pair.Value.ActiveGamePlies.Count == 0
                    ? new[] { $"[{plans[pair.Key].Label}] 実行中" }
                    : pair.Value.ActiveGamePlies.Select(g => $"[{plans[pair.Key].Label}]#{g.GameIndex + 1}:{g.Ply}手"))
                .Take(8)
                .ToArray();
            string activeText = active.Length == 0 ? "進行中なし" : string.Join(", ", active);
            int activeConditions = progressStates.Count(pair => pair.Value.ActiveGames > 0);
            double elapsedSeconds = batchWallClock.Elapsed.TotalSeconds;
            double overallGps = elapsedSeconds > 0 ? overallCompleted / elapsedSeconds : 0;
            string elapsedText = FormatDuration(elapsedSeconds);
            string etaText;
            string finishText;
            if (overallCompleted > 0 && overallCompleted < totalPlannedGames && overallGps > 0)
            {
                double remainingSeconds = (totalPlannedGames - overallCompleted) / overallGps;
                etaText = FormatDuration(remainingSeconds);
                finishText = DateTime.Now.AddSeconds(remainingSeconds).ToString("HH:mm:ss");
            }
            else if (overallCompleted >= totalPlannedGames && totalPlannedGames > 0)
            {
                etaText = "00:00";
                finishText = "完了";
            }
            else
            {
                etaText = "計算中";
                finishText = "--:--:--";
            }

            _progressLabel.Text =
                $"全体 {overallCompleted:N0}/{totalPlannedGames:N0} 局 / 条件 {plans.Count}件 / 実行中 {activeConditions}件 / {overallGps:0.000} 局/秒\n" +
                $"経過 {elapsedText} / 概算残り {etaText} / 完了予想 {finishText}\n" +
                activeText;
        }

        for (int i = 0; i < plans.Count; i++)
        {
            int configIndex = i;
            perConfigProgress[i] = new Progress<HeadlessProgress>(progress =>
            {
                progressStates[configIndex] = progress;
                RefreshProgressLabel();
            });
        }

        _running = true;
        _cts = new CancellationTokenSource();
        CancellationToken cancellationToken = _cts.Token;
        _startButton.Enabled = false;
        _cancelButton.Enabled = true;
        _progress.Minimum = 0;
        _progress.Maximum = Math.Max(1, totalPlannedGames);
        _progress.Value = 0;
        _resultBox.Clear();
        SetSettingsEnabled(false);

        try
        {
            HeadlessCompletedPlan[] completedPlans = await Task.Run(() =>
            {
                var completed = new ConcurrentBag<HeadlessCompletedPlan>();
                bool suiteParallel = _loadedScenarioCases is not null &&
                                     plans.Count > 1 &&
                                     plans.All(plan => plan.Options.Games == 1 && plan.Options.PerMoveTimeMilliseconds == 0);

                if (suiteParallel)
                {
                    int caseParallelism = Math.Max(1, Math.Min(plans.Count, plans.Min(plan => plan.Options.Parallelism)));
                    Parallel.ForEach(
                        Enumerable.Range(0, plans.Count),
                        new ParallelOptions { MaxDegreeOfParallelism = caseParallelism },
                        i =>
                        {
                            if (cancellationToken.IsCancellationRequested) return;
                            HeadlessBatchResult result = HeadlessBatchRunner.Run(plans[i].Options, perConfigProgress[i], cancellationToken);
                            completed.Add(new HeadlessCompletedPlan(i, result));
                        });
                }
                else
                {
                    for (int i = 0; i < plans.Count; i++)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        HeadlessBatchResult result = HeadlessBatchRunner.Run(plans[i].Options, perConfigProgress[i], cancellationToken);
                        completed.Add(new HeadlessCompletedPlan(i, result));
                        if (result.Cancelled) break;
                    }
                }

                return completed.OrderBy(item => item.PlanIndex).ToArray();
            });

            int totalCompleted = completedPlans.Sum(item => item.Result.CompletedGames);
            _progress.Value = Math.Min(_progress.Maximum, totalCompleted);
            bool cancelled = completedPlans.Any(item => item.Result.Cancelled) || completedPlans.Length < plans.Count;
            _progressLabel.Text = cancelled ? "中止済み（完了分は保存済み）" : "完了";
            _resultBox.Text = BuildResultText(plans, completedPlans);
        }
        catch (OperationCanceledException)
        {
            _progressLabel.Text = "中止しました";
        }
        catch (Exception ex)
        {
            _progressLabel.Text = "エラー";
            _resultBox.Text = ex.ToString();
            MessageBox.Show(this, ex.ToString(), "Headless Scenario Lab error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _running = false;
            _cts?.Dispose();
            _cts = null;
            _startButton.Enabled = true;
            _cancelButton.Enabled = false;
            SetSettingsEnabled(true);
        }
    }

    private IReadOnlyList<HeadlessRunPlan> BuildRunPlans(HeadlessBatchOptions uiBaseOptions)
    {
        var plans = new List<HeadlessRunPlan>();
        if (_loadedScenarioCases is null || _loadedScenarioCases.Count == 0)
        {
            HeadlessBatchOptions options = (uiBaseOptions with { StartConfiguration = GameStartConfiguration.Initial }).Normalize();
            plans.Add(new HeadlessRunPlan("Initial / Standard", options));
            return plans;
        }

        foreach (HeadlessScenarioCase scenario in _loadedScenarioCases)
        {
            HeadlessBatchOptions options = scenario.ApplyTo(uiBaseOptions).Normalize();
            string label = $"{options.StartConfiguration?.Name ?? "Scenario"} / Standard";
            plans.Add(new HeadlessRunPlan(label, options));
        }
        return plans;
    }

    private static string BuildResultText(
        IReadOnlyList<HeadlessRunPlan> plans,
        IReadOnlyList<HeadlessCompletedPlan> completedPlans)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Scenario Lab: {completedPlans.Count}/{plans.Count} 条件完了 / Rules=Standard");
        sb.AppendLine();

        foreach (HeadlessCompletedPlan completed in completedPlans)
        {
            HeadlessBatchResult result = completed.Result;
            HeadlessRunPlan plan = plans[completed.PlanIndex];
            HeadlessBatchOptions options = plan.Options;
            GameStartConfiguration start = options.StartConfiguration ?? GameStartConfiguration.Initial;
            double p1Rate = result.CompletedGames > 0 ? result.P1Wins * 100.0 / result.CompletedGames : 0;
            double p2Rate = result.CompletedGames > 0 ? result.P2Wins * 100.0 / result.CompletedGames : 0;

            sb.AppendLine($"[{plan.Label}]");
            sb.AppendLine($"  Start {start.CurrentPlayer.ShortName()} / D{options.P1Depth} vs D{options.P2Depth} / Seed {options.Seed} / {start.Hash}");
            ScenarioMovePolicy? policy = options.MovePolicy?.IsActive == true ? options.MovePolicy : null;
            sb.AppendLine($"  Strategy P1={policy?.P1.Id ?? "Free"} / P2={policy?.P2.Id ?? "Free"}");
            sb.AppendLine($"  完了 {result.CompletedGames}/{result.RequestedGames}  P1 {result.P1Wins} ({p1Rate:0.0}%)  P2 {result.P2Wins} ({p2Rate:0.0}%)");
            sb.AppendLine($"  Goal {result.GoalReached} / Immobilized {result.RunnerImmobilized} / StrategyStop {result.StrategyConstraintNoMoves} / 千日手 {result.FourfoldRepetitions} / MoveLimit {result.MoveLimits}");
            sb.AppendLine($"  平均手数 {result.AveragePlies:0.0} / 平均Depth P1 {result.P1AverageCompletedDepth:0.00} P2 {result.P2AverageCompletedDepth:0.00}");
            string? rushAssessment = BuildRushAssessment(policy, result);
            if (rushAssessment is not null) sb.AppendLine($"  Rush判定: {rushAssessment}");
            sb.AppendLine($"  JSONL: {result.BatchLogPath}");
            sb.AppendLine();
        }

        if (completedPlans.Count > 0)
        {
            sb.AppendLine($"累積CSV: {completedPlans[^1].Result.LatestCsvPath}");
            sb.AppendLine("scenarioName/scenarioHash/startPlayer/startBoard/Strategy/K と探索設定で条件を再現できます。ルールはStandard固定です。");
        }
        return sb.ToString();
    }

    private static string? BuildRushAssessment(ScenarioMovePolicy? policy, HeadlessBatchResult result)
    {
        bool p1Rush = policy?.P1.Mode == StrategyMode.RushOne;
        bool p2Rush = policy?.P2.Mode == StrategyMode.RushOne;
        if (p1Rush == p2Rush) return null;

        int rushWins = p1Rush ? result.P1Wins : result.P2Wins;
        if (result.CompletedGames <= 0) return "判定不能";
        if (rushWins == result.CompletedGames)
        {
            return "Rush側全勝 = この探索深度では受け未発見（必勝証明ではない）";
        }
        return $"Rush非勝利 {result.CompletedGames - rushWins}局 = 受け候補あり（棋譜確認）";
    }

    private void SetSettingsEnabled(bool enabled)
    {
        foreach (Control control in new Control[]
        {
            _games, _p1Depth, _p2Depth, _timeLimit, _nodeLimit, _parallelism,
            _searchParallelism, _progressIntervalSeconds, _maxPlies,
            _openingPlies, _openingTopK, _openingWindow, _seed, _saveMoves,
            _scenarioBrowseButton, _scenarioClearButton, _evaluationTunerButton
        })
        {
            control.Enabled = enabled;
        }
    }

    private void LoadScenarioFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Headless Scenario JSONを選択",
            Filter = "Scenario JSON (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            IReadOnlyList<HeadlessScenarioCase> cases = HeadlessScenarioFile.Load(dialog.FileName);
            _loadedScenarioCases = cases;
            _scenarioPath.Text = dialog.FileName;
            ApplyUniformScenarioSettingsToUi(cases);
            VerifyUniformScenarioSettingsReflected(cases);

            GameStartConfiguration first = cases[0].StartConfiguration!;
            ScenarioMovePolicy? firstPolicy = cases[0].MovePolicy;
            string settingSummary = BuildScenarioSettingSummary(cases);
            string uiSummary = BuildScenarioUiReflectionSummary(cases);
            _scenarioStatus.Text = cases.Count == 1
                ? $"1ケース: {first.Name} / {first.CurrentPlayer.ShortName()} / {firstPolicy?.P1.Id ?? "Free"} vs {firstPolicy?.P2.Id ?? "Free"} / {settingSummary} / UI反映: {uiSummary} / {first.Hash}"
                : $"Suite {cases.Count}ケース / 先頭: {first.Name} / {settingSummary} / UI反映: {uiSummary} / {first.Hash}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Scenario JSONを読み込めません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ApplyUniformScenarioSettingsToUi(IReadOnlyList<HeadlessScenarioCase> cases)
    {
        ApplyUniformInt(cases.Select(c => c.Settings?.Games), _games);
        ApplyUniformInt(cases.Select(c => c.Settings?.P1Depth), _p1Depth);
        ApplyUniformInt(cases.Select(c => c.Settings?.P2Depth), _p2Depth);
        ApplyUniformInt(cases.Select(c => c.Settings?.PerMoveTimeMilliseconds), _timeLimit);
        ApplyUniformLong(cases.Select(c => c.Settings?.PerMoveNodeLimit), _nodeLimit);
        ApplyUniformInt(cases.Select(c => c.Settings?.Parallelism), _parallelism);
        ApplyUniformInt(cases.Select(c => c.Settings?.SearchParallelism), _searchParallelism);
        ApplyUniformInt(cases.Select(c => c.Settings?.MaxPlies), _maxPlies);
        ApplyUniformInt(cases.Select(c => c.Settings?.OpeningRandomPlies), _openingPlies);
        ApplyUniformInt(cases.Select(c => c.Settings?.OpeningTopK), _openingTopK);
        ApplyUniformInt(cases.Select(c => c.Settings?.OpeningScoreWindow), _openingWindow);
        ApplyUniformInt(cases.Select(c => c.Settings?.Seed), _seed);

        int? progressMs = GetUniformExplicit(cases.Select(c => c.Settings?.ProgressIntervalMilliseconds));
        if (progressMs.HasValue && progressMs.Value >= 1000 && progressMs.Value % 1000 == 0)
        {
            SetNumericValue(_progressIntervalSeconds, progressMs.Value / 1000m);
        }

        bool? saveMoves = GetUniformExplicit(cases.Select(c => c.Settings?.SaveMoveSequences));
        if (saveMoves.HasValue)
        {
            _saveMoves.Checked = saveMoves.Value;
        }
    }

    private void VerifyUniformScenarioSettingsReflected(IReadOnlyList<HeadlessScenarioCase> cases)
    {
        VerifyNumericReflection(cases.Select(c => c.Settings?.Games), _games, nameof(ScenarioBatchSettings.Games));
        VerifyNumericReflection(cases.Select(c => c.Settings?.P1Depth), _p1Depth, nameof(ScenarioBatchSettings.P1Depth));
        VerifyNumericReflection(cases.Select(c => c.Settings?.P2Depth), _p2Depth, nameof(ScenarioBatchSettings.P2Depth));
        VerifyNumericReflection(cases.Select(c => c.Settings?.PerMoveTimeMilliseconds), _timeLimit, nameof(ScenarioBatchSettings.PerMoveTimeMilliseconds));
        VerifyNumericReflection(cases.Select(c => c.Settings?.Parallelism), _parallelism, nameof(ScenarioBatchSettings.Parallelism));
        VerifyNumericReflection(cases.Select(c => c.Settings?.SearchParallelism), _searchParallelism, nameof(ScenarioBatchSettings.SearchParallelism));
        VerifyNumericReflection(cases.Select(c => c.Settings?.MaxPlies), _maxPlies, nameof(ScenarioBatchSettings.MaxPlies));
        VerifyNumericReflection(cases.Select(c => c.Settings?.OpeningRandomPlies), _openingPlies, nameof(ScenarioBatchSettings.OpeningRandomPlies));
        VerifyNumericReflection(cases.Select(c => c.Settings?.OpeningTopK), _openingTopK, nameof(ScenarioBatchSettings.OpeningTopK));
        VerifyNumericReflection(cases.Select(c => c.Settings?.OpeningScoreWindow), _openingWindow, nameof(ScenarioBatchSettings.OpeningScoreWindow));
        VerifyNumericReflection(cases.Select(c => c.Settings?.Seed), _seed, nameof(ScenarioBatchSettings.Seed));
        VerifyNumericReflection(cases.Select(c => c.Settings?.PerMoveNodeLimit), _nodeLimit, nameof(ScenarioBatchSettings.PerMoveNodeLimit));

        bool? saveMoves = GetUniformExplicit(cases.Select(c => c.Settings?.SaveMoveSequences));
        if (saveMoves.HasValue && _saveMoves.Checked != saveMoves.Value)
        {
            throw new InvalidOperationException($"ScenarioのSaveMoveSequences={saveMoves.Value}をGUIへ反映できませんでした。");
        }
    }

    private static void VerifyNumericReflection<T>(IEnumerable<T?> values, NumericUpDown control, string settingName)
        where T : struct, IEquatable<T>, IConvertible
    {
        T? value = GetUniformExplicit(values);
        if (!value.HasValue) return;

        decimal expected = Convert.ToDecimal(value.Value);
        expected = Math.Min(control.Maximum, Math.Max(control.Minimum, expected));
        if (control.Value != expected)
        {
            throw new InvalidOperationException($"Scenarioの{settingName}={value.Value}をGUIへ反映できませんでした（GUI={control.Value}）。");
        }
    }

    private string BuildScenarioUiReflectionSummary(IReadOnlyList<HeadlessScenarioCase> cases)
    {
        var parts = new List<string>();
        if (GetUniformExplicit(cases.Select(c => c.Settings?.Games)).HasValue) parts.Add($"{_games.Value:0}局");
        if (GetUniformExplicit(cases.Select(c => c.Settings?.P1Depth)).HasValue &&
            GetUniformExplicit(cases.Select(c => c.Settings?.P2Depth)).HasValue)
        {
            parts.Add($"D{_p1Depth.Value:0} vs D{_p2Depth.Value:0}");
        }
        if (GetUniformExplicit(cases.Select(c => c.Settings?.MaxPlies)).HasValue) parts.Add($"Max{_maxPlies.Value:0}ply");
        if (GetUniformExplicit(cases.Select(c => c.Settings?.OpeningRandomPlies)).HasValue) parts.Add($"序盤{_openingPlies.Value:0}ply");
        if (GetUniformExplicit(cases.Select(c => c.Settings?.OpeningTopK)).HasValue) parts.Add($"TopK={_openingTopK.Value:0}");
        if (GetUniformExplicit(cases.Select(c => c.Settings?.Seed)).HasValue) parts.Add($"Seed={_seed.Value:0}");
        if (GetUniformExplicit(cases.Select(c => c.Settings?.SaveMoveSequences)).HasValue) parts.Add($"全着手={(_saveMoves.Checked ? "ON" : "OFF")}");

        return parts.Count == 0 ? "共通明示設定なし（GUI値を使用）" : string.Join(", ", parts);
    }

    private static string BuildScenarioSettingSummary(IReadOnlyList<HeadlessScenarioCase> cases)
    {
        int? p1Depth = GetUniformExplicit(cases.Select(c => c.Settings?.P1Depth));
        int? p2Depth = GetUniformExplicit(cases.Select(c => c.Settings?.P2Depth));
        string depthText;
        if (p1Depth.HasValue && p2Depth.HasValue)
        {
            depthText = $"D{p1Depth.Value} vs D{p2Depth.Value}";
        }
        else
        {
            int[] p1Values = cases.Select(c => c.Settings?.P1Depth).Where(v => v.HasValue).Select(v => v!.Value).Distinct().OrderBy(v => v).ToArray();
            int[] p2Values = cases.Select(c => c.Settings?.P2Depth).Where(v => v.HasValue).Select(v => v!.Value).Distinct().OrderBy(v => v).ToArray();
            string p1 = p1Values.Length == 0 ? "GUI" : string.Join("/", p1Values.Select(v => $"D{v}"));
            string p2 = p2Values.Length == 0 ? "GUI" : string.Join("/", p2Values.Select(v => $"D{v}"));
            depthText = $"Depthケース別 P1={p1} P2={p2}";
        }

        int? games = GetUniformExplicit(cases.Select(c => c.Settings?.Games));
        string gamesText = games.HasValue ? $"{games.Value}局/条件" : "局数GUI/ケース別";

        return $"{depthText} / {gamesText} / Rules=Standard";
    }

    private static void ApplyUniformInt(IEnumerable<int?> values, NumericUpDown control)
    {
        int? value = GetUniformExplicit(values);
        if (value.HasValue) SetNumericValue(control, value.Value);
    }

    private static void ApplyUniformLong(IEnumerable<long?> values, NumericUpDown control)
    {
        long? value = GetUniformExplicit(values);
        if (value.HasValue) SetNumericValue(control, value.Value);
    }

    private static void SetNumericValue(NumericUpDown control, decimal value)
    {
        control.Value = Math.Min(control.Maximum, Math.Max(control.Minimum, value));
    }

    private static T? GetUniformExplicit<T>(IEnumerable<T?> values) where T : struct, IEquatable<T>
    {
        T?[] items = values.ToArray();
        if (items.Length == 0 || items.Any(v => !v.HasValue)) return null;
        T first = items[0]!.Value;
        return items.All(v => v!.Value.Equals(first)) ? first : null;
    }

    private void ClearScenarioFile()
    {
        _loadedScenarioCases = null;
        _scenarioPath.Clear();
        _scenarioStatus.Text = "通常初期盤面 / Strategy: Free vs Free";
    }

    private void OpenEvaluationTuner()
    {
        using var dialog = new EvaluationTunerForm();
        dialog.ShowDialog(this);
    }

    private void OpenAnalysisFolder()
    {
        try
        {
            string directory = HeadlessBatchRunner.ResolveAnalysisDirectory();
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "解析ログフォルダを開けません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            return "--:--";
        }

        TimeSpan value = TimeSpan.FromSeconds(seconds);
        if (value.TotalHours >= 24)
        {
            return $"{(int)value.TotalDays}日 {value.Hours:00}:{value.Minutes:00}:{value.Seconds:00}";
        }
        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
        }
        return $"{value.Minutes:00}:{value.Seconds:00}";
    }

    private static void AddSetting(
        TableLayoutPanel panel,
        int row,
        string leftLabel,
        Control leftControl,
        string rightLabel,
        Control rightControl)
    {
        panel.Controls.Add(MakeLabel(leftLabel), 0, row);
        panel.Controls.Add(leftControl, 1, row);
        panel.Controls.Add(MakeLabel(rightLabel), 2, row);
        panel.Controls.Add(rightControl, 3, row);
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 7, 3, 3)
    };

    private static void ConfigureNumeric(NumericUpDown control, decimal min, decimal max, decimal value, decimal increment)
    {
        control.Minimum = min;
        control.Maximum = max;
        control.Value = Math.Clamp(value, min, max);
        control.Increment = increment;
        control.Width = 120;
        control.TextAlign = HorizontalAlignment.Right;
        control.ThousandsSeparator = true;
    }

    private static void ConfigureButton(Button button, string text, int width)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 34;
        button.Margin = new Padding(3);
    }

    private sealed record HeadlessRunPlan(string Label, HeadlessBatchOptions Options);
    private sealed record HeadlessCompletedPlan(int PlanIndex, HeadlessBatchResult Result);

}
