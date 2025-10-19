using System.Windows;
using System.Windows.Threading;
using ScottPlot;

namespace GraphLongYValueIssue
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        bool _multApplied = false;
        long bottom = -140_737_488_355_327;
        long top = 422_212_465_065_982;
        readonly DispatcherTimer AddNewDataTimer = new() { Interval = new(5000)};
        readonly DispatcherTimer UpdatePlotTimer = new() { Interval = new(5000)};

        readonly ScottPlot.Plottables.DataLogger Logger1;
        readonly ScottPlot.Plottables.DataLogger Logger2;

        readonly ScottPlot.DataGenerators.RandomWalker Walker1 = new(0, multiplier: 0.01);
        readonly ScottPlot.DataGenerators.RandomWalker Walker2 = new(1, multiplier: 1000_000_000_000_000);
        public MainWindow()
        {
            InitializeComponent();

            //WpfPlot.UserInputProcessor.Disable();

            // create two loggers and add them to the plot
            Logger1 = WpfPlot.Plot.Add.DataLogger();
            Logger2 = WpfPlot.Plot.Add.DataLogger();

            AddNewDataTimer.Tick += (s, e) =>
            {
                int count = 5;
                Logger1.Add(Walker1.Next(count));
                Logger2.Add(Walker1.Next(count));
/*                long[] values = LargeRangeRng.NextArray(count, bottom, top);
                foreach (long value in values)
                {
                    double toDouble = (double)value;
                    Logger2.Add(toDouble);
                }*/
            };

            UpdatePlotTimer.Tick += (s, e) =>
            {
                if (Logger1.HasNewData || Logger2.HasNewData)
                {
                    // 데이터가 들어와 틱이 만들어진 첫 순간에만 적용
                    if (!_multApplied && (Logger1.Data.Coordinates.Count > 0 || Logger2.Data.Coordinates.Count > 0))
                    {
                        var axes = WpfPlot.Plot.Axes;
                        axes.SetupMultiplierNotation(axes.Left);   // Y축
                                                                   // axes.SetupMultiplierNotation(axes.Bottom); // 필요 시 X축
                        _multApplied = true;
                    }

                    WpfPlot.Refresh();
                }
            };

            AddNewDataTimer.Start();
            UpdatePlotTimer.Start();
        }

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
    }
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
