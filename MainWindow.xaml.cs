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
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Forms;
using System.IO;

namespace ImageProcessSuite
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            // OpenFileDialogオブジェクトの生成
            System.Windows.Forms.OpenFileDialog od = new System.Windows.Forms.OpenFileDialog();
            od.Title = "ファイルを開く";  //ダイアログ名
            od.FileName = @"sample.bmp";  //初期選択ファイル名
            od.Filter = "画像ファイル(*.bmp)|*.bmp|すべてのファイル(*.*)|*.*";  //選択できる拡張子
            od.FilterIndex = 1;  //初期の拡張子

            // ダイアログを表示する
            DialogResult result = od.ShowDialog();


            // 選択後の判定
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                //「開く」ボタンクリック時の処理
                string fileName = od.FileName;  //これで選択したファイルパスを取得できる
                textBox_1.Text = fileName;

            }
            else if (result == System.Windows.Forms.DialogResult.Cancel)
            {
                //「キャンセル」ボタンクリック時の処理
            }

        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {

            // OpenFileDialogオブジェクトの生成
            System.Windows.Forms.OpenFileDialog od = new System.Windows.Forms.OpenFileDialog();
            od.Title = "ファイルを開く";  //ダイアログ名
            od.FileName = @"sample.bmp";  //初期選択ファイル名
            od.Filter = "画像ファイル(*.bmp)|*.bmp|すべてのファイル(*.*)|*.*";  //選択できる拡張子
            od.FilterIndex = 1;  //初期の拡張子

            // ダイアログを表示する
            DialogResult result = od.ShowDialog();


            // 選択後の判定
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                //「開く」ボタンクリック時の処理
                string fileName = od.FileName;  //これで選択したファイルパスを取得できる
                textBox_2.Text = fileName;

            }
            else if (result == System.Windows.Forms.DialogResult.Cancel)
            {
                //「キャンセル」ボタンクリック時の処理
            }

        }

        private void Button_Click_5(object sender, RoutedEventArgs e)
        {
            string path = textBox_1.Text;

            var src = LoadNoLock(path);
            var win = new Window2(src, System.IO.Path.GetFileName(path))
            {
                Owner = this
            };
            win.Show(); // モードレスで開く
//            var win = new ImageProcessSuite.Window2() { Owner = this };
//            win.Show();          // モードレス
                                 // win.ShowDialog(); // モーダルにしたいならこちら
        }
        private static BitmapSource LoadNoLock(string path)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad; // 読み込み後にロックしない
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {

        }
    }
}
