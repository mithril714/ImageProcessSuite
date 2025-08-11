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

namespace ImageProcessSuite
{
    public enum BinarizeMode { Fixed, Otsu }
    /// <summary>
    /// ThresholdDialog.xaml の相互作用ロジック
    /// </summary>
    public partial class ThresholdDialog : Window
    {
        public BinarizeMode Mode { get; private set; } = BinarizeMode.Fixed;
        public int Threshold { get; private set; } = 128; // Fixedのとき有効
        public bool Invert { get; private set; } = false;
      
        public ThresholdDialog(int initial = 128, bool invert = false, BinarizeMode mode = BinarizeMode.Fixed)
        {
            InitializeComponent();

            // 初期値反映
            Threshold = Clamp(initial, 0, 255);
            Invert = invert;
            Mode = mode;

            ValueBox.Text = Threshold.ToString();
            ValueSlider.Value = Threshold;
            InvertCheck.IsChecked = Invert;
            ModeTabs.SelectedIndex = (Mode == BinarizeMode.Fixed) ? 0 : 1;

            // イベント
            ValueBox.TextChanged += ValueBox_TextChanged;
            ValueSlider.ValueChanged += ValueSlider_ValueChanged;

            Loaded += (s, e) =>
            {
                if (Mode == BinarizeMode.Fixed)
                {
                    ValueBox.Focus();
                    ValueBox.SelectAll();
                }
                else
                {
                    InvertCheck.Focus();
                }
            };
        }

        private void ModeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModeTabs.SelectedIndex == 0) Mode = BinarizeMode.Fixed;
            else Mode = BinarizeMode.Otsu;
        }

        private void ValueBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            int v;
            if (!int.TryParse(ValueBox.Text, out v)) return;
            v = Clamp(v, 0, 255);
            Threshold = v;
            if ((int)ValueSlider.Value != v) ValueSlider.Value = v;
        }

        private void ValueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int v = (int)Math.Round(e.NewValue);
            Threshold = v;
            if (ValueBox.Text != v.ToString()) ValueBox.Text = v.ToString();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // 最終値の確定
            int v;
            if (!int.TryParse(ValueBox.Text, out v)) v = Threshold;
            Threshold = Clamp(v, 0, 255);
            Invert = (InvertCheck.IsChecked == true);
            DialogResult = true;
            Close();
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
