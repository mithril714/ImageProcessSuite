using System;
using System.Collections.Generic;
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
using Microsoft.Win32;
using System.IO;
using System.Globalization; // 目盛ラベル描画で使用

namespace ImageProcessSuite
{
    /// <summary>
    /// Window2.xaml の相互作用ロジック
    /// </summary>

    public static class MathEx
    {
        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
    public partial class Window2 : Window
    {

        private BitmapSource _src;           // BGRA32で保持
        private int[] _graySeries;
        private int[] _rSeries, _gSeries, _bSeries;

        private GraphWindow _graphWin;       // 別ウィンドウ

        public Window2(BitmapSource source, string titleSuffix = null)
        {
            InitializeComponent();

            _src = EnsureBgra32(source);
            PreviewImage.Source = _src;

            if (!string.IsNullOrEmpty(titleSuffix))
                this.Title = $"Line Profile - {titleSuffix}";

            InitIndexRange();
            DrawGuideLine(); // 初回ガイド

            // 必要なら起動と同時にグラフウィンドウも開く
            // OpenGraphWindowIfNeeded();
        }

        // ====== UI ======

        private void OpenGraphButton_Click(object sender, RoutedEventArgs e)
        {
            OpenGraphWindowIfNeeded();
            UpdateSeriesAndGraph(); // データ計算＆描画
        }

        private void DrawButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateSeriesAndGraph();
        }

