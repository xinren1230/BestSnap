using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace BestSnap.Plugins
{
    /// <summary>
    /// Serves the recording from a host that is NOT folder-mapped in WebView2.
    /// Folder mapping swallows WebResourceRequested (WebView2Feedback #1847, #2685).
    /// </summary>
    public sealed class HostedMediaResponse : IDisposable
    {
        public int Status { get; init; }
        public string Reason { get; init; }
        public string Headers { get; init; }
        public Stream Body { get; init; }
        public long ContentLength { get; init; }

        public void Dispose() => Body?.Dispose();
    }

    public static class HostedMediaServe
    {
        public const string SourcePrefix = "/source/";

        public static bool IsMediaHostUri(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) return false;
            try
            {
                var u = new Uri(uri, UriKind.Absolute);
                return string.Equals(u.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(u.Host, RecordingSavePath.MediaHost, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool TryParseSourceUri(string uri, out string decodedName)
        {
            decodedName = null;
            if (!IsMediaHostUri(uri)) return false;
            try
            {
                var u = new Uri(uri, UriKind.Absolute);
                string path = u.AbsolutePath ?? "";
                if (!path.StartsWith(SourcePrefix, StringComparison.OrdinalIgnoreCase)) return false;
                string rest = path.Substring(SourcePrefix.Length);
                if (string.IsNullOrEmpty(rest)) return false;
                decodedName = Uri.UnescapeDataString(rest);
                return !string.IsNullOrEmpty(decodedName);
            }
            catch { return false; }
        }

        public static bool NameMatchesSource(string decodedName, string sourcePath)
        {
            if (string.IsNullOrEmpty(decodedName) || string.IsNullOrWhiteSpace(sourcePath)) return false;
            try
            {
                return string.Equals(decodedName, Path.GetFileName(sourcePath), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Build a 200/206/404/416/204 response. Body is a stream the caller must not dispose
        /// until the WebView has finished reading it (WebView2 takes ownership).
        /// </summary>
        public static HostedMediaResponse Serve(string method, string requestUri, string rangeHeader, string sourcePath)
        {
            method = string.IsNullOrWhiteSpace(method) ? "GET" : method.Trim().ToUpperInvariant();
            if (method == "OPTIONS")
                return Empty(204, "No Content", 0, extra: "Access-Control-Allow-Methods: GET, HEAD, OPTIONS\r\nAccess-Control-Allow-Headers: Range, Content-Type\r\n");

            if (!TryParseSourceUri(requestUri, out string name) || !NameMatchesSource(name, sourcePath))
                return Empty(404, "Not Found", 0);

            if (!RecordingSavePath.IsUsableMedia(sourcePath))
                return Empty(404, "Not Found", 0);

            long fileLength;
            try { fileLength = new FileInfo(sourcePath).Length; }
            catch { return Empty(404, "Not Found", 0); }

            string mime = MimeFor(sourcePath);
            long start = 0, end = fileLength - 1;
            bool ranged = false;
            if (!string.IsNullOrWhiteSpace(rangeHeader) && rangeHeader.IndexOf(',') < 0)
            {
                if (!TryParseByteRange(rangeHeader, fileLength, out start, out end))
                    return Empty(416, "Range Not Satisfiable", 0,
                        extra: "Content-Range: bytes */" + fileLength.ToString(CultureInfo.InvariantCulture) + "\r\n");
                ranged = true;
            }

            long bodyLen = end - start + 1;
            int status = ranged ? 206 : 200;
            string reason = ranged ? "Partial Content" : "OK";
            var headers = new StringBuilder();
            headers.Append("Content-Type: ").Append(mime).Append("\r\n");
            headers.Append("Content-Length: ").Append(bodyLen.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            headers.Append("Accept-Ranges: bytes\r\n");
            headers.Append("Access-Control-Allow-Origin: ").Append(RecordingSavePath.EditorOrigin).Append("\r\n");
            headers.Append("Access-Control-Expose-Headers: Content-Length, Content-Range, Accept-Ranges\r\n");
            if (ranged)
            {
                headers.Append("Content-Range: bytes ")
                    .Append(start.ToString(CultureInfo.InvariantCulture)).Append('-')
                    .Append(end.ToString(CultureInfo.InvariantCulture)).Append('/')
                    .Append(fileLength.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            }

            Stream body;
            if (method == "HEAD")
                body = new MemoryStream(Array.Empty<byte>());
            else
            {
                var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                body = (start == 0 && bodyLen == fileLength) ? fs : new SliceStream(fs, start, bodyLen);
            }

            return new HostedMediaResponse
            {
                Status = status,
                Reason = reason,
                Headers = headers.ToString(),
                Body = body,
                ContentLength = bodyLen
            };
        }

        public static bool TryParseByteRange(string header, long fileLength, out long start, out long endInclusive)
        {
            start = 0;
            endInclusive = fileLength > 0 ? fileLength - 1 : -1;
            if (fileLength <= 0 || string.IsNullOrWhiteSpace(header)) return false;
            string h = header.Trim();
            const string prefix = "bytes=";
            if (!h.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string spec = h.Substring(prefix.Length).Trim();
            int dash = spec.IndexOf('-');
            if (dash < 0) return false;
            string a = spec.Substring(0, dash).Trim();
            string b = spec.Substring(dash + 1).Trim();
            try
            {
                if (a.Length == 0)
                {
                    if (b.Length == 0) return false;
                    long suffix = long.Parse(b, CultureInfo.InvariantCulture);
                    if (suffix <= 0) return false;
                    start = Math.Max(0, fileLength - suffix);
                    endInclusive = fileLength - 1;
                }
                else
                {
                    start = long.Parse(a, CultureInfo.InvariantCulture);
                    endInclusive = b.Length == 0 ? fileLength - 1 : long.Parse(b, CultureInfo.InvariantCulture);
                    if (endInclusive >= fileLength) endInclusive = fileLength - 1;
                }
                if (start < 0 || start >= fileLength || endInclusive < start) return false;
                return true;
            }
            catch { return false; }
        }

        static HostedMediaResponse Empty(int status, string reason, long length, string extra = null)
        {
            var headers = new StringBuilder();
            headers.Append("Content-Length: ").Append(length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            headers.Append("Access-Control-Allow-Origin: ").Append(RecordingSavePath.EditorOrigin).Append("\r\n");
            headers.Append("Access-Control-Expose-Headers: Content-Length, Content-Range, Accept-Ranges\r\n");
            if (!string.IsNullOrEmpty(extra)) headers.Append(extra);
            return new HostedMediaResponse
            {
                Status = status,
                Reason = reason,
                Headers = headers.ToString(),
                Body = new MemoryStream(Array.Empty<byte>()),
                ContentLength = length
            };
        }

        public static string MimeFor(string path)
        {
            string e = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            return e switch
            {
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mov" => "video/quicktime",
                ".gif" => "image/gif",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                _ => "application/octet-stream"
            };
        }

        sealed class SliceStream : Stream
        {
            readonly Stream _inner;
            readonly long _start;
            readonly long _length;
            long _pos;

            public SliceStream(Stream inner, long start, long length)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _start = start;
                _length = length;
                if (_inner.CanSeek) _inner.Seek(start, SeekOrigin.Begin);
                _pos = 0;
            }

            public override bool CanRead => true;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position
            {
                get => _pos;
                set => Seek(value, SeekOrigin.Begin);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_pos >= _length) return 0;
                int n = (int)Math.Min(count, _length - _pos);
                int r = _inner.Read(buffer, offset, n);
                _pos += r;
                return r;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                long next = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => _pos + offset,
                    SeekOrigin.End => _length + offset,
                    _ => _pos
                };
                if (next < 0) next = 0;
                if (next > _length) next = _length;
                if (_inner.CanSeek) _inner.Seek(_start + next, SeekOrigin.Begin);
                _pos = next;
                return _pos;
            }

            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
