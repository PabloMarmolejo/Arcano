using System.Runtime.InteropServices;

namespace Arcano.Motor.Pdfium;

/// <summary>
/// Declaraciones de PDFium (fpdfview.h, fpdf_text.h, fpdf_edit.h, fpdf_annot.h,
/// fpdf_attachment.h, fpdf_doc.h). PDFium no es seguro entre hilos: todo acceso
/// pasa por <see cref="Pdfium.Candado"/>.
/// </summary>
internal static unsafe partial class PdfiumNativo
{
    private const string Lib = "pdfium";

    public const int FormatoBgra = 4;
    public const int BanderaAnotaciones = 0x01;
    public const int BanderaLcdText = 0x02;
    public const int BanderaImpresion = 0x800;

    public const int ObjetoTexto = 1;
    public const int ObjetoImagen = 3;
    public const int ObjetoForma = 5;

    public const uint ErrorPassword = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct RectF
    {
        public float Left;
        public float Top;
        public float Right;
        public float Bottom;
    }

    [DllImport(Lib)] public static extern void FPDF_InitLibrary();
    [DllImport(Lib)] public static extern uint FPDF_GetLastError();

    [DllImport(Lib)] public static extern IntPtr FPDF_LoadMemDocument64(void* data, nuint size, byte* password);
    [DllImport(Lib)] public static extern void FPDF_CloseDocument(IntPtr document);
    [DllImport(Lib)] public static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport(Lib)] public static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
    [DllImport(Lib)] public static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Lib)] public static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport(Lib)] public static extern float FPDF_GetPageHeightF(IntPtr page);

    [DllImport(Lib)]
    public static extern int FPDF_PageToDevice(IntPtr page, int startX, int startY, int sizeX, int sizeY,
        int rotate, double pageX, double pageY, out int deviceX, out int deviceY);

    [DllImport(Lib)] public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, void* firstScan, int stride);
    [DllImport(Lib)] public static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Lib)] public static extern void FPDFBitmap_Destroy(IntPtr bitmap);

    [DllImport(Lib)]
    public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY,
        int sizeX, int sizeY, int rotate, int flags);

    [DllImport(Lib)] public static extern IntPtr FPDFText_LoadPage(IntPtr page);
    [DllImport(Lib)] public static extern void FPDFText_ClosePage(IntPtr textPage);
    [DllImport(Lib)] public static extern int FPDFText_CountChars(IntPtr textPage);
    [DllImport(Lib)] public static extern uint FPDFText_GetUnicode(IntPtr textPage, int index);
    [DllImport(Lib)] public static extern int FPDFText_IsGenerated(IntPtr textPage, int index);
    [DllImport(Lib)] public static extern int FPDFText_GetLooseCharBox(IntPtr textPage, int index, out RectF rect);

    [DllImport(Lib)] public static extern int FPDFPage_CountObjects(IntPtr page);
    [DllImport(Lib)] public static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);
    [DllImport(Lib)] public static extern int FPDFPageObj_GetType(IntPtr obj);
    [DllImport(Lib)] public static extern int FPDFPageObj_GetBounds(IntPtr obj, out float left, out float bottom, out float right, out float top);
    [DllImport(Lib)] public static extern int FPDFFormObj_CountObjects(IntPtr formObj);
    [DllImport(Lib)] public static extern IntPtr FPDFFormObj_GetObject(IntPtr formObj, uint index);

    [DllImport(Lib)] public static extern int FPDFPage_GetAnnotCount(IntPtr page);
    [DllImport(Lib)] public static extern int FPDFDoc_GetAttachmentCount(IntPtr document);
    [DllImport(Lib)] public static extern uint FPDF_GetMetaText(IntPtr document, byte* tag, void* buffer, uint length);
}
