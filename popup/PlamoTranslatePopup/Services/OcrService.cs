using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace PlamoTranslatePopup.Services;

/// <summary>
/// Windows 内蔵 OCR (Windows.Media.Ocr) による画像の文字認識。
/// 日本語認識エンジンを優先し、無ければ英語、それも無ければユーザー言語を使用する。
/// </summary>
public sealed class OcrService
{
    private readonly OcrEngine? _engine;

    public OcrService()
    {
        _engine = OcrEngine.TryCreateFromLanguage(new Language("ja"))
                  ?? OcrEngine.TryCreateFromLanguage(new Language("en"))
                  ?? OcrEngine.TryCreateFromUserProfileLanguages();
    }

    /// <summary>OCR エンジンが利用可能か(言語パックがインストールされていない環境では false)</summary>
    public bool IsAvailable => _engine != null;

    /// <summary>画像からテキストを抽出する</summary>
    public async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        if (_engine is null)
            throw new InvalidOperationException("OCR エンジンが利用できません");

        // Windows OCR の寸法上限を超える場合は縮小する
        using var scaled = ScaleIfNeeded(bitmap);

        // 一時ファイル経由で WinRT の BitmapDecoder に渡す
        var tempPath = Path.Combine(Path.GetTempPath(), $"plamo-ocr-{Guid.NewGuid():N}.png");
        scaled.Save(tempPath, ImageFormat.Png);
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(tempPath);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);

            using var software = await decoder.GetSoftwareBitmapAsync();
            var input = software.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                ? software
                : SoftwareBitmap.Convert(software, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            try
            {
                var result = await _engine.RecognizeAsync(input);
                return (result.Text ?? string.Empty).Trim();
            }
            finally
            {
                if (!ReferenceEquals(input, software))
                    input.Dispose();
            }
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private static Bitmap ScaleIfNeeded(Bitmap source)
    {
        var max = (int)OcrEngine.MaxImageDimension;
        if (source.Width <= max && source.Height <= max)
            return source;

        var scale = Math.Min((double)max / source.Width, (double)max / source.Height);
        var resized = new Bitmap((int)(source.Width * scale), (int)(source.Height * scale));
        using var g = Graphics.FromImage(resized);
        g.DrawImage(source, 0, 0, resized.Width, resized.Height);
        return resized;
    }
}
