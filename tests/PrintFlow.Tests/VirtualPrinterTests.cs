using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// الطابعة الوهمية: الأسامي والمسارات، ومتابعة الملف اللي بيتكتب.
///
/// أهم تستات هنا هي بتاعة <see cref="FileWatch"/> — لأن الغلط فيها بيطلّع
/// نص ملزمة على إنها ملزمة كاملة، وده أسوأ عطل ممكن في الميزة دي:
/// بيبان زي النجاح بالظبط لحد ما حد يعدّ الورق.
/// </summary>
public class VirtualPrinterTests
{
    private const string ProgramData = @"C:\ProgramData";

    // ══════════ الأسامي والمسارات ══════════

    [Fact]
    public void The_Printer_Is_Called_PrintFlow()
    {
        Assert.Equal("PrintFlow", VirtualPrinter.PrinterName);
    }

    [Fact]
    public void The_Driver_Is_One_Windows_Already_Has()
    {
        // لو الاسم ده اتغيّر، إحنا بنسطّب درايفر — وده كل اللي عايزين
        // نتجنّبه (توقيع، تكلفة، واتجاه مايكروسوفت لإنهاء الدرايفرات الخارجية)
        Assert.Equal("Microsoft Print To PDF", VirtualPrinter.DriverName);
    }

    [Fact]
    public void Everything_Lives_Under_ProgramData_Not_Temp()
    {
        // خدمة الطباعة بتشتغل بحساب SYSTEM، وممكن ماتكونش لها صلاحية
        // على مجلد TEMP بتاع المستخدم
        Assert.StartsWith(ProgramData, VirtualPrinter.SpoolFolder(ProgramData));
        Assert.StartsWith(ProgramData, VirtualPrinter.QueueFolder(ProgramData));
        Assert.StartsWith(ProgramData, VirtualPrinter.PortPath(ProgramData));
    }

    [Fact]
    public void The_Port_Is_A_File_Inside_The_Spool_Folder()
    {
        Assert.Equal(
            Path.Combine(VirtualPrinter.SpoolFolder(ProgramData), VirtualPrinter.PortFileName),
            VirtualPrinter.PortPath(ProgramData));
    }

    [Fact]
    public void The_Queue_Is_Not_The_Spool_Folder()
    {
        // لازم يكونوا مجلدين مختلفين: البورت بيكتب في الأول، وإحنا بننقل
        // للتاني. لو كانوا واحد، النقل كان هيدهس نفسه.
        Assert.NotEqual(VirtualPrinter.SpoolFolder(ProgramData), VirtualPrinter.QueueFolder(ProgramData));
    }

    // ══════════ أسامي الطابور ══════════

    [Fact]
    public void A_Queue_Name_Carries_Its_Time_So_The_Order_Is_Visible()
    {
        string name = VirtualPrinter.QueueNameFor(new DateTime(2026, 8, 24, 14, 52, 3), 1);

        Assert.Equal("job_20260824_145203_001.pdf", name);
    }

    [Fact]
    public void Two_Jobs_In_The_Same_Second_Do_Not_Collide()
    {
        var moment = new DateTime(2026, 8, 24, 14, 52, 3);

        Assert.NotEqual(
            VirtualPrinter.QueueNameFor(moment, 1),
            VirtualPrinter.QueueNameFor(moment, 2));
    }

