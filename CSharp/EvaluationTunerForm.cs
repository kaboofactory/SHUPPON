using System.Diagnostics;
using System.Text;

namespace StarRunnerPrototype;

public sealed class EvaluationTunerForm : Form
{
    private readonly NumericUpDown _generations = new();
    private readonly NumericUpDown _candidates = new();
    private readonly NumericUpDown _shallowDepth = new();
    private readonly NumericUpDown _shallowGames = new();
    private readonly NumericUpDown _validationDepth = new();
    private readonly NumericUpDown _validationGames = new();
    private readonly NumericUpDown _parallelism = new();
    private readonly NumericUpDown _maxPlies = new();
    private readonly NumericUpDown _openingPlies = new();
    private readonly NumericUpDown _mutationStep = new();
    private readonly NumericUpDown _seed = new();
    private readonly Label _currentProfileLabel = new();
    private readonly Label _initialProfileTitle = new();
    private readonly Label _bestProfileTitle = new();
    private readonly TextBox _initialProfileBox = new();
    private readonly TextBox _bestProfileBox = new();
    private readonly Label _dashboardLabel = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _progressLabel = new();
    private readonly TextBox _resultBox = new();
    private readonly Button _startButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _adoptButton = new();
    private readonly Button _resetButton = new();
    private readonly Button _openReportButton = new();
    private readonly ComboBox _scanFeature = new();
    private readonly ComboBox _scanPhase = new();
    private readonly NumericUpDown _scanMin = new();
    private readonly NumericUpDown _scanMax = new();
    private readonly NumericUpDown _scanStep = new();
    private readonly NumericUpDown _scanDepth = new();
    private readonly NumericUpDown _scanGames = new();
    private readonly Button _scanButton = new();
    private CancellationTokenSource? _cts;
    private EvaluationTuningResult? _lastResult;
    private CpuEvaluationProfile? _lastAdoptableProfile;
    private string? _lastReportPath;
    private bool _running;
    private readonly Stopwatch _sessionStopwatch = new();
    private readonly System.Windows.Forms.Timer _dashboardTimer = new() { Interval = 1000 };
    private long _liveTotalGames;
    private int _liveGeneration;
    private int _liveBestUpdates;
    private int _liveLastBestGeneration;
    private int _liveMutationStep;
    private int _liveStagnation;
    private CpuEvaluationProfile? _liveStartingProfile;
    private CpuEvaluationProfile? _liveBestProfile;

