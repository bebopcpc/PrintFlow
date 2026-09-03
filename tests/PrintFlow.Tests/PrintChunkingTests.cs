using PrintFlow.Domain;

namespace PrintFlow.Tests;

/// <summary>
/// بيحمي تقسيم الجوبات الضخمة.
///
/// القاعدة الذهبية اللي التستات دي موجودة عشانها: **مجموع الدفعات لازم
/// يساوي عدد النسخ بالظبط**. أي غلطة هنا معناها نسخة ضايعة أو نسخة زيادة
/// في أوردر حقيقي — وحد في المطبعة هيعدّ الورق ويلاقيه ناقص.
/// </summary>
public class PrintChunkingTests
{
    // ══════════ الحالة اللي التقسيم اتعمل عشانها ══════════

    /// <summary>
    /// السيناريو اللي وقع فعلًا: مستند ١٨٠ صفحة، والمكنة الواحدة ماخدة
    /// كل النسخ. من غير تقسيم بيتبعت ١٨٠٠ صفحة في أمر واحد.
    /// </summary>
    [Fact]
    public void Ten_Copies_Of_A_Big_Document_Are_Split_Into_Single_Copies()
    {
        var chunks = PrintChunking.Split(copies: 10, pagesPerCopy: 180);

        Assert.Equal(10, chunks.Count);
        Assert.All(chunks, copies => Assert.Equal(1, copies));
    }

    [Fact]
    public void A_Normal_Order_Stays_One_Job()
    {
        // ٢٠ نسخة × ١٢ صفحة = ٢٤٠ صفحة، تحت الحد
        var chunks = PrintChunking.Split(copies: 20, pagesPerCopy: 12);

        Assert.Single(chunks);
        Assert.Equal(20, chunks[0]);
    }

    [Fact]
    public void A_Flyer_In_Bulk_Splits_By_The_Page_Budget()
    {
        // ٣٠٠ ÷ ٢ = ١٥٠ نسخة في الدفعة
        var chunks = PrintChunking.Split(copies: 500, pagesPerCopy: 2);

        Assert.Equal(500, chunks.Sum());
        Assert.Equal(150, chunks[0]);
    }

    // ══════════ القاعدة الذهبية ══════════

    /// <summary>
    /// مفيش نسخة بتضيع ولا بتتكرر — في أي مدخل مهما كان.
    /// ٦٩٦٠ حالة، لأن الغلطة هنا بتتحوّل لورق ناقص عند العميل.
    /// </summary>
    [Fact]
    public void Chunks_Always_Add_Up_To_The_Copies_Asked_For()
    {
        for (int copies = 1; copies <= 120; copies++)
        {
            for (int pages = 0; pages <= 400; pages += 7)
            {
                var chunks = PrintChunking.Split(copies, pages);

                Assert.Equal(copies, chunks.Sum());
                Assert.NotEmpty(chunks);
                Assert.All(chunks, piece => Assert.True(piece > 0));
            }
        }
    }

    // ══════════ الحالات اللي بنسيب فيها التقسيم ══════════

    /// <summary>
    /// عدد الصفحات مجهول = مانقدرش نحسب. التخمين هنا ممكن يقسّم أوردر
    /// صغير لعشرين جوب من غير أي داعي.
    /// </summary>
    [Fact]
    public void Unknown_Page_Count_Stays_One_Job()
    {
        var chunks = PrintChunking.Split(copies: 50, pagesPerCopy: 0);

        Assert.Single(chunks);
        Assert.Equal(50, chunks[0]);
    }

    [Fact]
    public void One_Copy_Is_Never_Split_However_Big()
    {
        // نسخة واحدة ٥٠٠ صفحة — مالهاش حل تاني، وتقسيمها معناه
        // كسر النسخة نفسها وده ممنوع
        var chunks = PrintChunking.Split(copies: 1, pagesPerCopy: 500);

        Assert.Single(chunks);
        Assert.Equal(1, chunks[0]);
    }

    [Fact]
    public void A_Copy_Bigger_Than_The_Budget_Gets_Its_Own_Job()
    {
        var chunks = PrintChunking.Split(copies: 4, pagesPerCopy: 500);

        Assert.Equal(4, chunks.Count);
        Assert.All(chunks, copies => Assert.Equal(1, copies));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Zero_Or_Negative_Copies_Come_Back_Untouched(int copies)
    {
        // القرار ده مش هنا — PdfPrintService بيرفضها قبل ما توصل
        var chunks = PrintChunking.Split(copies, pagesPerCopy: 100);

        Assert.Single(chunks);
        Assert.Equal(copies, chunks[0]);
    }

    [Fact]
    public void The_Last_Chunk_Carries_The_Remainder()
    {
        // ٣٠٠ ÷ ١٠٠ = ٣ نسخ في الدفعة، و٧ نسخ = 3 + 3 + 1
        var chunks = PrintChunking.Split(copies: 7, pagesPerCopy: 100);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(3, chunks[0]);
        Assert.Equal(3, chunks[1]);
        Assert.Equal(1, chunks[2]);
    }

    /// <summary>
    /// كل دفعة لازم تفضل تحت الحد — إلا لو النسخة الواحدة أصلًا أكبر منه.
    /// </summary>
    [Fact]
    public void No_Chunk_Goes_Over_The_Budget_Unless_One_Copy_Already_Does()
    {
        for (int pages = 1; pages <= PrintChunking.MaxPagesPerJob; pages++)
        {
            foreach (int copies in (int[])[2, 5, 17, 99])
            {
                var chunks = PrintChunking.Split(copies, pages);

                Assert.All(chunks, piece =>
                    Assert.True(piece * pages <= PrintChunking.MaxPagesPerJob,
                        $"دفعة {piece} × {pages} صفحة عدّت الحد"));
            }
        }
    }
}
