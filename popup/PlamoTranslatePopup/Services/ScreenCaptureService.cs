using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using PlamoTranslatePopup.Models;

namespace PlamoTranslatePopup.Services;

/// <summary>
/// 画面の一部をキャプチャするサービス(物理ピクセル座標で指定)
/// </summary>
public sealed class ScreenCaptureService
{
    /// <summary>指定領域をキャプチャした Bitmap を返す(呼び出し元で Dispose すること)</summary>
    public Bitmap CaptureRegion(ScreenRegion region)
    {
        if (!region.IsValid)
            throw new ArgumentException("領域が不正です", nameof(region));

        var bitmap = new Bitmap(region.Width, region.Height);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(region.X, region.Y, 0, 0, new Size(region.Width, region.Height));

        // デバッグ用: 環境変数 PLAMO_OCR_SAVE=1 でキャプチャ画像を保存する
        if (Environment.GetEnvironmentVariable("PLAMO_OCR_SAVE") == "1")
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), $"plamo-ocr-capture-{DateTime.Now:HHmmss}.png");
                bitmap.Save(path, ImageFormat.Png);
                App.Log($"OCR: キャプチャ画像保存 {path}");
            }
            catch
            {
                // 保存失敗は無視
            }
        }

        return bitmap;
    }
}