        private void SaveCsvButton_Click(object sender, RoutedEventArgs e)
        {
            if (_graySeries == null && _rSeries == null)
            {
                System.Windows.MessageBox.Show("まず『描画/更新』でデータを作成してください。", "情報",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "CSVファイル|*.csv",
                FileName = "line_profile.csv"
            };
            if (dlg.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                if (_graySeries != null)
                {
                    sb.AppendLine("index,gray");
                    for (int i = 0; i < _graySeries.Length; i++)
                        sb.AppendLine($"{i},{_graySeries[i]}");
                }
                else if (_rSeries != null && _gSeries != null && _bSeries != null)
                {
                    sb.AppendLine("index,R,G,B");
                    int len = Math.Max(_rSeries.Length, Math.Max(_gSeries.Length, _bSeries.Length));
                    for (int i = 0; i < len; i++)
                        sb.AppendLine($"{i},{_rSeries[i]}:{_gSeries[i]}:{_bSeries[i]}".Replace(':', ','));
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            }
        }

        private void ModeChanged(object sender, RoutedEventArgs e) => InitIndexRange();

        private void IndexSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int idx = (int)Math.Round(e.NewValue);
            IndexBox.Text = idx.ToString();
            DrawGuideLine();
        }

        private void RedrawIfPossible(object sender, RoutedEventArgs e)
        {
            if (_src == null) return;
            UpdateSeriesAndGraph();
        }

        // ====== ロジック ======

        private void UpdateSeriesAndGraph()
        {
            if (_src == null)
            {
                System.Windows.MessageBox.Show("画像が未設定です。", "情報",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!int.TryParse(IndexBox.Text, out int idx))
            {
                System.Windows.MessageBox.Show("インデックスは整数で入力してください。", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            int w = _src.PixelWidth;
            int h = _src.PixelHeight;
            int stride = (w * _src.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * h];
            _src.CopyPixels(pixels, stride, 0);

            bool isRow = RowRadio.IsChecked == true;

            if (isRow && (idx < 0 || idx >= h))
            {
                System.Windows.MessageBox.Show($"行インデックスは 0～{h - 1} の範囲です。", "範囲外",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!isRow && (idx < 0 || idx >= w))
            {
                System.Windows.MessageBox.Show($"列インデックスは 0～{w - 1} の範囲です。", "範囲外",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (RgbCheck.IsChecked == true)
            {
                (_rSeries, _gSeries, _bSeries) = ExtractRgbLine(pixels, w, h, stride, idx, isRow);
                _graySeries = null;
            }
            else
            {
                _graySeries = ExtractGrayLine(pixels, w, h, stride, idx, isRow);
                _rSeries = _gSeries = _bSeries = null;
            }

            // ガイド線更新
            DrawGuideLine();

            // グラフウィンドウが開いていれば反映
            if (_graphWin != null)
            {
                _graphWin.Title = isRow ? $"Graph - Row {idx}" : $"Graph - Column {idx}";
                _graphWin.SetData(_graySeries, _rSeries, _gSeries, _bSeries);
                _graphWin.Redraw();
            }
        }


        private void InitIndexRange()
        {
            if (_src == null) return;
            if (RowRadio.IsChecked == true)
            {
                IndexSlider.Minimum = 0;
                IndexSlider.Maximum = _src.PixelHeight - 1;
            }
            else
            {
                IndexSlider.Minimum = 0;
                IndexSlider.Maximum = _src.PixelWidth - 1;
            }

            if (!int.TryParse(IndexBox.Text, out var idx)) idx = 0;
            idx = (int)MathEx.Clamp(idx, IndexSlider.Minimum, IndexSlider.Maximum);
            IndexBox.Text = idx.ToString();
            IndexSlider.Value = idx;

            DrawGuideLine();
        }

        private void OpenGraphWindowIfNeeded()
        {
            if (_graphWin != null && _graphWin.IsVisible) return;

            _graphWin = new GraphWindow
            {
                Owner = this
            };
            _graphWin.Show();
        }

        // ====== ガイド線 ======

        private void DrawGuideLine()
        {
            GuideCanvas.Children.Clear();

            var src = PreviewImage.Source as BitmapSource;
            if (src == null) return;
            if (!int.TryParse(IndexBox.Text, out int idx)) return;

            double imgW = src.PixelWidth;
            double imgH = src.PixelHeight;

            GuideCanvas.Width = imgW;
            GuideCanvas.Height = imgH;

            var line = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(200, 30, 144, 255)),
                StrokeThickness = 1.5,
                SnapsToDevicePixels = true
            };

            if (RowRadio.IsChecked == true)
            {
                line.X1 = 0; line.Y1 = idx + 0.5;
                line.X2 = imgW; line.Y2 = idx + 0.5;
            }
            else
            {
                line.X1 = idx + 0.5; line.Y1 = 0;
                line.X2 = idx + 0.5; line.Y2 = imgH;
            }

            GuideCanvas.Children.Add(line);
        }

        // ====== データ抽出 ======

        private static int[] ExtractGrayLine(byte[] pixels, int w, int h, int stride, int index, bool isRow)
        {
            if (isRow)
            {
                var line = new int[w];
                int yOff = index * stride;
                for (int x = 0; x < w; x++)
                {
                    int i = yOff + x * 4; // BGRA
                    byte B = pixels[i + 0], G = pixels[i + 1], R = pixels[i + 2];
                    line[x] = (int)Math.Round(0.299 * R + 0.587 * G + 0.114 * B);
                }
                return line;
            }
            else
            {
                var line = new int[h];
                for (int y = 0; y < h; y++)
                {
                    int i = y * stride + index * 4;
                    byte B = pixels[i + 0], G = pixels[i + 1], R = pixels[i + 2];
                    line[y] = (int)Math.Round(0.299 * R + 0.587 * G + 0.114 * B);
                }
                return line;
            }
        }

        private static (int[] r, int[] g, int[] b) ExtractRgbLine(byte[] pixels, int w, int h, int stride, int index, bool isRow)
        {
            if (isRow)
            {
                var r = new int[w]; var g = new int[w]; var b = new int[w];
                int yOff = index * stride;
                for (int x = 0; x < w; x++)
                {
                    int i = yOff + x * 4;
                    b[x] = pixels[i + 0];
                    g[x] = pixels[i + 1];
                    r[x] = pixels[i + 2];
                }
                return (r, g, b);
            }
            else
            {
                var r = new int[h]; var g = new int[h]; var b = new int[h];
                for (int y = 0; y < h; y++)
                {
                    int i = y * stride + index * 4;
                    b[y] = pixels[i + 0];
                    g[y] = pixels[i + 1];
                    r[y] = pixels[i + 2];
                }
                return (r, g, b);
            }
        }

        // ====== 画像ユーティリティ ======

        private static BitmapSource EnsureBgra32(BitmapSource src)
        {
            if (src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Pbgra32) return src;
            var f = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            f.Freeze();
            return f;
        }

    }
}
