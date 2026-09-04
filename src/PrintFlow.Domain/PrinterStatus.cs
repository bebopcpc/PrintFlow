namespace PrintFlow.Domain;

public enum PrinterStatus
{
    Ready,
    Offline,
    Error,
    Unknown,

    /// <summary>
    /// الطابور موقوف يدويًا. بتقبل جوبات وبتكوّمها من غير ما تطلّع ورق.
    ///
    /// ⚠ متحطوطة في **آخر** القايمة عن قصد: لو اتحطت في النص، أرقام
    /// الأعضاء اللي بعدها بتتغيّر — وأي حاجة اتحفظت بالرقم بتبقى غلط.
    /// </summary>
    Paused
}