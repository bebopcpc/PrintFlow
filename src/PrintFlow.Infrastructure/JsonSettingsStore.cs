using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using PrintFlow.Application;
using PrintFlow.Domain;

namespace PrintFlow.Infrastructure;

/// <summary>
/// بيحفظ الإعدادات والـ Presets كملفات JSON في مجلد المستخدم.
///
/// مبدأ ثابت هنا: **البرنامج مايفشلش عشان ملف إعدادات**. أي مشكلة في القراءة
/// (ملف تالف، صلاحيات، نسخة قديمة) بترجّع الافتراضي بدل ما ترمي استثناء —
/// مطبعة مش هتقف عشان JSON فيه فاصلة زيادة.
/// </summary>
public sealed class JsonSettingsStore : IAppSettingsStore, IPresetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // من غير الحتة دي العربي بيتحفظ كـ ال... ومحدش يقدر يقرا الملف
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly string _folder;

    public JsonSettingsStore(string? folder = null)
    {
        _folder = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrintFlow");
    }

    public string SettingsPath => Path.Combine(_folder, "settings.json");

    public string PresetsPath => Path.Combine(_folder, "presets.json");

    // ══════════ تفضيلات البرنامج ══════════

    public AppSettings Load() => ReadOrDefault(SettingsPath, () => new AppSettings());

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Write(SettingsPath, settings);
    }

    // ══════════ الإعدادات المسبقة ══════════

    public IReadOnlyList<Preset> LoadAll()
    {
        var presets = ReadOrDefault(PresetsPath, () => new List<Preset>());

        // بنستبعد أي مدخل بايظ بدل ما نرمي القايمة كلها
        return presets
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
            .ToList();
    }

    public void SaveAll(IEnumerable<Preset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        Write(PresetsPath, presets.ToList());
    }

    // ══════════ قراءة وكتابة ══════════

    private static T ReadOrDefault<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path))
            {
                return fallback();
            }

            string json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json))
            {
                return fallback();
            }

            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback();
        }
        catch (Exception)
        {
            // ملف تالف أو صلاحيات — نكمّل بالافتراضي
            return fallback();
        }
    }

    private static int _writeCounter;

    /// <summary>
    /// بيخلّي كتابات العملية الواحدة ورا بعض بدل ما تتخانق على نفس الملف.
    ///
    /// من غيره، خيطين في نفس البرنامج بيحاولوا ينقلوا فوق نفس الملف في
    /// نفس اللحظة — وويندوز بيرفض النقل لو الهدف مفتوح عند حد. القفل ده
    /// بيشيل الحالة دي من أصلها **جوه العملية**. اللي بره العملية (نسخة
    /// تانية من البرنامج، مضاد فيروسات، فهرسة ويندوز) بتتعالج بإعادة
    /// المحاولة تحت.
    ///
    /// ساكن (static) عن قصد: المقصود كل نسخ المخزن في البرنامج، مش نسخة
    /// واحدة. والكتابة نفسها ملف صغير — القفل ده عمره ما هيتحس.
    ///
    /// ⚠ صدق مع النفس: **إعادة المحاولة** هي اللي بتحل المشكلة فعلًا —
    /// لما شيلنا القفل في التخريب، التستات كلها فضلت خضرا ٣ مرات من ٣.
    /// القفل موجود لسببين مش مبرهنين بتست:
    ///
    ///   ١) بيمنع "قطيع" الخيوط: من غيره، خيطين بيفشلوا مع بعض وبيستنوا
    ///      **نفس** المدة بالظبط (مفيش عشوائية في الجدول)، فبيصطدموا
    ///      تاني في كل محاولة لحد ما الميزانية تخلص.
    ///   ٢) بيخلّي الحالة اللي جوه العملية قاطعة بدل "غالبًا الإعادة
    ///      هتنقذها".
    /// </summary>
    private static readonly Lock WriteGate = new();

    /// <summary>
    /// بيكتب الملف بأمان: ملف مؤقت الأول وبعدين نقل فوق الأصلي.
    ///
    /// ═══ ليه ملف مؤقت ═══
    ///
    /// لو الكهربا قطعت وسط الكتابة، الملف القديم بيفضل سليم بدل ما يبقى
    /// نص ملف مقطوع. النقل نفسه عملية ذرّية — يا بيتم بالكامل يا مابيتمش.
    ///
    /// ═══ ليه الاسم المؤقت فريد ═══
    ///
    /// كان اسمه ثابت (settings.json.tmp)، وده بيتلخبط لو البرنامج مفتوح
    /// **مرتين** على نفس الجهاز: العمليتين بيكتبوا في نفس الملف المؤقت،
    /// فواحد ينقل نص كتابة التاني.
    ///
    /// ═══ ليه إعادة المحاولة ═══
    ///
    /// أول بيلد لـ ١.٩.٥ على ويندوز وقع على:
    ///
    ///   UnauthorizedAccessException at System.IO.FileSystem.MoveFile
    ///
    /// <c>MoveFileEx</c> بترفض لو الملف الهدف مفتوح عند أي حد — حتى لو
    /// بيقرا بس، وحتى لو الصلاحيات مظبوطة. ومضاد الفيروسات وWindows
    /// Search وOneDrive بيعملوا ده كل شوية على مجلد %AppData%.
    ///
    /// ═══ ليه المحاولة بتلف على الخطوتين مش على النقل بس ═══
    ///
    /// أول نسخة من الإصلاح كانت بتعيد المحاولة على النقل بس. جربناها
    /// بمجلد مقفول للكتابة، فطلع إن الفشل بيحصل **قبل كده** — في إنشاء
    /// الملف المؤقت نفسه — والإعادة مكانتش بتشتغل أصلًا. الخطوتين
    /// الاتنين بيلمسوا القرص، فالاتنين بيتعادوا.
    ///
    /// وكل محاولة بتاخد اسم مؤقت **جديد**: لو المحاولة اللي فاتت سابت
    /// ملف نص كتابة، مانبنيش فوقه.
    /// </summary>
    private void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(_folder);

        // التسلسل بره الحلقة: لو الكائن نفسه مش قابل للتسلسل، الإعادة
        // مش هتصلّحه — والخطأ ده يستاهل يطلع من أول مرة
        string json = JsonSerializer.Serialize(value, Options);

        lock (WriteGate)
        {
            for (int attempt = 0; ; attempt++)
            {
                string temporary =
                    $"{path}.{Environment.ProcessId}-{Interlocked.Increment(ref _writeCounter)}.tmp";

                try
                {
                    File.WriteAllText(temporary, json);
                    File.Move(temporary, path, overwrite: true);
                    return;
                }
                catch (Exception exception)
                {
                    DeleteQuietly(temporary);

                    if (!FileReplace.WorthRetrying(exception) || FileReplace.IsLastAttempt(attempt))
                    {
                        throw;
                    }

                    Thread.Sleep(FileReplace.DelayMilliseconds(attempt + 1));
                }
            }
        }
    }

    /// <summary>مابنسيبش زبالة ورانا في مجلد المستخدم — ومابنفشلش عشانها.</summary>
    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ملف مؤقت مايستاهلش نوقّع الحفظ عشانه
        }
    }
}
