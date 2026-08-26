using PrintFlow.Domain;
using PrintFlow.Presentation;

namespace PrintFlow.Tests;

/// <summary>
/// طابعة PrintFlow الوهمية ماينفعش تبقى هدف للطباعة.
///
/// ═══ ليه ═══
///
/// الطابعة الوهمية بتكتب في مجلد البرنامج، والبرنامج بيراقب المجلد ده.
/// لو البرنامج طبع عليها، الجوب بيرجعله تاني — وفي وضع الطباعة التلقائية
/// بتبقى حلقة لا نهائية بتاكل القرص.
///
/// وده مش احتمال نظري: بعد ما الطابعة اتسطّبت على جهاز المستخدم بقت
/// **الافتراضية** على ويندوز، والبرنامج بيبدأ على الافتراضية — فبقت
/// هدف الطباعة من غير ما حد يختارها.
/// </summary>
public class VirtualPrinterTargetTests
{
    private static PrinterItem Make(string name, PrinterStatus status = PrinterStatus.Ready, bool isDefault = false)
        => new(new Printer { Name = name, Status = status, IsDefault = isDefault, Port = "USB001" });

    [Fact]
    public void The_PrintFlow_Printer_Is_Never_Eligible()
    {
        var virtualPrinter = Make(VirtualPrinter.PrinterName);

        Assert.True(virtualPrinter.IsVirtualPrintFlow);
        Assert.False(virtualPrinter.IsEligible);
    }

    [Fact]
    public void Being_The_Windows_Default_Does_Not_Make_It_Eligible()
    {
        // الحالة اللي حصلت فعلًا: التسطيب خلاها الافتراضية على الجهاز
        var virtualPrinter = Make(VirtualPrinter.PrinterName, PrinterStatus.Ready, isDefault: true);

        Assert.False(virtualPrinter.IsEligible);
    }

    [Fact]
    public void The_Name_Match_Ignores_Case()
    {
        Assert.True(Make("printflow").IsVirtualPrintFlow);
        Assert.True(Make("PRINTFLOW").IsVirtualPrintFlow);
    }

    [Fact]
    public void A_Real_Printer_With_A_Similar_Name_Is_Still_Fine()
    {
        // مانقفلش طابعة حقيقية بالغلط عشان اسمها فيه الكلمة
        Assert.False(Make("PrintFlow HP LaserJet").IsVirtualPrintFlow);
        Assert.True(Make("PrintFlow HP LaserJet").IsEligible);
    }

    [Fact]
    public void Real_Printers_Keep_Working_Normally()
    {
        Assert.True(Make("HP LaserJet Professional P1102").IsEligible);
        Assert.False(Make("HP LaserJet", PrinterStatus.Offline).IsEligible);
        Assert.False(Make("HP LaserJet", PrinterStatus.Error).IsEligible);
    }

    [Fact]
    public void The_List_Says_Why_It_Cannot_Be_Chosen()
    {
        // من غير الشرح ده، المستخدم هيشوفها رمادية ومش هيعرف ليه
        string text = Make(VirtualPrinter.PrinterName, isDefault: true).DisplayText;

        Assert.Contains("طابعة الاستقبال", text);
        Assert.Contains("مش هدف للطباعة", text);
    }

    [Fact]
    public void A_Real_Printer_Still_Shows_Its_Status_And_Port()
    {
        string text = Make("HP LaserJet", isDefault: true).DisplayText;

        Assert.Contains("HP LaserJet", text);
        Assert.Contains("جاهزة", text);
        Assert.Contains("افتراضية", text);
    }
}
