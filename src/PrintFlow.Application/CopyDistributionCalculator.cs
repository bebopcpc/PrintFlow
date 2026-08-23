using PrintFlow.Domain;

namespace PrintFlow.Application;

/// <summary>
/// منطق بحت: يوزّع عدد نسخ إجمالي على مجموعة برنترات بالتساوي قدر الإمكان.
/// الباقي (remainder) يتوزع على أول برنترات في القائمة بواحد إضافي لكل واحدة.
/// </summary>
public static class CopyDistributionCalculator
{
    public static List<CopyDistribution> Distribute(int totalCopies, List<string> printerNames)
    {
        if (totalCopies <= 0)
        {
            throw new ArgumentException("عدد النسخ لازم يكون أكبر من صفر.", nameof(totalCopies));
        }

        if (printerNames == null || printerNames.Count == 0)
        {
            throw new ArgumentException("لازم برنتر واحدة على الأقل.", nameof(printerNames));
        }

        int baseCopies = totalCopies / printerNames.Count;
        int remainder = totalCopies % printerNames.Count;

        var result = new List<CopyDistribution>();
        for (int i = 0; i < printerNames.Count; i++)
        {
            int copies = baseCopies + (i < remainder ? 1 : 0);
            result.Add(new CopyDistribution { PrinterName = printerNames[i], CopiesAssigned = copies });
        }

        return result;
    }
}