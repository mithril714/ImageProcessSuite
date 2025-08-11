// ImageProcess.cpp : このファイルには 'main' 関数が含まれています。プログラム実行の開始と終了がそこで行われます。
//

#include <iostream>
#include <opencv2/core.hpp>
#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>
#include <iostream>
#include <string>
#include <vector>
#include <stdexcept>
#include <cmath>

struct Args {
    std::string op;         // "blur" | "edge" | "bin"
    std::string inPath;
    std::string outPath;
    double sigma = 1.5;     // blur
    int th = 128;           // bin (fixed threshold)
    bool otsu = false;      // bin
    bool inv = false;      // bin
};

static void print_usage() {
    std::cerr <<
        "MyProc - simple CLI image processor (OpenCV)\n"
        "\n"
        "Usage:\n"
        "  MyProc.exe -op blur -sigma 2 -in <input> -out <output>\n"
        "  MyProc.exe -op edge              -in <input> -out <output>\n"
        "  MyProc.exe -op bin  [-th 128] [-otsu] [-inv] -in <input> -out <output>\n"
        "\n"
        "Options:\n"
        "  -op <blur|edge|bin>\n"
        "  -sigma <float>   (blur) Gaussian sigma (default 1.5)\n"
        "  -th <0..255>     (bin)  fixed threshold (default 128)\n"
        "  -otsu            (bin)  use Otsu's method (ignore -th)\n"
        "  -inv             (bin)  invert output (black/white swap)\n"
        "  -in <path>       input image file (png/jpg/bmp/tif...)\n"
        "  -out <path>      output image file\n";
}

static std::vector<std::string> to_vec(int argc, char** argv) {
    std::vector<std::string> v;
    v.reserve(argc > 1 ? size_t(argc - 1) : 0);
    for (int i = 1; i < argc; ++i) v.emplace_back(argv[i]);
    return v;
}

static bool hasopt(const std::vector<std::string>& a, const std::string& key) {
    for (auto& s : a) if (s == key) return true;
    return false;
}

static std::string getopt(const std::vector<std::string>& a, const std::string& key, const std::string& def = "") {
    for (size_t i = 0; i + 1 < a.size(); ++i)
        if (a[i] == key) return a[i + 1];
    return def;
}

static Args parse_args(int argc, char** argv) {
    if (argc < 2) {
        print_usage();
        throw std::runtime_error("No arguments.");
    }
    auto a = to_vec(argc, argv);

    Args args;
    args.op = getopt(a, "-op");
    args.inPath = getopt(a, "-in");
    args.outPath = getopt(a, "-out");

    if (hasopt(a, "-sigma")) args.sigma = std::stod(getopt(a, "-sigma"));
    if (hasopt(a, "-th"))    args.th = std::stoi(getopt(a, "-th"));
    args.otsu = hasopt(a, "-otsu");
    args.inv = hasopt(a, "-inv");

    if (args.op.empty() || args.inPath.empty() || args.outPath.empty()) {
        print_usage();
        throw std::runtime_error("Missing required arguments.");
    }
    if (args.op != "blur" && args.op != "edge" && args.op != "bin")
        throw std::runtime_error("Unsupported -op: " + args.op);

    if (args.op == "blur") {
        if (!(args.sigma > 0.0)) throw std::runtime_error("sigma must be > 0");
    }
    else if (args.op == "bin") {
        if (!args.otsu) {
            if (args.th < 0 || args.th > 255) throw std::runtime_error("-th must be 0..255");
        }
    }
    return args;
}

static int gaussian_ksize_from_sigma(double sigma) {
    // OpenCVの経験則に近い: k = 1 + 2*ceil(3*sigma)（奇数）
    int k = 1 + 2 * static_cast<int>(std::ceil(3.0 * sigma));
    if (k % 2 == 0) ++k;
    return k;
}

static cv::Mat ensure_bgr_3ch(const cv::Mat& src) {
    if (src.channels() == 3) return src;
    cv::Mat bgr;
    if (src.channels() == 4) cv::cvtColor(src, bgr, cv::COLOR_BGRA2BGR);
    else if (src.channels() == 1) cv::cvtColor(src, bgr, cv::COLOR_GRAY2BGR);
    else throw std::runtime_error("Unsupported channel count.");
    return bgr;
}

static cv::Mat ensure_gray_1ch(const cv::Mat& src) {
    if (src.channels() == 1) return src;
    cv::Mat gray;
    if (src.channels() == 4) cv::cvtColor(src, gray, cv::COLOR_BGRA2GRAY);
    else if (src.channels() == 3) cv::cvtColor(src, gray, cv::COLOR_BGR2GRAY);
    else throw std::runtime_error("Unsupported channel count.");
    return gray;
}

int main(int argc, char** argv) {
    try {
        Args args = parse_args(argc, argv);

        // 入力読み込み（拡張子に応じて自動）
        cv::Mat src = cv::imread(args.inPath, cv::IMREAD_UNCHANGED);
        if (src.empty()) {
            std::cerr << "Failed to read image: " << args.inPath << std::endl;
            return 2;
        }

        cv::Mat out;

        if (args.op == "blur") {
            cv::Mat bgr = ensure_bgr_3ch(src);
            int ksize = gaussian_ksize_from_sigma(args.sigma);
            cv::GaussianBlur(bgr, out, cv::Size(ksize, ksize), args.sigma, args.sigma, cv::BORDER_DEFAULT);
        }
        else if (args.op == "edge") {
            cv::Mat gray = ensure_gray_1ch(src);
            // しきい値は簡易固定。必要なら引数化してください
            const double th1 = 100.0, th2 = 200.0;
            cv::Canny(gray, out, th1, th2, 3, true);
            // 可視化のために 0/255 の1ch出力（そのままでOK）
        }
        else if (args.op == "bin") {
            cv::Mat gray = ensure_gray_1ch(src);
            int type = args.inv ? cv::THRESH_BINARY_INV : cv::THRESH_BINARY;
            double thresh = args.otsu ? 0.0 : static_cast<double>(args.th);
            if (args.otsu) type |= cv::THRESH_OTSU;
            cv::threshold(gray, out, thresh, 255.0, type);
        }

        // 出力保存
        if (out.empty()) {
            std::cerr << "No output produced." << std::endl;
            return 4;
        }
        if (!cv::imwrite(args.outPath, out)) {
            std::cerr << "Failed to write: " << args.outPath << std::endl;
            return 3;
        }

        return 0; // success
    }
    catch (const std::exception& ex) {
        std::cerr << "Error: " << ex.what() << std::endl;
        return 1;
    }
    catch (...) {
        std::cerr << "Unknown error." << std::endl;
        return 99;
    }
}