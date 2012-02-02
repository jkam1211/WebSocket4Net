﻿using System;
using System.Collections.Generic;
#if !SILVERLIGHT
using System.Collections.Specialized;
#endif
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SuperSocket.ClientEngine;

namespace WebSocket4Net.Protocol
{
    /// <summary>
    /// http://tools.ietf.org/html/draft-ietf-hybi-thewebsocketprotocol-10
    /// </summary>
    class DraftHybi10Processor : ProtocolProcessorBase
    {
        private const string m_Magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private string m_ExpectedAcceptKey;

        private static Random m_Random = new Random();

        public DraftHybi10Processor()
            : base(8, new CloseStatusCodeHybi10())
        {
        }

        protected DraftHybi10Processor(int version, ICloseStatusCode closeStatusCode)
            : base(version, closeStatusCode)
        {

        }

        public override void SendHandshake()
        {
            var secKey = Guid.NewGuid().ToString().Substring(0, 5);

#if !SILVERLIGHT
            m_ExpectedAcceptKey = Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(secKey + m_Magic)));
#else
            m_ExpectedAcceptKey = Convert.ToBase64String(SHA1.Create().ComputeHash(ASCIIEncoding.Instance.GetBytes(secKey + m_Magic)));
#endif

            var handshakeBuilder = new StringBuilder();

#if SILVERLIGHT
            handshakeBuilder.AppendFormatWithCrCf("GET {0} HTTP/1.1", WebSocket.TargetUri.GetPathAndQuery());
#else
            handshakeBuilder.AppendFormatWithCrCf("GET {0} HTTP/1.1", WebSocket.TargetUri.PathAndQuery);
#endif

            handshakeBuilder.AppendWithCrCf("Upgrade: WebSocket");
            handshakeBuilder.AppendWithCrCf("Connection: Upgrade");
            handshakeBuilder.Append("Sec-WebSocket-Version: ");
            handshakeBuilder.AppendWithCrCf(VersionTag);
            handshakeBuilder.Append("Sec-WebSocket-Key: ");
            handshakeBuilder.AppendWithCrCf(secKey);
            handshakeBuilder.Append("Host: ");
            handshakeBuilder.AppendWithCrCf(WebSocket.TargetUri.Host);
            handshakeBuilder.Append("Origin: ");
            handshakeBuilder.AppendWithCrCf(WebSocket.TargetUri.Host);

            if (!string.IsNullOrEmpty(WebSocket.SubProtocol))
            {
                handshakeBuilder.Append("Sec-WebSocket-Protocol: ");
                handshakeBuilder.AppendWithCrCf(WebSocket.SubProtocol);
            }

            var cookies = WebSocket.Cookies;

            if (cookies != null && cookies.Count > 0)
            {
                string[] cookiePairs = new string[cookies.Count];

                for (int i = 0; i < cookies.Count; i++)
                {
                    var item = cookies[i];
                    cookiePairs[i] = item.Key + "=" + Uri.EscapeUriString(item.Value);
                }

                handshakeBuilder.Append("Cookie: ");
                handshakeBuilder.AppendWithCrCf(string.Join(";", cookiePairs));
            }

            handshakeBuilder.AppendWithCrCf();

            byte[] handshakeBuffer = Encoding.UTF8.GetBytes(handshakeBuilder.ToString());

