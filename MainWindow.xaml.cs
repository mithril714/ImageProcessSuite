using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;


// フォルダ選択だけ WinForms を使う（衝突回避のためエイリアス）
using WinForms = System.Windows.Forms;

namespace ImageProcessSuite
{
    public partial class MainWindow : Window
    {
        // 対象拡張子
        private static readonly string[] _imgExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

        private BitmapSource _src;          // BGRA32
        private bool _hasImage = false;

        private int[] _graySeries;
        private int[] _rSeries, _gSeries, _bSeries;

        private GraphWindow _graphWin;

        // 直近の設定を覚えておくと使いやすい
        private int _lastThreshold = 128;
        private bool _lastInvert = false;
        private BinarizeMode _lastMode = BinarizeMode.Fixed;

        public MainWindow()
        { 
            InitializeComponent();
            SetUiEnabled(false);   // 起動時はオフ
        }

        private void SetUiEnabled(bool enabled)
        {
            MainToolBar.IsEnabled = enabled;
            if (MenuSaveCsv != null) MenuSaveCsv.IsEnabled = enabled; // メニュー側の保存も連動
        }


        // ====== ファイル系 ======
        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "画像|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|すべて|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                var src = LoadNoLock(dlg.FileName);
                _src = EnsureBgra32(src);
                _hasImage = true;

                PreviewImage.Source = _src;
                SetUiEnabled(true);    // 有効化
                InitIndexRange();
                DrawGuideLine();

                PreviewImage.Source = _src;
                this.Title = "Line Profile - " + System.IO.Path.GetFileName(dlg.FileName);

