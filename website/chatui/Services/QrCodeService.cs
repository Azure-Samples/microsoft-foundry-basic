using QRCoder;

namespace chatui.Services;

/// <summary>
/// Renders text (join URLs) to base64 PNG data URLs so the client can display
/// a QR code without a second round-trip. Backend generation lets email and
/// print flows work without JS while still being fast for the mobile UI.
/// </summary>
public sealed class QrCodeService
{
    public string GenerateDataUrl(string content, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var pngBytes = new PngByteQRCode(data).GetGraphic(pixelsPerModule);
        return $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";
    }
}