            Client.Send(handshakeBuffer, 0, handshakeBuffer.Length);
        }

        public override ReaderBase CreateHandshakeReader()
        {
            return new DraftHybi10HandshakeReader(WebSocket);
        }

        private void SendMessage(int opCode, string message)
        {
            byte[] playloadData = Encoding.UTF8.GetBytes(message);
            SendDataFragment(opCode, playloadData, 0, playloadData.Length);
        }

        private void SendDataFragment(int opCode, byte[] playloadData, int offset, int length)
        {
            byte[] headData;

            if (length < 126)
            {
                headData = new byte[6];
                headData[1] = (byte) (length | 0x80);
            }
            else if (length < 65536)
            {
                headData = new byte[8];
                headData[1] = (byte)(126 | 0x80);
                headData[2] = (byte)(length / 256);
                headData[3] = (byte)(length % 256);
            }
            else
            {
                headData = new byte[14];
                headData[1] = (byte)(127 | 0x80);

                int left = length;
                int unit = 256;

                for (int i = 9; i > 1; i--)
                {
                    headData[i] = (byte)(left % unit);
                    left = left / unit;

                    if (left == 0)
                        break;
                }
            }

            headData[0] = (byte)(opCode | 0x80);

            GenerateMask(headData, headData.Length - 4);
            MaskData(playloadData, offset, length, headData, headData.Length - 4);

            Client.Send(new ArraySegment<byte>[]
            {
                new ArraySegment<byte>(headData, 0, headData.Length),
                new ArraySegment<byte>(playloadData, offset, length)
            });
        }

        public override void SendData(byte[] data, int offset, int length)
        {
            SendDataFragment(OpCode.Binary, data, offset, length);
        }

        public override void SendMessage(string message)
        {
            SendMessage(OpCode.Text, message);
        }

        public override void SendCloseHandshake(int statusCode, string closeReason)
        {
            byte[] playloadData = new byte[(string.IsNullOrEmpty(closeReason) ? 0 : Encoding.UTF8.GetMaxByteCount(closeReason.Length)) + 2];

            int highByte = statusCode / 256;
            int lowByte = statusCode % 256;

            playloadData[0] = (byte)highByte;
            playloadData[1] = (byte)lowByte;

            if (!string.IsNullOrEmpty(closeReason))
            {
                int bytesCount = Encoding.UTF8.GetBytes(closeReason, 0, closeReason.Length, playloadData, 2);
                SendDataFragment(OpCode.Close, playloadData, 0, bytesCount + 2);
            }
            else
            {
                SendDataFragment(OpCode.Close, playloadData, 0, playloadData.Length);
            }
        }

        public override void SendPing(string ping)
        {
            SendMessage(OpCode.Ping, ping);
        }

        private NameValueCollection ParseHandshake(string handshake)
        {
            var items = new NameValueCollection();

            string line;
            string firstLine = string.Empty;
            string prevKey = string.Empty;

            var reader = new StringReader(handshake);

            while (!string.IsNullOrEmpty(line = reader.ReadLine()))
            {
                if (string.IsNullOrEmpty(firstLine))
                {
                    firstLine = line;
                    continue;
                }

                if (line.StartsWith("\t") && !string.IsNullOrEmpty(prevKey))
                {
                    string currentValue = items.GetValue(prevKey, string.Empty);
                    items[prevKey] = currentValue + line.Trim();
                    continue;
                }

                int pos = line.IndexOf(':');

                string key = line.Substring(0, pos);

                if (!string.IsNullOrEmpty(key))
                    key = key.Trim();

                string value = line.Substring(pos + 1);
                if (!string.IsNullOrEmpty(value) && value.StartsWith(" ") && value.Length > 1)
                    value = value.Substring(1);

                if (string.IsNullOrEmpty(key))
                    continue;

                items[key] = value;
                prevKey = key;
            }

            return items;
        }

        public override bool VerifyHandshake(WebSocketCommandInfo handshakeInfo)
        {
            var handshake = handshakeInfo.Text;

            if (string.IsNullOrEmpty(handshake))
                return false;

            var items = ParseHandshake(handshake);

            if (!string.IsNullOrEmpty(WebSocket.SubProtocol))
            {
                var protocol = items.GetValue("Sec-WebSocket-Protocol", string.Empty);

                if (!WebSocket.SubProtocol.Equals(protocol, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            var acceptKey = items.GetValue("Sec-WebSocket-Accept", string.Empty);

            if (!m_ExpectedAcceptKey.Equals(acceptKey, StringComparison.OrdinalIgnoreCase))
                return false;

            //more validations

            return true;
        }

        public override bool SupportBinary
        {
            get { return true; }
        }

        private void GenerateMask(byte[] mask, int offset)
        {
            for (var i = offset; i < offset + 4; i++)
            {
                mask[i] = (byte)m_Random.Next(0, 255);
            }
        }

        private void MaskData(byte[] rawData, int offset, int length, byte[] mask, int maskOffset)
        {
            for (var i = 0; i < length; i++)
            {
                var pos = offset + i;
                rawData[pos] = (byte)(rawData[pos] ^ mask[maskOffset + i % 4]);
            }
        }
    }
}
