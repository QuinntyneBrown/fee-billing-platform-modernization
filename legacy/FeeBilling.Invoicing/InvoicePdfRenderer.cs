using System;
using System.Configuration;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using log4net;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FeeBilling.Invoicing
{
    public class InvoicePdfRenderer : IInvoicePdfRenderer
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(InvoicePdfRenderer));

        // GDI+ objects cached for the lifetime of the app pool.
        private static Image _logo;
        private static readonly object LogoLock = new object();

        public byte[] Render(InvoiceDocument invoice)
        {
            var document = new PdfDocument();
            document.Info.Title = "Invoice " + invoice.InvoiceId;

            var page = document.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var titleFont = new XFont("Arial", 18, XFontStyle.Bold);
                var bodyFont = new XFont("Arial", 11, XFontStyle.Regular);

                using (var logoStream = new MemoryStream())
                {
                    GetLogo().Save(logoStream, ImageFormat.Png);
                    logoStream.Position = 0;
                    gfx.DrawImage(XImage.FromStream(logoStream), 40, 30, 120, 40);
                }
                gfx.DrawString("Advisory Fee Invoice", titleFont, XBrushes.Black, new XPoint(40, 110));

                var y = 150;
                DrawLine(gfx, bodyFont, "Firm", invoice.FirmName, ref y);
                DrawLine(gfx, bodyFont, "Account", invoice.AccountNumber + "  " + invoice.AccountName, ref y);
                if (!string.IsNullOrEmpty(invoice.HouseholdCode))
                {
                    DrawLine(gfx, bodyFont, "Household", invoice.HouseholdCode, ref y);
                }
                DrawLine(gfx, bodyFont, "Period ending", invoice.PeriodEnd.ToShortDateString(), ref y);   // server culture
                DrawLine(gfx, bodyFont, "Fee schedule", invoice.ScheduleCode, ref y);
                DrawLine(gfx, bodyFont, "Amount due", invoice.Amount.ToString("C"), ref y);                // server culture
            }

            using (var stream = new MemoryStream())
            {
                document.Save(stream, false);
                Log.Debug("Rendered invoice " + invoice.InvoiceId + " (" + stream.Length + " bytes)");
                return stream.ToArray();
            }
        }

        private static void DrawLine(XGraphics gfx, XFont font, string label, string value, ref int y)
        {
            gfx.DrawString(label + ":", font, XBrushes.Gray, new XPoint(40, y));
            gfx.DrawString(value ?? string.Empty, font, XBrushes.Black, new XPoint(160, y));
            y += 22;
        }

        private static Image GetLogo()
        {
            lock (LogoLock)
            {
                if (_logo != null) return _logo;

                var path = ConfigurationManager.AppSettings["Invoice.LogoPath"];
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    _logo = Image.FromFile(path);
                }
                else
                {
                    // No logo configured: draw a placeholder with System.Drawing.
                    var bitmap = new Bitmap(300, 100);
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.White);
                        g.FillRectangle(new SolidBrush(Color.FromArgb(0, 70, 127)), 0, 0, 300, 100);
                        g.DrawString("FeeBilling", new Font("Arial", 28, FontStyle.Bold), Brushes.White, 20, 25);
                    }
                    _logo = bitmap;
                }
                return _logo;
            }
        }
    }
}
