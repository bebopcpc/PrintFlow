using PdfSharp.Fonts;

namespace PrintFlow.Infrastructure;

/// <summary>
/// PDFsharp 6+ بيحتاج مصدر خطوط صريح بدل الاعتماد التلقائي على Windows.
/// بيقرأ خط Arial مباشرة من مجلد خطوط النظام.
/// </summary>
public class AppFontResolver : IFontResolver
{
    public byte[] GetFont(string faceName)
    {
        string path = faceName switch
        {
            "Arial#b" => @"C:\Windows\Fonts\arialbd.ttf",
            _ => @"C:\Windows\Fonts\arial.ttf"
        };

        return File.ReadAllBytes(path);
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        return new FontResolverInfo(isBold ? "Arial#b" : "Arial#");
    }
}