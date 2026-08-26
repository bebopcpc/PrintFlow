using PrintFlow.Domain;

namespace PrintFlow.Application;

public interface IImageToPdfConverter
{
    /// <summary>بيحوّل صورة لـ PDF من صفحة واحدة.</summary>
    MergeResult Convert(ImageConvertRequest request);
}
