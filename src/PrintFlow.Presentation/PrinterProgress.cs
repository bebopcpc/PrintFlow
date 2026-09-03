using PrintFlow.Domain;

namespace PrintFlow.Presentation;

/// <summary>
/// صف واحد في شاشة التقدم: مكنة واحدة والشغل اللي خلّصته لحد دلوقتي.
///
/// ═══ ليه الحالة دي عايشة هنا مش في الموزّع ═══
///
/// WorkDispatcher شغلته إنه يوزّع ويضمن إن القطعة ماتتبعتش مرتين. إحنا مش
/// عايزين نلمسه. فبدل ما نضيفله حدث جديد، الـ ViewModel بيلفّ دالة الطباعة
/// اللي هو أصلًا بيبعتها له — وبيعدّ من هناك.
/// </summary>
public sealed class PrinterProgress : ObservableObject
{
    public PrinterProgress(string printerName, int pagesPlanned, int copiesPlanned = 0)
    {
        PrinterName = printerName;
        _pagesPlanned = Math.Max(0, pagesPlanned);
        _copiesPlanned = Math.Max(0, copiesPlanned);
    }

    public string PrinterName { get; }

    private int _copiesPlanned;
    /// <summary>
    /// نصيبها بالنسخ في الخطة. صفر = مش معروف (وساعتها السطر بيرجع
    /// لصيغته القديمة بدل ما يقول «فاضل ٠ من ٠»).
    /// </summary>
    public int CopiesPlanned
    {
        get => _copiesPlanned;
        set
        {
            if (SetProperty(ref _copiesPlanned, Math.Max(0, value)))
            {
                Recalculate();
            }
        }
    }

    /// <summary>
    /// فاضل عليها كام نسخة. مقفول عند صفر — سرقة الشغل ممكن تخلّيها
    /// تطلّع أكتر من نصيبها، ورقم سالب هنا معناه خلصت مش عليها دَين.
    /// </summary>
    public int CopiesLeft => Math.Max(0, CopiesPlanned - CopiesDone);

    private int _pagesPlanned;
    /// <summary>نصيبها في الخطة. توقُّع مش أمر — سرقة الشغل ممكن تغيّره.</summary>
    public int PagesPlanned
    {
        get => _pagesPlanned;
        set
        {
            if (SetProperty(ref _pagesPlanned, Math.Max(0, value)))
            {
                Recalculate();
            }
        }
    }

    private int _pagesDone;
    public int PagesDone
    {
        get => _pagesDone;
        private set
        {
            if (SetProperty(ref _pagesDone, value))
            {
                Recalculate();
            }
        }
    }

    private int _copiesDone;
    public int CopiesDone
    {
        get => _copiesDone;
        private set
        {
            if (SetProperty(ref _copiesDone, value))
            {
                Recalculate();
            }
        }
    }

