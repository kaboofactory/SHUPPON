using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace StarRunnerPrototype;

public sealed class DepthBenchmarkForm : Form
{
    private readonly GameEngine _position;
    private readonly PlayerId _player;
    private readonly NumericUpDown _maxDepth = new();
    private readonly NumericUpDown _timeLimit = new();
    private readonly NumericUpDown _nodeLimit = new();
    private readonly NumericUpDown _threads = new();
    private readonly Button _start = new();
    private readonly Button _cancel = new();
    private readonly Button _openFolder = new();
    private readonly Label _status = new();
    private readonly DataGridView _grid = new();
    private CancellationTokenSource? _cts;
    private bool _running;

    public DepthBenchmarkForm(
        GameEngine position,
        int suggestedMaxDepth,
        int suggestedTimeLimit,
        long suggestedNodeLimit,
        int suggestedThreads)
    {
        _position = position.Clone();
        _player = position.CurrentPlayer;

        Text = $"現在局面 深度ベンチ - {_player.ShortName()} / ply {position.PlyCount}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 560);
        ClientSize = new Size(1040, 650);
        Font = new Font("Yu Gothic UI", 9f);

        BuildUi(suggestedMaxDepth, suggestedTimeLimit, suggestedNodeLimit, suggestedThreads);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosing(e);
    }

