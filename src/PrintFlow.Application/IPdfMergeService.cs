using PrintFlow.Domain;

namespace PrintFlow.Application;

public interface IPdfMergeService
{
    /// <summary>
    /// بيدمج الملفات ويحط عليها الترقيم والعلامة المائية والنص المخصص حسب الطلب.
    /// </summary>
    MergeResult Merge(MergeRequest request);
}
