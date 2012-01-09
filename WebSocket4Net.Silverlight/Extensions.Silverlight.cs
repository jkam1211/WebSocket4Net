﻿using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace WebSocket4Net
{
    public static partial class Extensions
    {
        public static string GetString(this Encoding encoding, byte[] buffer)
        {
            return encoding.GetString(buffer, 0, buffer.Length);
        }

        public static string GetPathAndQuery(this Uri uri)
        {
            int pos = uri.OriginalString.IndexOf(uri.Host);
            return uri.OriginalString.Substring(pos + uri.Host.Length);
        }
    }

    public static class MD5
    {
        public static HashAlgorithm Create()
        {
            return new MD5Managed();
        }
    }


    public static class SHA1
    {
        public static HashAlgorithm Create()
        {
            return new SHA1Managed();
        }
    }
}
