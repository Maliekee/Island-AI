using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;

namespace Shared
{
    internal sealed class UpdateFailure : Exception
    {
        internal readonly string Kind;

        internal UpdateFailure(string kind, string detail = "") : base(detail)
        {
            Kind = kind;
        }
    }

    // THE ONE PLACE THE MODS TOUCH THE NETWORK. Nothing else in Island UI or Island Ex opens a
    // connection, and this only runs from the update's buttons (UpdateFlow, UpdatePanel) and,
    // when the player turned the option on, from the one check at startup (ModUpdate).
    //
    // HttpWebRequest, not UnityWebRequest: Unity adds an X-Unity-Version header that cannot be
    // taken off, and a request here carries a User-Agent and nothing else. A certificate is
    // judged per request, so a global callback some other mod set to accept everything has
    // no say here. Redirects are followed by hand, each hop asked for by name and checked
    // first, so a redirect to a host off the list never gets a connection. The mod logs
    // nothing about a request's address beyond the host it refused.
    internal sealed class UpdateNet
    {
        private const int Timeout = 20000;
        private const int MaxHops = 5;

        private volatile HttpWebRequest _request;
        private volatile bool _cancelled;

        internal void Cancel()
        {
            _cancelled = true;
            try { _request?.Abort(); }
            catch (Exception) { /* it was already over */ }
        }

        internal void Get(string address, Func<Uri, bool> allowed, Stream sink, long limit, Action<long> progress)
        {
            var uri = new Uri(address);
            for (int hop = 0; hop <= MaxHops; hop++)
            {
                if (_cancelled)
                {
                    throw new UpdateFailure("cancelled");
                }
                if (!allowed(uri))
                {
                    throw new UpdateFailure("host", uri.Host);
                }
                Uri next = Fetch(uri, sink, limit, progress);
                if (next == null)
                {
                    return;
                }
                uri = next;
            }
            throw new UpdateFailure("redirects");
        }

        // the bytes into sink and null, or the address it was redirected to
        private Uri Fetch(Uri uri, Stream sink, long limit, Action<long> progress)
        {
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "GET";
            request.UserAgent = UpdateRules.UserAgent;
            request.AllowAutoRedirect = false;
            request.KeepAlive = false;
            request.Timeout = Timeout;
            request.ReadWriteTimeout = Timeout;
            request.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => errors == SslPolicyErrors.None;
            _request = request;
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    int code = (int)response.StatusCode;
                    if (code >= 300 && code < 400)
                    {
                        string location = response.Headers["Location"];
                        if (string.IsNullOrEmpty(location))
                        {
                            throw new UpdateFailure("http", code.ToString());
                        }
                        return new Uri(uri, location);
                    }
                    if (code != 200)
                    {
                        throw new UpdateFailure("http", code.ToString());
                    }
                    long declared = response.ContentLength;
                    if (declared > limit)
                    {
                        throw new UpdateFailure("too-big", declared.ToString());
                    }
                    long total = 0;
                    using (Stream body = response.GetResponseStream())
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = body.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            total += read;
                            if (total > limit)
                            {
                                throw new UpdateFailure("too-big", total.ToString());
                            }
                            sink.Write(buffer, 0, read);
                            progress?.Invoke(read);
                        }
                    }
                    if (declared >= 0 && total != declared)
                    {
                        throw new UpdateFailure("interrupted", total + " of " + declared);
                    }
                    return null;
                }
            }
            catch (WebException e)
            {
                throw Translate(e);
            }
            catch (IOException e) when (Network(e))
            {
                throw _cancelled ? new UpdateFailure("cancelled") : new UpdateFailure("interrupted", e.Message);
            }
            finally
            {
                _request = null;
            }
        }

        // a socket's IOException, as against the disk's: the disk's is the caller's to report
        private static bool Network(IOException e)
        {
            return e.InnerException is System.Net.Sockets.SocketException || e.InnerException is ObjectDisposedException
                || e.InnerException is WebException;
        }

        private UpdateFailure Translate(WebException e)
        {
            if (_cancelled || e.Status == WebExceptionStatus.RequestCanceled)
            {
                return new UpdateFailure("cancelled");
            }
            if (e.Status == WebExceptionStatus.ProtocolError && e.Response is HttpWebResponse response)
            {
                int code = (int)response.StatusCode;
                bool limited = code == 429 || (code == 403 && (response.Headers["X-RateLimit-Remaining"] == "0"
                    || response.Headers["Retry-After"] != null));
                response.Close();
                return new UpdateFailure(limited ? "rate-limited" : "http", code.ToString());
            }
            for (Exception inner = e; inner != null; inner = inner.InnerException)
            {
                if (inner is AuthenticationException)
                {
                    return new UpdateFailure("secure", inner.Message);
                }
            }
            switch (e.Status)
            {
                case WebExceptionStatus.TrustFailure:
                case WebExceptionStatus.SecureChannelFailure:
                    return new UpdateFailure("secure", e.Status.ToString());
                case WebExceptionStatus.Timeout:
                    return new UpdateFailure("timeout", "");
                default:
                    return new UpdateFailure("offline", e.Status.ToString());
            }
        }
    }
}