    [Fact]
    public void Queue_Names_Sort_In_Arrival_Order()
    {
        var names = new[]
        {
            VirtualPrinter.QueueNameFor(new DateTime(2026, 8, 24, 9, 0, 0), 1),
            VirtualPrinter.QueueNameFor(new DateTime(2026, 8, 24, 14, 52, 3), 1),
            VirtualPrinter.QueueNameFor(new DateTime(2026, 8, 24, 14, 52, 3), 2),
            VirtualPrinter.QueueNameFor(new DateTime(2026, 8, 25, 8, 0, 0), 1)
        };

        Assert.Equal(names, names.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void A_Silly_Sequence_Number_Does_Not_Break_The_Name()
    {
        Assert.EndsWith(".pdf", VirtualPrinter.QueueNameFor(DateTime.MinValue, -5));
        Assert.EndsWith(".pdf", VirtualPrinter.QueueNameFor(DateTime.MinValue, 99999));
    }

    // ══════════ أوامر التسطيب ══════════

    [Fact]
    public void The_Install_Commands_Make_The_Folders_Before_The_Port()
    {
        // Add-PrinterPort على مسار مجلده مش موجود بيفشل
        var commands = VirtualPrinter.InstallCommands(ProgramData);

        int folder = commands.ToList().FindIndex(c => c.Contains("New-Item"));
        int port = commands.ToList().FindIndex(c => c.Contains("Add-PrinterPort"));

        Assert.True(folder >= 0 && port > folder, "المجلدات لازم تتعمل قبل البورت");
    }

    [Fact]
    public void The_Install_Commands_Make_The_Port_Before_The_Printer()
    {
        var commands = VirtualPrinter.InstallCommands(ProgramData).ToList();

        Assert.True(
            commands.FindIndex(c => c.Contains("Add-PrinterPort")) <
            commands.FindIndex(c => c.Contains("Add-Printer ")),
            "البورت لازم يتعمل قبل الطابعة");
    }

    [Fact]
    public void Uninstall_Removes_The_Printer_Before_The_Port()
    {
        // بورت مربوط بطابعة مابيتشالش
        var commands = VirtualPrinter.UninstallCommands(ProgramData).ToList();

        Assert.True(
            commands.FindIndex(c => c.Contains("Remove-Printer")) <
            commands.FindIndex(c => c.Contains("Remove-PrinterPort")));
    }

    // ══════════ متابعة الملف وهو بيتكتب ══════════

    [Fact]
    public void A_File_Still_Growing_Is_Never_Taken()
    {
        var watch = FileWatch.Start
            .Observe(1000)
            .Observe(5000)
            .Observe(20000);

        Assert.False(watch.IsSettled(IncomingWatchPolicy.StableTicksNeeded));
        Assert.True(watch.IsGrowing);
    }

    [Fact]
    public void A_File_That_Stopped_Growing_Is_Taken()
    {
        var watch = FileWatch.Start
            .Observe(20000)
            .Observe(20000)
            .Observe(20000)
            .Observe(20000);

        Assert.True(watch.IsSettled(IncomingWatchPolicy.StableTicksNeeded));
    }

    [Fact]
    public void A_Pause_In_The_Middle_Does_Not_Count_As_Finished()
    {
        // أخطر حالة: الكتابة بتقف لحظة (المستند بيتحضّر) وبعدين بتكمّل.
        // لو خطفنا الملف في الوقفة دي، نص الملزمة بس هيتطبع.
        var watch = FileWatch.Start
            .Observe(10000)
            .Observe(10000)      // وقفة
            .Observe(45000)      // كمّل
            .Observe(45000);

        Assert.False(watch.IsSettled(3));
    }

    [Fact]
    public void The_Counter_Resets_The_Moment_The_Size_Changes()
    {
        var watch = FileWatch.Start
            .Observe(500).Observe(500).Observe(500)
            .Observe(900);

        Assert.Equal(0, watch.StableTicks);
        Assert.Equal(900, watch.LastSize);
    }

    [Fact]
    public void An_Empty_File_Is_Never_Taken_No_Matter_How_Long_It_Sits()
    {
        // ويندوز بيعمل الملف فاضي الأول وبعدين يملاه. ملف صفر بايت ثابت
        // معناه "لسه مابدأش" مش "خلص".
        var watch = FileWatch.Start;

        for (int i = 0; i < 50; i++)
        {
            watch = watch.Observe(0);
        }

        Assert.False(watch.IsSettled(3));
    }

    [Fact]
    public void The_First_Reading_Is_Never_Settled()
    {
        Assert.False(FileWatch.Start.Observe(50000).IsSettled(3));
    }

    [Fact]
    public void Nothing_Seen_Yet_Is_Not_Growing()
    {
        Assert.False(FileWatch.Start.IsGrowing);
    }

    [Fact]
    public void More_Ticks_Needed_Means_More_Patience()
    {
        var watch = FileWatch.Start.Observe(100).Observe(100).Observe(100);

        Assert.True(watch.IsSettled(2));
        Assert.False(watch.IsSettled(5));
    }

    // ══════════ الملف الوارد ══════════

    [Fact]
    public void An_Incoming_File_Says_Where_It_Came_From()
    {
        var fromPrinter = new IncomingFile(@"C:\x\job_1.pdf", IncomingSource.VirtualPrinter, 100);
        var fromFolder = new IncomingFile(@"C:\x\job_1.pdf", IncomingSource.HotFolder, 100);

        Assert.Contains("طابعة", fromPrinter.SourceLabel);
        Assert.Contains("المجلد", fromFolder.SourceLabel);
    }
}
