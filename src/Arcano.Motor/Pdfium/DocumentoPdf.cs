using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using Arcano.Motor.Modelo;
using Arcano.Motor.Texto;
using static Arcano.Motor.Pdfium.PdfiumNativo;

namespace Arcano.Motor.Pdfium;

public sealed class PdfProtegidoException(string mensaje) : Exception(mensaje);
public sealed class PdfInvalidoException(string mensaje) : Exception(mensaje);

/// <summary>
/// Documento PDF abierto con PDFium. Todas las coordenadas que entrega son
/// <see cref="RectN"/> sobre la página ya rotada, igual que la ve el usuario.
/// </summary>
public sealed unsafe class DocumentoPdf : IDisposable
{
    // Escala para FPDF_PageToDevice, que devuelve enteros: centésimas de punto.
    private const int Escala = 100;
    private const int LadoMaximoPx = 14000;

    private readonly void* _datos;
    private IntPtr _doc;

    public int Paginas { get; }

    private DocumentoPdf(void* datos, IntPtr doc, int paginas)
    {
        _datos = datos;
        _doc = doc;
        Paginas = paginas;
    }

    public static DocumentoPdf Abrir(byte[] contenido)
    {
        Pdfium.Asegurar();
        var datos = NativeMemory.Alloc((nuint)contenido.Length);
        contenido.AsSpan().CopyTo(new Span<byte>(datos, contenido.Length));
        lock (Pdfium.Candado)
        {
            var doc = FPDF_LoadMemDocument64(datos, (nuint)contenido.Length, null);
            if (doc == IntPtr.Zero)
            {
                var error = FPDF_GetLastError();
                NativeMemory.Free(datos);
                if (error == ErrorPassword)
                    throw new PdfProtegidoException("El PDF está protegido con contraseña. Quite la protección antes de testarlo.");
                throw new PdfInvalidoException($"No se pudo abrir el PDF (código {error}). El archivo puede estar dañado.");
            }
            return new DocumentoPdf(datos, doc, FPDF_GetPageCount(doc));
        }
    }

    public static DocumentoPdf Abrir(string ruta) => Abrir(File.ReadAllBytes(ruta));

    public (double AnchoPt, double AltoPt) TamanoPagina(int pagina)
    {
        lock (Pdfium.Candado)
        {
            var p = CargarPagina(pagina);
            try { return (FPDF_GetPageWidthF(p), FPDF_GetPageHeightF(p)); }
            finally { FPDF_ClosePage(p); }
        }
    }

    /// <summary>Texto propio de la página con la caja de cada carácter.</summary>
    public TextoPagina ExtraerTexto(int pagina)
    {
        lock (Pdfium.Candado)
        {
            var p = CargarPagina(pagina);
            var tp = FPDFText_LoadPage(p);
            try
            {
                var conversor = new Conversor(p);
                var total = tp == IntPtr.Zero ? 0 : FPDFText_CountChars(tp);
                var caracteres = new List<Caracter>(total);
                for (var i = 0; i < total; i++)
                {
                    var codigo = FPDFText_GetUnicode(tp, i);
                    var generado = FPDFText_IsGenerated(tp, i) == 1;
                    string valor;
                    if (codigo < 0x20 || codigo == 0xFFFE || codigo == 0xFFFF)
                    {
                        valor = " ";
                        generado = true;
                    }
                    else if (char.IsHighSurrogate((char)codigo) && i + 1 < total
                             && char.IsLowSurrogate((char)FPDFText_GetUnicode(tp, i + 1)))
                    {
                        valor = new string([(char)codigo, (char)FPDFText_GetUnicode(tp, i + 1)]);
                        i++;
                    }
                    else if (char.IsSurrogate((char)codigo))
                    {
                        valor = "�";
                    }
                    else
                    {
                        valor = ((char)codigo).ToString();
                    }

                    RectN? caja = null;
                    if (!generado && FPDFText_GetLooseCharBox(tp, i, out var r) != 0)
                        caja = conversor.ARect(r.Left, r.Top, r.Right, r.Bottom);
                    caracteres.Add(new Caracter(valor, caja));
                }
                var origen = caracteres.Any(c => c.Caja is not null) ? OrigenTexto.Nativo : OrigenTexto.SinTexto;
                return new TextoPagina(pagina, origen, caracteres);
            }
            finally
            {
                if (tp != IntPtr.Zero) FPDFText_ClosePage(tp);
                FPDF_ClosePage(p);
            }
        }
    }

    /// <summary>
    /// Imágenes de la página. Se descartan las que cubren casi toda la hoja
    /// (el fondo de un escaneo) y las diminutas.
    /// </summary>
    public IReadOnlyList<ImagenPagina> ExtraerImagenes(int pagina)
    {
        lock (Pdfium.Candado)
        {
            var p = CargarPagina(pagina);
            try
            {
                var conversor = new Conversor(p);
                var imagenes = new List<ImagenPagina>();
                var total = FPDFPage_CountObjects(p);
                for (var i = 0; i < total; i++)
                {
                    var obj = FPDFPage_GetObject(p, i);
                    var tipo = FPDFPageObj_GetType(obj);
                    if (tipo != ObjetoImagen && !(tipo == ObjetoForma && ContieneImagen(obj, 0))) continue;
                    if (FPDFPageObj_GetBounds(obj, out var l, out var b, out var r, out var t) == 0) continue;
                    var caja = conversor.ARect(l, t, r, b).Acotar();
                    if (caja.Area is > 0.85 or < 0.0005) continue;
                    imagenes.Add(new ImagenPagina(pagina, caja));
                }
                return imagenes;
            }
            finally { FPDF_ClosePage(p); }
        }
    }