    private void BuildUi(int suggestedMaxDepth, int suggestedTimeLimit, long suggestedNodeLimit, int suggestedThreads)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(990, 0),
            Text = "同じ現在局面を D1 → D2 → … と独立に再解析します。選択手と評価値がどの深度で安定するかを見るための検証です。\n" +
                   "時間/Node上限が0なら無制限。深いDが重い場合はNode上限を使うと実験条件を固定しやすくなります。CPU処理は低優先度です。",
            Margin = new Padding(0, 0, 0, 8)
        };
        root.Controls.Add(intro, 0, 0);

        var settings = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        root.Controls.Add(settings, 0, 1);

        ConfigureNumeric(_maxDepth, 1, 10, Math.Clamp(suggestedMaxDepth, 1, 10), 1, 70);
        ConfigureNumeric(_timeLimit, 0, 120000, Math.Clamp(suggestedTimeLimit, 0, 120000), 100, 100);
        ConfigureNumeric(_nodeLimit, 0, 2_000_000_000m, Math.Clamp((decimal)suggestedNodeLimit, 0m, 2_000_000_000m), 100_000, 130);
        ConfigureNumeric(_threads, 1, Math.Max(1, Environment.ProcessorCount), Math.Clamp(suggestedThreads, 1, Math.Max(1, Environment.ProcessorCount)), 1, 70);

        AddSetting(settings, "最大D", _maxDepth);
        AddSetting(settings, "1回上限ms", _timeLimit);
        AddSetting(settings, "1回Node上限", _nodeLimit);
        AddSetting(settings, "探索スレッド", _threads);

        ConfigureButton(_start, "開始", 90);
        ConfigureButton(_cancel, "中止", 90);
        ConfigureButton(_openFolder, "解析ログを開く", 130);
        _cancel.Enabled = false;
        settings.Controls.Add(_start);
        settings.Controls.Add(_cancel);
        settings.Controls.Add(_openFolder);

        _status.AutoSize = true;
        _status.Text = "待機中";
        _status.Margin = new Padding(0, 4, 0, 8);
        root.Controls.Add(_status, 0, 2);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.Columns.Add("requested", "要求D");
        _grid.Columns.Add("completed", "完了D");
        _grid.Columns.Add("move", "選択手");
        _grid.Columns.Add("same", "前Dと同手");
        _grid.Columns.Add("score", "評価値");
        _grid.Columns.Add("nodes", "Nodes");
        _grid.Columns.Add("ms", "ms");
        _grid.Columns.Add("nps", "NPS");
        _grid.Columns.Add("alloc", "割当MB");
        _grid.Columns.Add("stop", "停止理由");
        _grid.Columns.Add("tt", "TT hit");
        root.Controls.Add(_grid, 0, 3);

        _start.Click += async (_, _) => await RunAsync();
        _cancel.Click += (_, _) => _cts?.Cancel();
        _openFolder.Click += (_, _) => OpenAnalysisFolder();
    }

    private async Task RunAsync()
    {
        if (_running) return;

        _running = true;
        _cts = new CancellationTokenSource();
        _start.Enabled = false;
        _cancel.Enabled = true;
        _grid.Rows.Clear();
        SetSettingsEnabled(false);

        int maxDepth = (int)_maxDepth.Value;
        int timeLimit = (int)_timeLimit.Value;
        long nodeLimit = decimal.ToInt64(_nodeLimit.Value);
        int threads = (int)_threads.Value;
        var rows = new List<BenchmarkRow>();
        Move? previousMove = null;

        try
        {
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                _status.Text = $"D{depth} を解析中… ({depth}/{maxDepth})";

                var options = new CpuSearchOptions(
                    MaxDepth: depth,
                    TimeLimitMilliseconds: timeLimit,
                    MaxNodes: nodeLimit,
                    UseTranspositionTable: true,
                    CollectExactRootScores: false,
                    MaxParallelism: threads,
                    UseBelowNormalThreadPriority: true);

                long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
                CpuDecision decision = await Task.Run(
                    () => CpuPlayer.DecideMove(_position.Clone(), _player, options, _cts.Token),
                    _cts.Token);
                long allocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore);

                bool? sameMove = previousMove.HasValue ? previousMove.Value == decision.Move : null;
                string stopReason = decision.NodeLimitReached
                    ? "Node上限"
                    : decision.TimedOut
                        ? "時間上限"
                        : decision.Depth < depth
                            ? "未完了"
                            : "完了";

                var row = new BenchmarkRow(
                    depth,
                    decision.Depth,
                    decision.Move.ToNotation(),
                    sameMove,
                    decision.Score,
                    decision.Nodes,
                    decision.ElapsedMilliseconds,
                    decision.NodesPerSecond,
                    allocatedBytes,
                    stopReason,
                    decision.TranspositionHits);
                rows.Add(row);
                _grid.Rows.Add(
                    row.RequestedDepth,
                    row.CompletedDepth,
                    row.Move,
                    row.SameAsPrevious is null ? "-" : row.SameAsPrevious.Value ? "YES" : "NO",
                    row.Score,
                    row.Nodes.ToString("N0"),
                    row.ElapsedMilliseconds.ToString("N0"),
                    row.NodesPerSecond.ToString("N0"),
                    (row.AllocatedBytes / (1024d * 1024d)).ToString("N2", CultureInfo.CurrentCulture),
                    row.StopReason,
                    row.TranspositionHits.ToString("N0"));
                if (_grid.Rows.Count > 0)
                {
                    _grid.FirstDisplayedScrollingRowIndex = _grid.Rows.Count - 1;
                }
                previousMove = decision.Move;
            }

            string path = SaveCsv(rows, timeLimit, nodeLimit, threads);
            int stableTail = CountStableTail(rows);
            _status.Text = $"完了: D{maxDepth}まで / 最後から同じ手が {stableTail} 深度連続 / CSV: {Path.GetFileName(path)}";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "中止しました";
        }
        catch (Exception ex)
        {
            _status.Text = "エラー";
            MessageBox.Show(this, ex.ToString(), "深度ベンチ エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _running = false;
            _cts?.Dispose();
            _cts = null;
            _start.Enabled = true;
            _cancel.Enabled = false;
            SetSettingsEnabled(true);
        }
    }

    private static int CountStableTail(IReadOnlyList<BenchmarkRow> rows)
    {
        if (rows.Count == 0) return 0;
        string move = rows[^1].Move;
        int count = 0;
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(rows[i].Move, move, StringComparison.Ordinal)) break;
            count++;
        }
        return count;
    }

    private string SaveCsv(IReadOnlyList<BenchmarkRow> rows, int timeLimit, long nodeLimit, int threads)
    {
        string directory = HeadlessBatchRunner.ResolveAnalysisDirectory();
        Directory.CreateDirectory(directory);
        RotateBenchmarkFiles(directory, 29);
        string path = Path.Combine(directory, $"depth_benchmark_{DateTime.Now:yyyyMMdd_HHmmss_fff}.csv");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine($"# player={_player.ShortName()},ply={_position.PlyCount},timeLimitMs={timeLimit},nodeLimit={nodeLimit},threads={threads}");
        writer.WriteLine("requestedDepth,completedDepth,move,sameAsPrevious,score,nodes,elapsedMs,nps,allocatedBytes,stopReason,transpositionHits");
        foreach (BenchmarkRow row in rows)
        {
            writer.WriteLine(string.Join(",",
                row.RequestedDepth.ToString(CultureInfo.InvariantCulture),
                row.CompletedDepth.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(row.Move),
                row.SameAsPrevious?.ToString() ?? string.Empty,
                row.Score.ToString(CultureInfo.InvariantCulture),
                row.Nodes.ToString(CultureInfo.InvariantCulture),
                row.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
                row.NodesPerSecond.ToString(CultureInfo.InvariantCulture),
                row.AllocatedBytes.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(row.StopReason),
                row.TranspositionHits.ToString(CultureInfo.InvariantCulture)));
        }
        RotateBenchmarkFiles(directory, 30);
        return path;
    }

    private static void RotateBenchmarkFiles(string directory, int maxFiles)
    {
        try
        {
            FileInfo[] files = new DirectoryInfo(directory)
                .GetFiles("depth_benchmark_*.csv", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f.CreationTimeUtc)
                .ThenBy(f => f.Name, StringComparer.Ordinal)
                .ToArray();
            int excess = files.Length - Math.Max(0, maxFiles);
            for (int i = 0; i < excess; i++)
            {
                try { files[i].Delete(); } catch { }
            }
        }
        catch { }
    }

    private void OpenAnalysisFolder()
    {
        try
        {
            string directory = HeadlessBatchRunner.ResolveAnalysisDirectory();
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "解析ログを開けません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetSettingsEnabled(bool enabled)
    {
        _maxDepth.Enabled = enabled;
        _timeLimit.Enabled = enabled;
        _nodeLimit.Enabled = enabled;
        _threads.Enabled = enabled;
    }

    private static void AddSetting(FlowLayoutPanel panel, string label, Control control)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(8, 7, 3, 0) });
        panel.Controls.Add(control);
    }

    private static void ConfigureNumeric(NumericUpDown control, decimal min, decimal max, decimal value, decimal increment, int width)
    {
        control.Minimum = min;
        control.Maximum = max;
        control.Value = value;
        control.Increment = increment;
        control.Width = width;
        control.TextAlign = HorizontalAlignment.Right;
        control.ThousandsSeparator = true;
    }

    private static void ConfigureButton(Button button, string text, int width)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 28;
        button.Margin = new Padding(8, 2, 2, 2);
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n')) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private sealed record BenchmarkRow(
        int RequestedDepth,
        int CompletedDepth,
        string Move,
        bool? SameAsPrevious,
        int Score,
        long Nodes,
        long ElapsedMilliseconds,
        long NodesPerSecond,
        long AllocatedBytes,
        string StopReason,
        long TranspositionHits);
}
