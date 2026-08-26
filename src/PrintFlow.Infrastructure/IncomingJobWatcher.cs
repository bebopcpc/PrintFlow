using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Infrastructure;

/// <summary>
/// بيلقط الملفات الواردة من الطابعة الوهمية ومن المجلد المراقَب.
///
/// ═══ المشكلة الأساسية اللي الكلاس ده موجود عشانها ═══
///
/// بورت الطابعة الوهمية **ملف واحد بمسار ثابت**. يعني:
///
///   • لو خطفنا الملف وهو لسه بيتكتب → نص ملزمة بتتطبع على إنها كاملة
///   • لو اتأخرنا عليه → جوب جديد بيكتب فوقه والجوب الأول بيضيع
///
/// الحل هنا:
///
///   ١) بنقرا الحجم كل ٤٠٠ مللي. طول ما بيزيد، الكتابة شغالة.
///   ٢) لما يقف عند نفس الرقم ٣ مرات ورا بعض → خلص.
///   ٣) بننقله **فورًا** لمجلد الطابور باسم فيه التوقيت ورقم مسلسل،
///      فالمكان يفضى للجوب اللي بعده.
///
/// والنقل نفسه (File.Move) عملية ذرّية على نفس القرص — يا بيتم بالكامل
/// يا مابيتمش، مفيش نص ملف. لو فشل (الملف لسه مقفول) بنعيد المحاولة.
///
/// ═══ ليه بنعتمد على الحجم مش على "الملف مقفول ولا لأ" ═══
///
/// جربنا نفتح الملف بصلاحية حصرية كإشارة إنه خلص، بس ده بيعتمد على
/// إزاي الـ Local Port بيقفل الهاندل، وده سلوك مش موثّق ومختلف بين نسخ
/// ويندوز. استقرار الحجم بيشتغل في كل الحالات وبيتفحص بالأرقام لوحده.
/// </summary>
public class IncomingJobWatcher : IIncomingJobWatcher, IDisposable
{
    private readonly object _gate = new();

    private CancellationTokenSource? _stopping;
    private Task? _loop;
    private int _sequence;

    /// <summary>كام مرة فشلنا في أخذ كل ملف — عشان الرسالة تتقال مرة واحدة.</summary>
    private readonly Dictionary<string, int> _failedAttempts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// ملفات طلّعنا عليها تحذير. لو وصلت بعد كده، بنقول إنها اتحلّت —
    /// عشان آخر سطر في اللوج مايفضلش تحذير عن حاجة خلصت.
    /// </summary>
    private readonly HashSet<string> _warned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// بصمة آخر ملف أخدناه من كل مسار: (الحجم، وقت آخر تعديل).
    ///
    /// موجودة عشان الحالة اللي بننسخ فيها الملف ومانقدرش نمسح الأصل.
    /// الأصل بيفضل مكانه، وفي الدورة الجاية هنشوفه تاني — من غير البصمة
    /// دي كنا هنسلّم نفس الجوب مرة ورا التانية للأبد.
    ///
    /// وقت التعديل بيتغيّر مع كل كتابة جديدة، فأي جوب حقيقي جديد بصمته
    /// مختلفة وبيعدّي عادي.
    /// </summary>
    private readonly Dictionary<string, (long Size, long Ticks)> _alreadyTaken =
        new(StringComparer.OrdinalIgnoreCase);