    private string _state = "مستنية";
    /// <summary>كلمة واحدة توصف المكنة دلوقتي: مستنية / بتطبع / خلصت / وقفت.</summary>
    public string State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                Recalculate();
            }
        }
    }

    private bool _isFaulted;
    /// <summary>فيها عطل — الواجهة بتلوّن البار أحمر على أساسها.</summary>
    public bool IsFaulted
    {
        get => _isFaulted;
        private set => SetProperty(ref _isFaulted, value);
    }

    /// <summary>
    /// النسبة، مقفولة عند ١٠٠ عن قصد.
    ///
    /// المكنة السريعة بتسحب شغل من طوابير المكن البطيئة، فممكن تخلّص أكتر
    /// من نصيبها في الخطة. البار مايصحّش يعدّي آخره — الرقم اللي جنبه هو
    /// اللي بيقول الحقيقة.
    /// </summary>
    public double Percent => _pagesPlanned <= 0
        ? (_pagesDone > 0 ? 100 : 0)
        : Math.Min(100d, _pagesDone * 100d / _pagesPlanned);

    /// <summary>
    /// فاضل من نصيبها في الخطة. مقفول عند صفر: سرقة الشغل ممكن تخلّي
    /// المكنة تطلّع أكتر من نصيبها، ورقم سالب هنا معناه صفر مش دَين.
    /// </summary>
    public int PagesLeft => Math.Max(0, PagesPlanned - PagesDone);

    private PrinterQueueState _queue = PrinterQueueState.Idle;
    /// <summary>
    /// اللي في طابور الطابعة دلوقتي — جاي من ويندوز مش من عندنا.
    ///
    /// بيتحدّث كل تانيتين طول ما الأوردر ماشي. شوف
    /// <see cref="PrinterQueueState"/> للسبب.
    /// </summary>
    public PrinterQueueState Queue
    {
        get => _queue;
        set
        {
            if (SetProperty(ref _queue, value ?? PrinterQueueState.Idle))
            {
                OnPropertyChanged(nameof(QueueCaption));
                OnPropertyChanged(nameof(HasQueueNews));
            }
        }
    }

    /// <summary>السطر التاني — كلام الطابعة نفسها. "" يعني مفيش حاجة تتقال.</summary>
    public string QueueCaption => IsFaulted ? "" : Queue.Describe();

    /// <summary>فيه سطر تاني يتعرض؟ الواجهة بتخفي الفراغ على أساسها.</summary>
    public bool HasQueueNews => QueueCaption.Length > 0;

    /// <summary>
    /// السطر اللي بيتقرا جنب البار.
    ///
    /// **"بعتنا" مقصودة**: الرقم ده هو اللي إحنا سلّمناه للطابور، مش اللي
    /// طلع ورق. اللي طلع ورق بيتقال في <see cref="QueueCaption"/> من مصدر
    /// تاني. خلط الاتنين في رقم واحد كان هيبقى كذب مرتب.
    /// </summary>
    public string Caption
    {
        get
        {
            if (IsFaulted)
            {
                return $"{PrinterName} — {State}";
            }

            // اللي واقف على المكنة بيسأل «فاضل عليها كام؟» — فالرقم ده
            // بيتقال الأول، والباقي تفاصيل.
            string copies = CopiesPlanned > 0
                ? $"فاضل {CopiesLeft} من {CopiesPlanned} نسخة"
                : $"{CopiesDone} نسخة";

            return $"{PrinterName} — {copies} • {PagesDone}/{PagesPlanned} صفحة ({Percent:0}٪)";
        }
    }
        /// <summary>
    /// دفعة خلصت **والجوب لسه ماشي**.
    ///
    /// ═══ ليه دالة لوحدها مش Record ═══
    ///
    /// <see cref="Record"/> بتاخد <see cref="PrintOutcome"/> — يعني نتيجة
    /// نهائية للقطعة كلها. الدفعة مش نتيجة نهائية: هي جزء اتسلّم من جوب
    /// لسه شغّال وممكن اللي بعده يقع. فلازم تتسجّل من غير ما ندّعي إن
    /// القطعة خلصت ولا إن فيها عطل.
    ///
    /// ⚠ اللي بينده الدالة دي مسؤول إنه **مايحسبش نفس النسخ مرتين**:
    /// اللي اتسجّل هنا لازم يتخصم من الرقم اللي بيروح لـ Record في الآخر.
    /// </summary>
    public void NoteChunk(int copies, int pages)
    {
        if (copies <= 0 && pages <= 0)
        {
            return;
        }

        CopiesDone += Math.Max(0, copies);
        PagesDone += Math.Max(0, pages);
        State = "بتطبع";
        IsFaulted = false;
    }

    /// <summary>
    /// قطعة خلصت. بيتنده من الـ ViewModel بعد كل جوب.
    ///
    /// النتايج اللي مش Delivered مابتتحسبش ورق: NotSent معناها مفيش حاجة
    /// اتحركت أصلًا، وAbandoned معناها إحنا مش عارفين — والبار اللي بيعدّ
    /// شغل مش متأكد منه بيكدب على اللي واقف قدام المكنة.
    /// </summary>
    public void Record(WorkUnit unit, PrintOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(outcome);

        switch (outcome.Kind)
        {
            case PrintResult.Delivered:
                CopiesDone += unit.Copies;
                PagesDone += unit.Weight;
                State = "بتطبع";
                IsFaulted = false;
                break;

            case PrintResult.Skipped:
                break;

            case PrintResult.Cancelled:
                State = "اتوقفت";
                break;

            case PrintResult.BadJob:
                State = "ملف مرفوض";
                break;

            default:
                // NotSent / Abandoned — المكنة هي السبب
                State = outcome.Kind == PrintResult.Abandoned ? "في الشك — راجعها" : "وقفت";
                IsFaulted = true;
                break;
        }
    }

        /// <summary>
    /// الأوردر خلص — الصف بيقفل على كلمة أخيرة.
    ///
    /// ═══ ليه الخطة بتتشال من المقام هنا ═══
    ///
    /// طول الشغل، البار بيقيس التقدم جنب **الخطة** — وده مفيد وإنت واقف
    /// بتتفرّج: بيقولك مين سابق ومين لسه.
    ///
    /// بس الخطة بتتلغي وقت الشغل. المكنة السريعة بتسحب من طوابير التانيين
    /// (سرقة الشغل)، فاللي كان نصيبه ١٩٠ صفحة يطلّع ٣٦١، واللي كان نصيبه
    /// ١٩٠ يطلّع ٥٧ — **مش لأنه فشل، لأن شغله اتاخد منه**.
    ///
    /// ولو سبنا المقام على الخطة القديمة، أوردر خلص ٥٧٠ من ٥٧٠ بيقفل على
    /// بارات بتقول ٣٠٪ و٨٠٪ و١٠٠٪. اللي واقف في المطبعة بيقراها "مكنتين
    /// ماخلصوش" ويروح يدوّر على ورق مش ناقص.
    ///
    /// فلما الأوردر يخلص فعلًا، المقام بيبقى **اللي المكنة عملته** — لأن
    /// ده بقى نصيبها الحقيقي. الأرقام اللي جنب البار (النسخ والصفحات) هي
    /// اللي بتقول التقسيم، والبار بيقول "خلصت ولا لأ" وبس.
    ///
    /// حالتين بيفضلوا صادقين زي ما هما:
    ///   • مكنة فيها عطل → بتفضل حمرا وواقفة عند نسبتها. دي **ماخلصتش**.
    ///   • أوردر اتوقف بالإيد → مفيش حد بيتقال عنه "خلص".
    /// </summary>
    /// <param name="orderCompleted">الأوردر مشي لآخره؟ false لو المستخدم وقّفه.</param>
    public void Finish(bool orderCompleted)
    {
        if (IsFaulted)
        {
            return;
        }

        if (PagesDone == 0)
        {
            State = "ماشتغلتش";
            return;
        }

        if (!orderCompleted)
        {
            State = "اتوقفت";
            return;
        }

        // النسخ بتتعامل زي الصفحات بالظبط: نصيبها الحقيقي هو اللي عملته.
        // من غير السطر ده، مكنة سرقت شغل وخلّصت ١٤ نسخة بدل ١٠ كانت
        // هتقفل على «فاضل ٠ من ١٠» وهي عملت ١٤ — والرقم يبقى كذب.
        PagesPlanned = PagesDone;
        CopiesPlanned = CopiesDone;
        State = "خلصت";
    }

    /// <summary>
    /// المكنة سابت الشغل — التقرير هو اللي بيقول كده مش نتيجة قطعة.
    ///
    /// ═══ ليه ده لازم يبقى موجود ═══
    ///
    /// <see cref="Record"/> بيعلّم العطل من **نتيجة قطعة اتبعتت وفشلت**.
    /// بس المكنة اللي بتتوقف (paused) أو الورق بيخلص منها مابتوصلهاش
    /// قطعة أصلًا — الموزّع بيسأل عن حالتها الأول ويلاقيها مش قادرة،
    /// فبيبطّل يبعتلها. يعني مفيش نتيجة فشل توصل الصف ده أبدًا.
    ///
    /// والنتيجة كانت كارثة صامتة: المكنة تموت في نص الأوردر، الصف
    /// مايعرفش، و<see cref="Finish"/> يلاقي <c>IsFaulted = false</c>
    /// فيقفلها على **أخضر ١٠٠٪ «خلصت»** — واللي في المطبعة يقرا إن
    /// المكنة طلّعت نصيبها كامل وهي ماطلّعتش ولا ورقة بعد ما وقفت.
    ///
    /// التقرير عارف مين سابت الشغل (<c>PrinterTally.Retired</c>)، فبناخد
    /// منه قبل ما نقفل الصفوف.
    /// </summary>
    public void Stopped(string because)
    {
        IsFaulted = true;
        State = string.IsNullOrWhiteSpace(because) ? "وقفت" : because;
    }

    private void Recalculate()
    {
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(PagesLeft));
        OnPropertyChanged(nameof(CopiesLeft));
        OnPropertyChanged(nameof(Caption));
        OnPropertyChanged(nameof(QueueCaption));
        OnPropertyChanged(nameof(HasQueueNews));
    }
}