    private static bool ContieneImagen(IntPtr forma, int profundidad)
    {
        if (profundidad > 8) return false;
        var total = FPDFFormObj_CountObjects(forma);
        for (var i = 0; i < total; i++)
        {
            var hijo = FPDFFormObj_GetObject(forma, (uint)i);
            var tipo = FPDFPageObj_GetType(hijo);
            if (tipo == ObjetoImagen) return true;
            if (tipo == ObjetoForma && ContieneImagen(hijo, profundidad + 1)) return true;
        }
        return false;
    }

    /// <summary>Dibuja la página en un mapa de bits BGRA opaco a la resolución pedida.</summary>
    public SKBitmap Renderizar(int pagina, int dpi)
    {
        lock (Pdfium.Candado)
        {
            var p = CargarPagina(pagina);
            try
            {
                var ancho = FPDF_GetPageWidthF(p);
                var alto = FPDF_GetPageHeightF(p);
                var factor = dpi / 72.0;
                var lado = Math.Max(ancho, alto) * factor;
                if (lado > LadoMaximoPx) factor *= LadoMaximoPx / lado;
                var anchoPx = Math.Max(1, (int)Math.Round(ancho * factor));
                var altoPx = Math.Max(1, (int)Math.Round(alto * factor));

                var bitmap = new SKBitmap(new SKImageInfo(anchoPx, altoPx, SKColorType.Bgra8888, SKAlphaType.Premul));
                var bmp = FPDFBitmap_CreateEx(anchoPx, altoPx, FormatoBgra, (void*)bitmap.GetPixels(), bitmap.RowBytes);
                if (bmp == IntPtr.Zero)
                {
                    bitmap.Dispose();
                    throw new InvalidOperationException($"No se pudo crear el lienzo para la página {pagina + 1}.");
                }
                try
                {
                    FPDFBitmap_FillRect(bmp, 0, 0, anchoPx, altoPx, 0xFFFFFFFF);
                    FPDF_RenderPageBitmap(bmp, p, 0, 0, anchoPx, altoPx, 0, BanderaAnotaciones);
                }
                finally { FPDFBitmap_Destroy(bmp); }
                bitmap.NotifyPixelsChanged();
                return bitmap;
            }
            finally { FPDF_ClosePage(p); }
        }
    }

    public int ContarAnotaciones(int pagina)
    {
        lock (Pdfium.Candado)
        {
            var p = CargarPagina(pagina);
            try { return FPDFPage_GetAnnotCount(p); }
            finally { FPDF_ClosePage(p); }
        }
    }

    public int ContarAdjuntos()
    {
        lock (Pdfium.Candado) return FPDFDoc_GetAttachmentCount(_doc);
    }

    public string Metadato(string clave)
    {
        lock (Pdfium.Candado)
        {
            var tag = Encoding.ASCII.GetBytes(clave + "\0");
            fixed (byte* pt = tag)
            {
                var largo = FPDF_GetMetaText(_doc, pt, null, 0);
                if (largo <= 2) return "";
                var buffer = new byte[largo];
                fixed (byte* pb = buffer) FPDF_GetMetaText(_doc, pt, pb, largo);
                return Encoding.Unicode.GetString(buffer, 0, (int)largo - 2);
            }
        }
    }

    private IntPtr CargarPagina(int pagina)
    {
        ObjectDisposedException.ThrowIf(_doc == IntPtr.Zero, this);
        if (pagina < 0 || pagina >= Paginas)
            throw new ArgumentOutOfRangeException(nameof(pagina), $"La página {pagina + 1} no existe.");
        var p = FPDF_LoadPage(_doc, pagina);
        if (p == IntPtr.Zero)
            throw new PdfInvalidoException($"No se pudo leer la página {pagina + 1}.");
        return p;
    }

    public void Dispose()
    {
        lock (Pdfium.Candado)
        {
            if (_doc == IntPtr.Zero) return;
            FPDF_CloseDocument(_doc);
            _doc = IntPtr.Zero;
            NativeMemory.Free(_datos);
        }
    }

    /// <summary>Convierte coordenadas de página (puntos, origen abajo) a <see cref="RectN"/>.</summary>
    private readonly struct Conversor
    {
        private readonly IntPtr _pagina;
        private readonly int _ancho;
        private readonly int _alto;

        public Conversor(IntPtr pagina)
        {
            _pagina = pagina;
            _ancho = Math.Max(1, (int)Math.Round(FPDF_GetPageWidthF(pagina) * Escala));
            _alto = Math.Max(1, (int)Math.Round(FPDF_GetPageHeightF(pagina) * Escala));
        }

        public RectN ARect(double izquierda, double arriba, double derecha, double abajo)
        {
            FPDF_PageToDevice(_pagina, 0, 0, _ancho, _alto, 0, izquierda, arriba, out var x1, out var y1);
            FPDF_PageToDevice(_pagina, 0, 0, _ancho, _alto, 0, derecha, abajo, out var x2, out var y2);
            return RectN.DesdeBordes((double)x1 / _ancho, (double)y1 / _alto, (double)x2 / _ancho, (double)y2 / _alto);
        }
    }
}