    public event Action<IncomingFile>? JobArrived;
    public event Action<string>? Reported;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// بينده المشتركين من غير ما استثناء عندهم يقتل المراقبة.
    ///
    /// ده مش دفاع نظري: الـ ViewModel بيضيف في قوايم مربوطة بالواجهة،
    /// ولو النداء جه من ثريد خلفي بيرمي. الاستثناء ده كان بيطلع من
    /// الحلقة ويوقّف الاستقبال **للأبد** — والبرنامج يفضل شكله سليم
    /// ومش شايف أي جوب. المراقب مسؤوليته يلقط الملفات؛ غلطة عند اللي
    /// بيسمع مش سبب إنه يموت.
    /// </summary>
    private void RaiseArrived(IncomingFile file)
    {
        try
        {
            JobArrived?.Invoke(file);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"JobArrived رمى: {ex}");
        }
    }

    private void Report(string line)
    {
        try
        {
            Reported?.Invoke(line);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reported رمى: {ex}");
        }
    }

    public void Start(string spoolFolder, string queueFolder, string? hotFolder)
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

            _stopping = new CancellationTokenSource();
            IsRunning = true;

            var token = _stopping.Token;

            _loop = Task.Run(() => RunAsync(spoolFolder, queueFolder, hotFolder, token), token);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            _stopping?.Cancel();
        }

        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // البرنامج بيقفل — مش وقت الاعتراضات
        }
    }

    private async Task RunAsync(string spoolFolder, string queueFolder, string? hotFolder, CancellationToken token)
    {
        try
        {
            await WatchAsync(spoolFolder, queueFolder, hotFolder, token);
        }
        catch (OperationCanceledException)
        {
            // إغلاق عادي
        }
        catch (Exception ex)
        {
            // آخر خط دفاع. من غيره الحلقة كانت بتموت في صمت والمستخدم
            // يفضل مستني جوبات مش جاية ومفيش أي إشارة إن حاجة حصلت.
            Report($"[فشل] الاستقبال وقف: {ex.Message}. اقفل البرنامج وافتحه تاني.");
            System.Diagnostics.Debug.WriteLine($"حلقة الاستقبال ماتت: {ex}");
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task WatchAsync(string spoolFolder, string queueFolder, string? hotFolder, CancellationToken token)
    {
        try
        {
            Directory.CreateDirectory(spoolFolder);
            Directory.CreateDirectory(queueFolder);
        }
        catch (Exception ex)
        {
            Report($"[فشل] مقدرناش نجهّز مجلدات الاستقبال: {ex.Message}");
            return;
        }

        // ملفات كانت مستنية من قبل ما البرنامج يفتح — الجوب اللي وصل
        // والبرنامج مقفول ماينفعش يضيع
        DrainQueue(queueFolder);

        string portPath = Path.Combine(spoolFolder, VirtualPrinter.PortFileName);

        var portWatch = FileWatch.Start;
        var hotWatches = new Dictionary<string, FileWatch>(StringComparer.OrdinalIgnoreCase);

        while (!token.IsCancellationRequested)
        {
            try
            {
                portWatch = PollOne(portPath, portWatch, queueFolder, IncomingSource.VirtualPrinter);

                if (!string.IsNullOrWhiteSpace(hotFolder))
                {
                    PollFolder(hotFolder, hotWatches, queueFolder, token);
                }
            }
            catch (Exception ex)
            {
                // المراقبة ماينفعش توقف عشان غلطة في دورة واحدة
                Report($"[تنبيه] الاستقبال: {ex.Message}");
            }

            try
            {
                await Task.Delay(IncomingWatchPolicy.Interval, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// بيقرا حجم ملف واحد ويقرر: نستنى ولا ناخده؟
    /// بيرجّع حالة المتابعة الجديدة.
    /// </summary>
    private FileWatch PollOne(string path, FileWatch watch, string queueFolder, IncomingSource source)
    {
        long size;

        try
        {
            var info = new FileInfo(path);

            if (!info.Exists)
            {
                return FileWatch.Start;
            }

            size = info.Length;
        }
        catch
        {
            // الملف اتشال في نص القراءة — نبدأ من أول وجديد
            return FileWatch.Start;
        }

        var next = watch.Observe(size);

        if (!next.IsSettled(IncomingWatchPolicy.StableTicksNeeded))
        {
            return next;
        }

        // أخدناه قبل كده ومقدرناش نمسحه؟ يبقى نسيبه في حاله بدل ما
        // نسلّم نفس الجوب تاني
        if (WasAlreadyTaken(path, size))
        {
            return next;
        }

        return TryClaim(path, queueFolder, source) ? FileWatch.Start : next;
    }

    private void PollFolder(
        string hotFolder,
        Dictionary<string, FileWatch> watches,
        string queueFolder,
        CancellationToken token)
    {
        if (!Directory.Exists(hotFolder))
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.EnumerateFiles(hotFolder))
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            // نفس قايمة الصيغ اللي التحميل اليدوي بيقبلها — مصدر واحد
            // للحقيقة، عشان ما يحصلش إن صيغة تتقبل من هنا وتترفض من هناك
            if (SupportedInput.KindOf(path) is not (InputKind.Pdf or InputKind.Image))
            {
                continue;
            }

            seen.Add(path);

            var watch = watches.TryGetValue(path, out var existing) ? existing : FileWatch.Start;

            watches[path] = PollOne(path, watch, queueFolder, IncomingSource.HotFolder);
        }

        // ملفات اتشالت من المجلد — منظّفين حالتها عشان القاموس مايكبرش
        foreach (string gone in watches.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            watches.Remove(gone);
        }
    }

    /// <summary>
    /// بينقل الملف للطابور باسم فريد. النقل ذرّي، فيا بيتم بالكامل يا لأ.
    /// بيرجّع true لو نجح.
    /// </summary>
    private bool TryClaim(string path, string queueFolder, IncomingSource source)
    {
        long size;

        try
        {
            size = new FileInfo(path).Length;
        }
        catch
        {
            return false;
        }

        string destination = NextFreeName(queueFolder);

        Exception? failure = null;

        try
        {
            File.Move(path, destination);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (failure is null)
        {
            AnnounceRecovery(path);
            RaiseArrived(new IncomingFile(destination, source, size));
            return true;
        }

        // النقل فشل بسبب الصلاحيات. نجرّب ننسخ بدل ما ننقل: النقل محتاج
        // صلاحية **حذف** على الأصل، والنسخ محتاج **قراءة** بس.
        //
        // ده بالظبط وضع الطابعة الوهمية: خدمة الطباعة بتشتغل بحساب النظام
        // وهي اللي بتعمل الملف فبتملكه، والمستخدم يقدر يقراه ومايقدرش
        // يمسحه. النسخ بينقذ الجوب.
        // اسم جديد تمامًا للنسخة، مش نفس اسم محاولة النقل الفاشلة —
        // عشان مانتعاملش مع أي بقايا سابها النقل ورا نفسه
        string fallback = NextFreeName(queueFolder);

        if (FileClaim.Classify(failure) == ClaimFailure.NoPermission && TryCopy(path, fallback))
        {
            // النقل الفاشل ممكن يكون ساب نسخة نصّية أو كاملة ورا نفسه.
            // لو سبناها، الطابور هيبقى فيه الجوب مرتين — و DrainQueue
            // عند التشغيل الجاي هيسلّمهم الاتنين والورق يتطبع مرتين.
            TryDeleteQuietly(destination);

            AnnounceRecovery(path);

            // الأصل ممكن يكون فضل مكانه (مقدرناش نمسحه). بنسجّل بصمته
            // عشان الدورة الجاية ماتسلّمش نفس الجوب تاني.
            RememberTaken(path, size);

            RaiseArrived(new IncomingFile(fallback, source, size));

            if (File.Exists(path))
            {
                Report(
                    $"[تنبيه] الجوب وصل، بس \"{Path.GetFileName(path)}\" فضل مكانه ومقدرناش نمسحه. " +
                    "شغّل  .\\install-printer.ps1 -FixPermissions  كمسؤول عشان الاستقبال يشتغل صح.");
            }

            return true;
        }

        TryDeleteQuietly(destination);
        TryDeleteQuietly(fallback);

        ReportClaimFailure(path, failure);

        return false;
    }

    /// <summary>
    /// بينسخ الملف وبيحاول يمسح الأصل. النسخة هي المهمة — المسح إضافة.
    ///
    /// بيرجّع true لو **النسخ** نجح، حتى لو المسح فشل. الجوب أهم من
    /// نضافة المجلد، والبصمة في <see cref="_alreadyTaken"/> بتمنع التكرار.
    /// </summary>
    private static bool TryCopy(string path, string destination)
    {
        // بقايا من محاولة النقل الفاشلة.
        //
        // File.Move لما بيفشل في مسح الأصل بيسيب النسخة اللي عملها في
        // الوجهة — **وبصلاحيات الأصل**، يعني ملف للقراءة بس. وبعد كده أي
        // محاولة كتابة عليه بترفض بنفس خطأ الصلاحيات، فالخطة البديلة
        // كانت بتفشل بسبب نفسها. (اتصادت في التجربة: الخطأ كان بيقول
        // اسم ملف **الوجهة** مش المصدر، وده اللي وصّلنا للسبب.)
        TryDeleteQuietly(destination);

        try
        {
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
            }
        }
        catch
        {
            // نسخة نص ملف أسوأ من مفيش نسخة
            TryDeleteQuietly(destination);
            return false;
        }

        TryDeleteQuietly(path);

        return true;
    }

    /// <summary>بينضّف حالة الفشل، وبيقول "اتحل" لو كنا حذّرنا قبل كده.</summary>
    private void AnnounceRecovery(string path)
    {
        _failedAttempts.Remove(path);

        if (_warned.Remove(path))
        {
            Report(FileClaim.Resolved(Path.GetFileName(path)));
        }
    }

    private bool WasAlreadyTaken(string path, long size)
    {
        if (!_alreadyTaken.TryGetValue(path, out var taken))
        {
            return false;
        }

        try
        {
            return taken.Size == size && taken.Ticks == File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch
        {
            return false;
        }
    }

    private void RememberTaken(string path, long size)
    {
        try
        {
            _alreadyTaken[path] = (size, File.GetLastWriteTimeUtc(path).Ticks);
        }
        catch
        {
            // الملف اختفى بين النسخ والتسجيل — يبقى المسح نجح وخلاص
        }
    }

    /// <summary>
    /// بيقول للمستخدم إيه اللي حصل — **بعد** كام محاولة صامتة.
    ///
    /// من غير العدّاد ده، الرسالة كانت بتتكرر كل ٤٠٠ مللي وتغرق شريط
    /// النتايج. والأهم إنها بقت بتقول الحل مش بس المشكلة.
    /// </summary>
    private void ReportClaimFailure(string path, Exception failure)
    {
        var kind = FileClaim.Classify(failure);

        if (FileClaim.IsSilent(kind))
        {
            return;
        }

        int attempts = _failedAttempts.TryGetValue(path, out int previous) ? previous + 1 : 1;
        _failedAttempts[path] = attempts;

        int threshold = FileClaim.QuietAttemptsFor(kind);

        if (FileClaim.WorthRetrying(kind) && attempts < threshold)
        {
            return;
        }

        // بنقولها مرة واحدة عند الحد بالظبط، مش كل دورة بعد كده
        if (attempts == threshold || !FileClaim.WorthRetrying(kind))
        {
            _warned.Add(path);
            Report(FileClaim.Explain(kind, Path.GetFileName(path)));
            System.Diagnostics.Debug.WriteLine($"claim failed: {failure}");
        }
    }

    private string NextFreeName(string queueFolder)
    {
        _sequence = (_sequence + 1) % 1000;

        string destination = Path.Combine(queueFolder, VirtualPrinter.QueueNameFor(DateTime.Now, _sequence));

        // نفس الاسم موجود؟ (البرنامج اتفتح مرتين مثلًا) — بنزوّد المسلسل
        while (File.Exists(destination))
        {
            _sequence = (_sequence + 1) % 1000;
            destination = Path.Combine(queueFolder, VirtualPrinter.QueueNameFor(DateTime.Now, _sequence));
        }

        return destination;
    }

    private static void TryDeleteQuietly(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            File.Delete(path);
            return;
        }
        catch
        {
            // هنجرّب تاني بعد ما نشيل خاصية "للقراءة بس"
        }

        // ملف متعلّم عليه "للقراءة بس" مابيتمسحش على ويندوز غير لما نشيل
        // الخاصية. بنعمل ده **بعد** ما المسح العادي يفشل مش قبله: تغيير
        // خصايص ملف مش ملكنا بيفشل هو كمان، وكان بيمنع المسح اللي كان
        // ممكن ينجح لوحده.
        try
        {
            var attributes = File.GetAttributes(path);

            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(path);
        }
        catch
        {
            // ملف مؤقت فضل موجود مش سبب نوقف الاستقبال
        }
    }

    /// <summary>ملفات كانت في الطابور من تشغيلة سابقة.</summary>
    private void DrainQueue(string queueFolder)
    {
        try
        {
            var waiting = Directory.EnumerateFiles(queueFolder, "*.pdf")
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            if (waiting.Count == 0)
            {
                return;
            }

            Report($"في {waiting.Count} ملف كانوا مستنيين من قبل ما البرنامج يفتح.");

            foreach (string path in waiting)
            {
                long size = 0;

                try
                {
                    size = new FileInfo(path).Length;
                }
                catch
                {
                    // مش مشكلة — الحجم للعرض بس
                }

                RaiseArrived(new IncomingFile(path, IncomingSource.VirtualPrinter, size));
            }
        }
        catch (Exception ex)
        {
            Report($"[تنبيه] مقدرناش نقرا طابور الاستقبال: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _stopping?.Dispose();
        GC.SuppressFinalize(this);
    }
}
