using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace SchoolInventoryManagement.Web.Helpers
{
    // Every upload in the system goes through here, so the size limit and
    // the allowed file types live in one place. The forms check the size in
    // the browser too (wwwroot/js/site.js, data-max-bytes), but that is only
    // a convenience -- it can be switched off. These checks cannot.
    public static class FileUploads
    {
        public const int MaxMegabytes = 3;
        public const long MaxBytes = MaxMegabytes * 1024 * 1024;

        // Cap on a whole upload form post: two files at the limit plus the
        // other fields. Anything bigger is cut off while it is still
        // arriving, instead of being read in full and then refused. Used by
        // [RequestSizeLimit] / [RequestFormLimits] on the upload actions.
        public const long MaxRequestBytes = 8 * 1024 * 1024;

        // Asset photos.
        public static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        public const string ImageAccept = "image/jpeg,image/png,image/webp";

        // Warranty card or receipt: a photo of it, or a PDF.
        public static readonly string[] DocumentExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".pdf" };
        public const string DocumentAccept = "image/jpeg,image/png,image/webp,application/pdf";

        // Throws ArgumentException (shown on the form) if the file is too
        // big, has the wrong extension, or is not really what its extension
        // says. No file chosen is fine.
        public static void Validate(IFormFile? file, string[] allowedExtensions, string what)
        {
            if (file is null || file.Length == 0)
                return;

            // file.Length is the number of bytes actually received, not
            // anything the browser claims, so it cannot be faked.
            if (file.Length > MaxBytes)
                throw new ArgumentException(
                    $"The {what} is {Megabytes(file.Length)} MB. The limit is {MaxMegabytes} MB.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext))
                throw new ArgumentException(
                    $"The {what} must be one of: {string.Join(", ", allowedExtensions.Select(e => e.TrimStart('.').ToUpperInvariant()).Distinct())}.");

            // A renamed file (say, a program saved as photo.jpg) passes the
            // extension check, so look at the first bytes too: every real
            // JPG, PNG, WEBP and PDF starts with its own fixed signature.
            if (!ContentMatches(file, ext))
                throw new ArgumentException(
                    $"The {what} is not a real {ext.TrimStart('.').ToUpperInvariant()} file. Choose another file.");
        }

        // Rounded up to one decimal, so a file just over the limit reads
        // "3.1 MB" rather than "3.0 MB".
        private static string Megabytes(long bytes) =>
            (Math.Ceiling(bytes / 1024d / 1024d * 10) / 10).ToString("0.0");

        // Any image signature is accepted for an image extension (phones do
        // save PNGs as .jpg now and then -- still a harmless picture); a
        // .pdf must really be a PDF.
        private static bool ContentMatches(IFormFile file, string ext)
        {
            var head = new byte[12];
            int read;
            using (var stream = file.OpenReadStream())
                read = stream.Read(head, 0, head.Length);

            bool StartsWith(params byte[] signature) =>
                read >= signature.Length && head.Take(signature.Length).SequenceEqual(signature);

            var isJpg = StartsWith(0xFF, 0xD8, 0xFF);
            var isPng = StartsWith(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
            // "RIFF", four size bytes, then "WEBP"
            var isWebp = StartsWith(0x52, 0x49, 0x46, 0x46) && read >= 12 &&
                         head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50;
            var isPdf = StartsWith(0x25, 0x50, 0x44, 0x46, 0x2D); // "%PDF-"

            return ext == ".pdf" ? isPdf : (isJpg || isPng || isWebp);
        }

        // Saves under wwwroot/{folder} with a random name and returns the
        // URL to store, or null when no file was chosen. Call Validate first.
        //
        // WebRootPath (the real, absolute path to wwwroot) rather than a bare
        // "wwwroot" string -- a relative path resolves against the process's
        // current working directory, which is only the project folder by
        // coincidence when running under the Visual Studio debugger.
        public static async Task<string?> SaveAsync(IFormFile? file, string webRootPath, string folder)
        {
            if (file is null || file.Length == 0)
                return null;

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var fileName = $"{Guid.NewGuid():N}{ext}";

            var fullFolder = Path.Combine(webRootPath, folder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(fullFolder);

            using (var stream = new FileStream(Path.Combine(fullFolder, fileName), FileMode.Create))
                await file.CopyToAsync(stream);

            return $"/{folder}/{fileName}";
        }
    }
}