    public EvaluationTunerForm()
    {
        Text = "評価関数チューナー - 自動調整 / 1パラメータ・スキャン";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 820);
        ClientSize = new Size(1080, 1000);
        Font = new Font("Yu Gothic UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        BuildUi();
        _dashboardTimer.Tick += (_, _) => UpdateDashboardDisplay();
        RefreshCurrentProfileDisplay();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running)
        {
            _cts?.Cancel();
        }
        _dashboardTimer.Stop();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 10,
            Padding = new Padding(14),
            AutoScroll = true
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // Keep enough vertical room for the parameter-scan controls when the flow wraps to two lines.
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 225));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var intro = new Label
        {
            AutoSize = true,
            Text = "全17特徴量×序盤/終盤=34重みを、現在の標準評価Profileから自動調整します。\n" +
                   "通常候補は1～3項目だけ変更し、6候補ごとに1回だけ5～8項目の大変異を入れます。終了世代=0なら停止まで無期限です。\n" +
                   "1パラメータ・スキャンでは他33重みを固定し、指定した1重みだけを範囲走査して同一seed・先後均等で現Profileと比較します。",
            Margin = new Padding(0, 0, 0, 10)
        };
        root.Controls.Add(intro, 0, 0);

        _currentProfileLabel.AutoSize = true;
        _currentProfileLabel.Font = new Font(Font, FontStyle.Bold);
        _currentProfileLabel.Margin = new Padding(0, 0, 0, 8);
        root.Controls.Add(_currentProfileLabel, 0, 1);

        var settings = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 4,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 10)
        };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        root.Controls.Add(settings, 0, 2);

        ConfigureNumeric(_generations, 0, 100000, 0, 1);
        ConfigureNumeric(_candidates, 2, 32, 6, 1);
        ConfigureNumeric(_shallowDepth, 2, 8, 5, 1);
        ConfigureNumeric(_shallowGames, 4, 1000, 24, 4);
        ConfigureNumeric(_validationDepth, 5, 10, 7, 1);
        ConfigureNumeric(_validationGames, 0, 500, 12, 4);
        ConfigureNumeric(_parallelism, 1, Math.Max(1, Environment.ProcessorCount * 2), Math.Min(8, Math.Max(1, Environment.ProcessorCount * 2)), 1);
        ConfigureNumeric(_maxPlies, 40, 1000, 250, 10);
        ConfigureNumeric(_openingPlies, 0, 20, 4, 1);
        ConfigureNumeric(_mutationStep, 20, 600, 180, 10);
        ConfigureNumeric(_seed, int.MinValue, int.MaxValue, 24681357, 1);

        AddSetting(settings, 0, "終了世代 (0=停止まで)", _generations, "候補数 / 世代", _candidates);
        AddSetting(settings, 1, "高速探索Depth", _shallowDepth, "浅い最大局数 / 最終候補", _shallowGames);
        AddSetting(settings, 2, "検証Depth", _validationDepth, "深い側 対局数", _validationGames);
        AddSetting(settings, 3, "同時対局数", _parallelism, "最大手数", _maxPlies);
        AddSetting(settings, 4, "ランダム序盤手数", _openingPlies, "初期変異幅(‰)", _mutationStep);
        AddSetting(settings, 5, "Seed", _seed, "", new Label { AutoSize = true });

        var scanGroup = new GroupBox
        {
            Text = "1パラメータ・スキャン（他31重み固定 / 各値を現在Profileと直接比較）",
            AutoSize = false,
            Height = 120,
            MinimumSize = new Size(0, 120),
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 8, 10, 8),
            Margin = new Padding(0, 0, 0, 10)
        };
        var scanFlow = new FlowLayoutPanel
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 2, 0, 0)
        };
        scanGroup.Controls.Add(scanFlow);

        _scanFeature.DropDownStyle = ComboBoxStyle.DropDownList;
        _scanFeature.Width = 210;
        foreach (string feature in EvaluationTuner.TunableFeatureNames) _scanFeature.Items.Add(feature);
        _scanFeature.SelectedIndex = Math.Max(0, _scanFeature.Items.IndexOf("RunnerGoalPath"));

        _scanPhase.DropDownStyle = ComboBoxStyle.DropDownList;
        _scanPhase.Width = 95;
        _scanPhase.Items.AddRange(new object[] { "Opening", "Endgame" });
        _scanPhase.SelectedIndex = 1;

        ConfigureNumeric(_scanMin, 0, 3000, 600, 50);
        ConfigureNumeric(_scanMax, 0, 3000, 1400, 50);
        ConfigureNumeric(_scanStep, 10, 3000, 100, 10);
        ConfigureNumeric(_scanDepth, 2, 10, 5, 1);
        ConfigureNumeric(_scanGames, 4, 2000, 24, 4);
        _scanMin.Width = _scanMax.Width = _scanStep.Width = 85;
        _scanDepth.Width = 65;
        _scanGames.Width = 80;
        ConfigureButton(_scanButton, "1パラメータ・スキャン開始", 190);

        AddFlowSetting(scanFlow, "対象", _scanFeature);
        AddFlowSetting(scanFlow, "Phase", _scanPhase);
        AddFlowSetting(scanFlow, "最小‰", _scanMin);
        AddFlowSetting(scanFlow, "最大‰", _scanMax);
        AddFlowSetting(scanFlow, "刻み‰", _scanStep);
        AddFlowSetting(scanFlow, "Depth", _scanDepth);
        AddFlowSetting(scanFlow, "局/値", _scanGames);
        scanFlow.Controls.Add(_scanButton);
        root.Controls.Add(scanGroup, 0, 3);

        var profileCompare = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 6)
        };
        profileCompare.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        profileCompare.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        profileCompare.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        profileCompare.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _initialProfileTitle.AutoSize = true;
        _initialProfileTitle.Font = new Font(Font, FontStyle.Bold);
        _initialProfileTitle.Text = "開始時パラメータ";
        _initialProfileTitle.Margin = new Padding(0, 0, 8, 4);
        profileCompare.Controls.Add(_initialProfileTitle, 0, 0);

        _bestProfileTitle.AutoSize = true;
        _bestProfileTitle.Font = new Font(Font, FontStyle.Bold);
        _bestProfileTitle.Text = "現在の最善パラメータ";
        _bestProfileTitle.Margin = new Padding(8, 0, 0, 4);
        profileCompare.Controls.Add(_bestProfileTitle, 1, 0);

        ConfigureProfileBox(_initialProfileBox);
        _initialProfileBox.Margin = new Padding(0, 0, 6, 0);
        profileCompare.Controls.Add(_initialProfileBox, 0, 1);

        ConfigureProfileBox(_bestProfileBox);
        _bestProfileBox.Margin = new Padding(6, 0, 0, 0);
        profileCompare.Controls.Add(_bestProfileBox, 1, 1);
        root.Controls.Add(profileCompare, 0, 4);

        _dashboardLabel.AutoSize = true;
        _dashboardLabel.Dock = DockStyle.Top;
        _dashboardLabel.Font = new Font("Consolas", 9f);
        _dashboardLabel.BorderStyle = BorderStyle.FixedSingle;
        _dashboardLabel.Padding = new Padding(6, 5, 6, 5);
        _dashboardLabel.Margin = new Padding(0, 2, 0, 8);
        _dashboardLabel.Text = "待機中";
        root.Controls.Add(_dashboardLabel, 0, 5);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 8)
        };
        ConfigureButton(_startButton, "自動調整開始", 120);
        ConfigureButton(_cancelButton, "停止（最良を保持）", 140);
        ConfigureButton(_adoptButton, "結果を採用", 120);
        ConfigureButton(_resetButton, "組込み既定へ戻す", 140);
        ConfigureButton(_openReportButton, "結果JSONを開く", 130);
        _cancelButton.Enabled = false;
        _adoptButton.Enabled = false;
        _openReportButton.Enabled = false;
        buttons.Controls.AddRange(new Control[] { _startButton, _cancelButton, _adoptButton, _resetButton, _openReportButton });
        root.Controls.Add(buttons, 0, 6);

        _progress.Dock = DockStyle.Top;
        _progress.Minimum = 0;
        _progress.Maximum = 1000;
        _progress.Height = 24;
        root.Controls.Add(_progress, 0, 7);

        _progressLabel.AutoSize = true;
        _progressLabel.Text = "待機中";
        _progressLabel.Margin = new Padding(0, 6, 0, 6);
        root.Controls.Add(_progressLabel, 0, 8);

        _resultBox.Dock = DockStyle.Fill;
        _resultBox.Multiline = true;
        _resultBox.ReadOnly = true;
        _resultBox.ScrollBars = ScrollBars.Both;
        _resultBox.WordWrap = false;
        _resultBox.Font = new Font("Consolas", 9f);
        root.Controls.Add(_resultBox, 0, 9);

        _startButton.Click += async (_, _) => await StartAsync();
        _scanButton.Click += async (_, _) => await StartParameterScanAsync();
        _cancelButton.Click += (_, _) =>
        {
            _cancelButton.Enabled = false;
            _progressLabel.Text = "停止要求を送信しました。現在の対局を安全に打ち切っています…";
            _cts?.Cancel();
        };
        _adoptButton.Click += (_, _) => AdoptLastResult();
        _resetButton.Click += (_, _) => ResetProfile();
        _openReportButton.Click += (_, _) => OpenReport();
        _shallowDepth.ValueChanged += (_, _) =>
        {
            if (_validationDepth.Value < _shallowDepth.Value)
            {
                _validationDepth.Value = _shallowDepth.Value;
            }
        };
    }

    private async Task StartAsync()
    {
        if (_running) return;
        _running = true;
        _lastResult = null;
        _lastAdoptableProfile = null;
        _lastReportPath = null;
        _openReportButton.Enabled = false;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        CpuEvaluationProfile starting = CpuEvaluationProfileProvider.Current;
        EvaluationTuningOptions options = ReadOptions();

        _liveStartingProfile = starting;
        _liveBestProfile = starting;
        _liveTotalGames = 0;
        _liveGeneration = 0;
        _liveBestUpdates = 0;
        _liveLastBestGeneration = 0;
        _liveMutationStep = options.InitialMutationStep;
        _liveStagnation = 0;
        _sessionStopwatch.Restart();
        _dashboardTimer.Start();
        RefreshProfileComparison(starting, starting);
        UpdateDashboardDisplay();

        SetRunningState(true);
        _resultBox.Clear();
        _resultBox.AppendText($"開始 profile={starting.Name} / D{options.ShallowDepth}→D{options.ValidationDepth}\r\n");
        _resultBox.AppendText(options.Generations == 0
            ? $"停止されるまで継続 × {options.CandidatesPerGeneration}候補/世代 / shallow winner最大{options.ShallowGamesPerCandidate}局（Successive Halving）\r\n\r\n"
            : $"{options.Generations}世代 × {options.CandidatesPerGeneration}候補 / shallow winner最大{options.ShallowGamesPerCandidate}局（Successive Halving）\r\n\r\n");

        if (options.Generations == 0)
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 25;
        }
        else
        {
            _progress.Style = ProgressBarStyle.Blocks;
            _progress.Value = 0;
        }

        var progress = new Progress<EvaluationTuningProgress>(p =>
        {
            string totalText = p.TotalGenerations == 0 ? "∞" : p.TotalGenerations.ToString();
            if (p.TotalGenerations > 0)
            {
                int done = (p.Generation - 1) * options.CandidatesPerGeneration + Math.Min(p.Candidate, options.CandidatesPerGeneration);
                int total = p.TotalGenerations * options.CandidatesPerGeneration;
                _progress.Value = Math.Clamp(total > 0 ? done * 1000 / total : 0, 0, 1000);
            }
            _progressLabel.Text = $"G{p.Generation}/{totalText} C{p.Candidate}/{p.CandidatesPerGeneration} / {p.Stage} / {p.Message}";
            _liveGeneration = p.Generation;
            _liveTotalGames = p.TotalGamesCompleted;
            _liveBestUpdates = p.BestUpdateCount;
            _liveLastBestGeneration = p.LastBestGeneration;
            _liveMutationStep = p.MutationStep;
            _liveStagnation = p.Stagnation;
            if (_liveBestProfile is null || !Equals(_liveBestProfile, p.CurrentBest))
            {
                _liveBestProfile = p.CurrentBest;
                RefreshProfileComparison(_liveStartingProfile ?? starting, p.CurrentBest);
            }
            UpdateDashboardDisplay();
            if (p.Stage is "ベスト更新" or "据え置き")
            {
                _resultBox.AppendText($"G{p.Generation:0000}: {p.Stage}  score={p.CandidateScore:P1}  best={p.CurrentBest.Name}\r\n");
                _resultBox.SelectionStart = _resultBox.TextLength;
                _resultBox.ScrollToCaret();
            }
        });

        try
        {
            EvaluationTuningResult result = await Task.Run(() =>
                EvaluationTuner.Run(starting, options, progress, token));
            _lastResult = result;
            ShowResult(result);
        }
        catch (Exception ex)
        {
            _resultBox.AppendText($"\r\nERROR: {ex}\r\n");
            MessageBox.Show(this, ex.ToString(), "評価関数チューナー error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _running = false;
            _sessionStopwatch.Stop();
            _dashboardTimer.Stop();
            UpdateDashboardDisplay();
            _progress.MarqueeAnimationSpeed = 0;
            _progress.Style = ProgressBarStyle.Blocks;
            SetRunningState(false);
        }
    }

    private async Task StartParameterScanAsync()
    {
        if (_running) return;
        _running = true;
        _lastResult = null;
        _lastAdoptableProfile = null;
        _lastReportPath = null;
        _openReportButton.Enabled = false;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        CpuEvaluationProfile baseline = CpuEvaluationProfileProvider.Current;
        EvaluationParameterScanOptions options = ReadScanOptions();

        _liveStartingProfile = baseline;
        _liveBestProfile = baseline;
        _liveTotalGames = 0;
        _liveGeneration = 0;
        _liveBestUpdates = 0;
        _liveLastBestGeneration = 0;
        _liveMutationStep = 0;
        _liveStagnation = 0;
        _sessionStopwatch.Restart();
        _dashboardTimer.Start();
        RefreshProfileComparison(baseline, baseline);
        UpdateDashboardDisplay();
        SetRunningState(true);
        _resultBox.Clear();

        string feature = EvaluationTuner.TunableFeatureNames[options.FeatureIndex];
        string phase = options.Endgame ? "Endgame" : "Opening";
        _resultBox.AppendText($"1パラメータ・スキャン開始: {phase}.{feature} {options.MinValue}～{options.MaxValue}‰ / step {options.Step}‰ / D{options.Depth} / {options.GamesPerValue}局/値\r\n");
        _resultBox.AppendText($"Baseline: {baseline.Name} / 全候補で同一seed・先後均等\r\n\r\n");
        _progress.Style = ProgressBarStyle.Blocks;
        _progress.Value = 0;

        var progress = new Progress<EvaluationParameterScanProgress>(p =>
        {
            _progress.Value = Math.Clamp(p.TotalValues > 0 ? p.CompletedValues * 1000 / p.TotalValues : 0, 0, 1000);
            _progressLabel.Text = $"SCAN {p.CompletedValues}/{p.TotalValues} / {p.Message}";
            _liveTotalGames = p.TotalGamesCompleted;
            UpdateDashboardDisplay();
            if (p.Score is not null)
            {
                _resultBox.AppendText($"{p.CurrentValue,4}‰  score={p.Score:P1}\r\n");
                _resultBox.SelectionStart = _resultBox.TextLength;
                _resultBox.ScrollToCaret();
            }
        });

        try
        {
            EvaluationParameterScanResult result = await Task.Run(() =>
                EvaluationTuner.RunParameterScan(baseline, options, progress, token));
            ShowParameterScanResult(result);
        }
        catch (OperationCanceledException)
        {
            _progressLabel.Text = "スキャンを停止しました（完了した値がないため採用候補はありません）。";
            _resultBox.AppendText("\r\n--- SCAN CANCELLED ---\r\n");
        }
        catch (Exception ex)
        {
            _resultBox.AppendText($"\r\nERROR: {ex}\r\n");
            MessageBox.Show(this, ex.ToString(), "1パラメータ・スキャン error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _running = false;
            _sessionStopwatch.Stop();
            _dashboardTimer.Stop();
            UpdateDashboardDisplay();
            _progress.MarqueeAnimationSpeed = 0;
            _progress.Style = ProgressBarStyle.Blocks;
            SetRunningState(false);
        }
    }

    private void ShowParameterScanResult(EvaluationParameterScanResult result)
    {
        if (!result.Cancelled) _progress.Value = 1000;
        _progressLabel.Text = result.Cancelled
            ? "スキャンを停止しました。完了済みの値だけで暫定最良を表示しています。"
            : "1パラメータ・スキャン完了。結果を確認し、必要なら『結果を採用』してください。";

        _lastAdoptableProfile = result.BestEntry.Profile;
        _lastReportPath = result.ReportPath;
        _liveStartingProfile = result.BaselineProfile;
        _liveBestProfile = result.BestEntry.Profile;
        _liveTotalGames = result.TotalGamesCompleted;
        RefreshProfileComparison(result.BaselineProfile, result.BestEntry.Profile);
        UpdateDashboardDisplay();

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine(result.Cancelled ? "--- SCAN STOPPED ---" : "--- SCAN COMPLETED ---");
        sb.AppendLine($"Target: {(result.Endgame ? "Endgame" : "Opening")}.{result.FeatureName}");
        sb.AppendLine($"Baseline: {result.BaselineProfile.Name} / {result.BaselineValue}‰");
        sb.AppendLine($"Best: {result.BestEntry.Value}‰ / score={result.BestEntry.Score.Score:P1} ({result.BestEntry.Score.Wins}-{result.BestEntry.Score.Losses}-{result.BestEntry.Score.DrawsOrLimits})");
        sb.AppendLine($"D{result.Options.Depth} / {result.Options.GamesPerValue}局/値 / 総対局 {result.TotalGamesCompleted:N0} / same seeds");
        if (!string.IsNullOrWhiteSpace(result.ReportPath)) sb.AppendLine($"Report: {result.ReportPath}");
        sb.AppendLine();
        sb.AppendLine(" value      score        W-L-D");
        sb.AppendLine("--------------------------------");
        foreach (EvaluationParameterScanEntry entry in result.Entries.OrderBy(e => e.Value))
        {
            string mark = entry.Value == result.BestEntry.Value ? "  < BEST" : string.Empty;
            string current = entry.Value == result.BaselineValue ? "  < CURRENT" : string.Empty;
            sb.AppendLine($"{entry.Value,5}‰    {entry.Score.Score,7:P1}    {entry.Score.Wins,3}-{entry.Score.Losses,3}-{entry.Score.DrawsOrLimits,3}{mark}{current}");
        }
        sb.AppendLine();
        sb.AppendLine(FormatProfile(result.BestEntry.Profile));
        _resultBox.AppendText(sb.ToString());
        _adoptButton.Enabled = true;
        _openReportButton.Enabled = !string.IsNullOrWhiteSpace(result.ReportPath) && File.Exists(result.ReportPath);
    }

    private void ShowResult(EvaluationTuningResult result)
    {
        _lastAdoptableProfile = result.BestProfile;
        _lastReportPath = result.ReportPath;
        if (!result.Cancelled) _progress.Value = 1000;
        _progressLabel.Text = result.Cancelled
            ? "停止しました。停止時点までに昇格確定した最良チャンピオンを保持しています。"
            : "指定世代まで完了。結果を確認し、必要なら『最良結果を採用』してください。";

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine(result.Cancelled ? "--- STOPPED ---" : "--- COMPLETED ---");
        sb.AppendLine($"開始: {result.StartingProfile.Name}");
        sb.AppendLine($"最良: {result.BestProfile.Name}");
        sb.AppendLine($"経過: {result.ElapsedMilliseconds:N0} ms");
        RefreshProfileComparison(result.StartingProfile, result.BestProfile);
        _liveStartingProfile = result.StartingProfile;
        _liveBestProfile = result.BestProfile;
        _liveGeneration = result.Generations.Count > 0 ? result.Generations[^1].Generation : 0;
        _liveBestUpdates = result.BestUpdateCount;
        _liveLastBestGeneration = result.LastBestGeneration;
        _liveStagnation = result.Stagnation;
        _liveMutationStep = result.Generations.Count > 0 ? result.Generations[^1].MutationStep : _liveMutationStep;
        _liveTotalGames = result.TotalGamesCompleted;
        UpdateDashboardDisplay();
        if (!string.IsNullOrWhiteSpace(result.ReportPath)) sb.AppendLine($"Report: {result.ReportPath}");
        sb.AppendLine();
        foreach (EvaluationTuningGenerationResult g in result.Generations)
        {
            string validation = FormatMatchScore(g.ValidationScore);
            string confirmation = FormatMatchScore(g.ConfirmationScore);
            string mutationKind = g.LargeMutation ? "BIG" : "LOCAL";
            string changed = string.Join(", ", g.ChangedParameters);
            string halving = FormatHalving(g.ShallowRounds);
            sb.AppendLine($"G{g.Generation:0000} step={g.MutationStep,3} {mutationKind,-5} changed={g.ChangedParameters.Count} shallow={g.ShallowScore.Score:P1} {halving} validation={validation} confirm={confirmation} combined={g.CombinedScore:P1} {(g.Accepted ? "ACCEPT" : "KEEP")}");
            sb.AppendLine($"       {changed}");
        }
        sb.AppendLine();
        sb.AppendLine(FormatProfile(result.BestProfile));
        _resultBox.AppendText(sb.ToString());

        _adoptButton.Enabled = true;
        _openReportButton.Enabled = !string.IsNullOrWhiteSpace(result.ReportPath) && File.Exists(result.ReportPath);
    }

    private static string FormatMatchScore(EvaluationMatchScore? score)
    {
        if (score is null) return "-";
        string early = score.StoppedEarly
            ? $" EARLY {score.Games}/{score.PlannedGames}"
            : string.Empty;
        return $"{score.Score:P1} ({score.Wins}-{score.Losses}-{score.DrawsOrLimits}){early}";
    }

    private static string FormatHalving(IReadOnlyList<EvaluationShallowRoundResult> rounds)
    {
        if (rounds.Count == 0) return "halving=-";
        var counts = rounds.Select(r => r.ActiveCandidates).ToList();
        counts.Add(rounds[^1].AdvancingCandidates);
        string path = string.Join("→", counts);
        string targets = string.Join("/", rounds.Select(r => r.TargetGamesPerCandidate));
        return $"halving={path}@{targets}";
    }

    private void AdoptLastResult()
    {
        if (_lastAdoptableProfile is null) return;
        try
        {
            CpuEvaluationProfile adopted = _lastAdoptableProfile with { Name = $"Tuned-{DateTime.Now:yyyyMMdd-HHmmss}" };
            CpuEvaluationProfileStorage.SaveOverride(adopted);
            RefreshCurrentProfileDisplay();
            MessageBox.Show(
                this,
                $"評価関数を採用しました。\n次のCPU思考から使用されます。\n\n{CpuEvaluationProfileStorage.CurrentSource}",
                "評価関数を採用",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "評価関数を保存できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ResetProfile()
    {
        if (_running) return;
        bool reset = CpuEvaluationProfileStorage.DeleteOverride();
        _lastResult = null;
        _lastAdoptableProfile = null;
        _lastReportPath = null;
        RefreshCurrentProfileDisplay();
        _sessionStopwatch.Reset();
        _liveTotalGames = 0;
        _liveGeneration = 0;
        _liveBestUpdates = 0;
        _liveLastBestGeneration = 0;
        _liveMutationStep = 0;
        _liveStagnation = 0;
        UpdateDashboardDisplay();
        _adoptButton.Enabled = false;
        MessageBox.Show(
            this,
            reset ? $"組込み既定の {CpuEvaluationProfile.BuiltInDefault.Name} に戻しました。" : "一部のoverrideファイルを削除できませんでした。現在のprofile表示を確認してください。",
            "評価関数リセット",
            MessageBoxButtons.OK,
            reset ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void OpenReport()
    {
        string? path = _lastReportPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "結果JSONを開けません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private EvaluationTuningOptions ReadOptions() => new EvaluationTuningOptions(
        Generations: (int)_generations.Value,
        CandidatesPerGeneration: (int)_candidates.Value,
        ShallowDepth: (int)_shallowDepth.Value,
        ShallowGamesPerCandidate: (int)_shallowGames.Value,
        ValidationDepth: (int)_validationDepth.Value,
        ValidationGames: (int)_validationGames.Value,
        Parallelism: (int)_parallelism.Value,
        MaxPlies: (int)_maxPlies.Value,
        OpeningRandomPlies: (int)_openingPlies.Value,
        OpeningTopK: 3,
        OpeningScoreWindow: 120,
        InitialMutationStep: (int)_mutationStep.Value,
        Seed: (int)_seed.Value).Normalize();

    private EvaluationParameterScanOptions ReadScanOptions() => new EvaluationParameterScanOptions(
        FeatureIndex: Math.Max(0, _scanFeature.SelectedIndex),
        Endgame: _scanPhase.SelectedIndex == 1,
        MinValue: (int)_scanMin.Value,
        MaxValue: (int)_scanMax.Value,
        Step: (int)_scanStep.Value,
        Depth: (int)_scanDepth.Value,
        GamesPerValue: (int)_scanGames.Value,
        Parallelism: (int)_parallelism.Value,
        MaxPlies: (int)_maxPlies.Value,
        OpeningRandomPlies: (int)_openingPlies.Value,
        OpeningTopK: 3,
        OpeningScoreWindow: 120,
        Seed: (int)_seed.Value).Normalize(EvaluationTuner.TunableFeatureNames.Count);

    private void RefreshCurrentProfileDisplay()
    {
        CpuEvaluationProfile profile = CpuEvaluationProfileProvider.Current;
        _currentProfileLabel.Text = $"現在の標準評価: {profile.Name} / source: {CpuEvaluationProfileStorage.CurrentSource}";
        if (!_running && _lastAdoptableProfile is null)
        {
            _liveStartingProfile = profile;
            _liveBestProfile = profile;
            RefreshProfileComparison(profile, profile);
        }
    }

    private void RefreshProfileComparison(CpuEvaluationProfile initial, CpuEvaluationProfile best)
    {
        _initialProfileTitle.Text = $"開始時パラメータ  [{initial.Name}]";
        _bestProfileTitle.Text = $"現在の最善パラメータ  [{best.Name}]";
        _initialProfileBox.Text = FormatProfile(initial);
        _bestProfileBox.Text = FormatProfile(best);
        _initialProfileBox.SelectionStart = 0;
        _bestProfileBox.SelectionStart = 0;
    }

    private void UpdateDashboardDisplay()
    {
        TimeSpan elapsed = _sessionStopwatch.Elapsed;
        string elapsedText = elapsed.ToString(@"hh\:mm\:ss");
        string lastBest = _liveLastBestGeneration > 0 ? $"G{_liveLastBestGeneration:0000}" : "--";
        string champion = _liveBestProfile?.Name ?? "--";
        string state = _running ? "RUNNING" : (_lastAdoptableProfile is null ? "IDLE" : "STOPPED");
        _dashboardLabel.Text =
            $"{state,-7}  経過 {elapsedText}   世代 G{_liveGeneration:0000}   総対局 {_liveTotalGames:N0}   " +
            $"ベスト更新 {_liveBestUpdates}回   最終更新 {lastBest}   変異幅 {_liveMutationStep}‰   停滞 {_liveStagnation}\n" +
            $"Champion: {champion}";
    }

    private static string FormatProfile(CpuEvaluationProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Profile: {profile.Name}");
        sb.AppendLine("feature                     opening   endgame");
        sb.AppendLine("------------------------------------------------");
        void L(string name, int opening, int endgame) => sb.AppendLine($"{name,-27} {opening,6}‰   {endgame,6}‰");
        L("RunnerProgress", profile.Opening.RunnerProgress, profile.Endgame.RunnerProgress);
        L("RunnerMobility", profile.Opening.RunnerMobility, profile.Endgame.RunnerMobility);
        L("BlockerMaterial", profile.Opening.BlockerMaterial, profile.Endgame.BlockerMaterial);
        L("FriendlyRunnerSupport", profile.Opening.FriendlyRunnerSupport, profile.Endgame.FriendlyRunnerSupport);
        L("FrontPressure", profile.Opening.FrontPressure, profile.Endgame.FrontPressure);
        L("GoalDefense", profile.Opening.GoalDefense, profile.Endgame.GoalDefense);
        L("ImmediateGoalThreats", profile.Opening.ImmediateGoalThreats, profile.Endgame.ImmediateGoalThreats);
        L("BlockerAdvancement", profile.Opening.BlockerAdvancement, profile.Endgame.BlockerAdvancement);
        L("BridgeheadConnection", profile.Opening.BridgeheadConnection, profile.Endgame.BridgeheadConnection);
        L("RunnerGoalPath", profile.Opening.RunnerGoalPath, profile.Endgame.RunnerGoalPath);
        L("PreparedGoalThreat", profile.Opening.PreparedGoalThreat, profile.Endgame.PreparedGoalThreat);
        L("UnansweredGoalThreat", profile.Opening.UnansweredGoalThreat, profile.Endgame.UnansweredGoalThreat);
        L("ConnectedGoalThreat", profile.Opening.ConnectedGoalThreat, profile.Endgame.ConnectedGoalThreat);
        L("ViableRunnerProgress", profile.Opening.ViableRunnerProgress, profile.Endgame.ViableRunnerProgress);
        L("SacrificeDebt", profile.Opening.SacrificeDebt, profile.Endgame.SacrificeDebt);
        return sb.ToString();
    }

    private void SetRunningState(bool running)
    {
        _startButton.Enabled = !running;
        _scanButton.Enabled = !running;
        _cancelButton.Enabled = running;
        _resetButton.Enabled = !running;
        _adoptButton.Enabled = !running && _lastAdoptableProfile is not null;
        foreach (Control c in new Control[]
        {
            _generations, _candidates, _shallowDepth, _shallowGames, _validationDepth,
            _validationGames, _parallelism, _maxPlies, _openingPlies, _mutationStep, _seed,
            _scanFeature, _scanPhase, _scanMin, _scanMax, _scanStep, _scanDepth, _scanGames
        })
        {
            c.Enabled = !running;
        }
    }

    private static void ConfigureProfileBox(TextBox box)
    {
        box.Dock = DockStyle.Fill;
        box.Multiline = true;
        box.ReadOnly = true;
        box.ScrollBars = ScrollBars.Both;
        box.WordWrap = false;
        box.Font = new Font("Consolas", 9f);
    }

    private static void ConfigureNumeric(NumericUpDown box, decimal min, decimal max, decimal value, decimal increment)
    {
        box.Minimum = min;
        box.Maximum = max;
        box.Value = Math.Clamp(value, min, max);
        box.Increment = increment;
        box.Width = 120;
        box.TextAlign = HorizontalAlignment.Right;
        box.ThousandsSeparator = true;
    }

    private static void AddSetting(
        TableLayoutPanel panel,
        int row,
        string leftLabel,
        Control leftControl,
        string rightLabel,
        Control rightControl)
    {
        while (panel.RowCount <= row)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowCount++;
        }
        var left = new Label { AutoSize = true, Text = leftLabel, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };
        var right = new Label { AutoSize = true, Text = rightLabel, Anchor = AnchorStyles.Left, Margin = new Padding(12, 7, 3, 3) };
        panel.Controls.Add(left, 0, row);
        panel.Controls.Add(leftControl, 1, row);
        panel.Controls.Add(right, 2, row);
        panel.Controls.Add(rightControl, 3, row);
    }

    private static void AddFlowSetting(FlowLayoutPanel panel, string labelText, Control control)
    {
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = labelText,
            Margin = new Padding(8, 7, 3, 3)
        });
        control.Margin = new Padding(0, 2, 8, 2);
        panel.Controls.Add(control);
    }

    private static void ConfigureButton(Button button, string text, int width)
    {
        button.Text = text;
        button.AutoSize = false;
        button.Width = width;
        button.Height = 30;
        button.Margin = new Padding(0, 0, 8, 0);
    }
}
