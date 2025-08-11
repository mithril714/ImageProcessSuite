using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;


namespace ImageProcessSuite
{
    /// <summary>
    /// GraphWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class GraphWindow : Window
    {
        private int[] _gray; private int[] _r; private int[] _g; private int[] _b;

        public GraphWindow()
        {
            InitializeComponent();
        }

        public void SetData(int[] gray, int[] r, int[] g, int[] b)
        {
            _gray = gray; _r = r; _g = g; _b = b;
        }

        private void GraphImage_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

        public void Redraw()
        {
            int dataLen = _gray != null ? _gray.Length :
                          (_r != null ? Math.Max(_r.Length, Math.Max(_g.Length, _b.Length)) : 0);
            if (dataLen <= 1) { GraphImage.Source = null; return; }

            int W = (int)Math.Max(400, GraphImage.ActualWidth);
            int H = (int)Math.Max(240, GraphImage.ActualHeight);
            if (W <= 0 || H <= 0) return;

            const double LEFT = 44, RIGHT = 10, TOP = 10, BOTTOM = 28;
            var plot = new Rect(LEFT, TOP, W - LEFT - RIGHT, H - TOP - BOTTOM);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // 背景
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));

                // 軸・目盛
                DrawAxes(dc, plot, dataLen, 0, 255);

                // データ
                if (_gray != null)
                    DrawSeries(dc, _gray, plot, Brushes.Black);
                else
                {
                    DrawSeries(dc, _r, plot, Brushes.Red);
                    DrawSeries(dc, _g, plot, Brushes.Green);
                    DrawSeries(dc, _b, plot, Brushes.Blue);
                }

                // 外枠
                dc.DrawRectangle(null, new Pen(Brushes.Gray, 1), new Rect(0.5, 0.5, W - 1, H - 1));
            }

            var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            GraphImage.Source = rtb;
        }

        // ==== 描画ユーティリティ ====

        private static void DrawAxes(DrawingContext dc, Rect plot, int xCount, double yMin, double yMax)
        {
            var axisPen = new Pen(Brushes.Black, 1);
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(235, 235, 235)), 1);

            // 軸
            dc.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom)); // X
            dc.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));  // Y

            // Y 目盛（0..255, 64刻み）
            int[] yTicks = new[] { 0, 64, 128, 192, 255 };
            foreach (var v in yTicks)
            {
                double y = MapValue(v, yMin, yMax, plot.Bottom, plot.Top);
                dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                dc.DrawLine(axisPen, new Point(plot.Left - 4, y), new Point(plot.Left, y));
                DrawText(dc, v.ToString(), new Point(plot.Left - 8, y), "right", "center");
            }

            // X 目盛（最大約10個）
            int targetTicks = 10;
            int step = Math.Max(1, (int)Math.Round((double)(xCount - 1) / targetTicks));
            for (int i = 0; i < xCount; i += step)
            {
                double x = MapValue(i, 0, xCount - 1, plot.Left, plot.Right);
                dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                dc.DrawLine(axisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
                DrawText(dc, i.ToString(), new Point(x, plot.Bottom + 6), "center", "top");
            }
            if ((xCount - 1) % step != 0)
            {
                double x = MapValue(xCount - 1, 0, xCount - 1, plot.Left, plot.Right);
                dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                dc.DrawLine(axisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
                DrawText(dc, (xCount - 1).ToString(), new Point(x, plot.Bottom + 6), "center", "top");
            }
        }

        private static void DrawSeries(DrawingContext dc, int[] series, Rect plot, Brush brush)
        {
            if (series.Length <= 1) return;

            var pen = new Pen(brush, 1.5);
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (int i = 0; i < series.Length; i++)
                {
                    double x = MapValue(i, 0, series.Length - 1, plot.Left, plot.Right);
                    double y = MapValue(series[i], 0, 255, plot.Bottom, plot.Top);
                    if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                    else ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        private static void DrawText(DrawingContext dc, string text, Point anchor, string hAlign, string vAlign)
        {
            var ft = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                11,
                Brushes.Black,
                1.0);

            double x = anchor.X;
            double y = anchor.Y;

            if (hAlign == "center") x -= ft.Width / 2.0;
            else if (hAlign == "right") x -= ft.Width;

            if (vAlign == "center") y -= ft.Height / 2.0;
            else if (vAlign == "bottom") y -= ft.Height;

            dc.DrawText(ft, new Point(x, y));
        }

        private static double MapValue(double v, double inMin, double inMax, double outMin, double outMax)
            => (v - inMin) / (inMax - inMin) * (outMax - outMin) + outMin;
    }
}
