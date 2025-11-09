using System.Windows;
using System.Windows.Shapes;
using System.Windows.Threading;
using ScottPlot;
using ScottPlot.Plottables;

namespace GraphLongYValueIssue
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        object _lock = new();
        bool _multApplied = false;
        long bottom = -140_737_488_355_327;
        long top = 422_212_465_065_982;
        private ViewMode _SavedViewMode;

        private double _width = 1000;

        private List<DataLogger> _lines = new();
        readonly DispatcherTimer AddNewDataTimer = new() { Interval = new(5000) };
        readonly DispatcherTimer UpdatePlotTimer = new() { Interval = new(5000) };

        readonly ScottPlot.Plottables.DataLogger Logger1;
        readonly ScottPlot.Plottables.DataLogger Logger2;

        readonly ScottPlot.DataGenerators.RandomWalker Walker1 = new(0, multiplier: 0.01);
        readonly ScottPlot.DataGenerators.RandomWalker Walker2 = new(1, multiplier: 1000_000_000_000_000);

        public MainWindow()
        {
            InitializeComponent();
            _SavedViewMode = ViewMode.None;

            InitGraphMouseEvent();
            // create two loggers and add them to the plot
            Logger1 = GraphControl.Plot.Add.DataLogger();
            Logger2 = GraphControl.Plot.Add.DataLogger();
            _lines.Add(Logger1);
            _lines.Add(Logger2);

            AddNewDataTimer.Tick += (s, e) =>
            {
                int count = 5;
                Logger1.Add(Walker1.Next(count));
                //Logger2.Add(Walker1.Next(count));
                long[] values = LargeRangeRng.NextArray(count, bottom, top);
                foreach (long value in values)
                {
                    double toDouble = (double)value;
                    Logger2.Add(toDouble);
                }
            };

            UpdatePlotTimer.Tick += (s, e) =>
            {
                if (Logger1.HasNewData || Logger2.HasNewData)
                {
                    #region Not Used
                    /*                    // 데이터가 들어와 틱이 만들어진 첫 순간에만 적용
                                        if (!_multApplied && (Logger1.Data.Coordinates.Count > 0 || Logger2.Data.Coordinates.Count > 0))
                                        {
                                            var axes = GraphControl.Plot.Axes;
                                            axes.SetupMultiplierNotation(axes.Left);   // Y축
                                                                                       // axes.SetupMultiplierNotation(axes.Bottom); // 필요 시 X축
                                            _multApplied = true;
                                        }*/
                    #endregion

                    switch (_SavedViewMode)
                    {
                        case ViewMode.Slide:
                            CustomViewSlide();
                            break;
                        case ViewMode.Jump:
                            CustomViewJump();
                            break;
                    }

                    GraphControl.Refresh();
                }
            };

            AddNewDataTimer.Start();
            UpdatePlotTimer.Start();
        }

        private void InitGraphMouseEvent()
        {
            GraphControl.MouseWheel += (s, e) =>
            {
                DisableMouseMoveLimit();
                _SavedViewMode = ViewMode.None;
            };
        }

        #region Default View Modes
        private void Full_Click(object sender, RoutedEventArgs e)
        {
            Logger1.ViewFull(); Logger2.ViewFull();
        }

        private void Jump_Click(object sender, RoutedEventArgs e)
        {
            Logger1.ViewJump(); Logger2.ViewJump();
        }

        private void Slide_Click(object sender, RoutedEventArgs e)
        {
            Logger1.ViewSlide(); Logger2.ViewSlide();
        }
        #endregion

        private void Disable_Click(object sender, RoutedEventArgs e)
        {
            DisableMouseMoveLimit();
            _SavedViewMode = ViewMode.None;
        }

        private void CustomJump_Click(object sender, RoutedEventArgs e)
        {
            DisableMouseMoveLimit();
            _SavedViewMode = ViewMode.Jump;
        }

        private void DisableMouseMoveLimit()
        {
            Logger1.ManageAxisLimits = false;
            Logger2.ManageAxisLimits = false;
        }

        private bool _isCustomViewJumpInProgress = false;

        private static bool ShouldJump(double newestX, double currentRight, double marginX)
    => newestX > currentRight - marginX;

        public void CustomViewSlide()
        {
            double leftLimit, rightLimit, xAxisLength;

            lock (_lock)
            {
                xAxisLength = _width;
                double maxX = double.NegativeInfinity;

                foreach (var line in _lines)
                {
                    var last = line.Data.Coordinates.LastOrDefault();
                    maxX = Math.Max(maxX, last.X);
                }

                if (double.IsNegativeInfinity(maxX))
                    maxX = 0; // 라인이 비었을 때 대비

                rightLimit = maxX;
                leftLimit = rightLimit - xAxisLength;
            }

            // 2) UI 호출은 락 밖에서 디스패처로
            // WPF:
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var plot = GraphControl.Plot;
                var limit = plot.Axes.GetLimits(); // UI 스레드에서
                ZoomToRegion(plot, leftLimit, rightLimit, limit.Bottom, limit.Top);
                GraphControl.Refresh(); // 필요 시
            });
        }

        public void CustomViewJump()
        {
            DisableMouseMoveLimit();

            double width, newestX;
            lock (_lock)
            {
                width = _width;
                newestX = 0;
                foreach (var line in _lines)
                    if (line.Data.Coordinates.Count > 0)
                        newestX = Math.Max(newestX, line.Data.Coordinates[^1].X);
            }

            const double paddingFraction = 0.5;
            const double marginX = 0; // 필요하면 0.05 * width 등으로 여유를 둘 수 있음

            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var plot = GraphControl.Plot;
                var cur = plot.Axes.GetLimits();

                if (ShouldJump(newestX, cur.Right, marginX))
                {
                    var (leftLimit, rightLimit) = ComputeJumpWindow(newestX, width, paddingFraction);
                    ZoomToRegion(plot, leftLimit, rightLimit, cur.Bottom, cur.Top);
                    GraphControl.Refresh();
                }
            });
        }

        public void CustomViewJump2()
        {
            // 1) 공유 데이터만 락으로 보호하여 스냅샷/계산
            DisableMouseMoveLimit();
            double width;
            double newestX;

            lock (_lock)
            {
                width = _width;

                newestX = double.NegativeInfinity;
                foreach (var line in _lines)
                {
                    // 좌표가 비어있을 수 있으니 안전 접근
                    if (line.Data.Coordinates.Count > 0)
                    {
                        var last = line.Data.Coordinates[^1];
                        if (last.X > newestX) newestX = last.X;
                    }
                }

                if (double.IsNegativeInfinity(newestX))
                    newestX = 0;
            }

            const double paddingFraction = 0.5;
            var (leftLimit, rightLimit) = ComputeJumpWindow(newestX, width, paddingFraction);

            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var plot = GraphControl.Plot;
                var lim = plot.Axes.GetLimits(); // 현재 Y는 유지
                ZoomToRegion(plot, leftLimit, rightLimit, lim.Bottom, lim.Top);
                GraphControl.Refresh();
            });
        }

        private static (double left, double right) ComputeJumpWindow(double newestX, double width, double paddingFraction)
        {
            double left = newestX - width * (1.0 - paddingFraction);
            double right = newestX + width * paddingFraction;
            return (left, right);
        }

        public void ZoomToRegion2(Plot plot, double targetLeft, double targetRight, double targetBottom, double targetTop)
        {
            plot.Axes.SetLimits(targetLeft, targetRight, targetBottom, targetTop);
        }

        /// <summary>
        /// 특정 영역으로 줌 및 이동
        /// </summary>
        /// <param name="targetLeft">목표 영역의 왼쪽 X 좌표</param>
        /// <param name="targetRight">목표 영역의 오른쪽 X 좌표</param>
        /// <param name="targetBottom">목표 영역의 아래쪽 Y 좌표</param>
        /// <param name="targetTop">목표 영역의 위쪽 Y 좌표</param>
        public void ZoomToRegion(Plot plot, double targetLeft, double targetRight, double targetBottom, double targetTop)
        {
            var axes = plot.Axes;

            // 1. 목표 영역의 중심과 span 계산
            double targetCenterX = (targetLeft + targetRight) / 2.0;
            double targetCenterY = (targetBottom + targetTop) / 2.0;
            double targetSpanX = targetRight - targetLeft;
            double targetSpanY = targetTop - targetBottom;

            // 2. 현재 limits 가져오기
            var currentLimits = axes.GetLimits();
            double currentSpanX = currentLimits.Right - currentLimits.Left;
            double currentSpanY = currentLimits.Top - currentLimits.Bottom;

            // 3. Zoom factor 계산 (목표 영역이 화면에 꽉 차도록)
            double fractionX = (targetSpanX > 0) ? currentSpanX / targetSpanX : 1.0;
            double fractionY = (targetSpanY > 0) ? currentSpanY / targetSpanY : 1.0;

            // 4. 줌 적용
            axes.Zoom(fractionX, fractionY);

            // 5. 목표 영역의 중심으로 이동
            var updatedLimits = axes.GetLimits();
            double currentCenterX = (updatedLimits.Left + updatedLimits.Right) / 2.0;
            double currentCenterY = (updatedLimits.Bottom + updatedLimits.Top) / 2.0;

            double dx = targetCenterX - currentCenterX;
            double dy = targetCenterY - currentCenterY;

            axes.Pan(new CoordinateOffset(dx, dy));
        }

        private void CustomSlide_Click(object sender, RoutedEventArgs e)
        {
            DisableMouseMoveLimit();
            _SavedViewMode = ViewMode.Slide;
        }
    }
}

public enum ViewMode
{
    None,
    Slide,
    Jump,
}

public static class LargeRangeRng
{
    private static readonly Random _rng = new Random(1234); // 필요시 시드 변경

    /// <summary> [min, max] 범위의 균일분포 난수(double) </summary>
    public static long Nextlong(long min, long max)
    {
        if (max < min) (min, max) = (max, min);
        return min + _rng.NextInt64() * (max - min);
    }

    /// <summary> [min, max] 범위의 난수 N개 생성 </summary>
    public static long[] NextArray(int count, long min, long max)
        => Enumerable.Range(0, count)
                     .Select(_ => Nextlong(min, max))
                     .ToArray();
}
