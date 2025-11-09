using System.Windows;
using System.Windows.Input;
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

        private bool USE_OPTION_2 = false;
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

                    if (!_suspendAutoView) // ★ 플래그로 멈춤 제어
                    {
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
                }
            };

            AddNewDataTimer.Start();
            UpdatePlotTimer.Start();
        }

        // 휠 핸들러 (의도 감지 후 X 관련이면 켜기)
        private void GraphControl_PreviewMouseWheel0(object? sender, MouseWheelEventArgs e)
        {
            // ... 좌표/수치 계산 생략 ...
            var p = e.GetPosition(GraphControl);
            var mousePixel = new ScottPlot.Pixel(p.X, p.Y);
            ScottPlot.Coordinates c = GraphControl.Plot.GetCoordinates(mousePixel);

            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            // 의도 판별 (기존 규칙 그대로 사용)
            bool intentXScroll = shift;
            bool intentXZoom = ctrl;
            bool intentYZoom = !ctrl && (!USE_OPTION_2 || alt);
            bool intentXYZoom = USE_OPTION_2 && !(ctrl || alt || shift);

            if (intentXScroll)
            {
                PanX(GraphControl.Plot, (e.Delta > 0 ? -1 : 1) * 0.10);
                BeginUserXOverride();
                _suspendAutoView = true;               // ★ 자동 뷰 멈춤
            }
            else if (intentXZoom)
            {
                ZoomXAt(GraphControl.Plot, c.X, ZoomFactorFromDelta(e.Delta, 0.9));
                BeginUserXOverride();
                _suspendAutoView = true;               // ★
            }
            else if (intentXYZoom)
            {
                ZoomXAt(GraphControl.Plot, c.X, ZoomFactorFromDelta(e.Delta, 0.9));
                ZoomYAt(GraphControl.Plot, c.Y, ZoomFactorFromDelta(e.Delta, 0.9));
                BeginUserXOverride();
                _suspendAutoView = true;               // ★
            }
            else if (intentYZoom)
            {
                ZoomYAt(GraphControl.Plot, c.Y, ZoomFactorFromDelta(e.Delta, 0.9));
                // 필요시 Y만 변경 때도 멈추려면 다음 줄 추가
                _suspendAutoView = true;
            }

            GraphControl.Refresh();
            e.Handled = true;
        }


        private bool _suspendAutoView = false;

        private void InitGraphMouseEvent()
        {
            // 내장 휠 줌 응답 제거(중복 방지)
            GraphControl.UserInputProcessor.UserActionResponses.RemoveAll(r =>
                r is ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom);

            // (필요 시) 모든 기본 마우스 동작을 완전히 끄고 싶으면 아래 주석을 해제
            // GraphControl.UserInputProcessor.UserActionResponses.Clear();

            // 기본 마우스 휠 핸들러 제거(있다면)
            // GraphControl.MouseWheel -= GraphControl_MouseWheel_NoUse;

            // 커스텀 휠 입력
            GraphControl.PreviewMouseWheel += GraphControl_PreviewMouseWheel0;

            // 포커스(키보드 조합 안정화)
            Loaded += (_, __) =>
            {
                GraphControl.Focus();
                // Plot.GetCoordinates 사용 시 첫 렌더를 보장하고 싶다면:
                // GraphControl.Refresh();
            };
        }

        // 혹시 기존에 연결되어 있던 핸들러가 있으면 비워두고 제거
        private void GraphControl_MouseWheel_NoUse(object? s, MouseWheelEventArgs e) { }

        #region Wheel helpers (커서 기준 X/Y 줌, X축 스크롤)
        private static double ZoomFactorFromDelta(int delta, double step = 0.9)
            => delta > 0 ? step : 1.0 / step; // 업: 확대, 다운: 축소

        private static void ZoomXAt(ScottPlot.Plot plot, double x, double factor)
        {
            var ax = plot.Axes.Bottom;
            double min = ax.Min, max = ax.Max, span = max - min;
            if (span <= 0) return;

            double t = (x - min) / span;
            double newSpan = span * factor;   // factor<1 확대, >1 축소
            ax.Min = x - newSpan * t;
            ax.Max = x + newSpan * (1 - t);
        }

        private static void ZoomYAt(ScottPlot.Plot plot, double y, double factor)
        {
            var ay = plot.Axes.Left;
            double min = ay.Min, max = ay.Max, span = max - min;
            if (span <= 0) return;

            double t = (y - min) / span;
            double newSpan = span * factor;
            ay.Min = y - newSpan * t;
            ay.Max = y + newSpan * (1 - t);
        }

        private static void PanX(ScottPlot.Plot plot, double fraction)
        {
            var ax = plot.Axes.Bottom;
            double span = ax.Max - ax.Min;
            double dx = span * fraction;
            ax.Min += dx;
            ax.Max += dx;
        }
        #endregion

        #region Wheel handler (1안/2안 적용)

        // MainWindow 필드
        private DateTime _userXOverrideUntil = DateTime.MinValue;
        private double? _pinnedLeft = null;               // 사용자 조작 직후 고정할 Left
        private readonly TimeSpan _pinDuration = TimeSpan.FromMilliseconds(50);
        private const double _minSpan = 1e-9;             // 0폭 방지용 최소 폭
        private void BeginUserXOverride()
        {
            _userXOverrideUntil = DateTime.UtcNow + _pinDuration;

            var lim = GraphControl.Plot.Axes.GetLimits();
            _pinnedLeft = lim.Left;

            _width = lim.Right - lim.Left;
            if (_width < _minSpan) _width = _minSpan;   // ★ 추가

            Logger1.ManageAxisLimits = false;
            Logger2.ManageAxisLimits = false;
        }

        private bool IsUserXOverrideActive()
            => DateTime.UtcNow <= _userXOverrideUntil && _pinnedLeft.HasValue;

        // (선택) 핀이 끝났다면 해제
        private void MaybeClearPin()
        {
            if (!IsUserXOverrideActive())
                _pinnedLeft = null;
        }


        private void GraphControl_PreviewMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            // ① 더 이상 모드 해제(자유 모드 전환)하지 않음
            // _SavedViewMode = ViewMode.None;        // ← 삭제
            // DisableMouseMoveLimit();               // ← 삭제 (Slide/Jump와 충돌)

            // 커서의 픽셀 → 좌표
            var p = e.GetPosition(GraphControl);
            var mousePixel = new ScottPlot.Pixel(p.X, p.Y);
            ScottPlot.Coordinates c = GraphControl.Plot.GetCoordinates(mousePixel);

            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            double factor = ZoomFactorFromDelta(e.Delta, 0.9);

            // ② 현재 모드가 유지되는 전제에서, Slide/Jump일 때는 X축을 건드리지 않도록 권장
            bool modeLocksX = _SavedViewMode is ViewMode.Slide or ViewMode.Jump;

            if (shift)
            {
                // X축 팬
                double dir = e.Delta > 0 ? -1 : 1;
                PanX(GraphControl.Plot, dir * 0.10);
                BeginUserXOverride();           // ★ 추가
            }
            else if (ctrl)
            {
                // X축 줌
                ZoomXAt(GraphControl.Plot, c.X, factor);
                BeginUserXOverride();           // ★ 추가
            }
            else
            {
                if (USE_OPTION_2)
                {
                    if (alt)
                    {
                        // Y만
                        ZoomYAt(GraphControl.Plot, c.Y, factor);
                    }
                    else
                    {
                        // 기본: XY 동시 줌 → X도 바뀌므로 보호
                        ZoomXAt(GraphControl.Plot, c.X, factor);
                        ZoomYAt(GraphControl.Plot, c.Y, factor);
                        BeginUserXOverride();   // ★ 추가
                    }
                }
                else
                {
                    // 1안: 기본 Y만 → 보호 불필요
                    ZoomYAt(GraphControl.Plot, c.Y, factor);
                }
            }

            GraphControl.Refresh();
            e.Handled = true;
        }

        private void GraphControl_PreviewMouseWheel2(object? sender, MouseWheelEventArgs e)
        {
            // 휠을 돌리면 자유 이동 모드로 전환
            _SavedViewMode = ViewMode.None;
            DisableMouseMoveLimit();

            // 커서의 픽셀 → 좌표
            var p = e.GetPosition(GraphControl);
            var mousePixel = new ScottPlot.Pixel(p.X, p.Y);
            ScottPlot.Coordinates c = GraphControl.Plot.GetCoordinates(mousePixel); // c.X, c.Y

            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            double factor = ZoomFactorFromDelta(e.Delta, 0.9);

            if (shift)
            {
                // X축 스크롤(팬): 한 번에 현재 범위의 10% 이동
                double dir = e.Delta > 0 ? -1 : 1; // 업: 왼쪽, 다운: 오른쪽
                PanX(GraphControl.Plot, dir * 0.10);
                // 필요 시: OnXAxisScrolled();
            }
            else if (ctrl)
            {
                // X축만 줌
                ZoomXAt(GraphControl.Plot, c.X, factor);
            }
            else if (USE_OPTION_2 && alt)
            {
                // (2안일 때) Alt: Y축만 줌
                ZoomYAt(GraphControl.Plot, c.Y, factor);
            }
            else
            {
                if (USE_OPTION_2)
                {
                    // 2안: 기본 = X·Y 동시 줌
                    ZoomXAt(GraphControl.Plot, c.X, factor);
                    ZoomYAt(GraphControl.Plot, c.Y, factor);
                }
                else
                {
                    // 1안: 기본 = Y축만 줌
                    ZoomYAt(GraphControl.Plot, c.Y, factor);
                }
            }

            GraphControl.Refresh();
            e.Handled = true; // 내장 처리 방지
        }

        #endregion

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
            _suspendAutoView = false; 
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
            const double marginX = 0;

            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var plot = GraphControl.Plot;
                var cur = plot.Axes.GetLimits(); // Y 유지

                // 점프 목표(기존 로직)
                var (tLeft, tRight) = ComputeJumpWindow(newestX, width, paddingFraction);

                double left, right;

                if (IsUserXOverrideActive())
                {
                    // Left 고정 + "폭(_width) 그대로" 유지
                    left = _pinnedLeft!.Value;
                    right = Math.Max(left + _width, left + _minSpan);
                }

                else
                {
                    // ★ 평소 점프: 기존 규칙대로
                    left = tLeft;
                    right = tRight;
                }

                plot.Axes.SetLimits(left, right, cur.Bottom, cur.Top);
                GraphControl.Refresh();

                MaybeClearPin();
            });
        }

        public void CustomViewSlide()
        {
            // 최신 X 계산
            double maxX = double.NegativeInfinity;
            lock (_lock)
            {
                foreach (var line in _lines)
                {
                    var last = line.Data.Coordinates.LastOrDefault();
                    maxX = Math.Max(maxX, last.X);
                }
                if (double.IsNegativeInfinity(maxX))
                    maxX = 0;
            }

            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var plot = GraphControl.Plot;
                var lim = plot.Axes.GetLimits(); // Y 유지

                double left, right;

                if (IsUserXOverrideActive())
                {
                    // Left 고정 + "폭(_width) 그대로" 유지 (축소/확대 모두 반영)
                    left = _pinnedLeft!.Value;
                    right = Math.Max(left + _width, left + _minSpan);
                }

                else
                {
                    // ★ 평소 슬라이드: 폭을 유지하고 오른쪽 끝이 최신 X
                    right = maxX;
                    left = right - _width;
                }

                plot.Axes.SetLimits(left, right, lim.Bottom, lim.Top);
                GraphControl.Refresh();

                // 핀 해제 시점이 되었는지 확인 (선택)
                MaybeClearPin();
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