                InitIndexRange();
                DrawGuideLine();
                UpdateSeriesAndGraph();
            }
        }

        private void SaveCsv_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasImage) { System.Windows.MessageBox.Show("先に画像を開いてください。"); return; }
            if (_graySeries == null && _rSeries == null) { System.Windows.MessageBox.Show("先に『描画/更新』してください。"); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV|*.csv", FileName = "line_profile.csv" };
            if (dlg.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                if (_graySeries != null)
                {
                    sb.AppendLine("index,gray");
                    for (int i = 0; i < _graySeries.Length; i++) sb.AppendLine(i + "," + _graySeries[i]);
                }
                else if (_rSeries != null && _gSeries != null && _bSeries != null)
                {
                    sb.AppendLine("index,R,G,B");
                    int len = Max3(_rSeries.Length, _gSeries.Length, _bSeries.Length);
                    for (int i = 0; i < len; i++)
                        sb.AppendLine(i + "," + _rSeries[i] + "," + _gSeries[i] + "," + _bSeries[i]);
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();
        private void Help_Click(object sender, RoutedEventArgs e)
            => System.Windows.MessageBox.Show("1) ファイル→開く\n2) ToolBarでモード/インデックス設定\n3) 描画/更新\n4) グラフを開く", "使い方");

        // ====== ToolBar イベント ======

        private void ModeChanged(object sender, RoutedEventArgs e)
        {
            if (!_hasImage) return;
            InitIndexRange();
            UpdateSeriesAndGraph(); // 警告不要
        }

        // スライダー／テキスト変更／モード変更：起動時にも走る → 無音でスキップ
        private void IndexSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_hasImage) return;
            IndexBox.Text = ((int)Math.Round(e.NewValue)).ToString();
            DrawGuideLine();
        }


        private void IndexBox_TextChanged(object s, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_hasImage) return;
            int idx;
            if (int.TryParse(IndexBox.Text, out idx))
            {
                idx = Clamp(idx, (int)IndexSlider.Minimum, (int)IndexSlider.Maximum);
                if ((int)IndexSlider.Value != idx) IndexSlider.Value = idx;
            }
        }


        // 「描画/更新」ボタン：ユーザーの明示操作 → 警告OK
        private void DrawButton_Click(object sender, RoutedEventArgs e)
            => UpdateSeriesAndGraph(showWarningIfNoImage: true);
     

        private void RedrawIfPossible(object sender, RoutedEventArgs e)
        {
            if (_hasImage) UpdateSeriesAndGraph();
        }

        // グラフを開く：未読なら警告、読んでいれば表示＆更新
        private void OpenGraph_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasImage)
            {
                System.Windows.MessageBox.Show("先に画像を開いてください。", "情報",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            OpenGraphWindowIfNeeded();
            UpdateSeriesAndGraph(); // 警告不要
        }

        private void StretchToggle_Changed(object sender, RoutedEventArgs e)
        {
            var cb = sender as System.Windows.Controls.CheckBox;
            if (cb == null || PreviewImage == null) return;   // 念のため

            PreviewImage.Stretch = (cb.IsChecked == true) ? Stretch.None : Stretch.Uniform;
            if (_hasImage) DrawGuideLine();

        }

        // ====== ロジック ======
        private bool IsRowMode() { return RowRadio.IsChecked == true; }



        private void UpdateSeriesAndGraph(bool showWarningIfNoImage = false)
        {
            if (!_hasImage)
            {
                if (showWarningIfNoImage)
                    System.Windows.MessageBox.Show("先に画像を開いてください。", "情報",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }


            int idx;
            if (!int.TryParse(IndexBox.Text, out idx)) { System.Windows.MessageBox.Show("インデックスは整数で入力してください。"); return; }

            int w = _src.PixelWidth;
            int h = _src.PixelHeight;
            int stride = (w * _src.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * h];
            _src.CopyPixels(pixels, stride, 0);

            bool isRow = IsRowMode();
            idx = Clamp(idx, 0, isRow ? h - 1 : w - 1);

            if (RgbCheck.IsChecked == true)
            {
                var tup = ExtractRgbLine(pixels, w, h, stride, idx, isRow);
                _rSeries = tup.r; _gSeries = tup.g; _bSeries = tup.b; _graySeries = null;
            }
            else
            {
                _graySeries = ExtractGrayLine(pixels, w, h, stride, idx, isRow);
                _rSeries = _gSeries = _bSeries = null;
            }

            DrawGuideLine();

            if (_graphWin != null)
            {
                _graphWin.Title = isRow ? ("Graph - Row " + idx) : ("Graph - Column " + idx);
                _graphWin.SetData(_graySeries, _rSeries, _gSeries, _bSeries);
                _graphWin.Redraw();
            }
        }

        private void InitIndexRange()
        {
            if (!_hasImage) return;

            if (IsRowMode()) { IndexSlider.Minimum = 0; IndexSlider.Maximum = _src.PixelHeight - 1; }
            else { IndexSlider.Minimum = 0; IndexSlider.Maximum = _src.PixelWidth - 1; }

            int idx;
            if (!int.TryParse(IndexBox.Text, out idx)) idx = 0;
            idx = Clamp(idx, (int)IndexSlider.Minimum, (int)IndexSlider.Maximum);
            IndexBox.Text = idx.ToString();
            IndexSlider.Value = idx;
            DrawGuideLine();
        }

        private void OpenGraphWindowIfNeeded()
        {
            if (_graphWin != null && _graphWin.IsVisible) return;
            _graphWin = new GraphWindow { Owner = this };
            _graphWin.Show();
        }

        // ====== ガイド線 ======
        private void DrawGuideLine()
        {
            GuideCanvas.Children.Clear();
            if (!_hasImage) return;

            int idx;
            if (!int.TryParse(IndexBox.Text, out idx)) return;

            var src = PreviewImage.Source as BitmapSource;
            if (src == null) return;

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

            if (IsRowMode())
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
                    int i = yOff + x * 4;
                    byte B = pixels[i], G = pixels[i + 1], R = pixels[i + 2];
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
                    byte B = pixels[i], G = pixels[i + 1], R = pixels[i + 2];
                    line[y] = (int)Math.Round(0.299 * R + 0.587 * G + 0.114 * B);
                }
                return line;
            }
        }

        private static dynamic ExtractRgbLine(byte[] pixels, int w, int h, int stride, int index, bool isRow)
        {
            if (isRow)
            {
                var r = new int[w]; var g = new int[w]; var b = new int[w];
                int yOff = index * stride;
                for (int x = 0; x < w; x++)
                {
                    int i = yOff + x * 4;
                    b[x] = pixels[i];
                    g[x] = pixels[i + 1];
                    r[x] = pixels[i + 2];
                }
                return new { r, g, b };
            }
            else
            {
                var r = new int[h]; var g = new int[h]; var b = new int[h];
                for (int y = 0; y < h; y++)
                {
                    int i = y * stride + index * 4;
                    b[y] = pixels[i];
                    g[y] = pixels[i + 1];
                    r[y] = pixels[i + 2];
                }
                return new { r, g, b };
            }
        }

        // ====== Utils ======
        private static BitmapSource LoadNoLock(string path)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad; // ファイルロック回避
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        private static BitmapSource EnsureBgra32(BitmapSource src)
        {
            if (src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Pbgra32) return src;
            var f = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            f.Freeze();
            return f;
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
        private static int Max3(int a, int b, int c) { return Math.Max(a, Math.Max(b, c)); }

        private void PreviewImage_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_hasImage || _src == null) return;

            // クリック位置（Image基準）
            Point p = e.GetPosition(PreviewImage);

            int imgW = _src.PixelWidth;
            int imgH = _src.PixelHeight;

            double viewW = PreviewImage.ActualWidth;
            double viewH = PreviewImage.ActualHeight;

            double xPix, yPix;

            if (PreviewImage.Stretch == Stretch.None)
            {
                xPix = p.X; yPix = p.Y;
            }
            else
            {
                // Uniform 等に対応
                double sx = viewW / imgW;
                double sy = viewH / imgH;
                double scale = Math.Min(sx, sy);
                double drawW = imgW * scale;
                double drawH = imgH * scale;
                double offX = (viewW - drawW) / 2.0;
                double offY = (viewH - drawH) / 2.0;

                xPix = (p.X - offX) / scale;
                yPix = (p.Y - offY) / scale;
            }

            // 画像領域外なら無視
            int x = (int)Math.Floor(xPix);
            int y = (int)Math.Floor(yPix);
            if (x < 0 || y < 0 || x >= imgW || y >= imgH) return;

            // ピクセル取得（BGRA）
            var one = new byte[4];
            _src.CopyPixels(new Int32Rect(x, y, 1, 1), one, 4, 0);
            byte B = one[0], G = one[1], R = one[2];
            int gray = (int)Math.Round(0.299 * R + 0.587 * G + 0.114 * B);

            // マーカー描画
            DrawClickMarker(x, y);

            var cm = new System.Windows.Controls.ContextMenu();
            cm.Items.Add(new System.Windows.Controls.MenuItem { Header = $"X:{x}  Y:{y}", IsEnabled = false });
            cm.Items.Add(new System.Windows.Controls.Separator());
            cm.Items.Add(new System.Windows.Controls.MenuItem { Header = $"R:{R}  G:{G}  B:{B}", IsEnabled = false });
            cm.Items.Add(new System.Windows.Controls.MenuItem { Header = $"Gray:{gray}", IsEnabled = false });

            PreviewImage.ContextMenu = cm;
            cm.IsOpen = true;

            e.Handled = true;
        }

        private void DrawClickMarker(int x, int y)
        {
            GuideCanvas.Children.Clear();
            DrawGuideLine(); // 既存のガイド線を再描画

            double cx = x + 0.5;
            double cy = y + 0.5;

            var horiz = new Line
            {
                X1 = cx - 6,
                Y1 = cy,
                X2 = cx + 6,
                Y2 = cy,
                Stroke = Brushes.OrangeRed,
                StrokeThickness = 1.5
            };
            var vert = new Line
            {
                X1 = cx,
                Y1 = cy - 6,
                X2 = cx,
                Y2 = cy + 6,
                Stroke = Brushes.OrangeRed,
                StrokeThickness = 1.5
            };
            var dot = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = Brushes.OrangeRed
            };
            Canvas.SetLeft(dot, cx - 2);
            Canvas.SetTop(dot, cy - 2);

            GuideCanvas.Children.Add(horiz);
            GuideCanvas.Children.Add(vert);
            GuideCanvas.Children.Add(dot);
        }

        /* ---------- 処理メニュー（二値化ダイアログ） ---------- */

        private void Proc_BinarizeDialog_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasImage) { System.Windows.MessageBox.Show("先に画像を開いてください。"); return; }

            var dlg = new ThresholdDialog(_lastThreshold, _lastInvert, _lastMode) { Owner = this };
            var ok = dlg.ShowDialog();
            if (ok != true) return;

            _lastThreshold = dlg.Threshold;
            _lastInvert = dlg.Invert;
            _lastMode = dlg.Mode;

            string invOpt = _lastInvert ? " -inv" : "";
            string opArgs = (_lastMode == BinarizeMode.Fixed)
                ? $"-op bin -th {_lastThreshold}{invOpt}"
                : $"-op bin -otsu{invOpt}";

            RunExternalProc(opArgs, true);
        }

        /* ---------- 処理実行（方法1：アプリ基準） ---------- */
        private void RunExternalProc(string opArgs, bool overwritePreview)
        {
            string exePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ImageProcess.exe");

            string tmpIn = System.IO.Path.GetTempFileName() + ".png";
            string tmpOut = System.IO.Path.GetTempFileName() + ".png";
            SaveBitmap(_src, tmpIn);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"{opArgs} -in \"{tmpIn}\" -out \"{tmpOut}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            try
            {
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    if (p.ExitCode != 0 || !File.Exists(tmpOut))
                    {
                        string err = p.StandardError.ReadToEnd();
                        System.Windows.MessageBox.Show("処理に失敗しました。\n" + err, "エラー",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                if (overwritePreview)
                {
                    var processed = LoadNoLock(tmpOut);
                    _src = EnsureBgra32(processed);
                    _hasImage = true;
                    PreviewImage.Source = _src;
                    InitIndexRange();
                    DrawGuideLine();
                    UpdateSeriesAndGraph();
                }
            }
            finally
            {
                TryDelete(tmpIn); TryDelete(tmpOut);
            }
        }

        // 固定シグマで blur 実行（出力でプレビューを上書き）
        private void Proc_Blur_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasImage) { System.Windows.MessageBox.Show("先に画像を開いてください。"); return; }

            double sigma = 2.0; // 必要なら後でダイアログ化
                                // MyProc.exe 側は: -op blur -sigma <float> -in ... -out ...
            RunExternalProc($"-op blur -sigma {sigma}", overwritePreview: true);
        }

        /* ========== バッチ処理 ========== */
        private string PickFolder(string title)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = title;
                dlg.ShowNewFolderButton = true;
                var r = dlg.ShowDialog();
                return r == WinForms.DialogResult.OK ? dlg.SelectedPath : null;
            }
        }

        private int RunOneExternal(string inPath, string outPath, string opArgs)
        {
            string exePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ImageProcess.exe");
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"{opArgs} -in \"{inPath}\" -out \"{outPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    string err = p.StandardError.ReadToEnd();
                    Debug.WriteLine($"[MyProc] exit={p.ExitCode} err={err}");
                }
                return p.ExitCode;
            }
        }

        private void BatchRunCore(string opArgs)
        {
            string inDir = PickFolder("入力フォルダを選択");
            if (string.IsNullOrEmpty(inDir)) return;

            string outDir = PickFolder("出力フォルダを選択（存在しなければ作成）");
            if (string.IsNullOrEmpty(outDir)) return;
            Directory.CreateDirectory(outDir);

            var exts = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };
            var files = Directory.EnumerateFiles(inDir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(p => exts.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant()))
                .ToList();
            if (files.Count == 0) { System.Windows.MessageBox.Show("対象画像が見つかりませんでした。"); return; }

            int ok = 0, ng = 0;
            foreach (var inPath in files)
            {
                string outPath = System.IO.Path.Combine(outDir, System.IO.Path.GetFileName(inPath)); // 拡張子そのまま
                int code = RunOneExternal(inPath, outPath, opArgs);
                if (code == 0) ok++; else ng++;
            }
            System.Windows.MessageBox.Show($"完了: {ok} 件 / 失敗: {ng} 件\n出力: {outDir}", "バッチ処理");
        }

        // バッチ：二値化／ぼかし／エッジ
        private void Batch_Binarize_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ThresholdDialog(_lastThreshold, _lastInvert, _lastMode) { Owner = this };
            var ok = dlg.ShowDialog();
            if (ok != true) return;

            _lastThreshold = dlg.Threshold;
            _lastInvert = dlg.Invert;
            _lastMode = dlg.Mode;

            string invOpt = _lastInvert ? " -inv" : "";
            string opArgs = (_lastMode == BinarizeMode.Fixed)
                ? $"-op bin -th {_lastThreshold}{invOpt}"
                : $"-op bin -otsu{invOpt}";
            BatchRunCore(opArgs);
        }
        private void Batch_Blur_Click(object sender, RoutedEventArgs e)
        {
/*            string s = Microsoft.VisualBasic.Interaction.InputBox("Gaussian σ を入力（例: 2.0）", "ぼかし", "2.0");
            if (string.IsNullOrWhiteSpace(s)) return;
            double sigma;
            if (!double.TryParse(s, out sigma) || sigma <= 0) { MessageBox.Show("σ は正の数で入力してください。"); return; }
            BatchRunCore($"-op blur -sigma {sigma}");
*/        }

        private static void SaveBitmapSourceAsPng(BitmapSource src, string path)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using (var fs = new FileStream(path, FileMode.Create))
            {
                enc.Save(fs);
            }

        }

        private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasImage || _src == null) { System.Windows.MessageBox.Show("先に画像を開くか処理してください。"); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "名前を付けて保存",
                Filter = "PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|BMP (*.bmp)|*.bmp|TIFF (*.tif;*.tiff)|*.tif;*.tiff",
                FileName = "processed.png"
            };
            if (dlg.ShowDialog() == true)
            {
                SaveBitmap(_src, dlg.FileName);
            }
        }

        private static void SaveBitmap(BitmapSource src, string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            BitmapEncoder enc;

            switch (ext)
            {
                case ".png":
                    enc = new PngBitmapEncoder();
                    break;
                case ".jpg":
                case ".jpeg":
                    enc = new JpegBitmapEncoder { QualityLevel = 95 }; // 必要なら変更
                    break;
                case ".bmp":
                    enc = new BmpBitmapEncoder();
                    break;
                case ".tif":
                case ".tiff":
                    var tiff = new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw };
                    enc = tiff;
                    break;
                default:
                    // 未対応拡張子ならPNGで保存
                    enc = new PngBitmapEncoder();
                    path = path + ".png";
                    break;
            }

            enc.Frames.Add(BitmapFrame.Create(src));
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                enc.Save(fs);
            }
        }
    }
}