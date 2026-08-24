using System.Windows;
using PrintFlow.Infrastructure;

namespace PrintFlow.UI;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // لازم قبل أي XFont يتعمل في البرنامج. من غيرها PdfSharp بيرمي
        // "No appropriate font found" أول ما نحاول نرسم أي نص على PDF.
        PdfFonts.Register();
    }
}
