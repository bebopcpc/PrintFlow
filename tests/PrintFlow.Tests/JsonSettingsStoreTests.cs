using PrintFlow.Domain;
using PrintFlow.Infrastructure;

namespace PrintFlow.Tests;

/// <summary>
/// المبدأ اللي بنختبره هنا: البرنامج مايقفش عشان ملف إعدادات.
/// أي مشكلة في القراءة بترجّع الافتراضي بدل ما ترمي استثناء.
/// </summary>
public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _folder;
    private readonly JsonSettingsStore _store;

    public JsonSettingsStoreTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "PrintFlowStore_" + Guid.NewGuid().ToString("N"));
        _store = new JsonSettingsStore(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // تنضيف بعد التست
        }

        GC.SuppressFinalize(this);
    }

    // ══════════ تفضيلات البرنامج ══════════

    [Fact]
    public void Missing_File_Gives_Defaults_Not_A_Crash()
    {
        var settings = _store.Load();

        Assert.Equal(10, settings.PrinterRefreshSeconds);
        Assert.False(settings.WatermarkEnabled);
    }

    [Fact]
    public void App_Settings_Survive_A_Save_And_Load()
    {
        var original = new AppSettings
        {
            WatermarkEnabled = true,
            WatermarkText = "مطبعة النور الحديثة",
            WatermarkColorHex = "#C0392B",
            WatermarkFontFamily = "Times New Roman",
            WatermarkOpacityPercent = 35,
            WatermarkRotationDegrees = -30,
            PageNumberPosition = ContentPosition.TopCenter,
            PageNumberFontSize = 18,
            CountingMethod = CountingMethod.BySheet,
            FileSortOrder = FileSortOrder.ByPageCount,
            DefaultPrinterName = "طابعة المكتب",
            PrinterRefreshSeconds = 25
        };

        _store.Save(original);
        var loaded = _store.Load();

        Assert.True(loaded.WatermarkEnabled);
        Assert.Equal("مطبعة النور الحديثة", loaded.WatermarkText);
        Assert.Equal("#C0392B", loaded.WatermarkColorHex);
        Assert.Equal("Times New Roman", loaded.WatermarkFontFamily);
        Assert.Equal(35, loaded.WatermarkOpacityPercent);
        Assert.Equal(-30, loaded.WatermarkRotationDegrees);
        Assert.Equal(ContentPosition.TopCenter, loaded.PageNumberPosition);
        Assert.Equal(18, loaded.PageNumberFontSize);
        Assert.Equal(CountingMethod.BySheet, loaded.CountingMethod);
        Assert.Equal(FileSortOrder.ByPageCount, loaded.FileSortOrder);
        Assert.Equal("طابعة المكتب", loaded.DefaultPrinterName);
        Assert.Equal(25, loaded.PrinterRefreshSeconds);
    }

    /// <summary>
    /// من غير Encoder مخصص، System.Text.Json بيحوّل العربي لأكواد \uXXXX
    /// والملف بيبقى مش مقروء لو المستخدم فتحه بنفسه.
    /// </summary>
    [Fact]
    public void Arabic_Is_Written_Readable_Not_Escaped()
    {
        _store.Save(new AppSettings { WatermarkText = "شركة الأمل" });

        string json = File.ReadAllText(_store.SettingsPath);

        Assert.Contains("شركة الأمل", json);
        Assert.DoesNotContain("\\u0634", json);
    }

    [Fact]
    public void Corrupt_File_Gives_Defaults_Not_A_Crash()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(_store.SettingsPath, "{ ده مش JSON خالص ,,, }");

        var settings = _store.Load();

        Assert.Equal(10, settings.PrinterRefreshSeconds);
    }

    [Fact]
    public void Empty_File_Gives_Defaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(_store.SettingsPath, "");

        Assert.False(_store.Load().WatermarkEnabled);
    }

    [Fact]
    public void No_Temp_File_Is_Left_Behind_After_Saving()
    {
        _store.Save(new AppSettings());

        // بنسأل عن **أي** ملف .tmp مش عن اسم معيّن. النسخة القديمة كانت
        // بتسأل عن "settings.json.tmp" بالحرف، فأول ما الاسم المؤقت اتغيّر
        // التست كان هيعدّي على الفاضي من غير ما يفحص حاجة.
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
        Assert.True(File.Exists(_store.SettingsPath));
    }

    [Fact]
    public void Saving_From_Many_Threads_At_Once_Never_Corrupts_The_File()
    {
        // ═══ التاريخ ═══
        //
        // التست ده اتكتب عشان الاسم المؤقت الثابت (settings.json.tmp):
        // نسختين من البرنامج كانوا بيكتبوا في نفس الملف.
        //
        // وأول ما اشتغل على ويندوز وقع على حاجة تانية خالص — مش الكتابة،
        // النقل:
        //
        //   UnauthorizedAccessException at System.IO.FileSystem.MoveFile
        //
        // يعني الاسم الفريد حل نص المشكلة بس. MoveFileEx بترفض لو الملف
        // الهدف مفتوح عند حد، والخيوط كانت بتستبدل نفس الملف في نفس
        // اللحظة.
        //
        // الإصلاح جزئين: قفل جوه العملية (بيشيل الحالة دي من أصلها)،
        // وإعادة محاولة (للي بره العملية — نسخة تانية، مضاد فيروسات،
        // فهرسة ويندوز).
        var stores = Enumerable.Range(0, 4).Select(_ => new JsonSettingsStore(_folder)).ToList();

        Parallel.ForEach(stores, store =>
        {
            for (int i = 0; i < 25; i++)
            {
                store.Save(new AppSettings { WatermarkFontSize = 12 + i });
            }
        });

        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));

        // بيتقري ومش رايح للافتراضي (يعني الملف مش بايظ)
        Assert.NotEqual(new AppSettings().WatermarkFontSize, _store.Load().WatermarkFontSize);
    }

    [Fact]
    public void A_Blocked_Replace_Is_Retried_Until_It_Works()
    {
        // بنسدّ السكة بطريقة بتشتغل على ويندوز ولينكس الاتنين: بنحط
        // **مجلد** مكان ملف الإعدادات. النقل فوق مجلد بيفشل دايمًا —
        // UnauthorizedAccessException على ويندوز وIOException على لينكس،
        // والاتنين في قايمة "يستاهل إعادة محاولة".
        //
        // ده التست الوحيد اللي بيثبت إن JsonSettingsStore **بيستخدم**
        // FileReplace فعلًا. تستات FileReplace نفسها بتفحص الأرقام بس.
        //
        // ⚠ السطر اللي تحت مش زيادة: أول نداء لـ JsonSerializer في العملية
        // بياخد ١٠٠ مللي+ في الـ JIT. من غير التسخين ده، التسلسل بياخد
        // وقت أطول من السد نفسه — فالنقل بيتنفذ بعد ما السد يتشال
        // والتست بيعدّي حتى من غير أي إعادة محاولة. اتكشف بالتخريب.
        _store.Save(new AppSettings { WatermarkFontSize = 5 });

        File.Delete(_store.SettingsPath);
        Directory.CreateDirectory(_store.SettingsPath);
        File.WriteAllText(Path.Combine(_store.SettingsPath, "بتاعنا.txt"), "سد");

        var saving = Task.Run(() => _store.Save(new AppSettings { WatermarkFontSize = 37 }));

        // نسيب أول محاولات تفشل فعلًا وبعدين نفتح السكة. الميزانية
        // (شوف FileReplace) أكبر من ٥٠ مللي بمراحل.
        Thread.Sleep(50);
        Directory.Delete(_store.SettingsPath, recursive: true);

        saving.GetAwaiter().GetResult();

        Assert.Equal(37, _store.Load().WatermarkFontSize);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void A_Permanent_Block_Gives_Up_Instead_Of_Hanging()
    {
        // لو السكة مسدودة للأبد، مايصحش البرنامج يفضل يحاول لحد الأبد.
        // بيقول الحقيقة، والملف القديم بيفضل سليم.
        _store.Save(new AppSettings { WatermarkFontSize = 21 });

        File.Delete(_store.SettingsPath);
        Directory.CreateDirectory(_store.SettingsPath);
        File.WriteAllText(Path.Combine(_store.SettingsPath, "سد.txt"), "سد");

        try
        {
            Assert.ThrowsAny<Exception>(
                () => _store.Save(new AppSettings { WatermarkFontSize = 99 }));

            Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
        }
        finally
        {
            Directory.Delete(_store.SettingsPath, recursive: true);
        }
    }

    [Fact]
    public void A_Busy_Settings_File_Is_Waited_Out_Not_Given_Up_On()
    {
        // بنقلّد بالظبط اللي مضاد الفيروسات وWindows Search بيعملوه:
        // بيفتحوا الملف لجزء من الثانية بعد ما يتكتب.
        //
        // ⚠ صدق مع النفس: التست ده بيعض على **ويندوز** بس. هناك القفل
        // إجباري، فالنقل بيرفض والإعادة هي اللي بتنقذ الموقف. على لينكس
        // مفيش قفل إجباري — النقل بينجح من أول مرة والتست بيعدّي من غير
        // ما يفحص الإعادة أصلًا. الميزة إن ده بالظبط الجهاز اللي البلاغ
        // جه منه.
        //
        // الحفظ ده بيسخّن الـ JIT كمان قبل التوقيت الحساس (شوف التست
        // اللي فوق) — أول تسلسل في العملية بياخد ١٠٠ مللي+.
        _store.Save(new AppSettings { WatermarkFontSize = 11 });
        _store.Save(new AppSettings { WatermarkFontSize = 11 });

        var holder = new FileStream(
            _store.SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        try
        {
            var saving = Task.Run(() => _store.Save(new AppSettings { WatermarkFontSize = 44 }));

            // نسيب المحاولات الأولى تفشل فعلًا، وبعدين نفتح السكة.
            // الميزانية أكبر من ٥٠ مللي بكتير (شوف FileReplace)، فمفيش
            // سباق هنا حتى على جهاز بطيء.
            Thread.Sleep(50);
            holder.Dispose();

            saving.GetAwaiter().GetResult();
        }
        finally
        {
            holder.Dispose();
        }

        Assert.Equal(44, _store.Load().WatermarkFontSize);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void Saving_Many_Times_In_A_Row_Stays_Readable()
    {
        // الحفظ بقى لحظي في ١.٩.٥ — يعني الكتابة بتحصل أكتر بكتير من
        // الأول. لازم نتأكد إن التكرار مابيخلّفش زبالة ولا ملف نص كتابة.
        for (int size = 10; size <= 60; size++)
        {
            _store.Save(new AppSettings { WatermarkFontSize = size });
        }

        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
        Assert.Equal(60, _store.Load().WatermarkFontSize);
    }

    // ══════════ الإعدادات المسبقة ══════════

    [Fact]
    public void Presets_Survive_A_Save_And_Load()
    {
        var presets = new[]
        {
            new Preset
            {
                Name = "كتالوج A3",
                Settings = new PrintSettings
                {
                    PaperSize = "A3",
                    TotalCopies = 50,
                    Duplex = true,
                    DuplexFlip = DuplexFlip.ShortEdge,
                    PageOrientation = PageOrientation.Landscape,
                    SelectedPrinters = new List<string> { "HP-1", "HP-2" }
                }
            },
            new Preset { Name = "مسودة سريعة", Settings = new PrintSettings { Grayscale = true } }
        };

        _store.SaveAll(presets);
        var loaded = _store.LoadAll();

        Assert.Equal(2, loaded.Count);

        var catalogue = loaded.First(p => p.Name == "كتالوج A3");
        Assert.Equal("A3", catalogue.Settings.PaperSize);
        Assert.Equal(50, catalogue.Settings.TotalCopies);
        Assert.True(catalogue.Settings.Duplex);
        Assert.Equal(DuplexFlip.ShortEdge, catalogue.Settings.DuplexFlip);
        Assert.Equal(PageOrientation.Landscape, catalogue.Settings.PageOrientation);
        Assert.Equal(new[] { "HP-1", "HP-2" }, catalogue.Settings.SelectedPrinters);

        Assert.True(loaded.First(p => p.Name == "مسودة سريعة").Settings.Grayscale);
    }

    [Fact]
    public void No_Presets_File_Gives_An_Empty_List()
    {
        Assert.Empty(_store.LoadAll());
    }

    [Fact]
    public void Corrupt_Presets_File_Gives_An_Empty_List()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(_store.PresetsPath, "[[[ بايظ");

        Assert.Empty(_store.LoadAll());
    }

    [Fact]
    public void Presets_Without_A_Name_Are_Skipped()
    {
        _store.SaveAll(new[]
        {
            new Preset { Name = "سليم" },
            new Preset { Name = "   " },
            new Preset { Name = "" }
        });

        var loaded = _store.LoadAll();

        Assert.Single(loaded);
        Assert.Equal("سليم", loaded[0].Name);
    }

    [Fact]
    public void Saving_Twice_Replaces_Rather_Than_Appends()
    {
        _store.SaveAll(new[] { new Preset { Name = "أول" }, new Preset { Name = "تاني" } });
        _store.SaveAll(new[] { new Preset { Name = "تالت" } });

        var loaded = _store.LoadAll();

        Assert.Single(loaded);
        Assert.Equal("تالت", loaded[0].Name);
    }
}
